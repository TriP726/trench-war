using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using HarmonyLib;
using UnityEngine;

[assembly: AssemblyVersion("1.0.1.0")]

// Mortar Range Per Nation
// Scales the maximum mortar range of each nation independently (singleplayer/offline only).
// Values live in BepInEx/config/xano.trenchwar.mortarrange.cfg after the first run.

[BepInPlugin("xano.trenchwar.mortarrange", "Mortar Range Per Nation", "1.0.1")]
[HarmonyPatch]
public class MortarRangePlugin : BaseUnityPlugin
{
    private static ManualLogSource log;
    private static ConfigEntry<float> germany;
    private static ConfigEntry<float> britain;
    private static ConfigEntry<float> usa;
    private static ConfigEntry<float> vietnam;
    private static ConfigEntry<float> soviet;
    private static ConfigEntry<float> japan;

    private void Awake()
    {
        log = Logger;

        const string section = "Mortar range";
        var desc = new ConfigDescription("Mortar max range multiplier. 1 = game default (170). 1.5 = +50%.", new AcceptableValueRange<float>(0.1f, 10f));

        germany = Config.Bind(section, "Germany 1940", 1f, desc);
        britain = Config.Bind(section, "Great Britain 1940", 1f, desc);
        usa     = Config.Bind(section, "USA 1965", 1f, desc);
        vietnam = Config.Bind(section, "North Vietnam 1965", 1f, desc);
        soviet  = Config.Bind(section, "Soviet Union 1941", 1f, desc);
        japan   = Config.Bind(section, "Japan 1942", 1f, desc);

        var harmony = new Harmony("xano.trenchwar.mortarrange");
        try
        {
            harmony.PatchAll(typeof(MortarRangePlugin));

            int patched = 0;
            foreach (var m in harmony.GetPatchedMethods())
            {
                patched++;
                log.LogInfo("Patched: " + m.DeclaringType?.FullName + "." + m.Name);
            }

            if (patched == 0)
            {
                log.LogError("Mortar Range Per Nation loaded, but 0 methods were patched!");
            }
            else
            {
                log.LogInfo(
                    "Mortar Range Per Nation loaded (v1.0.1, " + patched + " methods patched; "
                    + "Germany x" + germany.Value + ", UK x" + britain.Value
                    + ", USA x" + usa.Value + ", Vietnam x" + vietnam.Value
                    + ", Soviet x" + soviet.Value + ", Japan x" + japan.Value + ")."
                );
            }
        }
        catch (Exception ex)
        {
            log.LogError("Mortar Range Per Nation failed to patch: " + ex);
        }
    }

    private static bool OfflineOnly()
    {
        return !Net.Active && !Net.ViaRelay && !Lobby.InMatch;
    }

    public static float Scale(Nation n)
    {
        if (!OfflineOnly())
        {
            return 1f;
        }
        if (n == Nation.Germany1940 && germany != null) return germany.Value;
        if (n == Nation.Britain1940 && britain != null) return britain.Value;
        if (n == Nation.USA1965     && usa     != null) return usa.Value;
        if (n == Nation.Vietnam1965 && vietnam != null) return vietnam.Value;
        if (n == Nation.Soviet1941  && soviet  != null) return soviet.Value;
        if (n == Nation.Japan1942   && japan   != null) return japan.Value;
        return 1f;
    }

    // Multiplier for a faction's nation.
    public static float Scale(Faction f)
    {
        return Scale(Nations.Of(f));
    }

    public static float ScaleForSquad(Squad q)
    {
        if (q == null)
        {
            return 1f;
        }
        if (q.owner >= 0 && q.owner < Players.Count)
        {
            return Scale(Players.NationOf(q.owner));
        }
        return Scale(q.faction);
    }

    public static float ScaleForSoldier(Soldier s)
    {
        if (s == null)
        {
            return 1f;
        }
        if (s.squad != null)
        {
            return ScaleForSquad(s.squad);
        }
        return Scale(s.faction);
    }

    // Helper called from the patched methods (the BattleManager instance is already on the stack).
    public static float RangeFor(BattleManager bm, Faction f)
    {
        return bm != null ? bm.mortarRange * Scale(f) : 0f;
    }

    public static float RangeForSoldier(BattleManager bm, Soldier s)
    {
        return bm != null ? bm.mortarRange * ScaleForSoldier(s) : 0f;
    }

    // Replaces every "bm.mortarRange" read inside a Soldier method with RangeForSoldier(bm, this).
    private static IEnumerable<CodeInstruction> ScaleSoldierRange(IEnumerable<CodeInstruction> instructions, string callerName)
    {
        var list = new List<CodeInstruction>(instructions);
        var helper = AccessTools.Method(typeof(MortarRangePlugin), nameof(RangeForSoldier));

        int hits = 0;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].opcode == OpCodes.Ldfld &&
                list[i].operand is FieldInfo fi &&
                fi.Name == "mortarRange" &&
                fi.DeclaringType == typeof(BattleManager))
            {
                // stack currently: [..., bm]
                list[i].opcode = OpCodes.Ldarg_0;                                   // [..., bm, this]
                list[i].operand = null;
                list.Insert(i + 1, new CodeInstruction(OpCodes.Call, helper));      // [..., range]
                hits++;
                i += 1;
            }
        }

        log?.LogInfo($"ScaleSoldierRange({callerName}): hits={hits} (insns={list.Count})");
        if (hits == 0)
        {
            log?.LogError($"ScaleSoldierRange({callerName}) found no BattleManager.mortarRange reads!");
        }
        return list;
    }

    // Where a mortar soldier picks its target (actual firing range).
    [HarmonyPatch(typeof(Soldier), "FindMortarTarget")]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> PatchFindMortarTarget(IEnumerable<CodeInstruction> instructions)
    {
        return ScaleSoldierRange(instructions, "Soldier.FindMortarTarget");
    }

    // Where a mortar soldier decides to hold position instead of advancing.
    [HarmonyPatch(typeof(Soldier), "WantsAdvance")]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> PatchWantsAdvance(IEnumerable<CodeInstruction> instructions)
    {
        return ScaleSoldierRange(instructions, "Soldier.WantsAdvance");
    }

    // Engagement range used by the battle manager / AI for mortar squads.
    [HarmonyPatch(typeof(BattleManager), "EngageReach")]
    [HarmonyPostfix]
    private static void PatchEngageReach(ref float __result, Squad q)
    {
        if (q != null && q.kind == SquadKind.Mortar)
        {
            __result *= ScaleForSquad(q);
        }
    }
}
