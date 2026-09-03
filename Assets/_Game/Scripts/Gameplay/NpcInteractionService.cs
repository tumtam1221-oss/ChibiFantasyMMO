using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Gameplay
{
    /// <summary>Why talking to an NPC was refused.</summary>
    public enum NpcInteractionRejection
    {
        None = 0,

        /// <summary>No location state or no registry was supplied.</summary>
        MissingContext = 1,

        /// <summary>No such NPC could be resolved.</summary>
        UnknownNpc = 2,

        /// <summary>Content turned the NPC off.</summary>
        NpcDisabled = 3,

        /// <summary>The NPC is not on the map the player is standing on.</summary>
        WrongMap = 4,

        /// <summary>The player is too far away to talk.</summary>
        TooFar = 5,

        /// <summary>The NPC does not offer what was asked for.</summary>
        RoleNotOffered = 6,

        /// <summary>The role is offered but its content could not be resolved.</summary>
        RoleUnavailable = 7,

        /// <summary>The NPC is placed nowhere, so nobody can reach it.</summary>
        NpcNotPlaced = 8
    }

    /// <summary>
    /// What an NPC will do for a player.
    /// </summary>
    /// <remarks>
    /// <b>An authorisation, not an action.</b> Nothing has opened, nothing has been bought
    /// and no quest has moved. This says the player may use a role, and which content that
    /// role resolves to; the client opens the matching screen and every actual operation
    /// still goes through the service that owns it -- <c>QuestService</c>,
    /// <c>ItemContainerTransfer</c>, and so on.
    ///
    /// That split is what stops NPC interaction becoming a second path into quests or
    /// inventories.
    /// </remarks>
    public readonly struct NpcInteractionResult
    {
        private NpcInteractionResult(bool accepted, NpcInteractionRejection reason,
            DefinitionId npc, NpcRole role, DefinitionId content)
        {
            IsAccepted = accepted;
            Reason = reason;
            Npc = npc;
            Role = role;
            Content = content;
        }

        public bool IsAccepted { get; }

        public NpcInteractionRejection Reason { get; }

        public DefinitionId Npc { get; }

        /// <summary>Which role was authorised.</summary>
        public NpcRole Role { get; }

        /// <summary>
        /// What the role resolves to.
        /// </summary>
        /// <remarks>A shop id for <see cref="NpcRole.Shop"/>. Invalid for roles that need
        /// no content of their own -- storage opens the character's own container, and
        /// quests are read from the NPC's list.</remarks>
        public DefinitionId Content { get; }

        public static NpcInteractionResult Accepted(DefinitionId npc, NpcRole role,
            DefinitionId content = default)
        {
            return new NpcInteractionResult(true, NpcInteractionRejection.None, npc, role, content);
        }

        public static NpcInteractionResult Rejected(NpcInteractionRejection reason,
            DefinitionId npc = default, NpcRole role = NpcRole.Generic)
        {
            return new NpcInteractionResult(false, reason, npc, role, default);
        }

        public override string ToString()
        {
            return IsAccepted ? Npc + " -> " + Role : "rejected: " + Reason;
        }
    }

    /// <summary>
    /// Talking to an NPC.
    /// </summary>
    /// <remarks>
    /// <b>It authorises and resolves; it opens nothing.</b> Every role below returns a
    /// result the client acts on, and the operation behind that screen still goes through
    /// the service that owns it. Storage returns permission to open the container the
    /// character already has -- there is no second storage state and no NPC-owned inventory.
    ///
    /// <b>Nothing here knows an NPC.</b> No <see cref="DefinitionId"/> is compared to a
    /// literal, and there is no shopkeeper path or job-changer path: which roles an NPC
    /// offers is authored, and <see cref="NPCDefinition.HasRole"/> answers it.
    ///
    /// <b>Proximity is checked against the player's own location state.</b> A client
    /// asserting it stands next to a vendor proves nothing; this is the check a server runs.
    /// Squared distances throughout, and nothing is scanned per frame -- interaction is a
    /// request, not a poll.
    /// </remarks>
    public static class NpcInteractionService
    {
        /// <summary>Fallback reach when an NPC authors none.</summary>
        public const float DefaultInteractionRadius = 3f;

        /// <summary>Everything an interaction needs.</summary>
        public readonly struct Context
        {
            public Context(IDefinitionRegistry<NPCDefinition> npcs,
                IDefinitionRegistry<SpawnPointDefinition> spawnPoints = null,
                IDefinitionRegistry<ShopDefinition> shops = null,
                IDefinitionRegistry<QuestDefinition> quests = null)
            {
                Npcs = npcs;
                SpawnPoints = spawnPoints;
                Shops = shops;
                Quests = quests;
            }

            public IDefinitionRegistry<NPCDefinition> Npcs { get; }

            /// <summary>Where the NPC stands, for the proximity check.</summary>
            public IDefinitionRegistry<SpawnPointDefinition> SpawnPoints { get; }

            /// <summary>Needed only to open a vendor.</summary>
            public IDefinitionRegistry<ShopDefinition> Shops { get; }

            /// <summary>Needed only to confirm a quest giver has resolvable quests.</summary>
            public IDefinitionRegistry<QuestDefinition> Quests { get; }

            public bool IsUsable => Npcs != null;
        }

        /// <summary>
        /// Asks an NPC for a role.
        /// </summary>
        /// <param name="location">Where the player is. Never modified.</param>
        /// <param name="npcId">Who they are talking to.</param>
        /// <param name="role">What they want.</param>
        /// <param name="context">Registries.</param>
        public static NpcInteractionResult TryInteract(CharacterLocationState location,
            DefinitionId npcId, NpcRole role, in Context context)
        {
            if (location == null || !context.IsUsable)
                return NpcInteractionResult.Rejected(NpcInteractionRejection.MissingContext, npcId, role);

            NPCDefinition npc;
            if (!npcId.IsValid || !context.Npcs.TryGet(npcId, out npc) || npc == null)
                return NpcInteractionResult.Rejected(NpcInteractionRejection.UnknownNpc, npcId, role);

            if (!npc.Enabled)
                return NpcInteractionResult.Rejected(NpcInteractionRejection.NpcDisabled, npcId, role);

            if (!npc.Map.IsValid)
                return NpcInteractionResult.Rejected(NpcInteractionRejection.NpcNotPlaced, npcId, role);

            if (!location.IsOn(npc.Map))
                return NpcInteractionResult.Rejected(NpcInteractionRejection.WrongMap, npcId, role);

            NpcInteractionRejection proximity = CheckProximity(location, npc, context);
            if (proximity != NpcInteractionRejection.None)
                return NpcInteractionResult.Rejected(proximity, npcId, role);

            if (!npc.HasRole(role))
                return NpcInteractionResult.Rejected(NpcInteractionRejection.RoleNotOffered, npcId, role);

            return ResolveRole(npc, role, context);
        }

        /// <summary>
        /// Whether a player could talk to an NPC at all, whatever the role.
        /// </summary>
        /// <remarks>What a prompt uses to decide whether to appear, so the UI asks the same
        /// question the service will answer rather than measuring distance itself.</remarks>
        public static bool CanReach(CharacterLocationState location, NPCDefinition npc,
            in Context context)
        {
            if (location == null || npc == null || !npc.Enabled || !npc.Map.IsValid) return false;
            if (!location.IsOn(npc.Map)) return false;

            return CheckProximity(location, npc, context) == NpcInteractionRejection.None;
        }

        /// <summary>
        /// Resolves what a role actually points at.
        /// </summary>
        /// <remarks>
        /// A role offered but unresolvable is refused rather than opened empty: a vendor
        /// whose stock list was deleted by a patch should say so, not present an empty shop
        /// a player will report as a bug.
        /// </remarks>
        private static NpcInteractionResult ResolveRole(NPCDefinition npc, NpcRole role,
            in Context context)
        {
            switch (role)
            {
                case NpcRole.Shop:
                {
                    if (context.Shops == null)
                        return NpcInteractionResult.Rejected(NpcInteractionRejection.MissingContext, npc.Id, role);

                    ShopDefinition shop;
                    if (!context.Shops.TryGet(npc.Shop, out shop) || shop == null)
                        return NpcInteractionResult.Rejected(NpcInteractionRejection.RoleUnavailable, npc.Id, role);

                    return NpcInteractionResult.Accepted(npc.Id, role, npc.Shop);
                }

                case NpcRole.Quest:
                {
                    // A quest giver with no resolvable quests has nothing to say.
                    if (npc.Quests.Length == 0)
                        return NpcInteractionResult.Rejected(NpcInteractionRejection.RoleUnavailable, npc.Id, role);

                    return NpcInteractionResult.Accepted(npc.Id, role);
                }

                case NpcRole.JobChange:
                {
                    if (npc.ClassesOffered.Length == 0 && npc.JobsOffered.Length == 0)
                        return NpcInteractionResult.Rejected(NpcInteractionRejection.RoleUnavailable, npc.Id, role);

                    return NpcInteractionResult.Accepted(npc.Id, role);
                }

                default:
                    // Storage opens the character's own container; generic and warp need no
                    // content of their own.
                    return NpcInteractionResult.Accepted(npc.Id, role);
            }
        }

        private static NpcInteractionRejection CheckProximity(CharacterLocationState location,
            NPCDefinition npc, in Context context)
        {
            // With no spawn registry the NPC's position is unknown, so distance cannot be
            // checked. Being on the right map is all that can be asserted, and the caller is
            // told nothing stricter than the truth.
            if (context.SpawnPoints == null || !npc.SpawnPoint.IsValid)
                return NpcInteractionRejection.None;

            SpawnPointDefinition spawn;
            if (!context.SpawnPoints.TryGet(npc.SpawnPoint, out spawn) || spawn == null)
                return NpcInteractionRejection.None;

            float radius = npc.InteractionRadius > 0f
                ? npc.InteractionRadius
                : DefaultInteractionRadius;

            var at = new CombatPosition(spawn.X, spawn.Y, spawn.Z);

            return location.Position.SqrDistanceTo(at) <= radius * radius
                ? NpcInteractionRejection.None
                : NpcInteractionRejection.TooFar;
        }
    }
}
