// Development only, for the same reason the visualizer is: it exists to make an
// authoritative monster clickable while monsters have no art.
#if DEVELOPMENT_BUILD || UNITY_EDITOR

using ChibiFantasy.Network;
using UnityEngine;

namespace ChibiFantasy.Client.World
{
    /// <summary>
    /// Says which monster a development placeholder stands for.
    /// </summary>
    /// <remarks>A click hits a capsule, and a capsule is not a monster. This is the way back
    /// from one to the other, so targeting names the entity the server sent rather than
    /// guessing from a position.</remarks>
    public sealed class DevelopmentMonsterMarker : MonoBehaviour
    {
        /// <summary>The authoritative monster this placeholder is standing in for.</summary>
        public MonsterNetworkEntity Monster;
    }
}

#endif
