using System;
using UnityEngine;

namespace ChibiFantasy.Contracts
{
    /// <summary>
    /// A three-part version number.
    /// </summary>
    /// <remarks>
    /// Integers and ordered comparison, nothing more. Deliberately not a string: "1.10.0" and
    /// "1.9.0" compare the wrong way round as text, and a version comparison that is wrong
    /// once is a client let into a server it cannot speak to.
    ///
    /// Flat because it has to travel and to persist: three columns, or one packed integer.
    /// </remarks>
    [Serializable]
    public struct VersionNumber : IEquatable<VersionNumber>, IComparable<VersionNumber>
    {
        [SerializeField] private int _major;
        [SerializeField] private int _minor;
        [SerializeField] private int _patch;

        public VersionNumber(int major, int minor = 0, int patch = 0)
        {
            _major = major < 0 ? 0 : major;
            _minor = minor < 0 ? 0 : minor;
            _patch = patch < 0 ? 0 : patch;
        }

        public int Major => _major;

        public int Minor => _minor;

        public int Patch => _patch;

        /// <summary>Zero in every part, which is what an unset version reads as.</summary>
        public bool IsUnset => _major == 0 && _minor == 0 && _patch == 0;

        public int CompareTo(VersionNumber other)
        {
            if (_major != other._major) return _major.CompareTo(other._major);
            if (_minor != other._minor) return _minor.CompareTo(other._minor);
            return _patch.CompareTo(other._patch);
        }

        public bool IsOlderThan(VersionNumber other) => CompareTo(other) < 0;

        public bool IsNewerThan(VersionNumber other) => CompareTo(other) > 0;

        public bool Equals(VersionNumber other) => CompareTo(other) == 0;

        public override bool Equals(object obj) => obj is VersionNumber other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (_major * 397 ^ _minor) * 397 ^ _patch;
            }
        }

        public override string ToString()
        {
            return _major + "." + _minor + "." + _patch;
        }

        public static bool operator ==(VersionNumber a, VersionNumber b) => a.Equals(b);

        public static bool operator !=(VersionNumber a, VersionNumber b) => !a.Equals(b);
    }

    /// <summary>Which kind of version a compatibility answer is about.</summary>
    /// <remarks>
    /// Closed technical category, and the reason the three are never merged. They fail
    /// differently: a stale binary can be patched, a stale protocol cannot be talked to, and
    /// stale content may or may not matter. A single "version" field would force one answer
    /// onto all three.
    /// </remarks>
    public enum VersionKind
    {
        None = 0,

        /// <summary>The application binary the player is running.</summary>
        Client = 1,

        /// <summary>The network and API contract shape.</summary>
        Protocol = 2,

        /// <summary>The authored content and data the client shipped with.</summary>
        Content = 3
    }

    /// <summary>
    /// What a client reports about itself.
    /// </summary>
    /// <remarks>
    /// <b>Supplied by the client, decided by the authority.</b> The client states what it is;
    /// it does not get to conclude that it is acceptable. A future launcher fills this in
    /// after patching, which is why nothing here computes or invents a version -- the login
    /// flow receives one.
    /// </remarks>
    [Serializable]
    public struct VersionSet : IEquatable<VersionSet>
    {
        [SerializeField] private VersionNumber _client;
        [SerializeField] private VersionNumber _protocol;
        [SerializeField] private VersionNumber _content;

        public VersionSet(VersionNumber client, VersionNumber protocol, VersionNumber content)
        {
            _client = client;
            _protocol = protocol;
            _content = content;
        }

        public VersionNumber Client => _client;

        public VersionNumber Protocol => _protocol;

        public VersionNumber Content => _content;

        public VersionNumber Of(VersionKind kind)
        {
            switch (kind)
            {
                case VersionKind.Client: return _client;
                case VersionKind.Protocol: return _protocol;
                case VersionKind.Content: return _content;
                default: return default;
            }
        }

        public bool Equals(VersionSet other)
        {
            return _client == other._client && _protocol == other._protocol
                && _content == other._content;
        }

        public override bool Equals(object obj) => obj is VersionSet other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (_client.GetHashCode() * 397 ^ _protocol.GetHashCode()) * 397
                    ^ _content.GetHashCode();
            }
        }

        public override string ToString()
        {
            return "client " + _client + " / protocol " + _protocol + " / content " + _content;
        }
    }

    /// <summary>How acceptable a client's versions are.</summary>
    /// <remarks>
    /// Four outcomes, not a bool, because they mean four different things to a player: carry
    /// on, carry on but there is an update, patch before you can play, and this build can
    /// never talk to this server.
    /// </remarks>
    public enum VersionCompatibility
    {
        /// <summary>Everything matches what the authority requires.</summary>
        Compatible = 0,

        /// <summary>Playable, but a newer build exists.</summary>
        OptionalUpdate = 1,

        /// <summary>Below the required floor. A patch fixes it.</summary>
        RequiredUpdate = 2,

        /// <summary>No patch helps -- the contract itself differs.</summary>
        Incompatible = 3
    }

    /// <summary>
    /// The versions an authority requires, and how strictly.
    /// </summary>
    /// <remarks>
    /// <b>Data, not code.</b> Every bound here arrives from the authority -- a server row, an
    /// API response -- so raising a minimum is a configuration change and no service compares
    /// a version to a literal. A test authors two different policies and watches the same
    /// service reach different answers.
    ///
    /// <b>Protocol is exact; the others have floors.</b> A client one protocol version behind
    /// cannot be talked to at all, so a mismatch is <see cref="VersionCompatibility.Incompatible"/>
    /// rather than something a patch is promised to fix. Client and content have a minimum
    /// and a latest: below the minimum is <see cref="VersionCompatibility.RequiredUpdate"/>,
    /// below the latest is <see cref="VersionCompatibility.OptionalUpdate"/>.
    ///
    /// <see cref="ContentIsAdvisory"/> exists because a content mismatch is sometimes cosmetic
    /// and sometimes fatal, and which one is a decision for whoever ships the content rather
    /// than for this file.
    /// </remarks>
    [Serializable]
    public struct VersionRequirement
    {
        [SerializeField] private VersionNumber _minimumClient;
        [SerializeField] private VersionNumber _latestClient;
        [SerializeField] private VersionNumber _requiredProtocol;
        [SerializeField] private VersionNumber _minimumContent;
        [SerializeField] private VersionNumber _latestContent;
        [SerializeField] private bool _contentIsAdvisory;

        public VersionRequirement(VersionNumber minimumClient, VersionNumber latestClient,
            VersionNumber requiredProtocol, VersionNumber minimumContent = default,
            VersionNumber latestContent = default, bool contentIsAdvisory = false)
        {
            _minimumClient = minimumClient;
            _latestClient = latestClient;
            _requiredProtocol = requiredProtocol;
            _minimumContent = minimumContent;
            _latestContent = latestContent;
            _contentIsAdvisory = contentIsAdvisory;
        }

        public VersionNumber MinimumClient => _minimumClient;

        /// <summary>The newest build available. Unset means no update is advertised.</summary>
        public VersionNumber LatestClient => _latestClient;

        /// <summary>The exact protocol the authority speaks. Unset means it is not checked.</summary>
        public VersionNumber RequiredProtocol => _requiredProtocol;

        public VersionNumber MinimumContent => _minimumContent;

        public VersionNumber LatestContent => _latestContent;

        /// <summary>Whether stale content is an inconvenience rather than a barrier.</summary>
        public bool ContentIsAdvisory => _contentIsAdvisory;

        public override string ToString()
        {
            return "client >= " + _minimumClient + ", protocol == " + _requiredProtocol;
        }
    }

    /// <summary>What a compatibility check concluded, and about what.</summary>
    public readonly struct VersionCompatibilityResult
    {
        private VersionCompatibilityResult(VersionCompatibility compatibility, VersionKind kind,
            VersionNumber supplied, VersionNumber expected)
        {
            Compatibility = compatibility;
            Kind = kind;
            Supplied = supplied;
            Expected = expected;
        }

        public VersionCompatibility Compatibility { get; }

        /// <summary>Which version failed, or <see cref="VersionKind.None"/> when none did.</summary>
        public VersionKind Kind { get; }

        /// <summary>What the client reported.</summary>
        public VersionNumber Supplied { get; }

        /// <summary>What the authority wanted, so a launcher knows what to fetch.</summary>
        public VersionNumber Expected { get; }

        public bool IsPlayable => Compatibility == VersionCompatibility.Compatible
            || Compatibility == VersionCompatibility.OptionalUpdate;

        public static VersionCompatibilityResult Compatible =>
            new VersionCompatibilityResult(VersionCompatibility.Compatible, VersionKind.None,
                default, default);

        public static VersionCompatibilityResult For(VersionCompatibility compatibility,
            VersionKind kind, VersionNumber supplied, VersionNumber expected)
        {
            return new VersionCompatibilityResult(compatibility, kind, supplied, expected);
        }

        public override string ToString()
        {
            return Compatibility + (Kind == VersionKind.None
                ? string.Empty : " (" + Kind + " " + Supplied + " vs " + Expected + ")");
        }
    }

    /// <summary>
    /// Decides whether a client may connect.
    /// </summary>
    /// <remarks>
    /// <b>Pure, and it names no version.</b> Every bound comes from the
    /// <see cref="VersionRequirement"/> passed in; nothing below compares against a literal,
    /// so the shipped numbers live in configuration and a bump is a data change.
    ///
    /// <b>The strictest failure wins.</b> Protocol is checked first because an incompatible
    /// protocol cannot be patched around, then the client floor, then content. Reporting the
    /// mildest problem first would tell a player to take an optional update when their build
    /// actually cannot connect.
    /// </remarks>
    public static class VersionPolicy
    {
        /// <summary>Checks a client's reported versions against what an authority requires.</summary>
        public static VersionCompatibilityResult Evaluate(VersionSet supplied,
            VersionRequirement required)
        {
            // Protocol first: a mismatch here is not something a patch is promised to fix.
            if (!required.RequiredProtocol.IsUnset && supplied.Protocol != required.RequiredProtocol)
            {
                return VersionCompatibilityResult.For(VersionCompatibility.Incompatible,
                    VersionKind.Protocol, supplied.Protocol, required.RequiredProtocol);
            }

            if (!required.MinimumClient.IsUnset
                && supplied.Client.IsOlderThan(required.MinimumClient))
            {
                return VersionCompatibilityResult.For(VersionCompatibility.RequiredUpdate,
                    VersionKind.Client, supplied.Client, required.MinimumClient);
            }

            if (!required.MinimumContent.IsUnset
                && supplied.Content.IsOlderThan(required.MinimumContent))
            {
                // Advisory content is a nudge; mandatory content is a barrier.
                return VersionCompatibilityResult.For(
                    required.ContentIsAdvisory
                        ? VersionCompatibility.OptionalUpdate
                        : VersionCompatibility.RequiredUpdate,
                    VersionKind.Content, supplied.Content, required.MinimumContent);
            }

            if (!required.LatestClient.IsUnset
                && supplied.Client.IsOlderThan(required.LatestClient))
            {
                return VersionCompatibilityResult.For(VersionCompatibility.OptionalUpdate,
                    VersionKind.Client, supplied.Client, required.LatestClient);
            }

            if (!required.LatestContent.IsUnset
                && supplied.Content.IsOlderThan(required.LatestContent))
            {
                return VersionCompatibilityResult.For(VersionCompatibility.OptionalUpdate,
                    VersionKind.Content, supplied.Content, required.LatestContent);
            }

            return VersionCompatibilityResult.Compatible;
        }

        /// <summary>Whether a client may proceed at all.</summary>
        public static bool IsPlayable(VersionSet supplied, VersionRequirement required)
        {
            return Evaluate(supplied, required).IsPlayable;
        }
    }
}
