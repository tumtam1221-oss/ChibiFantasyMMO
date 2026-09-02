using System;
using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Tests.EditMode
{
    /// <summary>
    /// Test-only persistent state. Deliberately generic: it models no game concept, it
    /// only proves the boundary works.
    /// </summary>
    [Serializable]
    internal sealed class TestPersistentState : IPersistentState
    {
        [SerializeField] private InstanceId _instanceId;
        [SerializeField] private DefinitionId _definitionId;
        [SerializeField] private OwnerId _owner;
        [SerializeField] private Revision _revision;
        [SerializeField] private int _value;

        public TestPersistentState()
        {
        }

        public TestPersistentState(InstanceId instanceId, DefinitionId definitionId, OwnerId owner, int value)
        {
            _instanceId = instanceId;
            _definitionId = definitionId;
            _owner = owner;
            _revision = Revision.Initial;
            _value = value;
        }

        public InstanceId InstanceId => _instanceId;

        public DefinitionId DefinitionId => _definitionId;

        public OwnerId Owner => _owner;

        public Revision Revision => _revision;

        public int Value => _value;

        public void SetValue(int value)
        {
            _value = value;
            _revision = _revision.Next();
        }
    }

    /// <summary>
    /// Test-only runtime state. Carries no serialization attributes, showing that runtime
    /// state is not obliged to be persistable.
    /// </summary>
    internal sealed class TestRuntimeState : IRuntimeState
    {
        public TestRuntimeState(int value)
        {
            Value = value;
        }

        public int Value { get; set; }

        public Revision Revision { get; private set; }

        public void Advance()
        {
            Revision = Revision.Next();
        }
    }

    /// <summary>
    /// Immutable state, the shape for which the container boundary is airtight: there is
    /// no way to change it except by producing a new one.
    /// </summary>
    internal sealed class ImmutableTestState
    {
        public ImmutableTestState(int value)
        {
            Value = value;
        }

        public int Value { get; }

        public ImmutableTestState WithValue(int value) => new ImmutableTestState(value);
    }

    /// <summary>Mutable state, used to show what the container cannot enforce.</summary>
    internal sealed class MutableTestState
    {
        public MutableTestState(int value)
        {
            Value = value;
        }

        public int Value { get; set; }
    }
}
