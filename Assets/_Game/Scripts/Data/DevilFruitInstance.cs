using System;
using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// Where an owned Devil Fruit currently stands.
    /// </summary>
    /// <remarks>
    /// Needed because <see cref="DevilFruitDefinition.Usage"/> already distinguishes fruits
    /// that are consumed from ones that are equipped or toggled. Without a per-copy state
    /// there would be no way to represent a player who has eaten one, so this is recorded
    /// state rather than invented gameplay.
    /// </remarks>
    public enum DevilFruitState
    {
        /// <summary>Held, not yet used.</summary>
        Owned = 0,

        /// <summary>Permanently consumed by its owner.</summary>
        Consumed = 1,

        /// <summary>Equipped and active.</summary>
        Equipped = 2
    }

    /// <summary>
    /// A player's actual owned Devil Fruit.
    /// </summary>
    /// <remarks>
    /// Records which fruit, whose it is and whether it has been used. Nothing else.
    ///
    /// No passive or active ability, no silence, debuff or immunity handling, no visual or
    /// sound effect. Those are described by the definition and executed by
    /// server-authoritative gameplay. Acquisition, including the intended ultra-rare drop,
    /// is a loot concern and appears nowhere in this layer.
    /// </remarks>
    [Serializable]
    public sealed class DevilFruitInstance : GameInstance
    {
        [SerializeField] private DevilFruitState _state;

        /// <summary>Exists for deserializers.</summary>
        public DevilFruitInstance()
        {
        }

        public DevilFruitInstance(InstanceId instanceId, DefinitionId devilFruitDefinitionId, OwnerId owner)
            : this(instanceId, devilFruitDefinitionId, owner, DevilFruitState.Owned)
        {
        }

        public DevilFruitInstance(InstanceId instanceId, DefinitionId devilFruitDefinitionId, OwnerId owner,
            DevilFruitState state)
            : base(instanceId, devilFruitDefinitionId, owner)
        {
            _state = state;
        }

        public DevilFruitState State => _state;

        /// <summary>
        /// Records a new state and advances the revision.
        /// </summary>
        /// <remarks>
        /// Applies no rules. Whether a transition is legal, such as whether a consumed fruit
        /// may be equipped, depends on the fruit's authored
        /// <see cref="DevilFruitDefinition.Usage"/> and is decided by the server.
        /// </remarks>
        public void SetState(DevilFruitState state)
        {
            _state = state;
            AdvanceRevision();
        }
    }
}
