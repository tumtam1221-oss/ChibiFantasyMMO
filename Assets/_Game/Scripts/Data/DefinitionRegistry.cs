using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ChibiFantasy.Core;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// Dictionary-backed <see cref="IDefinitionRegistry{T}"/>.
    /// </summary>
    /// <remarks>
    /// Validates eagerly and fails fast. A duplicate or invalid identity is a content
    /// authoring bug; accepting it silently, last-wins, would surface as a defect far from
    /// its cause. Registration never overwrites an existing entry.
    ///
    /// Two registration paths exist because they serve different callers.
    /// <see cref="Register"/> throws, which is what code that knows its content is sound
    /// wants. <see cref="TryRegister"/> reports instead, which is what a bulk loader
    /// wants when it needs to collect every problem in a content set rather than stop at
    /// the first.
    ///
    /// <b>Identity scope.</b> Uniqueness is enforced within one registry, not globally.
    /// The registry is generic, so the caller chooses the scope: a
    /// DefinitionRegistry&lt;ItemDefinition&gt; and a DefinitionRegistry&lt;SkillDefinition&gt;
    /// may each hold an id "fireball" without conflict, while a
    /// DefinitionRegistry&lt;GameDefinition&gt; spanning both would reject the second. This
    /// is the rule the existing architecture already implied, not a new one.
    ///
    /// <see cref="All"/> preserves insertion order, so enumeration is deterministic.
    ///
    /// The registry holds definition references because it is content infrastructure.
    /// Instances must not; they hold a <see cref="DefinitionId"/> and come here to resolve
    /// it, which is what keeps persisted state valid across content patches.
    /// </remarks>
    public sealed class DefinitionRegistry<T> : IDefinitionRegistry<T> where T : IDefinition
    {
        private readonly Dictionary<DefinitionId, T> _byId = new Dictionary<DefinitionId, T>();
        private readonly List<T> _all = new List<T>();
        private readonly ReadOnlyCollection<T> _allReadOnly;

        /// <summary>Creates an empty registry for incremental registration.</summary>
        public DefinitionRegistry()
        {
            _allReadOnly = new ReadOnlyCollection<T>(_all);
        }

        /// <summary>Creates a registry from a known set, rejecting the whole set on any fault.</summary>
        public DefinitionRegistry(IEnumerable<T> definitions)
        {
            _allReadOnly = new ReadOnlyCollection<T>(_all);

            if (definitions == null)
            {
                throw new ArgumentNullException(nameof(definitions));
            }

            foreach (T definition in definitions)
            {
                Add(definition, nameof(definitions));
            }
        }

        /// <summary>Every definition in insertion order.</summary>
        /// <remarks>A read-only view over the backing list, not the list itself, so a caller
        /// cannot cast it back and alter registered content behind the registry.</remarks>
        public IReadOnlyList<T> All => _allReadOnly;

        public int Count => _all.Count;

        /// <summary>
        /// Adds a definition, throwing if it is null, unidentified or already present.
        /// </summary>
        public void Register(T definition)
        {
            Add(definition, nameof(definition));
        }

        /// <summary>
        /// Adds a definition, returning false instead of throwing when it is null,
        /// unidentified or already present.
        /// </summary>
        /// <remarks>An existing entry is never replaced; a rejected registration leaves the
        /// registry exactly as it was.</remarks>
        public bool TryRegister(T definition)
        {
            if (ReferenceEquals(definition, null))
            {
                return false;
            }

            DefinitionId id = definition.Id;

            if (!id.IsValid || _byId.ContainsKey(id))
            {
                return false;
            }

            _byId.Add(id, definition);
            _all.Add(definition);
            return true;
        }

        /// <summary>Removes every definition, leaving the registry reusable.</summary>
        public void Clear()
        {
            _byId.Clear();
            _all.Clear();
        }

        public bool TryGet(DefinitionId id, out T definition)
        {
            return _byId.TryGetValue(id, out definition);
        }

        public bool Contains(DefinitionId id)
        {
            return _byId.ContainsKey(id);
        }

        private void Add(T definition, string paramName)
        {
            if (ReferenceEquals(definition, null))
            {
                throw new ArgumentException("Registry cannot contain a null definition.", paramName);
            }

            DefinitionId id = definition.Id;

            if (!id.IsValid)
            {
                throw new ArgumentException(
                    "Definition has an invalid (null, empty or whitespace) id.", paramName);
            }

            if (_byId.ContainsKey(id))
            {
                throw new ArgumentException($"Duplicate definition id '{id}'.", paramName);
            }

            _byId.Add(id, definition);
            _all.Add(definition);
        }
    }
}
