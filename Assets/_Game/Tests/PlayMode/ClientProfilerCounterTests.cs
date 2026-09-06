#if UNITY_EDITOR

using System.Collections;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

namespace ChibiFantasy.Tests.PlayMode
{
    /// <summary>
    /// Which performance counters this runtime actually provides.
    /// </summary>
    /// <remarks>
    /// <b>An instrument check, not a benchmark.</b> 18.18A shipped a number produced by a
    /// counter that returns zero on this runtime, and 18.18A1 had to withdraw it. The
    /// cheapest defence against repeating that is to ask every counter whether it exists
    /// before any figure is quoted from it, and to write down the answer where a reader of
    /// the baseline can check it.
    ///
    /// <b>It asserts availability, never a value.</b> Frame times and draw counts depend on
    /// what happens to be on screen in a test runner; asserting them would be brittle and
    /// would prove nothing about the game. What is asserted is that the counters the
    /// baseline document quotes are the ones this runtime reports as valid -- so a Unity
    /// upgrade that removes one fails here rather than silently reporting zero forever.
    /// </remarks>
    [TestFixture]
    internal sealed class ClientProfilerCounterTests
    {
        /// <summary>Frames sampled before a counter is believed either way.</summary>
        /// <remarks>A recorder can be valid and still be empty on the frame it was created,
        /// so availability is judged over several frames rather than one.</remarks>
        private const int Frames = 30;

        private readonly struct Probe
        {
            public Probe(ProfilerCategory category, string stat)
            {
                Category = category;
                Stat = stat;
            }

            public ProfilerCategory Category { get; }

            public string Stat { get; }
        }

        private static IEnumerable<Probe> Wanted()
        {
            yield return new Probe(ProfilerCategory.Memory, "GC Allocated In Frame");
            yield return new Probe(ProfilerCategory.Memory, "System Used Memory");
            yield return new Probe(ProfilerCategory.Memory, "Total Reserved Memory");
            yield return new Probe(ProfilerCategory.Memory, "GC Reserved Memory");
            yield return new Probe(ProfilerCategory.Memory, "GC Used Memory");

            yield return new Probe(ProfilerCategory.Render, "Batches Count");
            yield return new Probe(ProfilerCategory.Render, "SetPass Calls Count");
            yield return new Probe(ProfilerCategory.Render, "Draw Calls Count");
            yield return new Probe(ProfilerCategory.Render, "Triangles Count");
            yield return new Probe(ProfilerCategory.Render, "Vertices Count");
            yield return new Probe(ProfilerCategory.Render, "Shadow Casters Count");
            yield return new Probe(ProfilerCategory.Render, "Used Buffers Count");
            yield return new Probe(ProfilerCategory.Render, "Used Textures Count");

            yield return new Probe(ProfilerCategory.Internal, "Main Thread");
            yield return new Probe(ProfilerCategory.Render, "CPU Main Thread Frame Time");
            yield return new Probe(ProfilerCategory.Render, "CPU Render Thread Frame Time");
            yield return new Probe(ProfilerCategory.Render, "GPU Frame Time");

            yield return new Probe(ProfilerCategory.Scripts, "Behaviour Update");
            yield return new Probe(ProfilerCategory.Scripts, "Behaviour LateUpdate");
            yield return new Probe(ProfilerCategory.Animation, "Animator Count");
            yield return new Probe(ProfilerCategory.Ai, "Agent Count");
        }

        /// <summary>
        /// Reports which counters exist, and pins the two the baseline depends on.
        /// </summary>
        [UnityTest]
        public IEnumerator EveryCounterTheBaselineQuotesIsAvailableHere()
        {
            var recorders = new List<(Probe Probe, ProfilerRecorder Recorder)>();

            foreach (Probe probe in Wanted())
            {
                recorders.Add((probe,
                    ProfilerRecorder.StartNew(probe.Category, probe.Stat, Frames + 4)));
            }

            for (var i = 0; i < Frames; i++) yield return null;

            var report = new StringBuilder();
            var available = new HashSet<string>();

            for (var i = 0; i < recorders.Count; i++)
            {
                ProfilerRecorder recorder = recorders[i].Recorder;

                bool valid = recorder.Valid;
                int samples = valid ? recorder.Count : 0;
                long last = valid && samples > 0 ? recorder.LastValue : 0L;

                if (valid && samples > 0) available.Add(recorders[i].Probe.Stat);

                report.Append("[counter] ")
                    .Append(recorders[i].Probe.Stat)
                    .Append(" valid=").Append(valid)
                    .Append(" samples=").Append(samples)
                    .Append(" last=").Append(last)
                    .AppendLine();

                recorder.Dispose();
            }

            Debug.Log(report.ToString());

            // The counters the client baseline is built on. Anything else in the list above
            // is reported for the record and allowed to be absent -- these are not.
            Assert.That(available, Does.Contain("GC Allocated In Frame"),
                "the only working allocation-traffic counter on this runtime is gone");

            Assert.That(available, Does.Contain("Batches Count"),
                "no render batch counter: the rendering baseline would have no instrument");

            Assert.That(available, Does.Contain("SetPass Calls Count"));
            Assert.That(available, Does.Contain("Triangles Count"));
            Assert.That(available, Does.Contain("Vertices Count"));
        }
    }
}

#endif
