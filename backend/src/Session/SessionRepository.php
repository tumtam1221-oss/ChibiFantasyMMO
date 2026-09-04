<?php

declare(strict_types=1);

namespace ChibiFantasy\Session;

use ChibiFantasy\Auth\Secrets;
use PDO;

/**
 * Persists account sessions and resolves bearer tokens back to them.
 *
 * The state values are Phase 14's SessionState ordinals, unchanged. There is one
 * state model shared by the domain and the database; a second, incompatible one
 * here is exactly what the brief forbids.
 *
 * Every selection update carries `revision` in its WHERE clause. That is what
 * makes a stale writer lose rather than silently overwrite: if the session moved
 * since the caller read it, zero rows match and the caller is told.
 */
final class SessionRepository
{
    public const UNAUTHENTICATED = 0;
    public const AUTHENTICATED = 1;
    public const SERVER_SELECTED = 2;
    public const CHANNEL_SELECTED = 3;
    public const CHARACTER_SELECTED = 4;
    public const ENTERING_WORLD = 5;
    public const ACTIVE = 6;
    public const EXPIRED = 7;
    public const REVOKED = 8;

    /**
     * Whether a session has run out, decided by MySQL rather than by PHP.
     *
     * This must never be computed in PHP. `expires_at` is written by MySQL's
     * NOW(3) in the database server's timezone; PHP's `time()` and `strtotime()`
     * use PHP's own, which defaults to UTC when `date.timezone` is unset. On any
     * machine where the two differ -- which is most of them -- a session would
     * appear to expire hours early or, far worse, never expire at all.
     *
     * Comparing the stored value against NOW(3) inside the query removes the
     * question entirely: one clock, one timezone, one answer.
     */
    private const LAPSED_EXPRESSION = '(expires_at IS NOT NULL AND expires_at <= NOW(3))';

    public function __construct(private readonly PDO $pdo)
    {
    }

    /**
     * Issues a session and its bearer token.
     *
     * Returns the plaintext token to the caller exactly once; only its SHA-256 is
     * stored. If the table later leaks, an attacker holds hashes of tokens that
     * have long since expired rather than a set of live credentials.
     *
     * Both rows are written in one transaction: a session with no token could
     * never be used, and a token row with no session violates its foreign key.
     *
     * @return array{session_id:string,token:string,expires_at:?string}
     */
    public function issue(
        string $accountId,
        string $clientVersion,
        string $protocolVersion,
        string $contentVersion,
        int $lifetimeSeconds,
        int $idBytes = 32,
        int $tokenBytes = 32
    ): array {
        $sessionId = Secrets::randomToken($idBytes);
        $token = Secrets::randomToken($tokenBytes);

        $this->pdo->beginTransaction();

        try {
            $session = $this->pdo->prepare(
                'INSERT INTO account_session
                   (session_id, account_id, state, client_version, protocol_version,
                    content_version, issued_at, expires_at, revision)
                 VALUES
                   (:sid, :aid, :state, :cv, :pv, :ctv, NOW(3),
                    ' . ($lifetimeSeconds > 0 ? 'NOW(3) + INTERVAL :life SECOND' : 'NULL') . ', 0)'
            );

            $session->bindValue(':sid', $sessionId);
            $session->bindValue(':aid', $accountId);
            $session->bindValue(':state', self::AUTHENTICATED, PDO::PARAM_INT);
            $session->bindValue(':cv', $clientVersion);
            $session->bindValue(':pv', $protocolVersion);
            $session->bindValue(':ctv', $contentVersion);

            if ($lifetimeSeconds > 0) {
                $session->bindValue(':life', $lifetimeSeconds, PDO::PARAM_INT);
            }

            $session->execute();

            $tokenRow = $this->pdo->prepare(
                'INSERT INTO account_session_token (session_id, token_hash, issued_at)
                 VALUES (:sid, :hash, NOW(3))'
            );

            $tokenRow->execute([
                ':sid'  => $sessionId,
                ':hash' => Secrets::hashToken($token),
            ]);

            $this->pdo->commit();
        } catch (\Throwable $e) {
            if ($this->pdo->inTransaction()) {
                $this->pdo->rollBack();
            }

            throw $e;
        }

        $stored = $this->findById($sessionId);

        return [
            'session_id' => $sessionId,
            'token'      => $token,
            'expires_at' => $stored['expires_at'] ?? null,
        ];
    }

    /**
     * Resolves a bearer token to its session.
     *
     * The token is hashed and the hash is looked up, so the plaintext is never
     * compared against anything stored and a leaked table yields nothing usable.
     * The lookup is a unique-index hit rather than a scan-and-compare, which also
     * means no per-row timing to measure.
     *
     * @return array<string,mixed>|null
     */
    public function findByToken(string $token): ?array
    {
        $statement = $this->pdo->prepare(
            'SELECT s.*, ' . self::LAPSED_EXPRESSION . ' AS is_lapsed
             FROM account_session s
             INNER JOIN account_session_token t ON t.session_id = s.session_id
             WHERE t.token_hash = :hash'
        );

        $statement->execute([':hash' => Secrets::hashToken($token)]);

        $row = $statement->fetch();

        return $row === false ? null : $this->hydrate($row);
    }

    /** @return array<string,mixed>|null */
    public function findById(string $sessionId): ?array
    {
        $statement = $this->pdo->prepare(
            'SELECT *, ' . self::LAPSED_EXPRESSION . ' AS is_lapsed
             FROM account_session WHERE session_id = :sid'
        );

        $statement->execute([':sid' => $sessionId]);

        $row = $statement->fetch();

        return $row === false ? null : $this->hydrate($row);
    }

    /**
     * Whether an account already holds a session that could still be used.
     *
     * Terminal and lapsed sessions do not count: a player whose session expired
     * must be able to sign in again.
     */
    public function hasLiveSession(string $accountId): bool
    {
        $statement = $this->pdo->prepare(
            'SELECT COUNT(*) FROM account_session
             WHERE account_id = :aid
               AND state NOT IN (:expired, :revoked)
               AND (expires_at IS NULL OR expires_at > NOW(3))'
        );

        $statement->execute([
            ':aid'     => $accountId,
            ':expired' => self::EXPIRED,
            ':revoked' => self::REVOKED,
        ]);

        return ((int) $statement->fetchColumn()) > 0;
    }

    /**
     * Moves a session to a new state and records a selection, guarded by revision.
     *
     * One statement, so the state, the selection and the revision advance together
     * or not at all -- there is no window in which a session has a server selected
     * but is still in the Authenticated state.
     *
     * `$clearBelow` implements the Phase 14 rule that choosing a server discards
     * the channel and character beneath it: a channel of another server and a
     * character on another server are both nonsense, and leaving them is exactly
     * the mismatch enter-world exists to catch.
     *
     * @param array<string,string|null> $selections column => value
     */
    public function applyTransition(
        string $sessionId,
        int $expectedRevision,
        int $newState,
        array $selections = [],
        array $clearBelow = []
    ): bool {
        $sets = ['state = :state', 'revision = revision + 1'];
        $params = [
            ':sid'      => $sessionId,
            ':revision' => $expectedRevision,
            ':state'    => $newState,
        ];

        foreach ($selections as $column => $value) {
            $placeholder = ':sel_' . $column;
            $sets[] = "{$column} = {$placeholder}";
            $params[$placeholder] = $value;
        }

        foreach ($clearBelow as $column) {
            $sets[] = "{$column} = NULL";
        }

        $statement = $this->pdo->prepare(
            'UPDATE account_session SET ' . implode(', ', $sets)
            . ' WHERE session_id = :sid AND revision = :revision'
            . ' AND state NOT IN (' . self::EXPIRED . ', ' . self::REVOKED . ')'
        );

        $statement->execute($params);

        return $statement->rowCount() === 1;
    }

    /** Ends a session. The authority's to call, never a client's. */
    public function revoke(string $sessionId): bool
    {
        $statement = $this->pdo->prepare(
            'UPDATE account_session
             SET state = :revoked, revoked_at = NOW(3), revision = revision + 1
             WHERE session_id = :sid AND state NOT IN (:expired, :already)'
        );

        $statement->execute([
            ':revoked' => self::REVOKED,
            ':sid'     => $sessionId,
            ':expired' => self::EXPIRED,
            ':already' => self::REVOKED,
        ]);

        return $statement->rowCount() === 1;
    }

    /**
     * Marks lapsed sessions expired.
     *
     * Expiry is evaluated on read as well, so a session that lapsed while nobody
     * was looking is already unusable. This is housekeeping that makes the state
     * column agree with the clock, not the thing that enforces expiry.
     */
    public function expireLapsed(): int
    {
        $statement = $this->pdo->prepare(
            'UPDATE account_session
             SET state = :expired, revision = revision + 1
             WHERE expires_at IS NOT NULL
               AND expires_at <= NOW(3)
               AND state NOT IN (:already, :revoked)'
        );

        $statement->execute([
            ':expired' => self::EXPIRED,
            ':already' => self::EXPIRED,
            ':revoked' => self::REVOKED,
        ]);

        return $statement->rowCount();
    }

    /**
     * Normalises a row and answers the two questions every caller asks.
     *
     * `is_usable` is computed here rather than trusted from `state`, because a
     * session can lapse without anything having updated its column yet.
     *
     * @param array<string,mixed> $row
     * @return array<string,mixed>
     */
    private function hydrate(array $row): array
    {
        $state = (int) $row['state'];
        $expiresAt = $row['expires_at'] === null ? null : (string) $row['expires_at'];

        // Taken from the query, never recomputed here. See LAPSED_EXPRESSION.
        $lapsed = (bool) ($row['is_lapsed'] ?? false);
        $terminal = $state === self::EXPIRED || $state === self::REVOKED;

        return [
            'session_id'            => (string) $row['session_id'],
            'account_id'            => (string) $row['account_id'],
            'state'                 => $state,
            'client_version'        => (string) $row['client_version'],
            'protocol_version'      => (string) $row['protocol_version'],
            'content_version'       => (string) $row['content_version'],
            'selected_server_id'    => $row['selected_server_id'] === null ? null : (string) $row['selected_server_id'],
            'selected_channel_id'   => $row['selected_channel_id'] === null ? null : (string) $row['selected_channel_id'],
            'selected_character_id' => $row['selected_character_id'] === null ? null : (string) $row['selected_character_id'],
            'issued_at'             => (string) $row['issued_at'],
            'expires_at'            => $expiresAt,
            'revision'              => (int) $row['revision'],
            'is_expired'            => $lapsed || $state === self::EXPIRED,
            'is_revoked'            => $state === self::REVOKED,
            'is_usable'             => !$terminal && !$lapsed,
        ];
    }
}
