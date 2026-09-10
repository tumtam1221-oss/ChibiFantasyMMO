using ChibiFantasy.Backend;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Whether an API address would put a player's identity on a wire in the clear.
    /// </summary>
    /// <remarks>
    /// The traffic this question is about carries a password on the way in and a session
    /// token forever after. Getting the answer wrong in the safe direction costs a developer
    /// a minute; getting it wrong in the other direction costs a player their account and
    /// nobody finds out until afterwards. Every awkward spelling of "this machine" therefore
    /// has a test, including the ones written to look like it and not be.
    /// </remarks>
    public sealed class HttpEndpointEncryptionTests
    {
        private static bool Unsafe(string address)
            => new HttpEndpoint(address).IsUnencryptedOverNetwork;

        // ---- the ordinary answers -------------------------------------------------------

        [Test]
        public void Https_anywhere_is_fine()
        {
            Assert.That(Unsafe("https://api.example.com"), Is.False);
            Assert.That(Unsafe("https://10.0.0.5:8099"), Is.False);
            Assert.That(Unsafe("HTTPS://API.EXAMPLE.COM"), Is.False);
        }

        [Test]
        public void Plaintext_to_this_machine_is_fine()
        {
            Assert.That(Unsafe("http://127.0.0.1:8099"), Is.False);
            Assert.That(Unsafe("http://localhost:8099"), Is.False);
            Assert.That(Unsafe("http://LocalHost:8099"), Is.False);
            Assert.That(Unsafe("http://[::1]:8099"), Is.False);
        }

        [Test]
        public void Plaintext_to_anywhere_else_is_not()
        {
            Assert.That(Unsafe("http://api.example.com"), Is.True);
            Assert.That(Unsafe("http://203.0.113.7:8099"), Is.True);
        }

        [Test]
        public void An_address_nobody_configured_is_not_reported_as_unsafe()
        {
            // It is broken, which is a different problem with a different message.
            Assert.That(Unsafe(null), Is.False);
            Assert.That(Unsafe(string.Empty), Is.False);
        }

        // ---- the traps ------------------------------------------------------------------

        [Test]
        public void A_private_network_address_is_still_a_network()
        {
            // The tempting mistake: treating 10.x and 192.168.x as safe. An office LAN is
            // still somewhere traffic can be watched, and a deployment that wants plaintext
            // there should have to say so rather than have it assumed.
            Assert.That(Unsafe("http://10.0.0.5:8099"), Is.True);
            Assert.That(Unsafe("http://192.168.1.20:8099"), Is.True);
            Assert.That(Unsafe("http://172.16.4.9:8099"), Is.True);
        }

        [Test]
        public void A_host_that_merely_contains_localhost_is_not_this_machine()
        {
            Assert.That(Unsafe("http://localhost.attacker.example"), Is.True);
            Assert.That(Unsafe("http://notlocalhost"), Is.True);
            Assert.That(Unsafe("http://127.0.0.1.attacker.example"), Is.True);
        }

        [Test]
        public void Credentials_in_the_authority_do_not_disguise_the_host()
        {
            // "http://localhost@evil.example" connects to evil.example.
            Assert.That(Unsafe("http://localhost@evil.example"), Is.True);
            Assert.That(Unsafe("http://user:pass@evil.example"), Is.True);
        }

        [Test]
        public void A_path_after_the_host_does_not_confuse_it()
        {
            Assert.That(Unsafe("http://127.0.0.1:8099/api"), Is.False);
            Assert.That(Unsafe("http://evil.example/127.0.0.1"), Is.True);
        }

        [Test]
        public void A_trailing_dot_on_the_host_is_still_the_same_host()
        {
            Assert.That(Unsafe("http://localhost.:8099"), Is.False);
        }

        [Test]
        public void An_address_with_no_scheme_is_treated_as_plaintext()
        {
            // Guessing https on somebody's behalf is how a deployment ends up unencrypted
            // while believing otherwise.
            Assert.That(Unsafe("api.example.com"), Is.True);
        }

        [Test]
        public void Something_that_only_starts_with_https_is_not_https()
        {
            Assert.That(Unsafe("httpsx://api.example.com"), Is.True);
            Assert.That(Unsafe("http://https.example.com"), Is.True);
        }

        // ---- the pieces, so a failure above says which half broke -----------------------

        [Test]
        public void Plaintext_and_loopback_are_answered_separately()
        {
            var remote = new HttpEndpoint("http://api.example.com");

            Assert.That(remote.IsPlaintext, Is.True);
            Assert.That(remote.IsLoopback, Is.False);

            var local = new HttpEndpoint("https://localhost:8099");

            Assert.That(local.IsPlaintext, Is.False);
            Assert.That(local.IsLoopback, Is.True);
        }
    }
}
