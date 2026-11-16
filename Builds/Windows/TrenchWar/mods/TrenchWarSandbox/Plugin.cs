using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using HarmonyLib;

// Trench War — Sandbox mode
// Adds a third "SANDBOX" entry to the singleplayer mode picker:
//   * 6-way free-for-all against bots
//   * unlimited resources for the local player
//   * no victory/defeat screen, the battle never ends
//   * the local player is never eliminated
//   * SURRENDER in the pause menu leaves back to the title screen

[BepInPlugin("xano.trenchwar.sandbox", "Trench War Sandbox", "1.0.0")]
public class SandboxPlugin : BaseUnityPlugin
{
    public static readonly string[] Modes = { "DUEL  1 VS 1", "FREE-FOR-ALL", "SANDBOX" };

    public static bool Active;

    private static ManualLogSource log;
    private static bool leaving;

    private void Awake()
    {
        log = Logger;
        new Harmony("xano.trenchwar.sandbox").PatchAll();
        log.LogInfo("Sandbox mod loaded.");
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
        Active = i == 2;
        MatchSettings.SoloFreeForAll = i >= 1;
        if (Active)
        {
            MatchSettings.SoloBots = 5; // fill the remaining seats -> 6 players
        }
        log?.LogInfo("Sandbox " + (Active ? "ON" : "off") + "; solo FFA=" + MatchSettings.SoloFreeForAll + ", bots=" + MatchSettings.SoloBots);
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

        for (int i = 0; i < list.Count; i++)
        {
            // 1) use our three-entry label array instead of StartMenu.SoloModes
            if (list[i].opcode == OpCodes.Ldsfld && list[i].operand is FieldInfo f && f == soloModes)
            {
                list[i].operand = modesField;
                continue;
            }

            // 2) "MatchSettings.SoloFreeForAll ? 1 : 0"  ->  ModeIndex()
            if (list[i].opcode == OpCodes.Ldsfld && list[i].operand is FieldInfo f2 && f2 == soloFfa
                && i + 1 < list.Count
                && (list[i + 1].opcode == OpCodes.Brtrue_S || list[i + 1].opcode == OpCodes.Brtrue))
            {
                list[i].opcode = OpCodes.Call;
                list[i].operand = modeIndex;
                for (int k = 1; k <= 4 && i + k < list.Count; k++)
                {
                    list[i + k].opcode = OpCodes.Nop;
                }
                i += 4;
                continue;
            }

            // 3) "SoloFreeForAll = (Options(...) == 1)"  ->  SetMode(Options(...))
            if (list[i].opcode == OpCodes.Stsfld && list[i].operand is FieldInfo f3 && f3 == soloFfa
                && i >= 2 && list[i - 1].opcode == OpCodes.Ceq && list[i - 2].opcode == OpCodes.Ldc_I4_1)
            {
                list[i].opcode = OpCodes.Call;
                list[i].operand = setMode;
                list[i - 1].opcode = OpCodes.Nop;
                list[i - 2].opcode = OpCodes.Nop;
                continue;
            }
        }
        return list;
    }

    // ----- unlimited resources for the local player -----

    [HarmonyPatch(typeof(BattleManager), "StockOf")]
    [HarmonyPostfix]
    private static void FreeStock(Faction f, ref Stock __result)
    {
        if (!Active || __result == null)
        {
            return;
        }
        if (f != Players.Team(Players.Local))
        {
            return;
        }
        __result.fuel = 999999;
        __result.iron = 999999;
        __result.food = 999999;
    }

    // ----- no victory / defeat: the battle never ends -----

    [HarmonyPatch(typeof(BattleManager), "EndBattle")]
    [HarmonyPrefix]
    private static bool NoEndBattle()
    {
        return !Active;
    }

    // ----- the local player is never eliminated -----

    [HarmonyPatch(typeof(BattleManager), "PlayerOut")]
    [HarmonyPrefix]
    private static bool NoEliminate(int p)
    {
        return !Active || p != Players.Local;
    }

    // ----- SURRENDER leaves to the title screen instead of ending the run -----

    [HarmonyPatch(typeof(BattleManager), "Surrender")]
    [HarmonyPrefix]
    private static bool SandboxSurrender()
    {
        if (!Active)
        {
            return true;
        }
        if (!leaving)
        {
            leaving = true;
            BattleManager.BackToTitle();
            leaving = false;
        }
        return false;
    }
}
