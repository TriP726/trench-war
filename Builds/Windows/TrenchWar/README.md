# Trench War — Mod Pack

One extract, three mods. Built for the **Trench War** build of 2026-11-14
(Unity 6000.4.9f1, Mono).

Contains:

| Mod | What it does |
| --- | --- |
| **Performance Mod** | Eliminates frame stutters, GC allocations, redundant frontline/fog/UI work, and offline AI O(N^2) scans, plus adds hotkeys for the built-in performance overlay (`F3`) and CSV logger (`F9`). |
| **Mortar Range Per Nation** | Sets the maximum mortar range independently for each nation (game default is 170). |
| **Sandbox Mode** | Adds a `SANDBOX` entry to the singleplayer mode picker: 6-way FFA vs bots, unlimited resources for you, no win/lose, never eliminated. |

Gameplay mods (**Mortar Range**, **Sandbox**, and the AI targeting optimizations inside **Performance Mod**) are gated singleplayer/offline only — the game has an anti-cheat and a ranked online mode. Client-side GC/rendering/UI/terrain smoothing in **Performance Mod** does not alter simulation state.

---

## Install

1. Unzip this folder so its contents sit **next to `TrenchWar.exe`**
   (the same folder as `TrenchWar.exe` and `TrenchWar_Data/`).
2. Start the game. BepInEx and the mods load automatically.

That's it. No other files are touched.

## Using the mods

**Performance Mod** — works out of the box with zero configuration, and exposes
all toggles/budgets after the first launch in:

```
BepInEx/config/xano.trenchwar.performance.cfg
```

- Press **`F3`** in battle to toggle the built-in `"PERFORMANCE"` timing/GC overlay.
- Press **`F9`** in battle to start/stop writing `PerfLogs/perf_*.csv` frame telemetry.
- Key settings in `xano.trenchwar.performance.cfg`:
  - **2. Memory and GC**: `CacheTerrainDelegates` (removes 6 per-frame delegate allocations), `ZeroAllocBotTick`, `IncrementalGcTimeSliceMs` (when supported by Unity player build).
  - **3. AI (Offline Only)**: `ZeroAllocRefreshThreats` (zero-allocation top-5 threat scan), `SpatialMortarTargeting` (`BattleManager.Near` spatial bucket lookup, respects `MortarRangeMod`).
  - **4. Rendering and UI**: `SkipStationaryFrontLineMesh`, `SkipUnchangedFogUploads`, `FogUpdateInterval` (`0.35s`), `MaxColliderSwapsPerFrame` (`1` swap/frame to prevent crater stutters), `FilterRedundantOnGuiEvents`.
  - **5. Visual Budgets**: `HealthBarMaxDistance` (`90` vs `120`), `DebrisMaxPieces` (`5000` vs `10000`), `DebrisMaxResting` (`1500` vs `3000`), `DebrisDepositChance` (`0.5` vs `0.75`), `VSyncCount`, `TargetFrameRate`.

**Mortar range** — after the first launch, edit:

```
BepInEx/config/xano.trenchwar.mortarrange.cfg
```

```
[Mortar range]
Germany 1940 = 1.5
Great Britain 1940 = 1
USA 1965 = 1
North Vietnam 1965 = 1
Soviet Union 1941 = 1
Japan 1942 = 1
```

`1.5` = 255 range instead of 170.

**Sandbox** — Startmenu → **SINGLEPLAYER** → pick **SANDBOX** → choose nation /
map / bots / AI level → **START BATTLE**. Leave again with Esc → **SURRENDER**
(returns to the title screen).

## Uninstall / disable

Delete `BepInEx/` (and `doorstop_config.ini`, `winhttp.dll`, `.doorstop_version`)
to remove everything, or just delete / rename a single dll in `BepInEx/plugins/`
to disable one mod.

## Logs

`BepInEx/LogOutput.log` — look for `Trench War Performance Mod loaded`, `Mortar Range Per Nation loaded`, and `Sandbox mod loaded.`

## Adding more mods later

Drop any new `*.dll` into `BepInEx/plugins/`. See `PLAN.md` for the mod
architecture, the source of both mods, and the planned future mods.
