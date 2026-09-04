<?php

declare(strict_types=1);

namespace ChibiFantasy\Auth;

/**
 * Every cryptographic operation in this backend, in one place.
 *
 * Centralised so there is exactly one answer to "how is a password stored" and
 * "where does a token come from", and so an audit reads one short file rather
 * than grepping for `md5`.
 *
 * Nothing here invents cryptography. Passwords go through PHP's `password_hash`,
 * which today means bcrypt and tomorrow means whatever PHP's default becomes --
 * that indirection is the point, and `needsRehash` is what carries existing users
 * across the change. Randomness comes from `random_bytes`, which is the CSPRNG and
 * throws rather than degrading if the system cannot provide entropy.
 *
 * There is no salt parameter, no cost constant copied from a blog post, no
 * pepper, and no key. A secret this repository does not hold is a secret it cannot
 * leak.
 */
final class Secrets
{
    /**
     * Hashes a password for storage.
     *
     * PASSWORD_DEFAULT rather than a pinned algorithm: PHP updates it when the
     * current default stops being adequate, and `needsRehash` upgrades existing
     * hashes at the next successful login. Pinning bcrypt here would freeze this
     * codebase at 2026's judgement forever.
     */
    public static function hashPassword(string $plaintext): string
    {
        $hash = password_hash($plaintext, PASSWORD_DEFAULT);

        // password_hash returns a string in PHP 8, but a failed hash is a security
        // event and must never be stored as something truthy.
        if ($hash === '' ) {
            throw new \RuntimeException('Password hashing failed.');
        }

        return $hash;
    }

    /**
     * Verifies a password against a stored hash.
     *
     * `password_verify` compares in constant time, which is why the comparison is
     * not written here as `===`. A timing difference on a hash comparison is a
     * slow but real credential oracle.
     */
    public static function verifyPassword(string $plaintext, string $hash): bool
    {
        return password_verify($plaintext, $hash);
    }

    /** Whether a stored hash was made with outdated parameters and should be replaced. */
    public static function passwordNeedsRehash(string $hash): bool
    {
        return password_needs_rehash($hash, PASSWORD_DEFAULT);
    }

    /**
     * A new opaque identifier or token.
     *
     * Hex rather than base64 so the value is URL-safe, header-safe and
     * case-insensitively transportable without any escaping anywhere.
     *
     * The bytes come from `random_bytes`. Not `rand`, not `mt_rand`, not
     * `uniqid`, and emphatically not anything derived from a timestamp, an account
     * id or a username -- a session identifier that can be predicted from things
     * an attacker already knows is not an identifier, it is a formality.
     */
    public static function randomToken(int $bytes = 32): string
    {
        if ($bytes < 16) {
            throw new \InvalidArgumentException('Refusing to mint a token below 128 bits.');
        }

        return bin2hex(random_bytes($bytes));
    }

    /**
     * The stored form of a bearer token.
     *
     * SHA-256, deliberately, and deliberately not `password_hash`. A token already
     * carries 256 bits of CSPRNG entropy, so there is nothing to brute-force and a
     * slow KDF would only add latency to every authenticated request. What matters
     * is that the plaintext is never stored, so a leaked table hands an attacker
     * nothing usable.
     */
    public static function hashToken(string $token): string
    {
        return hash('sha256', $token);
    }

    /**
     * Compares two hashes without leaking where they first differ.
     *
     * Used for token lookup comparisons. `hash_equals` is constant-time; `===` on
     * strings is not, and a remote attacker can measure the difference.
     */
    public static function hashesMatch(string $a, string $b): bool
    {
        return hash_equals($a, $b);
    }
}
