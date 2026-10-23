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

public static class SceneProbe
{
    public static object Scene;
    public static object Game;

    public static object Results;

    class WatchSpec
    {
        public string Label;
        public object Host;
        public PropertyInfo[] Props;
        public string Last;
        public double MinInterval;
        public double NextLogAt;
    }

    static readonly List<WatchSpec> _watches = new List<WatchSpec>();
    static bool _built;

    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    static object Walk(object root, params string[] path)
    {
        object cur = root;
        foreach (var name in path)
        {
            if (cur == null) { Log($"  {string.Join(".", path)}: 中途为 null"); return null; }
            var p = cur.GetType().GetProperty(name, Flags);
            if (p == null) { Log($"  {string.Join(".", path)}: {cur.GetType().Name} 上没有 {name}"); return null; }
            try { cur = p.GetValue(cur); }
            catch (Exception ex) { Log($"  {string.Join(".", path)}: 读 {name} 抛 {ex.GetType().Name}"); return null; }
        }
        return cur;
    }

    static readonly HashSet<string> _said = new HashSet<string>();

    static void LogOnce(string s)
    {
        if (_said.Add(s)) Log(s);
    }

    static bool Watch(string label, object host, params string[] names)
    {
        if (host == null) { LogOnce($"{label}: 宿主为 null（容器可能还没 Awake，稍后重试）"); return false; }
        var t = host.GetType();
        var props = names.Select(n => t.GetProperty(n, Flags)).Where(p => p != null).ToArray();

        var missing = names.Where(n => t.GetProperty(n, Flags) == null).ToArray();
        if (props.Length == 0) { LogOnce($"{label}: {t.Name} 上一个成员都没找到（缺: {string.Join(",", missing)}）"); return false; }
        if (_said.Add("built:" + label))
        {
            Log($"{label} → {t.FullName}");
            Log($"  盯: {string.Join("  ", props.Select(p => p.Name + ":" + p.PropertyType.Name))}");
            if (missing.Length > 0) Log($"  未找到: {string.Join(", ", missing)}");
        }
        _watches.Add(new WatchSpec
        {
            Label = label, Host = host, Props = props,

            MinInterval = label == "scene" ? 0.5 : 0.0,
        });
        return true;
    }

    static bool _allOk;
    static DateTime _builtAt;

    static bool Build()
    {
        _watches.Clear();
        bool ok = true;

        ok &= Watch("dive", Walk(Game, "clearStateContainer"),
            "_ioA", "_IoA", "_joA", "_HoA", "_GoA", "_hoA");

        ok &= Watch("scene", Game,
            "_mN", "_nN", "_IN", "_a", "_lN", "_LN",
            "_GN", "_hN", "_HN", "_iN", "_JN", "_kN", "_KN");

        return ok;
    }

    static double Secs() => System.Diagnostics.Stopwatch.GetTimestamp()
                          / (double)System.Diagnostics.Stopwatch.Frequency;

        static readonly bool Verbose = false;

    static void Poll(WatchSpec w)
    {

        if (w.Label == "dive")
        {
            foreach (var p in w.Props)
                if (p.Name == "_ioA")
                {
                    try { Dive = Convert.ToInt32(p.GetValue(w.Host)); } catch { }
                    break;
                }
            if (!Verbose) return;
        }
        else if (!Verbose) return;

        string sig;
        try
        {
            sig = string.Join("  ", w.Props.Select(p =>
            {
                string v;
                try { v = Show(p.GetValue(w.Host)); } catch { v = "?"; }
                return p.Name + "=" + v;
            }));
        }
        catch (Exception ex) { Plugin.Logger.LogError($"[Probe] {w.Label} 读取异常: {ex}"); return; }
        if (sig != w.Last)
        {
            w.Last = sig;
            if (w.MinInterval > 0)
            {
                double now = Secs();
                if (now < w.NextLogAt) return;
                w.NextLogAt = now + w.MinInterval;
            }
            Log($"{w.Label}  帧{Frame()}  {sig}");
        }
    }

    public static int Dive = 9999;

    public static int Combo = -1;

    public static int ClearStateA = -1;
    public static int ClearStateB = -1;
    public static int ClearStateC = -1;

    static PropertyInfo _comboProp, _cscProp, _goaProp, _hoaProp, _HoaProp, _comboBackProp, _goActiveProp;
    static MethodInfo _comboText;
    static string _comboWhy, _cscWhy;
    static int _lastIndA = -2, _lastIndB = -2, _lastIndC = -2;

        static void ResetRuntime()
    {
        Combo = -1;
        ClearStateA = ClearStateB = ClearStateC = -1;
    }

    static void SampleRuntime()
    {

        try
        {
            if (_comboProp == null) _comboProp = Game.GetType().GetProperty("comboText", Flags);
            object ft = _comboProp != null ? _comboProp.GetValue(Game) : null;
            if (ft == null) Combo = -1;
            else
            {
                if (_comboText == null)
                    _comboText = ft.GetType().GetMethod("GetText",
                        BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);

                string raw = _comboText != null ? (_comboText.Invoke(ft, null) as string ?? "") : "";

                Combo = int.TryParse(raw.Trim(), out int c) ? c : -1;

                if (_comboBackProp == null) _comboBackProp = Game.GetType().GetProperty("comboTextBackingObject", Flags);
                object back = _comboBackProp != null ? _comboBackProp.GetValue(Game) : null;
                if (back != null)
                {
                    if (_goActiveProp == null) _goActiveProp = back.GetType().GetProperty("activeInHierarchy", Flags);
                    if (_goActiveProp != null && !Convert.ToBoolean(_goActiveProp.GetValue(back))) Combo = -1;
                }
            }
        }
        catch (Exception ex)
        {
            Combo = -1;
            if (_comboWhy == null)
            {
                _comboWhy = ex.GetType().Name + ": " + ex.Message;
                Log($"连击读不到（{_comboWhy}）—— 血条缺口那个 FL 色块这一局会一直亮着");
            }
        }

        try
        {
            if (_cscProp == null) _cscProp = Game.GetType().GetProperty("clearStateContainer", Flags);
            object csc = _cscProp != null ? _cscProp.GetValue(Game) : null;
            if (csc == null) { ClearStateA = ClearStateB = ClearStateC = -1; }
            else
            {
                var t = csc.GetType();
                if (_goaProp == null) _goaProp = t.GetProperty("_GoA", Flags);
                if (_hoaProp == null) _hoaProp = t.GetProperty("_hoA", Flags);
                if (_HoaProp == null) _HoaProp = t.GetProperty("_HoA", Flags);
                ClearStateA = _goaProp != null ? Convert.ToInt32(_goaProp.GetValue(csc)) : -1;
                ClearStateB = _hoaProp != null ? Convert.ToInt32(_hoaProp.GetValue(csc)) : -1;

                ClearStateC = _HoaProp != null ? (Convert.ToBoolean(_HoaProp.GetValue(csc)) ? 1 : 0) : -1;
            }
        }
        catch (Exception ex)
        {
            ClearStateA = ClearStateB = ClearStateC = -1;
            if (_cscWhy == null)
            {
                _cscWhy = ex.GetType().Name + ": " + ex.Message;
                Log($"指示器状态读不到（{_cscWhy}）");
            }
        }

        if (ClearStateA != _lastIndA || ClearStateB != _lastIndB || ClearStateC != _lastIndC)
        {
            _lastIndA = ClearStateA;
            _lastIndB = ClearStateB;
            _lastIndC = ClearStateC;
            Log($"指示器 帧{Frame()}  _GoA={ClearStateA}  _hoA={ClearStateB}  _HoA={ClearStateC}  连击={Combo}");
        }
    }

    public static int FrameNo;

    static void RefreshFrame()
    {
        try { FrameNo = UnityEngine.Time.frameCount; } catch { }
    }

    public static string Frame() => FrameNo.ToString();

    public static int QuitSeq;
    public static int GameSeq;

        public static volatile bool OurAbort;

    public static void AbortChart()
    {
        if (Game == null) { Log("中断：当前不在谱面里"); return; }
        var m = Game.GetType()
                    .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(x => x.Name == "_Sk" && x.GetParameters().Length == 0);
        if (m == null) { Log("中断：GameScene 上没有无参的 _Sk"); return; }
        Log($"中断：调 GameScene._Sk()（当前 dive={Dive}）");
        OurAbort = true;
        try { m.Invoke(Game, null); }
        catch (Exception ex) { Log($"中断失败: {ex.GetType().Name}: {ex.Message}"); }
        finally { OurAbort = false; }
    }

    public static volatile bool _panicPending;

    public static void PanicRequest() => _panicPending = true;

    public static void PanicRelease()
    {
        Log("强收：开始");

        try
        {
            float ts = UnityEngine.Time.timeScale;
            if (ts != 1f) { UnityEngine.Time.timeScale = 1f; Log($"强收：Time.timeScale {ts} → 1"); }
            else Log("强收：Time.timeScale 本来就是 1（不是它冻的）");
        }
        catch (Exception ex) { Log($"强收：动 timeScale 抛 {Why(ex)}"); }

        Log("强收：完成");
    }

    static string Why(Exception ex)
    {
        var sb = new System.Text.StringBuilder();
        var e = ex;
        for (int depth = 0; e != null && depth < 5; depth++)
        {
            if (depth > 0) sb.Append("  ←  ");
            sb.Append(e.GetType().Name).Append(": ").Append(e.Message);
            e = e.InnerException;
        }
        return sb.ToString();
    }

    public static void Tick()
    {
        RefreshFrame();

        if (_panicPending) { _panicPending = false; PanicRelease(); }

        if (Game == null)
        {
            if (_watches.Count > 0) { _watches.Clear(); _said.Clear(); }
            _built = false; _allOk = false;
            Dive = 9999;
            Combo = -1;
            ClearStateA = ClearStateB = ClearStateC = -1;
            _lastIndA = _lastIndB = _lastIndC = -2;
            return;
        }

        if (ExamHp.Active) SampleRuntime();
        else ResetRuntime();
        try
        {

            if (!_built || (!_allOk && (DateTime.Now - _builtAt).TotalSeconds > 1.0))
            {
                _built = true;
                _builtAt = DateTime.Now;
                _allOk = Build();
            }
            foreach (var w in _watches) Poll(w);
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[Probe] 读取异常: {ex}");
            _watches.Clear();
            _built = true;
            _allOk = true;
        }
    }

    public static Type Find(string full)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType(full);
            if (t != null) return t;
        }

        string shortName = full.Substring(full.LastIndexOf('.') + 1);
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] ts;
            try { ts = asm.GetTypes(); } catch { continue; }
            foreach (var t in ts)
                if (t.Name == shortName) return t;
        }
        return null;
    }

    static void Log(string s) => Plugin.Logger.LogInfo($"[Probe] {s}");

    static string Show(object v)
    {
        if (v == null) return "null";
        try { return v.ToString(); } catch { return $"<{v.GetType().Name} ToString 抛异常>"; }
    }

    static object Prop(object o, string name)
    {
        if (o == null) { Log($"  .{name} → (宿主为 null)"); return null; }
        var p = o.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (p == null) { Log($"  .{name} → 没有这个属性"); return null; }
        try { return p.GetValue(o); }
        catch (Exception ex) { Log($"  .{name} → 读失败 {ex.GetType().Name}: {ex.Message}"); return null; }
    }

    static object StaticProp(Type t, string name)
    {
        if (t == null) return null;
        var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        if (p == null) { Log($"  {t.Name}.{name} → 没有这个静态属性"); return null; }
        try { return p.GetValue(null); }
        catch (Exception ex) { Log($"  {t.Name}.{name} → 读失败 {ex.GetType().Name}"); return null; }
    }

    static object KeyVal(string keyName)
    {
        Type keyT = Find("UnityEngine.InputSystem.Key");
        if (keyT == null) { Log("  找不到 Key 枚举"); return null; }
        try { return Enum.Parse(keyT, keyName); } catch { Log($"  Key 里没有 {keyName}"); return null; }
    }

    static object Call(object target, string method, Func<ParameterInfo[], object[]> build)
    {
        if (target == null) { Log($"  {method}: 宿主 null"); return null; }
        var m = target.GetType().GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (m == null) { Log($"  {method}: 找不到这个方法"); return null; }
        object[] args;
        try { args = build(m.GetParameters()); }
        catch (Exception ex) { Log($"  {method}: 拼参数失败 {ex.GetType().Name}: {ex.Message}"); return null; }
        try { return m.Invoke(target, args); }
        catch (Exception ex) { var i = ex.InnerException ?? ex; Log($"  {method}: 抛 {i.GetType().Name}: {i.Message}"); return null; }
    }

    static object CallKey(object target, string keyName)
    {
        if (target == null) { Log("  宿主 null"); return null; }
        var m = target.GetType().GetMethod("_OvA", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (m == null) { Log("  找不到 _OvA"); return null; }
        var ps = m.GetParameters();
        if (ps.Length != 3) { Log($"  _OvA 参数个数是 {ps.Length}，不是预期的 3"); return null; }
        object mods = null;
        try { mods = Activator.CreateInstance(ps[2].ParameterType.GetElementType()); }
        catch (Exception ex) { Log($"  造 mods 失败 {ex.GetType().Name}"); return null; }
        try { return m.Invoke(target, new object[] { KeyVal(keyName), 0.0, mods }); }
        catch (Exception ex) { var i = ex.InnerException ?? ex; Log($"  _OvA 抛 {i.GetType().Name}: {i.Message}"); return null; }
    }
}