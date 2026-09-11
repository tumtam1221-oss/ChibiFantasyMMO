<?php

declare(strict_types=1);

namespace ChibiFantasy\World;

use ChibiFantasy\Character\CharacterRepository;
use ChibiFantasy\Session\SessionRepository;
use ChibiFantasy\Session\SessionService;
use PDO;

/**
 * Hands back the characters a world server left behind when it died.
 *
 * **The bug this exists for.** A world server releases everyone in it when it stops
 * cleanly. Nothing releases anyone when it does not: a kill, a crash, an out-of-memory,
 * a power cut. The character stays `availability = InWorld` and the session stays
 * Active, so the player is refused at the door with `character_unavailable` -- and
 * because nothing in the system ever revisits that column, the refusal is permanent.
 * Expiring the session does not help; `expireLapsed()` moves the session's own state
 * and never touches the character. Restarting the server did not help either, which is
 * the part that made it look like data loss: the fix had to be applied by hand, to the
 * database, every time.
 *
 * **Why a restarting server is allowed to do this.** A process that has just started
 * has no players in it. Anything the database still believes is inside that world
 * therefore belongs to the process that is gone. This is not a guess about staleness --
 * no timeout, no heuristic, no "probably dead by now"; it is the one moment when the
 * answer is known for certain, which is exactly why it is done here and not by a
 * background sweeper.
 *
 * **Two passes, because there are two ways to be stranded.** A session that is still
 * Active holds its character legitimately as far as any other query can tell, so the
 * character can only be freed by ending the session -- the first pass. But a character
 * can also outlive its session entirely: the session lapses or is revoked while the
 * character is left at InWorld, which is precisely the state an operator produces by
 * clearing sessions by hand after a crash. Nothing owns that character and nothing
 * would ever find it, so the second pass looks from the character's side instead.
 *
 * **Neither pass can touch a live player.** The first is confined to one server and
 * channel -- the world that just restarted -- so the other channels of the same server
 * are untouched however busy they are. The second frees a character only when no usable
 * session anywhere holds it, which a player who is actually online always has.
 */
final class WorldReclaimService
{
    public function __construct(
        private readonly PDO $pdo,
        private readonly SessionRepository $sessions,
        private readonly SessionService $flow
    ) {
    }

    /**
     * Releases everything stranded in one world.
     *
     * @return array{sessions:int,characters:int}
     */
    public function reclaim(string $serverId, string $channelId): array
    {
        $released = 0;

        foreach ($this->sessions->findInWorld($serverId, $channelId) as $session) {
            $outcome = $this->flow->release($session);

            if (($outcome['ok'] ?? false) === true) {
                $released++;
            }
        }

        return [
            'sessions'   => $released,
            'characters' => $this->releaseUnheldCharacters($serverId),
        ];
    }

    /**
     * Frees characters marked InWorld that no usable session holds.
     *
     * **One statement on purpose.** Reading the candidates and then updating them would
     * leave a window in which a player signs in, takes a character the read had already
     * decided was abandoned, and has it released out from under them a moment later.
     * Evaluated inside the write, the check and the change cannot be separated.
     *
     * **Usable is decided by MySQL, not by PHP.** `expires_at` is written with the
     * database's `NOW(3)`; comparing it against PHP's clock would answer differently on
     * any machine whose timezone differs -- and this project has exactly that mismatch.
     * The same reasoning as `SessionRepository::LAPSED_EXPRESSION`, for the same reason.
     *
     * **Scoped to one server.** A character belongs to a server, not to a channel, so
     * this cannot be narrowed further -- but it does not need to be: a character held by
     * a live session on another channel fails the NOT EXISTS and is left alone.
     */
    private function releaseUnheldCharacters(string $serverId): int
    {
        $statement = $this->pdo->prepare(
            'UPDATE `character` AS c
             SET c.availability = :playable,
                 c.revision = c.revision + 1,
                 c.updated_at = NOW(3)
             WHERE c.server_id = :sid
               AND c.availability = :in_world
               AND NOT EXISTS (
                   SELECT 1
                   FROM account_session AS s
                   WHERE s.selected_character_id = c.character_id
                     AND s.state NOT IN (:expired, :revoked)
                     AND (s.expires_at IS NULL OR s.expires_at > NOW(3))
               )'
        );

        $statement->execute([
            ':playable'  => CharacterRepository::AVAILABILITY_PLAYABLE,
            ':sid'       => $serverId,
            ':in_world'  => CharacterRepository::AVAILABILITY_IN_WORLD,
            ':expired'   => SessionRepository::EXPIRED,
            ':revoked'   => SessionRepository::REVOKED,
        ]);

        return $statement->rowCount();
    }
}
