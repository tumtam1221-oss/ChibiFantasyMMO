<?php

declare(strict_types=1);

namespace ChibiFantasy\Auth;

/**
 * Why an authentication attempt did not succeed.
 *
 * Mirrors Phase 14's LoginRejection so the client handles one vocabulary from the
 * domain and from the wire.
 */
enum AuthFailure: string
{
    case InvalidCredentials = 'invalid_credentials';
    case AccountDisabled = 'account_disabled';
    case AccountBanned = 'account_banned';
    case AccountSuspended = 'account_suspended';
    case RateLimited = 'rate_limited';
}

/** What an authentication attempt concluded. */
final class AuthResult
{
    private function __construct(
        public readonly bool $succeeded,
        public readonly ?AuthFailure $failure,
        public readonly string $accountId,
        public readonly int $status
    ) {
    }

    public static function success(string $accountId, int $status): self
    {
        return new self(true, null, $accountId, $status);
    }

    public static function failed(AuthFailure $failure): self
    {
        return new self(false, $failure, '', 0);
    }
}

/**
 * Verifies a credential. The only place a password is ever examined.
 *
 * Everything above this class receives an outcome, never a secret -- which is the
 * boundary Phase 14 drew when it gave `LoginRequest` nowhere to put a password and
 * `AuthenticatedAccount` nowhere to put a hash. This is the other side of that
 * line, and it is deliberately small enough to audit in one reading.
 *
 * Two properties are load-bearing:
 *
 * **Unknown account and wrong password are indistinguishable.** Both return
 * `InvalidCredentials`, and a dummy verification runs when no account was found so
 * the two paths take comparable time. Without that, response timing enumerates
 * accounts even when the messages match.
 *
 * **A disabled or banned account still verifies its password first.** Answering
 * "banned" to anyone who guesses an identifier would confirm the account exists
 * and reveal its state to somebody who cannot sign in anyway.
 */
final class Authenticator
{
    public function __construct(
        private readonly AccountRepository $accounts,
        private readonly RateLimiter $rateLimiter
    ) {
    }

    public function attempt(
        string $loginIdentifier,
        string $password,
        string $remoteAddress = ''
    ): AuthResult {
        if (!$this->rateLimiter->isAllowed($loginIdentifier, $remoteAddress)) {
            // Not recorded: a refused attempt never reached verification, and
            // counting it would let an attacker extend their own lockout forever.
            return AuthResult::failed(AuthFailure::RateLimited);
        }

        $credential = $this->accounts->findCredential($loginIdentifier);

        if ($credential === null) {
            // Verify against a throwaway hash so an unknown identifier costs
            // roughly the same time as a known one. Skipping this returns in
            // microseconds and turns the endpoint into an account enumerator.
            Secrets::verifyPassword($password, self::DUMMY_HASH);

            $this->rateLimiter->record($loginIdentifier, $remoteAddress, false);

            return AuthResult::failed(AuthFailure::InvalidCredentials);
        }

        if (!Secrets::verifyPassword($password, $credential['password_hash'])) {
            $this->rateLimiter->record($loginIdentifier, $remoteAddress, false);

            return AuthResult::failed(AuthFailure::InvalidCredentials);
        }

        // The password was right. Only now does the account's state matter, and
        // only now is it safe to say something about it.
        $failure = match ($credential['status']) {
            AccountRepository::STATUS_ACTIVE    => null,
            AccountRepository::STATUS_DISABLED  => AuthFailure::AccountDisabled,
            AccountRepository::STATUS_BANNED    => AuthFailure::AccountBanned,
            AccountRepository::STATUS_SUSPENDED => AuthFailure::AccountSuspended,
            default                             => AuthFailure::InvalidCredentials,
        };

        if ($failure !== null) {
            $this->rateLimiter->record($loginIdentifier, $remoteAddress, false);

            return AuthResult::failed($failure);
        }

        // Carry an existing user onto a stronger algorithm now that their password
        // is in hand and verified. This is the only moment it can be done.
        if (Secrets::passwordNeedsRehash($credential['password_hash'])) {
            $this->accounts->upgradeHash($credential['account_id'], $password);
        }

        $this->rateLimiter->record($loginIdentifier, $remoteAddress, true);
        $this->rateLimiter->clear($loginIdentifier);

        return AuthResult::success($credential['account_id'], $credential['status']);
    }

    /**
     * A real bcrypt hash of a value nobody knows, used only to burn time.
     *
     * Constant rather than generated per call so the cost is the stored cost and
     * does not vary. It is not a secret: it protects nothing, it only makes the
     * unknown-account path take about as long as the known-account path.
     */
    private const DUMMY_HASH = '$2y$12$usesomesillystringforeseeing.HDxCn6h/JXVQ8IyGvHYbFHHKC';
}
