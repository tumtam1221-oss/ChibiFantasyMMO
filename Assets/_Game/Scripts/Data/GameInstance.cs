using System;
using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// Base for runtime, player-owned state.
    /// </summary>
    /// <remarks>
    /// Inheritance is used rather than composition because every instance carries exactly
    /// the same four identity members with the same rules, and a shared base lets the
    /// revision counter be advanced in one place that subclasses cannot forget.
    ///
    /// Plain serializable C#: not a MonoBehaviour, not a NetworkBehaviour, not a
    /// ScriptableObject. It holds no gameplay behaviour, and it references definitions by
    /// <see cref="DefinitionId"/> rather than by object, so an instance never pins a
    /// ScriptableObject in memory and stays valid across content patches and asset
    /// reimports.
    ///
    /// Mutating state is deliberately explicit and always advances the revision. Nothing
    /// here is authoritative: the client is untrusted, and a server must validate every
    /// field it receives before accepting it.
    /// </remarks>
    [Serializable]
    public abstract class GameInstance : IGameInstance
    {
        [SerializeField] private InstanceId _instanceId;
        [SerializeField] private DefinitionId _definitionId;
        [SerializeField] private OwnerId _owner;
        [SerializeField] private Revision _revision;

        /// <summary>Exists for deserializers, which construct before populating.</summary>
        /// <remarks>An instance created this way is not valid until deserialization fills
        /// it in. Prefer the parameterised constructor in code.</remarks>
        protected GameInstance()
        {
        }

        protected GameInstance(InstanceId instanceId, DefinitionId definitionId, OwnerId owner)
        {
            if (!instanceId.IsValid)
            {
                throw new ArgumentException("An instance requires a valid identity.", nameof(instanceId));
            }

            if (!definitionId.IsValid)
            {
                throw new ArgumentException(
                    "An instance requires the definition it is a copy of.", nameof(definitionId));
            }

            _instanceId = instanceId;
            _definitionId = definitionId;
            _owner = owner;
            _revision = Revision.Initial;
        }

        public InstanceId InstanceId => _instanceId;

        public DefinitionId DefinitionId => _definitionId;

        public OwnerId Owner => _owner;

        public Revision Revision => _revision;

        /// <summary>
        /// Reassigns ownership and advances the revision.
        /// </summary>
        /// <remarks>
        /// State assignment only. Whether a transfer is permitted, and any trade, mail or
        /// drop flow around it, is server-authoritative logic that lives elsewhere.
        /// </remarks>
        public void SetOwner(OwnerId owner)
        {
            _owner = owner;
            AdvanceRevision();
        }

        /// <summary>Advances the revision. Call after any mutation of subclass state.</summary>
        protected void AdvanceRevision()
        {
            _revision = _revision.Next();
        }
    }
}
