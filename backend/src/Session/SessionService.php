<?php

declare(strict_types=1);

namespace ChibiFantasy\Session;

use ChibiFantasy\Character\CharacterRepository;
use ChibiFantasy\Directory\DirectoryRepository;
use ChibiFantasy\Http\ApiProblem;
use PDO;

/**
 * The login-to-world flow, server-side and authoritative.
 *
 * This is the same state machine Phase 14 built in Gameplay, implemented against
 * the database. The client's copy exists so a screen can grey out a button without
 * a round trip; this one is what decides, because a client that concluded it was
 * allowed would simply be believed otherwise.
 *
 * Three rules govern every method:
 *
 * **The account is never taken from the request.** It is resolved from the bearer
 * token, server-side. A client that edits an account id in a payload changes
 * nothing except making its request disagree with its session.
 *
 * **Every stage re-reads the world.** A server that closed, a channel that filled
 * or a character that got locked between selection and entry is caught, because
 * the checks run again rather than trusting what was true earlier.
 *
 * **Nothing is written until everything passes.** Each transition is a single
 * UPDATE guarded by the session revision, so a refusal leaves the row untouched
 * and two concurrent writers cannot both win.
 */
final class SessionService
{
    public function __construct(
        private readonly PDO $pdo,
        private readonly SessionRepository $sessions,
        private readonly DirectoryRepository $directory,
        private readonly CharacterRepository $characters
    ) {
    }

    /**
     * Ends a session and puts its character back.
     *
     * **Why this endpoint has to exist.** Phase 15 refuses a second live session, on
     * the deliberate principle that taking somebody's session away is a policy
     * decision rather than a side effect of signing in again. Without a way to give
     * one up, that principle locks a player out of their own account for the whole
     * session lifetime the moment they close the game -- and it made the very first
     * live integration run fail, correctly, on the second login.
     *
     * **Releasing is not the same as expiring.** An expired session ran out of time;
     * a released one was handed back. Both end up unusable, and a player is owed
     * different words for them, so the state recorded is Revoked and the reason is
     * the caller's own request.
     *
     * **The character comes back too.** A session that reached the world left its
     * character marked InWorld. If ending the session did not undo that, the
     * character would be permanently unplayable -- the exact ownership corruption
     * disconnect handling exists to prevent. The two changes happen in one
     * transaction, so a character is never released without its session ending or
     * the other way round.
     *
     * **Idempotent by construction.** Releasing an already-released session is not
     * an error: a client that retries after a lost reply, and a disconnect callback
     * that fires twice, must both be harmless. The second call finds nothing to do
     * and says so.
     *
     * @param array<string,mixed> $session
     * @return array{ok:true,result:array<string,mixed>}
     */
    public function release(array $session): array
    {
        $sessionId = (string) $session['session_id'];
        $characterId = (string) ($session['selected_character_id'] ?? '');
        $wasInWorld = (int) $session['state'] === SessionRepository::ENTERING_WORLD
            || (int) $session['state'] === SessionRepository::ACTIVE;

        $this->pdo->beginTransaction();

        try {
            $releasedCharacter = false;

            // The character is locked before it is read, so a world entry racing this
            // release cannot slip between the check and the write.
            if ($wasInWorld && $characterId !== '') {
                $locked = $this->pdo->prepare(
                    'SELECT character_id, availability, revision
                     FROM `character`
                     WHERE character_id = :cid AND account_id = :aid
                     FOR UPDATE'
                );

                $locked->execute([':cid' => $characterId, ':aid' => $session['account_id']]);

                $character = $locked->fetch();

                if ($character !== false
                    && (int) $character['availability'] === CharacterRepository::AVAILABILITY_IN_WORLD) {
                    $releasedCharacter = $this->characters->updateAvailability(
                        $characterId,
                        CharacterRepository::AVAILABILITY_PLAYABLE,
                        (int) $character['revision']
                    );
                }
            }

            $alreadyEnded = (int) $session['state'] === SessionRepository::REVOKED
                || (int) $session['state'] === SessionRepository::EXPIRED;

            $ended = $alreadyEnded ? false : $this->sessions->revoke($sessionId);

            $this->pdo->commit();

            return ['ok' => true, 'result' => [
                'session_id'        => $sessionId,
                'state'             => SessionRepository::REVOKED,
                'session_ended'     => $ended,
                'character_id'      => $characterId,
                'character_released' => $releasedCharacter,
            ]];
        } catch (\Throwable $e) {
            if ($this->pdo->inTransaction()) {
                $this->pdo->rollBack();
            }

            throw $e;
        }
    }

    /**
     * Resolves a bearer token to a usable session.
     *
     * Returns an ApiProblem rather than a boolean so every endpoint reports the
     * same reason for the same condition. Expiry and revocation are distinct
     * because a player is owed different words for them.
     *
     * @return array{session:array<string,mixed>}|array{problem:ApiProblem}
     */
    public function authorize(?string $token): array
    {
        if ($token === null || $token === '') {
            return ['problem' => ApiProblem::unauthenticated(
                'missing_token',
                'error.session.missing_token'
            )];
        }

        $session = $this->sessions->findByToken($token);

        if ($session === null) {
            return ['problem' => ApiProblem::unauthenticated(
                'session_invalid',
                'error.session.invalid'
            )];
        }

        if ($session['is_revoked']) {
            return ['problem' => ApiProblem::unauthenticated(
                'session_revoked',
                'error.session.revoked'
            )];
        }

        if ($session['is_expired']) {
            return ['problem' => ApiProblem::unauthenticated(
                'session_expired',
                'error.session.expired'
            )];
        }

        return ['session' => $session];
    }

    /**
     * Selects a server.
     *
     * Choosing a server clears any channel and character beneath it, because a
     * channel of another server and a character on another server are both
     * nonsense. Leaving them would be exactly the mismatch enter-world exists to
     * catch, discovered one step too late.
     *
     * @param array<string,mixed> $session
     * @return array{ok:true,session:array<string,mixed>}|array{ok:false,problem:ApiProblem}
     */
    public function selectServer(array $session, string $serverId): array
    {
        if (!$this->canReach($session, SessionRepository::SERVER_SELECTED)) {
            return $this->refuse('invalid_transition', 'error.session.invalid_transition');
        }

        $server = $this->directory->findServer($serverId);

        if ($server === null || $server['status'] === DirectoryRepository::SERVER_HIDDEN) {
            // Hidden and absent give the same answer, so asking for a hidden
            // server directly reveals nothing that the list withheld.
            return $this->refuse('unknown_server', 'error.server.unknown', 404);
        }

        $problem = $this->checkServer($server, $session);

        if ($problem !== null) {
            return ['ok' => false, 'problem' => $problem];
        }

        $applied = $this->sessions->applyTransition(
            $session['session_id'],
            $session['revision'],
            SessionRepository::SERVER_SELECTED,
            ['selected_server_id' => $serverId],
            ['selected_channel_id', 'selected_character_id']
        );

        if (!$applied) {
            return $this->refuse('stale_revision', 'error.session.stale', 409);
        }

        return ['ok' => true, 'session' => $this->sessions->findById($session['session_id'])];
    }

    /**
     * Selects a channel of the already-chosen server.
     *
     * The channel must name the selected server. That check is why a bare channel
     * number is never an identity: without it, selecting server A and channel 1 of
     * server B would be indistinguishable from a legitimate choice.
     *
     * @param array<string,mixed> $session
     * @return array{ok:true,session:array<string,mixed>}|array{ok:false,problem:ApiProblem}
     */
    public function selectChannel(array $session, string $channelId): array
    {
        if (!$this->canReach($session, SessionRepository::CHANNEL_SELECTED)) {
            return $this->refuse('invalid_transition', 'error.session.invalid_transition');
        }

        $channel = $this->directory->findChannel($channelId);

        if ($channel === null) {
            return $this->refuse('unknown_channel', 'error.channel.unknown', 404);
        }

        if ($channel['server_id'] !== $session['selected_server_id']) {
            return $this->refuse('channel_server_mismatch', 'error.channel.server_mismatch', 409);
        }

        $problem = $this->checkChannel($channel);

        if ($problem !== null) {
            return ['ok' => false, 'problem' => $problem];
        }

        $applied = $this->sessions->applyTransition(
            $session['session_id'],
            $session['revision'],
            SessionRepository::CHANNEL_SELECTED,
            ['selected_channel_id' => $channelId],
            ['selected_character_id']
        );

        if (!$applied) {
            return $this->refuse('stale_revision', 'error.session.stale', 409);
        }

        return ['ok' => true, 'session' => $this->sessions->findById($session['session_id'])];
    }

    /**
     * Selects a character.
     *
     * Ownership is asked of the database, not read from a list the client was sent
     * earlier. A character belonging to another account gets the same answer as
     * one that does not exist, so the refusal does not confirm that somebody
     * else's character is real.
     *
     * @param array<string,mixed> $session
     * @return array{ok:true,session:array<string,mixed>}|array{ok:false,problem:ApiProblem}
     */
    public function selectCharacter(array $session, string $characterId): array
    {
        if (!$this->canReach($session, SessionRepository::CHARACTER_SELECTED)) {
            return $this->refuse('invalid_transition', 'error.session.invalid_transition');
        }

        $character = $this->characters->findOwned($session['account_id'], $characterId);

        if ($character === null) {
            return $this->refuse('character_not_found', 'error.character.not_found', 404);
        }

        if (!$character['is_playable']) {
            return $this->refuse('character_unavailable', 'error.character.unavailable', 409);
        }

        $applied = $this->sessions->applyTransition(
            $session['session_id'],
            $session['revision'],
            SessionRepository::CHARACTER_SELECTED,
            ['selected_character_id' => $characterId]
        );

        if (!$applied) {
            return $this->refuse('stale_revision', 'error.session.stale', 409);
        }

        return ['ok' => true, 'session' => $this->sessions->findById($session['session_id'])];
    }

    /**
     * Authorises the handoff to the game world.
     *
     * Everything is re-checked and everything the client claimed is compared
     * against what the session holds. A mismatch on any of them is a refusal
     * naming that field: editing one has not changed where the player is going, it
     * has produced a request that no longer describes their session.
     *
     * The whole thing runs in one database transaction and takes a row lock on the
     * character, so two simultaneous attempts cannot both claim it. On success the
     * character is marked in-world and the session reaches EnteringWorld.
     *
     * Nothing connects. Establishing the connection is Phase 16's; pretending to
     * do it here would be the fake handoff the brief forbids.
     *
     * @param array<string,mixed> $session
     * @param array<string,string> $claims account_id, character_id, server_id, channel_id
     * @return array{ok:true,result:array<string,mixed>}|array{ok:false,problem:ApiProblem}
     */
    public function enterWorld(array $session, array $claims, array $versions): array
    {
        // Every identity the client restated, against the session it actually has.
        if (($claims['account_id'] ?? '') !== $session['account_id']) {
            return $this->refuse('session_invalid', 'error.session.invalid', 401);
        }

        if ($session['state'] === SessionRepository::ENTERING_WORLD
            || $session['state'] === SessionRepository::ACTIVE) {
            return $this->refuse('already_in_world', 'error.session.already_in_world', 409);
        }

        if ($session['state'] !== SessionRepository::CHARACTER_SELECTED) {
            return $this->refuse('invalid_transition', 'error.session.invalid_transition');
        }

        if (($claims['server_id'] ?? '') !== (string) $session['selected_server_id']) {
            return $this->refuse('server_mismatch', 'error.server.mismatch', 409);
        }

        if (($claims['channel_id'] ?? '') !== (string) $session['selected_channel_id']) {
            return $this->refuse('channel_mismatch', 'error.channel.mismatch', 409);
        }

        if (($claims['character_id'] ?? '') !== (string) $session['selected_character_id']) {
            return $this->refuse('character_not_owned', 'error.character.not_owned', 403);
        }

        $server = $this->directory->findServer((string) $session['selected_server_id']);

        if ($server === null) {
            return $this->refuse('unknown_server', 'error.server.unknown', 404);
        }

        $problem = $this->checkServer($server, $session, $versions);

        if ($problem !== null) {
            return ['ok' => false, 'problem' => $problem];
        }

        $channel = $this->directory->findChannel((string) $session['selected_channel_id']);

        if ($channel === null || $channel['server_id'] !== $server['server_id']) {
            return $this->refuse('channel_mismatch', 'error.channel.mismatch', 409);
        }

        $problem = $this->checkChannel($channel);

        if ($problem !== null) {
            return ['ok' => false, 'problem' => $problem];
        }

        // From here the character must not change under us, so the work happens in
        // a transaction with the character row locked.
        $this->pdo->beginTransaction();

        try {
            $locked = $this->pdo->prepare(
                'SELECT character_id, account_id, availability, revision, map_definition_id
                 FROM `character`
                 WHERE character_id = :cid AND account_id = :aid
                 FOR UPDATE'
            );

            $locked->execute([
                ':cid' => (string) $session['selected_character_id'],
                ':aid' => $session['account_id'],
            ]);

            $character = $locked->fetch();

            if ($character === false) {
                $this->pdo->rollBack();

                return $this->refuse('character_not_owned', 'error.character.not_owned', 403);
            }

            if ((int) $character['availability'] !== CharacterRepository::AVAILABILITY_PLAYABLE) {
                $this->pdo->rollBack();

                return $this->refuse('character_unavailable', 'error.character.unavailable', 409);
            }

            $claimed = $this->characters->updateAvailability(
                (string) $character['character_id'],
                CharacterRepository::AVAILABILITY_IN_WORLD,
                (int) $character['revision']
            );

            if (!$claimed) {
                $this->pdo->rollBack();

                return $this->refuse('character_unavailable', 'error.character.unavailable', 409);
            }

            $moved = $this->sessions->applyTransition(
                $session['session_id'],
                $session['revision'],
                SessionRepository::ENTERING_WORLD
            );

            if (!$moved) {
                $this->pdo->rollBack();

                return $this->refuse('stale_revision', 'error.session.stale', 409);
            }

            $this->characters->markPlayed((string) $character['character_id']);

            $this->pdo->commit();

            $fresh = $this->sessions->findById($session['session_id']);

            return ['ok' => true, 'result' => [
                'session_id'         => $session['session_id'],
                'character_id'       => (string) $character['character_id'],
                'server_id'          => $server['server_id'],
                'channel_id'         => $channel['channel_id'],
                'map_id'             => (string) $character['map_definition_id'],
                // Authorised, not connected. Phase 16 moves this on.
                'world_entry_state'  => 1,
                'character_revision' => (int) $character['revision'] + 1,
                'session_revision'   => $fresh['revision'],
            ]];
        } catch (\Throwable $e) {
            if ($this->pdo->inTransaction()) {
                $this->pdo->rollBack();
            }

            throw $e;
        }
    }

    /**
     * The Phase 14 transition table, server-side.
     *
     * Stated once here, as it is stated once there. Stepping back to re-pick an
     * earlier choice is allowed; skipping forward is not.
     *
     * @param array<string,mixed> $session
     */
    private function canReach(array $session, int $target): bool
    {
        $from = (int) $session['state'];

        if ($from === SessionRepository::EXPIRED || $from === SessionRepository::REVOKED) {
            return false;
        }

        return match ($target) {
            SessionRepository::SERVER_SELECTED => in_array($from, [
                SessionRepository::AUTHENTICATED,
                SessionRepository::SERVER_SELECTED,
                SessionRepository::CHANNEL_SELECTED,
                SessionRepository::CHARACTER_SELECTED,
            ], true),

            SessionRepository::CHANNEL_SELECTED => in_array($from, [
                SessionRepository::SERVER_SELECTED,
                SessionRepository::CHANNEL_SELECTED,
                SessionRepository::CHARACTER_SELECTED,
            ], true),

            SessionRepository::CHARACTER_SELECTED => in_array($from, [
                SessionRepository::CHANNEL_SELECTED,
                SessionRepository::CHARACTER_SELECTED,
            ], true),

            default => false,
        };
    }

    /**
     * @param array<string,mixed> $server
     * @param array<string,mixed> $session
     * @param array<string,string>|null $versions
     */
    private function checkServer(array $server, array $session, ?array $versions = null): ?ApiProblem
    {
        if (!$server['enabled']) {
            return ApiProblem::conflict('server_unavailable', 'error.server.unavailable');
        }

        if ($server['status'] === DirectoryRepository::SERVER_MAINTENANCE) {
            return ApiProblem::unavailable('server_maintenance', 'error.server.maintenance');
        }

        if ($server['status'] !== DirectoryRepository::SERVER_ONLINE
            && $server['status'] !== DirectoryRepository::SERVER_BUSY) {
            return ApiProblem::conflict('server_unavailable', 'error.server.unavailable');
        }

        // Capacity is only a barrier when a figure was actually reported. An
        // unknown population must not read as full.
        if ($server['is_full']) {
            return ApiProblem::conflict('server_full', 'error.server.full');
        }

        $client = $versions['client'] ?? $session['client_version'];
        $protocol = $versions['protocol'] ?? $session['protocol_version'];
        $content = $versions['content'] ?? $session['content_version'];

        $evaluation = VersionPolicy::evaluate($client, $protocol, $content, $server['versions']);

        if (!VersionPolicy::isPlayable($evaluation)) {
            return ApiProblem::conflict('version_mismatch', 'error.version.mismatch');
        }

        return null;
    }

    /** @param array<string,mixed> $channel */
    private function checkChannel(array $channel): ?ApiProblem
    {
        if (!$channel['enabled']) {
            return ApiProblem::conflict('channel_unavailable', 'error.channel.unavailable');
        }

        if ($channel['status'] === DirectoryRepository::CHANNEL_MAINTENANCE) {
            return ApiProblem::unavailable('channel_maintenance', 'error.channel.maintenance');
        }

        if ($channel['status'] !== DirectoryRepository::CHANNEL_ONLINE
            && $channel['status'] !== DirectoryRepository::CHANNEL_BUSY) {
            return ApiProblem::conflict('channel_unavailable', 'error.channel.unavailable');
        }

        if ($channel['is_full']) {
            return ApiProblem::conflict('channel_full', 'error.channel.full');
        }

        return null;
    }

    /** @return array{ok:false,problem:ApiProblem} */
    private function refuse(string $code, string $messageKey, int $status = 409): array
    {
        $problem = match ($status) {
            401     => ApiProblem::unauthenticated($code, $messageKey),
            403     => ApiProblem::forbidden($code, $messageKey),
            404     => ApiProblem::notFound($code, $messageKey),
            default => ApiProblem::conflict($code, $messageKey),
        };

        return ['ok' => false, 'problem' => $problem];
    }
}
