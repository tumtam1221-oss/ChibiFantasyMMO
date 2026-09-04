<?php

declare(strict_types=1);

/**
 * Boots the backend: autoloading and configuration, nothing else.
 *
 * A PSR-4 autoloader is written out here rather than taken from Composer so the
 * application runs from a clean checkout with no `composer install` step. Composer
 * is still used for the test framework, and its autoloader is preferred when
 * present -- but the application never depends on `vendor/` existing, which keeps
 * a deployment one file copy rather than a build.
 */

$root = dirname(__DIR__);

$composerAutoload = $root . '/vendor/autoload.php';

if (is_readable($composerAutoload)) {
    require_once $composerAutoload;
} else {
    spl_autoload_register(static function (string $class) use ($root): void {
        $prefix = 'ChibiFantasy\\';

        if (!str_starts_with($class, $prefix)) {
            return;
        }

        $relative = substr($class, strlen($prefix));
        $path = $root . '/src/' . str_replace('\\', '/', $relative) . '.php';

        if (is_readable($path)) {
            require_once $path;
        }
    });
}

\ChibiFantasy\Support\Env::load($root . '/.env');
