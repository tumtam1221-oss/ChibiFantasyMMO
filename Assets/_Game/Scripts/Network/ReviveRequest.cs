namespace ChibiFantasy.Network
{
    /// <summary>
    /// Where a request to get up again lands on the server.
    /// </summary>
    /// <remarks>
    /// <b>It carries nothing but a sequence, and that is the point.</b> A client asking to be
    /// revived does not say where, does not say how much health, and does not say whether it
    /// is allowed -- all three are the server's, read from state this call cannot reach. The
    /// only thing a client contributes is the wish.
    ///
    /// The same shape as every other request sink in this project: the network object knows
    /// an interface, the server implements it, and nothing in between can be handed a
    /// connection id that is not the caller's own.
    /// </remarks>
    public interface ICharacterReviveRequestSink
    {
        /// <param name="connectionId">Taken from the owner of the object, never from a parameter.</param>
        /// <param name="sequence">The client's own ordering, for duplicate suppression.</param>
        void Submit(int connectionId, long sequence);
    }
}
