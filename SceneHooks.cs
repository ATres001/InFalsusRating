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

public static class SceneHooks
{
    public static void OnSceneEnable(object __instance)
    {
        if (__instance == null) return;
        string name = "?";
        try { name = __instance.GetType().FullName; } catch { }
        SceneTracker.Increment(name);
        if (name == "ifapp.Game.Scenes.SongSelectScene") SceneProbe.Scene = __instance;
        if (name == "ifapp.Game.Scenes.GameScene")
        {
            SceneProbe.Game = __instance;

            Interlocked.Increment(ref SceneProbe.GameSeq);
        }

        if (name == "ifapp.Game.Scenes.HubScene"
            || name == "ifapp.Game.Scenes.SongSelectScene"
            || name == "ifapp.Game.Scenes.GameScene")
        {
            SceneTracker.SetPostGameResults(false);
        }
    }

    public static void OnResultsSceneStart(object __instance)
    {
        if (__instance != null) SceneProbe.Results = __instance;
    }

        public static void OnGameSceneExit()
    {
        Interlocked.Increment(ref SceneProbe.QuitSeq);
    }

    public static void OnSceneDisable(object __instance)
    {
        if (__instance == null) return;
        string name = "?";
        try { name = __instance.GetType().FullName; } catch { }
        SceneTracker.Decrement(name);
        if (name == "ifapp.Game.Scenes.SongSelectScene") SceneProbe.Scene = null;
        if (name == "ifapp.Game.Scenes.GameScene") SceneProbe.Game = null;

        if (name == "ifapp.Game.Scenes.ResultsScene") SceneProbe.Results = null;

        if (name == "ifapp.Game.Scenes.GameScene")
        {
            SceneTracker.SetPostGameResults(true);
            StartDelayedRecalculate();
        }
    }

    public static void StartDelayedRecalculate(string reason = "结算", int delayMs = 1000)
    {
        var t = new Thread(() =>
        {
            try
            {
                if (delayMs > 0) Thread.Sleep(delayMs);
                var r = RatingEngine.LastGameResults;
                if (r == null)
                {
                    Plugin.Logger.LogInfo("[Results] 延迟重算：无缓存的 GameResults");
                    return;
                }

                Plugin.Logger.LogInfo($"[Results] 延迟重算触发（{reason}）");
                RatingEngine.Recalculate(r, reason, throttle: false);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[Results] 延迟重算失败（{reason}）: {ex.Message}");
            }
        });
        t.IsBackground = true;
        t.Start();
    }
}