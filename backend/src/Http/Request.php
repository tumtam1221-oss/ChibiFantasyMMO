<?php

declare(strict_types=1);

namespace ChibiFantasy\Http;

/**
 * One inbound HTTP request, already parsed and validated in shape.
 *
 * Constructed from superglobals at the edge and from arrays in tests, so every
 * handler is exercisable without a web server. That is why nothing below reads
 * `$_POST` or `php://input` directly.
 *
 * The accessors validate as they read. A handler asking for `requireString('x')`
 * either gets a non-empty string or an exception it does not have to think about,
 * which keeps the "is this field present and the right type" checks out of every
 * handler and in one place that is tested.
 */
final class Request
{
    /**
     * @param array<string,mixed>  $body
     * @param array<string,string> $headers lower-cased keys
     * @param array<string,string> $query
     */
    public function __construct(
        public readonly string $method,
        public readonly string $path,
        private readonly array $body = [],
        private readonly array $headers = [],
        private readonly array $query = [],
        public readonly string $remoteAddress = ''
    ) {
    }

    /** Builds a request from the PHP superglobals. The only place they are touched. */
    public static function fromGlobals(): self
    {
        $method = strtoupper((string) ($_SERVER['REQUEST_METHOD'] ?? 'GET'));

        $path = (string) ($_SERVER['REQUEST_URI'] ?? '/');
        $queryStart = strpos($path, '?');

        if ($queryStart !== false) {
            $path = substr($path, 0, $queryStart);
        }

        $headers = [];

        foreach ($_SERVER as $key => $value) {
            if (!is_string($key) || !str_starts_with($key, 'HTTP_')) {
                continue;
            }

            $name = strtolower(str_replace('_', '-', substr($key, 5)));
            $headers[$name] = (string) $value;
        }

        $body = [];
        $raw = file_get_contents('php://input');

        if (is_string($raw) && $raw !== '') {
            $decoded = json_decode($raw, true);

            if (is_array($decoded)) {
                $body = $decoded;
            }
        }

        /** @var array<string,string> $query */
        $query = array_map(static fn ($v): string => is_string($v) ? $v : '', $_GET);

        return new self(
            $method,
            $path,
            $body,
            $headers,
            $query,
            (string) ($_SERVER['REMOTE_ADDR'] ?? '')
        );
    }

    public function header(string $name): ?string
    {
        return $this->headers[strtolower($name)] ?? null;
    }

    /**
     * The bearer token, if one was presented.
     *
     * Returned as an opaque string and never parsed, decoded or logged. What it
     * means is decided by looking it up, not by reading it -- matching the Phase 14
     * SessionToken contract exactly.
     */
    public function bearerToken(): ?string
    {
        $authorization = $this->header('authorization');

        if ($authorization === null) {
            return null;
        }

        if (!preg_match('/^Bearer\s+(\S+)$/i', $authorization, $matches)) {
            return null;
        }

        return $matches[1];
    }

    public function query(string $key, ?string $default = null): ?string
    {
        return $this->query[$key] ?? $default;
    }

    public function has(string $key): bool
    {
        return array_key_exists($key, $this->body);
    }

    public function string(string $key, string $default = ''): string
    {
        $value = $this->body[$key] ?? null;

        return is_string($value) ? $value : $default;
    }

    /** @throws ValidationException when absent, not a string, or empty */
    public function requireString(string $key, int $maxLength = 190): string
    {
        $value = $this->body[$key] ?? null;

        if (!is_string($value) || trim($value) === '') {
            throw new ValidationException($key, 'error.field_required');
        }

        if (mb_strlen($value) > $maxLength) {
            throw new ValidationException($key, 'error.field_too_long');
        }

        return $value;
    }

    public function int(string $key, int $default = 0): int
    {
        $value = $this->body[$key] ?? null;

        if (is_int($value)) {
            return $value;
        }

        return is_string($value) && is_numeric($value) ? (int) $value : $default;
    }

    /**
     * A number that is allowed a fractional part.
     *
     * Separate from int() because a world position is not a whole number and reading one
     * through int() would quietly floor it, moving a recovered loot pile.
     */
    public function float(string $key, float $default = 0.0): float
    {
        $value = $this->body[$key] ?? null;

        if (is_float($value) || is_int($value)) {
            return (float) $value;
        }

        return is_string($value) && is_numeric($value) ? (float) $value : $default;
    }

    public function bool(string $key, bool $default = false): bool
    {
        $value = $this->body[$key] ?? null;

        return is_bool($value) ? $value : $default;
    }

    /** @return array<string,mixed> */
    public function nested(string $key): array
    {
        $value = $this->body[$key] ?? null;

        return is_array($value) ? $value : [];
    }
}

/**
 * A field was missing or the wrong shape.
 *
 * Carries the field name so the response can say which, and a message key rather
 * than a sentence so the client localises it.
 */
final class ValidationException extends \RuntimeException
{
    public function __construct(
        public readonly string $field,
        public readonly string $messageKey
    ) {
        parent::__construct("Invalid field: {$field}");
    }
}
