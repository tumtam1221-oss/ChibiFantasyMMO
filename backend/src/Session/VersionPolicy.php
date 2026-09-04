<?php

declare(strict_types=1);

namespace ChibiFantasy\Session;

/**
 * Decides whether a client's reported versions are acceptable.
 *
 * A deliberate mirror of Phase 14's VersionPolicy, and the authoritative copy.
 * The client evaluates the same rule so it can show a patch prompt without a round
 * trip; this one is what actually decides, because a client that concluded it was
 * compatible would simply be believed otherwise.
 *
 * The two must agree, so the rule is written the same way in both: protocol is an
 * exact match, client and content have a floor and a latest, and the strictest
 * failure is reported first.
 *
 * Nothing here names a version. Every bound comes from the server row, so raising
 * a floor is a database update and no PHP changes.
 */
final class VersionPolicy
{
    public const COMPATIBLE = 0;
    public const OPTIONAL_UPDATE = 1;
    public const REQUIRED_UPDATE = 2;
    public const INCOMPATIBLE = 3;

    public const KIND_NONE = 0;
    public const KIND_CLIENT = 1;
    public const KIND_PROTOCOL = 2;
    public const KIND_CONTENT = 3;

    /**
     * @param array{min_client:string,latest_client:string,required_protocol:string,min_content:string,latest_content:string,content_advisory:bool} $required
     * @return array{compatibility:int,kind:int,supplied:string,expected:string}
     */
    public static function evaluate(
        string $clientVersion,
        string $protocolVersion,
        string $contentVersion,
        array $required
    ): array {
        // Protocol first: a mismatch here is not something a patch is promised to
        // fix, and reporting a milder problem would tell a player to update when
        // their build simply cannot speak to this server.
        if ($required['required_protocol'] !== ''
            && !self::equals($protocolVersion, $required['required_protocol'])) {
            return self::result(
                self::INCOMPATIBLE,
                self::KIND_PROTOCOL,
                $protocolVersion,
                $required['required_protocol']
            );
        }

        if ($required['min_client'] !== ''
            && self::isOlder($clientVersion, $required['min_client'])) {
            return self::result(
                self::REQUIRED_UPDATE,
                self::KIND_CLIENT,
                $clientVersion,
                $required['min_client']
            );
        }

        if ($required['min_content'] !== ''
            && self::isOlder($contentVersion, $required['min_content'])) {
            // Whether stale content blocks play is the content owner's decision,
            // carried on the server row rather than assumed here.
            return self::result(
                $required['content_advisory'] ? self::OPTIONAL_UPDATE : self::REQUIRED_UPDATE,
                self::KIND_CONTENT,
                $contentVersion,
                $required['min_content']
            );
        }

        if ($required['latest_client'] !== ''
            && self::isOlder($clientVersion, $required['latest_client'])) {
            return self::result(
                self::OPTIONAL_UPDATE,
                self::KIND_CLIENT,
                $clientVersion,
                $required['latest_client']
            );
        }

        if ($required['latest_content'] !== ''
            && self::isOlder($contentVersion, $required['latest_content'])) {
            return self::result(
                self::OPTIONAL_UPDATE,
                self::KIND_CONTENT,
                $contentVersion,
                $required['latest_content']
            );
        }

        return self::result(self::COMPATIBLE, self::KIND_NONE, '', '');
    }

    /** Compatible or merely behind: either way the player may proceed. */
    public static function isPlayable(array $evaluation): bool
    {
        return $evaluation['compatibility'] === self::COMPATIBLE
            || $evaluation['compatibility'] === self::OPTIONAL_UPDATE;
    }

    /**
     * Compares two dotted version strings numerically.
     *
     * Written out rather than using `version_compare` because that function
     * applies PHP's own release conventions -- it treats "1.0.0-beta" as older
     * than "1.0.0" and understands suffixes this project does not use. Comparing
     * three integers is the whole requirement, and doing it explicitly means the
     * answer cannot change with a PHP upgrade.
     *
     * A missing or malformed part reads as zero, so "1.2" and "1.2.0" are equal.
     */
    private static function isOlder(string $supplied, string $required): bool
    {
        $a = self::parse($supplied);
        $b = self::parse($required);

        for ($i = 0; $i < 3; $i++) {
            if ($a[$i] !== $b[$i]) {
                return $a[$i] < $b[$i];
            }
        }

        return false;
    }

    private static function equals(string $a, string $b): bool
    {
        return self::parse($a) === self::parse($b);
    }

    /** @return array{0:int,1:int,2:int} */
    private static function parse(string $version): array
    {
        $parts = explode('.', trim($version));

        return [
            (int) ($parts[0] ?? 0),
            (int) ($parts[1] ?? 0),
            (int) ($parts[2] ?? 0),
        ];
    }

    /** @return array{compatibility:int,kind:int,supplied:string,expected:string} */
    private static function result(int $compatibility, int $kind, string $supplied, string $expected): array
    {
        return [
            'compatibility' => $compatibility,
            'kind'          => $kind,
            'supplied'      => $supplied,
            'expected'      => $expected,
        ];
    }
}
