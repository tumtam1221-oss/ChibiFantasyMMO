<?php

declare(strict_types=1);

/**
 * The single entry point.
 *
 * `public/` is the only directory a web server should expose. Source, migrations,
 * configuration and the .env file all live one level up, outside the document
 * root, so a misconfigured server cannot serve them as text.
 *
 * Errors are never displayed. In development they go to the log where a developer
 * reads them; in production display would leak file paths, a stack trace and often
 * a fragment of SQL to whoever provoked the failure.
 */

ini_set('display_errors', '0');
ini_set('log_errors', '1');

require_once dirname(__DIR__) . '/src/bootstrap.php';

use ChibiFantasy\Database\Connection;
use ChibiFantasy\Http\Api;
use ChibiFantasy\Http\ApiProblem;
use ChibiFantasy\Http\Request;
use ChibiFantasy\Http\Response;

$request = Request::fromGlobals();

try {
    $api = new Api(Connection::get());
    $api->handle($request)->send();
} catch (\Throwable $e) {
    // Reaching here means the database was unreachable or bootstrapping failed --
    // the API itself catches everything else. The client is told the service is
    // unavailable and nothing about why.
    error_log('[boot] ' . $e->getMessage());

    Response::problem(
        ApiProblem::unavailable('service_unavailable', 'error.service_unavailable'),
        $request->string('request_id', '')
    )->send();
}
