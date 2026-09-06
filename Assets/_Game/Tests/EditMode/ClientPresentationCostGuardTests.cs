using System.IO;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// The per-frame client work that must not come back.
    /// </summary>
    /// <remarks>
    /// <b>Why a source guard rather than a benchmark.</b> The defect this pins was a search:
    /// <c>WorldPresentationBinder</c> walked every spawned network object every frame,
    /// looking for the one this client owns. With a hundred and fifty monsters spawned and
    /// no owned character yet -- logging in, changing map, respawning, reconnecting -- it
    /// allocated 102 kB per frame and cost 0.31 ms. Behaviour tests cannot catch its return,
    /// because a rewritten search would still find the right object; only the shape of the
    /// code says whether it is a search at all.
    ///
    /// <b>What replaced it.</b> The connection already knows which objects it owns, so the
    /// answer is a walk of that set. This asserts the shape: the owned-object lookup asks the
    /// connection, and does not enumerate the world.
    /// </remarks>
    [TestFixture]
    internal sealed class ClientPresentationCostGuardTests
    {
        private const string Binder =
            "Assets/_Game/Scripts/Client/UI/WorldPresentationBinder.cs";

        [Test]
        public void TheOwnedCharacterIsAskedOfTheConnectionRatherThanSearchedFor()
        {
            string source = File.ReadAllText(Binder);

            Assert.That(source.Contains("_networkManager.ClientManager.Connection"), Is.True,
                "the binder no longer starts from the connection it belongs to");

            Assert.That(source.Contains("in connection.Objects"), Is.True,
                "the binder no longer walks the objects the connection owns");

            Assert.That(source.Contains("ClientManager.Objects.Spawned"), Is.False,
                "the binder is enumerating every spawned object again; at a hundred and "
                + "fifty monsters that measured 102 kB of garbage per frame");
        }

        [Test]
        public void TheLookupDoesNotUseAllocatingGetComponent()
        {
            string source = File.ReadAllText(Binder);

            Assert.That(source.Contains("TryGetComponent"), Is.True,
                "a failed GetComponent allocates in the Editor, which is where this is "
                + "profiled");

            Assert.That(source.Contains("GetComponent<CharacterNetworkEntity>()"), Is.False);
        }
    }
}
