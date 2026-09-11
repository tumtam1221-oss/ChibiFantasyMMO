using ChibiFantasy.Core;

namespace ChibiFantasy.Contracts
{
    /// <summary>What a starting world server found the last one had left behind.</summary>
    public readonly struct WorldReclaimResult
    {
        public WorldReclaimResult(int sessions, int characters)
        {
            Sessions = sessions;
            Characters = characters;
        }

        /// <summary>Sessions the database still believed were inside this world.</summary>
        public int Sessions { get; }

        /// <summary>Characters still marked as being in a world that nobody held.</summary>
        public int Characters { get; }

        /// <summary>Whether the last shutdown left anything stranded.</summary>
        /// <remarks>The thing worth putting in a log: zero is the ordinary case and says
        /// nothing, anything else says the previous process did not stop cleanly.</remarks>
        public bool FoundAnything => Sessions > 0 || Characters > 0;

        public static WorldReclaimResult Nothing => new WorldReclaimResult(0, 0);
    }

    /// <summary>
    /// Hands back what a world server left behind when it died.
    /// </summary>
    /// <remarks>
    /// <b>Why a starting server is the one allowed to do this.</b> A process that has just
    /// started has nobody in it. Anything the authority still believes is inside that world
    /// therefore belongs to the process that is gone. No timeout, no heartbeat and no
    /// "probably dead by now" -- this is the single moment when the answer is known, which
    /// is why it is asked here rather than by something watching from outside.
    ///
    /// <b>Called before the socket opens.</b> Reclaiming after players could already be
    /// connecting would race a returning player against their own ghost, and the ghost wins
    /// the ones that matter.
    ///
    /// <b>Failure is not fatal.</b> An authority that cannot be reached leaves the world
    /// exactly as stranded as it already was, which is worse than working and much better
    /// than a server that refuses to start. It returns nothing found and carries on.
    /// </remarks>
    public interface IWorldReclaim
    {
        /// <summary>Releases everything stranded in one world. Never throws.</summary>
        WorldReclaimResult ReleaseStranded(ServerId server, ChannelId channel);

        /// <summary>
        /// Whether this process is able to ask at all.
        /// </summary>
        /// <remarks>Same key as the calendar write, and the same reason for saying so at
        /// startup: an operator who is told once that stranded players will not be handed
        /// back can fix the configuration, where one who is never told discovers it from a
        /// player who cannot log in.</remarks>
        bool CanReclaim { get; }
    }
}
