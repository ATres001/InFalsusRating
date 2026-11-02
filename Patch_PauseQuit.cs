using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;

namespace InFalsusRating;

[HarmonyPatch]
public static class Patch_PauseQuit
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    static Type PauseLayerType()
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType("ifapp.Game.Scenes.Game.GamePauseLayer");
            if (t != null) return t;
        }
        return null;
    }

    static IEnumerable<MethodBase> TargetMethods()
    {
        var t = PauseLayerType();
        if (t == null)
        {
            Plugin.Logger.LogWarning("[Exam] 暂停退出钩子：找不到 GamePauseLayer，暂停退出不会中止课题");
            yield break;
        }

        int n = 0;
        foreach (var name in new[] { "_gz", "_Gz", "_hz" })
        {
            var m = t.GetMethod(name, Flags);
            if (m != null) { n++; yield return m; }
        }
        Plugin.Logger.LogInfo(n > 0
            ? $"[Exam] 暂停退出钩子已挂（GamePauseLayer 的 {n} 个按钮回调）"
            : "[Exam] 暂停退出钩子：GamePauseLayer 上没有 _gz/_Gz/_hz，暂停退出不会中止课题");
    }

    static bool Prefix(object __instance, object __0, MethodBase __originalMethod)
    {
        if (__instance == null) return true;

        try
        {
            bool quit = __originalMethod != null && __originalMethod.Name == "_Gz";
            if (!quit && __0 != null)
            {
                object btn = Read(__instance, "quitButton");
                quit = btn != null && Same(btn, __0);
            }
            if (!quit) return true;

            DumpDelegate(__instance, "OnQuitPressed");
            DumpDelegate(__instance, "OnReplayPressed");
            DumpDelegate(__instance, "OnResumeHappening");

            Plugin.Logger.LogWarning("[Exam] 暂停界面点了「退出」—— 还原故事信封、中止课题，改走正常退出");
            GameProbe.RestoreStoryEnvelopeToPrevNow();
            TaskRunner.NotePauseQuit();

            try { UnityEngine.Time.timeScale = 1f; } catch { }
            if (SceneProbe.Game != null) SceneProbe.AbortChart();
            return false;
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[Exam] 暂停退出处理失败：{ex.GetType().Name}: {ex.Message}");
            return true;
        }
    }

    static object Read(object o, string name)
    {
        var t = o.GetType();
        var p = t.GetProperty(name, Flags);
        if (p != null) return p.GetValue(o);
        var f = t.GetField(name, Flags);
        return f != null ? f.GetValue(o) : null;
    }

    static void DumpDelegate(object layer, string name)
    {
        try
        {
            object del = Read(layer, name);
            if (del == null)
            {
                Plugin.Logger.LogInfo($"[Exam] 暂停回调 {name} = <null>");
                return;
            }

            object mi = del.GetType().GetProperty("Method", Flags)?.GetValue(del);
            object mn = mi?.GetType().GetProperty("Name", Flags)?.GetValue(mi);
            object dt = mi?.GetType().GetProperty("DeclaringType", Flags)?.GetValue(mi);
            string dn = dt?.GetType().GetProperty("FullName", Flags)?.GetValue(dt) as string
                        ?? dt?.ToString() ?? "?";
            object tgt = del.GetType().GetProperty("Target", Flags)?.GetValue(del);
            Plugin.Logger.LogInfo($"[Exam] 暂停回调 {name} = {dn}::{mn}  target={(tgt == null ? "<null>" : tgt.GetType().Name)}");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[Exam] 暂停回调 {name} 读不出：{ex.GetType().Name}: {ex.Message}");
        }
    }

    static bool Same(object a, object b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is Il2CppObjectBase ia && b is Il2CppObjectBase ib)
            return ia.Pointer == ib.Pointer;
        return false;
    }
}
