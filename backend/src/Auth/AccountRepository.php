<?php

declare(strict_types=1);

namespace ChibiFantasy\Auth;

use PDO;

/**
 * Reads and writes accounts and their credentials.
 *
 * Every statement below is prepared with bound parameters. No value is ever
 * concatenated into SQL, which is what makes injection structurally impossible
 * here rather than a matter of remembering to escape -- and the connection sets
 * `ATTR_EMULATE_PREPARES => false`, so the placeholders really are sent to the
 * server as parameters.
 *
 * `findCredential` returns the hash to exactly one caller, the authenticator, and
 * that caller never puts it in a response, a log or an exception. The separate
 * `account_credential` table is what makes that easy to keep true: a query against
 * `account` cannot return a hash by accident.
 */
final class AccountRepository
{
    /** Mirrors Phase 14 AccountStatus by ordinal. */
    public const STATUS_UNKNOWN = 0;
    public const STATUS_ACTIVE = 1;
    public const STATUS_DISABLED = 2;
    public const STATUS_BANNED = 3;
    public const STATUS_SUSPENDED = 4;

    public function __construct(private readonly PDO $pdo)
    {
    }

    /**
     * The credential row for a login identifier, or null.
     *
     * Returns null both for "no such account" and for a malformed identifier. The
     * caller must produce the same refusal either way -- distinguishing them tells
     * an attacker which accounts exist.
     *
     * @return array{account_id:string,password_hash:string,status:int}|null
     */
    public function findCredential(string $loginIdentifier): ?array
    {
        $statement = $this->pdo->prepare(
            'SELECT c.account_id, c.password_hash, a.status
             FROM account_credential c
             INNER JOIN account a ON a.account_id = c.account_id
             WHERE c.login_identifier = :login'
        );

        $statement->execute([':login' => $loginIdentifier]);

        $row = $statement->fetch();

        if ($row === false) {
            return null;
        }

        return [
            'account_id'    => (string) $row['account_id'],
            'password_hash' => (string) $row['password_hash'],
            'status'        => (int) $row['status'],
        ];
    }

    /** @return array{account_id:string,display_name:string,status:int,revision:int}|null */
    public function findById(string $accountId): ?array
    {
        $statement = $this->pdo->prepare(
            'SELECT account_id, display_name, status, revision
             FROM account WHERE account_id = :id'
        );

        $statement->execute([':id' => $accountId]);

        $row = $statement->fetch();

        if ($row === false) {
            return null;
        }

        return [
            'account_id'   => (string) $row['account_id'],
            'display_name' => (string) $row['display_name'],
            'status'       => (int) $row['status'],
            'revision'     => (int) $row['revision'],
        ];
    }

    public function statusOf(string $accountId): int
    {
        $account = $this->findById($accountId);

        // An account that does not exist is Unknown, not Active. Unknown is never
        // permission anywhere in this system.
        return $account['status'] ?? self::STATUS_UNKNOWN;
    }

    /**
     * Creates an account and its credential together.
     *
     * One transaction, because an account with no credential could never sign in
     * and a credential with no account violates its foreign key. Either both rows
     * exist or neither does.
     *
     * The plaintext password is hashed here and is not retained: the parameter goes
     * out of scope with the call. It is never assigned to a property, never
     * logged, and never returned.
     */
    public function create(
        string $accountId,
        string $displayName,
        string $loginIdentifier,
        string $plaintextPassword,
        int $status = self::STATUS_ACTIVE
    ): void {
        $this->pdo->beginTransaction();

        try {
            $account = $this->pdo->prepare(
                'INSERT INTO account (account_id, display_name, status, revision, created_at, updated_at)
                 VALUES (:id, :name, :status, 0, NOW(3), NOW(3))'
            );

            $account->execute([
                ':id'     => $accountId,
                ':name'   => $displayName,
                ':status' => $status,
            ]);

            $credential = $this->pdo->prepare(
                'INSERT INTO account_credential (account_id, login_identifier, password_hash, updated_at)
                 VALUES (:id, :login, :hash, NOW(3))'
            );

            $credential->execute([
                ':id'    => $accountId,
                ':login' => $loginIdentifier,
                ':hash'  => Secrets::hashPassword($plaintextPassword),
            ]);

            $this->pdo->commit();
        } catch (\Throwable $e) {
            if ($this->pdo->inTransaction()) {
                $this->pdo->rollBack();
            }

            throw $e;
        }
    }

    /**
     * Replaces a stored hash, after a successful login found it outdated.
     *
     * Called only with a password that has just verified, so this cannot be used
     * to set a hash for a password nobody proved they knew.
     */
    public function upgradeHash(string $accountId, string $plaintextPassword): void
    {
        $statement = $this->pdo->prepare(
            'UPDATE account_credential
             SET password_hash = :hash, updated_at = NOW(3)
             WHERE account_id = :id'
        );

        $statement->execute([
            ':hash' => Secrets::hashPassword($plaintextPassword),
            ':id'   => $accountId,
        ]);
    }

    /**
     * Changes an account's status, guarded by its revision.
     *
     * The revision in the WHERE clause is what makes this safe against a
     * concurrent writer: if somebody else changed the row since it was read, zero
     * rows match and the caller is told rather than silently overwriting them.
     */
    public function updateStatus(string $accountId, int $status, int $expectedRevision): bool
    {
        $statement = $this->pdo->prepare(
            'UPDATE account
             SET status = :status, revision = revision + 1, updated_at = NOW(3)
             WHERE account_id = :id AND revision = :revision'
        );

        $statement->execute([
            ':status'   => $status,
            ':id'       => $accountId,
            ':revision' => $expectedRevision,
        ]);

        return $statement->rowCount() === 1;
    }
}
