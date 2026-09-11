<?php

declare(strict_types=1);

namespace ChibiFantasy\Tests;

use ChibiFantasy\Http\Api;
use ChibiFantasy\World\WorldClockRepository;

/**
 * The world's calendar, saved so a restart resumes instead of starting the day again.
 *
 * Two properties are being defended here. The first is that a restart does not move the
 * world: what goes in comes back out, midnight and day number included. The second is
 * that only the deployment can write it -- this is the one write in the API that no
 * player's session authorises, so if its guard is weak, anybody who can reach the port
 * can set what time it is for everyone at once.
 */
final class WorldClockTest extends BackendTestCase
{
    private const Server = 'server.one';
    private const Channel = 'channel.one';
    private const Key = 'a-deployment-key-that-is-not-in-git';

    private const Password = 'a-password-invented-here-only';

    private WorldClockRepository $repository;

    protected function setUp(): void
    {
        parent::setUp();

        $this->pdo->exec('DELETE FROM world_clock');

        $this->makeAccount('acc-clock', 'ayla@test', self::Password);

        $this->repository = new WorldClockRepository($this->pdo);

        // The key is handed to the API directly rather than through the environment, so
        // these tests say the same thing whatever the machine running them has configured.
        $this->api = new Api($this->pdo, self::Key);
    }

    // ---- the repository -----------------------------------------------------------------

    public function testAWorldThatHasNeverRunHasNothingSaved(): void
    {
        self::assertNull($this->repository->load(self::Server, self::Channel));
    }

    public function testWhatIsSavedComesBack(): void
    {
        self::assertTrue(
            $this->repository->save(self::Server, self::Channel, 12345.5, 3600.0)
        );

        $row = $this->repository->load(self::Server, self::Channel);

        self::assertNotNull($row);
        self::assertEqualsWithDelta(12345.5, $row['elapsed_seconds'], 0.001);
        self::assertEqualsWithDelta(3600.0, $row['seconds_per_day'], 0.001);
    }

    public function testSavingTwiceUpdatesTheSameRow(): void
    {
        $this->repository->save(self::Server, self::Channel, 100.0, 3600.0);
        $this->repository->save(self::Server, self::Channel, 200.0, 3600.0);

        $count = (int) $this->pdo->query('SELECT COUNT(*) FROM world_clock')->fetchColumn();

        self::assertSame(1, $count, 'a world must not accumulate a row per save');

        $row = $this->repository->load(self::Server, self::Channel);

        self::assertEqualsWithDelta(200.0, $row['elapsed_seconds'], 0.001);
    }

    public function testTwoChannelsKeepSeparateClocks(): void
    {
        $this->repository->save(self::Server, 'channel.one', 100.0, 3600.0);
        $this->repository->save(self::Server, 'channel.two', 900.0, 3600.0);

        self::assertEqualsWithDelta(
            100.0,
            $this->repository->load(self::Server, 'channel.one')['elapsed_seconds'],
            0.001
        );
        self::assertEqualsWithDelta(
            900.0,
            $this->repository->load(self::Server, 'channel.two')['elapsed_seconds'],
            0.001
        );
    }

    public function testTheDayNumberSurvivesTheRoundTrip(): void
    {
        // three and a half days into an hour-long day, which is the case that a
        // time-of-day-only column would lose entirely
        $elapsed = (3 * 3600.0) + 1800.0;

        $this->repository->save(self::Server, self::Channel, $elapsed, 3600.0);

        $row = $this->repository->load(self::Server, self::Channel);

        self::assertSame(3, (int) floor($row['elapsed_seconds'] / $row['seconds_per_day']));
    }

    public function testAnUnusableClockIsRefusedRatherThanStored(): void
    {
        self::assertFalse($this->repository->save(self::Server, self::Channel, -1.0, 3600.0));
        self::assertFalse($this->repository->save(self::Server, self::Channel, 100.0, 0.0));
        self::assertFalse($this->repository->save(self::Server, self::Channel, 100.0, -60.0));
        self::assertFalse($this->repository->save('', self::Channel, 100.0, 3600.0));

        self::assertNull($this->repository->load(self::Server, self::Channel));
    }

    // ---- the endpoint, and who may use it -----------------------------------------------

    public function testWritingTheClockNeedsTheDeploymentKey(): void
    {
        $response = $this->post('/api/world/clock', [
            'server_id'       => self::Server,
            'channel_id'      => self::Channel,
            'elapsed_seconds' => 500.0,
            'seconds_per_day' => 3600.0,
        ]);

        self::assertSame(401, $response->status, 'no key presented');
        self::assertNull($this->repository->load(self::Server, self::Channel));
    }

    public function testAWrongKeyIsRefused(): void
    {
        $response = $this->post('/api/world/clock', [
            'server_id'       => self::Server,
            'channel_id'      => self::Channel,
            'elapsed_seconds' => 500.0,
            'seconds_per_day' => 3600.0,
        ], 'not-the-key');

        self::assertSame(401, $response->status);
        self::assertNull($this->repository->load(self::Server, self::Channel));
    }

    public function testAPlayersSessionTokenIsNotEnough(): void
    {
        // The point of the guard: a signed-in player is still not the deployment.
        $token = $this->login('ayla@test', self::Password);

        $response = $this->post('/api/world/clock', [
            'server_id'       => self::Server,
            'channel_id'      => self::Channel,
            'elapsed_seconds' => 500.0,
            'seconds_per_day' => 3600.0,
        ], $token);

        self::assertSame(401, $response->status,
            'a player must not be able to set what time it is for everyone');
        self::assertNull($this->repository->load(self::Server, self::Channel));
    }

    public function testWithNoKeyConfiguredTheWriteIsRefusedRatherThanOpened(): void
    {
        // An API that was given no key at all: the case a deployment reaches by forgetting
        // the setting, which must close the door rather than open it.
        $this->api = new Api($this->pdo, '');

        $response = $this->post('/api/world/clock', [
            'server_id'       => self::Server,
            'channel_id'      => self::Channel,
            'elapsed_seconds' => 500.0,
            'seconds_per_day' => 3600.0,
        ], self::Key);

        self::assertSame(403, $response->status,
            'a forgotten key must close the door, not open it');
        self::assertNull($this->repository->load(self::Server, self::Channel));
    }

    public function testTheDeploymentKeyWritesTheClock(): void
    {
        $response = $this->post('/api/world/clock', [
            'server_id'       => self::Server,
            'channel_id'      => self::Channel,
            'elapsed_seconds' => 4321.5,
            'seconds_per_day' => 3600.0,
        ], self::Key);

        self::assertSame(200, $response->status, json_encode($response->body));

        $row = $this->repository->load(self::Server, self::Channel);

        self::assertNotNull($row);
        self::assertEqualsWithDelta(4321.5, $row['elapsed_seconds'], 0.001);
    }

    public function testReadingAnUnsavedWorldSaysSoRatherThanFailing(): void
    {
        $response = $this->get('/api/world/clock', [
            'server_id'  => self::Server,
            'channel_id' => self::Channel,
        ]);

        self::assertSame(200, $response->status);
        self::assertFalse($response->body['saved']);
    }

    public function testTheClockCanBeReadBackThroughTheApi(): void
    {
        $this->repository->save(self::Server, self::Channel, 777.25, 3600.0);

        $response = $this->get('/api/world/clock', [
            'server_id'  => self::Server,
            'channel_id' => self::Channel,
        ]);

        self::assertSame(200, $response->status);
        self::assertTrue($response->body['saved']);
        self::assertEqualsWithDelta(777.25, (float) $response->body['elapsed_seconds'], 0.001);
        self::assertEqualsWithDelta(3600.0, (float) $response->body['seconds_per_day'], 0.001);
    }

    public function testTheResponseCarriesNothingSensitive(): void
    {
        $this->repository->save(self::Server, self::Channel, 777.25, 3600.0);

        $response = $this->get('/api/world/clock', [
            'server_id'  => self::Server,
            'channel_id' => self::Channel,
        ]);

        $body = strtolower(json_encode($response->body));

        foreach (['password', 'token', 'secret', 'key', 'hash'] as $forbidden) {
            self::assertStringNotContainsString($forbidden, $body);
        }
    }
}
