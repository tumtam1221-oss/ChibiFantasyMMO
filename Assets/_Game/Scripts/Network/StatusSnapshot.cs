namespace ChibiFantasy.Network
{
    /// <summary>
    /// One status effect on a character, as much of it as a client may know.
    /// </summary>
    /// <remarks>
    /// <b>An id and three numbers.</b> What the effect <em>does</em> -- its modifiers, its
    /// control type, its icon, its name -- is authored content the client already has, so
    /// sending it would be putting a copy of the definition on the wire once per player per
    /// change. The same rule the inventory snapshot follows for items.
    ///
    /// <b>What is deliberately absent.</b> No source. A source is what granted an effect and
    /// exists so the server can take back exactly what it gave; telling a client which
    /// hidden mechanism buffed somebody is server business leaking outward, and no
    /// presentation needs it. No persistence revision, no connection, no account, no
    /// authority object -- none of which a bar of icons has any use for.
    ///
    /// <b>The category travels even though it could be looked up.</b> A client that meets an
    /// effect its content does not describe still has to decide whether to draw it as a buff
    /// or a debuff, and guessing wrong puts a poison in the buff row. Four bytes buys an
    /// answer that is right even when the definition is missing.
    /// </remarks>
    public struct StatusEffectSnapshot
    {
        /// <summary>Reference to a <c>StatusEffectDefinition</c>.</summary>
        public string EffectId;

        /// <summary>How many stacks, never below one.</summary>
        public int Stacks;

        /// <summary>
        /// Seconds left when the server sent this, or zero or less for an effect that does
        /// not expire.
        /// </summary>
        /// <remarks>The same convention the runtime state uses, carried rather than
        /// translated so the two layers read the same way. A client counts this down for
        /// display; reaching zero locally removes nothing.</remarks>
        public float RemainingSeconds;

        /// <summary>
        /// The numeric value of the authored <c>StatusEffectCategory</c>.
        /// </summary>
        /// <remarks>An int because this assembly does not reference Data -- the same choice
        /// the inventory snapshot makes for equipment slots and the character entity makes
        /// for gender.</remarks>
        public int Category;

        public bool IsIndefinite => RemainingSeconds <= 0f;

        public override string ToString()
        {
            return EffectId + (Stacks > 1 ? " x" + Stacks : string.Empty)
                + (IsIndefinite ? string.Empty : " " + RemainingSeconds + "s");
        }
    }

    /// <summary>
    /// Every status effect on one character, as of one authoritative moment.
    /// </summary>
    /// <remarks>
    /// <b>Replacement, not a delta.</b> The server builds the whole list and the client
    /// throws away the previous one, for the reason the inventory snapshot gives: a client
    /// that maintained a buff list by applying changes would be a client keeping its own
    /// buff list, and a single dropped removal would leave a debuff on screen forever.
    ///
    /// <b><see cref="Revision"/> is the server's.</b> It exists so a client can drop a
    /// snapshot that arrives after a newer one, not so it can argue about which is right. It
    /// is the status runtime's own revision, which already advances on exactly the changes
    /// worth sending. It is not the persistence save revision -- that is a database
    /// concurrency token with no business on the wire.
    ///
    /// <b>Owner-scoped.</b> Sent to the connection that owns the character and to nobody
    /// else. A player's buffs tell an opponent what they are immune to, when their defensive
    /// cooldown ends and whether it is safe to engage; that is not public information, and
    /// there is no authored notion of a world-visible status to widen it with.
    /// </remarks>
    public struct StatusSnapshot
    {
        /// <summary>Who this belongs to. The client checks it against its own character.</summary>
        public string CharacterId;

        /// <summary>Advances on every authoritative change. The server owns it.</summary>
        public int Revision;

        /// <summary>Everything currently applied.</summary>
        public StatusEffectSnapshot[] Effects;

        public int Count => Effects == null ? 0 : Effects.Length;

        public override string ToString()
        {
            return "status(" + CharacterId + ") r" + Revision + " x" + Count;
        }
    }
}
