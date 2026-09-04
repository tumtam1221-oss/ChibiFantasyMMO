# ChibiFantasyMMO Project Status

> Living status document. Update it at the end of every phase.
> Last updated: 2026-09-02 — Phase 01 (Foundation), version-control baseline.

---

## 1. Status Summary

| Area | State |
|---|---|
| **Current Phase** | Phase 01 — Foundation |
| **Unity** | 2023.2.3f1 |
| **Render Pipeline** | URP 16.0.5 |
| **Blender** | 5.1.2 |
| **MCP — Unity** | Connected |
| **MCP — Blender** | Connected |
| **Gameplay** | Not implemented |
| **Networking** | Not selected |
| **Backend** | Not implemented |
| **Database** | Not implemented |
| **Character** | Not implemented |
| **World** | Not implemented |
| **Version Control** | Git (git version 2.50.1.windows.1) |
| **Git LFS** | Installed and enabled — git-lfs/3.7.0, initialized repo-local via `git lfs install --local`. Tracks 13 binary formats (see §2). |

### Environment detail

- **Unity project root:** `E:\GameDev\ChibiFantasyMMO`
- **Color space:** Linear
- **Active input handler:** Legacy Input Manager (Input System package not installed)
- **Asset serialization:** Force Text (`m_SerializationMode: 2`)
- **Scenes in build settings:** `Assets/Scenes/SampleScene.unity` (URP template scene) only
- **Console:** 0 errors, 2 warnings + 1 assert (engine-internal TLS/stack allocator noise only)
- **Blender file:** unsaved default startup scene (Cube / Light / Camera), no `.blend` on disk

---

## 2. Current Project Structure

```
E:\GameDev\ChibiFantasyMMO\
├── .git/                             # version control (Phase 01)
├── .gitignore                        # Unity ignore rules
├── .gitattributes                    # Git LFS binary tracking
├── PROJECT_STATUS.md                 # this document
├── Assets/
│   ├── Readme.asset                  # Unity URP template (kept)
│   ├── Scenes/SampleScene.unity      # Unity URP template (kept, untouched)
│   ├── Settings/                     # URP pipeline assets (8 files)
│   │   ├── URP-Performant / Balanced / HighFidelity (+ Renderers)
│   │   ├── DefaultVolumeProfile.asset
│   │   ├── SampleSceneProfile.asset
│   │   └── UniversalRenderPipelineGlobalSettings.asset
│   ├── TutorialInfo/                 # Unity URP template (kept)
│   ├── ThirdParty/                   # 3rd-party imports, isolated  [empty]
│   └── _Game/                        # ALL first-party content
│       ├── README.md                 # Phase-01 conventions (source of truth)
│       ├── Art/
│       │   ├── Characters/  Monsters/  NPC/  Environment/       [empty]
│       │   └── Items/  Weapons/  VFX/  UI/                      [empty]
│       ├── Audio/  BGM/  SFX/  Voice/                           [empty]
│       ├── Prefabs/                                             [empty]
│       ├── Scenes/
│       │   └── Login/ ServerSelect/ ChannelSelect/
│       │       CharacterSelect/ GameWorld/                      [empty]
│       ├── Scripts/
│       │   └── Core/ UI/ Network/ Character/ Gameplay/ Data/    [empty]
│       ├── Materials/  Textures/  Animations/                   [empty]
│       └── Resources/  Settings/                                [empty]
├── Packages/                         # manifest.json + packages-lock.json
├── ProjectSettings/                  # 27 configuration assets
│
└── (ignored, not in version control)
    Library/  Temp/  Logs/  UserSettings/  obj/  .vscode/  *.csproj  *.sln
```

All 29 empty scaffold folders carry a `.gitkeep` file so the architecture
survives a fresh clone. `.gitkeep` is a dot-file, so Unity's asset pipeline
ignores it and generates no `.meta` for it.

### Installed Unity packages

`com.coplaydev.unity-mcp` (git `#main`) · `com.unity.render-pipelines.universal` 16.0.5 ·
`com.unity.ai.navigation` 2.0.0 · `com.unity.timeline` 1.8.6 · `com.unity.ugui` 2.0.0 ·
`com.unity.visualscripting` 1.8.0 · `com.unity.test-framework` 1.3.9 ·
`com.unity.collab-proxy` 2.12.4 · `com.unity.ide.rider` 3.0.27 ·
`com.unity.ide.visualstudio` 2.0.22 · standard built-in modules

**Not installed:** Input System · Addressables · Cinemachine · any networking
transport (Netcode / Mirror / Fish-Net) · Localization · Burst / Collections

---

## 3. Current Known Risks

| # | Risk | Impact | Status |
|---|---|---|---|
| 1 | ~~`*.asset`, `*.unity`, `*.prefab`, `*.mat`, `*.anim` tracked by Git LFS despite Force Text serialization~~ — would have turned all 26 `ProjectSettings/*.asset` files, the 8 URP settings assets and `SampleScene.unity` into opaque LFS pointers (no readable diffs, no three-way merge, broken clone without LFS). | High | **RESOLVED 2026-09-02** — the 5 YAML patterns were untracked before the initial commit. LFS now covers only the 13 genuinely binary formats. |
| 2 | **No networking stack selected.** Constrains character controller, state ownership, tick model and scene flow. | High — architectural | Open |
| 3 | **Legacy Input Manager is active.** No rebindable keys, weak gamepad/multi-device support. Switching later means rewriting every input call site and requires an editor restart. | High | Open |
| 4 | **Zero custom tags and layers.** Layer collision matrix is fully open (everything collides with everything). Targeting, ground checks and camera culling all depend on these. | Medium–High | Open |
| 5 | **Blender work is unsaved and has no home.** No `.blend` exists on disk; the asset pipeline in `_Game/README.md` never states where `.blend` source files live. | Medium | Open |
| 6 | **Unity MCP package tracks a git `#main` branch with no version pin.** An upstream push can change or break the bridge on any package resolve. | Medium | Open |
| 7 | **Unity 2023.2 / URP 16 is a Tech Stream release, not LTS.** No long-term patch support for a multi-year project. | Medium | Open |
| 8 | **No Addressables.** With `Resources/` present and Addressables absent, the path of least resistance does not scale to a 5-scene MMO client. | Medium | Open |
| 9 | **Blender scene is 24 fps**, mismatched with typical Unity animation expectations (30/60). Needs explicit handling on FBX export. | Low–Medium | Open |
| 10 | **`companyName` is still `DefaultCompany`.** Affects persistent data path, PlayerPrefs registry key and bundle identifier. Cheap now, painful to migrate later. | Low | Open |
| 11 | **`Assets/_Game/Settings` vs `Assets/Settings` name collision.** Easy for humans and agents to write URP assets into the wrong folder. | Low | Documented in `_Game/README.md` |
| 12 | **No Git remote configured.** The baseline exists only on this machine; a disk failure still loses everything. | High | Open |

### Git LFS scope (as committed)

**Tracked by LFS — 13 binary formats:**

```
*.fbx  *.blend  *.glb  *.gltf          # 3D models
*.png  *.jpg  *.jpeg  *.tga  *.psd     # textures / source art
*.wav  *.mp3  *.ogg  *.mp4             # audio / video
```

**Deliberately NOT in LFS:** `*.asset`, `*.unity`, `*.prefab`, `*.mat`,
`*.anim`. The project uses Force Text serialization, so these are YAML and
stay plain text — readable diffs, three-way merge, and `UnityYAMLMerge`
conflict resolution all keep working, and the repo still clones into a
working Unity project on a machine without LFS.

If scene/prefab serialization is ever switched to Force Binary, revisit this
decision and add those patterns back at that time.

---

## 4. Architectural Decisions Still Pending

| # | Decision | Why it blocks work | Should be decided before |
|---|---|---|---|
| 1 | ~~Networking model~~ | — | **DECIDED, Phase 16.** Server-authoritative, FishNet 4.7.2 (`com.firstgeargames.fishnet`, pinned tag). The client is authoritative for nothing: identity comes from the account API, placement from authored spawn definitions. Proven by a real loopback connection in `FishNetWorldEntryTests`. See `docs/NETWORKING.md`. |
| 2 | **Input System vs. legacy Input Manager.** | Every input call site depends on it; migration is a rewrite. | Any character controller or UI input |
| 3 | ~~Backend & database~~ | — | **DECIDED, Phases 15–16.** PHP 8.2+ API over MySQL 8.0+, own auth (`password_hash`, `random_bytes` session tokens). Twelve endpoints; the full login → server → channel → character → world flow resolves against the database. Verified end to end over real HTTP in `LiveBackendIntegrationTests`. Hosting is still undecided. |
| 4 | **Asset loading strategy** — Addressables vs. `Resources/` vs. direct references. | Affects folder layout, build size and patching. | First real art/prefab import |
| 5 | **Tag & layer taxonomy** plus the layer collision matrix. | Physics, targeting and culling depend on it. | First character or world collision work |
| 6 | **Blender → Unity art pipeline** — `.blend` storage location, FBX vs. glTF, scale/axis/fps convention, rig standard (Humanoid vs. Generic). | Changing the rig standard later means re-rigging every character. | First character mesh |
| 7 | ~~Git LFS scope for Unity YAML assets~~ | — | **DECIDED 2026-09-02: binary formats only.** See §3. |
| 8 | **Git remote / backup host**, and whether the host offers enough LFS quota for a project of this size. | Baseline is currently single-machine. | Any further content work |
| 9 | ~~Unity version policy~~ | — | **SETTLED IN PRACTICE: Unity 6000.3.23f1** (`ProjectSettings/ProjectVersion.txt`), URP 17.3.0. Whether to move to an LTS release remains open, but the project has been on 6000.3 since Phase 02 and all content was authored against it. |
| 10 | **UI toolkit choice** — uGUI vs. UI Toolkit for the MMO HUD and menus. | uGUI is installed; UI Toolkit would change all UI authoring. | First UI screen |

---

## 5. Phase Log

Each phase is one or more commits; the commit messages carry the reasoning and the
failures found along the way.

- [x] **Phase 00 — Environment audit.** Unity MCP PASS, Blender MCP PASS, 0 console
      errors, folder scaffold verified.
- [x] **Phase 01 — Foundation / version control.** Git, Unity `.gitignore`, Git LFS
      for binary assets, baseline commit.
- [x] **Phases 02–06 — Characters and content foundation.** Male and female production
      characters (Humanoid, 21 bones, 4-bone influence limit, FBX round-trip
      validated), definitions, registries, progression, skills.
- [x] **Phase 07 — Character controller and combat.** Third-person controller, combat
      foundation, skill integration, production combat runtime, presentation.
- [x] **Phase 08 — Inventory, equipment, storage.** Runtime, UI, interaction.
- [x] **Phase 09 — Equipment enhancement.** Rarity, status stones, enchanting, fusion.
- [x] **Phase 10 — Monsters, drops, loot, quests.**
- [x] **Phase 11 — Maps, cities, NPCs, portals, travel.**
- [x] **Phase 12 — Devil Fruit, cards, pets.**
- [x] **Phase 13 — Party, guild, trade, player shop, economy.**
- [x] **Phase 14 — Login, session, server/channel/character select.** Transport-neutral
      `IAccountApi`; ends deliberately at `WorldEntryState.Authorised`.
- [x] **Phase 15 — PHP API + MySQL backend.** 10 migrations, 36 tables, 9 endpoints.
      Schema exists for economy, trade, shop, party and guild; the services for those
      do not — see the Phase 15 report.
- [x] **Phase 16 — Real networking.** Production `UnityWebRequest` transport, live PHP
      integration, session release, FishNet 4.7.2 world entry, authoritative character
      spawn from authored definitions. `docs/NETWORKING.md`.
- [ ] **Phase 17+ — Not authorized.** Nothing is replicated yet: no movement, combat,
      inventory, trade or party crosses the wire.
