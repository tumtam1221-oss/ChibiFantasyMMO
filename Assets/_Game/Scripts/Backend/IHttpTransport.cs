using System.Collections.Generic;

namespace ChibiFantasy.Backend
{
    /// <summary>One HTTP exchange, described without naming an HTTP library.</summary>
    /// <remarks>
    /// <b>Why an interface at all.</b> Unity's <c>UnityWebRequest</c> only works on the
    /// main thread, needs a coroutine, and cannot be constructed in an EditMode test. An
    /// interface here means the API adapter is exercised against a scripted transport in
    /// the test suite and against the real one at runtime, with no difference in the code
    /// being tested.
    ///
    /// <b>Synchronous, deliberately.</b> The domain above is engine-free and its tests are
    /// deterministic. A task-returning transport would push asynchrony through every
    /// service and every test for the benefit of an implementation that has not been
    /// written yet. A real client does its waiting on its own side of this line -- in a
    /// coroutine, on a worker, or on a dedicated server thread -- and calls in with what it
    /// received.
    /// </remarks>
    public interface IHttpTransport
    {
        /// <summary>
        /// Sends a request and returns what came back.
        /// </summary>
        /// <param name="method">GET or POST.</param>
        /// <param name="path">Path only, such as <c>/api/auth/login</c>. Never a full URL:
        /// the base address belongs to the transport, so no caller can be pointed at
        /// another host by a value it was handed.</param>
        /// <param name="jsonBody">Serialized body, or null for a GET.</param>
        /// <param name="bearerToken">Sent as an Authorization header. Never logged.</param>
        HttpExchange Send(string method, string path, string jsonBody, string bearerToken);
    }

    /// <summary>
    /// What a transport observed: a status, a body, and whether it connected at all.
    /// </summary>
    /// <remarks>
    /// <b>Transport failure and HTTP status are separate.</b> A 403 is a reply; a refused
    /// connection is not. Collapsing them would let a network outage be reported to a
    /// player as a ban, which is the exact confusion <see cref="ApiError"/> exists to
    /// prevent one layer up.
    /// </remarks>
    public readonly struct HttpExchange
    {
        private HttpExchange(bool reached, int status, string body, string failure)
        {
            Reached = reached;
            Status = status;
            Body = body;
            Failure = failure;
        }

        /// <summary>Whether the server answered at all, whatever it said.</summary>
        public bool Reached { get; }

        /// <summary>The HTTP status. Meaningless unless <see cref="Reached"/>.</summary>
        public int Status { get; }

        /// <summary>The raw response body, expected to be JSON.</summary>
        public string Body { get; }

        /// <summary>Why the server was not reached. Diagnostic only, never shown to a player.</summary>
        public string Failure { get; }

        public bool IsSuccess => Reached && Status >= 200 && Status < 300;

        public static HttpExchange Responded(int status, string body)
        {
            return new HttpExchange(true, status, body ?? string.Empty, null);
        }

        public static HttpExchange Unreachable(string failure)
        {
            return new HttpExchange(false, 0, string.Empty, failure);
        }
    }

    /// <summary>
    /// A transport that records what it was asked and replays scripted answers.
    /// </summary>
    /// <remarks>
    /// Lives beside the interface rather than in the test assembly because the Client
    /// assembly needs it too -- an editor harness or a preview scene can drive the whole
    /// login flow with it and never touch a network.
    ///
    /// It is unmistakably not production: it opens no socket, resolves no host and knows
    /// no URL. Nothing here could be mistaken for a real client.
    /// </remarks>
    public sealed class ScriptedHttpTransport : IHttpTransport
    {
        private readonly Dictionary<string, Queue<HttpExchange>> _scripted =
            new Dictionary<string, Queue<HttpExchange>>();

        private readonly List<string> _calls = new List<string>();

        /// <summary>Every path this transport was asked for, in order.</summary>
        public IReadOnlyList<string> Calls => _calls;

        /// <summary>The bearer token presented on the most recent call, or null.</summary>
        public string LastBearerToken { get; private set; }

        /// <summary>The body sent on the most recent call, or null.</summary>
        public string LastBody { get; private set; }

        /// <summary>Queues an answer for the next call to a path.</summary>
        public ScriptedHttpTransport Enqueue(string method, string path, HttpExchange exchange)
        {
            string key = Key(method, path);

            if (!_scripted.TryGetValue(key, out Queue<HttpExchange> queue))
            {
                queue = new Queue<HttpExchange>();
                _scripted[key] = queue;
            }

            queue.Enqueue(exchange);
            return this;
        }

        /// <summary>Queues a JSON body with a 200 status.</summary>
        public ScriptedHttpTransport EnqueueOk(string method, string path, string json)
        {
            return Enqueue(method, path, HttpExchange.Responded(200, json));
        }

        public HttpExchange Send(string method, string path, string jsonBody, string bearerToken)
        {
            _calls.Add(Key(method, path));
            LastBearerToken = bearerToken;
            LastBody = jsonBody;

            if (_scripted.TryGetValue(Key(method, path), out Queue<HttpExchange> queue)
                && queue.Count > 0)
            {
                return queue.Dequeue();
            }

            // An unscripted call is a test asking for something it did not set up. Saying
            // so is more useful than a silent empty 200 that fails later somewhere else.
            return HttpExchange.Unreachable("no scripted response for " + Key(method, path));
        }

        private static string Key(string method, string path)
        {
            return method + " " + path;
        }
    }
}
