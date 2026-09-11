<?php

declare(strict_types=1);

namespace ChibiFantasy\World;

use PDO;

/**
 * Remembers where a world's calendar had got to, so a restart resumes rather than
 * beginning the day again.
 *
 * **The whole clock, not the hour.** What is stored is total elapsed world seconds,
 * because the day number is derived from that by division. Storing only the time of
 * day would lose the date every midnight, which is exactly what a festival calendar
 * cannot tolerate.
 *
 * **The rate is stored with it.** Elapsed seconds mean nothing without the day length
 * they were measured against: an operator who doubles the day and restarts would
 * otherwise have every stored second reinterpreted and the world would jump by weeks.
 * A caller that reads back a different rate to the one it uses is told, and decides
 * for itself whether to trust the number.
 *
 * **One row per world.** Keyed by server and channel, because that is the unit a world
 * server process already serves. Two channels are two worlds and may honestly be at
 * different hours.
 *
 * **Never destructive.** The write is an upsert of one row. There is no delete method
 * and no statement here touches any other table.
 */
final class WorldClockRepository
{
    public function __construct(private readonly PDO $pdo)
    {
    }

    /**
     * What this world's clock was last saved at, or null if it has never been saved.
     *
     * Null is the ordinary answer for a brand new world and is not an error: the
     * server starts the day at its authored time when it gets one.
     *
     * @return array{elapsed_seconds:float,seconds_per_day:float,updated_at:string}|null
     */
    public function load(string $serverId, string $channelId): ?array
    {
        if ($serverId === '' || $channelId === '') {
            return null;
        }

        $statement = $this->pdo->prepare(
            'SELECT elapsed_seconds, seconds_per_day, updated_at
             FROM world_clock
             WHERE server_id = :server AND channel_id = :channel'
        );

        $statement->execute([':server' => $serverId, ':channel' => $channelId]);

        $row = $statement->fetch(PDO::FETCH_ASSOC);

        if ($row === false) {
            return null;
        }

        return [
            'elapsed_seconds' => (float) $row['elapsed_seconds'],
            'seconds_per_day' => (float) $row['seconds_per_day'],
            'updated_at'      => (string) $row['updated_at'],
        ];
    }

    /**
     * Writes where the world has got to.
     *
     * Refuses a clock that is not a usable measurement rather than storing it: a
     * negative elapsed total or a day of zero seconds would come back as a world that
     * cannot be resumed, and the failure would appear one restart later with nothing
     * to connect it to. Returning false lets the caller keep the row it had.
     */
    public function save(
        string $serverId,
        string $channelId,
        float $elapsedSeconds,
        float $secondsPerDay
    ): bool {
        if ($serverId === '' || $channelId === '') {
            return false;
        }

        if (!is_finite($elapsedSeconds) || $elapsedSeconds < 0.0) {
            return false;
        }

        if (!is_finite($secondsPerDay) || $secondsPerDay <= 0.0) {
            return false;
        }

        $statement = $this->pdo->prepare(
            'INSERT INTO world_clock
                 (server_id, channel_id, elapsed_seconds, seconds_per_day)
             VALUES (:server, :channel, :elapsed, :rate)
             ON DUPLICATE KEY UPDATE
                 elapsed_seconds = VALUES(elapsed_seconds),
                 seconds_per_day = VALUES(seconds_per_day)'
        );

        return $statement->execute([
            ':server'  => $serverId,
            ':channel' => $channelId,
            ':elapsed' => $elapsedSeconds,
            ':rate'    => $secondsPerDay,
        ]);
    }
}
