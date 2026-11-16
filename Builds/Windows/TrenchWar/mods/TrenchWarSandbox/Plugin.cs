using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using HarmonyLib;

[assembly: AssemblyVersion("1.0.2.0")]

// Trench War — Sandbox mode
// Adds a third "SANDBOX" entry to the singleplayer mode picker:
//   * 6-way free-for-all against bots (defaults to 5 bots, adjustable 2..5)
//   * unlimited resources for the local player
//   * the local HQ cannot be destroyed (keeps construction & AI targeting working)
//   * no victory/defeat screen, the battle never ends
//   * the local player is never eliminated
//   * SURRENDER in the pause menu leaves back to the title screen
//   * career achievements/stats are paused while in sandbox

[BepInPlugin("xano.trenchwar.sandbox", "Trench War Sandbox", "1.0.2")]
public class SandboxPlugin : BaseUnityPlugin
{
    public static readonly string[] Modes = { "DUEL  1 VS 1", "FREE-FOR-ALL", "SANDBOX" };

    public static bool Active;

    private static ManualLogSource log;
    private static bool leaving;

    private void Awake()
    {
        log = Logger;
        var harmony = new Harmony("xano.trenchwar.sandbox");
        try
        {
            // Pass the assembly explicitly rather than zero-arg PatchAll(), which walks
            // StackTrace.GetFrame(1) and silently patches 0 methods if inlined by Mono JIT.
            harmony.PatchAll(typeof(SandboxPlugin).Assembly);

            int patched = 0;
            foreach (var m in harmony.GetPatchedMethods())
            {
                patched++;
                log.LogInfo("Patched: " + m.DeclaringType?.FullName + "." + m.Name);
            }

            if (patched == 0)
            {
                log.LogError("Sandbox mod loaded, but 0 methods were patched!");
                LogTargetDiagnostics();
            }
            else
            {
                log.LogInfo("Sandbox mod loaded (v1.0.2, " + patched + " methods patched).");
            }
        }
        catch (Exception ex)
        {
            log.LogError("Sandbox mod failed to patch: " + ex);
            LogTargetDiagnostics();
        }
    }

    private static void LogTargetDiagnostics()
    {
        if (log == null)
        {
            return;
        }
        var checks = new (Type type, string name)[]
        {
            (typeof(StartMenu), "Single"),
            (typeof(BattleManager), "StockOf"),
            (typeof(BattleManager), "StockOfPlayer"),
            (typeof(Building), "Damage"),
            (typeof(Building), "Blow"),
            (typeof(BattleManager), "EndBattle"),
            (typeof(BattleManager), "PlayerOut"),
            (typeof(BattleManager), "Surrender"),
            (typeof(Achievements), "CountsFor"),
        };
        foreach (var c in checks)
        {
            var found = AccessTools.Method(c.type, c.name);
            log.LogWarning("Target " + c.type.FullName + "." + c.name + ": " + (found != null ? "FOUND" : "MISSING"));
        }
    }

    private static bool InSandboxBattle()
    {
        return Active
            && !Net.Active
            && !Net.ViaRelay
            && !Lobby.InMatch
            && !Players.Preset
            && Players.FreeForAll
            && !Players.IsBot(Players.Local);
    }

    private static void Refill(Stock stock)
    {
        if (stock == null)
        {
            return;
        }
        stock.fuel = 999999;
        stock.iron = 999999;
        stock.food = 999999;
    }

    // ----- menu plumbing (called from the patched StartMenu.Single) -----

    public static int ModeIndex()
    {
        if (Active)
        {
            return 2;
        }
        return MatchSettings.SoloFreeForAll ? 1 : 0;
    }

    public static void SetMode(int i)
    {
        bool wasActive = Active;
        bool wasFfa = MatchSettings.SoloFreeForAll;

        Active = i == 2;
        MatchSettings.SoloFreeForAll = i >= 1;
        if (Active && !wasActive)
        {
            MatchSettings.SoloBots = 5; // default to 6 players when switching into SANDBOX; user can still adjust 2..5
        }

        if (Active != wasActive || MatchSettings.SoloFreeForAll != wasFfa)
        {
            log?.LogInfo("Sandbox " + (Active ? "ON" : "off") + "; solo FFA=" + MatchSettings.SoloFreeForAll + ", bots=" + MatchSettings.SoloBots);
        }
    }

    // ----- menu: replace the two-entry mode toggle with three entries -----

    [HarmonyPatch(typeof(StartMenu), "Single")]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> PatchSingle(IEnumerable<CodeInstruction> instructions)
    {
        var list = new List<CodeInstruction>(instructions);

        var soloModes = typeof(StartMenu).GetField("SoloModes", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        var soloFfa = AccessTools.Field(typeof(MatchSettings), "SoloFreeForAll");
        var modesField = AccessTools.Field(typeof(SandboxPlugin), nameof(Modes));
        var modeIndex = AccessTools.Method(typeof(SandboxPlugin), nameof(ModeIndex));
        var setMode = AccessTools.Method(typeof(SandboxPlugin), nameof(SetMode));

        int hitModes = 0;
        int hitRead = 0;
        int hitWrite = 0;

        if (soloModes == null || soloFfa == null)
        {
            log?.LogError("PatchSingle missing expected fields: SoloModes=" + (soloModes != null) + ", SoloFreeForAll=" + (soloFfa != null));
        }

        for (int i = 0; i < list.Count; i++)
        {
            // 1) use our three-entry label array instead of StartMenu.SoloModes
            if (soloModes != null && list[i].opcode == OpCodes.Ldsfld && list[i].operand is FieldInfo f && f == soloModes)
            {
                list[i].operand = modesField;
                hitModes++;
                continue;
            }

            // 2) "MatchSettings.SoloFreeForAll ? 1 : 0"  ->  ModeIndex()
            if (soloFfa != null && list[i].opcode == OpCodes.Ldsfld && list[i].operand is FieldInfo f2 && f2 == soloFfa
                && i + 4 < list.Count
                && (list[i + 1].opcode == OpCodes.Brtrue_S || list[i + 1].opcode == OpCodes.Brtrue
                    || list[i + 1].opcode == OpCodes.Brfalse_S || list[i + 1].opcode == OpCodes.Brfalse)
                && (list[i + 2].opcode == OpCodes.Ldc_I4_0 || list[i + 2].opcode == OpCodes.Ldc_I4_1)
                && (list[i + 3].opcode == OpCodes.Br_S || list[i + 3].opcode == OpCodes.Br)
                && (list[i + 4].opcode == OpCodes.Ldc_I4_0 || list[i + 4].opcode == OpCodes.Ldc_I4_1))
            {
                list[i].opcode = OpCodes.Call;
                list[i].operand = modeIndex;
                for (int k = 1; k <= 4; k++)
                {
                    list[i + k].opcode = OpCodes.Nop;
                    list[i + k].operand = null;
                }
                hitRead++;
                i += 4;
                continue;
            }

            // 3) "SoloFreeForAll = (Options(...) == 1)"  ->  SetMode(Options(...))
            if (soloFfa != null && list[i].opcode == OpCodes.Stsfld && list[i].operand is FieldInfo f3 && f3 == soloFfa
                && i >= 2 && list[i - 1].opcode == OpCodes.Ceq && list[i - 2].opcode == OpCodes.Ldc_I4_1)
            {
                list[i].opcode = OpCodes.Call;
                list[i].operand = setMode;
                list[i - 1].opcode = OpCodes.Nop;
                list[i - 1].operand = null;
                list[i - 2].opcode = OpCodes.Nop;
                list[i - 2].operand = null;
                hitWrite++;
                continue;
            }
        }

        log?.LogInfo($"PatchSingle: modes={hitModes}, read={hitRead}, write={hitWrite} (insns={list.Count})");
        if (hitModes != 1 || hitRead != 1 || hitWrite != 1)
        {
            log?.LogError("PatchSingle did not match expected StartMenu.Single IL! Game build may differ from 2026-11-14.");
            int limit = Math.Min(list.Count, 45);
            for (int i = 0; i < limit; i++)
            {
                log?.LogWarning($"  Single[{i:D2}] {list[i].opcode} {list[i].operand}");
            }
        }

        return list;
    }

    // ----- unlimited resources for the local player -----

    [HarmonyPatch(typeof(BattleManager), "StockOf")]
    [HarmonyPostfix]
    private static void FreeStock(Faction f, ref Stock __result)
    {
        if (!InSandboxBattle() || f != Players.Team(Players.Local))
        {
            return;
        }
        Refill(__result);
    }

    [HarmonyPatch(typeof(BattleManager), "StockOfPlayer")]
    [HarmonyPostfix]
    private static void FreePlayerStock(int p, ref Stock __result)
    {
        if (!InSandboxBattle() || p != Players.Local)
        {
            return;
        }
        Refill(__result);
    }

    // ----- protect the local HQ from destruction (keeps building & AI targeting alive) -----

    [HarmonyPatch(typeof(Building), "Damage")]
    [HarmonyPrefix]
    private static bool KeepLocalHqAlive(Building __instance, ref float amount)
    {
        if (!InSandboxBattle() || __instance == null)
        {
            return true;
        }
        if (__instance.kind == Building.Kind.HQ && __instance.owner == Players.Local)
        {
            if (__instance.Destroyed || amount <= 0f)
            {
                return false;
            }
            float maxDamage = __instance.Hp - 1f;
            if (maxDamage <= 0f)
            {
                return false;
            }
            if (amount > maxDamage)
            {
                amount = maxDamage;
            }
        }
        return true;
    }

    [HarmonyPatch(typeof(Building), "Blow")]
    [HarmonyPrefix]
    private static bool PreventLocalHqBlow(Building __instance)
    {
        if (!InSandboxBattle() || __instance == null)
        {
            return true;
        }
        return __instance.kind != Building.Kind.HQ || __instance.owner != Players.Local;
    }

    // ----- no victory / defeat: the battle never ends -----

    [HarmonyPatch(typeof(BattleManager), "EndBattle")]
    [HarmonyPrefix]
    private static bool NoEndBattle()
    {
        return !InSandboxBattle();
    }

    // ----- the local player is never eliminated -----

    [HarmonyPatch(typeof(BattleManager), "PlayerOut")]
    [HarmonyPrefix]
    private static bool NoEliminate(int p)
    {
        return !InSandboxBattle() || p != Players.Local;
    }

    // ----- SURRENDER leaves to the title screen instead of ending the run -----

    [HarmonyPatch(typeof(BattleManager), "Surrender")]
    [HarmonyPrefix]
    private static bool SandboxSurrender()
    {
        if (!InSandboxBattle())
        {
            return true;
        }
        if (!leaving)
        {
            leaving = true;
            try
            {
                BattleManager.BackToTitle();
            }
            finally
            {
                leaving = false;
            }
        }
        return false;
    }

    // ----- do not pollute career profile achievements/stats in sandbox -----

    [HarmonyPatch(typeof(Achievements), "CountsFor")]
    [HarmonyPrefix]
    private static bool SkipAchievementsInSandbox(ref bool __result)
    {
        if (!InSandboxBattle())
        {
            return true;
        }
        __result = false;
        return false;
    }
}
