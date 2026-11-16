# Mod pack — architecture & future plan

This document is for adding more mods later. It explains how the pack is put
together, where the game keeps the things you'll want to change, and which mods
are worth doing next.

---

## 1. How the pack works

```
TrenchWar/
  TrenchWar.exe
  TrenchWar_Data/
  doorstop_config.ini      <- Unity Doorstop: loads BepInEx before the game
  winhttp.dll
  .doorstop_version
  BepInEx/
    core/                  <- BepInEx 6 (Bleeding Edge, Unity.Mono, win-x64) + Harmony
    plugins/
      MortarRangeMod.dll   <- mod 1
      TrenchWarSandbox.dll <- mod 2
      PerformanceMod.dll   <- mod 3
      (drop future mods here)
    config/                <- per-mod .cfg files (created on first run)
    LogOutput.log
  mods/                    <- source code for the mods (not used by the game)
```

Each mod is a small C# class that derives from `BepInEx.Unity.Mono.BaseUnityPlugin`,
is tagged with `[BepInPlugin(guid, name, version)]`, and uses **Harmony** to patch
the game's methods at runtime. The game's original files are never modified.

**Why this design:** the game is Unity 6 with the **Mono** scripting backend (not
IL2CPP), so its code decompiles cleanly and Harmony can patch it reliably. All
game code lives in one assembly, `TrenchWar_Data/Managed/Assembly-CSharp.dll`.

**Adding a mod later:** build a new dll and drop it into `BepInEx/plugins/`.
No need to touch the other mods.

---

## 2. Where the game keeps things (hook points)

| You want to change… | Look here |
| --- | --- |
| Nation stats (HP, cost, speed, accuracy, income, building HP…) | `Nations` static `defs` → `NationDef` (built in `Nations..cctor`) |
| Squad types / what a nation can field | `NationDef.infantry` / `.vehicles` / `.air`, `SquadKind` enum |
| Prices (squads, buildings, air missions) | `Costs` (`.cctor`, `Costs.Squad`, `Costs.Building`, `Costs.Air`) |
| Starting resources / resources per minute | `MatchSettings.StartResources`, `BattleManager.hqIncomeEvery`, `Nations.Def(...).income` |
| Mortar / anti-tank / sniper range | `BattleManager.mortarRange`, `.mortarMinRange`, `.antiTankRange`, `.range` |
| Fatigue / stamina rules | `StaminaSettings` |
| Match setup (map, bots, FFA, night, AI level) | `MatchSettings`, `StartMenu.Single`, `Players.SoloFreeForAll` |
| Battle end / elimination | `BattleManager.EndBattle`, `.PlayerOut`, `.Surrender` |
| Economy (pay/income) | `BattleManager.StockOf`, `.StockOfPlayer`, `Stock` |
| 74 profile challenges | `missions` JSON TextAsset inside `resources.assets` (via `Achievements.Load`) |
| Localisation (EN/DE) | `Loc`, `LocDE`, `FieldManualText` |
| Maps (generated in code) | `BattlefieldHex*`, `BuildHexMaps`, `Battlefield.MapKind` |
| Camera / zoom limits | `RtsCamera` |
| Game speed | `MatchSettings.Speed`, `MatchSettings.PaceFactors` |
| Multiplayer | `Net`, `Relay`, `NetSync`, `Lobby` (avoid — anti-cheat) |

---

## 3. Current mods — what they patch

**MortarRangeMod** (`mods/MortarRangeMod/Plugin.cs`)
- `[HarmonyTranspiler]` on `Soldier.FindMortarTarget` and `Soldier.WantsAdvance`
  → replaces `bm.mortarRange` with a nation-scaled value.
- `[HarmonyPostfix]` on `BattleManager.EngageReach` → scales mortar engagement range.
- Config: `BepInEx/config/xano.trenchwar.mortarrange.cfg`.

**TrenchWarSandbox** (`mods/TrenchWarSandbox/Plugin.cs`)
- `[HarmonyTranspiler]` on `StartMenu.Single` → adds the third `SANDBOX` mode entry.
- `[HarmonyPostfix]` on `BattleManager.StockOf` and `BattleManager.StockOfPlayer` → refills the local player's stock.
- `[HarmonyPrefix]` on `Building.Damage` and `Building.Blow` → prevents the local HQ from being destroyed.
- `[HarmonyPrefix]` on `BattleManager.EndBattle` → never ends the battle.
- `[HarmonyPrefix]` on `BattleManager.PlayerOut` → never eliminates you.
- `[HarmonyPrefix]` on `BattleManager.Surrender` → reroutes to the title screen.
- `[HarmonyPrefix]` on `Achievements.CountsFor` → pauses profile achievement/stat recording during sandbox.

**PerformanceMod** (`mods/PerformanceMod/Plugin.cs`)
- `[HarmonyPostfix]` on `BattleManager.Keys` → adds configurable hotkeys (`F3` / `F9`) for `Perf.Toggle()` and `Perf.ForceLog` CSV capture.
- `[HarmonyTranspiler]` on `VoxelTerrain.LateUpdateInner` → caches all 8 `Action<int>` / `Func<int, bool>` delegates per terrain instance.
- `[HarmonyPrefix]` on `BattleManager.TickBotsInner` → removes per-frame closure + `Action` allocations when ticking bot sides.
- `[HarmonyPrefix]` on `Soldier.RefreshThreats` (offline only) → zero-allocation in-place top-5 enemy threat selection.
- `[HarmonyTranspiler]` (`Priority.Low`) on `Soldier.FindMortarTarget` (offline only) → queries `BattleManager.Near` spatial buckets sized to `range * 1.5f` (with `MortarRangeMod` interop).
- `[HarmonyPrefix]` on `FrontLine.BuildMesh` → skips rebuilding frontline meshes when `line[]` moved less than `FrontLineMoveThreshold`.
- `[HarmonyPrefix]` / `[HarmonyTranspiler]` on `FogOfWar.UpdateInner` & `FogOfWar.Recompute` → configurable interval and dirty-pixel tracking to skip empty `SetPixels32` + `Apply` GPU uploads.
- `[HarmonyPrefix]` / `[HarmonyTranspiler]` on `VoxelTerrain.ApplyColJobs` → caps finished `MeshCollider.sharedMesh` swaps per frame.
- `[HarmonyPrefix]` on `CommandUI.DrawCommand`, `BattleManager.DrawHud`, `FirstTips.Draw`, `Scoreboard.Draw`, and `Perf.Draw` → filters redundant Unity IMGUI event passes (`Layout`, `MouseMove`, etc.).
- `[HarmonyTranspiler]` on `SoldierView.LateUpdateInner` & `[HarmonyPrefix]` on `VoxelDebris.Awake` → configurable health-bar cull distance and debris pool limits.
- Config: `BepInEx/config/xano.trenchwar.performance.cfg`.

---

## 4. How to build a new mod

Needs: .NET SDK, the game's `TrenchWar_Data/Managed/*.dll` as references, and
`BepInEx/core/*.dll`. A ready-made project file is in each `mods/<name>/`
folder — copy one, rename the assembly, and reference the game + BepInEx dlls.

Useful tooling: `ilspycmd` (decompile `Assembly-CSharp.dll` to C#), dnSpy
(GUI, Windows), UnityPy (inspect/replace assets such as `missions.json`).

---

## 5. Future mods — candidate list (priority order)

**Easy / high value**
1. **Full balance mod** — generalize the mortar-range approach to every nation
   stat (`NationDef`) and every price (`Costs`): a single cfg with multipliers.
2. **Unlimited population** — remove the squad/unit cap (hook the squad-count checks).
3. **Starting resources & income sliders** — extend the existing pickers in
   `StartMenu` / `MatchSettings`.
4. **Default map options** — remember or force map size, day/night, AI level.
5. **Custom challenges / profile icons** — edit the `missions.json` TextAsset.

**Medium**
6. **Peaceful / passive bots** — skip AI offensive decisions (hook `AiBrain`).
7. **Camera & UI tweaks** — zoom limits, HUD scale, always-show resources.
8. **All-units-for-every-nation** — widen `NationDef.infantry/vehicles/air`.
9. **Game speed control** — expose `PaceFactors` in a cfg.

**Hard (real projects)**
10. **New units** — needs code + voxel art + `Nations`/AI wiring.
11. **New nations** — same, plus `Nations`, `NationArt`, `NationFlags`.
12. **New maps** — the maps are generated in code; possible but involved.

---

## 6. Maintenance

- The game is in active development and updates itself. After an update, a
  Harmony patch may no longer match (the patch targets are matched by
  method/field signature, so small changes usually survive; a rewritten method
  will silently stop applying).
- If a mod seems dead after an update, check `BepInEx/LogOutput.log` and ask —
  re-pointing a patch is usually a few minutes' work.
- Keep the game's own files untouched so the pack can always be cleanly removed.
