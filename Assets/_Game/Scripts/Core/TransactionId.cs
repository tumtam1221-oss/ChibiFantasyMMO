using System;
using UnityEngine;

namespace ChibiFantasy.Core
{
    /// <summary>
    /// Stable identity of one applied transaction.
    /// </summary>
    /// <remarks>
    /// <b>Never a timestamp.</b> Two transactions can be applied in the same tick, clocks
    /// move backwards, and a wall-clock identity is guessable by anyone who knows roughly
    /// when something happened. A minted GUID is none of those things. The time a
    /// transaction happened is recorded <em>beside</em> this, as data, not as identity.
    ///
    /// <b>Minted by whoever applies, not by whoever asks.</b> A client never chooses a
    /// transaction id; it supplies a <see cref="RequestId"/> and receives a transaction id
    /// back. That split is what makes a retry safe and an audit record trustworthy.
    /// </remarks>
    [Serializable]
    public struct TransactionId : IEquatable<TransactionId>
    {
        [SerializeField] private string _value;

        public TransactionId(string value)
        {
            _value = value;
        }

        public string Value => _value;

        public bool IsValid => !string.IsNullOrWhiteSpace(_value);

        public static TransactionId None => default;

        /// <summary>Mints a new identity. In production only the server does this.</summary>
        public static TransactionId New()
        {
            return new TransactionId(Guid.NewGuid().ToString("N"));
        }

        public bool Equals(TransactionId other)
        {
            return string.Equals(_value, other._value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is TransactionId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return IdentityHash.Fnv1a(_value);
        }

        public override string ToString()
        {
            return _value ?? "<none>";
        }

        public static bool operator ==(TransactionId left, TransactionId right) => left.Equals(right);

        public static bool operator !=(TransactionId left, TransactionId right) => !left.Equals(right);
    }

    /// <summary>
    /// Identity of one <em>request</em> to change something.
    /// </summary>
    /// <remarks>
    /// <b>The idempotency key.</b> A network drops a reply, a player double-clicks, a client
    /// retries: the same request arrives twice. Carrying an identity chosen by the caller
    /// lets the authority recognise the second arrival and return the first answer instead
    /// of doing the work again. Without it, "retry safely" is not expressible and every
    /// unreliable link becomes a duplication bug.
    ///
    /// <b>Distinct from <see cref="TransactionId"/> on purpose.</b> The caller owns this one
    /// and the authority owns that one. One request maps to at most one transaction; a
    /// request that is refused maps to none, and re-sending it must be refused the same way
    /// rather than being retried into success.
    /// </remarks>
    [Serializable]
    public struct RequestId : IEquatable<RequestId>
    {
        [SerializeField] private string _value;

        public RequestId(string value)
        {
            _value = value;
        }

        public string Value => _value;

        public bool IsValid => !string.IsNullOrWhiteSpace(_value);

        public static RequestId None => default;

        public static RequestId New()
        {
            return new RequestId(Guid.NewGuid().ToString("N"));
        }

        public bool Equals(RequestId other)
        {
            return string.Equals(_value, other._value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is RequestId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return IdentityHash.Fnv1a(_value);
        }

        public override string ToString()
        {
            return _value ?? "<none>";
        }

        public static bool operator ==(RequestId left, RequestId right) => left.Equals(right);

        public static bool operator !=(RequestId left, RequestId right) => !left.Equals(right);
    }
}
