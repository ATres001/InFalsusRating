using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using BepInEx.Logging;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using ifapp.Game.Data;

namespace InFalsusRating;

public static class Patch_NH_VNA
{
    static MethodBase TargetMethod()
    {
        Type nhType = null;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            nhType = asm.GetType("_K._NH");
            if (nhType != null) break;
        }
        return nhType?.GetMethod("_vNA", BindingFlags.Public | BindingFlags.Instance);
    }

    static void Postfix(object __instance, object __result)
    {
        if (__instance != null)
            SongInfoCache.Load(__instance);

        if (__result == null) return;
        RatingEngine.LastGameResults = __result;
        RatingEngine.Recalculate(__result, "界面刷新");
    }
}