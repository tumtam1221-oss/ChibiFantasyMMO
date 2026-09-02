using System;
using System.Collections.Generic;
using ChibiFantasy.Core;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// One lookup spanning several typed registries.
    /// </summary>
    /// <remarks>
    /// The implementation <see cref="IDefinitionLookup"/> was written for in 04.6 and never
    /// had. Reference checking is inherently cross-type -- a quest names an item, a monster
    /// and a map, a skill names a class, a job and a status effect -- so a validator cannot
    /// take any single typed registry. This holds several and answers for all of them.
    ///
    /// Order matters only for cost, not for the answer: an id resolves if any registry has
    /// it. Lookups are read-only, so composing registries never lets a caller alter one.
    /// </remarks>
    public sealed class CompositeDefinitionLookup : IDefinitionLookup
    {
        private readonly IDefinitionLookup[] _lookups;

        public CompositeDefinitionLookup(params IDefinitionLookup[] lookups)
        {
            if (lookups == null)
            {
                throw new ArgumentNullException(nameof(lookups));
            }

            var collected = new List<IDefinitionLookup>(lookups.Length);

            for (int i = 0; i < lookups.Length; i++)
            {
                if (lookups[i] != null)
                {
                    collected.Add(lookups[i]);
                }
            }

            _lookups = collected.ToArray();
        }

        /// <summary>How many registries are being consulted.</summary>
        public int Count => _lookups.Length;

        public bool Contains(DefinitionId id)
        {
            for (int i = 0; i < _lookups.Length; i++)
            {
                if (_lookups[i].Contains(id))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
