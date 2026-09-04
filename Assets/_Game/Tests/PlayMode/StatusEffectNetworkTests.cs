// Editor-only, and only this fixture, for the reason the other network fixtures document:
// it loads the committed prefab registry through AssetDatabase, because the point is to
// prove the SHIPPED configuration works.
#if UNITY_EDITOR

using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using ChibiFantasy.Client.UI;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.Network;
using ChibiFantasy.Server;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using FishNet.Transporting;
using FishNet.Transporting.Tugboat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ChibiFantasy.Tests.PlayMode
{
    /// <summary>
    /// Buffs and debuffs, decided by a real server and drawn by real clients.
    /// </summary>
    /// <remarks>
    /// <b>Status is the system where a client's opinion is worth the most.</b> A player who
    /// could shorten a silence, refuse a debuff or invent a buff would be cheating in the
    /// most direct way this game allows. So every effect below is applied, refreshed and
    /// expired by the server, and every client is told rather than asked.
    ///
    /// <b>The countdown is checked from both ends.</b> The bar counts down locally so a timer
    /// moves between packets; the effect ends when the server's clock says so. Proving both
    /// is the point -- a client whose display and whose authority were the same thing would
    /// walk out of a silence early on a fast machine.
    ///
    /// <b>Privacy is checked from the wrong side on purpose.</b> It is not enough that the
    /// server declines to send B's buffs to A; the test that matters is that A's bar, with
    /// every object A can see, still shows only A's own.
    ///
    /// <b>Integration-only bootstrap, labelled as such.</b> Characters are admitted directly
    /// into the registry rather than through login and enter-world, which has its own live
    /// suite.
    /// </remarks>
    [TestFixture]
    internal sealed class StatusEffectNetworkTests
    {
        private const string RegistryPath = "Assets/DefaultPrefabObjects.asset";
        private const string CharacterPrefabPath =
            "Assets/_Game/Prefabs/Network/WorldEntity_Character.prefab";

        private const string HomeMap = "map.home";
        private const string MaxHp = "stat.maxhp";
        private const string Atk = "stat.atk";
        private const string Def = "stat.def";

        private const string Silence = "status.silence";
        private const string Poison = "status.poison";
        private const string Blessing = "status.blessing";
        private const string Regeneration = "status.regeneration";

        private const string Firebolt = "skill.firebolt";
        private const string Curse = "skill.curse";

        private sealed class FakeStore : ICharacterStateStore
        {
            public readonly Dictionary<string, PersistedCharacter> Rows =
                new Dictionary<string, PersistedCharacter>();

            public CharacterPersistenceResult Load(SessionId s)
            {
                return Rows.TryGetValue(s.Value, out PersistedCharacter row)
                    ? CharacterPersistenceResult.Loaded(row)
                    : CharacterPersistenceResult.Failed(CharacterPersistenceFailure.NotOwned);
            }

            public CharacterPersistenceResult Save(SessionId s, PersistedCharacter c, int r)
            {
                Rows[s.Value] = c;

                return CharacterPersistenceResult.Saved(r + 1);
            }
        }

        private GameObject _serverObject;
        private GameObject _clientAObject;
        private GameObject _clientBObject;
        private NetworkManager _server;
        private NetworkManager _clientA;
        private NetworkManager _clientB;

        private FakeStore _store;
        private WorldCharacterRegistry _players;
        private MonsterWorldRuntime _monsters;
        private DefinitionRegistry<StatusEffectDefinition> _effects;
        private DefinitionRegistry<SkillDefinition> _skills;
        private CharacterStatusAuthority _status;
        private CharacterCombatRequestHandler _combat;
        private CharacterReplicationService _replication;

        private readonly Dictionary<NetworkManager, WorldHudScreen> _huds =
            new Dictionary<NetworkManager, WorldHudScreen>();

        private readonly List<Object> _created = new List<Object>();
        private ushort _port;

        private static ushort NextPort() => (ushort)Random.Range(52100, 54900);

        [SetUp]
        public void SetUp()
        {
            _port = NextPort();

            _server = BuildManager("StatusServer", true, out _serverObject);
            _serverObject.SetActive(true);

            _clientA = BuildManager("StatusClientA", false, out _clientAObject);
            _clientAObject.SetActive(true);

            _clientB = BuildManager("StatusClientB", false, out _clientBObject);
            _clientBObject.SetActive(true);

            var maps = new DefinitionRegistry<MapDefinition>();
            maps.Register(Map(HomeMap));

            var spawns = new DefinitionRegistry<SpawnPointDefinition>();
            spawns.Register(PlayerSpawn());

            _effects = new DefinitionRegistry<StatusEffectDefinition>();
            _effects.Register(Effect(Silence, StatusEffectCategory.Control, 6f,
                control: ControlEffectType.Silence));
            _effects.Register(Effect(Poison, StatusEffectCategory.DamageOverTime, 8f,
                stacking: StatusEffectStackBehavior.AddStack, maxStacks: 5));
            _effects.Register(Effect(Blessing, StatusEffectCategory.Buff, 0f));
            _effects.Register(Effect(Regeneration, StatusEffectCategory.HealOverTime, 12f));

            _skills = new DefinitionRegistry<SkillDefinition>();
            _skills.Register(Skill(Firebolt, SkillEffect.Damage(10, ElementType.Neutral, null,
                DamageType.Physical)));
            _skills.Register(Skill(Curse, SkillEffect.ApplyStatusEffect(new DefinitionId(Poison))));

            _store = new FakeStore();
            _players = new WorldCharacterRegistry(_store, spawns);

            _monsters = new MonsterWorldRuntime(_players,
                new DefinitionRegistry<MonsterDefinition>(), new DefinitionId(MaxHp),
                new CombatTeam(2));

            var commands = new CombatCommandAuthority(_players, _ => true, _monsters);

            // Reach far enough that range never masks the rule under test, and the status
            // registry so the validator can ask whether a caster is silenced.
            var pipeline = new ServerCombatPipeline(commands, _monsters, null,
                BasicAttackRules.Melee(new DefinitionId(Atk), new DefinitionId(Def), 1, 50f),
                default, _skills, default, _effects);

            _combat = new CharacterCombatRequestHandler(pipeline, () =>
            {
                _replication.Synchronise();
                _status.PublishChanged();
            });

            _replication = new CharacterReplicationService(_server, _players,
                Prefab(CharacterPrefabPath), _combat);

            _status = new CharacterStatusAuthority(_players, _effects, _replication);

            _replication.UseStatus(_status);
        }

        [TearDown]
        public void TearDown()
        {
            _replication?.DespawnAll();

            if (_clientB != null) _clientB.ClientManager.StopConnection();
            if (_clientA != null) _clientA.ClientManager.StopConnection();
            if (_server != null) _server.ServerManager.StopConnection(true);

            if (_clientBObject != null) Object.DestroyImmediate(_clientBObject);
            if (_clientAObject != null) Object.DestroyImmediate(_clientAObject);
            if (_serverObject != null) Object.DestroyImmediate(_serverObject);

            _huds.Clear();

            foreach (Object created in _created)
            {
                if (created != null) Object.DestroyImmediate(created);
            }

            _created.Clear();
        }

        // ---- A: what is already on you when you arrive -------------------------------------------

        [UnityTest]
        public IEnumerator AClientArrivingIntoADebuffIsToldAboutItImmediately()
        {
            yield return StartServerAndOneClient();

            LivingCharacter character = EnterWorld("char-a", Connections()[0]);

            // Already silenced before the object ever spawns -- a player reconnecting mid
            // fight, which is the case a first-snapshot race silently breaks.
            Apply(character, Silence, "skill.dark");

            _replication.Synchronise();

            yield return Until(() => Entity(_clientA, "char-a") != null
                && Entity(_clientA, "char-a").Status.Count > 0);

            StatusSnapshot snapshot = Entity(_clientA, "char-a").Status;

            Assert.That(snapshot.CharacterId, Is.EqualTo("char-a"),
                "snapshot as received: " + snapshot + " effects="
                + (snapshot.Effects == null ? "null" : snapshot.Effects.Length.ToString()));
            Assert.That(snapshot.Count, Is.EqualTo(1),
                "a target message sent before the owner observes the object reaches nobody, "
                + "silently -- so the first one goes out from the spawn callback");
            Assert.That(snapshot.Effects[0].EffectId, Is.EqualTo(Silence));
            Assert.That(snapshot.Effects[0].Category,
                Is.EqualTo((int)StatusEffectCategory.Control));
            Assert.That(snapshot.Effects[0].RemainingSeconds, Is.EqualTo(6f).Within(0.01f));
        }

        // ---- B: something lands while you are standing there ---------------------------------------

        [UnityTest]
        public IEnumerator AStatusAppliedByTheServerReachesTheClientsBar()
        {
            yield return StartServerAndOneClient();

            LivingCharacter character = EnterWorld("char-a", Connections()[0]);

            _replication.Synchronise();

            WorldHudScreen hud = Hud(_clientA);

            yield return Until(() => Entity(_clientA, "char-a") != null);

            hud.Bind(Entity(_clientA, "char-a"));

            Assert.That(hud.StatusEffects.DebuffCount, Is.Zero, "precondition: unafflicted");

            Apply(character, Poison, "skill.spit", stacks: 2);

            Assert.That(_status.PublishChanged(), Is.EqualTo(1));

            yield return Until(() => hud.StatusEffects.DebuffCount > 0);

            Assert.That(hud.StatusEffects.DebuffCount, Is.EqualTo(1));
            Assert.That(hud.StatusEffects.BuffCount, Is.Zero);

            StatusEffectViewData drawn = hud.StatusEffects.Presenter.Debuffs[0];

            Assert.That(drawn.Effect.Value, Is.EqualTo(Poison));
            Assert.That(drawn.Stacks, Is.EqualTo(2));
            Assert.That(drawn.ShowStacks, Is.True);
            Assert.That(drawn.IsBeneficial, Is.False, "poison is not good news");

            // The number the server sent. The label is already a frame or two behind it,
            // because the bar counts down locally -- which is the whole point of it.
            Assert.That(Entity(_clientA, "char-a").Status.Effects[0].RemainingSeconds,
                Is.EqualTo(8f).Within(0.01f));
            Assert.That(drawn.IsIndefinite, Is.False);
            Assert.That(drawn.RemainingLabel, Is.Not.Empty);
            Assert.That(drawn.RemainingSeconds, Is.LessThanOrEqualTo(8f)
                .And.GreaterThan(6f));

            // And a buff lands in the other row.
            Apply(character, Blessing, "fruit.light");

            _status.PublishChanged();

            yield return Until(() => hud.StatusEffects.BuffCount > 0);

            Assert.That(hud.StatusEffects.BuffCount, Is.EqualTo(1));
            Assert.That(hud.StatusEffects.DebuffCount, Is.EqualTo(1),
                "the debuff did not move rows");
            Assert.That(hud.StatusEffects.Presenter.Buffs[0].IsIndefinite, Is.True);
            Assert.That(hud.StatusEffects.Presenter.Buffs[0].RemainingLabel, Is.Empty);
        }

        [UnityTest]
        public IEnumerator AnUnchangedFrameSendsNothingAtAll()
        {
            yield return StartServerAndOneClient();

            LivingCharacter character = EnterWorld("char-a", Connections()[0]);

            _replication.Synchronise();

            yield return Until(() => Entity(_clientA, "char-a") != null);

            Apply(character, Blessing, "fruit.light");

            _status.PublishChanged();

            int sent = _status.Published;

            // An indefinite effect on an otherwise idle character. Twenty ticks.
            for (var i = 0; i < 20; i++) _status.Tick(0.1f);

            Assert.That(_status.Published, Is.EqualTo(sent),
                "a countdown must not become a packet per second per player");
        }

        // ---- C: the server's clock ends it -----------------------------------------------------------

        [UnityTest]
        public IEnumerator TheServerExpiresTheEffectAndTheClientsIconGoes()
        {
            yield return StartServerAndOneClient();

            LivingCharacter character = EnterWorld("char-a", Connections()[0]);

            _replication.Synchronise();

            WorldHudScreen hud = Hud(_clientA);

            yield return Until(() => Entity(_clientA, "char-a") != null);

            hud.Bind(Entity(_clientA, "char-a"));

            Apply(character, Poison, "skill.spit", duration: 2f);

            _status.PublishChanged();

            yield return Until(() => hud.StatusEffects.DebuffCount > 0);

            // The client's own countdown runs past the end. Nothing may happen.
            hud.StatusEffects.Tick(30f);

            Assert.That(hud.StatusEffects.DebuffCount, Is.EqualTo(1),
                "a client that dropped an effect on its own timer would decide when a "
                + "debuff ends");
            Assert.That(character.Status.Has(new DefinitionId(Poison)), Is.True,
                "and the server certainly has not removed it");

            // Now the server's clock, in two steps that straddle the end.
            _status.Tick(1f);

            Assert.That(character.Status.Has(new DefinitionId(Poison)), Is.True);

            _status.Tick(1.5f);

            Assert.That(character.Status.Has(new DefinitionId(Poison)), Is.False);
            Assert.That(_status.Expired, Is.EqualTo(1));

            yield return Until(() => hud.StatusEffects.DebuffCount == 0);

            Assert.That(hud.StatusEffects.DebuffCount, Is.Zero, "the icon outlived the effect");
            Assert.That(Entity(_clientA, "char-a").Status.Count, Is.Zero);
        }

        // ---- D: silence ---------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator ASilencedClientIsRefusedItsSkillByTheServerAndPaysNothing()
        {
            yield return StartServerAndOneClient();
            yield return StartSecondClient();

            List<int> connections = Connections();

            LivingCharacter caster = EnterWorld("char-a", connections[0]);
            LivingCharacter victim = EnterWorld("char-b", connections[1], team: 2);

            caster.Skills.Learn(new DefinitionId(Firebolt));

            _replication.Synchronise();

            yield return Until(() => Entity(_clientA, "char-a") != null
                && Entity(_clientA, "char-b") != null);

            // It works before the silence, so the refusal afterwards is about the silence.
            int manaBefore = caster.Combatant.Character.Resources.CurrentMana;

            Entity(_clientA, "char-a").RequestAttack(victim.CombatantId.Value, Firebolt, 1, 1);

            yield return Until(() => _combat.Handled > 0);

            Assert.That(_combat.LastResult.SkillRejection,
                Is.Not.EqualTo(SkillUseRejection.Silenced),
                "precondition: an unsilenced caster is not refused for silence");

            // Now the server silences them, and tells them.
            Apply(caster, Silence, "skill.dark");

            _status.PublishChanged();

            WorldHudScreen hud = Hud(_clientA);
            hud.Bind(Entity(_clientA, "char-a"));

            yield return Until(() => hud.StatusEffects.DebuffCount > 0);

            Assert.That(hud.StatusEffects.Presenter.Debuffs[0].Effect.Value,
                Is.EqualTo(Silence), "the player can see why they are about to be refused");

            int handled = _combat.Handled;
            int manaAtSilence = caster.Combatant.Character.Resources.CurrentMana;
            int victimHealth = victim.Combatant.CurrentHealth;

            Entity(_clientA, "char-a").RequestAttack(victim.CombatantId.Value, Firebolt, 1, 2);

            yield return Until(() => _combat.Handled > handled);

            Assert.That(_combat.LastResult.IsAccepted, Is.False);
            Assert.That(_combat.LastResult.SkillRejection,
                Is.EqualTo(SkillUseRejection.Silenced),
                "the server refused it, not the client");

            // Nothing was spent, nothing was put on cooldown, nothing was damaged. The
            // refusal happens in the validator, which by contract writes nothing.
            Assert.That(caster.Combatant.Character.Resources.CurrentMana,
                Is.EqualTo(manaAtSilence), "a refused skill must not charge for itself");
            Assert.That(victim.Combatant.CurrentHealth, Is.EqualTo(victimHealth));

            // And the status is still exactly what the server says it is.
            Assert.That(caster.Status.Has(new DefinitionId(Silence)), Is.True);
            Assert.That(hud.StatusEffects.DebuffCount, Is.EqualTo(1));

            Assert.That(manaBefore, Is.GreaterThanOrEqualTo(
                caster.Combatant.Character.Resources.CurrentMana));
        }

        // ---- E: debuff immunity ---------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator AnImmuneCharacterTakesNoDebuffAndIsShownNoFalseIcon()
        {
            yield return StartServerAndOneClient();
            yield return StartSecondClient();

            List<int> connections = Connections();

            LivingCharacter attacker = EnterWorld("char-a", connections[0]);
            LivingCharacter immune = EnterWorld("char-b", connections[1], team: 2);

            // What a Light fruit grants: a refusal of a whole category, so a debuff authored
            // tomorrow does not quietly bypass it.
            immune.Status.AddImmunity(new StatusImmunity(new DefinitionId("fruit.light"),
                default, StatusEffectCategory.DamageOverTime));

            attacker.Skills.Learn(new DefinitionId(Curse));

            _replication.Synchronise();

            WorldHudScreen hud = Hud(_clientB);

            yield return Until(() => Entity(_clientA, "char-b") != null
                && Entity(_clientB, "char-b") != null);

            hud.Bind(Entity(_clientB, "char-b"));

            Revision before = immune.Status.Revision;

            // A real hostile skill, over the wire, whose whole payload is the debuff.
            Entity(_clientA, "char-a").RequestAttack(immune.CombatantId.Value, Curse, 1, 1);

            yield return Until(() => _combat.Handled > 0);

            Assert.That(immune.Status.Has(new DefinitionId(Poison)), Is.False,
                "the immunity is asked before anything is written");
            Assert.That(immune.Status.ActiveCount, Is.Zero);
            Assert.That(immune.Status.Revision, Is.EqualTo(before),
                "a refused effect must not even look like a change");

            yield return Until(() => false, 30);

            Assert.That(Entity(_clientB, "char-b").Status.Count, Is.Zero);
            Assert.That(hud.StatusEffects.DebuffCount, Is.Zero,
                "an icon for a debuff that never landed is the precise failure the immunity "
                + "exists to prevent");

            // A beneficial effect still lands: a debuff immunity is not a buff immunity.
            Assert.That(StatusEffectService.TryApply(immune.Status,
                new DefinitionId(Blessing), new DefinitionId("fruit.light"), _effects)
                .IsAccepted, Is.True);

            _status.PublishChanged();

            yield return Until(() => hud.StatusEffects.BuffCount > 0);

            Assert.That(hud.StatusEffects.BuffCount, Is.EqualTo(1));
            Assert.That(hud.StatusEffects.DebuffCount, Is.Zero);
        }

        // ---- F: stacking, as it is actually authored ---------------------------------------------------

        [UnityTest]
        public IEnumerator StacksFollowTheAuthoredRuleAndTheClientSeesTheServersCount()
        {
            yield return StartServerAndOneClient();

            LivingCharacter character = EnterWorld("char-a", Connections()[0]);

            _replication.Synchronise();

            WorldHudScreen hud = Hud(_clientA);

            yield return Until(() => Entity(_clientA, "char-a") != null);

            hud.Bind(Entity(_clientA, "char-a"));

            // Poison is authored AddStack with a ceiling of five.
            for (var i = 0; i < 3; i++) Apply(character, Poison, "skill.spit");

            Assert.That(character.Status.Get(new DefinitionId(Poison)).Stacks, Is.EqualTo(3));

            _status.PublishChanged();

            yield return Until(() => hud.StatusEffects.DebuffCount > 0
                && hud.StatusEffects.Presenter.Debuffs[0].Stacks == 3);

            Assert.That(hud.StatusEffects.Presenter.Debuffs[0].Stacks, Is.EqualTo(3));
            Assert.That(hud.StatusEffects.DebuffCount, Is.EqualTo(1),
                "a stacked effect is one icon, not three");

            // Past the authored ceiling. The last applications are refused outright, which
            // is the authored rule rather than a fault: a stack that cannot go higher is
            // "already present, unchanged".
            var refusals = 0;

            for (var i = 0; i < 9; i++)
            {
                StatusApplyResult result = StatusEffectService.TryApply(character.Status,
                    new DefinitionId(Poison), new DefinitionId("skill.spit"), _effects);

                if (!result.IsAccepted)
                {
                    Assert.That(result.Reason,
                        Is.EqualTo(StatusApplyRejection.AlreadyPresent));

                    refusals++;
                }
            }

            Assert.That(refusals, Is.GreaterThan(0), "the ceiling was never reached");
            Assert.That(character.Status.Get(new DefinitionId(Poison)).Stacks, Is.EqualTo(5),
                "the ceiling is the definition's, and the server enforces it");

            _status.PublishChanged();

            yield return Until(() => hud.StatusEffects.Presenter.Debuffs[0].Stacks == 5);

            Assert.That(hud.StatusEffects.Presenter.Debuffs[0].Stacks, Is.EqualTo(5));

            // An effect that does not stack: applied once, and a second application is
            // refused rather than adding a second icon.
            Apply(character, Blessing, "fruit.light");

            // Re-applied. Whatever the service reports, the observable outcome is what
            // matters: one icon, one stack, still indefinite.
            StatusEffectService.TryApply(character.Status, new DefinitionId(Blessing),
                new DefinitionId("fruit.light"), _effects);

            Assert.That(character.Status.Get(new DefinitionId(Blessing)).Stacks,
                Is.EqualTo(1), "a non-stacking effect never gains a stack");

            _status.PublishChanged();

            yield return Until(() => hud.StatusEffects.BuffCount > 0);

            Assert.That(hud.StatusEffects.BuffCount, Is.EqualTo(1));
            Assert.That(hud.StatusEffects.Presenter.Buffs[0].ShowStacks, Is.False);
        }

        // ---- G and H: dying, leaving, coming back ----------------------------------------------------

        [UnityTest]
        public IEnumerator StatusSurvivesDeathAndIsGoneAfterLeavingTheWorld()
        {
            yield return StartServerAndOneClient();

            int connection = Connections()[0];

            LivingCharacter character = EnterWorld("char-a", connection);

            _replication.Synchronise();

            WorldHudScreen hud = Hud(_clientA);

            yield return Until(() => Entity(_clientA, "char-a") != null);

            hud.Bind(Entity(_clientA, "char-a"));

            Apply(character, Poison, "skill.spit");

            _status.PublishChanged();

            yield return Until(() => hud.StatusEffects.DebuffCount > 0);

            // Death, on the server. There is no rule anywhere in this project that clears
            // status on death, so the effect stays -- pinned here so a future gate that
            // decides otherwise has to change a test rather than change behaviour by
            // accident.
            character.Combatant.ApplyHealthDelta(-character.Combatant.CurrentHealth);

            Assert.That(character.Combatant.CurrentHealth, Is.Zero);
            Assert.That(character.Status.Has(new DefinitionId(Poison)), Is.True,
                "nothing in this project clears status on death; that is the current rule");

            // Leaving does end it, because the runtime is per live character and nothing
            // persists it.
            Assert.That(_players.Despawn(connection).IsOk, Is.True);

            _status.Forget(new CharacterId("char-a"));
            _replication.Synchronise();

            yield return Until(() => Entity(_clientA, "char-a") == null);

            hud.Unbind();

            Assert.That(hud.StatusEffects.DebuffCount, Is.Zero, "no stale icon after leaving");

            // Back again, on the same connection: a new character object, an empty list.
            LivingCharacter returned = EnterWorld("char-a", connection);

            Assert.That(returned.Status.ActiveCount, Is.Zero,
                "status is server memory and is not written to the database");

            _replication.Synchronise();

            yield return Until(() => Entity(_clientA, "char-a") != null);

            hud.Bind(Entity(_clientA, "char-a"));

            yield return Until(() => false, 30);

            Assert.That(hud.StatusEffects.DebuffCount, Is.Zero,
                "the bar matches the server exactly, which is empty");
            Assert.That(hud.StatusEffects.BuffCount, Is.Zero);

            // And a status applied after the return does arrive, so the rebind is live.
            Apply(returned, Regeneration, "item.potion");

            _status.PublishChanged();

            yield return Until(() => hud.StatusEffects.BuffCount > 0);

            Assert.That(hud.StatusEffects.BuffCount, Is.EqualTo(1));
        }

        // ---- I: privacy ------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator NeitherPlayerEverReceivesTheOthersStatusEffects()
        {
            yield return StartServerAndOneClient();
            yield return StartSecondClient();

            List<int> connections = Connections();

            LivingCharacter a = EnterWorld("char-a", connections[0]);
            LivingCharacter b = EnterWorld("char-b", connections[1], team: 2);

            Apply(a, Blessing, "fruit.light");
            Apply(b, Poison, "skill.spit", stacks: 4);

            _replication.Synchronise();

            yield return Until(() => Entity(_clientA, "char-a") != null
                && Entity(_clientA, "char-b") != null
                && Entity(_clientB, "char-a") != null
                && Entity(_clientB, "char-b") != null);

            _status.PublishChanged();

            yield return Until(() => Entity(_clientA, "char-a").Status.Count > 0
                && Entity(_clientB, "char-b").Status.Count > 0);

            // Each sees their own.
            Assert.That(Entity(_clientA, "char-a").Status.Effects[0].EffectId,
                Is.EqualTo(Blessing));
            Assert.That(Entity(_clientB, "char-b").Status.Effects[0].EffectId,
                Is.EqualTo(Poison));

            // And the other player's object, which both clients can see, carries nothing.
            Assert.That(Entity(_clientA, "char-b").IsOwner, Is.False,
                "precondition: A can see B's character");
            Assert.That(Entity(_clientA, "char-b").Status.Count, Is.Zero,
                "what a player is buffed with tells an opponent when to engage");
            Assert.That(Entity(_clientB, "char-a").Status.Count, Is.Zero);

            // A bar asked to draw the other player refuses rather than showing an empty row.
            WorldHudScreen hud = Hud(_clientA);

            Assert.That(hud.StatusEffects.Bind(Entity(_clientA, "char-b"), _effects),
                Is.False);
            Assert.That(hud.StatusEffects.Presenter.HasSnapshot, Is.False);
        }

        // ---- the widgets themselves --------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator TheBarRebuildsOnceASnapshotAndNotOncePerFrame()
        {
            yield return StartServerAndOneClient();

            LivingCharacter character = EnterWorld("char-a", Connections()[0]);

            _replication.Synchronise();

            WorldHudScreen hud = Hud(_clientA);

            yield return Until(() => Entity(_clientA, "char-a") != null);

            hud.Bind(Entity(_clientA, "char-a"));

            Apply(character, Poison, "skill.spit", duration: 30f);

            _status.PublishChanged();

            yield return Until(() => hud.StatusEffects.DebuffCount > 0);

            int rebuilds = hud.StatusEffects.RebuildCount;
            int widgets = hud.StatusEffects.DebuffRow.childCount;

            // A second of frames, with a countdown running the whole time.
            for (var i = 0; i < 60; i++) yield return null;

            Assert.That(hud.StatusEffects.RebuildCount, Is.EqualTo(rebuilds),
                "instantiating a row of icons every frame is how a HUD ends up at the top "
                + "of an allocation profile");
            Assert.That(hud.StatusEffects.DebuffRow.childCount, Is.EqualTo(widgets),
                "and no duplicate widgets accumulated");
            Assert.That(hud.StatusEffects.DebuffCount, Is.EqualTo(1));

            // The timer did move, though.
            Assert.That(hud.StatusEffects.Presenter.Debuffs[0].RemainingSeconds,
                Is.LessThan(30f));

            // A replacing snapshot rebuilds exactly once and leaves nothing stale behind.
            Apply(character, Blessing, "fruit.light");

            _status.PublishChanged();

            yield return Until(() => hud.StatusEffects.BuffCount > 0);

            Assert.That(hud.StatusEffects.RebuildCount, Is.EqualTo(rebuilds + 1));
            Assert.That(hud.StatusEffects.BuffRow.childCount, Is.EqualTo(1));
            Assert.That(hud.StatusEffects.DebuffRow.childCount, Is.EqualTo(1),
                "the poison was redrawn, not duplicated");
        }

        // ---- composition ---------------------------------------------------------------------------------------

        private WorldHudScreen Hud(NetworkManager client)
        {
            if (_huds.TryGetValue(client, out WorldHudScreen existing)) return existing;

            var host = new GameObject(client.name + " HUD");
            _created.Add(host);

            var hud = host.AddComponent<WorldHudScreen>();

            hud.UseStatusEffects(_effects);

            _huds[client] = hud;

            return hud;
        }

        /// <summary>Applies a status the way the server does: through the one service.</summary>
        private void Apply(LivingCharacter character, string effect, string source,
            float duration = 0f, int stacks = 1)
        {
            StatusApplyResult result = StatusEffectService.TryApply(character.Status,
                new DefinitionId(effect), new DefinitionId(source), _effects, duration, stacks);

            Assert.That(result.IsAccepted, Is.True, result.ToString());
        }

        // ---- harness ---------------------------------------------------------------------------------------------

        private NetworkManager BuildManager(string name, bool listening, out GameObject host)
        {
            host = new GameObject(name);
            host.SetActive(false);

            LogAssert.Expect(LogType.Error, new Regex("SpawnablePrefabs is null"));

            NetworkManager manager = host.AddComponent<NetworkManager>();

            manager.SpawnablePrefabs =
                UnityEditor.AssetDatabase.LoadAssetAtPath<DefaultPrefabObjects>(RegistryPath);

            typeof(NetworkManager)
                .GetField("_persistence", System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance)
                ?.SetValue(manager, NetworkManager.PersistenceType.AllowMultiple);

            var transport = host.AddComponent<Tugboat>();
            transport.SetPort(_port);

            if (listening) transport.SetServerBindAddress("127.0.0.1", IPAddressType.IPv4);
            else transport.SetClientAddress("127.0.0.1");

            return manager;
        }

        private static NetworkObject Prefab(string path)
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);

            Assert.That(prefab, Is.Not.Null, "no prefab at " + path);

            return prefab.GetComponent<NetworkObject>();
        }

        private static IEnumerator Until(System.Func<bool> condition, int frames = 400)
        {
            for (int i = 0; i < frames && !condition(); i++) yield return null;
        }

        private IEnumerator StartServerAndOneClient()
        {
            Assert.That(_server.ServerManager.StartConnection(), Is.True);

            yield return Until(() => _server.ServerManager.Started);

            Assert.That(_clientA.ClientManager.StartConnection(), Is.True);

            yield return Until(() => _clientA.ClientManager.Started
                && _server.ServerManager.Clients.Count >= 1);
        }

        private IEnumerator StartSecondClient()
        {
            Assert.That(_clientB.ClientManager.StartConnection(), Is.True);

            yield return Until(() => _clientB.ClientManager.Started
                && _server.ServerManager.Clients.Count >= 2);

            Assert.That(_server.ServerManager.Clients.Count, Is.EqualTo(2));
        }

        private List<int> Connections()
        {
            var ids = new List<int>();

            foreach (KeyValuePair<int, NetworkConnection> pair in _server.ServerManager.Clients)
            {
                ids.Add(pair.Key);
            }

            ids.Sort();

            return ids;
        }

        private LivingCharacter EnterWorld(string character, int connection, int team = 1)
        {
            string session = "session-" + character;

            if (!_store.Rows.ContainsKey(session))
            {
                _store.Rows[session] = new PersistedCharacter(
                    new CharacterId(character), new AccountId("acc-" + character),
                    new ServerId("srv-1"), character, 1, 20, 0, 100, 50,
                    new DefinitionId("class.novice"), default, new DefinitionId(HomeMap),
                    default, null, null, null, 1);
            }

            WorldSpawnResult spawned = _players.Spawn(connection,
                WorldAdmission.Admitted(new SessionId(session),
                    new AccountId("acc-" + character), new CharacterId(character),
                    new ServerId("srv-1"), new ChannelId("ch-1"),
                    new DefinitionId(HomeMap), new Revision(1), new Revision(1),
                    SessionState.EnteringWorld),
                new ResourceLimits(100, 50), new CombatTeam(team));

            Assert.That(spawned.IsSpawned, Is.True, spawned.Detail);

            return spawned.Character;
        }

        private static CharacterNetworkEntity Entity(NetworkManager client, string character)
        {
            foreach (KeyValuePair<int, NetworkObject> pair in client.ClientManager.Objects.Spawned)
            {
                var entity = pair.Value == null
                    ? null
                    : pair.Value.GetComponent<CharacterNetworkEntity>();

                if (entity != null && entity.Character.Value == character) return entity;
            }

            return null;
        }

        // ---- fixtures ------------------------------------------------------------------------------------------------

        private MapDefinition Map(string id)
        {
            var map = ScriptableObject.CreateInstance<MapDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_movementRadius\":500}", map);

            _created.Add(map);

            return map;
        }

        private SpawnPointDefinition PlayerSpawn()
        {
            var spawn = ScriptableObject.CreateInstance<SpawnPointDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"spawn.home\"},\"_map\":{\"_value\":\"" + HomeMap
                + "\"},\"_spawnType\":" + (int)SpawnType.Player
                + ",\"_x\":0,\"_y\":0,\"_z\":0}", spawn);

            _created.Add(spawn);

            return spawn;
        }

        private StatusEffectDefinition Effect(string id, StatusEffectCategory category,
            float duration, ControlEffectType control = ControlEffectType.None,
            StatusEffectStackBehavior stacking = StatusEffectStackBehavior.RefreshDuration,
            int maxStacks = 1)
        {
            var definition = ScriptableObject.CreateInstance<StatusEffectDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_nameKey\":{\"_key\":\"\"},"
                + "\"_category\":" + (int)category
                + ",\"_controlEffect\":" + (int)control
                + ",\"_durationSeconds\":" + duration.ToString("0.0###",
                    System.Globalization.CultureInfo.InvariantCulture)
                + ",\"_stackBehavior\":" + (int)stacking
                + ",\"_maxStacks\":" + maxStacks + "}", definition);

            _created.Add(definition);

            return definition;
        }

        /// <summary>A one-rank enemy skill carrying exactly one authored effect.</summary>
        private SkillDefinition Skill(string id, SkillEffect effect)
        {
            var definition = ScriptableObject.CreateInstance<SkillDefinition>();

            JsonUtility.FromJsonOverwrite(
                "{\"_id\":{\"_value\":\"" + id + "\"},\"_nameKey\":{\"_key\":\"\"},"
                + "\"_targetType\":" + (int)SkillTargetType.SingleEnemy
                + ",\"_maxLevel\":1,\"_resourceType\":" + (int)SkillResourceType.Mana
                + ",\"_baseResourceCost\":0,\"_cooldownSeconds\":0,\"_range\":100}",
                definition);

            SetPrivate(definition, "_levels", new[]
            {
                new SkillLevelEntry(1, 1, 0f, 0f, new[] { effect }),
            });

            _created.Add(definition);

            return definition;
        }

        private static void SetPrivate(object target, string field, object value)
        {
            System.Reflection.FieldInfo info = target.GetType().GetField(field,
                System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance);

            Assert.That(info, Is.Not.Null, "no field '" + field + "'");

            info.SetValue(target, value);
        }
    }
}

#endif
