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

        /// <summary>
        /// The world's time of day at the moment of arrival, 0 and 1 being midnight.
        /// </summary>
        /// <remarks>
        /// <b>Sent once, not streamed.</b> The clock is arithmetic -- a start point and a
        /// fixed rate -- so a client that is told where the world is can run the same sum
        /// itself and stay in step. Streaming a number that changes by 0.0003 a second would
        /// spend bandwidth to tell every client something it could already work out.
        ///
        /// <b>Why it must be sent at all.</b> Without a seed each client would start its own
        /// day from zero and two players standing together would see different skies, which
        /// reads as a rendering bug long before anyone suspects a clock.
        /// </remarks>
        public float TimeOfDay;

        /// <summary>Real seconds in one in-game day, so the client advances at the server's rate.</summary>
        /// <remarks>Sent rather than shared as a constant because an operator who changes the
        /// day length on the server must not have to ship a client to match.</remarks>
        public float SecondsPerDay;

        /// <summary>What the sky is doing on arrival, as <c>WorldWeather</c>.</summary>
        /// <remarks>An int rather than the enum so the wire format does not move when a new
        /// weather is added to the end of that enum.</remarks>
        public int Weather;
    }

    /// <summary>
    /// The weather turned.
    /// </summary>
    /// <remarks>
    /// <b>Why weather needs a message where the clock did not.</b> The time of day is a start
    /// point and a rate, so a client told once can work out the rest. A change of weather
    /// happens at a moment nobody can compute in advance, so it has to be said out loud.
    ///
    /// <b>Sent only when it actually turns.</b> The director raises its event on a real
    /// change, never on a re-roll that landed on the same sky, so this does not carry
    /// "still raining" to every client every few minutes.
    /// </remarks>
    public struct WorldWeatherMessage : IBroadcast
    {
        /// <summary>The new weather, as <c>WorldWeather</c>.</summary>
        public int Weather;
    }

    /// <summary>
    /// What time the world thinks it is, repeated so clients cannot drift away from it.
    /// </summary>
    /// <remarks>
    /// <b>Why a seed is not enough on its own.</b> The arrival message hands out a start
    /// point and a rate, and every client then counts real seconds itself. Two clocks counting
    /// independently do not stay together forever: a frame spike, a stall, a machine that
    /// slept, and the client's idea of the hour is its own. That is invisible for a few
    /// minutes and obvious after an evening, when one player is watching sunset and the player
    /// beside them is not.
    ///
    /// <b>It is cheap enough not to think about.</b> Two floats every minute is nothing beside
    /// what a single moving character costs per second, and it removes an entire class of
    /// "the sky is wrong on my machine" that is otherwise very hard to reproduce.
    ///
    /// <b>Correcting is the client's business.</b> This says where the world is; how gently
    /// to arrive there belongs to whatever is drawing the sky.
    /// </remarks>
    public struct WorldTimeMessage : IBroadcast
    {
        /// <summary>The world's time of day, 0 and 1 being midnight.</summary>
        public float TimeOfDay;

        /// <summary>Real seconds in one in-game day, repeated so a changed day length lands.</summary>
        public float SecondsPerDay;
    }

    /// <summary>
    /// A request to pin the world's weather, or to let it roll again.
    /// </summary>
    /// <remarks>
    /// <b>This is a request, not an instruction.</b> It travels client to server, and the
    /// server is free to ignore it -- and does, on any build that is not a development one.
    /// The weather that comes back is still <see cref="WorldWeatherMessage"/> from the
    /// authority, so a client that sent this and a client that did not are told the same
    /// thing in the same way. Nothing here changes what the sender sees on its own.
    ///
    /// <b>Why it exists at all.</b> Snow is a festival rather than something that happens,
    /// so somebody has to be able to switch it on; and weather that turns every few minutes
    /// cannot be tested by waiting for it. Both of those want the same door.
    /// </remarks>
    public struct WorldWeatherCommandMessage : IBroadcast
    {
        /// <summary>The weather to hold, as <c>WorldWeather</c>. Ignored when Automatic.</summary>
        public int Weather;

        /// <summary>Stop holding and let the sky roll on its own again.</summary>
        public bool Automatic;
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
