using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using UnityEngine.Networking;

namespace ChibiFantasy.Backend
{
    /// <summary>
    /// The production <see cref="IHttpTransport"/>: a real socket, over UnityWebRequest.
    /// </summary>
    /// <remarks>
    /// <b>This is the only file in the project that opens a connection.</b> Everything above
    /// it -- <see cref="HttpAccountApi"/>, the session flow, the panels -- was written against
    /// <see cref="IHttpTransport"/> precisely so that this class could arrive without any of
    /// them changing. <see cref="ScriptedHttpTransport"/> remains, and remains the one the
    /// deterministic tests use; this is what ships.
    ///
    /// <b>Blocking, because the seam is synchronous.</b> <see cref="IHttpTransport.Send"/>
    /// returns a result rather than a task, so the waiting happens here. UnityWebRequest
    /// performs its transfer off the calling thread, so polling <c>isDone</c> makes progress
    /// rather than deadlocking -- but it does occupy the caller for the duration. That is the
    /// right trade for a dedicated server thread or a worker, and the wrong one for the main
    /// thread of a client mid-frame, which is why <see cref="Send"/> is documented as
    /// something a client calls from off the main thread and never from inside a frame it
    /// wants to finish.
    ///
    /// <b>Two deadlines, deliberately.</b> UnityWebRequest's own <c>timeout</c> is asked for,
    /// and a wall-clock deadline is enforced here as well. The engine's timeout has historically
    /// not fired on every platform and every failure mode; a request that hangs forever is a
    /// client that hangs forever, so the loop refuses to wait past its own deadline whatever
    /// the engine reports.
    ///
    /// <b>Nothing here is logged.</b> Not the body, not the Authorization header, not the
    /// token, not the response. There is no logging call in this file at all -- the surest way
    /// not to log a secret is to have nowhere that logs anything.
    /// </remarks>
    public sealed class UnityWebRequestTransport : IHttpTransport, IDisposable
    {
        /// <summary>How often the wait loop checks whether the transfer finished.</summary>
        /// <remarks>Five milliseconds: short enough that a fast local reply is not delayed
        /// perceptibly, long enough that waiting does not become a spin that burns a core.</remarks>
        private const int PollMilliseconds = 5;

        private readonly HttpEndpoint _endpoint;

        /// <summary>Set from any thread to abandon in-flight and subsequent requests.</summary>
        /// <remarks>An <c>int</c> written through <see cref="Interlocked"/> rather than a
        /// <c>bool</c>, because the thread that cancels is not the thread that is waiting and
        /// a plain field gives no guarantee the waiter ever sees the write.</remarks>
        private int _cancelled;

        public UnityWebRequestTransport(HttpEndpoint endpoint)
        {
            _endpoint = endpoint;
        }

        public UnityWebRequestTransport(string baseAddress,
            int timeoutSeconds = HttpEndpoint.DefaultTimeoutSeconds)
            : this(new HttpEndpoint(baseAddress, timeoutSeconds))
        {
        }

        /// <summary>Where this transport points. Carries no credential.</summary>
        public HttpEndpoint Endpoint => _endpoint;

        public bool IsCancelled => Volatile.Read(ref _cancelled) != 0;

        /// <summary>
        /// Abandons the in-flight request and refuses further ones.
        /// </summary>
        /// <remarks>The cancellation the seam can express: the interface is synchronous, so
        /// there is no task to cancel and no token to pass. A caller that wants to stop --
        /// a player pressing back, a server shutting down -- sets this, and the waiting call
        /// returns a cancelled exchange rather than a fabricated failure.</remarks>
        public void Cancel()
        {
            Interlocked.Exchange(ref _cancelled, 1);
        }

        /// <summary>Allows the transport to be used again after a cancellation.</summary>
        public void Reset()
        {
            Interlocked.Exchange(ref _cancelled, 0);
        }

        public void Dispose()
        {
            Cancel();
        }

        /// <summary>
        /// Sends one request and waits for the reply.
        /// </summary>
        /// <remarks>
        /// Total: every path returns an <see cref="HttpExchange"/>, including the ones that
        /// threw. An exception escaping a transport would surface somewhere with no idea what
        /// to do with it, so anything unexpected becomes an unreachable exchange carrying the
        /// message as diagnostic text -- text that goes into <see cref="ApiError.Detail"/>,
        /// which is never shown to a player.
        /// </remarks>
        public HttpExchange Send(string method, string path, string jsonBody, string bearerToken)
        {
            if (!_endpoint.IsConfigured)
            {
                return HttpExchange.Unreachable("no base address configured");
            }

            if (IsCancelled)
            {
                return HttpExchange.Cancelled("cancelled before send");
            }

            UnityWebRequest request = null;

            try
            {
                request = Build(method, path, jsonBody, bearerToken);

                return Await(request);
            }
            catch (Exception e)
            {
                // The message, not the exception: a caller has no use for a stack trace and
                // a player must never see one.
                return HttpExchange.Unreachable(e.Message);
            }
            finally
            {
                request?.Dispose();
            }
        }

        /// <summary>Builds the request, headers and all.</summary>
        /// <remarks>
        /// <b>The Authorization header is attached only when there is a token.</b> Sending an
        /// empty bearer on the login call would put a meaningless header on the one request
        /// that has no session yet, and would be one more place a value could be captured for
        /// no benefit.
        /// </remarks>
        private UnityWebRequest Build(string method, string path, string jsonBody,
            string bearerToken)
        {
            string url = _endpoint.Resolve(path);
            bool isPost = string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase);

            var request = new UnityWebRequest(url, isPost ? "POST" : "GET")
            {
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = _endpoint.TimeoutSeconds,

                // The API is one host and never redirects. Following a redirect would let a
                // compromised or misconfigured server move an Authorization header somewhere
                // this client never chose to send it.
                redirectLimit = 0,
            };

            if (isPost && jsonBody != null)
            {
                byte[] payload = Encoding.UTF8.GetBytes(jsonBody);
                request.uploadHandler = new UploadHandlerRaw(payload);
                request.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
            }

            request.SetRequestHeader("Accept", "application/json");

            if (!string.IsNullOrEmpty(bearerToken))
            {
                request.SetRequestHeader("Authorization", "Bearer " + bearerToken);
            }

            return request;
        }

        /// <summary>
        /// Waits for the transfer and reports what happened.
        /// </summary>
        /// <remarks>
        /// A reply is a reply whatever its status: a 401 or a 500 arrives as
        /// <see cref="HttpExchange.Responded"/> with its body, because the body carries the
        /// error contract the API defines. Only the absence of a reply is a transport failure.
        /// That distinction is the whole reason this class and <see cref="HttpAccountApi"/>
        /// are separate.
        /// </remarks>
        private HttpExchange Await(UnityWebRequest request)
        {
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();

            // A stopwatch, not a wall clock: DateTime.Now moving backwards mid-request --
            // an NTP correction, a user changing the clock -- would extend the deadline
            // arbitrarily, and a request that outlives its deadline is the bug this exists
            // to prevent.
            var elapsed = Stopwatch.StartNew();
            long timeoutMs = (long)_endpoint.TimeoutSeconds * 1000L;

            while (!operation.isDone)
            {
                if (IsCancelled)
                {
                    request.Abort();

                    return HttpExchange.Cancelled("cancelled while in flight");
                }

                if (elapsed.ElapsedMilliseconds >= timeoutMs)
                {
                    request.Abort();

                    return HttpExchange.TimedOut("no reply within "
                        + _endpoint.TimeoutSeconds + "s");
                }

                Thread.Sleep(PollMilliseconds);
            }

            switch (request.result)
            {
                case UnityWebRequest.Result.ConnectionError:
                    // The engine reports its own timeout as a connection error, so the text is
                    // the only thing separating "host refused" from "host too slow".
                    return LooksLikeTimeout(request.error)
                        ? HttpExchange.TimedOut(request.error)
                        : HttpExchange.Unreachable(request.error);

                case UnityWebRequest.Result.DataProcessingError:
                    // The bytes arrived and could not be turned into a body. Reported as a
                    // reply with its status, so the layer above applies its own reading of a
                    // malformed response rather than mistaking it for a dead server.
                    return HttpExchange.Responded((int)request.responseCode, string.Empty);

                default:
                    // Success and ProtocolError alike: the server answered.
                    return HttpExchange.Responded((int)request.responseCode,
                        request.downloadHandler?.text ?? string.Empty);
            }
        }

        private static bool LooksLikeTimeout(string error)
        {
            return !string.IsNullOrEmpty(error)
                && error.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
