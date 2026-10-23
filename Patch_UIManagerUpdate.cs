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

[HarmonyPatch]
public static class Patch_UIManagerUpdate
{
    static MethodBase TargetMethod()
    {
        Type t = null;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            t = asm.GetType("ifapp.Game.UI.Common.UIManager");
            if (t != null) break;
        }
        return t?.GetMethod("Update", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
    }

    static void Postfix()
    {
        ResultsDetector.Tick();
        SceneProbe.Tick();
        GameProbe.Tick();
        TaskRunner.Tick();
        FreeRun.Tick();
    }
}