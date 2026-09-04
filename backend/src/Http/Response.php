<?php

declare(strict_types=1);

namespace ChibiFantasy\Http;

/**
 * One outbound response: a status, a JSON body, and nothing else.
 *
 * Returned by handlers rather than echoed, so a handler can be called in a test
 * and its result inspected without capturing output or sending headers. `send()`
 * is the only method that touches the transport, and it is called once, at the
 * edge.
 */
final class Response
{
    /** @param array<string,mixed>|list<mixed> $body */
    private function __construct(
        public readonly int $status,
        public readonly array $body
    ) {
    }

    /** @param array<string,mixed>|list<mixed> $body */
    public static function ok(array $body = []): self
    {
        return new self(200, $body);
    }

    /** @param array<string,mixed> $body */
    public static function created(array $body = []): self
    {
        return new self(201, $body);
    }

    public static function problem(ApiProblem $problem, string $requestId): self
    {
        return new self($problem->status, $problem->toArray($requestId));
    }

    /** True for 2xx. What a client checks before reading a payload. */
    public function isSuccess(): bool
    {
        return $this->status >= 200 && $this->status < 300;
    }

    public function toJson(): string
    {
        // JSON_THROW_ON_ERROR: a body that will not encode is a bug here, and
        // silently sending `false` would be a blank 200 nobody could diagnose.
        // UNESCAPED_UNICODE keeps player names readable in a log.
        return json_encode(
            $this->body,
            JSON_THROW_ON_ERROR | JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES
        );
    }

    /** Writes the response. The only method in the class that is not pure. */
    public function send(): void
    {
        if (!headers_sent()) {
            http_response_code($this->status);
            header('Content-Type: application/json; charset=utf-8');

            // This API is called by a game client, never by a browser page, so
            // there is no legitimate cross-origin caller and nothing to cache.
            header('Cache-Control: no-store');
            header('X-Content-Type-Options: nosniff');
        }

        echo $this->toJson();
    }
}
