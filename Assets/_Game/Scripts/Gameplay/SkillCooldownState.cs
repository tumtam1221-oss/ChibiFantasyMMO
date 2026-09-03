using System.Collections.Generic;
using ChibiFantasy.Core;

namespace ChibiFantasy.Gameplay
{
    /// <summary>
    /// How long until each skill may be used again.
    /// </summary>
    /// <remarks>
    /// <b>Runtime, never persistent.</b> Implements <see cref="IRuntimeState"/> and
    /// deliberately not <see cref="IPersistentState"/>, and carries no serialization
    /// attribute. <see cref="CharacterSkillsState"/> is explicit that a cooldown is a
    /// runtime concern and that storing one would put combat timing into save data; this
    /// is where it goes instead. Losing it on reconnect is acceptable and intended.
    ///
    /// <b>Why it exists at all.</b> The skill schema authors
    /// <c>SkillLevelEntry.CooldownSeconds</c>, so without somewhere to hold the remaining
    /// time a skill with a ten second cooldown could be used every frame. That is the same
    /// gap <see cref="AttackStateMachine"/> fills for basic attacks, and this is its
    /// smallest equivalent: a remaining time per skill, nothing more. There is no charge
    /// system, no category or global cooldown, no haste and no cooldown reduction, because
    /// nothing in the schema describes any of them yet.
    ///
    /// <b>The caller supplies the time.</b> <see cref="Advance"/> takes a delta rather than
    /// reading <c>UnityEngine.Time</c>, keeping this assembly engine-free and the behaviour
    /// reproducible at exact durations.
    ///
    /// <b>Only real transitions bump the revision</b>, matching
    /// <see cref="CharacterResourceState"/>: advancing time on an empty set changes
    /// nothing, so the counter tracks state changes rather than frames.
    /// </remarks>
    public sealed class SkillCooldownState : IRuntimeState
    {
        /// <summary>Below this, a cooldown is finished. See <see cref="AttackStateMachine"/> for why an epsilon is needed at all.</summary>
        private const float SettleEpsilon = 1e-5f;

        private readonly Dictionary<DefinitionId, float> _remaining =
            new Dictionary<DefinitionId, float>();

        private readonly List<DefinitionId> _finished = new List<DefinitionId>();

        private Revision _revision;

        public Revision Revision => _revision;

        /// <summary>How many skills are currently cooling down.</summary>
        public int Count => _remaining.Count;

        /// <summary>Whether a skill may be used right now as far as cooldown is concerned.</summary>
        /// <remarks>A skill that was never started is ready, so an empty state permits
        /// everything rather than nothing.</remarks>
        public bool IsReady(DefinitionId skill)
        {
            return !_remaining.ContainsKey(skill);
        }

        /// <summary>Seconds left, or zero when the skill is ready.</summary>
        public float GetRemaining(DefinitionId skill)
        {
            return _remaining.TryGetValue(skill, out float value) ? value : 0f;
        }

        /// <summary>
        /// Puts a skill on cooldown.
        /// </summary>
        /// <remarks>A non-positive or non-finite duration leaves the skill ready rather
        /// than storing a cooldown that would never expire. Restarting an active cooldown
        /// replaces it rather than adding to it.</remarks>
        public void Begin(DefinitionId skill, float seconds)
        {
            if (!skill.IsValid) return;

            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds <= SettleEpsilon)
            {
                // Nothing to track. Clear any existing entry so the skill is genuinely ready.
                if (_remaining.Remove(skill)) _revision = _revision.Next();
                return;
            }

            _remaining[skill] = seconds;
            _revision = _revision.Next();
        }

        /// <summary>
        /// Moves every cooldown forward.
        /// </summary>
        /// <remarks>Ignores non-finite and non-positive deltas, so a corrupt frame time
        /// cannot wind a cooldown backwards or freeze it at NaN.</remarks>
        public void Advance(float deltaSeconds)
        {
            if (_remaining.Count == 0) return;

            if (float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds) || deltaSeconds <= 0f)
            {
                return;
            }

            _finished.Clear();

            // Collect first: a dictionary cannot be written while it is being enumerated.
            foreach (KeyValuePair<DefinitionId, float> pair in _remaining)
            {
                if (pair.Value - deltaSeconds <= SettleEpsilon) _finished.Add(pair.Key);
            }

            if (_finished.Count == _remaining.Count)
            {
                _remaining.Clear();
                _revision = _revision.Next();
                return;
            }

            var keys = new List<DefinitionId>(_remaining.Keys);

            for (int i = 0; i < keys.Count; i++)
            {
                _remaining[keys[i]] = _remaining[keys[i]] - deltaSeconds;
            }

            for (int i = 0; i < _finished.Count; i++)
            {
                _remaining.Remove(_finished[i]);
            }

            _revision = _revision.Next();
        }

        /// <summary>Clears one skill's cooldown.</summary>
        public void Clear(DefinitionId skill)
        {
            if (_remaining.Remove(skill)) _revision = _revision.Next();
        }

        /// <summary>Clears everything. For death, despawn and character swaps.</summary>
        public void Reset()
        {
            if (_remaining.Count == 0) return;

            _remaining.Clear();
            _revision = _revision.Next();
        }
    }
}
