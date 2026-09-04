using System;
using ChibiFantasy.Contracts;

namespace ChibiFantasy.Backend
{
    /// <summary>
    /// Builds an authority that speaks to the account API, without naming a transport to
    /// whoever asked for one.
    /// </summary>
    /// <remarks>
    /// <b>This exists because of an audit finding.</b> The world server was constructing a
    /// <see cref="UnityWebRequestTransport"/> itself, which made the Server assembly depend
    /// directly on HTTP -- exactly what the boundary rule forbids and exactly what
    /// <see cref="IWorldSessionAuthority"/> was introduced to prevent. The composition still
    /// has to happen somewhere; it happens here, on the transport's own side of the line.
    ///
    /// A caller receives an interface and something to dispose. It never learns what the
    /// implementation was, so a future transport -- a pooled HTTP client, a gRPC channel, an
    /// in-process authority for a single-player build -- changes this file and nothing else.
    /// </remarks>
    public static class BackendAuthority
    {
        /// <summary>
        /// An authority backed by the PHP API at an address.
        /// </summary>
        /// <param name="baseAddress">Scheme, host and port. Carries no credential.</param>
        /// <param name="timeoutSeconds">How long a call may take.</param>
        /// <param name="lifetime">Dispose to cancel in-flight calls and release the transport.</param>
        /// <remarks>Two returns rather than one object with a Dispose, because the thing a
        /// caller wants to hold is the interface and the thing it must remember to release
        /// is the transport. Merging them would put a Dispose on the authority seam, and a
        /// seam with a Dispose is a seam that has admitted it owns a connection.</remarks>
        public static IWorldSessionAuthority OverHttp(string baseAddress, int timeoutSeconds,
            out IDisposable lifetime)
        {
            var transport = new UnityWebRequestTransport(baseAddress, timeoutSeconds);

            lifetime = transport;

            return new HttpWorldSessionAuthority(transport);
        }
    }
}
