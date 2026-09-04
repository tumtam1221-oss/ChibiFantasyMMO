<?php

declare(strict_types=1);

namespace ChibiFantasy\Auth;

use PDO;

/**
 * Counts recent attempts and refuses when there have been too many.
 *
 * Backed by a table rather than by memory, which is the whole reason it exists as
 * a Phase 15 concern: Phase 14's in-memory seam forgot everything when the process
 * ended, and a login limiter that resets on every request limits nothing.
 *
 * Two independent counters, because the two attacks differ. One account hammered
 * from many addresses is caught by the identifier counter; one address spraying
 * many accounts is caught by the address counter. Either alone leaves a hole.
 *
 * This is honestly a simple fixed-window counter. It is not a token bucket, not
 * distributed, and an attacker who waits out a window gets a fresh allowance. That
 * is adequate for slowing credential stuffing to a crawl and is not a substitute
 * for a WAF. What matters architecturally is that the seam is now durable and has
 * one implementation.
 */
final class RateLimiter
{
    public function __construct(
        private readonly PDO $pdo,
        private readonly int $maxPerIdentifier = 10,
        private readonly int $maxPerAddress = 30,
        private readonly int $windowSeconds = 300
    ) {
    }

    /**
     * Records an attempt.
     *
     * The identifier is stored so the counter can key on it. The password is not,
     * in any form -- not plaintext, not hashed, not its length. A failed-login log
     * that records what was tried is a credential leak with extra steps, and near
     * misses in such a log are especially dangerous.
     */
    public function record(string $loginIdentifier, string $remoteAddress, bool $succeeded): void
    {
        $statement = $this->pdo->prepare(
            'INSERT INTO login_attempt (login_identifier, remote_address, succeeded, attempted_at)
             VALUES (:login, :address, :ok, NOW(3))'
        );

        $statement->execute([
            ':login'   => $loginIdentifier,
            ':address' => $remoteAddress,
            ':ok'      => $succeeded ? 1 : 0,
        ]);
    }

    /**
     * Whether this attempt is within the allowance.
     *
     * Counts only failures. Counting successes would lock out a legitimate player
     * who signs in from several devices, which punishes exactly the wrong person.
     */
    public function isAllowed(string $loginIdentifier, string $remoteAddress): bool
    {
        if ($this->failuresSince($loginIdentifier, null) >= $this->maxPerIdentifier) {
            return false;
        }

        if ($remoteAddress !== ''
            && $this->failuresSince(null, $remoteAddress) >= $this->maxPerAddress) {
            return false;
        }

        return true;
    }

    private function failuresSince(?string $loginIdentifier, ?string $remoteAddress): int
    {
        if ($loginIdentifier !== null) {
            $statement = $this->pdo->prepare(
                'SELECT COUNT(*) FROM login_attempt
                 WHERE login_identifier = :login
                   AND succeeded = 0
                   AND attempted_at >= (NOW(3) - INTERVAL :seconds SECOND)'
            );

            $statement->bindValue(':login', $loginIdentifier);
        } else {
            $statement = $this->pdo->prepare(
                'SELECT COUNT(*) FROM login_attempt
                 WHERE remote_address = :address
                   AND succeeded = 0
                   AND attempted_at >= (NOW(3) - INTERVAL :seconds SECOND)'
            );

            $statement->bindValue(':address', (string) $remoteAddress);
        }

        $statement->bindValue(':seconds', $this->windowSeconds, PDO::PARAM_INT);
        $statement->execute();

        return (int) $statement->fetchColumn();
    }

    /**
     * Clears the failure history for an identifier.
     *
     * Called after a successful sign-in, so a player who mistyped twice and then
     * got it right is not still carrying those failures toward a lockout.
     */
    public function clear(string $loginIdentifier): void
    {
        $statement = $this->pdo->prepare(
            'DELETE FROM login_attempt WHERE login_identifier = :login AND succeeded = 0'
        );

        $statement->execute([':login' => $loginIdentifier]);
    }

    /**
     * Removes attempts older than the window.
     *
     * Housekeeping, so the table stays small. Safe to call from a cron or at the
     * end of a request; it only ever deletes rows that can no longer affect a
     * decision.
     */
    public function prune(): int
    {
        $statement = $this->pdo->prepare(
            'DELETE FROM login_attempt WHERE attempted_at < (NOW(3) - INTERVAL :seconds SECOND)'
        );

        $statement->bindValue(':seconds', $this->windowSeconds * 4, PDO::PARAM_INT);
        $statement->execute();

        return $statement->rowCount();
    }
}
