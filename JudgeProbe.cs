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

static class JudgeProbe
{
    public const int N = 7;

        public static readonly long[] Counts = new long[N];

        public static volatile bool Live;

        public static volatile bool Verified;

        public static bool NewDict;

        public static int Entries => _lastEntries;

        public static string Why = "还没试过";

    public static string Source => Live ? (Verified ? "判定（已对账）" : "判定（未对账）") : "dive（兜底）";

    public static string Describe()
        => Live ? $"{Source}：扫到 {_lastEntries} 个音、{_lastJudged} 条判定事件，逐值 {string.Join("/", Counts)}"
                : $"用不了（{Why}）";

        public static void BeginRun()
    {
        Verified = false;
        _said = false;
        _dictObj = null;

        _fails = 0;
        _dead = false;
    }

    const int GAP_MS = 250;
    const int MAX_FAILS = 5;
    static int _at = -100000;
    static int _fails;
    static bool _dead;
    static bool _ignoreDone;
    static bool _doneWorks;
    static int _firstOkAt;
    static bool _said;
    static int _lastEntries, _lastJudged;
    static object _dictObj;

        public static bool Poll(out long[] fresh)
    {
        fresh = null;
        if (_dead) return false;
        int now = Environment.TickCount;
        if (unchecked(now - _at) < GAP_MS) return Live;
        _at = now;
        try
        {
            long[] got = Read();
            if (got == null) return Live;
            _fails = 0;
            Array.Copy(got, Counts, N);
            fresh = Counts;
            if (!Live) { Live = true; _firstOkAt = now; }
            if (!_said)
            {
                _said = true;
                Plugin.Logger.LogInfo($"[Exam/判定] 接上了：{Describe()}");
            }

            if (!_ignoreDone && !_doneWorks && _lastEntries > 0 && unchecked(now - _firstOkAt) > 4000)
            {
                _ignoreDone = true;
                _dictObj = null;
                Plugin.Logger.LogWarning($"[Exam/判定] ⚠ 扫到 {_lastEntries} 个音，没有一条标着「已判定」"
                    + "（_Wdb 恒为 false）→ 那个字段多半不是这意思，改成不看它、所有判定事件都算。");
            }
            return true;
        }
        catch (Exception ex)
        {

            var real = (ex as TargetInvocationException)?.InnerException ?? ex;
            Why = real.GetType().Name + ": " + real.Message;

            if (++_fails >= MAX_FAILS)
            {
                _dead = true;
                Live = false;
                Plugin.Logger.LogWarning($"[Exam/判定] ⚠ 读判定连着挂了 {_fails} 次（{Why}）→ 血量退回按 dive 增量算。");
            }
            else Plugin.Logger.LogWarning($"[Exam/判定] ⚠ 这一拍没读成（{Why}），下一拍再试（{_fails}/{MAX_FAILS}）。");
            return false;
        }
    }

        static long[] Read()
    {
        if (!_resolved) { _resolved = true; Resolve(); }
        NewDict = false;
        if (_t == null) { Why = _whyResolve; return null; }

        object inst = _qe.GetValue(null);
        if (inst == null) { Why = "LogicalNotePlayer 单例还没起来（不在谱面）"; return null; }
        object dict = _ib.Invoke(inst, _noArgs);
        if (dict == null) { Why = "_ib() 给了 null"; return null; }

        object en = _dictEnum.Invoke(dict, _noArgs);
        if (en == null) throw new Exception($"字典的 GetEnumerator() 给了 null（{dict.GetType().FullName}）");
        if (_enMove == null) _enMove = en.GetType().GetMethod("MoveNext", Type.EmptyTypes);
        if (_enCur == null) _enCur = en.GetType().GetProperty("Current");
        if (_enMove == null || _enCur == null)
            throw new Exception($"枚举器上没有 MoveNext / Current（{en.GetType().FullName}）");

        var got = new long[N];
        int entries = 0, judged = 0, spins = 0;
        int wasEntries = _lastEntries;
        while ((bool)_enMove.Invoke(en, _noArgs))
        {

            if (++spins > 100000) throw new Exception("枚举器转了 10 万圈还没停");
            object kv = _enCur.GetValue(en);
            entries++;
            if (kv == null) continue;
            if (_kvpValue == null) _kvpValue = kv.GetType().GetProperty("Value");
            if (_kvpValue == null) throw new Exception("KeyValuePair 上没有 Value 属性");
            object mem = _kvpValue.GetValue(kv);
            if (mem == null) continue;

            var items = _toArray.Invoke(mem, _noArgs) as System.Collections.IEnumerable;
            if (items == null) continue;
            foreach (object e in items)
            {
                if (e == null) continue;
                if (!_ignoreDone && !(bool)_fDone.GetValue(e)) continue;
                int v = Convert.ToInt32(_fJudge.GetValue(e));
                if (v >= 0 && v < N) { got[v]++; judged++; }
            }
        }
        _lastEntries = entries;
        _lastJudged = judged;

        if (_dictCount != null)
        {
            int cnt = Convert.ToInt32(_dictCount.Invoke(dict, _noArgs));
            if (cnt != entries) throw new Exception($"字典说有 {cnt} 个音，枚举器只扫到 {entries} 个");
        }

        if (!_ignoreDone && judged > 0) _doneWorks = true;

        NewDict = !ReferenceEquals(dict, _dictObj) || (entries == 0 && wasEntries > 0);
        _dictObj = dict;
        Why = "";
        return got;
    }

    static bool _resolved;
    static Type _t;
    static PropertyInfo _qe, _kvpValue;
    static MethodInfo _ib, _toArray;
    static MethodInfo _dictEnum;
    static MethodInfo _dictCount;
    static MethodInfo _enMove;
    static PropertyInfo _enCur;
    static FieldInfo _fJudge, _fDone;
    static string _whyResolve = "";
    static readonly object[] _noArgs = Array.Empty<object>();

    static void Resolve()
    {
        try
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = null;
                try { t = asm.GetType("ifapp.Game.LogicalNotePlayer", false); } catch { }
                if (t != null) { _t = t; break; }
            }
            if (_t == null) _whyResolve = "找不到类型 ifapp.Game.LogicalNotePlayer（游戏更新过？）";
            else
            {
                _qe = _t.GetProperty("_Qe", BindingFlags.Public | BindingFlags.Static);
                _ib = _t.GetMethod("_ib", BindingFlags.Public | BindingFlags.Instance);
                if (_qe == null || _ib == null) { _whyResolve = "_Qe / _ib 这两个名字不在了（游戏更新过？）"; _t = null; }
                else
                {

                    foreach (var m in _ib.ReturnType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (m.Name != "GetEnumerator" || m.GetParameters().Length != 0) continue;
                        if (_dictEnum == null || (!m.ReturnType.IsInterface && _dictEnum.ReturnType.IsInterface))
                            _dictEnum = m;
                    }
                    Type mem = _ib.ReturnType.GetGenericArguments()[1];
                    _toArray = mem.GetMethod("ToArray");
                    _dictCount = _ib.ReturnType.GetMethod("get_Count", Type.EmptyTypes);
                    Type el = mem.GetGenericArguments()[0];
                    _fJudge = el.GetField("_wdb");
                    _fDone = el.GetField("_Wdb");
                    if (_dictEnum == null || _toArray == null || _fJudge == null || _fDone == null)
                    {
                        _whyResolve = "字典的 GetEnumerator / Memory.ToArray / _cH 的 _wdb·_Wdb 对不上了（游戏更新过？）";
                        _t = null;
                    }
                }
            }
        }
        catch (Exception ex) { _whyResolve = ex.GetType().Name + ": " + ex.Message; _t = null; }
        if (_t == null) Plugin.Logger.LogWarning($"[Exam/判定] ⚠ 实时判定用不了：{_whyResolve} → 血量退回按 dive 增量算。");
    }
}