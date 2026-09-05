using System.Collections.Generic;
using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// Every authored definition a running world needs, named explicitly.
    /// </summary>
    /// <remarks>
    /// <b>The production content pipeline, and deliberately the dullest one available.</b>
    /// Serialized references to assets, resolved by Unity's own build dependency walk. No
    /// <c>Resources.LoadAll</c>, no folder convention, no Addressables, no path string, no
    /// <c>AssetDatabase</c> -- every one of those either fails in a player build, silently
    /// drops content nobody referenced, or ties the game's identity to where a file happens
    /// to sit. What is referenced here is what ships, and a missing reference is a
    /// compile-time-visible empty slot rather than a runtime surprise.
    ///
    /// <b>It builds the registries that already exist.</b> Nothing here is a second lookup
    /// API: <see cref="DefinitionRegistry{T}"/> is what every service in this project already
    /// takes, and this hands them one. The catalogue's whole job is to be the list.
    ///
    /// <b>Which stat plays which role is configuration, not code.</b> A server has to know
    /// which derived stat is the health ceiling and which one the damage formula reads. Those
    /// four ids live here rather than as literals in a bootstrap, so renaming a stat is a
    /// content edit.
    ///
    /// <b>Adding content must not mean editing gameplay code.</b> A second map, a tenth
    /// monster or a whole job branch is an authored asset dropped into a list here. Nothing
    /// in Server or Gameplay names a single definition.
    /// </remarks>
    [CreateAssetMenu(menuName = "ChibiFantasy/World/World Content Catalogue",
        fileName = "WorldContentCatalogue")]
    public sealed class WorldContentCatalogue : ScriptableObject
    {
        [Header("Attributes and formulas")]
        [Tooltip("Every stat the world can compute or clamp, primary and derived.")]
        [SerializeField] private StatDefinition[] _stats = new StatDefinition[0];

        [Tooltip("How each derived stat is produced. Evaluated in order.")]
        [SerializeField] private DerivedStatFormulaDefinition[] _formulas =
            new DerivedStatFormulaDefinition[0];

        [Header("Characters")]
        [SerializeField] private ClassDefinition[] _classes = new ClassDefinition[0];

        [SerializeField] private CharacterProgressionDefinition[] _progressions =
            new CharacterProgressionDefinition[0];

        [Header("World")]
        [SerializeField] private MapDefinition[] _maps = new MapDefinition[0];

        [SerializeField] private SpawnPointDefinition[] _spawnPoints =
            new SpawnPointDefinition[0];

        [Header("Content")]
        [SerializeField] private MonsterDefinition[] _monsters = new MonsterDefinition[0];

        [SerializeField] private SkillDefinition[] _skills = new SkillDefinition[0];

        [SerializeField] private StatusEffectDefinition[] _statusEffects =
            new StatusEffectDefinition[0];

        [Tooltip("Items and equipment together: equipment is an item.")]
        [SerializeField] private ItemDefinition[] _items = new ItemDefinition[0];

        [Tooltip("Devil Fruits. Ultra-rare, permanent, one per character.")]
        [SerializeField] private DevilFruitDefinition[] _devilFruits =
            new DevilFruitDefinition[0];

        [Tooltip("What monsters leave behind. Rank gating lives on the table's entries.")]
        /// <summary>Cards a piece of equipment can be socketed with.</summary>
        /// <remarks>Shipped alongside the items rather than inside them: a card is authored
        /// content in its own right -- what it fits, what it grants -- and the item that
        /// carries it around an inventory is a separate definition, exactly as a Devil Fruit
        /// and its item are.</remarks>
        [SerializeField] private CardDefinition[] _cards = new CardDefinition[0];

        [SerializeField] private DropTableDefinition[] _dropTables =
            new DropTableDefinition[0];

        [Header("Stat roles")]
        [Tooltip("Which derived stat is the health ceiling.")]
        [SerializeField] private DefinitionId _maxHealthStat;

        [Tooltip("Which derived stat is the mana ceiling.")]
        [SerializeField] private DefinitionId _maxManaStat;

        [Tooltip("Which derived stat the damage formula reads as attack power.")]
        [SerializeField] private DefinitionId _attackStat;

        [Tooltip("Which derived stat resists physical damage.")]
        [SerializeField] private DefinitionId _defenceStat;

        [Tooltip("Which derived stat a magic skill's damage scales from.")]
        [SerializeField] private DefinitionId _magicAttackStat;

        [Tooltip("Which derived stat resists magic damage.")]
        [SerializeField] private DefinitionId _magicDefenceStat;

        [Header("World rules")]
        [Tooltip("Authored walking speed in metres per second. The movement authority's "
            + "budget, never a client's.")]
        [SerializeField] private float _walkMetresPerSecond = 4f;

        public DefinitionId MaxHealthStat => _maxHealthStat;

        public DefinitionId MaxManaStat => _maxManaStat;

        public DefinitionId AttackStat => _attackStat;

        public DefinitionId DefenceStat => _defenceStat;

        /// <summary>Which derived stat magic damage scales from.</summary>
        /// <remarks>Named here rather than in a skill: a skill authors how hard it hits and
        /// what resists it, never which of this world's stats is "magic attack".</remarks>
        public DefinitionId MagicAttackStat => _magicAttackStat;

        /// <summary>Which derived stat answers magic damage.</summary>
        /// <remarks>The one place the world says what resists a spell. Combat code reads it
        /// through <c>SkillExecutionRules</c> and names no stat itself.</remarks>
        public DefinitionId MagicDefenceStat => _magicDefenceStat;

        public float WalkMetresPerSecond => _walkMetresPerSecond <= 0f
            ? 1f
            : _walkMetresPerSecond;

        /// <summary>The formulas, in authored order, as the calculator wants them.</summary>
        public IReadOnlyList<DerivedStatFormulaDefinition> Formulas =>
            _formulas ?? System.Array.Empty<DerivedStatFormulaDefinition>();

        public DefinitionRegistry<StatDefinition> BuildStats() => Build(_stats);

        public DefinitionRegistry<ClassDefinition> BuildClasses() => Build(_classes);

        public DefinitionRegistry<CharacterProgressionDefinition> BuildProgressions() =>
            Build(_progressions);

        public DefinitionRegistry<MapDefinition> BuildMaps() => Build(_maps);

        public DefinitionRegistry<SpawnPointDefinition> BuildSpawnPoints() =>
            Build(_spawnPoints);

        public DefinitionRegistry<MonsterDefinition> BuildMonsters() => Build(_monsters);

        public DefinitionRegistry<SkillDefinition> BuildSkills() => Build(_skills);

        public DefinitionRegistry<StatusEffectDefinition> BuildStatusEffects() =>
            Build(_statusEffects);

        public DefinitionRegistry<ItemDefinition> BuildItems() => Build(_items);

        public DefinitionRegistry<DevilFruitDefinition> BuildDevilFruits() =>
            Build(_devilFruits);

        public DefinitionRegistry<CardDefinition> BuildCards() => Build(_cards);

        public DefinitionRegistry<DropTableDefinition> BuildDropTables() =>
            Build(_dropTables);

        /// <summary>
        /// Whether this catalogue describes a world that can actually run.
        /// </summary>
        /// <remarks>
        /// <b>A half-valid world must not start.</b> A server that admits a player and then
        /// cannot place them, or computes a health ceiling of nothing, is worse than a server
        /// that refused to start and said why -- the first corrupts a play session and the
        /// second costs a restart.
        ///
        /// <b>Every fault is reported, not the first.</b> An operator fixing content wants
        /// the list, not one item at a time.
        /// </remarks>
        public bool Validate(List<string> faults)
        {
            if (faults == null) return false;

            faults.Clear();

            Check(_stats, "stat", faults);
            Check(_formulas, "derived stat formula", faults);
            Check(_classes, "class", faults);
            Check(_progressions, "progression", faults);
            Check(_maps, "map", faults);
            Check(_spawnPoints, "spawn point", faults);
            Check(_monsters, "monster", faults);
            Check(_skills, "skill", faults);
            Check(_statusEffects, "status effect", faults);
            Check(_items, "item", faults);
            Check(_devilFruits, "devil fruit", faults);
            Check(_dropTables, "drop table", faults);

            // --- the four roles a world cannot run without --------------------------------
            DefinitionRegistry<StatDefinition> stats = Build(_stats);

            RequireStat(_maxHealthStat, "maximum health", stats, faults);
            RequireStat(_maxManaStat, "maximum mana", stats, faults);
            RequireStat(_attackStat, "attack", stats, faults);
            RequireStat(_defenceStat, "defence", stats, faults);
            RequireStat(_magicAttackStat, "magic attack", stats, faults);
            RequireStat(_magicDefenceStat, "magic defence", stats, faults);

            // --- a formula naming a stat nobody defined produces nothing, silently --------
            for (var i = 0; i < _formulas.Length; i++)
            {
                DerivedStatFormulaDefinition formula = _formulas[i];

                if (formula == null) continue;

                if (!stats.TryGet(formula.DerivedStat, out StatDefinition _))
                {
                    faults.Add("formula '" + formula.Id + "' produces unknown stat '"
                        + formula.DerivedStat + "'");
                }

                StatTerm[] terms = formula.Terms;

                for (var t = 0; t < terms.Length; t++)
                {
                    if (!stats.TryGet(terms[t].Source, out StatDefinition _))
                    {
                        faults.Add("formula '" + formula.Id + "' reads unknown stat '"
                            + terms[t].Source + "'");
                    }
                }
            }

            RequireFormulaFor(_maxHealthStat, faults);
            RequireFormulaFor(_maxManaStat, faults);
            RequireFormulaFor(_attackStat, faults);
            RequireFormulaFor(_defenceStat, faults);
            RequireFormulaFor(_magicAttackStat, faults);
            RequireFormulaFor(_magicDefenceStat, faults);

            // --- somewhere for a player to arrive -----------------------------------------
            DefinitionRegistry<MapDefinition> maps = Build(_maps);

            var playerSpawns = 0;

            for (var i = 0; i < _spawnPoints.Length; i++)
            {
                SpawnPointDefinition spawn = _spawnPoints[i];

                if (spawn == null) continue;

                if (!maps.TryGet(spawn.Map, out MapDefinition _))
                {
                    faults.Add("spawn '" + spawn.Id + "' is on unknown map '"
                        + spawn.Map + "'");

                    continue;
                }

                if (spawn.SpawnType == SpawnType.Player) playerSpawns++;
            }

            if (playerSpawns == 0)
            {
                faults.Add("no player spawn point: an admitted character would be refused "
                    + "entry rather than placed");
            }

            // --- a monster on a map this world does not have is unreachable ---------------
            for (var i = 0; i < _monsters.Length; i++)
            {
                MonsterDefinition monster = _monsters[i];

                if (monster == null) continue;

                DefinitionId[] allowed = monster.AllowedMaps;

                for (var m = 0; m < allowed.Length; m++)
                {
                    if (!allowed[m].IsValid) continue;

                    if (!maps.TryGet(allowed[m], out MapDefinition _))
                    {
                        faults.Add("monster '" + monster.Id + "' allows unknown map '"
                            + allowed[m] + "'");
                    }
                }
            }

            // --- a fruit naming content this world does not have grants nothing ----------
            DefinitionRegistry<SkillDefinition> skills = Build(_skills);
            DefinitionRegistry<StatusEffectDefinition> effects = Build(_statusEffects);
            DefinitionRegistry<StatDefinition> statsForFruit = stats;

            for (var i = 0; i < _devilFruits.Length; i++)
            {
                DevilFruitDefinition fruit = _devilFruits[i];

                if (fruit == null) continue;

                RequireSkill(fruit.ActiveAbility, fruit.Id, "active ability", skills, faults);
                RequireSkill(fruit.PassiveAbility, fruit.Id, "passive ability", skills, faults);

                DefinitionId[] granted = fruit.GrantedEffects;

                for (var g = 0; g < granted.Length; g++)
                {
                    if (!granted[g].IsValid) continue;

                    if (!effects.TryGet(granted[g], out StatusEffectDefinition _))
                    {
                        faults.Add("devil fruit '" + fruit.Id + "' grants unknown effect '"
                            + granted[g] + "'");
                    }
                }

                StatModifier[] modifiers = fruit.StatModifiers;

                for (var m = 0; m < modifiers.Length; m++)
                {
                    if (!modifiers[m].Stat.IsValid) continue;

                    if (!statsForFruit.TryGet(modifiers[m].Stat, out StatDefinition _))
                    {
                        faults.Add("devil fruit '" + fruit.Id + "' modifies unknown stat '"
                            + modifiers[m].Stat + "'");
                    }
                }
            }

            // --- where an ultra-rare fruit is actually allowed to come from ---------------
            //
            // A fruit that names a boss nobody authored, or a boss that is not a world boss,
            // or a table that does not contain it, is a fruit no player can ever obtain --
            // and nothing at runtime would say so. Checked here because this is the only
            // place that can see the monster, the table and the fruit at once.
            DefinitionRegistry<MonsterDefinition> monsters = Build(_monsters);
            DefinitionRegistry<DropTableDefinition> tables = Build(_dropTables);

            for (var i = 0; i < _devilFruits.Length; i++)
            {
                DevilFruitDefinition fruit = _devilFruits[i];

                if (fruit == null || !fruit.Id.IsValid) continue;

                if (fruit.SourceBoss.IsValid)
                {
                    if (!monsters.TryGet(fruit.SourceBoss, out MonsterDefinition boss))
                    {
                        faults.Add("devil fruit '" + fruit.Id + "' names unknown source boss '"
                            + fruit.SourceBoss + "'");
                    }
                    else if (boss.Rank != MonsterRank.WorldBoss)
                    {
                        faults.Add("devil fruit '" + fruit.Id + "' names '" + boss.Id
                            + "' as its source, which is a " + boss.Rank
                            + " and not a world boss");
                    }
                }

                if (!fruit.DropTable.IsValid) continue;

                if (!tables.TryGet(fruit.DropTable, out DropTableDefinition table))
                {
                    faults.Add("devil fruit '" + fruit.Id + "' names unknown drop table '"
                        + fruit.DropTable + "'");

                    continue;
                }

                // The table has to actually be able to produce the item that grants it.
                var carries = false;

                for (var e = 0; e < table.Entries.Length; e++)
                {
                    if (!Grants(table.Entries[e].Item, fruit.Id)) continue;

                    carries = true;

                    // And it has to be gated to world bosses, or an ordinary monster on the
                    // same table would be able to roll it.
                    if (table.Entries[e].MinMonsterRank != MonsterRank.WorldBoss)
                    {
                        faults.Add("drop table '" + table.Id + "' offers devil fruit item '"
                            + table.Entries[e].Item + "' at rank "
                            + table.Entries[e].MinMonsterRank
                            + "; a devil fruit must be world-boss only");
                    }
                }

                if (!carries)
                {
                    faults.Add("drop table '" + table.Id + "' carries no item granting '"
                        + fruit.Id + "'");
                }
            }

            // --- an item that grants a fruit this world does not have is a dead item -------
            DefinitionRegistry<DevilFruitDefinition> fruits = Build(_devilFruits);

            for (var i = 0; i < _items.Length; i++)
            {
                ItemDefinition item = _items[i];

                if (item == null) continue;

                ItemUseEffect[] uses = item.UseEffects;

                for (var u = 0; u < uses.Length; u++)
                {
                    if (uses[u].Kind != ItemEffectKind.ConsumeDevilFruit) continue;

                    if (!uses[u].DevilFruit.IsValid)
                    {
                        faults.Add("item '" + item.Id + "' grants no devil fruit");

                        continue;
                    }

                    if (!fruits.TryGet(uses[u].DevilFruit, out DevilFruitDefinition _))
                    {
                        faults.Add("item '" + item.Id + "' grants unknown devil fruit '"
                            + uses[u].DevilFruit + "'");
                    }
                }
            }

            return faults.Count == 0;
        }

        /// <summary>Whether an item is the one that grants a given fruit.</summary>
        /// <remarks>Asked of the item's authored effects rather than of its id, so a drop
        /// table and a fruit are linked by what the item does and not by a naming
        /// convention somebody could break.</remarks>
        private bool Grants(DefinitionId item, DefinitionId fruit)
        {
            if (!item.IsValid || !fruit.IsValid) return false;

            for (var i = 0; i < _items.Length; i++)
            {
                if (_items[i] == null || _items[i].Id != item) continue;

                ItemUseEffect[] uses = _items[i].UseEffects;

                for (var u = 0; u < uses.Length; u++)
                {
                    if (uses[u].Kind == ItemEffectKind.ConsumeDevilFruit
                        && uses[u].DevilFruit == fruit)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>A fruit ability nobody authored would be an ability that does nothing.</summary>
        private static void RequireSkill(DefinitionId skill, DefinitionId fruit, string role,
            DefinitionRegistry<SkillDefinition> skills, List<string> faults)
        {
            if (!skill.IsValid) return;

            if (!skills.TryGet(skill, out SkillDefinition _))
            {
                faults.Add("devil fruit '" + fruit + "' names unknown " + role + " '"
                    + skill + "'");
            }
        }

        /// <summary>
        /// Builds a registry from an authored list.
        /// </summary>
        /// <remarks>Deterministic: authored order, first id wins on a duplicate, and a
        /// duplicate is a validation fault rather than a silent overwrite.</remarks>
        private static DefinitionRegistry<T> Build<T>(T[] source) where T : GameDefinition
        {
            var registry = new DefinitionRegistry<T>();

            if (source == null) return registry;

            var seen = new HashSet<string>();

            for (var i = 0; i < source.Length; i++)
            {
                if (source[i] == null || !source[i].Id.IsValid) continue;

                // First id wins. The registry throws on a duplicate, and a catalogue that
                // threw while being validated could never report the duplicate as a fault --
                // an operator would get an exception instead of the list of things to fix.
                if (!seen.Add(source[i].Id.Value)) continue;

                registry.Register(source[i]);
            }

            return registry;
        }

        /// <summary>Null entries, invalid ids and duplicates, reported per list.</summary>
        private static void Check<T>(T[] source, string label, List<string> faults)
            where T : GameDefinition
        {
            if (source == null) return;

            var seen = new HashSet<string>();

            for (var i = 0; i < source.Length; i++)
            {
                if (source[i] == null)
                {
                    faults.Add("empty " + label + " slot at index " + i);

                    continue;
                }

                string id = source[i].Id.Value;

                if (!source[i].Id.IsValid)
                {
                    faults.Add(label + " '" + source[i].name + "' has no id");

                    continue;
                }

                if (!seen.Add(id))
                {
                    faults.Add("duplicate " + label + " id '" + id + "'");
                }
            }
        }

        private static void RequireStat(DefinitionId stat, string role,
            DefinitionRegistry<StatDefinition> stats, List<string> faults)
        {
            if (!stat.IsValid)
            {
                faults.Add("no stat is named as " + role);

                return;
            }

            if (!stats.TryGet(stat, out StatDefinition _))
            {
                faults.Add(role + " names unknown stat '" + stat + "'");
            }
        }

        /// <summary>A role stat with no formula behind it computes as nothing at all.</summary>
        private void RequireFormulaFor(DefinitionId stat, List<string> faults)
        {
            if (!stat.IsValid) return;

            for (var i = 0; i < _formulas.Length; i++)
            {
                if (_formulas[i] != null && _formulas[i].DerivedStat == stat) return;
            }

            faults.Add("no formula produces '" + stat + "'");
        }
    }
}
