# ChibiFantasyMMO Project Status

> Living status document. Update it at the end of every phase or gate.
> Last updated: 2026-09-13 — Phase 19 in progress; gate 19E (basic punch, attack
> speed, avatar posture) closed and on `main`. Next gate: 19F.

---

## 1. Status Summary

| Area | State |
|---|---|
| **Current major phase** | **Phase 19 — first playable world** (open; see §5) |
| **Last closed gate** | Phase 19E — Basic Punch / ASPD / male-female avatar T-pose fix (`b608938`, merged to `main` in PR #3) |
| **Next intended gate** | **Phase 19F — First Playable Loop Integration / Closure** (not started) |
| **Unity** | 6000.3.23f1, URP 17.3.0, Force Text serialization, Input System 1.20.0 + legacy manager |
| **Networking** | FishNet 4.7.2 (`com.firstgeargames.fishnet`, pinned tag), server-authoritative; dedicated Windows/Linux server builds |
| **Backend** | PHP 8 API (`backend/`, 22 migrations) over MySQL 8.4; own auth (`password_hash`, random session tokens); dev API on 127.0.0.1:8099, dev DB `chibifantasy_integration` on port 3307 |
| **Blender** | 5.1.2 via MCP; source `.blend`/scratch files live in `Blender/` and are **not** version-controlled |
| **Tests** | EditMode suite 3797/3797 green at `b608938` (last full run 2026-09-13); PlayMode suite exists (`Assets/_Game/Tests/PlayMode`) |
| **Builds** | `Builds/WindowsClient` (client), `Builds/WindowsServerDev` (the dev world server actually run), `Builds/WindowsServer` / `Builds/LinuxServer*` (release/perf-baseline servers). Build outputs are ignored by git. |
| **Version control** | Git + Git LFS (13 binary formats, see §3). Remote: `origin` = github.com/tumtam1221-oss/ChibiFantasyMMO. Mainline: `main`. |

### Environment detail

- **Unity project root:** `E:\GameDev\ChibiFantasyMMO`
- **Scenes in build settings:** `Login`, `ServerSelect`, `ChannelSelect`, `CharacterSelect`, `GameWorld` (`Assets/_Game/Scenes/Client/`), `World_Server` (`Assets/_Game/Scenes/World/`), plus the URP template `SampleScene`.
- **Per-machine packages (git-ignored, install locally):** ToonScapes (environment), Kevin Iglesias Human Animations (death placeholder only).
- **Dev stack after a reboot:** MySQL 8.4.9 portable (3307) → PHP API (8099) → `Builds/WindowsServerDev/ChibiFantasyServer.exe` (UDP 7770).

---

## 2. Current Project Structure

```
E:\GameDev\ChibiFantasyMMO\
├── PROJECT_STATUS.md                 # this document
├── docs/                             # NETWORKING.md, PERFORMANCE_BASELINE.md,
│                                     # CLIENT_NETWORK_PERFORMANCE_BASELINE.md
├── backend/                          # PHP API: public/, src/, database/migrations (22),
│                                     # tests/ (PHPUnit); .env and storage/ fixtures ignored
├── Assets/
│   ├── Settings/                     # URP pipeline assets (template)
│   ├── Scenes/SampleScene.unity      # URP template scene (kept)
│   └── _Game/                        # ALL first-party content
│       ├── README.md                 # conventions (source of truth)
│       ├── Art/Characters/Production # Meshy male/female rigs + clips (Idle, Run, Run02,
│       │                             # GuardIdle, CrossPunch), Shared/ Mixamo source clip
│       ├── Art/Monsters, NPC, Environment, Items, UI, VFX, Weapons
│       ├── Data/Production/          # 65 ScriptableObject definitions + WorldContentCatalogue
│       │                             # (cards, classes, devil fruits, drop tables, formulas,
│       │                             # items, maps, monsters, NPCs, pets, portals, progression,
│       │                             # quests, shops, skills, spawns, stats, status effects)
│       ├── Prefabs/                  # Network/, Presentation/, Prototype/ (animator controllers)
│       ├── Scenes/                   # Client/ (5 client scenes), World/ (server scene),
│       │                             # Prototype/, Validation/
│       ├── Scripts/                  # Backend, Character, Client, Contracts, Core, Data,
│       │                             # Editor, Gameplay, Network, Server, UI (one asmdef each)
│       └── Tests/                    # EditMode/ (217 files), PlayMode/ (36 files)
├── Blender/                          # (ignored) .blend sources, retarget/export scripts
├── Builds/                           # (ignored) client + server builds
├── Packages/                         # manifest.json + packages-lock.json
└── ProjectSettings/
```

---

## 3. Version-control facts

**Tracked by LFS — 13 binary formats:**

```
*.fbx  *.blend  *.glb  *.gltf          # 3D models
*.png  *.jpg  *.jpeg  *.tga  *.psd     # textures / source art
*.wav  *.mp3  *.ogg  *.mp4             # audio / video
```

**Deliberately NOT in LFS:** `*.asset`, `*.unity`, `*.prefab`, `*.mat`, `*.anim`
(Force Text YAML: readable diffs, three-way merge, `UnityYAMLMerge`).

**Working-tree noise that is never committed:** editor-churned settings
(`Assets/Settings/URP-*.asset`, `DefaultVolumeProfile.asset`,
`UniversalRenderPipelineGlobalSettings.asset`, `ProjectSettings/EditorSettings.asset`,
`SceneTemplateSettings.json`, `BurstAotSettings*`), the template `SampleScene.unity`,
`Assets/_Game/Scenes/Prototype/Proto_Inventory.unity`, and local scratch
(`Assets/Screenshots/`, `Assets/_Game/Art/Characters/Validation/`, `Assets/_Recovery/`,
`Assets/_Temp_HitchProbe/`, `Assets/InitTestScene*`, `Blender/`).

**Untracked animation files left on disk on purpose (2026-09-13):**
`MaleMeshy/CHR_Male_Meshy@BasicPunch.fbx`, `FemaleMeshy/CHR_Female_Meshy@BasicPunch.fbx`
(the retired hand-authored v12 punch) and `Shared/HookPunch.fbx` (a rejected trial).
Nothing in the project references them; `AttackSpeedAndMeleeRangeTests` asserts they
are not wired in. Delete or keep locally — they do not belong in the repository.

---

## 4. Architectural Decisions

| # | Decision | Status |
|---|---|---|
| 1 | Networking model | **DECIDED, Phase 16.** Server-authoritative FishNet 4.7.2. The client is authoritative for nothing. `docs/NETWORKING.md`. |
| 2 | Backend & database | **DECIDED, Phases 15–16.** PHP API over MySQL, own auth. Hosting still undecided. |
| 3 | Git LFS scope | **DECIDED 2026-09-02:** binary formats only (§3). |
| 4 | Unity version | **SETTLED IN PRACTICE:** 6000.3.23f1 / URP 17.3.0. LTS move still open. |
| 5 | Git remote | **DONE:** GitHub `origin`, mainline `main`, feature work merged by PR. |
| 6 | Character rig standard | **SETTLED IN PRACTICE:** Meshy male/female, Unity Humanoid, clips authored/retargeted in Blender (`Blender/Characters/`), one FBX per clip with the avatar copied from the model. The avatar T-pose is enforced in the model importer **and** mirrored into every `@clip` importer (Phase 19E). |
| 7 | Input System vs. legacy Input Manager | Both present (`com.unity.inputsystem` 1.20.0 installed; legacy manager still active). Not formally decided. |
| 8 | Asset loading strategy (Addressables vs. `Resources/` vs. direct references) | Open — direct references and the `WorldContentCatalogue` so far. |
| 9 | Tag & layer taxonomy / collision matrix | Open. |
| 10 | UI toolkit | uGUI in practice for every shipped screen; not formally decided. |

---

## 5. Phase Log

Each phase is one or more commits; the commit messages carry the reasoning and the
failures found along the way.

- [x] **Phase 00 — Environment audit.**
- [x] **Phase 01 — Foundation / version control.** Git, Unity `.gitignore`, Git LFS, baseline commit.
- [x] **Phases 02–06 — Characters and content foundation.** Male and female production
      characters (Humanoid), definitions, registries, progression, skills.
- [x] **Phase 07 — Character controller and combat.**
- [x] **Phase 08 — Inventory, equipment, storage.**
- [x] **Phase 09 — Equipment enhancement.** Rarity, status stones, enchanting, fusion.
- [x] **Phase 10 — Monsters, drops, loot, quests.**
- [x] **Phase 11 — Maps, cities, NPCs, portals, travel.**
- [x] **Phase 12 — Devil Fruit, cards, pets.**
- [x] **Phase 13 — Party, guild, trade, player shop, economy.**
- [x] **Phase 14 — Login, session, server/channel/character select.**
- [x] **Phase 15 — PHP API + MySQL backend.**
- [x] **Phase 16 — Real networking.** `UnityWebRequest` transport, live PHP integration,
      FishNet world entry, authoritative character spawn. `docs/NETWORKING.md`.
- [x] **Phase 17 (17.1–17.24) — Server authority.** Production FishNet world bootstrap,
      authoritative spawn from persistence, travel, monster runtime/movement/AI/spawn,
      combat command resolution, PK gate, monster EXP and item-drop/loot authority.
- [x] **Phase 18 (18.1–18.18B1) — Production pipeline.** Server combat pipeline;
      authoritative character/movement/inventory/equipment/status/derived-stat replication;
      client UI flow and world presentation; Meshy production characters and locomotion;
      magic combat + MDEF; Devil Fruit live state/persistence and world boss drop chain;
      party persistence, round-robin loot and boss rewards; crash-safe durable rewards and
      loot pickup; card socketing and boss card drop; pet ownership/experience/evolution
      aura; dedicated server entry and build bootstrap; server and client performance
      baselines (`docs/*BASELINE.md`); manual playable client flow.
- [ ] **Phase 19 — First playable world.** OPEN. Gates landed on `main` so far
      (PRs #1–#3, 2026-09-08 → 2026-09-13):
  - [x] **19 — World data backbone** (`e718ab2`): `map.harbor_outskirts`, spawns, portals,
        `npc.harbor_guide`, `quest.harbor_first_hunt`, `item.slime_gel`; shipping ground
        collider; skill scaling fix.
  - [x] **Running stutter fix** (`9029805`): position replicated every tick, not every 100 ms.
  - [x] **Harbor Town production world** (`a20b7e9` … `f18b033`): walkable town, saved
        character position (migration 0019), wind and water, day/night + weather, world
        clock owned by the server (migration 0020), world hand-back when a server dies.
  - [x] **19B — Five Harbor Town NPCs placed** (`6afcf54`): Blacksmith, Harbor Guide, Job
        Guide, Storage Keeper, Shopkeeper — presentation and placement.
  - [x] **Training Slime production gameplay** (`4780529`): spawns into a camp, wanders,
        chases, attacks with server-decided damage, dies, drops, pays EXP, respawns
        (spawn radius honoured); rebuilt slime model. Riding along in the same commit:
        NPC dialogue, the quest chain and its persistence (migrations 0021–0022), Thai/English
        localisation, revive.
  - [x] **19E — Basic Punch / ASPD / avatar posture** (`b608938`): Mixamo Cross Punch
        retargeted per rig in Blender (26 frames, contact 21/30), guard loop, replicated
        attack-speed stat pacing requests and scaling only the attack state (≤ 1.75×),
        one accepted swing = one drawn cycle, hits held to the contact frame, stand-off
        measured from the fist's reach, character faces its target, Fist shape key.
        Root cause of the male/female back-arch fixed at the avatar layer (T-pose was the
        bind pose; corrected in the model and every clip importer). Training slime `atk`
        set to 0 as a testing aid (the server's damage floor of 1 still applies).
  - [ ] **19F — First Playable Loop Integration / Closure.** Next. Not started.

### Not done / known gaps (as of 2026-09-13)

- Phase 19 is not closed: the first playable loop (login → town → talk → quest → hunt →
  turn in) has not been verified end to end as one gate.
- Services for economy, trade, player shop and guild exist as schema/runtime pieces from
  Phases 13/15 but are not wired into the live world.
- Hosting for the PHP/MySQL backend is undecided; the dev stack is local only.
- Player movement, camera, locomotion clips and the player rig are **locked** — changes
  need an explicit gate.
