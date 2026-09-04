<?php

declare(strict_types=1);

namespace ChibiFantasy\Directory;

use PDO;

/**
 * Reads servers and channels, and answers whether one may be selected.
 *
 * Servers and channels live in one repository because they are always queried
 * together and share the same availability vocabulary. Splitting them would mean
 * two classes with the same status logic, which is exactly how two answers to
 * "is this open" come to disagree.
 *
 * Nothing here names a server. There is no default, no first-server rule and no
 * identifier compared to a literal: the list is whatever the tables hold, and
 * hidden servers are filtered in SQL rather than fetched and dropped afterwards.
 */
final class DirectoryRepository
{
    public const SERVER_UNKNOWN = 0;
    public const SERVER_ONLINE = 1;
    public const SERVER_BUSY = 2;
    public const SERVER_MAINTENANCE = 3;
    public const SERVER_OFFLINE = 4;
    public const SERVER_HIDDEN = 5;

    public const CHANNEL_UNKNOWN = 0;
    public const CHANNEL_ONLINE = 1;
    public const CHANNEL_BUSY = 2;
    public const CHANNEL_MAINTENANCE = 3;
    public const CHANNEL_OFFLINE = 4;

    public function __construct(private readonly PDO $pdo)
    {
    }

    /**
     * The servers a client may see.
     *
     * Hidden servers are excluded in the WHERE clause. Fetching them and filtering
     * in PHP would already have sent their names over the wire, which is the whole
     * point of hiding one.
     *
     * @return list<array<string,mixed>>
     */
    public function listServers(): array
    {
        $rows = $this->pdo->query(
            'SELECT * FROM server_definition
             WHERE status <> ' . self::SERVER_HIDDEN . '
             ORDER BY region, name_key'
        )->fetchAll();

        return array_map([$this, 'hydrateServer'], $rows);
    }

    /** @return array<string,mixed>|null */
    public function findServer(string $serverId): ?array
    {
        $statement = $this->pdo->prepare(
            'SELECT * FROM server_definition WHERE server_id = :id'
        );

        $statement->execute([':id' => $serverId]);

        $row = $statement->fetch();

        return $row === false ? null : $this->hydrateServer($row);
    }

    /**
     * The channels of one server.
     *
     * Scoped by server in SQL, so a caller cannot receive another server's
     * channels and then be trusted to ignore them.
     *
     * @return list<array<string,mixed>>
     */
    public function listChannels(string $serverId): array
    {
        $statement = $this->pdo->prepare(
            'SELECT * FROM server_channel WHERE server_id = :sid ORDER BY name_key'
        );

        $statement->execute([':sid' => $serverId]);

        return array_map([$this, 'hydrateChannel'], $statement->fetchAll());
    }

    /** @return array<string,mixed>|null */
    public function findChannel(string $channelId): ?array
    {
        $statement = $this->pdo->prepare(
            'SELECT * FROM server_channel WHERE channel_id = :id'
        );

        $statement->execute([':id' => $channelId]);

        $row = $statement->fetch();

        return $row === false ? null : $this->hydrateChannel($row);
    }

    /**
     * Shapes a server row for the API and decides what is knowable about it.
     *
     * `population` is null when the server has never reported one. That absence is
     * carried all the way to the client as "unknown" rather than becoming zero: a
     * zero would tell a player a live server is empty, and a fabricated number
     * would be worse.
     *
     * @param array<string,mixed> $row
     * @return array<string,mixed>
     */
    private function hydrateServer(array $row): array
    {
        $status = (int) $row['status'];
        $enabled = (bool) $row['enabled'];
        $capacity = (int) $row['capacity'];

        $population = $row['cached_population'] === null
            ? null
            : (int) $row['cached_population'];

        return [
            'server_id'  => (string) $row['server_id'],
            'name_key'   => (string) $row['name_key'],
            'region'     => (string) $row['region'],
            'status'     => $status,
            'enabled'    => $enabled,
            'capacity'   => $capacity,
            'population' => $population,
            'population_known' => $population !== null,
            'is_full'    => $population !== null && $capacity > 0 && $population >= $capacity,
            'versions'   => [
                'min_client'        => (string) $row['min_client_version'],
                'latest_client'     => (string) $row['latest_client_version'],
                'required_protocol' => (string) $row['required_protocol_version'],
                'min_content'       => (string) $row['min_content_version'],
                'latest_content'    => (string) $row['latest_content_version'],
                'content_advisory'  => (bool) $row['content_is_advisory'],
            ],
            'revision'   => (int) $row['revision'],
            'selectable' => $enabled
                && ($status === self::SERVER_ONLINE || $status === self::SERVER_BUSY),
        ];
    }

    /**
     * @param array<string,mixed> $row
     * @return array<string,mixed>
     */
    private function hydrateChannel(array $row): array
    {
        $status = (int) $row['status'];
        $enabled = (bool) $row['enabled'];
        $capacity = (int) $row['capacity'];

        $population = $row['cached_population'] === null
            ? null
            : (int) $row['cached_population'];

        return [
            'channel_id' => (string) $row['channel_id'],
            'server_id'  => (string) $row['server_id'],
            'name_key'   => (string) $row['name_key'],
            'status'     => $status,
            'enabled'    => $enabled,
            'capacity'   => $capacity,
            'population' => $population,
            'population_known' => $population !== null,
            'is_full'    => $population !== null && $capacity > 0 && $population >= $capacity,
            // Read from the row an administrator sets. Never derived from a channel
            // number, never settable by a client, and only ever displayed by one.
            'pk_enabled' => (bool) $row['pk_enabled'],
            'revision'   => (int) $row['revision'],
            'selectable' => $enabled
                && ($status === self::CHANNEL_ONLINE || $status === self::CHANNEL_BUSY),
        ];
    }
}
