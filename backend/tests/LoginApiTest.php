<?php

declare(strict_types=1);

namespace ChibiFantasy\Tests;

use ChibiFantasy\Auth\AccountRepository;

/**
 * The login endpoint: credentials, account state, versions and idempotency.
 */
final class LoginApiTest extends BackendTestCase
{
    private const PASSWORD = 'a-password-invented-here-only';

    public function testValidCredentialsIssueASession(): void
    {
        $this->makeAccount('acc-a', 'ayla@test', self::PASSWORD);

        $response = $this->post('/api/auth/login', [
            'request_id'       => self::newRequestId(),
            'login_identifier' => 'ayla@test',
            'password'         => self::PASSWORD,
            'versions'         => ['client' => '1.0.0', 'protocol' => '1.0.0', 'content' => '1.0.0'],
        ]);

        self::assertSame(200, $response->status);
        self::assertSame('acc-a', $response->body['account_id']);
        self::assertNotEmpty($response->body['session_id']);
        self::assertNotEmpty($response->body['token']);
    }

    public function testTheResponseNeverCarriesACredential(): void
    {
        $this->makeAccount('acc-a', 'ayla@test', self::PASSWORD);

        $response = $this->post('/api/auth/login', [
            'request_id'       => self::newRequestId(),
            'login_identifier' => 'ayla@test',
            'password'         => self::PASSWORD,
        ]);

        $json = $response->toJson();

        self::assertStringNotContainsString(self::PASSWORD, $json);
        self::assertStringNotContainsString('$2y$', $json, 'a hash must never be returned');
        self::assertArrayNotHasKey('password', $response->body);
        self::assertArrayNotHasKey('password_hash', $response->body);
    }

    public function testTheIssuedTokenIsNotStoredInPlaintext(): void
    {
        $this->makeAccount('acc-a', 'ayla@test', self::PASSWORD);

        $token = $this->login('ayla@test', self::PASSWORD);

        $stored = $this->pdo->query('SELECT token_hash FROM account_session_token')->fetchColumn();

        self::assertNotSame($token, $stored);
        self::assertSame(64, strlen((string) $stored), 'sha256 hex');
        self::assertSame(hash('sha256', $token), $stored);
    }

    public function testWrongPasswordAndUnknownAccountAreIndistinguishable(): void
    {
        $this->makeAccount('acc-a', 'ayla@test', self::PASSWORD);

        $wrong = $this->post('/api/auth/login', [
            'request_id'       => self::newRequestId(),
            'login_identifier' => 'ayla@test',
            'password'         => 'not the password',
        ]);

        $unknown = $this->post('/api/auth/login', [
            'request_id'       => self::newRequestId(),
            'login_identifier' => 'nobody@test',
            'password'         => 'not the password',
        ]);

        self::assertSame($wrong->status, $unknown->status);
        self::assertSame($wrong->body['code'], $unknown->body['code']);
        self::assertSame('invalid_credentials', $wrong->body['code']);
    }

    public function testADisabledAccountIsRefusedOnlyAfterThePasswordVerifies(): void
    {
        $this->makeAccount('acc-a', 'ayla@test', self::PASSWORD, AccountRepository::STATUS_DISABLED);

        $wrongPassword = $this->post('/api/auth/login', [
            'request_id'       => self::newRequestId(),
            'login_identifier' => 'ayla@test',
            'password'         => 'wrong',
        ]);

        // A wrong password on a disabled account must not reveal that it is
        // disabled -- that would confirm the account exists to anyone guessing.
        self::assertSame('invalid_credentials', $wrongPassword->body['code']);

        $rightPassword = $this->post('/api/auth/login', [
            'request_id'       => self::newRequestId(),
            'login_identifier' => 'ayla@test',
            'password'         => self::PASSWORD,
        ]);

        self::assertSame('account_disabled', $rightPassword->body['code']);
        self::assertSame(403, $rightPassword->status);
    }

    public function testABannedAccountIsRefused(): void
    {
        $this->makeAccount('acc-a', 'ayla@test', self::PASSWORD, AccountRepository::STATUS_BANNED);

        $response = $this->post('/api/auth/login', [
            'request_id'       => self::newRequestId(),
            'login_identifier' => 'ayla@test',
            'password'         => self::PASSWORD,
        ]);

        self::assertSame('account_banned', $response->body['code']);
    }

    public function testASecondLiveSessionIsRefused(): void
    {
        $this->makeAccount('acc-a', 'ayla@test', self::PASSWORD);

        $this->login('ayla@test', self::PASSWORD);

        $second = $this->post('/api/auth/login', [
            'request_id'       => self::newRequestId(),
            'login_identifier' => 'ayla@test',
            'password'         => self::PASSWORD,
        ]);

        self::assertSame('session_already_active', $second->body['code']);
        self::assertSame(
            1,
            (int) $this->pdo->query('SELECT COUNT(*) FROM account_session')->fetchColumn()
        );
    }

    public function testTheSameRequestIdIssuesOneSession(): void
    {
        $this->makeAccount('acc-a', 'ayla@test', self::PASSWORD);

        $requestId = self::newRequestId();

        $body = [
            'request_id'       => $requestId,
            'login_identifier' => 'ayla@test',
            'password'         => self::PASSWORD,
        ];

        $first = $this->post('/api/auth/login', $body);
        $second = $this->post('/api/auth/login', $body);

        self::assertTrue($first->isSuccess());
        self::assertTrue($second->isSuccess(), 'a retry succeeds; it does not fail');
        self::assertTrue($second->body['replayed']);
        self::assertSame($first->body['session_id'], $second->body['session_id']);

        self::assertSame(
            1,
            (int) $this->pdo->query('SELECT COUNT(*) FROM account_session')->fetchColumn(),
            'one session, not two'
        );
    }

    public function testARejectedRequestIdIsReEvaluatedRatherThanCached(): void
    {
        $this->makeAccount('acc-a', 'ayla@test', self::PASSWORD, AccountRepository::STATUS_DISABLED);

        $requestId = self::newRequestId();

        $body = [
            'request_id'       => $requestId,
            'login_identifier' => 'ayla@test',
            'password'         => self::PASSWORD,
        ];

        self::assertSame('account_disabled', $this->post('/api/auth/login', $body)->body['code']);

        // An operator re-enables the account. The same request must now succeed.
        $account = (new AccountRepository($this->pdo))->findById('acc-a');
        (new AccountRepository($this->pdo))->updateStatus(
            'acc-a',
            AccountRepository::STATUS_ACTIVE,
            $account['revision']
        );

        self::assertTrue(
            $this->post('/api/auth/login', $body)->isSuccess(),
            'a rejection wrote nothing, so re-sending must be re-judged'
        );
    }

    public function testAMissingFieldIsAValidationError(): void
    {
        $response = $this->post('/api/auth/login', ['request_id' => self::newRequestId()]);

        self::assertSame(400, $response->status);
        self::assertSame('invalid_login_identifier', $response->body['code']);
    }

    public function testTheErrorBodyLeaksNothing(): void
    {
        $response = $this->post('/api/auth/login', [
            'request_id'       => self::newRequestId(),
            'login_identifier' => 'nobody@test',
            'password'         => 'x',
        ]);

        self::assertSame(
            ['code', 'message_key', 'request_id'],
            array_keys($response->body),
            'the error contract is exactly three fields'
        );

        $json = $response->toJson();

        foreach (['SELECT', 'SQLSTATE', 'PDO', '.php', 'chibifantasy', 'Stack trace'] as $leak) {
            self::assertStringNotContainsString($leak, $json);
        }
    }
}
