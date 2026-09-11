using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.Network;

namespace ChibiFantasy.Server
{
    /// <summary>
    /// Turns a client's "let me talk to him" into the interaction service's own answer.
    /// </summary>
    /// <remarks>
    /// <b>A door, not a second interaction system.</b> Every decision below already existed
    /// in <see cref="NpcInteractionService"/>: whether the NPC is real, whether content
    /// turned it off, whether it stands on the map the player is standing on, whether they
    /// are close enough, whether it offers what was asked for, and what that role resolves
    /// to. This adds the two things a networked player needs and a local test never did --
    /// an identity taken from the connection rather than the message, and an answer sent
    /// back to exactly one person.
    ///
    /// <b>The position it measures is the server's own.</b> <c>LivingCharacter.Location</c>
    /// is what the movement authority writes and what the combatant mirrors, so the distance
    /// checked here is the distance the server believes, never the one a client claims. A
    /// client that could interact from across the map could shop, bank and take quests
    /// without ever walking anywhere.
    ///
    /// <b>Refusals are answered, not dropped.</b> Silence is indistinguishable from a lost
    /// packet, and a player who is told nothing clicks again. Every request produces exactly
    /// one reply carrying the service's own reason.
    ///
    /// <b>Sequence numbers keep a late answer from overwriting a new one.</b> They are
    /// echoed rather than enforced: unlike loot or inventory, asking twice takes nothing and
    /// creates nothing, so a repeated request is answered rather than refused -- refusing it
    /// would break the ordinary case of a player clicking an NPC again after walking closer.
    /// </remarks>
    public sealed class CharacterNpcAuthority : ICharacterNpcRequestSink
    {
        private readonly WorldCharacterRegistry _characters;
        private readonly NpcInteractionService.Context _content;
        private readonly CharacterReplicationService _replication;

        public CharacterNpcAuthority(WorldCharacterRegistry characters,
            NpcInteractionService.Context content,
            CharacterReplicationService replication = null)
        {
            _characters = characters;
            _content = content;
            _replication = replication;
        }

        /// <summary>How many requests were handled, accepted or not. For diagnostics.</summary>
        public int Handled { get; private set; }

        /// <summary>What the last request decided.</summary>
        public NpcInteractionResult LastResult { get; private set; }

        /// <summary>What was last put on the wire, for a test that has no network.</summary>
        public NpcInteractionSnapshot LastSnapshot { get; private set; }

        public void Submit(int connectionId, string npcId, int role, long sequence)
        {
            Handled++;

            NpcInteractionSnapshot snapshot = Apply(connectionId, npcId, role, sequence);

            LastSnapshot = snapshot;

            if (_characters != null
                && _characters.TryGet(connectionId, out LivingCharacter asker))
            {
                Publish(asker, snapshot);
            }
        }

        /// <summary>Resolves the request against the world, and says what happened.</summary>
        /// <remarks>Separate from <see cref="Submit"/> so the decision can be tested without
        /// a network, which is the same split every other authority here uses.</remarks>
        public NpcInteractionSnapshot Apply(int connectionId, string npcId, int role,
            long sequence)
        {
            var wanted = new DefinitionId(npcId ?? string.Empty);

            if (_characters == null || !_content.IsUsable)
            {
                return Refuse(wanted, role, NpcInteractionRejection.MissingContext, sequence);
            }

            if (!_characters.TryGet(connectionId, out LivingCharacter asker))
            {
                // The identity is the connection's. Nothing in the request says who is
                // asking, so nobody can ask on somebody else's behalf.
                return Refuse(wanted, role, NpcInteractionRejection.MissingContext, sequence);
            }

            if (!System.Enum.IsDefined(typeof(NpcRole), role))
            {
                // A role nobody authored. Refused as "not offered" rather than crashing on
                // the cast, because that is exactly what it is from the player's side.
                return Refuse(wanted, role, NpcInteractionRejection.RoleNotOffered, sequence);
            }

            NpcInteractionResult result = NpcInteractionService.TryInteract(
                asker.Location, wanted, (NpcRole)role, _content);

            LastResult = result;

            return new NpcInteractionSnapshot
            {
                NpcId = wanted.Value ?? string.Empty,
                Role = role,
                Accepted = result.IsAccepted,
                Reason = (int)result.Reason,
                Content = result.Content.Value ?? string.Empty,
                Sequence = sequence,
            };
        }

        private NpcInteractionSnapshot Refuse(DefinitionId npc, int role,
            NpcInteractionRejection reason, long sequence)
        {
            LastResult = NpcInteractionResult.Rejected(reason, npc);

            return new NpcInteractionSnapshot
            {
                NpcId = npc.Value ?? string.Empty,
                Role = role,
                Accepted = false,
                Reason = (int)reason,
                Content = string.Empty,
                Sequence = sequence,
            };
        }

        /// <summary>Sends the answer to the one player who asked.</summary>
        /// <remarks>Reaches the player's own network object through the replication service,
        /// exactly as <c>CharacterLootAuthority</c> does. A character with no spawned object
        /// -- one that left between asking and being answered -- is simply not told, which
        /// is the only thing that can be done for somebody who is gone.</remarks>
        public bool Publish(LivingCharacter character, in NpcInteractionSnapshot snapshot)
        {
            if (character == null || _replication == null) return false;

            if (!_replication.TryGet(character.Character,
                out FishNet.Object.NetworkObject networkObject))
            {
                return false;
            }

            var entity = networkObject == null
                ? null
                : networkObject.GetComponent<CharacterNetworkEntity>();

            if (entity == null) return false;

            entity.ServerPublishNpcInteraction(snapshot);

            return true;
        }

        /// <summary>
        /// Every NPC standing on one map, for a client that needs to draw them.
        /// </summary>
        /// <remarks>Scoped to a map because an NPC somewhere else is one this player cannot
        /// reach, and listing it would only invite a request that is refused.</remarks>
        public static void NpcsOn(DefinitionId map, IDefinitionRegistry<NPCDefinition> npcs,
            List<NPCDefinition> into)
        {
            if (into == null) return;

            into.Clear();

            if (npcs == null || !map.IsValid) return;

            IReadOnlyList<NPCDefinition> all = npcs.All;

            for (var i = 0; i < all.Count; i++)
            {
                NPCDefinition npc = all[i];

                if (npc == null || !npc.Enabled) continue;

                if (npc.Map != map) continue;

                into.Add(npc);
            }
        }
    }
}
