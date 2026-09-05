<?php

declare(strict_types=1);

namespace ChibiFantasy\Tests;

use ChibiFantasy\Party\PartyRepository;

/**
 * What survives a disconnect, and what must not.
 *
 * The rule the whole suite circles is the UNIQUE on `character_id`: a character belongs
 * to at most one party, and the database is what guarantees it. Phase 13 held that in
 * memory, where two racing joins could both believe they had won.
 */
final class PartyPersistenceTest extends BackendTestCase
{
    private PartyRepository $parties;

    protected function setUp(): void
    {
        parent::setUp();

        $this->parties = new PartyRepository($this->pdo);

        $this->makeServer('srv-1');
        $this->makeAccount('acc-a', 'party-a', 'not-a-real-password');

        foreach (['char-ann', 'char-ben', 'char-cal'] as $id) {
            $this->makeCharacter($id, 'acc-a', 'srv-1', ucfirst(substr($id, 5)));
        }
    }

    // ---- create and load ----------------------------------------------------------

    public function testACharacterInNoPartyLoadsAsNothing(): void
    {
        self::assertNull($this->parties->loadByCharacter('char-ann'));
    }

    public function testAPartySurvivesAndComesBackWithItsLeaderAndPolicy(): void
    {
        $saved = $this->parties->save('party-1', 'char-ann', 1,
            ['char-ann', 'char-ben']);

        self::assertTrue($saved['ok']);
        self::assertSame(1, $saved['revision']);

        $loaded = $this->parties->loadByCharacter('char-ben');

        self::assertNotNull($loaded, 'a member could not find their own party');
        self::assertSame('party-1', $loaded['party_id']);
        self::assertSame('char-ann', $loaded['leader_character_id']);
        self::assertSame(1, $loaded['loot_policy']);
        self::assertCount(2, $loaded['members']);
    }

    public function testMembersComeBackInJoinOrderBecauseRoundRobinDependsOnIt(): void
    {
        $this->parties->save('party-1', 'char-ann', 1,
            ['char-ann', 'char-ben', 'char-cal']);

        $members = array_column(
            $this->parties->loadByCharacter('char-ann')['members'], 'character_id');

        self::assertSame(['char-ann', 'char-ben', 'char-cal'], $members,
            'the rotation order changed across a reload');

        $orders = array_column(
            $this->parties->loadByCharacter('char-ann')['members'], 'join_order');

        self::assertSame([0, 1, 2], $orders);
    }

    public function testEveryLootPolicyRoundTrips(): void
    {
        // Including NeedGreed, which has no UI yet: persistence must carry it anyway,
        // or a party that chose it would silently loot by another rule.
        foreach ([0, 1, 2] as $policy) {
            $this->parties->save('party-p' . $policy, 'char-ann', $policy, ['char-ann']);

            self::assertSame($policy,
                $this->parties->loadByCharacter('char-ann')['loot_policy']);

            $this->parties->disband('party-p' . $policy);
        }
    }

    public function testAPolicyNobodyAuthoredIsRefusedRatherThanClamped(): void
    {
        $result = $this->parties->save('party-1', 'char-ann', 99, ['char-ann']);

        self::assertFalse($result['ok']);
        self::assertSame('invalid_loot_policy', $result['reason']);

        self::assertNull($this->parties->loadByCharacter('char-ann'),
            'a refused save created a party anyway');
    }

    public function testALeaderWhoIsNotAMemberIsRefused(): void
    {
        $result = $this->parties->save('party-1', 'char-cal', 0,
            ['char-ann', 'char-ben']);

        self::assertFalse($result['ok']);
        self::assertSame('leader_not_a_member', $result['reason']);
        self::assertNull($this->parties->loadByCharacter('char-ann'));
    }

    // ---- one character, one party ----------------------------------------------------

    public function testACharacterCannotBelongToTwoPartiesAtOnce(): void
    {
        self::assertTrue(
            $this->parties->save('party-1', 'char-ann', 0, ['char-ann'])['ok']);

        $second = $this->parties->save('party-2', 'char-ben', 0,
            ['char-ben', 'char-ann']);

        self::assertFalse($second['ok'], 'a character joined a second party');
        self::assertSame('character_already_in_a_party', $second['reason']);

        // The first party is untouched, and the second was never created.
        self::assertSame('party-1',
            $this->parties->loadByCharacter('char-ann')['party_id']);
        self::assertNull($this->parties->loadByCharacter('char-ben'));
    }

    public function testTwoConcurrentJoinsLeaveExactlyOneDurableMembership(): void
    {
        // Both callers believe they may take Ann. The database decides.
        $first = $this->parties->save('party-1', 'char-ann', 0, ['char-ann']);
        $second = $this->parties->save('party-2', 'char-ben', 0, ['char-ben', 'char-ann']);

        self::assertTrue($first['ok']);
        self::assertFalse($second['ok']);

        $rows = (int) $this->pdo
            ->query("SELECT COUNT(*) FROM party_member WHERE character_id = 'char-ann'")
            ->fetchColumn();

        self::assertSame(1, $rows, 'one character held two memberships');
    }

    // ---- membership changes ------------------------------------------------------------

    public function testAMemberLeavingIsRemovedAndThePartyRemains(): void
    {
        $this->parties->save('party-1', 'char-ann', 0,
            ['char-ann', 'char-ben', 'char-cal']);

        self::assertTrue($this->parties->save('party-1', 'char-ann', 0,
            ['char-ann', 'char-cal'])['ok']);

        self::assertNull($this->parties->loadByCharacter('char-ben'),
            'a member who left is still in the party');

        self::assertCount(2, $this->parties->loadByCharacter('char-ann')['members']);
    }

    public function testAKickedMemberDoesNotComeBack(): void
    {
        $this->parties->save('party-1', 'char-ann', 0, ['char-ann', 'char-ben']);
        $this->parties->save('party-1', 'char-ann', 0, ['char-ann']);

        self::assertNull($this->parties->loadByCharacter('char-ben'));

        // And they are free to join elsewhere, which the UNIQUE would have blocked if
        // the old row had survived.
        self::assertTrue(
            $this->parties->save('party-2', 'char-ben', 0, ['char-ben'])['ok']);
    }

    public function testANewLeaderIsStored(): void
    {
        $this->parties->save('party-1', 'char-ann', 0, ['char-ann', 'char-ben']);
        $this->parties->save('party-1', 'char-ben', 0, ['char-ben', 'char-ann']);

        self::assertSame('char-ben',
            $this->parties->loadByCharacter('char-ann')['leader_character_id']);
    }

    // ---- disband ---------------------------------------------------------------------------

    public function testDisbandLeavesNothingToRestore(): void
    {
        $this->parties->save('party-1', 'char-ann', 0, ['char-ann', 'char-ben']);

        self::assertTrue($this->parties->disband('party-1')['ok']);

        self::assertNull($this->parties->loadByCharacter('char-ann'));
        self::assertNull($this->parties->loadByCharacter('char-ben'));

        $orphans = (int) $this->pdo
            ->query("SELECT COUNT(*) FROM party_member WHERE party_id = 'party-1'")
            ->fetchColumn();

        self::assertSame(0, $orphans, 'disband left orphan member rows');

        // The party is remembered as ended, not forgotten, so a reconnect can tell
        // "you left" from "you were never in one".
        $tombstone = $this->pdo
            ->query("SELECT disbanded_at FROM party WHERE party_id = 'party-1'")
            ->fetchColumn();

        self::assertNotNull($tombstone);
    }

    public function testSavingAnEmptyMemberListDisbands(): void
    {
        $this->parties->save('party-1', 'char-ann', 0, ['char-ann']);

        self::assertTrue($this->parties->save('party-1', 'char-ann', 0, [])['ok']);

        self::assertNull($this->parties->loadByCharacter('char-ann'));
    }

    // ---- idempotency and concurrency -------------------------------------------------------------

    public function testSavingTheSamePartyTwiceDuplicatesNothing(): void
    {
        $members = ['char-ann', 'char-ben', 'char-cal'];

        $this->parties->save('party-1', 'char-ann', 1, $members);
        $this->parties->save('party-1', 'char-ann', 1, $members);
        $this->parties->save('party-1', 'char-ann', 1, $members);

        $rows = (int) $this->pdo
            ->query("SELECT COUNT(*) FROM party_member WHERE party_id = 'party-1'")
            ->fetchColumn();

        self::assertSame(3, $rows, 'repeated saves duplicated the membership');

        $parties = (int) $this->pdo->query('SELECT COUNT(*) FROM party')->fetchColumn();

        self::assertSame(1, $parties);

        self::assertCount(3, $this->parties->loadByCharacter('char-ann')['members']);
    }

    public function testAStaleRevisionIsRefusedAndWritesNothing(): void
    {
        $this->parties->save('party-1', 'char-ann', 0, ['char-ann', 'char-ben']);

        $stale = $this->parties->save('party-1', 'char-ann', 1,
            ['char-ann'], 99);

        self::assertFalse($stale['ok']);
        self::assertSame('stale_revision', $stale['reason']);

        $loaded = $this->parties->loadByCharacter('char-ann');

        self::assertCount(2, $loaded['members'], 'a refused save changed the membership');
        self::assertSame(0, $loaded['loot_policy'], 'a refused save changed the policy');
    }

    public function testAFailedSaveLeavesThePartyExactlyAsItWas(): void
    {
        $this->parties->save('party-1', 'char-ann', 0, ['char-ann', 'char-ben']);

        // char-cal is put into another party first, so adding them here must fail on
        // the UNIQUE part-way through the member rewrite.
        $this->parties->save('party-2', 'char-cal', 0, ['char-cal']);

        $result = $this->parties->save('party-1', 'char-ann', 0,
            ['char-ann', 'char-ben', 'char-cal']);

        self::assertFalse($result['ok']);

        $loaded = $this->parties->loadByCharacter('char-ann');

        self::assertCount(2, $loaded['members'],
            'a rolled-back save left the party half rewritten');

        self::assertSame(['char-ann', 'char-ben'],
            array_column($loaded['members'], 'character_id'));
    }

    // ---- characters and accounts ------------------------------------------------------------------

    public function testTwoCharactersOnOneAccountMayBeInDifferentParties(): void
    {
        $this->parties->save('party-1', 'char-ann', 0, ['char-ann']);
        $this->parties->save('party-2', 'char-ben', 0, ['char-ben']);

        self::assertSame('party-1',
            $this->parties->loadByCharacter('char-ann')['party_id']);
        self::assertSame('party-2',
            $this->parties->loadByCharacter('char-ben')['party_id']);
    }

    public function testAMembershipNamingAMissingCharacterStillLoadsRatherThanFailing(): void
    {
        // Storage does not know which characters exist -- there is deliberately no
        // foreign key to `character`, because a party outliving a deleted character is
        // a cleanup problem and not a reason to make the party unloadable.
        $this->parties->save('party-1', 'char-ann', 0, ['char-ann', 'char-ghost']);

        $loaded = $this->parties->loadByCharacter('char-ann');

        self::assertNotNull($loaded, 'an unknown member made the party unreadable');
        self::assertCount(2, $loaded['members']);
    }

    public function testNoGameplayStateIsStoredOnAParty(): void
    {
        $this->parties->save('party-1', 'char-ann', 0, ['char-ann']);

        $columns = array_keys($this->pdo
            ->query('SELECT * FROM party')->fetch(\PDO::FETCH_ASSOC));

        foreach (['experience', 'health', 'mana', 'level', 'inventory', 'devil_fruit',
                  'connection', 'position'] as $forbidden) {
            foreach ($columns as $column) {
                self::assertStringNotContainsString($forbidden, $column,
                    'party rows carry gameplay state');
            }
        }
    }
}
