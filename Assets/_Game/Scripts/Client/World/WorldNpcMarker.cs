using UnityEngine;

namespace ChibiFantasy.Client.World
{
    /// <summary>
    /// Something a player can walk up to and talk to, once there is anything to say.
    /// </summary>
    /// <remarks>
    /// <b>The input path exists; the content does not yet.</b> This project authors
    /// <c>NPCDefinition</c>, <c>ShopDefinition</c> and <c>QuestDefinition</c> as content, and
    /// nothing in the world spawns an NPC or serves an interaction. Rather than invent
    /// dialogue to fill the gap, this marker exists so that a click on an NPC is already
    /// classified, already walks the character into range through the ordinary movement
    /// authority, and already has one obvious place to call an interaction from the day a
    /// real seam is wired: <see cref="Interaction"/>.
    ///
    /// <b>Nothing here talks to the server.</b> Whatever eventually fills
    /// <see cref="Interaction"/> must go through an authoritative request like every other
    /// verb in this client; a marker that opened a shop by itself would be a client deciding
    /// it had bought something.
    /// </remarks>
    public sealed class WorldNpcMarker : MonoBehaviour
    {
        /// <summary>Which NPC this stands for, as authored content names it.</summary>
        public string NpcId { get; set; }

        /// <summary>
        /// Raised once the player has walked into range.
        /// </summary>
        /// <remarks>Null today, and deliberately so: an unset callback means "arrived, and
        /// there is nothing to do yet", which is the honest state of NPC interaction in this
        /// project.</remarks>
        public System.Action<WorldNpcMarker> Interaction { get; set; }

        /// <summary>How many times a player has reached this NPC. For tests.</summary>
        public int Arrivals { get; private set; }

        /// <summary>Called by the pointer input when the character is close enough.</summary>
        public void Arrive()
        {
            Arrivals++;

            Interaction?.Invoke(this);
        }
    }
}
