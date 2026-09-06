# Server performance baseline — Phase 18.18A, corrected in 18.18A1

What one authoritative world tick costs, measured on the production `WorldSimulation`
before and after 18.18A's optimizations.

**18.18A reported one figure it had not measured.** It presented `GC.GetTotalMemory`
deltas as "managed bytes allocated per tick" and concluded that a stress tick had gone
from 22,033 B/tick to 0 B/tick. `GetTotalMemory` reports *retained* heap, not allocation
traffic: garbage a tick creates and drops never appears in it. The "0 B/tick" claim is
withdrawn. 18.18A1 re-measured the same code on a real allocation counter and the
optimization is still large — but it is a 76–87% reduction, not elimination, and the
residual is recorded below rather than rounded away.

Four different things are measured here, on three different instruments, and they are
kept apart on purpose:

| Measurement | Instrument | Where |
|---|---|---|
| CPU per tick | `Stopwatch` around `WorldSimulation.Tick` | `WorldSimulationPerformanceTests` (EditMode) |
| Allocation traffic per tick | `ProfilerRecorder(ProfilerCategory.Memory, "GC Allocated In Frame")` | `WorldSimulationAllocationTests` (PlayMode) |
| Retained managed heap | `GC.GetTotalMemory` delta across a run | `WorldSimulationPerformanceTests` (EditMode) |
| Process memory over hours | Windows working set / private bytes of the built server | active dedicated soak (below) |

Nothing here was estimated.

## Environment

| | |
|---|---|
| Unity | 6000.3.23f1 |
| Context | Editor test runner (EditMode), Mono, Windows 10 |
| Scripting backend | Mono (Editor) |
| Dedicated process | Windows/Linux dedicated builds verified separately (see below) |
| Warmup | 60 ticks |
| Samples | 240 ticks |
| Tick delta | 0.05 s |

## Fixtures

Characters are admitted through the production `WorldSimulation.Admit`. Monsters are
authored aggressive with a 12 m detection range and spread ten per spawn point, so the
per-spawner candidate gather and the per-monster target scan are both exercised.

| Fixture | Characters | Monsters | Spawn points |
|---|---|---|---|
| EMPTY | 0 | 0 | 0 |
| SMALL | 1 | 20 | 2 |
| MEDIUM / IDLE | 20 | 200 | 20 |
| STRESS | 50 | 500 | 50 |

**These are server-runtime characters, not real network clients.** The simulation is
the real one; the sockets are not. Real FishNet clients are exercised separately by the
PlayMode suites at a much smaller scale, and replication is not composed in these
fixtures at all — it needs a live `NetworkManager`. So these figures are *simulation*
cost, not total server cost.

## Instrument notes

Three counters were tried. Two of them cannot answer "how much does a tick allocate" on
this runtime, and saying so is part of the baseline:

- **`GC.GetAllocatedBytesForCurrentThread()` returns 0 here.** Verified directly by
  allocating ten thousand objects and reading a delta of zero — not assumed. The first
  18.18A harness used it and reported every fixture as allocation-free, which was the
  instrument failing rather than the code succeeding.
- **`GC.GetTotalMemory` measures retained heap, not allocation.** It is still recorded,
  under its own name, as a leak check: a run that ends holding no more than it started
  with did not leak. It cannot see garbage that was created and collected, so it must
  never be reported as bytes allocated. That mistake is what 18.18A1 exists to correct.
- **`ProfilerRecorder(ProfilerCategory.Memory, "GC Allocated In Frame")` works.** It is
  the real allocation counter and every allocation figure below comes from it. It samples
  at frame boundaries, so it only exists in PlayMode or a player — EditMode has no
  frames. The fixture therefore measures a busy world against an idle world over equal
  frame counts and attributes the difference to the ticks it ran:
  `perTick = (busyFrameTotal − idleFrameTotal) / (frames × ticksPerFrame)`.

The Unity Performance Testing package is not installed in this project, so no
`Unity.PerformanceTesting` measurement was available.

## Before → after

Median tick, microseconds (lower is better):

| Fixture | Before | After | Change |
|---|---|---|---|
| EMPTY | 0.30 | 0.10 | −67% |
| SMALL | 8.50 | 7.70 | −9% |
| MEDIUM | 277.20 | 236.40 | **−15%** |
| STRESS | 1681.70 | 1444.40 | **−14%** |

p95 tick, microseconds:

| Fixture | Before | After |
|---|---|---|
| MEDIUM | 298.20 | 243.70 |
| STRESS | 1879.70 | 1497.10 |

### Allocation traffic per tick — measured in 18.18A1 on a real counter

Measured in PlayMode with `"GC Allocated In Frame"`, 30 warmup frames then 120 measured
frames at 20 ticks per frame, against an idle-world baseline of the same length. "Before"
was produced by reverting `WorldCharacterRegistry` and `MonsterWorldRuntime` to their
pre-18.18A state (`git show HEAD~1`) and re-running the same fixture — not estimated
after the fact.

| Fixture | Before | After | Change |
|---|---|---|---|
| EMPTY | 0 B | 0 B | — |
| MEDIUM (20 characters, 200 monsters) | 7,975 B | **1,626–1,888 B** | **−76%** |
| STRESS (50 characters, 500 monsters) | 31,298 B | **4,105–4,141 B** | **−87%** |

At a 20 Hz tick the stress world was producing roughly 626 kB of garbage a second and now
produces about 82 kB. That is a large saving and it is not zero. The **~4.1 kB that
remains at STRESS is roughly 8 bytes per monster per tick and is currently
unattributed** — no claim is made about where it comes from, and finding it is 18.18B's
work, not a number to round down to zero here.

### Retained managed heap — a leak check, not allocation

Measured in EditMode as `GC.GetTotalMemory` growth across 240 ticks, divided by the tick
count. Reported under its own name because that is all it can honestly carry:

| Fixture | Retained per tick |
|---|---|
| EMPTY | 0 B |
| SMALL | 0 B |
| MEDIUM / IDLE | 0 B |
| STRESS | 0–17 B |

A run ending where it started means the world is not accumulating; it says nothing about
what the run allocated and released in between.

## What was changed, and why

Both changes cite the measurement that justified them.

1. **`WorldCharacterRegistry.All()` caches its snapshot** — rebuilt only when
   membership changes. Measured: at 50 characters and 50 spawn points it was called 53
   times per tick (status clock, status publish, stat refresh, and once per spawner for
   the monster candidate gather), allocating a fresh list every time. On the corrected
   allocation counter the three changes together removed 27.2 kB of the 31.3 kB a stress
   tick produced. A *new* list is built on change rather than the old one being cleared,
   so a caller already iterating keeps a valid snapshot: the semantics are exactly what
   they were, one snapshot per change instead of one per call.

   Proven structurally as well as by measurement, in
   `Assets/_Game/Tests/EditMode/SimulationSnapshotCachingTests.cs`: `All()` returns the
   same instance while membership is unchanged, a new one when somebody arrives or
   leaves, and a snapshot already handed out is never mutated underneath its caller.

2. **`MonsterWorldRuntime.All()` caches its snapshot** — same reason. Monster
   replication walks every monster every tick, and 500 monsters is a 4 kB list rebuilt
   for nothing when none spawned or died.

3. **The monster candidate gather is memoised per map, per tick.** Measured: the gather
   is per spawner, and 50 spawners on one map rebuilt the same candidate list 50 times a
   tick. Safe within a tick by construction — nothing in the behaviour pass adds,
   removes, kills or moves a character; an attack is recorded and executed later by the
   combat pipeline. The memo is cleared at the top of every tick.

   The saving is an absence of work, which a test cannot assert without being able to
   count it, so `MonsterWorldRuntime.CandidateGathers` counts actual rebuilds. It changes
   no behaviour and nothing reads it to decide anything. Twelve spawn points on one map
   now produce exactly one gather per tick, and three ticks produce exactly three.

## What was deliberately not changed

- **Squared distances in monster AI.** Already done: `MonsterAiController` uses
  `SqrDistanceTo` throughout, with no `sqrt` in the hot path. Nothing to fix.
- **Target re-selection.** The AI already keeps its current target instead of
  re-picking the nearest each tick. Changing it would change behaviour, not cost.
- **`MonsterTickResult.Attacking`'s array.** It allocates only when monsters actually
  want to attack, and the measured fixtures showed no per-tick allocation from it.
  Handing out the live list instead would risk a caller holding a buffer that is cleared
  next tick — a real aliasing bug traded for an unmeasured saving.
- **AI think-rate throttling.** No authored think interval exists, and inventing one
  would change when a monster reacts. Out of scope for a gate that must not alter
  gameplay.
- **Stat recomputation.** Already signature-gated by 18.8A: a tick where nothing changed
  recomputes nothing. Measurement confirmed no per-tick recompute cost to remove.
- **Status ticking, spawn processing, reward idle retry.** All already short-circuit on
  empty collections; none appeared in the measured cost.
- **Replication and client rendering.** Out of scope for this gate.
- **PHP/MySQL.** No server-side measurement pointed at persistence, so nothing was
  touched.

## Remaining cost

At STRESS the tick is dominated by monster AI: 500 monsters each considering 50
candidates is 25,000 distance comparisons per tick, and that arithmetic is the work
itself rather than waste around it. Reducing it needs spatial partitioning, which is a
design change with its own correctness surface — deliberately not attempted here.

## Active dedicated soak — added in 18.18A1

18.18A ran a 10.2-minute dedicated process that was **idle**: it booted the world and
nothing happened in it, so it proved the process starts and nothing else. This is the
replacement.

**What ran.** A Windows dedicated server built by
`GameBuilder.BuildDevelopmentDedicatedServer` — the production scene, the production
subtarget, one flag apart from what ships — started with:

```
ChibiFantasyServer.exe -batchmode -nographics -soak -soakMinutes=11 \
    -soakCharacters=12 -soakMonsters=100 -soakSampleSeconds=30 \
    -soakMap=<map> -soakMonster=<monster> -soakPet=<pet> \
    -soakEvolvedPet=<evolved pet> -soakClass=<class>
```

The content ids are arguments because the harness names none of its own: a default
written down in code would be a second place the world's ids live. A run that is not told
what to drive refuses to start and says which flags are missing.

`Assets/_Game/Scripts/Server/WorldSoakHarness.cs` drove it. The harness is a source of
input, not a second simulation: it recomposes the shipped world through
`WorldServerBootstrap.Compose` with in-memory stores, admits everybody through
`WorldSimulation.Admit`, swings through the shipped combat pipeline, and lets
`MonsterRewardAuthority` and `CharacterPetAuthority` decide every reward and every pet
action. It is compiled only under `DEVELOPMENT_BUILD || UNITY_EDITOR` and does nothing at
all without `-soak`, both pinned by tests in `ServerEntryTests`.

**These are 12 authoritative server-runtime actors, not 12 connected players.** No
sockets, no `NetworkConnection`s, no replication for them. The simulation, the rewards
and the persistence are real; the transport is not exercised.

**What happened over 11 minutes (660 s, 29,636 ticks):**

| | at 0 s | at 660 s |
|---|---|---|
| Characters in the registry | 12 | 12 |
| Monsters alive | 100 | 55 |
| Spawners | 25 | 25 |
| Pets summoned | 12 | 11 |
| Active statuses (pet auras) | 6 | 7 |
| Pet evolutions granted by the authority | 0 | 2 |
| Defeats | 0 | 987 |
| Rewards recorded | 0 | 987 |
| Rewards pending | 0 | **0** |
| Rewards held for retry | 0 | **0** |
| Reward applications in flight | 0 | **0** |
| Mono used / heap | 2 / 3 MB | 3 / 4 MB |
| Errors, asserts, exceptions | 0 | **0** |

Recurring activity across the run: continuous movement for every actor, monster
detection and chase, 987 melee defeats through the combat pipeline, 987 reward envelopes
created and completed, character and pet experience granted and stamped, pet
deactivate/reactivate cycles, two live evolutions through `CharacterPetAuthority.Evolve`,
and six pets seeded in the evolved aura form so `status.lumi_aura` was applied by the
canonical status service throughout.

**Process memory, sampled every 30 s from outside the process:**

| | first sample | last sample |
|---|---|---|
| Working set | 173 MB | 176 MB |
| Private bytes | 287 MB | 289 MB |
| Handles | 439 | 429 |
| Threads | 52 | 48 |
| CPU time consumed | 0.7 s | 2.3 s |

Private bytes moved by 2 MB across eleven minutes and 987 full defeat-to-reward cycles.
Handles and threads fell rather than grew. Nothing in the reward outbox, the application
ledger, the monster runtime, the character registry or the status runtime accumulated.

The run was performed twice — once while the harness still carried content defaults, and
again against the committed source after those were removed — and the two agree to within
17 ticks and 0 defeats.

**What this soak does not show.** It is eleven minutes, not eleven hours, and its
persistence is in memory rather than MySQL — a leak in the HTTP stores or in the database
would not appear here. It drives no sockets, so nothing about FishNet's own buffers is
measured. Both are stated rather than implied.

## Verification

- Full PHPUnit, EditMode and PlayMode suites pass unchanged.
- Rare drop rolls unchanged: one 1e-7 fruit roll and one 1e-6 card roll per boss defeat,
  solo and at party size 6.
- Dedicated server build boots `World_Server` and runs a bounded soak without exceptions.
- 18.18A1: an **active** 11-minute dedicated soak, above, with 987 defeats and zero
  errors, zero pending rewards and zero ledger residue.
- CPU figures reproduced in 18.18A1 on the committed code: SMALL 7.70 µs, MEDIUM
  236.10 µs, STRESS 1,444.60 µs median — within noise of the 18.18A "after" column.
