using System;
using System.Collections.Generic;
using ChibiFantasy.Core;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// Dictionary-backed <see cref="IDefinitionRegistry{T}"/> built from a fixed set of
    /// definitions.
    /// </summary>
    /// <remarks>
    /// Validates eagerly and fails fast. A duplicate or invalid identity is a content
    /// authoring bug; accepting it silently (last-wins) would surface as a defect far
    /// from its cause.
    ///
    /// <see cref="All"/> preserves construction order, so enumeration is deterministic.
    /// </remarks>
    public sealed class DefinitionRegistry<T> : IDefinitionRegistry<T> where T : IDefinition
    {
        private readonly Dictionary<DefinitionId, T> _byId;
        private readonly List<T> _all;

        public DefinitionRegistry(IEnumerable<T> definitions)
        {
            if (definitions == null)
            {
                throw new ArgumentNullException(nameof(definitions));
            }

            _byId = new Dictionary<DefinitionId, T>();
            _all = new List<T>();

            foreach (T definition in definitions)
            {
                if (ReferenceEquals(definition, null))
                {
                    throw new ArgumentException(
                        "Registry cannot contain a null definition.", nameof(definitions));
                }

                DefinitionId id = definition.Id;

                if (!id.IsValid)
                {
                    throw new ArgumentException(
                        "Definition has an invalid (null, empty or whitespace) id.",
                        nameof(definitions));
                }

                if (_byId.ContainsKey(id))
                {
                    throw new ArgumentException(
                        $"Duplicate definition id '{id}'.", nameof(definitions));
                }

                _byId.Add(id, definition);
                _all.Add(definition);
            }
        }

        public bool TryGet(DefinitionId id, out T definition)
        {
            return _byId.TryGetValue(id, out definition);
        }

        public bool Contains(DefinitionId id)
        {
            return _byId.ContainsKey(id);
        }

        public IReadOnlyList<T> All => _all;
    }
}
