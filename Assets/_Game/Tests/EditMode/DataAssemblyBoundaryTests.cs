using System;
using System.Reflection;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using NUnit.Framework;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Guards the architectural boundaries that the assembly graph is supposed to enforce.
    /// These fail loudly if a definition ever pulls in networking, gameplay or backend code.
    /// </summary>
    public sealed class DataAssemblyBoundaryTests
    {
        private static readonly string[] ForbiddenForData =
        {
            "FishNet",
            "ChibiFantasy.Network",
            "ChibiFantasy.Gameplay",
            "ChibiFantasy.Client",
            "ChibiFantasy.Server",
            "ChibiFantasy.UI",
            "ChibiFantasy.Backend",
            "ChibiFantasy.Contracts"
        };

        [Test]
        public void DataAssembly_DoesNotReferenceForbiddenAssemblies()
        {
            Assembly data = typeof(GameDefinition).Assembly;

            foreach (AssemblyName referenced in data.GetReferencedAssemblies())
            {
                foreach (string forbidden in ForbiddenForData)
                {
                    Assert.IsFalse(
                        referenced.Name.StartsWith(forbidden, StringComparison.Ordinal),
                        "Data must not reference " + referenced.Name);
                }
            }
        }

        [Test]
        public void DataAssembly_ReferencesCore()
        {
            Assembly data = typeof(GameDefinition).Assembly;
            bool referencesCore = false;

            foreach (AssemblyName referenced in data.GetReferencedAssemblies())
            {
                if (referenced.Name == "ChibiFantasy.Core")
                {
                    referencesCore = true;
                }
            }

            Assert.IsTrue(referencesCore, "Data is expected to reference Core.");
        }

        [Test]
        public void CoreAssembly_HasNoProjectReferences()
        {
            Assembly core = typeof(DefinitionId).Assembly;

            Assert.AreEqual("ChibiFantasy.Core", core.GetName().Name);

            foreach (AssemblyName referenced in core.GetReferencedAssemblies())
            {
                Assert.IsFalse(
                    referenced.Name.StartsWith("ChibiFantasy.", StringComparison.Ordinal),
                    "Core must stay free of project dependencies, found " + referenced.Name);
                Assert.IsFalse(
                    referenced.Name.StartsWith("FishNet", StringComparison.Ordinal),
                    "Core must not reference FishNet.");
            }
        }

        [Test]
        public void DefinitionsAreDiscoverableThroughRegistry()
        {
            var rarity = UnityEngine.ScriptableObject.CreateInstance<RarityDefinition>();
            var stat = UnityEngine.ScriptableObject.CreateInstance<StatDefinition>();
            try
            {
                UnityEngine.JsonUtility.FromJsonOverwrite("{\"_id\":{\"_value\":\"rarity.legendary\"}}", rarity);
                UnityEngine.JsonUtility.FromJsonOverwrite("{\"_id\":{\"_value\":\"stat.str\"}}", stat);

                var registry = new DefinitionRegistry<GameDefinition>(
                    new GameDefinition[] { rarity, stat });

                Assert.AreEqual(2, registry.All.Count);
                Assert.IsTrue(registry.Contains(new DefinitionId("rarity.legendary")));
                Assert.IsTrue(registry.TryGet(new DefinitionId("stat.str"), out GameDefinition found));
                Assert.AreSame(stat, found);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(rarity);
                UnityEngine.Object.DestroyImmediate(stat);
            }
        }
    }
}
