using System.Collections.Generic;
using ChibiFantasy.Core;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// What a process is told at launch, and what it does when it is told nothing.
    /// </summary>
    /// <remarks>
    /// The rules here decide which world a server process becomes. Getting one wrong does not
    /// crash anything: the server starts, listens, admits players and saves its calendar
    /// under the wrong name. That is why the awkward cases -- a blank variable, an option
    /// with no value, a value that is really the next option -- each have a test rather than
    /// a comment.
    /// </remarks>
    public sealed class LaunchOptionsTests
    {
        private const string Option = "channel";
        private const string Variable = "CHIBI_CHANNEL_ID";
        private const string Authored = "authored-channel";

        private static System.Func<string, string> Env(params string[] pairs)
        {
            var values = new Dictionary<string, string>();

            for (var i = 0; i + 1 < pairs.Length; i += 2) values[pairs[i]] = pairs[i + 1];

            return key => values.TryGetValue(key, out string value) ? value : null;
        }

        private static string Resolve(string[] arguments, System.Func<string, string> environment)
            => LaunchOptions.Resolve(Option, Variable, arguments, environment, Authored);

        // ---- nobody said anything -------------------------------------------------------

        [Test]
        public void What_the_scene_says_stands_when_nothing_overrides_it()
        {
            Assert.That(Resolve(new string[0], Env()), Is.EqualTo(Authored));
            Assert.That(Resolve(null, null), Is.EqualTo(Authored));
        }

        [Test]
        public void Unrelated_arguments_are_ignored()
        {
            // Unity puts a pile of its own on every command line
            string[] arguments = { "ChibiFantasyServer.exe", "-batchmode", "-nographics",
                "-logFile", "world.log" };

            Assert.That(Resolve(arguments, Env()), Is.EqualTo(Authored));
        }

        // ---- the three sources, in order ------------------------------------------------

        [Test]
        public void The_environment_beats_what_the_scene_says()
        {
            Assert.That(Resolve(new string[0], Env(Variable, "from-environment")),
                Is.EqualTo("from-environment"));
        }

        [Test]
        public void The_command_line_beats_the_environment()
        {
            string[] arguments = { "--channel=from-command-line" };

            Assert.That(Resolve(arguments, Env(Variable, "from-environment")),
                Is.EqualTo("from-command-line"));
        }

        // ---- how an option may be written -----------------------------------------------

        [Test]
        public void Both_spellings_of_an_option_are_accepted()
        {
            Assert.That(Resolve(new[] { "--channel=one" }, Env()), Is.EqualTo("one"));
            Assert.That(Resolve(new[] { "--channel", "one" }, Env()), Is.EqualTo("one"));
            Assert.That(Resolve(new[] { "-channel", "one" }, Env()), Is.EqualTo("one"));
        }

        [Test]
        public void An_option_is_matched_whatever_its_case()
        {
            Assert.That(Resolve(new[] { "--CHANNEL=one" }, Env()), Is.EqualTo("one"));
        }

        [Test]
        public void A_value_may_contain_an_equals_sign()
        {
            // base64 and connection strings both do
            Assert.That(LaunchOptions.Resolve("api", "X", new[] { "--api=http://h/?a=b" },
                Env(), null), Is.EqualTo("http://h/?a=b"));
        }

        // ---- the traps ------------------------------------------------------------------

        [Test]
        public void A_blank_value_falls_through_rather_than_blanking_the_setting()
        {
            // a script that produced an empty variable, which is not a request for "no id"
            Assert.That(Resolve(new[] { "--channel=" }, Env()), Is.EqualTo(Authored));
            Assert.That(Resolve(new string[0], Env(Variable, string.Empty)),
                Is.EqualTo(Authored));
        }

        [Test]
        public void An_option_with_nothing_after_it_falls_through()
        {
            Assert.That(Resolve(new[] { "--channel" }, Env()), Is.EqualTo(Authored));
        }

        [Test]
        public void The_next_option_is_never_taken_as_a_value()
        {
            // "--channel --port 7770" means the channel was left out, not that it is
            // called "--port". Taking it would start a world with a nonsense identity.
            string[] arguments = { "--channel", "--port", "7770" };

            Assert.That(Resolve(arguments, Env()), Is.EqualTo(Authored));
        }

        [Test]
        public void A_blank_command_line_value_still_lets_the_environment_answer()
        {
            Assert.That(Resolve(new[] { "--channel=" }, Env(Variable, "from-environment")),
                Is.EqualTo("from-environment"));
        }

        [Test]
        public void An_option_that_merely_starts_the_same_is_not_a_match()
        {
            Assert.That(Resolve(new[] { "--channels=many" }, Env()), Is.EqualTo(Authored));
            Assert.That(Resolve(new[] { "--chan=short" }, Env()), Is.EqualTo(Authored));
        }

        [Test]
        public void The_first_spelling_on_the_line_wins()
        {
            Assert.That(Resolve(new[] { "--channel=first", "--channel=second" }, Env()),
                Is.EqualTo("first"));
        }

        // ---- ports ----------------------------------------------------------------------

        [Test]
        public void A_port_is_read_from_the_same_three_sources()
        {
            Assert.That(LaunchOptions.ResolvePort("port", "CHIBI_PORT",
                new string[0], Env(), 7770), Is.EqualTo(7770));

            Assert.That(LaunchOptions.ResolvePort("port", "CHIBI_PORT",
                new string[0], Env("CHIBI_PORT", "7771"), 7770), Is.EqualTo(7771));

            Assert.That(LaunchOptions.ResolvePort("port", "CHIBI_PORT",
                new[] { "--port=7772" }, Env("CHIBI_PORT", "7771"), 7770), Is.EqualTo(7772));
        }

        [Test]
        public void A_port_that_is_not_a_port_leaves_the_authored_one_alone()
        {
            foreach (string nonsense in new[] { "abc", "-1", "70000", "77.70", " " })
            {
                Assert.That(LaunchOptions.ResolvePort("port", "CHIBI_PORT",
                    new[] { "--port=" + nonsense }, Env(), 7770),
                    Is.EqualTo(7770), "accepted '" + nonsense + "' as a port");
            }
        }

        [Test]
        public void Port_zero_is_refused()
        {
            // Zero tells the operating system "any free port", which for a server nobody
            // could then find is never what an operator meant.
            Assert.That(LaunchOptions.ResolvePort("port", "CHIBI_PORT",
                new[] { "--port=0" }, Env(), 7770), Is.EqualTo(7770));
        }

        [Test]
        public void A_port_with_spaces_around_it_still_reads()
        {
            Assert.That(LaunchOptions.ResolvePort("port", "CHIBI_PORT",
                new string[0], Env("CHIBI_PORT", " 7771 "), 7770), Is.EqualTo(7771));
        }
    }
}
