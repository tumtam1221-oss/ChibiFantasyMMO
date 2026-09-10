using System.IO;
using ChibiFantasy.Backend;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// What a world server asks for when it starts, having no idea how the last one ended.
    /// </summary>
    /// <remarks>
    /// The call this covers throws sessions out of a world, so the tests are weighted
    /// towards the ways it must refuse rather than the way it works: no key, no world, an
    /// authority that cannot be reached, a refusal. Each of those must leave the server
    /// starting normally and the world exactly as stranded as it already was -- a server
    /// that will not open because a cleanup failed has turned a recoverable problem into an
    /// outage.
    /// </remarks>
    public sealed class WorldReclaimTests
    {
        private const string Key = "a-key-that-is-not-in-git";

        private static readonly ServerId Server = new ServerId("srv-1");
        private static readonly ChannelId Channel = new ChannelId("ch-1a");

        // ---- the ordinary answer --------------------------------------------------------

        [Test]
        public void WhatTheAuthoritySaysItReleasedIsWhatComesBack()
        {
            var transport = new CannedTransport(
                "{\"sessions_released\":2,\"characters_released\":3}");

            WorldReclaimResult result =
                new HttpWorldReclaim(transport, Key).ReleaseStranded(Server, Channel);

            Assert.That(result.Sessions, Is.EqualTo(2));
            Assert.That(result.Characters, Is.EqualTo(3));
            Assert.That(result.FoundAnything, Is.True);
        }

        [Test]
        public void A_clean_previous_shutdown_leaves_nothing_to_report()
        {
            var transport = new CannedTransport(
                "{\"sessions_released\":0,\"characters_released\":0}");

            WorldReclaimResult result =
                new HttpWorldReclaim(transport, Key).ReleaseStranded(Server, Channel);

            Assert.That(result.FoundAnything, Is.False,
                "the ordinary case must be silent, or the log stops meaning anything");
        }

        [Test]
        public void The_world_it_asks_about_is_the_one_it_was_given()
        {
            var transport = new CannedTransport("{}");

            new HttpWorldReclaim(transport, Key).ReleaseStranded(Server, Channel);

            Assert.That(transport.Path, Is.EqualTo("/api/world/reclaim"));
            Assert.That(transport.Method, Is.EqualTo("POST"));
            Assert.That(transport.Body, Does.Contain("\"server_id\":\"srv-1\""));
            Assert.That(transport.Body, Does.Contain("\"channel_id\":\"ch-1a\""));
        }

        [Test]
        public void The_deployment_key_is_what_authorises_it()
        {
            var transport = new CannedTransport("{}");

            new HttpWorldReclaim(transport, Key).ReleaseStranded(Server, Channel);

            Assert.That(transport.Token, Is.EqualTo(Key),
                "no player's token can stand in for this");
        }

        // ---- every way it must decline --------------------------------------------------

        [Test]
        public void With_no_key_it_does_not_even_ask()
        {
            var transport = new CannedTransport("{\"sessions_released\":9}");
            var reclaim = new HttpWorldReclaim(transport, string.Empty);

            // Empty rather than null: null would fall through to the environment, and this
            // test must say the same thing on a machine that happens to have one set.
            if (!string.IsNullOrEmpty(
                System.Environment.GetEnvironmentVariable(
                    HttpWorldClockStore.KeyVariable)))
            {
                Assert.Ignore("this machine has a deployment key in its environment");
            }

            Assert.That(reclaim.CanReclaim, Is.False);
            Assert.That(reclaim.ReleaseStranded(Server, Channel).FoundAnything, Is.False);
            Assert.That(transport.Calls, Is.EqualTo(0),
                "a request with no credential would only be refused, loudly, every start");
        }

        [Test]
        public void An_authority_that_cannot_be_reached_is_not_fatal()
        {
            var transport = new UnreachableTransport();

            WorldReclaimResult result =
                new HttpWorldReclaim(transport, Key).ReleaseStranded(Server, Channel);

            Assert.That(result.FoundAnything, Is.False);
        }

        [Test]
        public void A_refusal_is_not_fatal_either()
        {
            var transport = new CannedTransport("{\"code\":\"invalid_world_key\"}", 401);

            WorldReclaimResult result =
                new HttpWorldReclaim(transport, Key).ReleaseStranded(Server, Channel);

            Assert.That(result.FoundAnything, Is.False);
        }

        [Test]
        public void A_world_with_no_name_is_never_asked_about()
        {
            var transport = new CannedTransport("{\"sessions_released\":9}");
            var reclaim = new HttpWorldReclaim(transport, Key);

            Assert.That(reclaim.ReleaseStranded(default, Channel).FoundAnything, Is.False);
            Assert.That(reclaim.ReleaseStranded(Server, default).FoundAnything, Is.False);

            Assert.That(transport.Calls, Is.EqualTo(0),
                "an unnamed world would match nothing, or -- far worse -- everything");
        }

        [Test]
        public void A_body_that_makes_no_sense_reports_nothing_rather_than_guessing()
        {
            var transport = new CannedTransport("not json at all");

            WorldReclaimResult result =
                new HttpWorldReclaim(transport, Key).ReleaseStranded(Server, Channel);

            Assert.That(result.FoundAnything, Is.False);
        }

        // ---- the credential stays out of everything -------------------------------------

        [Test]
        public void The_key_is_never_logged_and_never_authored()
        {
            string source = File.ReadAllText(Path.Combine(
                UnityEngine.Application.dataPath,
                "_Game/Scripts/Backend/HttpWorldReclaim.cs"));

            foreach (string forbidden in new[]
            {
                "Debug.Log", "Debug.LogWarning", "Debug.LogError",
                "SerializeField", "UnityEngine",
            })
            {
                Assert.That(source.Contains(forbidden), Is.False,
                    "HttpWorldReclaim contains '" + forbidden
                    + "': the only reliable way to keep a credential out of a log and out "
                    + "of a scene is for there to be nowhere to put it");
            }
        }

        [Test]
        public void The_key_comes_from_the_same_place_the_calendars_does()
        {
            // Two variables for one deployment key would be a configuration bug waiting to
            // happen: a server that saves its calendar but cannot hand back its players.
            string source = File.ReadAllText(Path.Combine(
                UnityEngine.Application.dataPath,
                "_Game/Scripts/Backend/HttpWorldReclaim.cs"));

            Assert.That(source, Does.Contain("HttpWorldClockStore.KeyVariable"));
            Assert.That(source.Contains("CHIBI_"), Is.False,
                "the variable's name must not be spelled out a second time");
        }

        // ---- fakes ----------------------------------------------------------------------

        private sealed class CannedTransport : IHttpTransport
        {
            private readonly string _body;
            private readonly int _status;

            public CannedTransport(string body, int status = 200)
            {
                _body = body;
                _status = status;
            }

            public int Calls { get; private set; }

            public string Method { get; private set; }

            public string Path { get; private set; }

            public string Body { get; private set; }

            public string Token { get; private set; }

            public HttpExchange Send(string method, string path, string jsonBody,
                string bearerToken)
            {
                Calls++;
                Method = method;
                Path = path;
                Body = jsonBody;
                Token = bearerToken;

                return HttpExchange.Responded(_status, _body);
            }
        }

        private sealed class UnreachableTransport : IHttpTransport
        {
            public HttpExchange Send(string method, string path, string jsonBody,
                string bearerToken)
            {
                return HttpExchange.Unreachable("nothing is listening");
            }
        }
    }
}
