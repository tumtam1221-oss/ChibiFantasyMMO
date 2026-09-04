using ChibiFantasy.Client.World;
using ChibiFantasy.Network;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ChibiFantasy.Client
{
    /// <summary>
    /// Reads the player's input, sends it as intent, and smooths towards the answer.
    /// </summary>
    /// <remarks>
    /// <b>It never decides where the character is.</b> The transform this drives is a
    /// picture: it eases towards the position the server replicated, and if the two disagree
    /// the server wins by simply continuing to replicate. Nothing here writes an
    /// authoritative value, and there is no path by which it could -- the entity's state is
    /// server-write-only and the only message this sends carries two axes.
    ///
    /// <b>Only the owner sends anything.</b> A remote player's character is drawn from its
    /// replicated position and nothing else; this component asks for input only on the
    /// object the local client owns, which is also the only object FishNet would accept a
    /// request through.
    ///
    /// <b>Smoothing is presentation and is deliberately dumb.</b> A lerp towards the last
    /// replicated position, no prediction, no rollback, no reconciliation -- those change
    /// what the player sees when the server disagrees, which is a design decision this gate
    /// does not make. What is here is the minimum that stops a replicated position looking
    /// like a teleport every packet.
    ///
    /// <b>Input System stays on this side of the line.</b> Gameplay is engine-free and the
    /// server assembly knows nothing about a keyboard; the dependency lives here, in the
    /// client, which is the only place it is allowed.
    /// </remarks>
    [RequireComponent(typeof(CharacterNetworkEntity))]
    public sealed class CharacterMovementInput : MonoBehaviour
    {
        [Tooltip("How quickly the visible character eases towards the replicated position.")]
        [SerializeField] private float _smoothing = 12f;

        [Tooltip("Seconds between movement requests. Zero sends one per frame.")]
        [SerializeField] private float _sendInterval = 0.05f;

        [Tooltip("Metres of disagreement past which the visible character is placed rather "
            + "than eased. Covers a respawn, a reconnect and a map change.")]
        [SerializeField] private float _snapDistance = 4f;

        private CharacterNetworkEntity _entity;
        private long _sequence;
        private float _sinceLastSend;

        /// <summary>The last input this client sent, for a HUD or a test to read.</summary>
        public Vector2 LastSentInput { get; private set; }

        /// <summary>How many requests this client has sent.</summary>
        public long SentRequests => _sequence;

        private void Awake()
        {
            _entity = GetComponent<CharacterNetworkEntity>();
        }

        private void Update()
        {
            if (_entity == null) return;

            Follow();

            if (!_entity.IsOwner) return;

            _sinceLastSend += Time.deltaTime;

            if (_sinceLastSend < _sendInterval) return;

            _sinceLastSend = 0f;

            Send(ReadInput());
        }

        /// <summary>
        /// Eases the visible transform towards where the server says the character is.
        /// </summary>
        /// <remarks>Applies to the local player and to everybody else identically. The
        /// owner gets no special treatment here, because giving it any would be the first
        /// step towards the client deciding its own position.</remarks>
        private void Follow()
        {
            var authoritative = new Vector3(_entity.X, _entity.Y, _entity.Z);

            if (_smoothing <= 0f || ShouldSnap(authoritative))
            {
                transform.position = authoritative;

                return;
            }

            transform.position = Vector3.Lerp(transform.position, authoritative,
                1f - Mathf.Exp(-_smoothing * Time.deltaTime));
        }

        /// <summary>
        /// Whether the gap is too large to be movement.
        /// </summary>
        /// <remarks>
        /// A respawn, a reconnect and a map change all replicate as one enormous position
        /// change, and easing towards it would fly the character across the world in front of
        /// the player -- through walls, for several seconds, looking like a hack. Past the
        /// threshold the visible character is simply placed where the server says it is.
        ///
        /// <b>This is presentation and nothing else.</b> Either branch draws the same
        /// authoritative position; they differ only in how long the picture takes to agree
        /// with it.
        /// </remarks>
        public bool ShouldSnap(Vector3 authoritative)
        {
            return CharacterVisualRules.ShouldSnap(transform.position, authoritative,
                _snapDistance);
        }

        /// <summary>The distance past which the visible character is placed, not eased.</summary>
        public float SnapDistance => _snapDistance;

        /// <summary>
        /// The current movement input, as a vector no longer than one.
        /// </summary>
        /// <remarks>
        /// Clamped here as a courtesy so an honest client is not refused for a diagonal
        /// worth 1.41. The server does not rely on this: it refuses an oversized input
        /// itself, because a client that wants to send 1.41 simply would not call this
        /// method.
        /// </remarks>
        private static Vector2 ReadInput()
        {
            Keyboard keyboard = Keyboard.current;

            if (keyboard == null) return Vector2.zero;

            var input = new Vector2(
                (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f),
                (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f));

            return input.sqrMagnitude > 1f ? input.normalized : input;
        }

        /// <summary>
        /// Sends one movement intent.
        /// </summary>
        /// <remarks>
        /// A monotonic sequence, so a duplicated or reordered packet is detectable by the
        /// server -- which refuses it rather than moving twice.
        ///
        /// Standing still is still sent once and then stopped, so the server is not asked to
        /// re-evaluate an empty input every frame for a player who is reading their bag.
        /// </remarks>
        private void Send(Vector2 input)
        {
            if (input == Vector2.zero && LastSentInput == Vector2.zero) return;

            LastSentInput = input;

            _entity.RequestMove(input.x, input.y, ++_sequence);
        }
    }
}
