using FishNet.Broadcast;

namespace ChibiFantasy.Network
{
    /// <summary>
    /// What a client sends to prove who it is, immediately after connecting.
    /// </summary>
    /// <remarks>
    /// <b>Strings and integers, deliberately.</b> These cross a wire, so nothing here is a
    /// <c>GameObject</c>, a <c>Transform</c>, a <c>NetworkObject</c> reference or any other
    /// engine type -- see rule 16.13. The typed identifiers used everywhere else in the
    /// project are wrappers over strings, and they are unwrapped at this boundary and
    /// re-wrapped on the far side, so the serializer has nothing to guess at.
    ///
    /// <b>The token is the only field that matters.</b> The rest are the client's claims,
    /// carried so the server can compare them against what the authority says and refuse a
    /// disagreement. Deleting every one of them would not change who gets in; it would only
    /// remove the server's ability to notice that a client had got confused.
    ///
    /// <b>No account id is trusted here even though one is present.</b> It is compared,
    /// never read. That distinction is the whole of rule 16.5.
    /// </remarks>
    public struct WorldJoinRequestMessage : IBroadcast
    {
        /// <summary>The session token issued by the account API. Never logged.</summary>
        public string Token;

        public string ClaimedAccountId;

        public string ClaimedCharacterId;

        public string ClaimedServerId;

        public string ClaimedChannelId;

        /// <summary>Dotted version strings, matching Phase 14's VersionSet.</summary>
        public string ClientVersion;

        public string ProtocolVersion;

        public string ContentVersion;
    }

    /// <summary>
    /// Whether the connection was admitted, and who it turned out to be.
    /// </summary>
    /// <remarks>
    /// <b>No token comes back.</b> The client already has it; returning it would put a
    /// secret in one more place for no gain.
    ///
    /// <b>The identities here are the authority's.</b> A client that claimed one character
    /// and was admitted as another has been refused, so an accepted response always agrees
    /// with what the client sent -- but it is the server's copy that is transmitted, because
    /// echoing the client's would make this message decorative.
    ///
    /// <see cref="Rejection"/> is the numeric value of Phase 14's <c>SessionRejection</c>,
    /// so a client displays the same message for the same reason whether it was refused at
    /// character select or at the world door.
    /// </remarks>
    public struct WorldJoinResponseMessage : IBroadcast
    {
        public bool Admitted;

        /// <summary>Phase 14 <c>SessionRejection</c> as an int. Zero when admitted.</summary>
        public int Rejection;

        public string SessionId;

        public string AccountId;

        public string CharacterId;

        public string ServerId;

        public string ChannelId;

        /// <summary>Phase 14 <c>WorldEntryState</c> as an int.</summary>
        public int EntryState;
    }

    /// <summary>
    /// Where the character stands, decided entirely by the server.
    /// </summary>
    /// <remarks>
    /// <b>A map and a spawn point, not coordinates.</b> The position is resolved from the
    /// authored <c>SpawnPointDefinition</c> Phase 11 already owns, and the numbers below are
    /// carried only so a client can place a model without loading definitions it may not
    /// have. A client that sends its own position is ignored -- there is no message in which
    /// it could send one, which is rule 16.8 and the "forged spawn" case of 16.16.
    ///
    /// <b>Nothing here is persisted.</b> The database stores a map and a spawn identifier;
    /// these floats are derived from the definition at spawn time and thrown away.
    /// </remarks>
    public struct WorldSpawnMessage : IBroadcast
    {
        public string CharacterId;

        /// <summary>The map definition the character is on.</summary>
        public string MapId;

        /// <summary>The authored spawn point resolved for this arrival.</summary>
        public string SpawnPointId;

        public float X;

        public float Y;

        public float Z;

        /// <summary>Authoritative level, read from the character row.</summary>
        public int Level;

        /// <summary>The character's revision when the server spawned it.</summary>
        public int CharacterRevision;
    }

    /// <summary>Why a connection is ending, sent before the socket closes where possible.</summary>
    /// <remarks>
    /// A disconnect a player was told about and one that simply happened are different
    /// experiences. This is the former; the latter is what the connection callback observes
    /// anyway, so nothing depends on this arriving.
    /// </remarks>
    public struct WorldLeaveMessage : IBroadcast
    {
        public string SessionId;

        /// <summary>Phase 14 <c>SessionRejection</c> as an int, or zero for an ordinary exit.</summary>
        public int Reason;
    }
}
