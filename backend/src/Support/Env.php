<?php

declare(strict_types=1);

namespace ChibiFantasy\Support;

/**
 * Reads configuration from a .env file and the process environment.
 *
 * Why this exists rather than a package: the backend needs exactly one thing from
 * a configuration library -- key/value pairs out of a file that is never committed --
 * and pulling a dependency in for that would put a third party between the
 * application and its own credentials.
 *
 * Values are never logged, never echoed and never returned to a client. The only
 * consumer is the database connection factory.
 */
final class Env
{
    /** @var array<string,string> */
    private static array $values = [];

    private static bool $loaded = false;

    /**
     * Loads a .env file once. Later calls are ignored.
     *
     * A missing file is not an error: a deployed server supplies its configuration
     * through the real environment, and requiring a file there would force secrets
     * onto disk for no reason.
     */
    public static function load(string $path): void
    {
        if (self::$loaded) {
            return;
        }

        self::$loaded = true;

        if (!is_readable($path)) {
            return;
        }

        $lines = file($path, FILE_IGNORE_NEW_LINES | FILE_SKIP_EMPTY_LINES);

        if ($lines === false) {
            return;
        }

        foreach ($lines as $line) {
            $line = trim($line);

            if ($line === '' || str_starts_with($line, '#')) {
                continue;
            }

            $split = strpos($line, '=');

            if ($split === false) {
                continue;
            }

            $key = trim(substr($line, 0, $split));
            $value = trim(substr($line, $split + 1));

            // Strip one layer of surrounding quotes, so a password containing a
            // space can be written the obvious way.
            if (strlen($value) >= 2) {
                $first = $value[0];
                $last = $value[strlen($value) - 1];

                if (($first === '"' && $last === '"') || ($first === "'" && $last === "'")) {
                    $value = substr($value, 1, -1);
                }
            }

            self::$values[$key] = $value;
        }
    }

    /**
     * The real environment wins over the file.
     *
     * That ordering is what lets a deployed server, a CI runner or a container
     * override anything without editing a file, and it is why production needs no
     * .env at all.
     */
    public static function get(string $key, ?string $default = null): ?string
    {
        $fromProcess = getenv($key);

        if ($fromProcess !== false && $fromProcess !== '') {
            return $fromProcess;
        }

        return self::$values[$key] ?? $default;
    }

    public static function getInt(string $key, int $default): int
    {
        $value = self::get($key);

        if ($value === null || !is_numeric($value)) {
            return $default;
        }

        return (int) $value;
    }

    public static function getBool(string $key, bool $default): bool
    {
        $value = self::get($key);

        if ($value === null) {
            return $default;
        }

        return in_array(strtolower($value), ['1', 'true', 'yes', 'on'], true);
    }

    /**
     * A value the application cannot start without.
     *
     * Fails loudly at startup rather than producing a connection with an empty
     * password and a confusing error later.
     */
    public static function require(string $key): string
    {
        $value = self::get($key);

        if ($value === null || $value === '') {
            throw new \RuntimeException("Missing required configuration key: {$key}");
        }

        return $value;
    }

    /** Test seam: forget everything loaded so far. */
    public static function reset(): void
    {
        self::$values = [];
        self::$loaded = false;
    }
}
