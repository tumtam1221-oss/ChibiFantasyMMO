<?php

declare(strict_types=1);

/**
 * The deployment key the world server proves itself with when it writes the calendar.
 *
 *   php bin/world-key.php              is a key configured? (never prints it)
 *   php bin/world-key.php --generate   make one and write it into .env
 *   php bin/world-key.php --export     print the line the GAME server needs
 *
 * Why this is a command and not something that happens by itself:
 *
 * The key has to be the same in two places -- the API checks it as WORLD_SERVER_KEY,
 * the game server presents it as CHIBI_WORLD_SERVER_KEY -- and those are usually two
 * processes and may be two machines. A key invented quietly by whichever one started
 * first would be a key the other one does not have, and the only symptom would be a
 * world that silently stops remembering what day it is. That is a bad afternoon.
 *
 * So: generating is explicit, and printing is a separate explicit step, because the
 * one thing worse than a credential nobody set is a credential in a scrollback buffer
 * that nobody meant to put there.
 *
 * It never overwrites an existing key without --force. Rotating the key is a real
 * operation -- both sides have to be restarted together -- and doing it by accident
 * while asking a question is not a thing this should allow.
 */

require_once dirname(__DIR__) . '/src/bootstrap.php';

use ChibiFantasy\Support\Env;

const VARIABLE = 'WORLD_SERVER_KEY';
const GAME_VARIABLE = 'CHIBI_WORLD_SERVER_KEY';

$args = array_slice($argv, 1);
$generate = in_array('--generate', $args, true);
$export = in_array('--export', $args, true);
$force = in_array('--force', $args, true);

$envPath = dirname(__DIR__) . '/.env';
$configured = (string) Env::get(VARIABLE, '');

// ---- report -------------------------------------------------------------------------

if (!$generate && !$export) {
    if ($configured === '') {
        echo "no " . VARIABLE . " configured: this API will refuse every calendar write." . PHP_EOL;
        echo "run: php bin/world-key.php --generate" . PHP_EOL;

        exit(1);
    }

    echo VARIABLE . " is configured (" . strlen($configured) . " characters)." . PHP_EOL;
    echo "the game server needs the same value as " . GAME_VARIABLE . "." . PHP_EOL;
    echo "run: php bin/world-key.php --export" . PHP_EOL;

    exit(0);
}

// ---- export -------------------------------------------------------------------------

if ($export) {
    if ($configured === '') {
        fwrite(STDERR, "nothing to export: no " . VARIABLE . " is configured." . PHP_EOL);

        exit(1);
    }

    // Printed on stdout alone so it can be piped somewhere sensible rather than read.
    echo GAME_VARIABLE . '=' . $configured . PHP_EOL;

    exit(0);
}

// ---- generate -----------------------------------------------------------------------

if ($configured !== '' && !$force) {
    fwrite(STDERR, VARIABLE . " is already configured. Rotating it means restarting the "
        . "API and every world server together, or the calendar stops being saved." . PHP_EOL);
    fwrite(STDERR, "re-run with --force if that is what you meant." . PHP_EOL);

    exit(1);
}

if (!is_writable(dirname($envPath)) || (file_exists($envPath) && !is_writable($envPath))) {
    fwrite(STDERR, "cannot write {$envPath}." . PHP_EOL);

    exit(1);
}

// 32 bytes from the OS, url-safe so it survives every config format it may pass through.
$key = rtrim(strtr(base64_encode(random_bytes(32)), '+/', '-_'), '=');

$existing = file_exists($envPath) ? (string) file_get_contents($envPath) : '';

// Replace the line if it is there, append it if it is not, so --force rotates in place
// rather than leaving two lines and letting the file decide which one wins.
$line = VARIABLE . '=' . $key;

if (preg_match('/^' . VARIABLE . '=.*$/m', $existing) === 1) {
    $existing = preg_replace('/^' . VARIABLE . '=.*$/m', $line, $existing);
} else {
    if ($existing !== '' && !str_ends_with($existing, "\n")) {
        $existing .= "\n";
    }

    $existing .= "\n# The world server proves it is the deployment, not a player, when it\n"
        . "# writes the world calendar. The game server reads it as " . GAME_VARIABLE . ".\n"
        . $line . "\n";
}

if (file_put_contents($envPath, $existing) === false) {
    fwrite(STDERR, "could not write {$envPath}." . PHP_EOL);

    exit(1);
}

// Deliberately not echoed. --export is how it leaves this machine, on purpose.
echo "wrote a new " . VARIABLE . " to .env." . PHP_EOL;
echo "now give the game server the same value:" . PHP_EOL;
echo "  php bin/world-key.php --export" . PHP_EOL;

exit(0);
