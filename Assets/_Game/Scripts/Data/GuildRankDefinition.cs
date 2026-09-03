using System;
using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Data
{
    /// <summary>What a guild rank is allowed to do.</summary>
    /// <remarks>
    /// <b>Flags, so a rank is a set rather than a level.</b> A guild that wants officers who
    /// may invite but not kick, or a quartermaster who may only touch storage, authors that
    /// as a combination. An ordered enum would force every permission to arrive together in
    /// one ladder, which is not how guilds actually work.
    ///
    /// <b>Checked in the domain.</b> <c>GuildService</c> asks these; a panel hiding a button
    /// is a convenience on top. A client that sends the request anyway is refused.
    ///
    /// <see cref="StorageAccess"/> is declared and <em>not</em> implemented: guild storage is
    /// future work, and the flag exists so a rank authored today does not have to be
    /// re-authored when it arrives. Nothing reads it yet, and nothing pretends to.
    /// </remarks>
    [Flags]
    public enum GuildPermission
    {
        None = 0,
        Invite = 1 << 0,
        Kick = 1 << 1,
        Promote = 1 << 2,
        Demote = 1 << 3,
        TransferLeadership = 1 << 4,
        Disband = 1 << 5,
        EditSettings = 1 << 6,

        /// <summary>Declared for future guild storage. Not implemented in this phase.</summary>
        StorageAccess = 1 << 7
    }

    /// <summary>
    /// One authored rank within a guild.
    /// </summary>
    /// <remarks>
    /// <b>Content, not code.</b> There is no leader class and no officer class. Leader,
    /// officer and member are three assets differing in <see cref="Permissions"/> and
    /// <see cref="Order"/>, so a guild system that later wants five ranks needs no code
    /// change.
    ///
    /// <see cref="Order"/> exists because promotion and demotion need a direction, and
    /// deriving one from a permission set would be guesswork -- two ranks can hold
    /// incomparable permissions. A higher order is senior.
    ///
    /// Flat and DB-friendly: one row of a future <c>guild_rank</c> table is an id, a key, an
    /// order and a permission bitmask.
    /// </remarks>
    public sealed class GuildRankDefinition : GameDefinition
    {
        [SerializeField] private LocalizationKey _nameKey;

        [Tooltip("Seniority. Higher is more senior; promotion moves up.")]
        [SerializeField] private int _order;

        [SerializeField] private GuildPermission _permissions = GuildPermission.None;

        [Tooltip("Whether this rank is the guild leader's. Exactly one rank should be.")]
        [SerializeField] private bool _isLeaderRank;

        public LocalizationKey NameKey => _nameKey;

        public int Order => _order;

        public GuildPermission Permissions => _permissions;

        /// <summary>
        /// Whether holding this rank means leading the guild.
        /// </summary>
        /// <remarks>Authored rather than inferred from having every permission: a guild may
        /// want an officer rank with the same powers as the leader, and only one of them is
        /// the person a leadership transfer moves.</remarks>
        public bool IsLeaderRank => _isLeaderRank;

        /// <summary>Whether this rank carries a permission.</summary>
        public bool Allows(GuildPermission permission)
        {
            return permission != GuildPermission.None && (_permissions & permission) == permission;
        }
    }
}
