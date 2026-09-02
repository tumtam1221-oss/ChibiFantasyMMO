# ChibiFantasyMMO — Project Guide

> **Phase 01 — Project Structure**
> This document is the single source of truth for how assets, code, and content
> are organized in the ChibiFantasyMMO Unity project. Read it before adding
> anything to the project.

---

## 1. Project Purpose

**ChibiFantasyMMO** is a 3D Cartoon / Chibi Fantasy MMORPG built in Unity.

- **Engine:** Unity `2023.2.3f1`
- **Render Pipeline:** Universal Render Pipeline (URP) — Universal 3D
- **Art Direction:** 3D Cartoon / Chibi Fantasy
- **Genre:** MMORPG (multiplayer online role-playing)

The project is being built in **phases**. Phase 01 establishes the folder
structure and conventions only — no gameplay, UI, networking, or backend is
implemented yet.

---

## 2. Folder Structure

All first-party project content lives under `Assets/_Game/`. The leading
underscore keeps our content pinned to the top of the Unity Project window,
separated from imported/template assets.

```
Assets/
├── _Game/                  # ALL first-party project content lives here
│   ├── Art/                # Source & authored visual art
│   │   ├── Characters/     # Player character art (chibi bodies, heads, hair)
│   │   ├── Monsters/       # Enemy / monster art
│   │   ├── NPC/            # Non-player character art
│   │   ├── Environment/    # Maps, props, terrain, buildings
│   │   ├── Items/          # Item icons & 3D item art
│   │   ├── Weapons/        # Weapon meshes & art
│   │   ├── VFX/            # Visual effects art (particles, trails, shaders src)
│   │   └── UI/             # UI art (sprites, icons, frames, atlases)
│   │
│   ├── Audio/
│   │   ├── BGM/            # Background music
│   │   ├── SFX/            # Sound effects
│   │   └── Voice/          # Voice-over / character voice
│   │
│   ├── Prefabs/            # Reusable prefabs (characters, props, UI widgets)
│   │
│   ├── Scenes/
│   │   ├── Login/          # SCN_Login
│   │   ├── ServerSelect/   # SCN_ServerSelect
│   │   ├── ChannelSelect/  # SCN_ChannelSelect
│   │   ├── CharacterSelect/# SCN_CharacterSelect
│   │   └── GameWorld/      # SCN_GameWorld
│   │
│   ├── Scripts/            # All C# code, separated by responsibility
│   │   ├── Core/           # Bootstrapping, app lifecycle, services, utils
│   │   ├── UI/             # UI/view logic only (no game rules, no network)
│   │   ├── Network/        # Networking / transport / server communication
│   │   ├── Character/      # Character logic (movement, stats, controllers)
│   │   ├── Gameplay/       # Game systems & rules (combat, quests, world)
│   │   └── Data/           # Data definitions (ScriptableObjects, DTOs, models)
│   │
│   ├── Materials/          # Shared materials
│   ├── Textures/           # Shared textures
│   ├── Animations/         # Animation clips, controllers, timelines
│   ├── Resources/          # Runtime-loaded assets (use sparingly)
│   └── Settings/           # Project-specific config assets (game settings)
│
└── ThirdParty/             # Imported 3rd-party packages / assets (isolated)
```

### Untouched template/engine folders (do NOT reorganize)

These already exist from the Unity URP template and are **left in place**:

- `Assets/Scenes/SampleScene.unity` — template scene (kept, not deleted)
- `Assets/Settings/` — URP render pipeline assets (URP-Balanced, etc.)
- `Assets/TutorialInfo/` — Unity template tutorial content
- `Assets/Readme.asset` — Unity template readme

> `Assets/Settings/` (URP pipeline) is **not** the same as
> `Assets/_Game/Settings/` (our game config). Do not merge them.

---

## 3. Naming Conventions

Use **PascalCase** for folders and C# types. Use **prefix tags** for content
assets so they sort and search predictably.

### Prefix table

| Prefix   | Used for            | Example                       |
|----------|---------------------|-------------------------------|
| `SCN_`   | Scenes              | `SCN_Login`, `SCN_GameWorld`  |
| `CHR_`   | Characters          | `CHR_Base_Male`               |
| `MAP_`   | Environment / maps  | `MAP_Login`, `MAP_CharacterSelect` |
| `UI_`    | UI screens/widgets  | `UI_Login`, `UI_ServerSelect` |
| `MON_`   | Monsters            | `MON_Slime` *(future)*        |
| `NPC_`   | NPCs                | `NPC_Merchant` *(future)*     |
| `ITM_`   | Items               | `ITM_HealthPotion` *(future)* |
| `WPN_`   | Weapons             | `WPN_ShortSword` *(future)*   |
| `VFX_`   | Visual effects      | `VFX_Heal` *(future)*         |
| `MAT_`   | Materials           | `MAT_ChibiSkin` *(future)*    |
| `SFX_`   | Sound effects       | `SFX_ButtonClick` *(future)*  |
| `BGM_`   | Background music     | `BGM_LoginTheme` *(future)*   |

### Defined Phase 01 names

**Scenes**
```
SCN_Login
SCN_ServerSelect
SCN_ChannelSelect
SCN_CharacterSelect
SCN_GameWorld
```

**Characters**
```
CHR_Base_Male
CHR_Base_Female
```

**Environment**
```
MAP_Login
MAP_CharacterSelect
```

**UI**
```
UI_Login
UI_ServerSelect
UI_ChannelSelect
UI_CharacterSelect
```

### Code naming

- **Namespaces:** `ChibiFantasyMMO.<Area>` (e.g. `ChibiFantasyMMO.Network`).
- **Classes / methods / properties:** `PascalCase`.
- **Private fields:** `_camelCase`.
- **ScriptableObject data assets:** suffix with `Data`/`Definition`
  (e.g. `ItemDefinition`, `MonsterDefinition`).
- **One public type per file**, file name == type name.

---

## 4. Architecture Rules

The architecture is **data-driven** and **modular**. Four concerns stay
strictly separated: **UI**, **Game Logic**, **Network**, and **Data**.

1. **Data-driven.** Content (classes/jobs, maps, items, monsters, stats) is
   defined in data assets (ScriptableObjects) and/or external config — never
   baked into logic.
2. **Modular.** Each system is self-contained with a clear responsibility and
   communicates through interfaces/events, not direct cross-references.
3. **Separation of layers:**
   - `Scripts/UI` — presentation only. No game rules, no networking.
   - `Scripts/Gameplay` — game rules/systems. No UI, no transport code.
   - `Scripts/Network` — transport & server comms. No UI, no game rules.
   - `Scripts/Data` — definitions & models shared by the layers above.
   - `Scripts/Core` — bootstrap, service locator/DI, shared utilities.
   - `Scripts/Character` — character controllers/state (uses Data, not UI).
4. **No giant manager scripts.** Prefer small, focused services/systems over
   a single `GameManager` that knows everything.
5. **No duplicated systems.** One canonical implementation per concern; reuse
   it, don't copy it.
6. **No hard-coded game IDs.** Reference content by data assets or string/enum
   keys resolved through a registry — never magic numbers scattered in code.
7. **No secrets in Unity.** No database credentials, connection strings, or
   private keys in the Unity project or committed assets.
8. **No fake systems.** Do not stub fake backend or fake multiplayer to
   simulate features; integrate real services when their phase arrives.

---

## 5. Asset Pipeline

1. **Import** raw/3rd-party assets into `Assets/ThirdParty/` only. Keep them
   isolated so upgrades/removal are clean and they never mix with `_Game`.
2. **Author** first-party art/audio into the matching `_Game/Art` or
   `_Game/Audio` subfolder using the correct prefix.
3. **Assemble** reusable objects as **Prefabs** in `_Game/Prefabs/`. Scenes
   reference prefabs; scenes should be thin.
4. **Materials/Textures/Animations** shared across many assets go in the
   shared `_Game/Materials|Textures|Animations` folders; asset-specific ones
   may sit beside their asset under `Art/`.
5. **Data assets** (ScriptableObjects) live under `_Game/Scripts/Data` output
   or a dedicated data folder as the project grows; they define content.
6. **Resources/** is for assets that must be loaded by path at runtime. Use it
   sparingly — prefer direct references or Addressables later.
7. **Scenes** are saved into their named subfolder under `_Game/Scenes/` with
   the `SCN_` prefix.

---

## 6. Future Expansion Rules

The structure is designed so new content is **additive**, never a rewrite.

- **New class/job:** add a new data definition (ScriptableObject) — no code
  changes to core systems. Systems iterate definitions, not hard-coded lists.
- **New map:** add a scene under `_Game/Scenes/` (`SCN_...`) and a map
  definition; register it in data. No branching logic per-map in code.
- **New item:** add an item definition asset (`ITM_...` art + data). Item
  systems read stats/behavior from data.
- **New monster:** add a monster definition asset (`MON_...` art + data).
- **New UI screen:** add `UI_...` prefab + a view script under `Scripts/UI`
  bound to data/services — no game logic inside the view.
- **New 3rd-party package:** import into `ThirdParty/`; only install packages
  that are actually needed (avoid dependency bloat).

**Golden rule:** if adding content requires editing a big `switch`/`if` chain
or a central manager, the design is wrong — move that variability into data.

---

## 7. Phase Status

- [x] **Phase 01 — Project structure & conventions** (this document)
- [ ] Phase 02+ — TBD (login flow, character system, networking, etc.)

Do not implement gameplay, UI, networking, or backend until the corresponding
phase is authorized.
