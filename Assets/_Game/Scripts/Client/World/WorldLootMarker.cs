using UnityEngine;

namespace ChibiFantasy.Client.World
{
    /// <summary>
    /// The pile a clickable object stands for.
    /// </summary>
    /// <remarks>
    /// <b>A label, not a pile.</b> It carries the two values a pickup request names -- which
    /// pile and which slot -- and nothing else. What is in the pile, whether this player may
    /// have it, and whether they are close enough are all the server's, and none of them is
    /// readable from here.
    ///
    /// <b>Always compiled, unlike the thing that creates it.</b> The visualiser that builds
    /// placeholder objects is development-only, because this project has no loot art. This
    /// marker is not, so production art can carry it the day there is any, without the
    /// pointer input needing to know which kind of build it is running in.
    /// </remarks>
    public sealed class WorldLootMarker : MonoBehaviour
    {
        /// <summary>Which pile. The id a pickup request names.</summary>
        public string LootId { get; set; }

        /// <summary>Which slot within the pile.</summary>
        public int Index { get; set; }
    }
}
