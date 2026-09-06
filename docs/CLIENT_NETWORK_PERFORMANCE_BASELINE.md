# Client and network performance baseline — Phase 18.18B

What a client frame costs, what the server actually sends, and which of it was worth
changing. Every figure below came from a run; nothing is estimated, and the two places
where an instrument could not answer are named rather than filled in.

## Environment

| | |
|---|---|
| Unity | 6000.3.23f1 |
| Render pipeline | URP, quality level **High Fidelity** (`URP-HighFidelity.asset`) |
| Graphics API | Direct3D11 |
| GPU | NVIDIA GeForce RTX 3060 (8 GB) |
| CPU | AMD Ryzen 5 5600X, 12 logical cores |
| RAM | 16 GB |
| OS | Windows 10 (10.0.19045) 64-bit |
| Context | Editor PlayMode test runner, Mono, development (not a shipped player) |
| Editor standalone subtarget | **Player** |
| Test resolution | 1920 × 1080, rendered to a render texture by the fixture's own camera |
| VSync | Quality setting `vSyncCount = 1`; the fixture renders to a render texture, and the MCP test runner disables interaction throttling, so frames are not paced by the display |
| Target frame rate | `Application.targetFrameRate = 500` |

**The subtarget matters and was wrong at the start of this gate.** The editor was left in
the *Server* standalone subtarget by 18.18A1's dedicated build, which defines
`UNITY_SERVER` for editor scripts and changes which code paths compile. Every client
figure below was taken after switching it back to *Player*; the first set was discarded.

## Instruments

Availability was probed rather than assumed
(`Assets/_Game/Tests/PlayMode/ClientProfilerCounterTests.cs`, which fails if a counter the
baseline quotes disappears):

| Counter | Available | Used for |
|---|---|---|
| `Memory / GC Allocated In Frame` | yes | **all allocation figures** |
| `Memory / GC Reserved`, `GC Used`, `System Used` | yes | heap and process context |
| `Render / Batches Count`, `SetPass Calls Count`, `Draw Calls Count` | yes | draw-call load |
| `Render / Triangles Count`, `Vertices Count`, `Shadow Casters Count` | yes | geometry load |
| `Internal / Main Thread` | yes | client CPU per frame |
| `Render / CPU Render Thread Frame Time` | yes | render-thread CPU |
| `Render / GPU Frame Time` | yes | **GPU timing is available here** |
| `Scripts / Behaviour Update`, `Behaviour LateUpdate` | **no** | — |
| `Animation / Animator Count` | **no** | — |

`GC.GetTotalMemory` appears only as retained-heap evidence, never as allocation — the
correction 18.18A1 made, kept.

Network bytes come from FishNet's own `NetworkTrafficStatistics`, switched on through the
same serialized fields its inspector writes. It counts socket bytes and attributes them per
packet id. **No production send path was instrumented**: a protocol that behaves
differently when watched is not the protocol being measured.

## Fixtures

### Client (visual load)

`Assets/_Game/Tests/PlayMode/ClientPresentationPerformanceTests.cs`. One real socket client
connected to a real server, both in this process; the local player's object comes through
the production `CharacterReplicationService`; the other characters are the same production
prefab spawned unowned by the fixture, so the presentation path under measurement — the
production `CharacterVisualPresenter`, the approved catalogue, the animator, the nameplate,
the HUD, the binder and the camera director — is the shipped one.

**Why the crowd is not twenty-five real clients:** a character's object is spawned *for* its
owning connection, so twenty-five players would need twenty-five client `NetworkManager`s in
this one process, and each of them would build its own copy of all twenty-five models — 625
character visuals, which is not what a client draws. Socket load is therefore measured
separately, below, where the client count is stated.

| Fixture | Characters (visuals) | Monsters | Pets |
|---|---|---|---|
| EMPTY | 0 | 0 | 0 |
| MONSTERS | 0 | 150 | 0 |
| SMALL | 1 | 5 | 1 |
| MEDIUM | 10 | 50 | 5 |
| STRESS | 25 | 150 | 12 |

### Network (socket load)

`Assets/_Game/Tests/PlayMode/NetworkBandwidthTests.cs`. Every client is its own
`NetworkManager` with its own Tugboat socket on loopback — real connections, real
serialization, real packets, in one process.

| Fixture | Real socket clients | Characters | Monsters |
|---|---|---|---|
| NETWORK SMALL | **2** | 2 | 50 |
| NETWORK MEDIUM | **8** | 8 | 100 |
| TWO-MAPS | **2** (one per map) | 2 | 100, all on one map |

No fixture with 25 real sockets was run. The counts above are what was actually opened.

## Client baseline

180 measured frames after 60 warmup frames, at 1920 × 1080. Medians for timings, mean for
allocation.

| Fixture | Main thread | Render thread | GPU | Alloc/frame | Batches | SetPass | Triangles | Vertices | Shadow casters | Replication prep |
|---|---|---|---|---|---|---|---|---|---|---|
| EMPTY | 1.755 ms | 0.704 ms | 0.781 ms | 9,599 B | 6 | 6 | 1,685 | 5,055 | 0 | 0.001 ms |
| MONSTERS (150) | 1.974 ms | 0.722 ms | 0.903 ms | 20,547 B | 6 | 6 | 1,685 | 5,055 | 0 | 0.085 ms |
| SMALL | 2.022 ms | 0.819 ms | 1.776 ms | 8,906 B | 14 | 10 | 216,281 | 134,527 | 4 | 0.007 ms |
| MEDIUM | 2.397 ms | 0.863 ms | 2.306 ms | 12,452 B | 115 | 12 | 2,721,854 | 1,680,143 | 60 | 0.035 ms |
| STRESS | 2.878 ms | 0.907 ms | 2.762 ms | 20,481 B | 262 | 12 | 6,363,164 | 3,919,203 | 142 | 0.102 ms |

An empty Editor PlayMode frame allocates about 9.6 kB on its own account; every allocation
figure is only meaningful against that row.

**Frame budget.** Nothing here is close to 16.67 ms on this machine. The client is not CPU
or GPU bound at twenty-five visible characters and a hundred and fifty replicated monsters,
and the honest conclusion is that the interesting costs are elsewhere — in what is sent, and
in what runs every frame for no reason.

## What the measurements found

### 1. The presentation binder searched the world every frame — fixed

`WorldPresentationBinder.Poll()` ran every frame and called `FindOwned()`, which walked
every spawned network object and did a `GetComponent` on each looking for the one this
client owns. Isolated by composing the same fixture three ways:

| 150 monsters, no owned character yet | Alloc/frame | Main thread |
|---|---|---|
| No client presentation at all | 21,343 B | 1.874 ms |
| HUD, bag and camera, **no binder** | 20,498 B | 1.923 ms |
| HUD, bag and camera **with binder** | **122,363 B** | **2.237 ms** |

The binder alone was **102 kB per frame** — about six megabytes of garbage a second at 60
fps — and 0.31 ms, spent looking for something that was never in the collection being
searched.

**Change:** ask the connection, which already knows what it owns
(`ClientManager.Connection.Objects`), and use `TryGetComponent`, whose failed lookups do not
allocate in the Editor.

**After:**

| 150 monsters, no owned character yet | Alloc/frame | Main thread |
|---|---|---|
| With binder, after | **20,735 B** | **1.966 ms** |
| Without binder (reference) | 20,873 B | 1.941 ms |

−102 kB/frame, −83%, and the binder is now indistinguishable from not having one. The
worst case is exactly the case that matters: every frame before a player's character
arrives — logging in, changing map, respawning, reconnecting.

**Correctness:** `ClientWorldPresentationTests` and `ClientWorldVisualTests` cover binding,
rebinding, unbinding and the camera following only the owned character; both suites pass
unchanged. The answer is also stricter than before — the set walked *is* the objects this
connection owns, rather than every object tested for `IsOwner`.

### 2. A client receives every object in the world, including other maps — measured, not fixed

With two clients on two different maps and a hundred monsters on the first:

| | Objects received |
|---|---|
| Client on the monsters' map | 102 |
| Client on the **other** map | **102** |

The far client received every monster and every character on a map it was not on, thirty
times a second. That is bandwidth and client cost spent on a world the player is not in,
and it is also every other player's position handed to a client with no business knowing
it. FishNet applies no observer condition unless one is configured, and neither network
prefab configures one.

**An implementation was attempted in this gate and withdrawn.** Both a FishNet
`MatchCondition` and a project-owned `ObserverCondition` reading a viewpoint stamped on each
connection produced the correct cross-map result — the far client dropped from 102 objects
to 1, and server output fell from 12,327 B/s to 6,338 B/s — but the same-map correctness
tests passed in isolation and failed when the fixture ran in sequence, in ways that moved
between runs. A visibility change that cannot be shown to be stable is a change that can
hide a player from the person standing next to them, so it was reverted rather than shipped.
The measurement, the fixtures and the diagnosis stand; the change belongs to a gate that can
finish it.

### 3. Replication preparation is not a hotspot

`CharacterReplicationService.Synchronise` plus `MonsterReplicationService.Synchronise`,
timed on their own: **0.102 ms per frame** at twenty-five characters and a hundred and fifty
monsters. Both call `GetComponent` per object per tick, which is the kind of thing that
looks like a hotspot and is not one here. Left alone deliberately.

### 4. Production monsters were not replicated at all — fixed

Profiling a client's world found it empty of everything except other players.
`WorldServerBootstrap` composed `CharacterReplicationService` and **never composed
`MonsterReplicationService`**: monsters spawned, chased, fought, died and paid out entirely
on the server, and no client was ever told one existed. Every test that covered monster
replication composed the service itself, so the gap lived exactly where nothing was looking
— in the production composition.

This is a playability defect, not a performance one, and it blocked the gate rather than
being deferred by it.

**The repair is composition only.** The bootstrap gained a serialized monster prefab field,
built the *existing* `MonsterReplicationService` from the *existing* `MonsterWorldRuntime`
it already ticks, and passed it to the `WorldSimulation` parameter that has always been
there and was always null. No second service, no second authority, no client-side monster
state, no change to AI, spawning, drops or rewards. The shipped `World_Server` scene now
wires the monster prefab.

**Proved over a real socket against the shipped scene**
(`ShippedWorldMonsterReplicationTests`, 5 tests):

| | |
|---|---|
| Shipped world composes monster replication | yes |
| Server monsters spawned | 6 |
| Monster network objects the server created | 6 |
| Monster objects a **real client** received | **6** |
| Definition, map, max health, alive flag on the client | the server's own values |
| Server moves a monster 25 m | client's copy follows |
| Server halves a monster's health | client's copy follows |
| Server retires a defeated monster | client despawns it, 1 → 0 |
| Client asks to attack a monster it was sent | server validates, applies damage, and the client is told the new health |

**What this does not claim.** The monster prefab carries a network identity and **no
renderer**, because this project has no monster art. Monster objects and their state
replicate to the client; nothing is drawn. That limitation is real and stated wherever
monsters appear in this document.

### 5. The approved character models are heavy

Measured from the render counters, per visible character:

| | |
|---|---|
| Triangles | ~272,000 |
| Vertices | ~168,000 |
| Batches | ~10.4 |
| SetPass calls | constant at 10–12 regardless of character count |

At twenty-five characters that is 6.36 M triangles and 3.92 M vertices in a frame the GPU
still finishes in 2.76 ms on an RTX 3060 — so it is not a bottleneck *on this machine*, and
it would be one on a weaker GPU. `CHR_Base_Male_LOD0` and `CHR_Base_Female_LOD0` are the
only meshes that exist: **there are no LOD1 or LOD2 assets**, and no monster art of any kind
(`Art/Monsters` is empty). No production art was touched in this gate; decimation, LOD
authoring and monster art are art work for a later subgate.

That SetPass stays flat while batches scale linearly is the material story: characters share
their material and are not creating instances per character.

## Network baseline

Five seconds of a moving world per fixture, after 120 settling frames, counted by FishNet.

| Fixture | Server out | Per client | Server in | `SyncType` payload | Client objects |
|---|---|---|---|---|---|
| SMALL (2 clients, 50 monsters) | 9,278 B/s | 4,639 B/s | 20 B/s | 4,533 B/s | 52 |
| MEDIUM (8 clients, 100 monsters) | 76,558 B/s | 9,570 B/s | 78 B/s | 9,239 B/s | 108 |

Re-measured after the monster composition repair, because adding monster replication to
production could have changed the production workload. It did not change these numbers —
9,227 → 9,278 B/s and 75,014 → 76,558 B/s, within run-to-run noise — because this fixture
always composed monster replication itself. What changed is that the figures now describe
the shipped composition rather than a heavier one than production was running. The earlier
column is not preserved as a "before", because a bandwidth number measured while production
was accidentally sending nothing would flatter the server for a defect.

Message rate: the traffic callback fires on the server's own tick, observed at **30 Hz**
(150 callbacks in five seconds) — FishNet's default `TimeManager` tick rate, which this
project does not override.

Attribution by packet kind (server outbound, MEDIUM): `SyncType` 9,239 B/s, `TimingUpdate`
48 B/s, `PingPong` 38 B/s, with the socket total of 75,014 B/s covering the same payload
delivered once per client plus per-packet framing. Synchronised values are the whole of the
world traffic; nothing else is close.

**What loopback cannot tell you:** no MTU pressure, no loss, no latency, no NIC. These are
payload volumes and message rates, which is what replication decisions turn on. They are not
a throughput or latency benchmark.

## Deliberately not changed

- **Distance or grid observer scoping.** Map-level scoping is the first seam and it is not
  finished; anything finer would be built on top of it. Deferred, explicitly.
- **`DefinitionId` string ids on the wire.** They are sent once per object at spawn, not per
  tick; the per-tick traffic is floats and ints. No measurement pointed at them, and trading
  patch compatibility for bytes nobody is spending would be the wrong trade.
- **Movement replication rate.** Positions are what a moving world sends; nothing measured
  suggested redundant sends, and thresholding invites rubber-banding for bandwidth this
  project is not short of.
- **`GetComponent` in the replication services.** 0.102 ms per frame at STRESS. Recorded,
  not optimized.
- **Shadows, shadow distance, URP settings.** The GPU is not the bottleneck at the measured
  loads, and the URP assets are protected. Untouched.
- **Canvas splitting, buff-icon pooling, damage-number pooling.** The HUD screens cost
  nothing measurable next to the binder: with the binder removed, composing the HUD, the bag
  and the camera changed allocation by less than noise (20,498 B against 21,343 B). Both the
  HUD and the status bar already gate their writes on change. Nothing to fix.
- **Animator culling, offscreen skinning.** One animator and two renderers per character, no
  measured cost at the loads tested, and no production art may be modified in this gate.

## Manual playability

Automated tests are not a person clicking. What a human can currently do, read from the
shipped scenes rather than inferred from tests:

| Step | Works | Missing wiring |
|---|---|---|
| Open Login | **yes** | — |
| Type credentials | **yes** (the screen builds its fields) | — |
| Log in | **no** | `SessionScreenBase.Bind(SessionUiController)` is never called by any production code; the screens have no controller, so nothing reaches the account API |
| Select server / channel / character | **no** | same: the screens exist and are wired into `ClientFlowDriver`, but no composition root binds a session controller to them |
| Enter the world scene | **yes** (`ClientFlowDriver` loads `GameWorld`) | — |
| Connect to the world server | **no** | `GameWorld` contains no `NetworkManager`, no Tugboat transport and no `WorldClientBootstrap`; nothing in production calls `WorldPresentationBinder.Compose` |
| See the HUD | **partly** — the HUD builds and shows itself, permanently unbound | it binds to the owned character, and no character ever arrives |
| Move | **no** | movement is sent by `CharacterMovementInput` on the character object, which is never spawned for a client that never connects |
| See a replicated monster | **no, for a human** | monster replication now works and is proven over a real socket; the shipped client simply cannot connect |
| Target and attack a monster | **no, for a human** | the request path works and is proven over a real socket; there is no connected client, and no targeting UI is wired |
| See HP change / death | **no, for a human** | same |
| Receive loot and EXP | **no, for a human** | same: the server pipeline is canonical and covered, but nothing is connected to receive it |

So: **network and combat architecture work through a real client connection, and human
manual play does not** — the shipped client's world scene has no networking composed at
all. That is a client composition gap, older than this gate and outside its charter, and it
is reported here rather than quietly fixed inside a profiling gate.

## Soaks

A built **development dedicated server** (`-soak`, 14 minutes, 8 authoritative
server-runtime actors, 80 monsters) and a **real socket client** connected to it for ten
minutes, drawing through the shipped presentation and asking the server to walk every frame.

**Server, 14 minutes:** 37,725 ticks, 1,257 defeats, 9 characters while the client was
attached (8 actors + 1 real client), 35–80 monsters alive, 4 pet evolutions, 0 rewards
pending, 0 held, 0 reward applications in flight, Mono heap 2/3 → 3/4 MB, **0 errors and 0
exceptions**.

**Client, 10 minutes, 20 samples:** connected throughout, 1 network object, 1 approved model
built, 2 renderers, 1 animator, allocation flat at 8.4–8.9 kB/frame, object count 1 at the
first sample and 1 at the last, **0 errors**.

**This soak ran before the monster composition repair**, so the client observed only its own
character and it is not evidence that a client sees monsters — that is proved separately, by
`ShippedWorldMonsterReplicationTests`, over a real socket against the shipped scene. The
soak's value is the ten-minute stability of the client's receive, presentation and render
path and of the server's simulation and reward path, and that is all it is quoted for.

## Limitations

- Fewer than 25 real socket clients (2 and 8 were run, in one process, over loopback).
- Monsters have no art and are not replicated by the shipped server; no monster GPU cost
  exists to measure.
- No production LOD assets exist, so no LOD architecture was built.
- The client is an Editor PlayMode development client, not a shipped player build.
- No WAN latency or loss simulation, no multi-machine test, no 24-hour soak, no mobile
  profiling.
- Map observer scoping is measured and diagnosed but **not shipped**. Cross-map
  over-replication is confirmed and remains: a client on one map still receives every object
  on every other map, which is both bandwidth and world-state privacy. Two implementations
  were built and both produced the correct cross-map result; both showed unstable same-map
  behaviour when their fixtures ran in sequence, so neither was shipped. This is deferred,
  explicitly, and is not fixed.
- Monsters replicate to clients but have no art, so nothing about monster rendering is
  measured.
- A human cannot yet play: the shipped client world scene composes no networking. See
  **Manual playability** above.
