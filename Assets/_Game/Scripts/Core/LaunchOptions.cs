using System;

namespace ChibiFantasy.Core
{
    /// <summary>
    /// Settings a process is told at launch, rather than ones baked into what was built.
    /// </summary>
    /// <remarks>
    /// <b>Why this exists.</b> A world server serves exactly one server and one channel on
    /// exactly one port, and those three values lived only in the scene it was built from.
    /// Ten channels therefore meant ten builds, and moving the account API to another machine
    /// meant rebuilding the world. Neither is a deployment; both are a rebuild wearing one as
    /// a costume. The same build now runs anywhere it is pointed.
    ///
    /// <b>Command line beats environment beats what was authored.</b> That order is the one
    /// every deployment tool already expects: a container image carries the authored default,
    /// its environment adapts it to a cluster, and a one-off run overrides both from a shell
    /// without touching either. Nothing here writes anything back, so what a scene says
    /// remains the answer when nobody said otherwise.
    ///
    /// <b>Empty is absent, not empty.</b> <c>--server=</c> means somebody's script produced a
    /// blank variable, and honouring it would start a server with no identity that fails much
    /// later and somewhere else. It falls through to the next source instead.
    ///
    /// <b>Pure, and given its inputs.</b> It reads neither the real command line nor the real
    /// environment; both arrive as arguments. That is what lets every rule below be a test
    /// rather than a claim, and it is why this can live in an assembly that knows nothing
    /// about processes.
    /// </remarks>
    public static class LaunchOptions
    {
        /// <summary>
        /// The value for one setting, from the first source that has one.
        /// </summary>
        /// <param name="option">Command-line name, without dashes. Matched case-insensitively.</param>
        /// <param name="variable">Environment variable name. Matched as given.</param>
        /// <param name="arguments">The process command line. Unity's own arguments are here
        /// too and are ignored.</param>
        /// <param name="environment">Reads an environment variable, or null when there is
        /// none. Supplied rather than called directly so a test can pin it.</param>
        /// <param name="authored">What the scene or prefab says. Returned when nobody
        /// overrode it.</param>
        public static string Resolve(string option, string variable, string[] arguments,
            Func<string, string> environment, string authored)
        {
            string fromCommandLine = FromArguments(option, arguments);

            if (!string.IsNullOrEmpty(fromCommandLine)) return fromCommandLine;

            string fromEnvironment = environment == null || string.IsNullOrEmpty(variable)
                ? null
                : environment(variable);

            if (!string.IsNullOrEmpty(fromEnvironment)) return fromEnvironment;

            return authored;
        }

        /// <summary>
        /// A port, from the same sources, refusing anything that is not one.
        /// </summary>
        /// <remarks>A port of zero means "any free port" to the operating system, which for a
        /// server nobody could then connect to is never what was meant -- so it is refused
        /// alongside the unparseable, and the authored port stands.</remarks>
        public static ushort ResolvePort(string option, string variable, string[] arguments,
            Func<string, string> environment, ushort authored)
        {
            string raw = Resolve(option, variable, arguments, environment, null);

            if (string.IsNullOrEmpty(raw)) return authored;

            if (!ushort.TryParse(raw.Trim(), out ushort port) || port == 0) return authored;

            return port;
        }

        /// <summary>
        /// Finds <c>--option=value</c> or <c>--option value</c>.
        /// </summary>
        /// <remarks>
        /// <b>Both spellings, because both are typed.</b> Compose files tend to the first and
        /// shells to the second.
        /// <b>One and two dashes.</b> Refusing <c>-option</c> would be a footgun for no gain.
        /// <b>A following option is not a value.</b> <c>--server --channel two</c> means the
        /// server was left out, not that it is called "--channel": taking the next token
        /// blindly would silently start a world with a nonsense identity.
        /// </remarks>
        private static string FromArguments(string option, string[] arguments)
        {
            if (arguments == null || string.IsNullOrEmpty(option)) return null;

            for (var i = 0; i < arguments.Length; i++)
            {
                string argument = arguments[i];

                if (string.IsNullOrEmpty(argument) || argument[0] != '-') continue;

                string name = argument.TrimStart('-');
                string inlineValue = null;

                int equals = name.IndexOf('=');

                if (equals >= 0)
                {
                    inlineValue = name.Substring(equals + 1);
                    name = name.Substring(0, equals);
                }

                if (!string.Equals(name, option, StringComparison.OrdinalIgnoreCase)) continue;

                if (equals >= 0) return string.IsNullOrEmpty(inlineValue) ? null : inlineValue;

                if (i + 1 >= arguments.Length) return null;

                string next = arguments[i + 1];

                if (string.IsNullOrEmpty(next) || next[0] == '-') return null;

                return next;
            }

            return null;
        }
    }
}
