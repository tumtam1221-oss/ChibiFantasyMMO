<?php

declare(strict_types=1);

namespace ChibiFantasy\Session;

use PDO;

/**
 * Remembers what a request already produced, so a retry does not repeat it.
 *
 * Follows the rule Phase 13 established and Phase 14 kept: a request key maps to
 * at most one committed outcome, a repeat is handed the first answer, and a
 * *rejected* request is deliberately not remembered.
 *
 * That last part is the subtle one. A rejection wrote nothing, so re-sending it
 * must be re-judged: the cause -- a full server, a lapsed session, an out-of-date
 * build -- may no longer hold, and a player who fixed it should succeed rather
 * than be told forever what was once true.
 *
 * The UNIQUE (request_id, scope) index is the real protection, not the read. Two
 * concurrent retries both find nothing, both do the work, and both try to record
 * it; the index makes one of them lose. A check-then-act in PHP alone would let
 * both commit.
 */
final class IdempotencyStore
{
    public function __construct(private readonly PDO $pdo)
    {
    }

    /**
     * The response a request already produced, or null.
     *
     * @return array<string,mixed>|null
     */
    public function find(string $requestId, string $scope): ?array
    {
        $statement = $this->pdo->prepare(
            'SELECT response_json FROM request_result
             WHERE request_id = :rid AND scope = :scope'
        );

        $statement->execute([':rid' => $requestId, ':scope' => $scope]);

        $json = $statement->fetchColumn();

        if ($json === false) {
            return null;
        }

        $decoded = json_decode((string) $json, true);

        return is_array($decoded) ? $decoded : null;
    }

    /**
     * Records what a request produced.
     *
     * Returns false when the key was already taken, which means a concurrent
     * attempt won the race. The caller must then read that attempt's answer and
     * return it rather than its own -- which is what keeps two simultaneous
     * retries from both committing.
     *
     * @param array<string,mixed> $response
     */
    public function remember(
        string $requestId,
        string $scope,
        ?string $accountId,
        array $response
    ): bool {
        $statement = $this->pdo->prepare(
            'INSERT IGNORE INTO request_result
                (request_id, scope, account_id, response_json, created_at)
             VALUES (:rid, :scope, :aid, :json, NOW(3))'
        );

        $statement->execute([
            ':rid'   => $requestId,
            ':scope' => $scope,
            ':aid'   => $accountId,
            ':json'  => json_encode($response, JSON_THROW_ON_ERROR | JSON_UNESCAPED_UNICODE),
        ]);

        return $statement->rowCount() === 1;
    }

    /**
     * Runs an operation at most once for a given request key.
     *
     * The shape every mutating endpoint uses:
     *
     *   1. look for a previous answer and return it if found;
     *   2. otherwise do the work;
     *   3. record the answer, and if that loses a race, return the winner's.
     *
     * Only accepted outcomes are recorded, which the caller signals through
     * `$isRecordable`. A refusal falls straight through and stays re-judgeable.
     *
     * @param callable():array{recordable:bool,response:array<string,mixed>} $work
     * @return array{response:array<string,mixed>,replayed:bool}
     */
    public function once(
        string $requestId,
        string $scope,
        ?string $accountId,
        callable $work
    ): array {
        $previous = $this->find($requestId, $scope);

        if ($previous !== null) {
            return ['response' => $previous, 'replayed' => true];
        }

        $outcome = $work();

        if (!$outcome['recordable']) {
            return ['response' => $outcome['response'], 'replayed' => false];
        }

        $won = $this->remember($requestId, $scope, $accountId, $outcome['response']);

        if (!$won) {
            // Somebody else recorded first. Their answer is the authoritative one;
            // returning ours would report a second execution that must not have
            // happened.
            $winner = $this->find($requestId, $scope);

            if ($winner !== null) {
                return ['response' => $winner, 'replayed' => true];
            }
        }

        return ['response' => $outcome['response'], 'replayed' => false];
    }
}
