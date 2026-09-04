using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using ChibiFantasy.Backend;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// The production transport, and how its failures reach the layer above.
    /// </summary>
    /// <remarks>
    /// Two kinds of test live here, and the difference matters. The endpoint and mapping
    /// tests are pure. The connection tests open a <b>real socket</b> -- to a port nothing
    /// listens on, and to an address that does not answer -- because "what does this do when
    /// the host is down" cannot be established against a scripted transport that was told to
    /// say the host is down. Neither needs a server running, so they are deterministic.
    /// </remarks>
    [TestFixture]
    internal sealed class UnityWebRequestTransportTests
    {
        // Port 1 is reserved and nothing binds it, so a connection is refused immediately
        // rather than after a wait. That refusal is the behaviour under test.
        private const string ClosedPort = "http://127.0.0.1:1";

        /// <summary>A listener that accepts a connection and then says nothing, ever.</summary>
        /// <remarks>
        /// The first attempt at this used an unroutable documentation address, on the
        /// assumption that packets to it would be dropped. They were not -- the local stack
        /// refused them immediately, which is an unreachable host rather than a slow one, and
        /// the test correctly said so.
        ///
        /// A real socket that completes its handshake and then withholds a response is the
        /// only way to produce a genuine HTTP timeout without depending on what a particular
        /// machine's network happens to do with a strange address. It needs no internet and
        /// no server.
        /// </remarks>
        private sealed class SilentListener : System.IDisposable
        {
            private readonly TcpListener _listener;
            private readonly List<TcpClient> _accepted = new List<TcpClient>();
            private volatile bool _stopped;

            public SilentListener()
            {
                _listener = new TcpListener(IPAddress.Loopback, 0);
                _listener.Start();

                Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

                System.Threading.ThreadPool.QueueUserWorkItem(_ => Accept());
            }

            public int Port { get; }

            public string BaseAddress => "http://127.0.0.1:" + Port;

            private void Accept()
            {
                try
                {
                    while (!_stopped)
                    {
                        TcpClient client = _listener.AcceptTcpClient();

                        // Held open and never written to. Closing it would itself be an
                        // answer of sorts; the point is to give none.
                        lock (_accepted) _accepted.Add(client);
                    }
                }
                catch
                {
                    // Stopping the listener is how this loop ends. Nothing to report.
                }
            }

            public void Dispose()
            {
                _stopped = true;

                try { _listener.Stop(); } catch { }

                lock (_accepted)
                {
                    foreach (TcpClient client in _accepted)
                    {
                        try { client.Close(); } catch { }
                    }

                    _accepted.Clear();
                }
            }
        }

        // ---- endpoint ----------------------------------------------------------------

        [Test]
        public void AnEndpointJoinsItsBaseAddressToAPath()
        {
            var endpoint = new HttpEndpoint("http://127.0.0.1:8080");

            Assert.That(endpoint.Resolve("/api/health"), Is.EqualTo("http://127.0.0.1:8080/api/health"));
        }

        [Test]
        public void ATrailingSlashDoesNotProduceADoubleSlash()
        {
            var endpoint = new HttpEndpoint("http://127.0.0.1:8080///");

            Assert.That(endpoint.Resolve("/api/health"), Is.EqualTo("http://127.0.0.1:8080/api/health"));
        }

        [Test]
        public void APathWithoutALeadingSlashIsStillJoinedCorrectly()
        {
            var endpoint = new HttpEndpoint("http://127.0.0.1:8080");

            Assert.That(endpoint.Resolve("api/health"), Is.EqualTo("http://127.0.0.1:8080/api/health"));
        }

        [Test]
        public void AnEmptyBaseAddressIsNotConfigured()
        {
            Assert.That(new HttpEndpoint(null).IsConfigured, Is.False);
            Assert.That(new HttpEndpoint("").IsConfigured, Is.False);
        }

        [Test]
        public void ANonPositiveTimeoutFallsBackToTheDefaultRatherThanMeaningNoLimit()
        {
            // A zero timeout on UnityWebRequest means "wait forever", which is precisely the
            // behaviour a caller passing zero does not want.
            Assert.That(new HttpEndpoint("http://x", 0).TimeoutSeconds,
                Is.EqualTo(HttpEndpoint.DefaultTimeoutSeconds));
            Assert.That(new HttpEndpoint("http://x", -5).TimeoutSeconds,
                Is.EqualTo(HttpEndpoint.DefaultTimeoutSeconds));
        }

        // ---- refusal without a socket --------------------------------------------------

        [Test]
        public void AnUnconfiguredTransportFailsRatherThanBuildingAMeaninglessUrl()
        {
            var transport = new UnityWebRequestTransport(new HttpEndpoint(null));

            HttpExchange exchange = transport.Send("GET", "/api/health", null, null);

            Assert.That(exchange.Reached, Is.False);
            Assert.That(exchange.FailureKind, Is.EqualTo(TransportFailureKind.Unreachable));
        }

        [Test]
        public void ACancelledTransportRefusesToSendAtAll()
        {
            var transport = new UnityWebRequestTransport(ClosedPort, 2);
            transport.Cancel();

            HttpExchange exchange = transport.Send("GET", "/api/health", null, null);

            Assert.That(exchange.Reached, Is.False);
            Assert.That(exchange.FailureKind, Is.EqualTo(TransportFailureKind.Cancelled));
        }

        [Test]
        public void ResetAllowsATransportToBeUsedAgainAfterCancellation()
        {
            var transport = new UnityWebRequestTransport(ClosedPort, 2);
            transport.Cancel();
            Assert.That(transport.IsCancelled, Is.True);

            transport.Reset();

            Assert.That(transport.IsCancelled, Is.False);

            // It sends again -- and fails for the honest reason, not the cancelled one.
            HttpExchange exchange = transport.Send("GET", "/api/health", null, null);
            Assert.That(exchange.FailureKind, Is.Not.EqualTo(TransportFailureKind.Cancelled));
        }

        [Test]
        public void DisposingATransportCancelsIt()
        {
            var transport = new UnityWebRequestTransport(ClosedPort, 2);

            transport.Dispose();

            Assert.That(transport.IsCancelled, Is.True);
        }

        // ---- real sockets ---------------------------------------------------------------

        [Test]
        public void AClosedPortIsReportedAsUnreachableAndNotAsAnHttpStatus()
        {
            var transport = new UnityWebRequestTransport(ClosedPort, 3);

            HttpExchange exchange = transport.Send("GET", "/api/health", null, null);

            Assert.That(exchange.Reached, Is.False, "nothing is listening, so nothing replied");
            Assert.That(exchange.Status, Is.EqualTo(0));
            Assert.That(exchange.FailureKind, Is.EqualTo(TransportFailureKind.Unreachable));
            Assert.That(exchange.Failure, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void AnUnreachableHostBecomesUnreachableRatherThanAnErrorTheDomainWouldShow()
        {
            var transport = new UnityWebRequestTransport(ClosedPort, 3);
            var api = new HttpAccountApi(transport);

            ApiResult<IReadOnlyList<ServerInfo>> result =
                api.GetServers(new AccountId("acc-a"));

            Assert.That(result.IsOk, Is.False);
            Assert.That(result.Error.Kind, Is.EqualTo(ApiErrorKind.Unreachable));

            // Worth retrying: the difference between a retry button and a dead end.
            Assert.That(result.Error.IsTransient, Is.True);
        }

        [Test]
        public void AHostThatNeverAnswersTimesOutAndDoesSoWithinItsDeadline()
        {
            using (var silent = new SilentListener())
            {
                var transport = new UnityWebRequestTransport(silent.BaseAddress, 2);
                var stopwatch = Stopwatch.StartNew();

                HttpExchange exchange = transport.Send("GET", "/api/health", null, null);

                stopwatch.Stop();

                Assert.That(exchange.Reached, Is.False);
                Assert.That(exchange.FailureKind, Is.EqualTo(TransportFailureKind.Timeout),
                    "the connection succeeded and the reply never came: slow, not absent");

                // The deadline is enforced here, not merely requested of the engine. It has
                // to actually stop -- a request that outlives its timeout hangs the caller.
                Assert.That(stopwatch.Elapsed.TotalSeconds, Is.LessThan(15.0));
            }
        }

        [Test]
        public void ATimeoutIsReportedAsTimeoutAndNotAsAnAbsentServer()
        {
            using (var silent = new SilentListener())
            {
                var transport = new UnityWebRequestTransport(silent.BaseAddress, 2);
                var api = new HttpAccountApi(transport);

                ApiResult<IReadOnlyList<ServerInfo>> result =
                    api.GetServers(new AccountId("acc-a"));

                Assert.That(result.IsOk, Is.False);
                Assert.That(result.Error.Kind, Is.EqualTo(ApiErrorKind.Timeout));
                Assert.That(result.Error.IsTransient, Is.True);
            }
        }

        [Test]
        public void ACancellationFromAnotherThreadStopsARequestThatWouldOtherwiseHang()
        {
            using (var silent = new SilentListener())
            {
                // A 60-second timeout that must not be reached: the cancellation is what
                // ends this call, which is the only way to tell the two mechanisms apart.
                var transport = new UnityWebRequestTransport(silent.BaseAddress, 60);

                System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                {
                    System.Threading.Thread.Sleep(250);
                    transport.Cancel();
                });

                var stopwatch = Stopwatch.StartNew();
                HttpExchange exchange = transport.Send("GET", "/api/health", null, null);
                stopwatch.Stop();

                Assert.That(exchange.FailureKind, Is.EqualTo(TransportFailureKind.Cancelled));
                Assert.That(stopwatch.Elapsed.TotalSeconds, Is.LessThan(30.0),
                    "cancelling must interrupt the wait, not merely be recorded");
            }
        }

        // ---- secrets --------------------------------------------------------------------

        [Test]
        public void AFailedRequestNeverCarriesTheTokenOrThePasswordInItsDiagnosticText()
        {
            const string secretToken = "SECRET-TOKEN-VALUE";
            var transport = new UnityWebRequestTransport(ClosedPort, 3);

            HttpExchange exchange = transport.Send("POST", "/api/session/select-server",
                "{\"password\":\"SECRET-PASSWORD\"}", secretToken);

            Assert.That(exchange.Failure ?? string.Empty, Does.Not.Contain(secretToken));
            Assert.That(exchange.Failure ?? string.Empty, Does.Not.Contain("SECRET-PASSWORD"));
        }

        [Test]
        public void AnEndpointPrintsItsAddressAndHasNowhereToPutACredential()
        {
            var endpoint = new HttpEndpoint("http://127.0.0.1:8080");

            Assert.That(endpoint.ToString(), Is.EqualTo("http://127.0.0.1:8080"));

            // The type has exactly two members. A password could not be stored on it if
            // somebody tried.
            Assert.That(typeof(HttpEndpoint).GetProperties().Length, Is.EqualTo(3));
        }
    }
}
