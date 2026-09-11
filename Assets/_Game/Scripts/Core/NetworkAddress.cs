using System;

namespace ChibiFantasy.Core
{
    /// <summary>
    /// What can be told about an address before anything is sent to it.
    /// </summary>
    /// <remarks>
    /// <b>Why this is here and not beside the HTTP client.</b> The backend seam is kept free
    /// of protocol literals on purpose -- exactly one file in it is allowed to name a
    /// transport, so that nothing else quietly becomes one. But "would this address carry my
    /// traffic in the clear" is not a question about HTTP; it is a question about an address,
    /// and it has the same answer whoever is asking. It lives here, where it can be reasoned
    /// about and tested without a transport in sight.
    ///
    /// <b>It answers; it does not decide.</b> Whether a process may run against an
    /// unencrypted address is a decision for whoever composes that process, and one they may
    /// need to be able to override. This only knows the facts.
    /// </remarks>
    public static class NetworkAddress
    {
        private const string SecureScheme = "https://";
        private const string SchemeSeparator = "://";

        /// <summary>
        /// Whether an address would carry its traffic unencrypted.
        /// </summary>
        /// <remarks>Anything that is not the secure scheme counts, including an address with
        /// no scheme at all. Guessing encryption on somebody's behalf is how a deployment
        /// ends up in the clear while believing otherwise.</remarks>
        public static bool IsPlaintext(string address)
        {
            return string.IsNullOrEmpty(address)
                || !address.StartsWith(SecureScheme, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Whether an address names this machine, so nothing sent to it reaches a network.
        /// </summary>
        /// <remarks>
        /// <b>Only the spellings that really are local.</b> Not a general "is this private"
        /// test: 10.x and 192.168.x are somebody's office network, and traffic there is
        /// still traffic somebody can watch. Treating those as safe is the mistake this is
        /// written to avoid.
        ///
        /// <b>The host is taken apart rather than searched.</b> "localhost.attacker.example"
        /// contains "localhost" and is not this machine; neither is
        /// "http://localhost@evil.example", whose host is everything after the last @.
        /// </remarks>
        public static bool IsLoopback(string address)
        {
            if (string.IsNullOrEmpty(address)) return false;

            string host = address;

            int scheme = host.IndexOf(SchemeSeparator, StringComparison.Ordinal);

            if (scheme >= 0) host = host.Substring(scheme + SchemeSeparator.Length);

            int end = host.IndexOfAny(new[] { '/', '?', '#' });

            if (end >= 0) host = host.Substring(0, end);

            int at = host.LastIndexOf('@');

            if (at >= 0) host = host.Substring(at + 1);

            if (host.StartsWith("[", StringComparison.Ordinal))
            {
                int close = host.IndexOf(']');

                host = close > 1 ? host.Substring(1, close - 1) : host.Substring(1);
            }
            else
            {
                int colon = host.IndexOf(':');

                if (colon >= 0) host = host.Substring(0, colon);
            }

            host = host.Trim().TrimEnd('.');

            return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
                || host == "127.0.0.1"
                || host == "::1"
                || host == "0:0:0:0:0:0:0:1";
        }

        /// <summary>
        /// Whether using this address would put its traffic on a network in the clear.
        /// </summary>
        /// <remarks>The question worth asking, and two questions at once: plaintext to this
        /// machine never reaches a wire and is right for development; plaintext to anywhere
        /// else is readable by anything between here and there. An address nobody configured
        /// is not reported as unsafe -- it is broken, which is a different problem with a
        /// different message.</remarks>
        public static bool IsUnencryptedOverNetwork(string address)
        {
            return !string.IsNullOrEmpty(address) && IsPlaintext(address) && !IsLoopback(address);
        }
    }
}
