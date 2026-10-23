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

public static class ResultsDetector
{
    private static readonly string[] _candidates = new[]
    {
        "ResultsGradeContainer",
        "ResultsTopBannerContainer",
        "ResultsBottomBannerContainer",
        "ResultsJacketContainer",
        "ResultsBattleLayer",
        "ResultsClearBadgeRow",
    };

    private static DateTime _lastCheck = DateTime.MinValue;
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(300);

    public static void Tick()
    {

        if (SceneProbe.Game != null)
        {
            if (SceneTracker.IsInResultsScreen)
            {
                SceneTracker.IsInResultsScreen = false;
                Plugin.Logger.LogInfo("[Results] 结算界面: 退出（谱面起来了）");
            }
            return;
        }

        var now = DateTime.Now;
        if ((now - _lastCheck) < Interval) return;
        _lastCheck = now;

        bool active = false;
        try
        {
            foreach (var name in _candidates)
            {
                var go = UnityEngine.GameObject.Find(name);
                if (go != null && go.activeInHierarchy)
                {
                    active = true;
                    break;
                }
            }
        }
        catch { }

        if (active != SceneTracker.IsInResultsScreen)
        {
            SceneTracker.IsInResultsScreen = active;
            Plugin.Logger.LogInfo($"[Results] 结算界面: {(active ? "进入" : "退出")}");
        }

    }
}