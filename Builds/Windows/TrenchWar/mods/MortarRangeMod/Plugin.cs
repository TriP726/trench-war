using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.Mono;
using HarmonyLib;
using UnityEngine;

// Mortar Range Per Nation
// Scales the maximum mortar range of each nation independently.
// Values live in BepInEx/config/xano.trenchwar.mortarrange.cfg after the first run.

[BepInPlugin("xano.trenchwar.mortarrange", "Mortar Range Per Nation", "1.0.0")]
public class MortarRangePlugin : BaseUnityPlugin
{
    private static ConfigEntry<float> germany;
    private static ConfigEntry<float> britain;
    private static ConfigEntry<float> usa;
    private static ConfigEntry<float> vietnam;
    private static ConfigEntry<float> soviet;
    private static ConfigEntry<float> japan;

    private void Awake()
    {
        const string section = "Mortar range";
        var desc = new ConfigDescription("Mortar max range multiplier. 1 = game default (170). 1.5 = +50%.", new AcceptableValueRange<float>(0.1f, 10f));

        germany = Config.Bind(section, "Germany 1940", 1f, desc);
        britain = Config.Bind(section, "Great Britain 1940", 1f, desc);
        usa     = Config.Bind(section, "USA 1965", 1f, desc);
        vietnam = Config.Bind(section, "North Vietnam 1965", 1f, desc);
        soviet  = Config.Bind(section, "Soviet Union 1941", 1f, desc);
        japan   = Config.Bind(section, "Japan 1942", 1f, desc);

        new Harmony("xano.trenchwar.mortarrange").PatchAll();

        Logger.LogInfo("Mortar Range Per Nation loaded (Germany x" + germany.Value + ", ... Japan x" + japan.Value + ").");
    }

    // Multiplier for a faction's nation.
    public static float Scale(Faction f)
    {
        Nation n = Nations.Of(f);
        if (n == Nation.Germany1940)  return germany.Value;
        if (n == Nation.Britain1940)  return britain.Value;
        if (n == Nation.USA1965)      return usa.Value;
        if (n == Nation.Vietnam1965)  return vietnam.Value;
        if (n == Nation.Soviet1941)   return soviet.Value;
        if (n == Nation.Japan1942)    return japan.Value;
        return 1f;
    }

    // Helper called from the patched methods (the BattleManager instance is already on the stack).
    public static float RangeFor(BattleManager bm, Faction f)
    {
        return bm.mortarRange * Scale(f);
    }

    // Replaces every "bm.mortarRange" read inside a Soldier method with RangeFor(bm, this.faction).
    private static IEnumerable<CodeInstruction> ScaleSoldierRange(IEnumerable<CodeInstruction> instructions)
    {
        var list = new List<CodeInstruction>(instructions);
        var factionField = AccessTools.Field(typeof(Soldier), "faction");
        var helper = AccessTools.Method(typeof(MortarRangePlugin), nameof(RangeFor));

        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].opcode == OpCodes.Ldfld &&
                list[i].operand is FieldInfo fi &&
                fi.Name == "mortarRange" &&
                fi.DeclaringType == typeof(BattleManager))
            {
                // stack currently: [..., bm]
                list[i] = new CodeInstruction(OpCodes.Ldarg_0);              // [..., bm, this]
                list.Insert(i + 1, new CodeInstruction(OpCodes.Ldfld, factionField)); // [..., bm, faction]
                list.Insert(i + 2, new CodeInstruction(OpCodes.Call, helper));        // [..., range]
                i += 2;
            }
        }
        return list;
    }

    // Where a mortar soldier picks its target (actual firing range).
    [HarmonyPatch(typeof(Soldier), "FindMortarTarget")]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> PatchFindMortarTarget(IEnumerable<CodeInstruction> instructions)
    {
        return ScaleSoldierRange(instructions);
    }

    // Where a mortar soldier decides to hold position instead of advancing.
    [HarmonyPatch(typeof(Soldier), "WantsAdvance")]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> PatchWantsAdvance(IEnumerable<CodeInstruction> instructions)
    {
        return ScaleSoldierRange(instructions);
    }

    // Engagement range used by the battle manager / AI for mortar squads.
    [HarmonyPatch(typeof(BattleManager), "EngageReach")]
    [HarmonyPostfix]
    private static void PatchEngageReach(ref float __result, Squad q)
    {
        if (q != null && q.kind == SquadKind.Mortar)
        {
            __result *= Scale(q.faction);
        }
    }
}
