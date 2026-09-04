<?php

declare(strict_types=1);

namespace ChibiFantasy\Tests;

use ChibiFantasy\Auth\Secrets;
use PHPUnit\Framework\Attributes\DataProvider;
use ChibiFantasy\Database\Connection;
use PDO;

/**
 * Injection, secret handling, and the properties an attacker probes first.
 */
final class SecurityTest extends BackendTestCase
{
    private const PASSWORD = 'a-password-invented-here-only';

    /**
     * Hostile strings are data, never syntax.
     *
     * Every one of these would break out of a query built by concatenation. They
     * are pushed through every field that reaches SQL; the assertions afterwards
     * check the schema and the rows are still intact.
     *
     * @return list<array{string}>
     */
    public static function hostileInputs(): array
    {
        return [
            ["' OR '1'='1"],
            ["'; DROP TABLE account; --"],
            ['" OR ""="'],
            ["admin'--"],
            ["1; DELETE FROM `character`"],
            ["' UNION SELECT password_hash, 1, 1 FROM account_credential --"],
            ["\\'; TRUNCATE TABLE account_session; --"],
            ["%' OR account_id LIKE '%"],
            ["' OR SLEEP(5) --"],
            ["\x00' OR 1=1 --"],
        ];
    }

    #[DataProvider('hostileInputs')]
    public function testInjectionThroughTheLoginIdentifierIsInert(string $hostile): void
    {
        $this->makeAccount('acc-a', 'ayla@test', self::PASSWORD);

        $response = $this->post('/api/auth/login', [
            'request_id'       => self::newRequestId(),
            'login_identifier' => $hostile,
            'password'         => 'anything',
        ]);

        self::assertSame(401, $response->status);
        self::assertSame('invalid_credentials', $response->body['code']);

        self::assertSame(
            1,
            (int) $this->pdo->query('SELECT COUNT(*) FROM account')->fetchColumn(),
            'the account table survived'
        );
    }

    #[DataProvider('hostileInputs')]
    public function testInjectionThroughSelectionIdentifiersIsInert(string $hostile): void
    {
        $this->makeAccount('acc-a', 'ayla@test', self::PASSWORD);
        $this->makeServer('srv-1');
        $this->makeChannel('ch-1', 'srv-1');
        $this->makeCharacter('char-1', 'acc-a', 'srv-1', 'Ayla');

        $token = $this->login('ayla@test', self::PASSWORD);

        foreach ([
            ['/api/session/select-server', 'server_id'],
            ['/api/session/select-channel', 'channel_id'],
            ['/api/session/select-character', 'character_id'],
        ] as [$path, $field]) {
            $response = $this->post($path, [
                'request_id' => self::newRequestId(),
                $field       => $hostile,
            ], $token);

            self::assertFalse($response->isSuccess(), $path . ' accepted a hostile id');
        }

        // Every table the hostile strings named is still present and populated.
        self::assertSame(1, (int) $this->pdo->query('SELECT COUNT(*) FROM account')->fetchColumn());
        self::assertSame(1, (int) $this->pdo->query('SELECT COUNT(*) FROM `character`')->fetchColumn());
        self::assertSame(1, (int) $this->pdo->query('SELECT COUNT(*) FROM account_session')->fetchColumn());
    }

    #[DataProvider('hostileInputs')]
    public function testInjectionThroughQueryParametersIsInert(string $hostile): void
    {
        $this->makeAccount('acc-a', 'ayla@test', self::PASSWORD);
        $this->makeServer('srv-1');
        $this->makeCharacter('char-1', 'acc-a', 'srv-1', 'Ayla');

        $token = $this->login('ayla@test', self::PASSWORD);

        $response = $this->get('/api/characters', ['server_id' => $hostile], $token);

        self::assertTrue($response->isSuccess());
        self::assertSame([], $response->body['characters'], 'a hostile id simply matches nothing');
    }

    public function testInjectionThroughTheRequestIdIsInert(): void
    {
        $this->makeAccount('acc-a', 'ayla@test', self::PASSWORD);

        $response = $this->post('/api/auth/login', [
            'request_id'       => "'; DROP TABLE request_result; --",
            'login_identifier' => 'ayla@test',
            'password'         => self::PASSWORD,
        ]);

        self::assertTrue($response->isSuccess());

        // The table still exists, and the hostile key was stored as literal data.
        $stored = $this->pdo->query('SELECT request_id FROM request_result')->fetchColumn();

        self::assertSame("'; DROP TABLE request_result; --", $stored);
    }

    public function testPreparedStatementEmulationIsOff(): void
    {
        // The single setting that makes the tests above structural rather than
        // lucky: with emulation on, PDO interpolates values itself.
        self::assertFalse(
            (bool) Connection::forTests()->getAttribute(PDO::ATTR_EMULATE_PREPARES),
            'emulated prepares would defeat parameter binding'
        );
    }

    public function testNoSourceFileConcatenatesAVariableIntoSql(): void
    {
        $offenders = [];

        foreach ($this->sourceFiles() as $path) {
            $source = (string) file_get_contents($path);

            // A quoted SQL keyword immediately followed by string concatenation is
            // the shape of an injectable query.
            if (preg_match('/["\'](?:SELECT|INSERT|UPDATE|DELETE)\b[^"\']*["\']\s*\.\s*\$/i', $source)) {
                $offenders[] = basename($path);
            }
        }

        self::assertSame([], $offenders, 'SQL built by concatenating a variable');
    }

    public function testNoPasswordOrSecretIsLoggedOrReturned(): void
    {
        foreach ($this->sourceFiles() as $path) {
            $source = (string) file_get_contents($path);

            self::assertDoesNotMatchRegularExpression(
                '/error_log\([^)]*\$password/i',
                $source,
                basename($path) . ' logs a password'
            );

            self::assertDoesNotMatchRegularExpression(
                '/error_log\([^)]*\$token/i',
                $source,
                basename($path) . ' logs a token'
            );
        }
    }

    public function testNoWeakHashingAppearsAnywhere(): void
    {
        foreach ($this->sourceFiles() as $path) {
            $source = (string) file_get_contents($path);

            foreach (['md5(', 'sha1(', 'crypt(', 'mt_rand(', 'uniqid('] as $weak) {
                self::assertStringNotContainsString(
                    $weak,
                    $source,
                    basename($path) . ' uses ' . $weak
                );
            }
        }
    }

    public function testNoCredentialIsCommittedInSource(): void
    {
        foreach ($this->sourceFiles() as $path) {
            $source = (string) file_get_contents($path);

            // A password assigned to a literal is the shape of a committed secret.
            self::assertDoesNotMatchRegularExpression(
                '/(DB_PASSWORD|db_password|apiKey|api_key|secret)\s*=\s*[\'"][^\'"]{6,}[\'"]/i',
                $source,
                basename($path) . ' appears to contain a literal credential'
            );
        }
    }

    public function testTokensAreUnpredictable(): void
    {
        $seen = [];

        for ($i = 0; $i < 200; $i++) {
            $token = Secrets::randomToken();

            self::assertSame(64, strlen($token));
            self::assertArrayNotHasKey($token, $seen, 'a token repeated');

            $seen[$token] = true;
        }
    }

    public function testAShortTokenIsRefused(): void
    {
        $this->expectException(\InvalidArgumentException::class);

        Secrets::randomToken(8);
    }

    public function testAnUnknownRouteRevealsNothing(): void
    {
        $response = $this->get('/api/../../etc/passwd');

        self::assertSame(404, $response->status);
        self::assertSame(
            ['code', 'message_key', 'request_id'],
            array_keys($response->body)
        );
    }

    /** @return list<string> */
    private function sourceFiles(): array
    {
        $directory = new \RecursiveIteratorIterator(
            new \RecursiveDirectoryIterator(dirname(__DIR__) . '/src')
        );

        $files = [];

        foreach ($directory as $file) {
            if ($file instanceof \SplFileInfo && $file->getExtension() === 'php') {
                $files[] = $file->getPathname();
            }
        }

        return $files;
    }
}
