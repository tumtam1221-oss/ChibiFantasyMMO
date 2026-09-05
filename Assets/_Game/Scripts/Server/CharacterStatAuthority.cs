using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;

namespace ChibiFantasy.Server
{
    /// <summary>
    /// Keeps every live character's effective stats up to date.
    /// </summary>
    /// <remarks>
    /// <b>It computes nothing.</b> Every number below comes out of
    /// <see cref="DerivedStatsCalculator"/>, which stays the only thing in this project that
    /// turns base stats and modifiers into derived ones. What is here is the part the
    /// calculator deliberately cannot do: knowing <em>when</em> to run, and gathering the
    /// modifiers that are currently in force. A second calculator would have been the easy
    /// mistake and would have started disagreeing about percent stacking within a week.
    ///
    /// <b>The gap this closes.</b> The calculator was only ever run at character creation and
    /// by the consistency validator. A live character's combatant was built with a null
    /// derived block and ceilings handed in once at spawn, so equipping a sword changed the
    /// authoritative attack stat not at all, and a strength buff changed nothing anywhere.
    /// <see cref="CharacterCombatant.SetLimits"/> was already the seam for replacing both;
    /// nothing called it.
    ///
    /// <b>Modifiers are collected, never invented.</b> Equipment -- with its enhancement,
    /// its enchants and its cards -- comes from <c>CharacterEquipmentState.CollectModifiers</c>
    /// through <c>EquipmentModifierResolver</c>; status effects come from
    /// <c>StatusEffectRuntimeState.CollectModifiers</c>. Both already existed and both are
    /// asked rather than reimplemented, so stacking order is decided in exactly one place.
    ///
    /// <b>It recomputes on change, not on a schedule.</b> The inputs are hashed into a small
    /// signature and compared; a status whose only difference is that its timer moved is not
    /// a different set of modifiers and produces no work. That distinction is the whole
    /// reason a buff with a countdown does not cost a recomputation sixty times a second.
    /// </remarks>
    public sealed class CharacterStatAuthority
    {
        private readonly WorldCharacterRegistry _characters;
        private readonly DerivedStatsCalculator _calculator = new DerivedStatsCalculator();

        private readonly IReadOnlyList<DerivedStatFormulaDefinition> _formulas;
        private readonly IDefinitionRegistry<StatDefinition> _stats;
        private readonly IDefinitionRegistry<StatusEffectDefinition> _effects;
        private readonly EquipmentModifierResolver.Context _equipment;

        private readonly DefinitionId _maxHealth;
        private readonly DefinitionId _maxMana;

        /// <summary>What each character's inputs looked like when it was last computed.</summary>
        private readonly Dictionary<string, long> _signatures = new Dictionary<string, long>();

        private readonly List<StatModifier> _modifiers = new List<StatModifier>();

        /// <param name="characters">The live characters this keeps current.</param>
        /// <param name="formulas">Authored derived-stat formulas, evaluated in order.</param>
        /// <param name="stats">Stat content, for the clamps the calculator applies.</param>
        /// <param name="effects">Status content, so an applied effect's modifiers resolve.</param>
        /// <param name="equipment">
        /// Item, rarity, enhancement and card content. Supplied once; a world with none
        /// simply contributes no equipment modifiers rather than guessing at them.
        /// </param>
        /// <param name="maxHealthStat">Which derived stat is the health ceiling.</param>
        /// <param name="maxManaStat">Which derived stat is the mana ceiling.</param>
        public CharacterStatAuthority(WorldCharacterRegistry characters,
            IReadOnlyList<DerivedStatFormulaDefinition> formulas,
            IDefinitionRegistry<StatDefinition> stats,
            IDefinitionRegistry<StatusEffectDefinition> effects,
            EquipmentModifierResolver.Context equipment,
            DefinitionId maxHealthStat, DefinitionId maxManaStat,
            IDefinitionRegistry<DevilFruitDefinition> devilFruits = null,
            IDefinitionRegistry<SkillDefinition> skills = null)
        {
            _characters = characters;
            _formulas = formulas ?? System.Array.Empty<DerivedStatFormulaDefinition>();
            _stats = stats;
            _effects = effects;
            _equipment = equipment;
            _maxHealth = maxHealthStat;
            _maxMana = maxManaStat;
            _devilFruits = devilFruits;
            _skills = skills;
        }

        /// <summary>Authored fruits. Null in a world with no fruit content.</summary>
        private readonly IDefinitionRegistry<DevilFruitDefinition> _devilFruits;

        /// <summary>Authored skills, which a fruit's abilities are named from.</summary>
        private readonly IDefinitionRegistry<SkillDefinition> _skills;

        /// <summary>How many times stats have actually been recomputed.</summary>
        /// <remarks>For the test that proves a countdown does not cause work. A number that
        /// grows with the frame rate is the defect this exists to make visible.</remarks>
        public int Recomputations { get; private set; }

        /// <summary>How many modifiers the last recomputation gathered.</summary>
        public int LastModifierCount { get; private set; }

        /// <summary>
        /// Brings every live character up to date, recomputing only what changed.
        /// </summary>
        /// <remarks>Cheap to call often: for an unchanged character it is a dictionary
        /// lookup and an integer comparison. Calling it every world tick is intended; the
        /// arithmetic behind it happens only when an input actually moved.</remarks>
        public int RefreshAll()
        {
            if (_characters == null) return 0;

            IReadOnlyList<LivingCharacter> all = _characters.All();

            var changed = 0;

            for (int i = 0; i < all.Count; i++)
            {
                if (Refresh(all[i])) changed++;
            }

            return changed;
        }

        /// <summary>Recomputes one character's stats, if its inputs have moved.</summary>
        /// <returns>Whether anything was actually recomputed.</returns>
        public bool Refresh(LivingCharacter character)
        {
            if (!CanCompute(character)) return false;

            long signature = SignatureOf(character);

            string key = character.Character.Value;

            if (_signatures.TryGetValue(key, out long last) && last == signature) return false;

            _signatures[key] = signature;

            Recompute(character);

            return true;
        }

        /// <summary>
        /// Recomputes regardless of whether the inputs moved.
        /// </summary>
        /// <remarks>What a fresh spawn uses: a character that has never been computed has no
        /// previous signature to differ from, and its combatant is still carrying the empty
        /// derived block it was constructed with.</remarks>
        public bool Force(LivingCharacter character)
        {
            if (!CanCompute(character)) return false;

            _signatures[character.Character.Value] = SignatureOf(character);

            Recompute(character);

            return true;
        }

        /// <summary>Forgets a character, so a reconnect is computed rather than skipped.</summary>
        public bool Forget(CharacterId character)
        {
            return character.IsValid && _signatures.Remove(character.Value);
        }

        /// <summary>
        /// The modifiers currently in force on a character, in the order they are applied.
        /// </summary>
        /// <remarks>Exposed because a tooltip and a test both want to see the composition
        /// without recomputing it. The list is rebuilt per call and handed out by value;
        /// nothing outside can edit what the next recomputation will use.</remarks>
        public List<StatModifier> ModifiersOf(LivingCharacter character)
        {
            var into = new List<StatModifier>();

            Gather(character, into);

            return into;
        }

        // ---- the work ----------------------------------------------------------------------

        private bool CanCompute(LivingCharacter character)
        {
            return character?.Domain?.Stats != null && _stats != null && _formulas.Count > 0;
        }

        /// <summary>
        /// Base stats plus every modifier in force, through the one calculator.
        /// </summary>
        /// <remarks>
        /// <b>The ceilings come out of the same result.</b> <c>ResourceLimits.From</c> reads
        /// the maximum-health and maximum-mana stats the calculator just produced, so a
        /// buff that raises maximum health raises it by the authored formula rather than by
        /// a second rule written here.
        ///
        /// <b>Clamping is the resource state's, unchanged.</b>
        /// <see cref="CharacterCombatant.SetLimits"/> hands the new ceilings to
        /// <c>CharacterResourceState.ClampTo</c>, whose existing policy is to leave current
        /// health alone unless it now exceeds the maximum. Losing a maximum-health buff
        /// therefore cannot leave a character above their ceiling, and does not heal or hurt
        /// anybody who was below it.
        /// </remarks>
        private void Recompute(LivingCharacter character)
        {
            _modifiers.Clear();

            Gather(character, _modifiers);

            DerivedStatsResult derived = _calculator.Calculate(character.Domain.Stats,
                _formulas, _stats, _modifiers);

            character.Combatant.SetLimits(
                ResourceLimits.From(derived, _maxHealth, _maxMana), derived);

            LastModifierCount = _modifiers.Count;

            Recomputations++;
        }

        /// <summary>Every modifier source this world actually supports, in one list.</summary>
        private void Gather(LivingCharacter character, List<StatModifier> into)
        {
            if (character == null || into == null) return;

            // Equipment, and everything hanging off it: enhancement steps, enchant stones
            // and socketed cards all come back through the one resolver.
            if (character.Equipment != null && _equipment.IsUsable)
            {
                character.Equipment.CollectModifiers(_equipment, into);
            }

            // Status effects, stacked exactly as the runtime already decides.
            if (character.Status != null && _effects != null)
            {
                character.Status.CollectModifiers(_effects, into);
            }

            // The Devil Fruit, through Phase 12's own service rather than by reading the
            // definition here. Its modifiers join the same list equipment and status use,
            // so the one calculator below sees a character's whole self at once and no
            // second arithmetic exists to disagree with it.
            if (character.DevilFruit != null && _devilFruits != null)
            {
                DevilFruitService.CollectModifiers(character.DevilFruit,
                    new DevilFruitService.Context(_devilFruits, character.Status, _effects,
                        _skills, character.Owner),
                    into);
            }
        }

        /// <summary>
        /// A cheap fingerprint of everything that would change the answer.
        /// </summary>
        /// <remarks>
        /// <b>Deliberately not the status runtime's revision.</b> That advances on every tick
        /// with any effect on it, including a tick where nothing moved, so using it would
        /// recompute the whole stat block sixty times a second for anybody holding a buff.
        /// What is hashed instead is what actually feeds the calculator: which effects are
        /// applied and how many stacks each has. A shrinking timer changes no modifier and
        /// changes nothing here.
        ///
        /// <b>Equipment and base stats come in by revision</b>, which those states already
        /// advance on real change and only on real change.
        /// </remarks>
        private static long SignatureOf(LivingCharacter character)
        {
            long hash = 17;

            hash = hash * 31 + character.Domain.Stats.Revision.Value;

            if (character.Equipment != null)
            {
                hash = hash * 31 + character.Equipment.Revision.Value;
            }

            if (character.Status != null)
            {
                IReadOnlyList<ActiveStatusEffect> active = character.Status.Active;

                hash = hash * 31 + active.Count;

                for (int i = 0; i < active.Count; i++)
                {
                    string id = active[i].Effect.Value;

                    hash = hash * 31 + (id == null ? 0 : id.GetHashCode());
                    hash = hash * 31 + active[i].Stacks;
                }
            }

            // Which fruit, by revision rather than by id: activation is the only thing that
            // moves it, so a quiet tick cannot make this differ and eating one cannot fail
            // to. Nothing else about the fruit can change while it is owned.
            if (character.DevilFruit != null)
            {
                hash = hash * 31 + character.DevilFruit.Revision.Value;
            }

            return hash;
        }
    }
}
