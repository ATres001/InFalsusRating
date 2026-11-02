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

public static class SceneTracker
{
    public static volatile bool IsInResultsScreen = false;
    public static volatile bool IsPostGameResults = false;

    private const string SCENE_GAME       = "ifapp.Game.Scenes.GameScene";
    private const string SCENE_SONGSELECT = "ifapp.Game.Scenes.SongSelectScene";
    private const string SCENE_RESULTS    = "ifapp.Game.Scenes.ResultsScene";

    private static readonly string[] _candidateScenes = new[]
    {
        "ifapp.Game.Scenes.TitleScene",
        "ifapp.Game.Scenes.HubScene",
        "ifapp.Game.Scenes.SongSelectScene",
        "ifapp.Game.Scenes.ResultsScene",
        "ifapp.Game.Scenes.PackSelectScene",
        "ifapp.Game.Scenes.StoryScene",
        "ifapp.Game.Scenes.StorySongTransitionScene",
        "ifapp.Game.Scenes.TimelineScene",
        "ifapp.Game.Scenes.CreditsScene",
        "ifapp.Game.Scenes.GameScene",
        "ifapp.Game.Scenes.CharacterSelectLayer",
    };

    private static List<string> _sceneStack = new List<string>();
    private static readonly object _lock = new object();
    private static bool _patched = false;
    private static readonly HashSet<string> _handled = new HashSet<string>();

    private static bool _lastShouldShow = false;
    private static bool _shouldShowInit = false;

    public static void SetPostGameResults(bool v)
    {
        if (IsPostGameResults != v)
        {
            IsPostGameResults = v;
            Plugin.Logger.LogInfo($"[Scene] IsPostGameResults → {v}");
        }
    }

    public static bool ShouldShowOverlay()
    {
        bool result;

        if (IsInResultsScreen || IsPostGameResults)
        {
            result = true;
        }
        else
        {
            lock (_lock)
            {

                if (_sceneStack.Contains(SCENE_RESULTS))
                    result = true;

                else if (_sceneStack.Contains(SCENE_GAME))
                    result = false;
                else if (_sceneStack.Contains(SCENE_SONGSELECT))
                    result = true;
                else
                    result = false;
            }
        }

        if (!_shouldShowInit || result != _lastShouldShow)
        {
            _shouldShowInit = true;
            _lastShouldShow = result;
            Plugin.Logger.LogInfo($"[Scene] ShouldShowOverlay → {result} (stack={GetStackSnapshot()})");
        }

        return result;
    }

    public static bool TryEnsurePatched()
    {
        if (_patched) return true;

        try
        {
            var harmony = new Harmony("com.atres.infalsusrating.scene");
            var enter = typeof(SceneHooks).GetMethod("OnSceneEnable", BindingFlags.Static | BindingFlags.Public);
            var exit  = typeof(SceneHooks).GetMethod("OnSceneDisable", BindingFlags.Static | BindingFlags.Public);

            foreach (var className in _candidateScenes)
            {
                if (_handled.Contains(className)) continue;

                Type t = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    t = asm.GetType(className);
                    if (t != null) break;
                }
                if (t == null) continue;

                try
                {
                    var onEnable  = t.GetMethod("OnEnable",  BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    var onDisable = t.GetMethod("OnDisable", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    var awake     = t.GetMethod("Awake",     BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    var onDestroy = t.GetMethod("OnDestroy", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                    bool didEnter = false, didExit = false;

                    if (onEnable != null) { harmony.Patch(onEnable, postfix: new HarmonyMethod(enter)); didEnter = true; }
                    else if (awake != null) { harmony.Patch(awake, postfix: new HarmonyMethod(enter)); didEnter = true; }

                    if (onDisable != null) { harmony.Patch(onDisable, postfix: new HarmonyMethod(exit)); didExit = true; }
                    else if (onDestroy != null) { harmony.Patch(onDestroy, postfix: new HarmonyMethod(exit)); didExit = true; }

                    if (didEnter && didExit)
                        Plugin.Logger.LogInfo($"[Scene] patch {t.Name}");

                    if (className == "ifapp.Game.Scenes.ResultsScene")
                    {
                        var start = t.GetMethod("Start", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (start != null)
                        {
                            harmony.Patch(start, postfix: new HarmonyMethod(
                                typeof(SceneHooks).GetMethod("OnResultsSceneStart", BindingFlags.Static | BindingFlags.Public)));
                            Plugin.Logger.LogInfo("[Scene] ResultsScene.Start 钩子已挂（拿结算界面实例）");
                        }
                        else Plugin.Logger.LogWarning("[Scene] ResultsScene 上没有 Start —— 拿不到结算界面实例，考试期间得自己点「退出」");

                        var quit = t.GetMethod("_bM", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (quit != null)
                        {
                            harmony.Patch(quit, prefix: new HarmonyMethod(
                                typeof(SceneHooks).GetMethod("OnResultsQuit", BindingFlags.Static | BindingFlags.Public)));
                            Plugin.Logger.LogInfo("[Scene] ResultsScene._bM 钩子已挂（退出前清 Encounter 信封）");
                        }
                        else Plugin.Logger.LogWarning("[Scene] ResultsScene 上没有 _bM —— 退出可能仍会走故事");

                        var retry = t.GetMethod("_wo", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (retry != null)
                        {
                            harmony.Patch(retry, prefix: new HarmonyMethod(
                                typeof(SceneHooks).GetMethod("OnResultsRetry", BindingFlags.Static | BindingFlags.Public)));
                            Plugin.Logger.LogInfo("[Scene] ResultsScene._wo 钩子已挂（结算重开 → 中止课题）");
                        }
                        else Plugin.Logger.LogWarning("[Scene] ResultsScene 上没有 _wo —— 结算重开不会中止课题");
                    }

                    if (className == "ifapp.Game.Scenes.SongSelectScene")
                    {
                        var upd = t.GetMethod("Update", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (upd == null) Plugin.Logger.LogWarning("[Scene] SongSelectScene 上没有 Update —— 风暴温度计没挂上");
                        else
                        {
                            harmony.Patch(upd, finalizer: new HarmonyMethod(
                                typeof(UpdateWatch).GetMethod("Finalizer", BindingFlags.Static | BindingFlags.Public)));
                            Plugin.Logger.LogInfo("[Scene] 风暴温度计已挂（SongSelectScene.Update 的 finalizer）");
                        }
                    }

                    _handled.Add(className);
                }
                catch (Exception ex)
                {
                    Plugin.Logger.LogWarning($"[Scene] patch {className} 失败: {ex.Message}");
                    _handled.Add(className);
                }
            }

            if (_handled.Count >= 3)
            {
                _patched = true;
                Plugin.Logger.LogInfo($"[Scene] patch 阶段完成，共处理 {_handled.Count} 个类");
                return true;
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[Scene] TryEnsurePatched: {ex}");
        }

        return false;
    }

    public static void Increment(string sceneName)
    {
        lock (_lock)
        {
            if (!_sceneStack.Contains(sceneName))
                _sceneStack.Add(sceneName);

            Plugin.Logger.LogInfo($"[Scene] ▶ {sceneName}");
        }
    }

    public static void Decrement(string sceneName)
    {
        lock (_lock)
        {
            _sceneStack.Remove(sceneName);
            Plugin.Logger.LogInfo($"[Scene] ◀ {sceneName}");
        }
    }

    public static string GetStackSnapshot()
    {
        lock (_lock)
        {
            return string.Join(" > ", _sceneStack);
        }
    }
}