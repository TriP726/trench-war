using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Scripting;

[assembly: AssemblyVersion("1.0.0.0")]

// Trench War — Performance Mod
// Client-side rendering/GC/terrain smoothing and offline-gated AI/targeting optimizations
// without modifying Assembly-CSharp.dll.

[BepInPlugin("xano.trenchwar.performance", "Trench War Performance Mod", "1.0.0")]
[HarmonyPatch]
public class PerformancePlugin : BaseUnityPlugin
{
    private static ManualLogSource log;

    // --- Profiler hotkeys ---
    private static ConfigEntry<Key> profilerKey;
    private static ConfigEntry<Key> profilerLogKey;

    // --- Memory & GC ---
    private static ConfigEntry<float> incrementalGcSliceMs;
    private static ConfigEntry<bool> cacheTerrainDelegates;
    private static ConfigEntry<bool> zeroAllocBotTick;

    // --- Offline-only AI / targeting ---
    private static ConfigEntry<bool> optimizeRefreshThreats;
    private static ConfigEntry<bool> spatialMortarTargeting;

    // --- Rendering / UI / Terrain smoothing ---
    private static ConfigEntry<bool> frontLineDirtySkip;
    private static ConfigEntry<float> frontLineMoveEpsilon;
    private static ConfigEntry<bool> skipEmptyFogUploads;
    private static ConfigEntry<float> fogUpdateInterval;
    private static ConfigEntry<int> maxColliderSwapsPerFrame;
    private static ConfigEntry<bool> filterOnGuiEvents;

    // --- Visual & frame budgets ---
    private static ConfigEntry<float> healthBarMaxDistance;
    private static ConfigEntry<int> debrisMaxPieces;
    private static ConfigEntry<int> debrisMaxResting;
    private static ConfigEntry<float> debrisDepositChance;
    private static ConfigEntry<int> vsyncCount;
    private static ConfigEntry<int> targetFrameRate;

    // --- Fast field/method accessors ---
    private static AccessTools.FieldRef<Soldier, BattleManager> soldierBmRef;
    private static AccessTools.FieldRef<Soldier, List<Soldier>> soldierThreatsRef;
    private static Func<Soldier, Vector3, bool> callSoldierSpots;
    private static FieldInfo soldierNearbyField;
    private static readonly float[] topThreatDist2 = new float[5];

    private static Func<BattleManager, Soldier, float> externalMortarRangeForSoldier;
    private static readonly List<Soldier> mortarCandidatesBuf = new List<Soldier>(128);

    private static Action<BattleManager, int, Action> callAsBot;
    private static Action<BattleManager, float> callTickEnemy;
    private static BattleManager activeBotBm;
    private static float activeBotDt;
    private static readonly Action cachedBotAction = InvokeCachedTickEnemy;

    private static AccessTools.FieldRef<FrontLine, float[]> frontLineArrayRef;
    private static readonly ConditionalWeakTable<FrontLine, float[]> frontLineSnapshots = new ConditionalWeakTable<FrontLine, float[]>();
    private static FrontLine lastFrontLine;
    private static float[] lastFrontLineSnap;

    private static AccessTools.FieldRef<FogOfWar, Texture2D> fogTexRef;
    private static AccessTools.FieldRef<FogOfWar, Color32[]> fogPxRef;
    private static bool fogDirty;

    private static int colSwapsThisFrame;

    private static readonly MethodInfo[] terrainDelegateMethods = new MethodInfo[8];
    private static readonly ConditionalWeakTable<VoxelTerrain, TerrainDelegateCache> terrainCaches = new ConditionalWeakTable<VoxelTerrain, TerrainDelegateCache>();
    private static VoxelTerrain lastTerrain;
    private static TerrainDelegateCache lastTerrainCache;

    private sealed class TerrainDelegateCache
    {
        public readonly Action<int>[] Actions = new Action<int>[8];
        public readonly Func<int, bool>[] Preds = new Func<int, bool>[8];
    }

    private void Awake()
    {
        log = Logger;

        profilerKey = Config.Bind("1. Profiler", "ToggleOverlayKey", Key.F3,
            "Key to toggle the built-in performance overlay during battle (None to disable).");
        profilerLogKey = Config.Bind("1. Profiler", "ToggleCsvLogKey", Key.F9,
            "Key to start/stop writing PerfLogs/perf_*.csv during battle (None to disable).");

        incrementalGcSliceMs = Config.Bind("2. Memory and GC", "IncrementalGcTimeSliceMs", 2.0f,
            new ConfigDescription("Incremental GC frame slice in milliseconds when supported by the Unity player build (0 = keep Unity default).", new AcceptableValueRange<float>(0f, 10f)));
        cacheTerrainDelegates = Config.Bind("2. Memory and GC", "CacheTerrainDelegates", true,
            "Cache delegates in VoxelTerrain.LateUpdateInner to eliminate 6 delegate allocations per frame.");
        zeroAllocBotTick = Config.Bind("2. Memory and GC", "ZeroAllocBotTick", true,
            "Eliminate closure/delegate allocations in BattleManager.TickBotsInner every frame.");

        optimizeRefreshThreats = Config.Bind("3. AI (Offline Only)", "ZeroAllocRefreshThreats", true,
            "Replace Soldier.RefreshThreats sort closure with zero-allocation top-5 selection (offline/singleplayer only).");
        spatialMortarTargeting = Config.Bind("3. AI (Offline Only)", "SpatialMortarTargeting", true,
            "Query BattleManager.Near spatial buckets in Soldier.FindMortarTarget instead of scanning all living soldiers (offline/singleplayer only).");

        frontLineDirtySkip = Config.Bind("4. Rendering and UI", "SkipStationaryFrontLineMesh", true,
            "Skip rebuilding FrontLine meshes every frame when the frontline hasn't moved beyond MoveThreshold.");
        frontLineMoveEpsilon = Config.Bind("4. Rendering and UI", "FrontLineMoveThreshold", 0.02f,
            new ConfigDescription("Minimum frontline movement (world units) before rebuilding FrontLine meshes.", new AcceptableValueRange<float>(0f, 1f)));
        skipEmptyFogUploads = Config.Bind("4. Rendering and UI", "SkipUnchangedFogUploads", true,
            "Skip uploading the FogOfWar texture to the GPU when zero fog pixels changed.");
        fogUpdateInterval = Config.Bind("4. Rendering and UI", "FogUpdateInterval", 0.35f,
            new ConfigDescription("Minimum seconds between FogOfWar recomputes (0 = game default 0.2s/0.3s).", new AcceptableValueRange<float>(0f, 2f)));
        maxColliderSwapsPerFrame = Config.Bind("4. Rendering and UI", "MaxColliderSwapsPerFrame", 1,
            new ConfigDescription("Maximum finished terrain collider mesh swaps applied per frame (0 = uncapped).", new AcceptableValueRange<int>(0, 16)));
        filterOnGuiEvents = Config.Bind("4. Rendering and UI", "FilterRedundantOnGuiEvents", true,
            "Skip CommandUI/HUD/Scoreboard/Perf OnGUI work on Layout, MouseMove, MouseDrag, ScrollWheel, and Key events where unused.");

        healthBarMaxDistance = Config.Bind("5. Visual Budgets", "HealthBarMaxDistance", 90f,
            new ConfigDescription("Maximum camera distance to draw soldier health bars (game default = 120, 0 = always hidden).", new AcceptableValueRange<float>(0f, 400f)));
        debrisMaxPieces = Config.Bind("5. Visual Budgets", "DebrisMaxPieces", 5000,
            new ConfigDescription("Maximum active voxel debris pieces (game default = 10000, -1 = keep default).", new AcceptableValueRange<int>(-1, 20000)));
        debrisMaxResting = Config.Bind("5. Visual Budgets", "DebrisMaxResting", 1500,
            new ConfigDescription("Maximum resting voxel debris pieces (game default = 3000, -1 = keep default).", new AcceptableValueRange<int>(-1, 10000)));
        debrisDepositChance = Config.Bind("5. Visual Budgets", "DebrisDepositChance", 0.5f,
            new ConfigDescription("Chance resting debris turns into terrain voxels (game default = 0.75, -1 = keep default).", new AcceptableValueRange<float>(-1f, 1f)));
        vsyncCount = Config.Bind("5. Visual Budgets", "VSyncCount", -1,
            new ConfigDescription("Override QualitySettings.vSyncCount (-1 = keep game setting, 0 = off, 1 = every vblank).", new AcceptableValueRange<int>(-1, 4)));
        targetFrameRate = Config.Bind("5. Visual Budgets", "TargetFrameRate", -1,
            new ConfigDescription("Override Application.targetFrameRate (-1 = keep game setting, 0 = uncapped, e.g. 60/120/144).", new AcceptableValueRange<int>(-1, 500)));

        ApplyStartupSettings();
        InitReflectionAccessors();

        var harmony = new Harmony("xano.trenchwar.performance");
        try
        {
            harmony.PatchAll(typeof(PerformancePlugin));

            int patched = 0;
            foreach (var m in harmony.GetPatchedMethods())
            {
                patched++;
                log.LogInfo("Patched: " + m.DeclaringType?.FullName + "." + m.Name);
            }

            if (patched == 0)
            {
                log.LogError("Performance Mod loaded, but 0 methods were patched!");
            }
            else
            {
                log.LogInfo("Trench War Performance Mod loaded (v1.0.0, " + patched + " methods patched).");
            }
        }
        catch (Exception ex)
        {
            log.LogError("Performance Mod failed to patch: " + ex);
        }
    }

    private static void ApplyStartupSettings()
    {
        try
        {
            if (GarbageCollector.isIncremental && incrementalGcSliceMs.Value > 0f)
            {
                ulong ns = (ulong)(incrementalGcSliceMs.Value * 1000000f);
                GarbageCollector.incrementalTimeSliceNanoseconds = ns;
                log?.LogInfo("Incremental GC time slice set to " + incrementalGcSliceMs.Value + " ms (" + ns + " ns).");
            }
            else
            {
                log?.LogInfo("GC status: mode=" + GarbageCollector.GCMode + ", isIncremental=" + GarbageCollector.isIncremental);
            }
        }
        catch (Exception ex)
        {
            log?.LogWarning("Could not inspect/configure GC settings: " + ex.Message);
        }

        try
        {
            if (vsyncCount.Value >= 0)
            {
                QualitySettings.vSyncCount = vsyncCount.Value;
                log?.LogInfo("VSyncCount override applied: " + vsyncCount.Value);
            }
            if (targetFrameRate.Value >= 0)
            {
                Application.targetFrameRate = targetFrameRate.Value;
                log?.LogInfo("TargetFrameRate override applied: " + targetFrameRate.Value);
            }
        }
        catch (Exception ex)
        {
            log?.LogWarning("Could not apply frame pacing settings: " + ex.Message);
        }
    }

    private static void InitReflectionAccessors()
    {
        soldierBmRef = AccessTools.FieldRefAccess<Soldier, BattleManager>("bm");
        soldierThreatsRef = AccessTools.FieldRefAccess<Soldier, List<Soldier>>("threats");
        soldierNearbyField = AccessTools.Field(typeof(Soldier), "nearby");
        var spotsMi = AccessTools.Method(typeof(Soldier), "Spots", new[] { typeof(Vector3) });
        if (spotsMi != null)
        {
            callSoldierSpots = (Func<Soldier, Vector3, bool>)Delegate.CreateDelegate(typeof(Func<Soldier, Vector3, bool>), spotsMi);
        }

        var mortarModType = AccessTools.TypeByName("MortarRangePlugin");
        if (mortarModType != null)
        {
            var rangeMi = AccessTools.Method(mortarModType, "RangeForSoldier", new[] { typeof(BattleManager), typeof(Soldier) });
            if (rangeMi != null)
            {
                externalMortarRangeForSoldier = (Func<BattleManager, Soldier, float>)Delegate.CreateDelegate(typeof(Func<BattleManager, Soldier, float>), rangeMi);
                log?.LogInfo("Detected MortarRangeMod — spatial mortar queries will respect per-nation mortar range.");
            }
        }

        var asBotMi = AccessTools.Method(typeof(BattleManager), "AsBot", new[] { typeof(int), typeof(Action) });
        if (asBotMi != null)
        {
            callAsBot = (Action<BattleManager, int, Action>)Delegate.CreateDelegate(typeof(Action<BattleManager, int, Action>), asBotMi);
        }
        var tickEnemyMi = AccessTools.Method(typeof(BattleManager), "TickEnemy", new[] { typeof(float) });
        if (tickEnemyMi != null)
        {
            callTickEnemy = (Action<BattleManager, float>)Delegate.CreateDelegate(typeof(Action<BattleManager, float>), tickEnemyMi);
        }

        frontLineArrayRef = AccessTools.FieldRefAccess<FrontLine, float[]>("line");
        fogTexRef = AccessTools.FieldRefAccess<FogOfWar, Texture2D>("tex");
        fogPxRef = AccessTools.FieldRefAccess<FogOfWar, Color32[]>("px");

        string[] vtNames =
        {
            "BuildChunkAsync",
            "ChunkBusy",
            "BuildChunk",
            "BuildVoxels",
            "BuildVoxelsAsync",
            "VoxPieceWaits",
            "BuildCollider",
            "ColBusy",
        };
        for (int i = 0; i < vtNames.Length; i++)
        {
            terrainDelegateMethods[i] = AccessTools.Method(typeof(VoxelTerrain), vtNames[i], new[] { typeof(int) });
            if (terrainDelegateMethods[i] == null)
            {
                log?.LogWarning("Missing VoxelTerrain method: " + vtNames[i]);
            }
        }
    }

    private static bool OfflineOnly()
    {
        return !Net.Active && !Net.ViaRelay && !Lobby.InMatch;
    }

    // -----------------------------------------------------------------------
    // 1. Built-in Perf Overlay & CSV Logger Hotkeys (F3 / F9)
    // -----------------------------------------------------------------------

    [HarmonyPatch(typeof(BattleManager), "Keys")]
    [HarmonyPostfix]
    private static void HandlePerfHotkeys()
    {
        var kb = Keyboard.current;
        if (kb == null || Chat.BlocksKeys)
        {
            return;
        }

        if (profilerKey != null && profilerKey.Value != Key.None && kb[profilerKey.Value]?.wasPressedThisFrame == true)
        {
            Perf.Toggle();
            log?.LogInfo("Perf overlay " + (Perf.Shown ? "ON" : "off"));
        }

        if (profilerLogKey != null && profilerLogKey.Value != Key.None && kb[profilerLogKey.Value]?.wasPressedThisFrame == true)
        {
            bool next = !Perf.ForceLog;
            Perf.ForceLog = next;
            if (next)
            {
                Perf.BeginLog();
                log?.LogInfo("Perf CSV logging started: " + Perf.LogPath);
            }
            else
            {
                Perf.EndLog();
                log?.LogInfo("Perf CSV logging stopped.");
            }
        }
    }

    // -----------------------------------------------------------------------
    // 2. Zero-Allocation VoxelTerrain.LateUpdateInner Delegates
    // -----------------------------------------------------------------------

    private static TerrainDelegateCache GetTerrainCache(VoxelTerrain vt)
    {
        if (ReferenceEquals(vt, lastTerrain) && lastTerrainCache != null)
        {
            return lastTerrainCache;
        }
        if (!terrainCaches.TryGetValue(vt, out var cache))
        {
            cache = new TerrainDelegateCache();
            terrainCaches.Add(vt, cache);
        }
        lastTerrain = vt;
        lastTerrainCache = cache;
        return cache;
    }

    public static Action<int> GetTerrainAction(VoxelTerrain vt, int slot)
    {
        var cache = GetTerrainCache(vt);
        var act = cache.Actions[slot];
        if (act == null)
        {
            act = (Action<int>)Delegate.CreateDelegate(typeof(Action<int>), vt, terrainDelegateMethods[slot]);
            cache.Actions[slot] = act;
        }
        return act;
    }

    public static Func<int, bool> GetTerrainPred(VoxelTerrain vt, int slot)
    {
        var cache = GetTerrainCache(vt);
        var pred = cache.Preds[slot];
        if (pred == null)
        {
            pred = (Func<int, bool>)Delegate.CreateDelegate(typeof(Func<int, bool>), vt, terrainDelegateMethods[slot]);
            cache.Preds[slot] = pred;
        }
        return pred;
    }

    private static int TerrainMethodSlot(MethodInfo mi)
    {
        for (int i = 0; i < terrainDelegateMethods.Length; i++)
        {
            if (terrainDelegateMethods[i] == mi)
            {
                return i;
            }
        }
        return -1;
    }

    [HarmonyPatch(typeof(VoxelTerrain), "LateUpdateInner")]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> PatchTerrainLateUpdate(IEnumerable<CodeInstruction> instructions)
    {
        var list = new List<CodeInstruction>(instructions);
        if (cacheTerrainDelegates != null && !cacheTerrainDelegates.Value)
        {
            return list;
        }

        var getAction = AccessTools.Method(typeof(PerformancePlugin), nameof(GetTerrainAction));
        var getPred = AccessTools.Method(typeof(PerformancePlugin), nameof(GetTerrainPred));
        int hits = 0;

        for (int i = 1; i + 1 < list.Count; i++)
        {
            if (list[i - 1].opcode == OpCodes.Ldarg_0
                && list[i].opcode == OpCodes.Ldftn
                && list[i].operand is MethodInfo mi
                && list[i + 1].opcode == OpCodes.Newobj
                && list[i + 1].operand is ConstructorInfo ci)
            {
                int slot = TerrainMethodSlot(mi);
                if (slot < 0)
                {
                    continue;
                }
                MethodInfo helper = null;
                if (ci.DeclaringType == typeof(Action<int>))
                {
                    helper = getAction;
                }
                else if (ci.DeclaringType == typeof(Func<int, bool>))
                {
                    helper = getPred;
                }
                if (helper == null)
                {
                    continue;
                }

                list[i].opcode = OpCodes.Ldc_I4;
                list[i].operand = slot;
                list[i + 1].opcode = OpCodes.Call;
                list[i + 1].operand = helper;
                hits++;
                i += 1;
            }
        }

        log?.LogInfo($"PatchTerrainLateUpdate: cached={hits} delegates");
        if (hits == 0)
        {
            log?.LogWarning("PatchTerrainLateUpdate found 0 delegate allocations.");
        }
        return list;
    }

    // -----------------------------------------------------------------------
    // 3. Zero-Allocation BattleManager.TickBotsInner
    // -----------------------------------------------------------------------

    private static void InvokeCachedTickEnemy()
    {
        callTickEnemy(activeBotBm, activeBotDt);
    }

    [HarmonyPatch(typeof(BattleManager), "TickBotsInner")]
    [HarmonyPrefix]
    private static bool PatchTickBotsInner(BattleManager __instance, float dt)
    {
        if ((zeroAllocBotTick != null && !zeroAllocBotTick.Value)
            || callAsBot == null || callTickEnemy == null || __instance == null)
        {
            return true;
        }

        if (Players.FreeForAll && Players.SidesLeft() < 2)
        {
            return false;
        }

        var prevBm = activeBotBm;
        float prevDt = activeBotDt;
        activeBotBm = __instance;
        activeBotDt = dt;
        try
        {
            int count = Players.Count;
            var outArr = Players.Out;
            for (int i = 0; i < count; i++)
            {
                if (Players.IsBot(i) && (outArr == null || i >= outArr.Length || !outArr[i]))
                {
                    callAsBot(__instance, i, cachedBotAction);
                }
            }
        }
        finally
        {
            activeBotBm = prevBm;
            activeBotDt = prevDt;
        }
        return false;
    }

    // -----------------------------------------------------------------------
    // 4. Zero-Allocation Soldier.RefreshThreats (Offline Only)
    // -----------------------------------------------------------------------

    [HarmonyPatch(typeof(Soldier), "RefreshThreats")]
    [HarmonyPrefix]
    private static bool PatchRefreshThreats(Soldier __instance)
    {
        if ((optimizeRefreshThreats != null && !optimizeRefreshThreats.Value)
            || !OfflineOnly()
            || __instance == null
            || soldierBmRef == null || soldierThreatsRef == null || callSoldierSpots == null || soldierNearbyField == null)
        {
            return true;
        }

        var threats = soldierThreatsRef(__instance);
        if (threats == null)
        {
            return true;
        }
        threats.Clear();
        if (__instance.Medic)
        {
            return false;
        }

        var bm = soldierBmRef(__instance);
        var nearby = soldierNearbyField.GetValue(null) as List<Soldier>;
        if (bm == null || nearby == null)
        {
            return true;
        }

        Vector3 p = __instance.transform.position;
        float r = bm.range * (__instance.Sniper ? 2f : 1f);
        bm.Near(p, r * 1.5f, nearby);

        int count = nearby.Count;
        var myFaction = __instance.faction;
        for (int i = 0; i < count; i++)
        {
            Soldier e = nearby[i];
            if (e == null || e.faction == myFaction || !e.Alive)
            {
                continue;
            }

            Vector3 ep = e.transform.position;
            float dx = ep.x - p.x;
            float dy = ep.y - p.y;
            float dz = ep.z - p.z;
            float d2 = dx * dx + dy * dy + dz * dz;
            float hg = r * BattleManager.HighGround(p.y - ep.y);
            if (d2 > hg * hg)
            {
                continue;
            }
            if (e.HiddenFrom(p) || !callSoldierSpots(__instance, ep))
            {
                continue;
            }

            int curCount = threats.Count;
            if (curCount == 5 && d2 >= topThreatDist2[4])
            {
                continue;
            }

            int insertAt = curCount;
            while (insertAt > 0 && d2 < topThreatDist2[insertAt - 1])
            {
                insertAt--;
            }

            if (curCount < 5)
            {
                threats.Add(null);
                for (int k = curCount; k > insertAt; k--)
                {
                    threats[k] = threats[k - 1];
                    topThreatDist2[k] = topThreatDist2[k - 1];
                }
                threats[insertAt] = e;
                topThreatDist2[insertAt] = d2;
            }
            else
            {
                for (int k = 4; k > insertAt; k--)
                {
                    threats[k] = threats[k - 1];
                    topThreatDist2[k] = topThreatDist2[k - 1];
                }
                threats[insertAt] = e;
                topThreatDist2[insertAt] = d2;
            }
        }
        return false;
    }

    // -----------------------------------------------------------------------
    // 5. Spatial-Bucket Mortar Targeting (Offline Only, MortarRangeMod Compatible)
    // -----------------------------------------------------------------------

    public static List<Soldier> GetMortarCandidates(BattleManager bm, Soldier mortar)
    {
        if (bm == null)
        {
            return null;
        }
        if ((spatialMortarTargeting != null && !spatialMortarTargeting.Value) || !OfflineOnly() || mortar == null)
        {
            return bm.Living;
        }

        float baseRange = externalMortarRangeForSoldier != null
            ? externalMortarRangeForSoldier(bm, mortar)
            : bm.mortarRange;
        // BattleManager.HighGround clamps multiplier to [1.0, 1.5], so * 1.5 covers every valid target.
        float maxQueryRadius = Mathf.Max(0f, baseRange) * 1.5f;
        bm.Near(mortar.transform.position, maxQueryRadius, mortarCandidatesBuf);
        return mortarCandidatesBuf;
    }

    [HarmonyPatch(typeof(Soldier), "FindMortarTarget")]
    [HarmonyPriority(Priority.Low)]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> PatchFindMortarTargetSpatial(IEnumerable<CodeInstruction> instructions)
    {
        var list = new List<CodeInstruction>(instructions);
        var helper = AccessTools.Method(typeof(PerformancePlugin), nameof(GetMortarCandidates));
        int hits = 0;

        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].opcode == OpCodes.Ldfld
                && list[i].operand is FieldInfo fi
                && fi.Name == "Living"
                && fi.DeclaringType == typeof(BattleManager))
            {
                // stack currently: [..., bm]
                list[i].opcode = OpCodes.Ldarg_0;
                list[i].operand = null;
                list.Insert(i + 1, new CodeInstruction(OpCodes.Call, helper));
                hits++;
                i += 1;
            }
        }

        log?.LogInfo($"PatchFindMortarTargetSpatial: hits={hits}");
        if (hits == 0)
        {
            log?.LogWarning("PatchFindMortarTargetSpatial found no BattleManager.Living reads.");
        }
        return list;
    }

    // -----------------------------------------------------------------------
    // 6. FrontLine Dirty Skip (No Mesh Rebuild When Stationary)
    // -----------------------------------------------------------------------

    [HarmonyPatch(typeof(FrontLine), "BuildMesh")]
    [HarmonyPrefix]
    private static bool PatchFrontLineBuildMesh(FrontLine __instance)
    {
        if ((frontLineDirtySkip != null && !frontLineDirtySkip.Value) || ReferenceEquals(__instance, null) || frontLineArrayRef == null)
        {
            return true;
        }

        float[] cur = frontLineArrayRef(__instance);
        if (cur == null || cur.Length == 0)
        {
            return true;
        }

        float[] snap;
        if (ReferenceEquals(__instance, lastFrontLine) && lastFrontLineSnap != null)
        {
            snap = lastFrontLineSnap;
        }
        else
        {
            frontLineSnapshots.TryGetValue(__instance, out snap);
        }

        float eps = frontLineMoveEpsilon != null ? frontLineMoveEpsilon.Value : 0.02f;
        if (snap != null && snap.Length == cur.Length)
        {
            bool moved = false;
            for (int i = 0; i < cur.Length; i++)
            {
                if (Math.Abs(cur[i] - snap[i]) >= eps)
                {
                    moved = true;
                    break;
                }
            }
            if (!moved)
            {
                lastFrontLine = __instance;
                lastFrontLineSnap = snap;
                return false;
            }
        }
        else
        {
            snap = new float[cur.Length];
            frontLineSnapshots.Remove(__instance);
            frontLineSnapshots.Add(__instance, snap);
        }

        Array.Copy(cur, snap, cur.Length);
        lastFrontLine = __instance;
        lastFrontLineSnap = snap;
        return true;
    }

    // -----------------------------------------------------------------------
    // 7. FogOfWar Interval & Skip Unchanged GPU Uploads
    // -----------------------------------------------------------------------

    [HarmonyPatch(typeof(FogOfWar), "UpdateInner")]
    [HarmonyPrefix]
    private static void PatchFogUpdateInterval(FogOfWar __instance)
    {
        if (__instance != null && fogUpdateInterval != null && fogUpdateInterval.Value > 0f)
        {
            __instance.updateEvery = fogUpdateInterval.Value;
        }
    }

    [HarmonyPatch(typeof(FogOfWar), "Recompute")]
    [HarmonyPrefix]
    private static void ResetFogDirty(bool instant)
    {
        fogDirty = instant;
    }

    public static void MarkFogDirty()
    {
        fogDirty = true;
    }

    public static void UploadFogIfDirty(FogOfWar fow)
    {
        if (fow == null || fogTexRef == null || fogPxRef == null)
        {
            return;
        }
        if (skipEmptyFogUploads != null && skipEmptyFogUploads.Value && !fogDirty)
        {
            return;
        }
        var tex = fogTexRef(fow);
        var px = fogPxRef(fow);
        if (tex != null && px != null)
        {
            tex.SetPixels32(px);
            tex.Apply(false);
        }
    }

    [HarmonyPatch(typeof(FogOfWar), "Recompute")]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> PatchFogRecomputeUpload(IEnumerable<CodeInstruction> instructions)
    {
        var list = new List<CodeInstruction>(instructions);
        var markDirty = AccessTools.Method(typeof(PerformancePlugin), nameof(MarkFogDirty));
        var uploadIfDirty = AccessTools.Method(typeof(PerformancePlugin), nameof(UploadFogIfDirty));

        int markHits = 0;
        int uploadHits = 0;

        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].opcode == OpCodes.Stelem
                && list[i].operand is Type t
                && t == typeof(Color32))
            {
                list.Insert(i + 1, new CodeInstruction(OpCodes.Call, markDirty));
                markHits++;
                i += 1;
                continue;
            }

            if (i + 8 < list.Count
                && list[i].opcode == OpCodes.Ldarg_0
                && list[i + 1].opcode == OpCodes.Ldfld && list[i + 1].operand is FieldInfo fTex1 && fTex1.Name == "tex"
                && list[i + 2].opcode == OpCodes.Ldarg_0
                && list[i + 3].opcode == OpCodes.Ldfld && list[i + 3].operand is FieldInfo fPx && fPx.Name == "px"
                && list[i + 4].opcode == OpCodes.Callvirt && list[i + 4].operand is MethodInfo mSet && mSet.Name == "SetPixels32"
                && list[i + 5].opcode == OpCodes.Ldarg_0
                && list[i + 6].opcode == OpCodes.Ldfld && list[i + 6].operand is FieldInfo fTex2 && fTex2.Name == "tex"
                && (list[i + 7].opcode == OpCodes.Ldc_I4_0 || list[i + 7].opcode == OpCodes.Ldc_I4)
                && list[i + 8].opcode == OpCodes.Callvirt && list[i + 8].operand is MethodInfo mApply && mApply.Name == "Apply")
            {
                // Keep list[i] = Ldarg_0 (along with any branch labels attached to it)
                list[i + 1].opcode = OpCodes.Call;
                list[i + 1].operand = uploadIfDirty;
                for (int k = 2; k <= 8; k++)
                {
                    list[i + k].opcode = OpCodes.Nop;
                    list[i + k].operand = null;
                }
                uploadHits++;
                i += 8;
            }
        }

        log?.LogInfo($"PatchFogRecomputeUpload: markHits={markHits}, uploadHits={uploadHits}");
        if (markHits == 0 || uploadHits == 0)
        {
            log?.LogWarning("PatchFogRecomputeUpload did not match expected FogOfWar.Recompute IL!");
        }
        return list;
    }

    // -----------------------------------------------------------------------
    // 8. Cap Collider Mesh Swaps Per Frame in VoxelTerrain.ApplyColJobs
    // -----------------------------------------------------------------------

    [HarmonyPatch(typeof(VoxelTerrain), "ApplyColJobs")]
    [HarmonyPrefix]
    private static void ResetColSwaps()
    {
        colSwapsThisFrame = 0;
    }

    public static bool HitColliderSwapCap()
    {
        int cap = maxColliderSwapsPerFrame != null ? maxColliderSwapsPerFrame.Value : 1;
        if (cap <= 0)
        {
            return false;
        }
        colSwapsThisFrame++;
        return colSwapsThisFrame >= cap;
    }

    [HarmonyPatch(typeof(VoxelTerrain), "ApplyColJobs")]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> PatchApplyColJobs(IEnumerable<CodeInstruction> instructions, ILGenerator il)
    {
        var list = new List<CodeInstruction>(instructions);
        var hitCap = AccessTools.Method(typeof(PerformancePlugin), nameof(HitColliderSwapCap));
        int hits = 0;

        for (int i = 2; i < list.Count; i++)
        {
            // Match the second set_sharedMesh (which sets col.sharedMesh = chunk.colMesh after setting null)
            if (list[i].opcode == OpCodes.Callvirt
                && list[i].operand is MethodInfo mi
                && mi.Name == "set_sharedMesh"
                && list[i - 1].opcode == OpCodes.Ldfld
                && list[i - 1].operand is FieldInfo fi
                && fi.Name == "colMesh")
            {
                var skipRet = il.DefineLabel();
                if (i + 1 < list.Count)
                {
                    list[i + 1].labels.Add(skipRet);
                }
                list.Insert(i + 1, new CodeInstruction(OpCodes.Call, hitCap));
                list.Insert(i + 2, new CodeInstruction(OpCodes.Brfalse_S, skipRet));
                list.Insert(i + 3, new CodeInstruction(OpCodes.Ret));
                hits++;
                break;
            }
        }

        log?.LogInfo($"PatchApplyColJobs: hits={hits}");
        if (hits == 0)
        {
            log?.LogWarning("PatchApplyColJobs found no colMesh set_sharedMesh call.");
        }
        return list;
    }

    // -----------------------------------------------------------------------
    // 9. Filter Redundant OnGUI Event Passes
    // -----------------------------------------------------------------------

    private static bool ShouldSkipRepaintOrClick()
    {
        if (filterOnGuiEvents != null && !filterOnGuiEvents.Value)
        {
            return false;
        }
        var ev = Event.current;
        if (ev == null)
        {
            return false;
        }
        var t = ev.type;
        return t != EventType.Repaint && t != EventType.MouseDown && t != EventType.MouseUp;
    }

    private static bool ShouldSkipRepaintOnly()
    {
        if (filterOnGuiEvents != null && !filterOnGuiEvents.Value)
        {
            return false;
        }
        var ev = Event.current;
        return ev != null && ev.type != EventType.Repaint;
    }

    [HarmonyPatch(typeof(CommandUI), "DrawCommand")]
    [HarmonyPrefix]
    private static bool FilterDrawCommand()
    {
        return !ShouldSkipRepaintOrClick();
    }

    [HarmonyPatch(typeof(BattleManager), "DrawHud")]
    [HarmonyPrefix]
    private static bool FilterDrawHud()
    {
        return !ShouldSkipRepaintOrClick();
    }

    [HarmonyPatch(typeof(FirstTips), "Draw")]
    [HarmonyPrefix]
    private static bool FilterFirstTipsDraw()
    {
        return !ShouldSkipRepaintOrClick();
    }

    [HarmonyPatch(typeof(Scoreboard), "Draw")]
    [HarmonyPrefix]
    private static bool FilterScoreboardDraw()
    {
        return !ShouldSkipRepaintOnly();
    }

    [HarmonyPatch(typeof(Perf), "Draw")]
    [HarmonyPrefix]
    private static bool FilterPerfDraw()
    {
        return !ShouldSkipRepaintOnly();
    }

    // -----------------------------------------------------------------------
    // 10. Visual Budgets: Health-Bar Distance & Debris Caps
    // -----------------------------------------------------------------------

    public static float GetHealthBarMaxDistance()
    {
        return healthBarMaxDistance != null ? healthBarMaxDistance.Value : 120f;
    }

    [HarmonyPatch(typeof(SoldierView), "LateUpdateInner")]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> PatchHealthBarDistance(IEnumerable<CodeInstruction> instructions)
    {
        var list = new List<CodeInstruction>(instructions);
        var helper = AccessTools.Method(typeof(PerformancePlugin), nameof(GetHealthBarMaxDistance));
        int hits = 0;

        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].opcode == OpCodes.Ldc_R4 && list[i].operand is float f && Math.Abs(f - 120f) < 0.01f)
            {
                list[i].opcode = OpCodes.Call;
                list[i].operand = helper;
                hits++;
            }
        }

        log?.LogInfo($"PatchHealthBarDistance: hits={hits}");
        if (hits == 0)
        {
            log?.LogWarning("PatchHealthBarDistance found no 120f literal in SoldierView.LateUpdateInner.");
        }
        return list;
    }

    [HarmonyPatch(typeof(VoxelDebris), "Awake")]
    [HarmonyPrefix]
    private static void TuneVoxelDebris(VoxelDebris __instance)
    {
        if (__instance == null)
        {
            return;
        }
        if (debrisMaxPieces != null && debrisMaxPieces.Value >= 0)
        {
            __instance.maxPieces = debrisMaxPieces.Value;
        }
        if (debrisMaxResting != null && debrisMaxResting.Value >= 0)
        {
            __instance.maxResting = debrisMaxResting.Value;
        }
        if (debrisDepositChance != null && debrisDepositChance.Value >= 0f)
        {
            __instance.depositChance = debrisDepositChance.Value;
        }
    }
}
