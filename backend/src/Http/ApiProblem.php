<?php

declare(strict_types=1);

namespace ChibiFantasy\Http;

/**
 * The one error shape this API ever returns.
 *
 * Every failure -- validation, authentication, authorisation, a domain rejection,
 * an internal fault -- comes back as the same three fields:
 *
 *   { "code": "...", "message_key": "...", "request_id": "..." }
 *
 * `code` is machine-readable and stable; a client switches on it. `message_key`
 * is a localisation key, not a sentence, because the server does not know what
 * language the player reads. `request_id` is echoed so a player can quote it in a
 * support ticket and an operator can find the exact request in a log.
 *
 * What is deliberately absent: no SQL, no stack trace, no file path, no exception
 * message, no hint about which of "unknown account" or "wrong password" failed.
 * Those are the four ways an error response leaks, and the type has nowhere to put
 * any of them.
 *
 * HTTP status is carried separately. A client can act on the category (retry a
 * 503, re-authenticate on a 401, give up on a 400) without parsing the body, and
 * still read the precise reason from `code`. Encoding every domain refusal as 200
 * would break every proxy, cache and monitor in front of this.
 */
final class ApiProblem
{
    private function __construct(
        public readonly int $status,
        public readonly string $code,
        public readonly string $messageKey
    ) {
    }

    /** The request was malformed: missing field, wrong type, unparseable body. */
    public static function validation(string $code, string $messageKey): self
    {
        return new self(400, $code, $messageKey);
    }

    /** No usable credential or session. The caller should authenticate. */
    public static function unauthenticated(string $code, string $messageKey): self
    {
        return new self(401, $code, $messageKey);
    }

    /**
     * Authenticated, but not allowed to do this.
     *
     * Used for another account's character, another player's shop listing, a rank
     * without the permission. Never used to reveal that a resource exists -- see
     * `notFound` for why those are sometimes the same answer.
     */
    public static function forbidden(string $code, string $messageKey): self
    {
        return new self(403, $code, $messageKey);
    }

    /**
     * No such thing, or none this caller may see.
     *
     * Deliberately ambiguous. Another account's character returns this rather than
     * 403, because a distinct answer would confirm the character exists.
     */
    public static function notFound(string $code, string $messageKey): self
    {
        return new self(404, $code, $messageKey);
    }

    /**
     * The request was well formed and refused by a domain rule.
     *
     * A full server, a lapsed session, a stale revision, a sold listing. 409
     * rather than 400 because nothing about the request was wrong -- the world was
     * simply not in the state it assumed.
     */
    public static function conflict(string $code, string $messageKey): self
    {
        return new self(409, $code, $messageKey);
    }

    /** Too many attempts. The caller should slow down, not change the request. */
    public static function rateLimited(string $code = 'rate_limited'): self
    {
        return new self(429, $code, 'error.rate_limited');
    }

    /**
     * Something broke on this side.
     *
     * The message key is deliberately generic and the real cause goes to the log,
     * never to the client. An exception message can carry a DSN, a table name or a
     * fragment of a query.
     */
    public static function internal(): self
    {
        return new self(500, 'internal_error', 'error.internal');
    }

    /** The service is closed to players. Distinct from a fault: retrying may work. */
    public static function unavailable(string $code, string $messageKey): self
    {
        return new self(503, $code, $messageKey);
    }

    /** @return array{code:string,message_key:string,request_id:string} */
    public function toArray(string $requestId): array
    {
        return [
            'code'        => $this->code,
            'message_key' => $this->messageKey,
            'request_id'  => $requestId,
        ];
    }
}
