using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>Plain definition with no reference contract.</summary>
    internal sealed class FakeDef : IDefinition
    {
        public FakeDef(string id)
        {
            Id = new DefinitionId(id);
        }

        public DefinitionId Id { get; }
    }

    /// <summary>Second definition type, used to prove identity scope is per registry.</summary>
    internal sealed class OtherFakeDef : IDefinition
    {
        public OtherFakeDef(string id)
        {
            Id = new DefinitionId(id);
        }

        public DefinitionId Id { get; }
    }

    /// <summary>Exercises the opt-in reference extension point.</summary>
    internal sealed class ReferencingDef : IDefinition, IReferencingDefinition
    {
        private readonly DefinitionId[] _references;

        public ReferencingDef(string id, params string[] references)
        {
            Id = new DefinitionId(id);
            _references = new DefinitionId[references.Length];

            for (int i = 0; i < references.Length; i++)
            {
                _references[i] = new DefinitionId(references[i]);
            }
        }

        public DefinitionId Id { get; }

        public IEnumerable<DefinitionId> GetRequiredReferences() => _references;
    }

    /// <summary>Proves rules are pluggable and pick their own severity.</summary>
    internal sealed class AlwaysWarnsRule : IDefinitionValidationRule
    {
        public void Validate(IDefinition definition, IDefinitionLookup lookup, ValidationReport report)
        {
            report.AddWarning(ValidationCode.MissingReference, definition.Id, "rule warning");
        }
    }

    internal static class ValidationTestHelper
    {
        public static DefinitionRegistry<FakeDef> LookupWith(params string[] ids)
        {
            var registry = new DefinitionRegistry<FakeDef>();

            foreach (string id in ids)
            {
                registry.Register(new FakeDef(id));
            }

            return registry;
        }
    }
}
