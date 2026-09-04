namespace ChibiFantasy.Backend
{
    /// <summary>
    /// Where the API lives, and how long a caller will wait for it.
    /// </summary>
    /// <remarks>
    /// <b>Configuration, not a constant.</b> A base address compiled into a transport is a
    /// build that can only ever talk to one deployment. This is passed in, so development,
    /// staging and production differ by a value rather than by a rebuild.
    ///
    /// <b>It holds no credential.</b> There is no username, password, API key or signing
    /// secret here and no field one could be put in. The only secret this client ever holds
    /// is the session token the server issues it, and that lives on
    /// <see cref="HttpAccountApi"/> for the lifetime of a session.
    ///
    /// <b>Trailing slashes are normalised once.</b> A base address ending in "/" and a path
    /// beginning with "/" would otherwise produce a double slash, which some servers route
    /// and some reject -- a difference nobody should have to discover at runtime.
    /// </remarks>
    public readonly struct HttpEndpoint
    {
        /// <summary>How long a request may take before it is abandoned, in seconds.</summary>
        /// <remarks>Ten is long enough for a login on a slow connection and short enough that
        /// a player is not left looking at a frozen screen. Overridable because a dedicated
        /// server calling the same API has entirely different patience.</remarks>
        public const int DefaultTimeoutSeconds = 10;

        public HttpEndpoint(string baseAddress, int timeoutSeconds = DefaultTimeoutSeconds)
        {
            BaseAddress = Normalise(baseAddress);
            TimeoutSeconds = timeoutSeconds > 0 ? timeoutSeconds : DefaultTimeoutSeconds;
        }

        /// <summary>The scheme, host and port, with no trailing slash.</summary>
        public string BaseAddress { get; }

        public int TimeoutSeconds { get; }

        public bool IsConfigured => !string.IsNullOrEmpty(BaseAddress);

        /// <summary>
        /// Joins the base address to a path.
        /// </summary>
        /// <remarks>The path is a path, never a URL. A caller that could pass a full address
        /// could point this client at another host by handing it a value, so anything
        /// resembling one is treated as a path and appended -- the base address always wins.</remarks>
        public string Resolve(string path)
        {
            if (string.IsNullOrEmpty(path)) return BaseAddress;

            return path[0] == '/' ? BaseAddress + path : BaseAddress + "/" + path;
        }

        private static string Normalise(string baseAddress)
        {
            if (string.IsNullOrEmpty(baseAddress)) return string.Empty;

            string trimmed = baseAddress.Trim();

            while (trimmed.Length > 0 && trimmed[trimmed.Length - 1] == '/')
            {
                trimmed = trimmed.Substring(0, trimmed.Length - 1);
            }

            return trimmed;
        }

        /// <summary>Prints the address. There is nothing secret in it to withhold.</summary>
        public override string ToString()
        {
            return IsConfigured ? BaseAddress : "<unconfigured>";
        }
    }
}
