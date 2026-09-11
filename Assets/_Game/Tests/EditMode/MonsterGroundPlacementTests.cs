using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using NUnit.Framework;
using UnityEditor;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Monsters stand on the ground, not at whatever height a database row carries.
    /// </summary>
    /// <remarks>
    /// <b>The bug this exists for.</b> <c>MonsterWorldRuntime</c> takes the map registry as
    /// an <i>optional</i> constructor argument, and the world server did not pass it. Its
    /// <c>DefinitionOf</c> therefore returned null for every map, and the code that stands a
    /// freshly spawned monster on the ground was skipped -- in silence, with no warning and
    /// no failing test. Every monster sat at the flat Y its spawn row happened to carry,
    /// which on Harbor Town's terrain buried a 0.28 m slime up to 0.15 m into a hillside.
    ///
    /// <b>Why an optional argument was the trap.</b> The project already knows that spawn
    /// height comes from the baked height field and is never authored -- that rule was
    /// learned once with players hanging in the air. Making the registry optional let a
    /// caller silently opt out of it, and one did.
    ///
    /// These tests hold the two halves: the field really does describe the terrain, and the
    /// runtime is really given what it needs to read the field.
    /// </remarks>
    [TestFixture]
    public sealed class MonsterGroundPlacementTests
    {
        private const string CataloguePath =
            "Assets/_Game/Data/Production/WorldContentCatalogue.asset";

        private static WorldContentCatalogue Catalogue()
        {
            var catalogue = AssetDatabase.LoadAssetAtPath<WorldContentCatalogue>(CataloguePath);

            Assert.That(catalogue, Is.Not.Null, CataloguePath + " is missing");

            return catalogue;
        }

        [Test]
        public void TheMapMonstersSpawnOnCarriesAHeightField()
        {
            // Without one there is nothing to stand a monster on, and the runtime quietly
            // falls back to the row's own Y.
            MapDefinition harbor = null;

            foreach (MapDefinition map in Catalogue().BuildMaps().All)
            {
                if (map.Id.Value == "map.harbor_town") harbor = map;
            }

            Assert.That(harbor, Is.Not.Null, "map.harbor_town is not in the catalogue");
            Assert.That(harbor.HeightField, Is.Not.Null,
                "no baked height field, so every monster on this map sits at its row's Y");
        }

        [Test]
        public void TheHeightFieldDescribesGroundThatIsNotFlat()
        {
            // If it sampled zero everywhere the bug above would have been invisible, and
            // this test would be proving nothing.
            MapDefinition harbor = null;

            foreach (MapDefinition map in Catalogue().BuildMaps().All)
            {
                if (map.Id.Value == "map.harbor_town") harbor = map;
            }

            Assert.That(harbor?.HeightField, Is.Not.Null);

            var sampled = new List<float>();

            for (float x = -60f; x <= 60f; x += 15f)
            {
                for (float z = -60f; z <= 60f; z += 15f)
                {
                    float y;

                    if (harbor.HeightField.TrySample(x, z, out y)) sampled.Add(y);
                }
            }

            Assert.That(sampled, Is.Not.Empty, "the height field sampled nowhere at all");

            float lowest = float.MaxValue, highest = float.MinValue;

            foreach (float y in sampled)
            {
                if (y < lowest) lowest = y;
                if (y > highest) highest = y;
            }

            Assert.That(highest - lowest, Is.GreaterThan(0.05f),
                "the ground is flat to within 5 cm across the whole map, which would make "
                + "standing a monster on it indistinguishable from ignoring it");
        }

        [Test]
        public void TheWorldServerHandsTheMapRegistryToTheMonsterRuntime()
        {
            // The wire that was missing. Asserted on the source rather than by booting a
            // server, because the failure was an argument nobody passed -- it compiled, ran,
            // and produced buried monsters.
            string path = "Assets/_Game/Scripts/Server/WorldServerBootstrap.cs";
            string source = System.IO.File.ReadAllText(path);

            int at = source.IndexOf("new MonsterWorldRuntime(", System.StringComparison.Ordinal);

            Assert.That(at, Is.GreaterThanOrEqualTo(0),
                "the world server no longer constructs a MonsterWorldRuntime");

            int close = source.IndexOf(");", at, System.StringComparison.Ordinal);

            Assert.That(close, Is.GreaterThan(at));

            string call = source.Substring(at, close - at);

            Assert.That(call, Does.Contain("maps"),
                "the monster runtime was constructed without the map registry, so it cannot "
                + "stand monsters on the ground and will silently leave them at their row's Y");
        }

        [Test]
        public void TheMapRegistryArgumentIsStillTheLastOne()
        {
            // Guards the test above from passing on a coincidence: if the parameter is
            // renamed or reordered, the substring check would keep passing while the wire
            // was wrong again.
            var constructors = typeof(ChibiFantasy.Server.MonsterWorldRuntime)
                .GetConstructors();

            Assert.That(constructors.Length, Is.EqualTo(1));

            var parameters = constructors[0].GetParameters();
            var last = parameters[parameters.Length - 1];

            Assert.That(last.Name, Is.EqualTo("maps"));
            Assert.That(last.ParameterType,
                Is.EqualTo(typeof(IDefinitionRegistry<MapDefinition>)));
        }
    }
}
