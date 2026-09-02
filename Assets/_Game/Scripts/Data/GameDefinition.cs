using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>
    /// Base type for every authored, static content definition.
    /// </summary>
    /// <remarks>
    /// Holds identity only. Concrete definitions (items, skills, monsters, quests, maps
    /// and so on) derive from this and add their own authored fields; none exist yet.
    ///
    /// Definitions are static content and are loaded identically on client and server,
    /// so nothing here is authoritative state.
    /// </remarks>
    public abstract class GameDefinition : ScriptableObject, IDefinition
    {
        [SerializeField] private DefinitionId _id;

        public DefinitionId Id => _id;
    }
}
