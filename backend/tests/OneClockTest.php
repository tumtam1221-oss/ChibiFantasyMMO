<?php

declare(strict_types=1);

namespace ChibiFantasy\Tests;

use PHPUnit\Framework\TestCase;

/**
 * One clock, one timezone, one answer.
 *
 * **The trap.** PHP and MySQL each have their own idea of what time it is, and on this
 * project they disagree: PHP is pinned to UTC and MySQL runs in the machine's own zone.
 * Every datetime column in this schema is a naked `DATETIME` -- no offset stored, no way
 * to tell afterwards which of the two wrote a given row. A value written by the wrong one
 * is therefore not merely late, it is unrecoverably ambiguous, and it looks entirely
 * normal in every query anybody runs.
 *
 * **The rule.** A moment is written by the database: `NOW(3)` inside the statement, never
 * a string formatted in PHP and bound in. Comparisons happen in SQL for the same reason --
 * see `SessionRepository::LAPSED_EXPRESSION`, where getting this wrong would have meant
 * sessions that expire hours early or never expire at all.
 *
 * **Why this is a test and not a comment.** It already was a comment, and
 * `MonsterRewardRepository` bound `date('Y-m-d H:i:s.v')` into a column anyway -- in the
 * same UPDATE whose line above it set `updated_at = NOW(3)`. A rule that is only written
 * down is one the next person in a hurry breaks, and that person was me.
 */
final class OneClockTest extends TestCase
{
    /**
     * Calls that produce a moment, and so produce it in the wrong timezone.
     *
     * @var list<string>
     */
    private const ForbiddenCalls = [
        'date',
        'gmdate',
        'strtotime',
        'mktime',
        'time',
        'microtime',
        'date_create',
        'date_create_immutable',
        'getdate',
        'localtime',
    ];

    /**
     * Classes that are a moment.
     *
     * @var list<string>
     */
    private const ForbiddenClasses = [
        'DateTime',
        'DateTimeImmutable',
    ];

    public function testNothingInSourceTellsTheTimeItself(): void
    {
        $offenders = [];

        foreach ($this->sourceFiles() as $path) {
            foreach ($this->clockUsesIn((string) file_get_contents($path)) as $use) {
                $offenders[] = $this->shortName($path) . ' uses ' . $use;
            }
        }

        self::assertSame([], $offenders,
            "a moment must come from MySQL's NOW(3), never from PHP: the two run in "
            . 'different timezones and the columns store no offset, so a row written by '
            . 'the wrong one cannot afterwards be told from a row written by the right '
            . 'one. Found: ' . implode('; ', $offenders));
    }

    public function testTheRuleWouldActuallyCatchSomething(): void
    {
        // A scanner that quietly matches nothing passes forever and defends nothing. This
        // is the offending line as it really was, and the shapes near-missed by matching
        // on text rather than on tokens: `update(` and `validate(` both contain "date(".
        $found = $this->clockUsesIn(<<<'PHP'
            <?php
            final class Sample
            {
                public function go(): void
                {
                    $this->update();
                    $this->validate();
                    $candidate = 1;
                    $x = ':completed' . (true ? date('Y-m-d H:i:s.v') : null);
                    $y = new \DateTimeImmutable('now');
                }
            }
            PHP);

        self::assertContains('date()', $found, 'the real offending call was not caught');
        self::assertContains('DateTimeImmutable', $found);

        self::assertCount(2, $found,
            'update(), validate() and $candidate are not clocks: '
            . implode('; ', $found));
    }

    public function testTheRuleIgnoresWhatIsOnlyWrittenAbout(): void
    {
        // SessionRepository explains this rule at length in a doc comment, naming the very
        // functions forbidden here. Matching inside comments would forbid explaining it.
        $found = $this->clockUsesIn(<<<'PHP'
            <?php
            final class Sample
            {
                /**
                 * PHP's time() and strtotime() use PHP's own timezone, so a session would
                 * appear to expire hours early. Use NOW(3) instead.
                 */
                public function go(): void
                {
                    // never date('Y-m-d')
                    $this->pdo->exec('UPDATE t SET at = NOW(3)');
                }
            }
            PHP);

        self::assertSame([], $found);
    }

    /**
     * Every clock this source reaches for, by name.
     *
     * Read as tokens rather than searched as text: `update(` and `validate(` both contain
     * "date(", and a scanner that reported those would be turned off within a week.
     *
     * @return list<string>
     */
    private function clockUsesIn(string $source): array
    {
        $tokens = token_get_all($source);
        $found = [];

        // PHP 8 hands back a qualified name as one token, so "\DateTimeImmutable" never
        // appears as T_STRING at all and a scanner looking only for that would miss it.
        $nameTokens = [T_STRING];

        if (defined('T_NAME_FULLY_QUALIFIED')) {
            $nameTokens[] = T_NAME_FULLY_QUALIFIED;
            $nameTokens[] = T_NAME_QUALIFIED;
        }

        foreach ($tokens as $index => $token) {
            if (!is_array($token) || !in_array($token[0], $nameTokens, true)) {
                continue;
            }

            // The last segment: "\DateTimeImmutable" and "DateTimeImmutable" are one class.
            $parts = explode('\\', $token[1]);
            $name = (string) end($parts);

            if (in_array($name, self::ForbiddenClasses, true)
                && $this->isPrecededByNew($tokens, $index)) {
                $found[] = $name;

                continue;
            }

            if (in_array(strtolower($name), self::ForbiddenCalls, true)
                && $this->isCall($tokens, $index)
                && !$this->isMethodCall($tokens, $index)) {
                $found[] = $name . '()';
            }
        }

        return $found;
    }

    /** @param array<int,mixed> $tokens */
    private function isCall(array $tokens, int $index): bool
    {
        return $this->nextMeaningful($tokens, $index) === '(';
    }

    /**
     * Whether this name is a method on something, which is this project's own code.
     *
     * `$this->time()` and `$clock->date()` are not PHP's clock and are none of this
     * rule's business.
     *
     * @param array<int,mixed> $tokens
     */
    private function isMethodCall(array $tokens, int $index): bool
    {
        $before = $this->previousMeaningful($tokens, $index);

        return $before === '->' || $before === '::';
    }

    /** @param array<int,mixed> $tokens */
    private function isPrecededByNew(array $tokens, int $index): bool
    {
        return strtolower((string) $this->previousMeaningful($tokens, $index, true)) === 'new';
    }

    /** @param array<int,mixed> $tokens */
    private function nextMeaningful(array $tokens, int $index): ?string
    {
        for ($i = $index + 1; $i < count($tokens); $i++) {
            $value = $this->meaningful($tokens[$i]);

            if ($value !== null) {
                return $value;
            }
        }

        return null;
    }

    /** @param array<int,mixed> $tokens */
    private function previousMeaningful(array $tokens, int $index,
        bool $skipBackslash = false): ?string
    {
        for ($i = $index - 1; $i >= 0; $i--) {
            $value = $this->meaningful($tokens[$i]);

            if ($value === null) {
                continue;
            }

            // "new \DateTimeImmutable" puts a name-separator between the two.
            if ($skipBackslash && ($value === '\\' || $value === '')) {
                continue;
            }

            return $value;
        }

        return null;
    }

    private function meaningful(mixed $token): ?string
    {
        if (!is_array($token)) {
            return (string) $token;
        }

        if (in_array($token[0], [T_WHITESPACE, T_COMMENT, T_DOC_COMMENT], true)) {
            return null;
        }

        if (defined('T_NAME_QUALIFIED') && $token[0] === T_NAME_FULLY_QUALIFIED) {
            return $token[1];
        }

        return $token[1];
    }

    /** @return list<string> */
    private function sourceFiles(): array
    {
        $root = dirname(__DIR__) . '/src';

        $files = [];

        $walk = new \RecursiveIteratorIterator(
            new \RecursiveDirectoryIterator($root, \FilesystemIterator::SKIP_DOTS)
        );

        foreach ($walk as $entry) {
            if ($entry->isFile() && $entry->getExtension() === 'php') {
                $files[] = $entry->getPathname();
            }
        }

        sort($files);

        self::assertNotEmpty($files, 'no source files were scanned at all');

        return $files;
    }

    private function shortName(string $path): string
    {
        return basename(dirname($path)) . '/' . basename($path);
    }
}
