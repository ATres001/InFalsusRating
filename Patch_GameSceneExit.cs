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
public static class Patch_GameSceneExit
{
    public static MethodBase TargetMethod()
    {
        Type t = null;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            t = asm.GetType("ifapp.Game.Scenes.GameScene");
            if (t != null) break;
        }
        return t?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                 .FirstOrDefault(x => x.Name == "_Sk" && x.GetParameters().Length == 0);
    }

    static void Prefix()
    {
        if (!SceneProbe.OurAbort) SceneHooks.OnGameSceneExit();
    }
}