# Server performance baseline — Phase 18.18A

What one authoritative world tick costs, measured on the production
`WorldSimulation` before and after this gate's optimizations.

Every number below came from a run of
`Assets/_Game/Tests/EditMode/WorldSimulationPerformanceTests.cs`, which composes the
real registries, authorities and monster runtime. Nothing here was estimated.

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

## Instrument note

`GC.GetAllocatedBytesForCurrentThread()` returns **0** on this Mono runtime — verified
directly, not assumed. An early version of this harness used it and reported every
fixture as allocation-free, which was the instrument failing rather than the code
succeeding. The harness now uses `GC.GetTotalMemory` deltas across the measured run,
divided by the tick count. That under-reports if a collection happens mid-run, so the
figure is a floor: zero from it means the run genuinely did not grow the heap.

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

Managed bytes allocated per tick:

| Fixture | Before | After | Change |
|---|---|---|---|
| EMPTY | 0 | 0 | — |
| SMALL | 0 | 0 | — |
| MEDIUM | 4,266 | **0** | −100% |
| STRESS | 22,033 | **0–136** | ~−100% |

At a 20 Hz server tick, the stress world was producing roughly 440 kB of garbage per
second with nobody doing anything unusual. It now produces effectively none.

## What was changed, and why

Both changes cite the measurement that justified them.

1. **`WorldCharacterRegistry.All()` caches its snapshot** — rebuilt only when
   membership changes. Measured: at 50 characters and 50 spawn points it was called 53
   times per tick (status clock, status publish, stat refresh, and once per spawner for
   the monster candidate gather), allocating a fresh list every time — about 22 kB of
   the 22.0 kB a stress tick produced. A *new* list is built on change rather than the
   old one being cleared, so a caller already iterating keeps a valid snapshot: the
   semantics are exactly what they were, one snapshot per change instead of one per call.

2. **`MonsterWorldRuntime.All()` caches its snapshot** — same reason. Monster
   replication walks every monster every tick, and 500 monsters is a 4 kB list rebuilt
   for nothing when none spawned or died.

3. **The monster candidate gather is memoised per map, per tick.** Measured: the gather
   is per spawner, and 50 spawners on one map rebuilt the same candidate list 50 times a
   tick. Safe within a tick by construction — nothing in the behaviour pass adds,
   removes, kills or moves a character; an attack is recorded and executed later by the
   combat pipeline. The memo is cleared at the top of every tick.

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

## Verification

- Full PHPUnit, EditMode and PlayMode suites pass unchanged.
- Rare drop rolls unchanged: one 1e-7 fruit roll and one 1e-6 card roll per boss defeat,
  solo and at party size 6.
- Dedicated server build boots `World_Server` and runs a bounded soak without exceptions.
