using System;
using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>What using an item is broadly for.</summary>
    /// <remarks>
    /// Closed technical category, like <see cref="EquipmentSlot"/> and
    /// <see cref="StatusEffectCategory"/>: each value gates a different validation path and
    /// a different UI affordance. It is the item's authored <em>classification</em>; what
    /// actually happens is in <see cref="ItemDefinition.UseEffects"/>.
    ///
    /// Both exist because they answer different questions. The classification is what a
    /// tooltip and a context menu need before any effect is inspected, and what warp
    /// validation keys on -- an item must declare itself a warp before it is allowed to
    /// move a character. The effects are the payload. They are not allowed to disagree:
    /// the use service refuses an item whose effects do not match what it declared, which
    /// turns the redundancy into a checked invariant instead of a chance to drift.
    /// </remarks>
    public enum ItemUseType
    {
        /// <summary>Not usable. The default, so an unconfigured item does nothing.</summary>
        None = 0,

        /// <summary>Restores a resource. See <see cref="ItemEffectKind.RestoreResource"/>.</summary>
        Recovery = 1,

        /// <summary>Applies an authored status effect for a duration.</summary>
        Buff = 2,

        /// <summary>Moves the character to an authored town.</summary>
        WarpTown = 3
    }

    /// <summary>Who an item acts on.</summary>
    /// <remarks>Only <see cref="Self"/> is executable today. The field exists so authored
    /// content does not have to be rewritten when party and target items arrive; anything
    /// else is refused rather than silently treated as self.</remarks>
    public enum ItemUseTarget
    {
        Self = 0,
        Ally = 1,
        Enemy = 2
    }

    /// <summary>Which pool a restore effect fills.</summary>
    /// <remarks>
    /// Closed technical category naming the two fields <c>CharacterResourceState</c>
    /// actually has. It is not content: an authored id here would let a designer name a
    /// pool that has nowhere to go.
    /// </remarks>
    public enum ItemResource
    {
        None = 0,
        Health = 1,
        Mana = 2
    }

    /// <summary>One thing an item does when used.</summary>
    /// <remarks>
    /// Closed technical category: each value is a different execution path with different
    /// validation. Adding one is a code change by design, because a value nothing executes
    /// would be authored content that silently does nothing.
    /// </remarks>
    public enum ItemEffectKind
    {
        None = 0,

        /// <summary>Fills <see cref="ItemUseEffect.Resource"/> by
        /// <see cref="ItemUseEffect.Amount"/> and/or <see cref="ItemUseEffect.Percent"/>.</summary>
        RestoreResource = 1,

        /// <summary>Grants <see cref="ItemUseEffect.StatusEffect"/>.</summary>
        ApplyStatusEffect = 2,

        /// <summary>Sends the character to <see cref="ItemUseEffect.DestinationMap"/>.</summary>
        WarpToMap = 3
    }

    /// <summary>
    /// One authored effect of using an item.
    /// </summary>
    /// <remarks>
    /// <b>Flat on purpose.</b> Every field is a primitive, an enum or a
    /// <see cref="DefinitionId"/>, so one row of a future <c>item_use_effect</c> table maps
    /// onto one of these with no interpretation: item id, ordinal, kind, resource, amount,
    /// percent, status effect id, duration, destination map id. A polymorphic effect
    /// hierarchy would read better in C# and would not survive the trip through a database
    /// and a PHP endpoint.
    ///
    /// <b>Unused fields are meant to be empty.</b> A restore effect leaves
    /// <see cref="StatusEffect"/> and <see cref="DestinationMap"/> invalid; a warp leaves
    /// <see cref="Amount"/> at zero. Which fields matter is decided by
    /// <see cref="Kind"/>, and the use service refuses an effect whose required fields are
    /// missing rather than treating a blank as a default.
    ///
    /// Nothing here knows any item, stat, status effect or map by name.
    /// </remarks>
    [Serializable]
    public struct ItemUseEffect
    {
        [SerializeField] private ItemEffectKind _kind;

        [Tooltip("Which pool a RestoreResource effect fills.")]
        [SerializeField] private ItemResource _resource;

        [Tooltip("Flat amount. For RestoreResource, points restored.")]
        [SerializeField] private int _amount;

        [Tooltip("Fraction of the maximum, 0..1, added to Amount. Zero means flat only.")]
        [SerializeField] private float _percent;

        [Tooltip("StatusEffectDefinition granted by an ApplyStatusEffect effect.")]
        [SerializeField] private DefinitionId _statusEffect;

        [Tooltip("Seconds. Zero means use the status effect's own authored duration.")]
        [SerializeField] private float _durationSeconds;

        [Tooltip("MapDefinition a WarpToMap effect travels to.")]
        [SerializeField] private DefinitionId _destinationMap;

        public ItemUseEffect(ItemEffectKind kind, ItemResource resource = ItemResource.None,
            int amount = 0, float percent = 0f, DefinitionId statusEffect = default,
            float durationSeconds = 0f, DefinitionId destinationMap = default)
        {
            _kind = kind;
            _resource = resource;
            _amount = amount;
            _percent = percent;
            _statusEffect = statusEffect;
            _durationSeconds = durationSeconds;
            _destinationMap = destinationMap;
        }

        public ItemEffectKind Kind => _kind;

        public ItemResource Resource => _resource;

        /// <summary>Flat magnitude. Never negative in authored content; a negative is refused.</summary>
        public int Amount => _amount;

        /// <summary>Fraction of the relevant maximum, added to <see cref="Amount"/>.</summary>
        public float Percent => _percent;

        /// <summary>Reference to a <see cref="StatusEffectDefinition"/>.</summary>
        public DefinitionId StatusEffect => _statusEffect;

        /// <summary>Authored override. Zero defers to the status effect's own duration.</summary>
        public float DurationSeconds => _durationSeconds;

        /// <summary>Reference to a <see cref="MapDefinition"/>.</summary>
        public DefinitionId DestinationMap => _destinationMap;

        public override string ToString()
        {
            switch (_kind)
            {
                case ItemEffectKind.RestoreResource:
                    return _kind + " " + _resource + " +" + _amount;
                case ItemEffectKind.ApplyStatusEffect:
                    return _kind + " " + _statusEffect;
                case ItemEffectKind.WarpToMap:
                    return _kind + " " + _destinationMap;
                default:
                    return _kind.ToString();
            }
        }
    }
}
