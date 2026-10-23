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

public static partial class GameProbe
{
        public static volatile bool Requested;

        public static volatile bool ClearLogRequested;

        public static volatile int SceneCmd;

        public static volatile int MvCmd;

    public const int MvStateOff = 0;
    public const int MvStateStarting = 1;
    public const int MvStatePlaying = 2;
    public const int MvStateEnded = 3;

    public static volatile int MvState;

        public static volatile int MvPick;
        public const int MvPickUnknown = 0;
        public const int MvPickHas = 1;
        public const int MvPickNone = 2;

        public static volatile int MvFrames;
        public static volatile float MvSeconds;

        public static volatile bool MvAnyKeySkip;

    const BindingFlags F  = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    const BindingFlags FS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

    static readonly Dictionary<string, Type> _tc = new Dictionary<string, Type>();
    static readonly StringBuilder _o = new StringBuilder();

        static Type T(string full)
    {
        if (_tc.TryGetValue(full, out var hit)) return hit;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type t = null;
            try { t = asm.GetType(full, false); } catch { }
            if (t != null) { _tc[full] = t; return t; }
        }
        return null;
    }

    static object P(object o, string n)
    {
        if (o == null) return null;
        try { return o.GetType().GetProperty(n, F)?.GetValue(o); } catch { return null; }
    }

        static string PX(object o, string n)
    {
        if (o == null) return "<宿主是 null>";
        try
        {
            var p = o.GetType().GetProperty(n, F);
            if (p == null) return "<没有这个属性>";
            object v = p.GetValue(o);
            return v == null ? "<null>" : S(v);
        }
        catch (Exception e)
        {
            var ex = (e is TargetInvocationException && e.InnerException != null) ? e.InnerException : e;
            return "<抛 " + ex.GetType().Name + ": " + ex.Message + ">";
        }
    }

    static object PF(object o, string n)
    {
        if (o == null) return null;
        try { return o.GetType().GetField(n, F)?.GetValue(o); } catch { return null; }
    }

    static int Len(object o)
    {
        if (o == null) return -1;
        var t = o.GetType();
        try
        {
            var p = t.GetProperty("Length", F) ?? t.GetProperty("Count", F);
            if (p != null) return Convert.ToInt32(p.GetValue(o));
            var f = t.GetField("Length", F) ?? t.GetField("Count", F);
            if (f != null) return Convert.ToInt32(f.GetValue(o));
        }
        catch { }

        try
        {
            var m = t.GetMethod("get_Count", F);
            if (m != null) return Convert.ToInt32(m.Invoke(o, null));
        }
        catch { }
        return -1;
    }

    static object At(object o, int i)
    {
        if (o == null || i < 0) return null;
        try
        {
            var t = o.GetType();
            var p = t.GetProperty("Item", F) ?? t.GetProperty("get_Item", F);
            if (p != null) return p.GetValue(o, new object[] { i });
        }
        catch { }
        return null;
    }

        static MethodInfo M(object target, string name, int argc)
    {
        if (target == null) return null;
        foreach (var m in target.GetType().GetMethods(F))
        {
            if (m.Name != name) continue;
            if (m.GetParameters().Length != argc) continue;
            return m;
        }
        return null;
    }

    static void W(string line)
    {
        _o.Append(line).Append('\n');
        Plugin.Logger.LogInfo("[Probe] " + line);
    }

    static string S(object o)
    {
        if (o == null) return "<null>";
        try { return o.ToString(); } catch { return "<ToString 抛了>"; }
    }

        static string Ref(object assetRef)
    {
        if (assetRef == null) return "<null>";
        string guid = S(P(assetRef, "AssetGUID"));
        string key = null;
        try { key = S(P(assetRef, "RuntimeKey")); } catch { }
        string done = null;
        try { done = S(P(assetRef, "IsDone")); } catch { }
        return $"guid={guid} key={key} done={done}";
    }

        public static void Tick()
    {

        if (ClearLogRequested)
        {
            ClearLogRequested = false;
            try { ClearProbeLog(); }
            catch (Exception e) { W("⚠ F3 抛 " + e.GetType().Name + ": " + e.Message); Flush(); }
        }

        int scmd = SceneCmd;
        if (scmd != 0)
        {
            SceneCmd = 0;
            if (scmd == 4)
            {
                try { FeedBgOnly(); }
                catch (Exception e) { W("⚠ 喂背景抛 " + EM(e)); Flush(); }
            }
        }

        try { DnHoldTick(); } catch (Exception e) { W("⚠ 保压抛 " + EM(e)); Flush(); }

        try { HpLockTick(); } catch (Exception e) { W("⚠ 锁血抛 " + EM(e)); Flush(); }

        try { MvTick(); } catch (Exception e) { W("⚠ 自播 MV 抛 " + EM(e)); Flush(); }

        try { UpdateWatchReport(); } catch (Exception e) { W("⚠ 风暴温度计抛 " + EM(e)); Flush(); }

        try { MusicSwapTick(); } catch (Exception e) { W("⚠ 音乐替换抛 " + EM(e)); Flush(); }

        if (Requested)
        {
            Requested = false;
            Overview();
        }
    }

    static void Overview()
    {

        _o.Append("\n\n=========== GameProbe ").Append(DateTime.Now.ToString("HH:mm:ss")).Append(" ===========\n");

        object da = null;
        try { da = S0_FindDataAccess(); } catch (Exception e) { W("S0 抛 " + e.GetType().Name + ": " + e.Message); }

        if (da != null)
        {
            try { S1_Backgrounds(da); } catch (Exception e) { W("① 背景 抛 " + e.GetType().Name + ": " + e.Message); }
            try { S2_Videos(da);     } catch (Exception e) { W("② 视频 抛 " + e.GetType().Name + ": " + e.Message); }
            try { S4_Charts(da);     } catch (Exception e) { W("④ 谱面 抛 " + e.GetType().Name + ": " + e.Message); }
        }
        try { S5_Live(); } catch (Exception e) { W("① ② 现场 抛 " + e.GetType().Name + ": " + e.Message); }

        try { S6_SlideCurve(); } catch (Exception e) { W("⑤ 滑条插值 抛 " + e.GetType().Name + ": " + e.Message); }

        W("=========== 总览结束 ===========");
        Flush();
    }

    static object S0_FindDataAccess()
    {
        W("--- S0 DataAccess ---");

        W("场景: Scene=" + Kind(SceneProbe.Scene) + "  Game=" + Kind(SceneProbe.Game) + "  Results=" + Kind(SceneProbe.Results)
          + "（SceneProbe 只跟这三个；全 <null> 时看下面 S5 的 [C]）");

        object da = P(SceneProbe.Scene, "dataAccess");
        if (da != null) { W("Scene.dataAccess → " + da.GetType().FullName); return da; }

        da = P(SceneProbe.Game, "dataAccess");
        if (da != null) { W("Game.dataAccess → " + da.GetType().FullName); return da; }

        da = P(SceneProbe.Results, "dataAccess");
        if (da != null) { W("Results.dataAccess → " + da.GetType().FullName); return da; }

        W("⚠ 三个场景都读不到 dataAccess（不在选歌/谱面/结算里？）");
        return null;
    }

        static string Kind(object o)
    {
        if (o == null) return "<null>";
        try
        {
            if (o is Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase ob)
            {
                IntPtr klass = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(
                    Il2CppInterop.Runtime.IL2CPP.Il2CppObjectBaseToPtr(ob));
                if (klass != IntPtr.Zero)
                {
                    string nm = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_name_(klass);
                    if (!string.IsNullOrEmpty(nm)) return nm;
                }
            }
        }
        catch { }
        try { return o.GetType().Name; } catch { return "?"; }
    }

    static void S1_Backgrounds(object da)
    {
        W("");
        W("--- ① 谱面特殊背景动画引用 ---");

        object gbs = P(da, "GameplayBackgrounds");
        if (gbs == null) { W("⚠ dataAccess.GameplayBackgrounds 读不到"); return; }
        W("GameplayBackgrounds 拿到: " + gbs.GetType().FullName);

        object list = P(gbs, "gameplayBackgrounds");
        int n = Len(list);
        W($"gameplayBackgrounds 表长 {n}");

        var guidOf = new Dictionary<string, string>();
        var emptyOnes = new List<string>();

        var um = M(gbs, "_UmA", 1);
        Type enumT = um?.GetParameters()[0].ParameterType;
        if (enumT == null) { W("⚠ 找不到 _UmA(GameplayBackground)，枚举类型拿不到"); }
        else
        {
            var vals = Enum.GetValues(enumT);
            W($"枚举 {enumT.FullName} 共 {vals.Length} 个值；逐值解析 _UmA → AssetReference：");
            foreach (var v in vals)
            {
                object aref = null;
                string err = null;
                try { aref = um.Invoke(gbs, new[] { v }); }
                catch (Exception e) { err = (e.InnerException ?? e).GetType().Name; }

                string name = v.ToString();
                string guid = err != null ? null : S(P(aref, "AssetGUID"));
                guidOf[name] = guid ?? "";
                if (err != null || string.IsNullOrEmpty(guid)) emptyOnes.Add(name);

                W($"  [{Convert.ToInt32(v),2}] {name,-24} → " + (err != null ? "抛 " + err
                    : (string.IsNullOrEmpty(guid) ? "**guid 空** ← 没有普通背景素材" : "guid=" + guid)));
            }
            W("");
            W($"⚠ guid 为空的枚举值共 {emptyOnes.Count} 个：{string.Join(" / ", emptyOnes)}");
            W("  这批是「没有普通背景」的 —— 特殊曲目的背景动画走 *_SpecialSceneAssets 预制，");
            W("  由 GameScene.specialSceneAssetsRef 载入（见 S5 现场那段打的 AssetReference）。");
        }

        W("");
        W("每首歌的 GameplayBackground（曲子 → 背景枚举；★=该枚举没有普通素材）：");
        foreach (var song in Songs(da))
        {
            string id   = S(P(song, "Id"));
            string nm   = S(P(song, "BaseName"));
            string bgen = S(P(song, "GameplayBackground"));
            string guid;
            bool star = guidOf.TryGetValue(bgen, out guid) && string.IsNullOrEmpty(guid);
            W($"  id={id,-4} {(star ? "★" : " ")}{bgen,-24} {nm}");
        }
    }

    static void S2_Videos(object da)
    {
        W("");
        W("--- ② 特殊曲目视频引用 ---");

        object vf = P(da, "VideoFiles");
        if (vf == null) { W("⚠ dataAccess.VideoFiles 读不到"); return; }
        W("VideoFiles 拿到: " + vf.GetType().FullName);

        object entries = P(vf, "entries");
        int n = Len(entries);
        W($"entries 条数 {n}（FilePair: AssetName / Asset / AudioAsset）");
        for (int i = 0; i < n; i++)
        {
            object fp = At(entries, i);
            if (fp == null) { W($"  [{i}] <null>"); continue; }
            W($"  [{i}] AssetName={S(P(fp, "AssetName"))}");
            W($"       Asset     → {Ref(P(fp, "Asset"))}");
            W($"       AudioAsset= {S(P(fp, "AudioAsset"))}");
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < n; i++)
        {
            string a = S(P(At(entries, i), "AssetName"));
            if (!string.IsNullOrEmpty(a) && a != "<null>") names.Add(a);
        }

        W("");
        W("有专属视频（<曲名>_bga.mp4）的曲子：");
        int hit = 0;
        foreach (var song in Songs(da))
        {
            string bn = S(P(song, "BaseName"));
            if (string.IsNullOrEmpty(bn) || bn == "<null>") continue;
            string want = bn + "_bga.mp4";
            if (!names.Contains(want)) continue;
            hit++;
            object fp = null;
            for (int i = 0; i < n; i++) { if (string.Equals(S(P(At(entries, i), "AssetName")), want, StringComparison.OrdinalIgnoreCase)) { fp = At(entries, i); break; } }
            W($"  ★ {bn,-22} → {want}  {Ref(P(fp, "Asset"))}");
        }
        W($"  共 {hit} 首有专属视频（全曲库 {n} 条视频素材）。");
        W("");
        W("其余按用途命名、和曲子无关的那些（dive / reveal / 剧情 ShroudSky 那批）：");
        foreach (var a in names)
            if (a.IndexOf("_bga.mp4", StringComparison.OrdinalIgnoreCase) < 0)
                W("    " + a);
    }

    static void S4_Charts(object da)
    {
        W("");
        W("--- ④ 游戏内谱面更改（全曲库的谱面表）---");
        W("每行：曲 id | 曲名 | 谱面 Id（这是引用一张谱的关键字） / 难度 / 定数");

        int songs = 0, charts = 0;
        foreach (var song in Songs(da))
        {
            songs++;
            string id = S(P(song, "Id"));
            string nm = S(P(song, "BaseName"));
            W($"  id={id,-4} {nm}");

            object ci = P(song, "ChartInfos");
            int cn = Len(ci);
            for (int j = 0; j < cn; j++)
            {
                object c = At(ci, j);
                if (c == null) continue;
                charts++;
                W($"       [{j}] Id={S(P(c, "Id")),-28} 难度={S(P(c, "Difficulty")),-10} " +
                  $"定数={S(P(c, "Rating")),-4} 可用={S(P(c, "Available"))}");
            }
        }
        W($"共 {songs} 首 / {charts} 张谱。");
    }

    static void S6_SlideCurve()
    {
        W("");
        W("--- ⑤ 空域滑条插值（`_b._lA._Yb`）v4 ---");

        Type tLA = T("_b._lA");
        if (tLA == null) { W("    ⛔ 类型 `_b._lA` 不在（这段不作数）"); return; }
        MethodInfo yb = MvPickM(tLA, "_Yb", 2, false);
        if (yb == null) { W("    ⛔ `_b._lA._Yb` 没找到（这段不作数）"); return; }
        var ps = yb.GetParameters();
        W("    `_Yb` = " + yb.ReturnType.Name + " _Yb(" +
          string.Join(", ", ps.Select(x => x.ParameterType.Name + " " + x.Name)) + ")  静态=" + yb.IsStatic);

        object n = SlideProbeNote(out string how);
        if (n == null) { W("    ⛔ 模板音符造不出来 —— " + how + "（这段不作数）"); return; }
        W("    模板：" + how);

        W("    F. 全字段清点 —— 模板 = 谱面第 0 个音，**一个字段都没写**：");
        W("       " + SlideFields(n));
        W("       [交叉核对·PF 路] _Be=" + S(PF(n, "_Be")) + " _ce=" + S(PF(n, "_ce"))
          + " _Ee=" + S(PF(n, "_Ee")) + " _fe=" + S(PF(n, "_fe"))
          + " _Fe=" + S(PF(n, "_Fe")) + " _he=" + S(PF(n, "_he")));
        Flush();

        SlideReadControl(yb, n);
        Flush();

        int bad = SlideProbeGeom(n, 0.25, 0.50, 0.75, 0.10);
        W("    B. 写进去：起点 x=0.25 w=0.50 → 终点 x=0.75 w=0.10，时间 0→1000ms，失败格数=" + bad);
        W("       读回来：_Ce=" + SlideBox(n, "_Ce") + "  _de=" + SlideBox(n, "_de")
          + "  _De=" + SlideBox(n, "_De") + "  _ee=" + SlideBox(n, "_ee"));
        W("              _Be=" + S(PF(n, "_Be")) + "  _ce=" + S(PF(n, "_ce")) + "  _be=" + S(PF(n, "_be"))
          + "  _Ae=" + S(PF(n, "_Ae")) + "  _he=" + S(PF(n, "_he")));
        W("       写完后再点一遍全字段 —— 重点看 **`_Ee/_fe` 跟不跟 `_Be/_ce` 走**：");
        W("       " + SlideFields(n));

        SlideWindowProbe(yb, n);

        SlideRawScan(yb);

        W("    D. 静止框（起=终，t=500）—— 钉输出单位：");
        foreach (double[] c in new[] { new[] { 0.25, 0.5 }, new[] { 0.75, 0.5 }, new[] { 0.25, 1.0 },
                                       new[] { 0.25, 0.1 }, new[] { 0.5, 0.5 }, new[] { 1.0, 1.0 } })
        {
            object s = SlideProbeCopy(n);
            if (s == null) break;
            SlideProbeGeom(s, c[0], c[1], c[0], c[1]);
            W("       (x=" + c[0].ToString("0.####", InvCult) + ", w=" + c[1].ToString("0.####", InvCult)
              + ") → " + SlideProbeSweep(yb, s, 500, 500, 1));
        }

        {
            object s = SlideProbeCopy(n);
            if (s != null)
            {
                SlideProbeGeom(s, 0.25, 0.50, 0.75, 0.10);
                W("    E. t 量级 → " + SlideProbeSweep(yb, s, 0, 0, 1, "0.####",
                    new[] { -1000000.0, -1000.0, -1.0, 0, 1, 500, 1000, 1001, 1000000.0 }));
            }
        }

        {
            object s = SlideProbeCopy(n);
            if (s != null)
            {
                SlideProbeGeom(s, 0.25, 0.50, 0.75, 0.10);
                W("    T. t=0..1000 → " + SlideProbeSweep(yb, s, 0, 1000, 11));
            }
        }

        foreach (int he in new[] { 36, 40, 48, 68, 72, 80, 132, 136, 144 })
        {
            object s = SlideProbeCopy(n);
            if (s == null) { W("    G2. ⛔ 拷贝失败（这段不作数）"); break; }
            SlideProbeGeom(s, 0.25, 0.50, 0.75, 0.10);
            SetPath(s, "_he", he.ToString(InvCult));
            bool w0 = SetPath(s, "_Ee", "0"), w1 = SetPath(s, "_fe", "1000");
            W("    G2. `_he`=" + he.ToString(InvCult).PadRight(4) + " 窗口写(0,1000)=" + (w0 && w1)
              + "  t=0..1000/17点 → " + SlideProbeSweep(yb, s, 0, 1000, 17, "G9"));
        }

        SlideRealStyles(yb);

        {
            Type tKA = T("_b._kA"), tFA = T("_b._FA");
            MethodInfo xb = MvPickM(tKA, "_xb", 1, false), Xb = MvPickM(tKA, "_Xb", 1, false);
            if (tKA == null || tFA == null || xb == null || Xb == null)
                W("    H. ⛔ `_b._kA._xb/_Xb` 没找齐（xb=" + (xb != null) + " Xb=" + (Xb != null) + "）");
            else
            {
                W("    H. `_he` → 两个风格（`_xb` / `_Xb` 返回 `_b._JA`：0=_EE 1=_fE 2=_FE）");
                foreach (int he in new[] { 36, 40, 48, 68, 72, 80, 132, 136, 144 })
                {
                    object e = Enum.ToObject(tFA, he);
                    W("        `_he`=" + he.ToString(InvCult).PadRight(4) + " → _xb=" + SlideInvoke(xb, e).PadRight(4) + " _Xb=" + SlideInvoke(Xb, e));
                }
            }
        }

        {
            object s = SlideProbeCopy(n);
            if (s != null)
            {
                SlideProbeGeom(s, 0.25, 0.50, 0.75, 0.10);
                SetPath(s, "_he", "72");
                string a = SlideProbeSweep(yb, s, 0, 1000, 3), b = SlideProbeSweep(yb, s, 0, 1000, 3);
                W("    I. [正对照②] `_he`=72 连叫两次：" + (a == b ? "逐位一样 ✓" : "⛔ 不一样：a=" + a + "  b=" + b));
            }
        }

        Flush();
        try { SlideZbProbe(); }
        catch (Exception e) { W("    Z. 抛 " + e.GetType().Name + "：" + e.Message); }
    }

        static void SlideRealStyles(MethodInfo yb)
    {
        W("    K. 真谱面里每个 `_he` 挑 2 个真音符（一个字段都不改）—— 三列 = 三种 arg 约定");
        object notes = null;
        try
        {
            object lnp = NotePlayer();
            notes = P(lnp == null ? null : CurrentTuple(lnp), "Item2");
        }
        catch (Exception e) { W("        ⛔ 取音符表抛 " + e.GetType().Name + "：" + e.Message); return; }
        int nn = Len(notes);
        if (nn <= 0) { W("        ⛔ 音符表拿不到（Len=" + nn + "）—— 这段不作数"); return; }

        var want = new[] { 36, 40, 48, 68, 72, 80, 132, 136, 144 };
        var picks = new Dictionary<int, List<object>>();
        for (int i = 0; i < nn; i++)
        {
            object s = At(notes, i);
            if (s == null) continue;
            try
            {
                if (Convert.ToInt32(PF(s, "_be"), InvCult) != 5) continue;
                if (Convert.ToInt32(PF(s, "_ce") ?? 0, InvCult) <= Convert.ToInt32(PF(s, "_Be") ?? 0, InvCult))
                    continue;
                int he = Convert.ToInt32(PF(s, "_he"), InvCult);
                if (Array.IndexOf(want, he) < 0) continue;
                List<object> l;
                if (!picks.TryGetValue(he, out l)) { l = new List<object>(); picks[he] = l; }
                if (l.Count < 2) l.Add(s);
            }
            catch { }
        }

        foreach (int he in want)
        {
            List<object> l;
            if (!picks.TryGetValue(he, out l)) { W("        `_he`=" + he.ToString(InvCult) + "：这首里没有这种（跳）"); continue; }
            for (int k = 0; k < l.Count; k++)
            {
                object s = l[k];
                W("        `_he`=" + he.ToString(InvCult).PadRight(4) + " #" + (k + 1) + "：");
                W("           " + SlideFields(s));
                W("           框：起 x=" + S(GetPath(s, "_Ce._LD")) + " w=" + S(GetPath(s, "_De._LD"))
                  + " → 终 x=" + S(GetPath(s, "_de._LD")) + " w=" + S(GetPath(s, "_ee._LD")));
                SlideConventions(yb, s, k == 0);
            }
        }
    }

        static void SlideConventions(MethodInfo yb, object s, bool fine)
    {
        int b0, c0;
        try { b0 = Convert.ToInt32(PF(s, "_Be"), InvCult); c0 = Convert.ToInt32(PF(s, "_ce"), InvCult); }
        catch { W("           ⛔ `_Be/_ce` 读不出（这枚跳）"); return; }
        double? e0 = RawF64(s, "_Ee"), e1 = RawF64(s, "_fe");
        bool winSame = e0.HasValue && e1.HasValue && e1.Value != e0.Value
                       && Math.Abs(e0.Value - b0) < 0.5 && Math.Abs(e1.Value - c0) < 0.5;

        W("           ① arg 原样（0..1）：   " + SlideProbeSweep(yb, s, 0, 1, 5, "G9"));
        W("           ② 按 `_Be.._ce`(" + b0 + ".." + c0 + ")： " + SlideProbeSweep(yb, s, b0, c0, 5, "G9"));
        if (e0.HasValue && e1.HasValue && e1.Value != e0.Value)
            W("           ③ 按 `_Ee.._fe`(" + SlideFmtD(e0) + ".." + SlideFmtD(e1) + ")： "
              + SlideProbeSweep(yb, s, e0.Value, e1.Value, 5, "G9") + (winSame ? "   ⇒ 窗口 = `_Be/_ce`" : ""));
        else
            W("           ③ 按 `_Ee.._fe`：       ⛔读不到（`_Ee`=" + SlideFmtD(e0) + " `_fe`=" + SlideFmtD(e1) + "）");
        if (fine)
        {
            W("           ② 17点： " + SlideProbeSweep(yb, s, b0, c0, 17, "G9"));
            if (e0.HasValue && e1.HasValue && e1.Value != e0.Value && !winSame)
                W("           ③ 17点： " + SlideProbeSweep(yb, s, e0.Value, e1.Value, 17, "G9"));
        }
    }

        static void SlideRawScan(MethodInfo yb)
    {
        try
        {
            object lnp = NotePlayer();
            object notes = P(lnp == null ? null : CurrentTuple(lnp), "Item2");
            int nn = Len(notes);
            object s = null;
            for (int i = 0; i < nn && s == null; i++)
            {
                object c = At(notes, i);
                if (c == null) continue;
                if (Convert.ToInt32(PF(c, "_be") ?? 0, InvCult) != 5) continue;
                if (Convert.ToInt32(PF(c, "_ce") ?? 0, InvCult) <= Convert.ToInt32(PF(c, "_Be") ?? 0, InvCult)) continue;
                s = c;
            }
            if (s == null) { W("    A. 这首里没有非零长的空域滑条（这段不作数）"); return; }
            int b0 = Convert.ToInt32(PF(s, "_Be"), InvCult), c0 = Convert.ToInt32(PF(s, "_ce"), InvCult);
            W("    A. [正对照①] 真音符（一个字段都不改）：`_he`=" + S(PF(s, "_he")) + " `_Be`=" + b0 + " `_ce`=" + c0
              + "  起点框 x=" + S(GetPath(s, "_Ce._LD")) + " w=" + S(GetPath(s, "_De._LD"))
              + " → 终点框 x=" + S(GetPath(s, "_de._LD")) + " w=" + S(GetPath(s, "_ee._LD")));
            W("       t=`_Be` → " + SlideProbeSweep(yb, s, b0, b0, 1)
              + "   t=中点 → " + SlideProbeSweep(yb, s, (b0 + c0) / 2.0, (b0 + c0) / 2.0, 1)
              + "   t=`_ce` → " + SlideProbeSweep(yb, s, c0, c0, 1));
        }
        catch (Exception e) { W("    A. 抛 " + e.GetType().Name + "：" + e.Message); }
    }

        static void SlideRawBox(object r)
    {
        IntPtr p = SlidePtr(r);
        if (p == IntPtr.Zero) { W("       （拿不到裸指针，裸内存这段不做数）"); return; }
        var sb = new StringBuilder();
        for (int off = 0; off <= 28; off += 4)
        {
            string v;
            try { v = BitConverter.ToSingle(BitConverter.GetBytes(Marshal.ReadInt32(p, off)), 0).ToString("G9", InvCult); }
            catch (Exception e) { v = "⛔" + e.GetType().Name; }
            sb.Append(" +").Append(off).Append('=').Append(v);
        }
        W("       裸内存：" + sb);
    }

        static bool RawSlot(object o, string field, out IntPtr ptr, out int off)
    {
        ptr = IntPtr.Zero; off = -1;
        try
        {
            IntPtr p = Ptr(o);
            if (p == IntPtr.Zero) return false;
            IntPtr klass = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(p);
            if (klass == IntPtr.Zero) return false;
            IntPtr fld = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_field_from_name(klass, field);
            if (fld == IntPtr.Zero) return false;
            ptr = p;
            off = (int)Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_offset(fld);
            return true;
        }
        catch { return false; }
    }

        static float? RawF32(object o, string field)
    {
        IntPtr p; int off;
        if (!RawSlot(o, field, out p, out off)) return null;
        try { return BitConverter.ToSingle(BitConverter.GetBytes(Marshal.ReadInt32(p, off)), 0); }
        catch { return null; }
    }

        static int? RawI32(object o, string field)
    {
        IntPtr p; int off;
        if (!RawSlot(o, field, out p, out off)) return null;
        try { return Marshal.ReadInt32(p, off); }
        catch { return null; }
    }

        static long? RawI64(object o, string field)
    {
        IntPtr p; int off;
        if (!RawSlot(o, field, out p, out off)) return null;
        try { return Marshal.ReadInt64(p, off); }
        catch { return null; }
    }

        static double? RawF64(object o, string field)
    {
        IntPtr p; int off;
        if (!RawSlot(o, field, out p, out off)) return null;
        try { return BitConverter.Int64BitsToDouble(Marshal.ReadInt64(p, off)); }
        catch { return null; }
    }

    static string SlideFmtI(int? v) { return v.HasValue ? v.Value.ToString(InvCult) : "·"; }
    static string SlideFmtL(long? v) { return v.HasValue ? v.Value.ToString(InvCult) : "·"; }
    static string SlideFmtD(double? v) { return v.HasValue ? v.Value.ToString("G9", InvCult) : "·"; }

        static string HeNames(int? he)
    {
        if (!he.HasValue) return "";
        int v = he.Value;
        string L = (v & 4) != 0 ? "EE" : (v & 8) != 0 ? "fE" : (v & 16) != 0 ? "FE" : "?";
        string R = (v & 32) != 0 ? "EE" : (v & 64) != 0 ? "fE" : (v & 128) != 0 ? "FE" : "?";
        return " (解" + L + "," + R + ")";
    }

        static string SlideFields(object n)
    {
        var sb = new StringBuilder();
        sb.Append("_ZD=").Append(SlideFmtL(RawI64(n, "_ZD")));
        sb.Append(" _ae=").Append(SlideFmtL(RawI64(n, "_ae")));
        sb.Append(" _Ae=").Append(SlideFmtI(RawI32(n, "_Ae")));
        sb.Append(" _be=").Append(SlideFmtI(RawI32(n, "_be")));
        sb.Append(" _Be=").Append(SlideFmtI(RawI32(n, "_Be")));
        sb.Append(" _ce=").Append(SlideFmtI(RawI32(n, "_ce")));
        int? he = RawI32(n, "_he");
        sb.Append(" _he=").Append(SlideFmtI(he)).Append(HeNames(he));
        sb.Append(" _He=").Append(SlideFmtI(RawI32(n, "_He")));
        sb.Append(" _ie=").Append(SlideFmtI(RawI32(n, "_ie")));
        sb.Append(" _Ge=").Append(SlideFmtI(RawI32(n, "_Ge")));
        sb.Append(" _ge=").Append(SlideFmtL(RawI64(n, "_ge")));
        sb.Append(" _Ee=").Append(SlideFmtD(RawF64(n, "_Ee")));
        sb.Append(" _fe=").Append(SlideFmtD(RawF64(n, "_fe")));
        sb.Append(" _Fe=").Append(SlideFmtD(RawF64(n, "_Fe")));
        sb.Append(" 框 _Ce=").Append(SlideBox(n, "_Ce")).Append(" _de=").Append(SlideBox(n, "_de"))
          .Append(" _De=").Append(SlideBox(n, "_De")).Append(" _ee=").Append(SlideBox(n, "_ee"));
        return sb.ToString();
    }

        static void SlideWindowProbe(MethodInfo yb, object n)
    {
        object s = SlideProbeCopy(n);
        if (s == null) { W("    U. ⛔ 拷贝失败（这段不作数）"); return; }
        SlideProbeGeom(s, 0.25, 0.50, 0.75, 0.10);
        W("    U. 窗口覆盖实验（几何固定 0.25/w0.50 → 0.75/w0.10，`_Be/_ce`=0/1000）：");
        W("       ⓪ 窗口原样 `_Ee`=" + SlideFmtD(RawF64(s, "_Ee")) + " `_fe`=" + SlideFmtD(RawF64(s, "_fe"))
          + "：t=0..1000 → " + SlideProbeSweep(yb, s, 0, 1000, 5, "G9"));
        bool o1 = SetPath(s, "_Ee", "0"), o2 = SetPath(s, "_fe", "1000");
        W("       ① 写 `_Ee`=0 `_fe`=1000（写入=" + (o1 && o2 ? "成功" : "⛔失败 Ee=" + o1 + " fe=" + o2)
          + "，回读 " + SlideFmtD(RawF64(s, "_Ee")) + "/" + SlideFmtD(RawF64(s, "_fe")) + "）：");
        if (o1 && o2)
        {
            W("          t=0..1000 → " + SlideProbeSweep(yb, s, 0, 1000, 5, "G9"));
            W("          t=0..1    → " + SlideProbeSweep(yb, s, 0, 1, 5, "G9"));
        }
        bool o3 = SetPath(s, "_Ee", "1000"), o4 = SetPath(s, "_fe", "0");
        if (o3 && o4)
            W("       ② 窗口反写 `_Ee`=1000 `_fe`=0：t=0..1000 → " + SlideProbeSweep(yb, s, 0, 1000, 5, "G9"));
    }

        static void SlideReadControl(MethodInfo yb, object note)
    {
        string err;
        object r = SlideCall(yb, note, 500f, out err);
        if (r == null) { W("    R. ⛔ 调用失败：" + err); return; }

        string cls = "?", sz = "?";
        try
        {
            IntPtr k = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(Ptr(r));
            cls = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_namespace_(k) + "."
                + Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_name_(k);
            sz = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_instance_size(k).ToString(InvCult);
        }
        catch (Exception e) { cls = "⛔" + e.GetType().Name; }

        string interopOff = "?";
        try
        {
            var fi = r.GetType().GetField("NativeFieldInfoPtr_Item1",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (fi == null) interopOff = "（连这个静态字段都没有）";
            else
            {
                IntPtr h = (IntPtr)fi.GetValue(null);
                interopOff = h == IntPtr.Zero ? "句柄=0"
                    : Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_offset(h).ToString(InvCult);
            }
        }
        catch (Exception e) { interopOff = "⛔" + e.GetType().Name; }

        string byClass = "?";
        try
        {
            IntPtr k = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(Ptr(r));
            IntPtr f1 = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_field_from_name(k, "Item1");
            IntPtr f2 = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_field_from_name(k, "Item2");
            byClass = f1 == IntPtr.Zero ? "`Item1` 字段没找到"
                : "Item1@" + Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_offset(f1).ToString(InvCult)
                  + "  Item2@" + (f2 == IntPtr.Zero ? "?" : Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_offset(f2).ToString(InvCult));
        }
        catch (Exception e) { byClass = "⛔" + e.GetType().Name; }

        W("    R. 读法正对照 —— 同一个返回对象（B 段那份几何，t=500）：");
        W("       原生类 = " + cls + "   实例大小 = " + sz + " 字节（装箱头 16 ⇒ 两个 Single 只可能在 +16/+20）");
        W("       ① 托管属性 `P(r,\"Item1\")` = " + FmtNum(P(r, "Item1") ?? PF(r, "Item1"), "G9")
          + " / " + FmtNum(P(r, "Item2") ?? PF(r, "Item2"), "G9") + "   ← v1/v2 全在用这条");
        W("       ② interop 自己那个句柄 → `il2cpp_field_get_offset` = " + interopOff + "   ← 指开泛型 ⇒ 0");
        W("       ③ 按**实例类**解析 = " + byClass);
        W("       ④ ③ 那条读出来的值 = " + FmtNum(RawF32(r, "Item1"), "G9") + " / " + FmtNum(RawF32(r, "Item2"), "G9"));
        SlideRawBox(r);
        W("       ⇒ 下面所有扫描一律用 ④。");
    }

        static object SlideCall(MethodInfo yb, object note, float t, out string err)
    {
        err = "";
        try { return yb.Invoke(null, new object[] { note, t }); }
        catch (Exception e)
        {
            var x = (e is TargetInvocationException && e.InnerException != null) ? e.InnerException : e;
            err = x.GetType().Name + "：" + x.Message;
            return null;
        }
    }

        static void SlideZbProbe()
    {
        Type tLA = T("_b._lA");
        if (tLA == null) { W("    Z. ⛔ 没有 `_b._lA`"); return; }
        FieldInfo hf = null;
        foreach (var f in tLA.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            if (f.Name.StartsWith("NativeMethodInfoPtr__Zb_", StringComparison.Ordinal)) { hf = f; break; }
        if (hf == null) { W("    Z. ⛔ 找不到 `_Zb` 的方法句柄字段"); return; }
        IntPtr mi = (IntPtr)hf.GetValue(null);
        W("    Z. `_Zb` 方法句柄 = 0x" + mi.ToInt64().ToString("X") + "（" + hf.Name + "）");
        if (mi == IntPtr.Zero) { W("       ⛔ 句柄是 0（方法没解析出来），不调"); return; }

        IntPtr args = Marshal.AllocHGlobal(20 * IntPtr.Size);
        IntPtr vals = Marshal.AllocHGlobal(20 * 4);
        try
        {
            W("       基线（20 槽全 0）→ out12..19 =" + ZbRun(mi, args, vals, -1, 0f));
            for (int i = 0; i < 12; i++)
                W("       入[" + i.ToString(InvCult).PadLeft(2) + "]=1，其余 0 → out12..19 =" + ZbRun(mi, args, vals, i, 1f));
            W("       入[10]=2 → out12..19 =" + ZbRun(mi, args, vals, 10, 2f));
            W("       入[11]=2 → out12..19 =" + ZbRun(mi, args, vals, 11, 2f));

            ZbSetup(args, vals);
            for (int i = 0; i < 10; i++)
                Marshal.WriteInt32(vals, i * 4, BitConverter.ToInt32(BitConverter.GetBytes(0.1f * (i + 1)), 0));
            W("       入[0..9]=0.1..1.0 → out12..19 =" + ZbCall(mi, args, vals));
        }
        finally { Marshal.FreeHGlobal(args); Marshal.FreeHGlobal(vals); }
    }

        static void ZbSetup(IntPtr args, IntPtr vals)
    {
        for (int i = 0; i < 20; i++)
        {
            Marshal.WriteInt32(vals, i * 4, 0);
            Marshal.WriteIntPtr(args, i * IntPtr.Size, new IntPtr(vals.ToInt64() + i * 4));
        }
    }

        static string ZbRun(IntPtr mi, IntPtr args, IntPtr vals, int oneHot, float v)
    {
        ZbSetup(args, vals);
        if (oneHot >= 0 && oneHot < 12)
            Marshal.WriteInt32(vals, oneHot * 4, BitConverter.ToInt32(BitConverter.GetBytes(v), 0));
        return ZbCall(mi, args, vals);
    }

        static string ZbCall(IntPtr mi, IntPtr args, IntPtr vals)
    {
        IntPtr exc = IntPtr.Zero;
        try { unsafe { Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(mi, IntPtr.Zero, (void**)args.ToPointer(), ref exc); } }
        catch (Exception e) { return "⛔托管抛 " + e.GetType().Name + "：" + e.Message; }
        if (exc != IntPtr.Zero)
        {
            try { Il2CppInterop.Runtime.Il2CppException.RaiseExceptionIfNecessary(exc); }
            catch (Exception e) { return "⛔原生抛 " + e.GetType().Name + "：" + e.Message; }
        }
        var sb = new StringBuilder();
        for (int i = 12; i < 20; i++)
            sb.Append(' ').Append(BitConverter.ToSingle(BitConverter.GetBytes(Marshal.ReadInt32(vals, i * 4)), 0).ToString("0.####", InvCult));
        return sb.ToString();
    }

        static IntPtr SlidePtr(object o)
    {
        foreach (var name in new[] { "Pointer", "NativePointer", "handle" })
        {
            object v = P(o, name) ?? PF(o, name);
            if (v is IntPtr ip) return ip;
            if (v is long l) return new IntPtr(l);
            if (v is ulong u) return new IntPtr(unchecked((long)u));
        }
        return IntPtr.Zero;
    }

        static string SlideInvoke(MethodInfo mi, object arg)
    {
        try
        {
            object r = mi.Invoke(null, new object[] { arg });
            if (r == null) return "null";
            string nm = "";
            try { nm = r.ToString(); } catch { }
            return Convert.ToInt64(r, InvCult).ToString(InvCult) + (nm != null && !nm.StartsWith("_b.", StringComparison.Ordinal) ? "(" + nm + ")" : "");
        }
        catch (Exception e)
        {
            var x = (e is TargetInvocationException && e.InnerException != null) ? e.InnerException : e;
            return "⛔" + x.GetType().Name;
        }
    }

        static string SlideBox(object n, string field)
    {
        return S(GetPath(n, field + "._KD")) + "|" + S(GetPath(n, field + "._lD")) + "|" + S(GetPath(n, field + "._LD"));
    }

        static int SlideProbeGeom(object s, double x0, double w0, double x1, double w1)
    {
        int bad = 0;
        if (!SetPath(s, "_Ae", "4")) bad++;
        if (!SetPath(s, "_be", "5")) bad++;
        if (!SetPath(s, "_He", "-1")) bad++;
        if (!SetPath(s, "_ie", "-1")) bad++;
        if (!SetPath(s, "_Be", "0")) bad++;
        if (!SetPath(s, "_ce", "1000")) bad++;
        bad += SlideSetBox(s, "_Ce", x0) + SlideSetBox(s, "_de", x1)
             + SlideSetBox(s, "_De", w0) + SlideSetBox(s, "_ee", w1);
        return bad;
    }

        static int SlideSetBox(object n, string field, double v)
    {
        const int den = 1000;
        int bad = 0;
        if (!SetPath(n, field + "._KD", ((int)Math.Round(v * den)).ToString(InvCult))) bad++;
        if (!SetPath(n, field + "._lD", den.ToString(InvCult))) bad++;
        if (!SetPath(n, field + "._LD", v.ToString("G9", InvCult))) bad++;
        return bad;
    }

        static object SlideProbeNote(out string how)
    {
        how = "";
        try
        {
            object lnp = NotePlayer();
            object tup = lnp == null ? null : CurrentTuple(lnp);
            object notes = P(tup, "Item2");
            int nn = Len(notes);
            if (nn > 0)
            {
                object s = null;
                for (int i = 0; i < nn && s == null; i++) s = At(notes, i);
                if (s != null)
                {
                    how = "真谱面第 0 个音（" + s.GetType().FullName + "，值类型=" + s.GetType().IsValueType
                        + "）；后面只动装箱副本、不回写";
                    return s;
                }
            }
            how = "拿不到谱面（tuple/notes 空）→ 新造一个";
        }
        catch (Exception e)
        {
            how = "取真音符抛 " + e.GetType().Name + "：" + e.Message + " → 新造一个";
        }
        try
        {
            Type tf = T("_b._fA");
            if (tf == null) { how += "；⛔ 但 `_b._fA` 类型也不在"; return null; }
            object z = Activator.CreateInstance(tf);
            how += "（新造 " + tf.FullName + "，值类型=" + tf.IsValueType + "）";
            return z;
        }
        catch (Exception e2) { how += "；⛔ 新造也抛 " + e2.GetType().Name + "：" + e2.Message; return null; }
    }

        static object SlideProbeCopy(object n)
    {
        try
        {
            if (!n.GetType().IsValueType) return null;
            var mc = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
            object c = mc == null ? null : mc.Invoke(n, null);
            if (c != null) return c;
        }
        catch { }

        try { return n.GetType().IsValueType ? Activator.CreateInstance(n.GetType()) : null; }
        catch { return null; }
    }

        static string SlideProbeSweep(MethodInfo yb, object note, double t0, double t1, int n, string fmt = "0.####", double[] ts = null)
    {
        var sb = new StringBuilder();
        int cnt = ts != null ? ts.Length : n;
        for (int i = 0; i < cnt; i++)
        {
            double t = ts != null ? ts[i] : (n <= 1 ? t0 : t0 + (t1 - t0) * i / (n - 1));
            string err;
            object r = SlideCall(yb, note, (float)t, out err);
            if (r == null) return "⛔ 抛 " + err;
            sb.Append(' ').Append(t.ToString(fmt, InvCult)).Append("→(")
              .Append(FmtNum(RawF32(r, "Item1"), fmt)).Append(',').Append(FmtNum(RawF32(r, "Item2"), fmt)).Append(')');
        }
        return sb.ToString();
    }

    static string FmtNum(object v, string fmt)
    {
        if (v == null) return "·";
        try
        {
            if (v is float f) return f.ToString(fmt, InvCult);
            if (v is double d) return d.ToString(fmt, InvCult);
            return S(v);
        }
        catch { return "?"; }
    }

    static IEnumerable<object> Songs(object da)
    {
        object sd = P(da, "SongData");
        if (sd == null) { W("⚠ dataAccess.SongData 读不到"); yield break; }
        object all = P(sd, "allSongInfo");
        int n = Len(all);
        if (n <= 0) { W("⚠ SongData.allSongInfo 空（len=" + n + "）"); yield break; }
        for (int i = 0; i < n; i++)
        {
            object s = At(all, i);
            if (s != null) yield return s;
        }
    }

    static void S5_Live()
    {
        W("");
        W("--- ① ② 现场：故事过场场景 + GameScene 上挂上了什么 ---");

        DumpStoryScene("ifapp.Game.Scenes.StorySongTransitionScene",
            new[] { "videoPlayer", "bossIntroBgaPlayer" });
        DumpStoryScene("ifapp.Game.Scenes.StoryScene",
            new[] { "c2dVideoPlayer", "introC2dVideoPlayer" });

        string inv = WatchPlayers();
        W("  [D] 全场播放器盘点：");
        W(inv ?? "      <一个类型都没加载>");

        W("  [E] 场景清单：");
        W(DumpScenes());

        object g = SceneProbe.Game;
        if (g == null)
        {
            W("  GameScene 不在 —— [A]/[B] 那两段要**在谱面里**按 F5 才有东西看（上面的 [C]/[D] 不受影响）。");
            return;
        }

        object bg = P(g, "backgroundObject");
        W("  [A] backgroundObject = " + Kind(bg) + " 「" + ObjName(bg) + "」");
        DumpRenderers(bg);

        object ssar = P(g, "specialSceneAssetsRef");
        W("  [A2] specialSceneAssetsRef = " + Kind(ssar) + "   托管类=" + FN(ssar));
        if (ssar != null)
        {
            Type[] ga = ssar.GetType().GetGenericArguments();
            W("        泛型实参 = " + (ga.Length == 0 ? "<不是泛型>"
                : string.Join(" / ", Array.ConvertAll(ga, x => x == null ? "?" : x.FullName))));
        }
        W("        " + Ref(ssar));
        object ssa = P(ssar, "Asset");
        W("        Asset = " + Kind(ssa) + " 「" + ObjName(ssa) + "」   托管类=" + FN(ssa));
        W("        ⇒ " + (ssa != null ? "素材**已经在内存里**了（游戏确实把它装进来了）"
                                     : "⚠ Asset 是空的 ⇒ **只有引用、素材没装进来**"));

        object c2d = P(g, "c2dSceneContainer");
        W("  [B] c2dSceneContainer = " + Kind(c2d) + " 「" + ObjName(c2d) + "」");
        DumpChildTree(c2d, 2);

        var vps = FindAll(T("ifapp.Game.UI.C2DVideoPlayer"));
        W($"  [B] 场景里 C2DVideoPlayer 共 {vps.Count} 个");
        for (int i = 0; i < vps.Count && i < 4; i++) DumpVideoPlayer(vps[i]);

        var siv = FindAll(T("ifapp.Game.Scenes.StoryVideoIntroPlayer"));
        W($"  [B] 场景里 StoryVideoIntroPlayer 共 {siv.Count} 个");
        if (siv.Count > 0)
        {
            object pairs = P(siv[0], "videoFilePairs");
            W("        videoFilePairs 条数 = " + Len(pairs) + "（每条 = { VideoClipAss, TextKey }）");
            for (int i = 0; i < Len(pairs) && i < 8; i++)
            {
                object p = At(pairs, i);
                W($"          [{i}] TextKey=「{S(P(p, "TextKey"))}」  " + Ref(P(p, "VideoClipAss")));
            }
        }

        W("  [F] GameScene 的 34 个匿名格子（找抓着 `_E.` 那族 feature 的）：");
        int fTotal = 0, fLive = 0, fErr = 0;
        foreach (var nm in GameScenePrivateFields)
        {
            string which, why;
            object v = RVA(g, new[] { nm }, out which, out why);
            fTotal++;
            if (which == null)
            {
                fErr++;
                W("        " + nm + " ⛔ 没读到：" + why);
                continue;
            }
            if (v == null) continue;
            fLive++;
            string ns = null;
            try { ns = v.GetType().Namespace; } catch { }
            int ln = Len(v);
            W("        " + nm + " = " + Kind(v) + "   托管类=" + FN(v)
              + (ns == "_E" ? "   ★★**就是特殊背景那一族**" : "")
              + (ln >= 0 ? "   长度=" + ln : ""));

            if (ns == "_E" || (ln > 0 && ln <= 64)) DumpAnonMembers(v, "            ");
        }
        W($"        —— 共问 {fTotal} 格：非空 {fLive} 个、读不到 {fErr} 个");
        if (fErr > 0) W("        ⚠ 有格子读不到 ⇒ **这一段的结论不作数**，先把上面 `why` 那几行看清");

        W("  [G] 特殊背景 feature 的实况（`_E._sD` 那一族）：");
        {
            string gW, gY;
            object vd = RVA(g, new[] { "_DN" }, out gW, out gY);
            W("        GameScene._DN = " + Kind(vd) + "   托管类=" + FN(vd) + Wh2(gW, gY));

            try
            {
                Type tr = T("ifapp.Game.Scenes.StorySongTransitionScene");
                W("        " + (tr == null ? "⛔ 过场场景类型没加载 ⇒ 信封读不到" : Statics(tr)));
            }
            catch (Exception e) { W("        ⛔ 读静态信封抛 " + EM(e)); }

            try
            {
                Type tr2 = T("ifapp.Game.Scenes.StorySongTransitionScene");
                if (tr2 == null) W("        ⛔ 过场场景类型没加载 —— `_rT` 摊不开");
                else
                {
                    string rw, ry;
                    object rt = RVA(tr2, ST_CTX, out rw, out ry);
                    NgDump(rt, "        `_rT`（过场场景的静态信封）" + Wh2(rw, ry) + "\n        　");
                }
            }
            catch (Exception e) { W("        ⛔ 摊开 `_rT` 抛 " + EM(e)); }

            try
            {
                string dW, dY;
                object dn = RVA(g, new[] { "_dN" }, out dW, out dY);
                W("        GameScene._dN = " + NgVerdict(dn) + Wh2(dW, dY)
                  + "   ← ★ GameScene 自己的**静态**信封（和过场场景的 `_rT` 不是一个槽）");

                W("        " + SdPick(dn));

                NgDump(dn, "        `_dN`（GameScene 自己的静态信封）\n        　");
            }
            catch (Exception e) { W("        ⛔ 读 GameScene._dN 抛 " + EM(e)); }

            if (vd == null)
            {
                W("        ⛔ `_DN` 读不到 ⇒ **这一段不作数**（先看上面 `why`）");
            }
            else
            {
                string pW, pY;
                W("        _VD 身份 = " + Fmt(RVA(vd, new[] { "Pointer" }, out pW, out pY)) + Wh2(pW, pY)
                  + "   ← 换首歌再按一次，变了 ⇒ `_VD` 每首重建");

                string sW, sY;
                object ss = RVA(vd, new[] { "_wEA" }, out sW, out sY);
                W("        _VD._wEA（Cecil 里唯一的 `_sD` 格）= " + Kind(ss) + "   托管类=" + FN(ss) + Wh2(sW, sY));
                W("             " + SdWhich(ss));

                try
                {
                    var mi = vd.GetType().GetMethod("_Xz", F | BindingFlags.Static);
                    if (mi == null) W("        _Xz() ⛔ 找不到这个名字");
                    else if (mi.GetParameters().Length != 0)
                        W("        _Xz() ⚠ 有 " + mi.GetParameters().Length + " 个参数，不是无参 getter，跳过");
                    else
                    {
                        object sx = mi.Invoke(mi.IsStatic ? null : vd, null);
                        W("        _Xz() → " + Kind(sx) + "   托管类=" + FN(sx));
                        W("             " + SdWhich(sx));
                    }
                }
                catch (Exception e) { W("        _Xz() ⛔ 抛 " + EM(e)); }
            }
        }
    }

        static readonly string[] SdRoster =
    {
        "_E._sD",
        "_E._Be", "_E._de", "_E._Fe", "_E._ke", "_E._me", "_E._Ne", "_E._pe", "_E._Qe", "_E._se",
        "_E._Te", "_E._ve", "_E._Xe", "_E._ze", "_E._bE", "_E._CE",
        "_E._he", "_E._He", "_E._ie", "_E._je", "_E._xe",
    };

        static string SdWhich(object o)
    {
        if (o == null) return "<null> ⇒ **这一格是空的**";
        var hit = new List<string>();
        int missing = 0;
        foreach (var full in SdRoster)
        {
            Type t = T(full);
            if (t == null) { missing++; continue; }
            bool yes = false;
            try { yes = t.IsInstanceOfType(o); } catch { }
            if (yes) hit.Add(full);
        }
        if (hit.Count == 0)
            return "⚠ **不是 `_sD` 那一族**（原生类名 " + Kind(o)
                 + (missing > 0 ? "；另有 " + missing + " 个类型没加载" : "") + "）";
        return "命中 " + hit.Count + " 级：" + string.Join(" → ", hit.ToArray())
             + (missing > 0 ? "（另 " + missing + " 个类型没加载）" : "");
    }

        static readonly string[] GameScenePrivateFields =
    {
        "_Xn", "_yn", "_Yn", "_zn", "_Zn", "_aN", "_AN", "_bN", "_BN", "_cN", "_CN",
        "_dN", "_DN", "_eN", "_EN", "_fN", "_FN", "_gN", "_GN", "_hN", "_HN", "_iN",
        "_IN", "_jN", "_JN", "_kN", "_KN", "_lN", "_LN", "_mN", "_MN", "_nN", "_jo", "_NN",
    };

        static void DumpAnonMembers(object o, string pad)
    {
        if (o == null) return;
        if (o is string) return;
        Type t = null;
        try { t = o.GetType(); } catch { }
        if (t == null) { W(pad + "⛔ GetType 抛了"); return; }

        int ln = Len(o);
        if (ln > 0)
        {
            int n = Math.Min(ln, 8);
            W(pad + "集合，共 " + ln + " 个，前 " + n + " 个：");
            for (int i = 0; i < n; i++)
            {
                object el = At(o, i);
                W(pad + "  [" + i + "] " + Kind(el) + "   托管类=" + FN(el));
                if (el != null) DumpAnonMembers(el, pad + "      ");
            }
            if (ln > n) W(pad + "  …（其余略）");
            return;
        }

        int shown = 0;
        bool sawName = false;
        try
        {
            foreach (var pr in t.GetProperties(F))
            {
                if (!pr.Name.StartsWith("_")) continue;
                if (pr.GetIndexParameters().Length != 0) continue;
                sawName = true;
                if (shown >= 24) { W(pad + "…（还有更多，截断）"); break; }
                object v = null;
                try { v = pr.GetValue(o); }
                catch (Exception e) { W(pad + pr.Name + " ⛔ 取值抛 " + (e.InnerException ?? e).GetType().Name); shown++; continue; }
                if (v == null) continue;
                shown++;
                W(pad + pr.Name + " = " + Kind(v) + "   托管类=" + FN(v));
            }
        }
        catch (Exception e) { W(pad + "⛔ 枚举成员抛 " + EM(e)); }
        if (shown == 0)
            W(pad + (sawName ? "（`_` 成员全是空的）" : "（没有 `_` 开头的实例成员 —— 这一坨不是匿名数据类）"));
    }

        static void DumpStoryScene(string typeName, string[] members)
    {
        Type t = T(typeName);
        if (t == null) { W($"  [C] {typeName}：<类型没加载 —— 现在不在这个故事场景里>"); return; }
        var found = FindAll(t);
        W($"  [C] {typeName}：{found.Count} 个实例");
        for (int i = 0; i < found.Count && i < 2; i++)
        {
            object sc = found[i];
            W($"      [{i}] 「{ObjName(sc)}」");
            foreach (var nm in members)
            {
                object v = P(sc, nm);
                W($"          {nm} = {Kind(v)} 「{ObjName(v)}」");
                if (v == null) continue;
                DumpVideoPlayer(v);

                object mat = P(v, "targetMaterial");
                if (mat != null) W("            targetMaterial = 「" + ObjName(mat) + "」");
            }
        }
    }

        static int _findPref = -1;

        static string WatchPlayers()
    {
        var all = new StringBuilder();
        var types = new[]
        {
            "ifapp.Game.UI.C2DVideoPlayer",
            "ifapp.Game.UI.GenericMeshVideoPlayer",
            "ifapp.Game.Scenes.StoryVideoIntroPlayer",
        };
        foreach (var tn in types)
        {
            Type t = T(tn);
            if (t == null)
            {
                all.Append("\n  ").Append(tn).Append("：<类型没加载>");
                continue;
            }
            var found = FindAll(t);
            var blocks = new List<string>();
            int live = 0;
            for (int i = 0; i < found.Count; i++)
            {
                object v = found[i];
                string w1, w2;
                object uvp = RV(v, "videoPlayer", out w1);
                object pairs = RV(v, "videoFilePairs", out w2);
                int pn = Len(pairs);
                if (uvp != null || pn > 0) live++;

                var sb = new StringBuilder();
                sb.Append("    「").Append(ObjName(v)).Append("」 路径=").Append(PathOf(v));

                sb.Append("\n        wrapper=").Append(FN(v)).Append("   原生类=").Append(Kind(v));
                sb.Append("\n        videoPlayer = ").Append(Fmt(uvp)).Append(Wh(w1));
                if (uvp != null)
                    sb.Append("\n            clip=").Append(Fmt(P(uvp, "clip")))
                      .Append("  url=「").Append(S(P(uvp, "url"))).Append('」')
                      .Append("  playing=").Append(S(P(uvp, "isPlaying")))
                      .Append("  prepared=").Append(S(P(uvp, "isPrepared")))
                      .Append("  renderMode=").Append(S(P(uvp, "renderMode")));
                sb.Append("\n        videoFilePairs = ").Append(pn).Append(Wh(w2));
                if (pn > 0)
                {
                    object p0 = At(pairs, 0);
                    sb.Append("   第一条 = ").Append(Ref(P(p0, "VideoClipAss")))
                      .Append("（TextKey=「").Append(S(P(p0, "TextKey"))).Append("」）");
                }

                if (uvp == null && pn <= 0)
                    sb.Append("\n        ↓ 按名字读全空，那它到底有什么：").Append(DumpMembers(v));
                blocks.Add(sb.ToString());
            }
            all.Append("\n  ").Append(tn).Append(" ×").Append(found.Count)
               .Append(" 实例，其中「有播放器或有表」 ").Append(live).Append(" 台");

            if (blocks.Count > 1) blocks.Sort(StringComparer.Ordinal);
            if (blocks.Count > 0) all.Append('\n').Append(string.Join("\n", blocks.ToArray()));
        }

        all.Append(DumpUnityVideoPlayers());

        return all.Length == 0 ? null : all.ToString();
    }

        static string CompNames(object go)
    {
        if (go == null) return "<没有 gameObject>";
        Type ct = T("UnityEngine.Component");
        if (ct == null) return "<UnityEngine.Component 没加载>";
        var m = MT(go, "GetComponents", typeof(Type));
        if (m == null) return "<没有 GetComponents(Type)>";
        object a0 = Fit(ct, m.GetParameters()[0].ParameterType);
        if (a0 == null) return "<Type 参数转换失败>";
        object arr;
        try { arr = m.Invoke(go, new object[] { a0 }); }
        catch (Exception e) { return "<GetComponents 抛 " + (e.InnerException ?? e).GetType().Name + ">"; }
        int n = Len(arr);
        var sb = new StringBuilder();
        for (int i = 0; i < n && i < 16; i++) sb.Append(Kind(At(arr, i))).Append(' ');
        return n + " 个: " + sb;
    }

        static string DumpUnityVideoPlayers()
    {
        string tn = "UnityEngine.Video.VideoPlayer";
        Type t = T(tn);
        if (t == null) return "\n  " + tn + "：<类型没加载>";
        var found = FindAll(t);
        var blocks = new List<string>();
        foreach (var v in found)
            blocks.Add("    「" + ObjName(v) + "」 路径=" + PathOf(v)
                + "\n        clip=" + Fmt(P(v, "clip")) + "  url=「" + S(P(v, "url")) + "」"
                + "\n        playing=" + S(P(v, "isPlaying")) + "  prepared=" + S(P(v, "isPrepared"))
                + "  renderMode=" + S(P(v, "renderMode"))
                + "  targetTexture=" + Fmt(P(v, "targetTexture"))

                + "\n        同物体组件 = " + CompNames(P(v, "gameObject")));
        if (blocks.Count > 1) blocks.Sort(StringComparer.Ordinal);
        var sb = new StringBuilder();
        sb.Append("\n  ").Append(tn).Append(" ×").Append(found.Count).Append(" 个");
        if (blocks.Count > 0) sb.Append('\n').Append(string.Join("\n", blocks.ToArray()));
        return sb.ToString();
    }

        static string PathOf(object comp)
    {
        try
        {
            var parts = new List<string>();
            object cur = comp;
            for (int i = 0; i < 6 && cur != null; i++)
            {
                parts.Add(ObjName(cur));
                object tr = P(cur, "transform");
                if (tr == null) break;
                object par = P(tr, "parent");
                if (par == null) break;
                cur = P(par, "gameObject");
            }
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }
        catch (Exception e) { return "<读路径抛 " + (e.InnerException ?? e).GetType().Name + ">"; }
    }

        static MethodInfo SM(Type t, string name, int argc)
    {
        if (t == null) return null;
        try
        {
            foreach (var m in t.GetMethods(FS))
                if (m.Name == name && !m.IsGenericMethodDefinition && m.GetParameters().Length == argc)
                    return m;
        }
        catch { }
        return null;
    }

        static object SP(Type t, string n)
    {
        if (t == null) return null;
        try { return t.GetProperty(n, FS)?.GetValue(null); } catch { return null; }
    }

        static string DumpScenes()
    {
        var sb = new StringBuilder();
        Type sm = T("UnityEngine.SceneManagement.SceneManager");
        Type su = T("UnityEngine.SceneManagement.SceneUtility");
        if (sm == null) return "\n  <SceneManager 没加载>";

        int n = -1;
        try { n = Convert.ToInt32(SP(sm, "sceneCount")); } catch { }
        sb.Append("\n  当前加载 ").Append(n < 0 ? "<sceneCount 读不到>" : n.ToString()).Append(" 个 Unity 场景：");
        var getAt = SM(sm, "GetSceneAt", 1);
        if (getAt == null) sb.Append("\n    <GetSceneAt(int) 取不到>");
        for (int i = 0; i < n && i < 12; i++)
        {
            object sc = null;
            try { sc = getAt.Invoke(null, new object[] { i }); } catch { }
            if (sc == null) { sb.Append("\n    [").Append(i).Append("] <取不到>"); continue; }
            sb.Append("\n    [").Append(i).Append("] 「").Append(S(P(sc, "name")))
              .Append("」 path=「").Append(S(P(sc, "path")))
              .Append("」 buildIndex=").Append(S(P(sc, "buildIndex")))
              .Append(" isLoaded=").Append(S(P(sc, "isLoaded")));
        }

        sb.Append("\n  Build Settings 里的场景：");
        var gu = SM(su, "GetScenePathByBuildIndex", 1);
        if (su == null || gu == null)
        {
            sb.Append("\n    ⚠ <SceneUtility.GetScenePathByBuildIndex 取不到 —— 列不出可加载的场景>");
            return sb.ToString();
        }
        int cnt = 0;
        for (int i = 0; i < 200; i++)
        {
            object pth;
            try { pth = gu.Invoke(null, new object[] { i }); }
            catch { break; }
            string s = S(pth);
            if (string.IsNullOrEmpty(s) || s == "<null>") break;
            sb.Append("\n    [").Append(i).Append("] ").Append(s);
            cnt++;
        }
        if (cnt == 0) sb.Append("\n    ⚠ <0 条 —— 要么真没有，要么这个重载不是 (int)>");
        return sb.ToString();
    }

        const string TRANSITION_SCENE_NAME = "68f68491d98d243489a916de6ed67fb5";

        static readonly List<string> _tsOwned = new List<string>();

        static string EM(Exception ex)
    {
        var e = ex.InnerException ?? ex;
        return e.GetType().Name + ": " + e.Message;
    }

        static List<object> Scenes()
    {
        var list = new List<object>();
        Type sm = T("UnityEngine.SceneManagement.SceneManager");
        if (sm == null) return list;
        int n = -1;
        try { n = Convert.ToInt32(SP(sm, "sceneCount")); } catch { }
        var getAt = SM(sm, "GetSceneAt", 1);
        if (getAt == null || n < 0) return list;
        for (int i = 0; i < n && i < 32; i++)
        {
            object sc = null;
            try { sc = getAt.Invoke(null, new object[] { i }); } catch { }
            if (sc != null) list.Add(sc);
        }
        return list;
    }

    static List<string> SceneNames()
    {
        var list = new List<string>();
        foreach (var sc in Scenes()) list.Add(S(P(sc, "name")));
        return list;
    }

        static bool TsLoaded()
    {
        foreach (var n in SceneNames())
            if (n == TRANSITION_SCENE_NAME || _tsOwned.Contains(n)) return true;
        return false;
    }

        static int _uwReported = -1;

        static void UpdateWatchReport()
    {
        if (UpdateWatch.FirstFrame < 0) return;
        if (_uwReported == UpdateWatch.FirstFrame) return;
        _uwReported = UpdateWatch.FirstFrame;

        W("⚠⚠ 【风暴温度计】`SongSelectScene.Update()` 开始每帧抛异常了 ——"
          + " **第一次 = 帧 " + UpdateWatch.FirstFrame + "**"
          + "（这会儿是帧 " + SceneProbe.FrameNo + "，已抛 " + UpdateWatch.Count + " 次）");
        W("⚠⚠   异常 = " + (UpdateWatch.FirstMsg ?? "<没记到>"));
        W("⚠⚠   当时的处境：GameSeq=" + SceneProbe.GameSeq
          + "  过场场景=" + (TsLoaded() ? "挂着" : "没挂")
          + "  选曲场景=" + (SceneProbe.Scene == null ? "<null>" : "在")
          + "  谱面=" + (SceneProbe.Game == null ? "<null>" : "在"));
        W("⚠⚠   读法 —— 拿这个帧号去对 `[Hotkey]` 那几行：");
        W("⚠⚠     · 一个热键都没按过就在炸 ⇒ 常驻补丁 / 游戏自己的锅");
        W("⚠⚠     · 按过 Alt+F1（喂背景）之后 ⇒ 顺着那条线往上查");
        W("⚠⚠     · 正好在开谱那一瞬间 ⇒ 开谱路径自己的事");
        W("⚠⚠   同一件事 Unity 也在报：`Player.log` 里那一串 `SongSelectScene.Update()` 的 NRE。");
        W("⚠⚠   ⚠ **别再把锅扣在过场场景上**：2026-10-04 实测它卸干净了（`isLoaded=False`），风暴照旧。");
        Flush();
    }

    static readonly string[] NG_SONG = { "_LYA", "_d", "SongInfo" };
    static readonly string[] NG_DIFF = { "_mYA", "_D", "ChartDifficultyFlag" };
        static readonly string[] ST_CTX = { "_rT" };
        static readonly string[] QT = { "_QT" };

        static object RVA(object o, string[] names, out string which, out string why)
    {
        which = null; why = "对象是 null";
        if (o == null || names == null) return null;
        Type t = o as Type;
        object inst = o;
        if (t != null) inst = null;
        else { try { t = o.GetType(); } catch (Exception e) { why = "GetType 抛 " + e.GetType().Name; return null; } }

        string ex = null;
        foreach (var n in names)
        {
            PropertyInfo p = null; FieldInfo f = null;
            try { p = t.GetProperty(n, F); } catch (Exception e) { ex = MemEx(n, e); }
            try { f = t.GetField(n, F); } catch (Exception e) { ex = MemEx(n, e); }
            if (p == null && f == null)
            {
                try { p = t.GetProperty(n, F | BindingFlags.Static); } catch (Exception e) { ex = MemEx(n, e); }
                try { f = t.GetField(n, F | BindingFlags.Static); } catch (Exception e) { ex = MemEx(n, e); }
            }
            if (p == null && f == null) continue;

            var acc = p != null ? (p.GetMethod ?? p.SetMethod) : null;
            bool isStatic = p != null ? (acc != null && acc.IsStatic) : f.IsStatic;
            if (inst == null && !isStatic) continue;
            try
            {
                object val = p != null ? p.GetValue(isStatic ? null : inst) : f.GetValue(isStatic ? null : inst);
                which = (isStatic ? "静态" : "") + (p != null ? "属性 " : "字段 ") + n;
                why = val == null ? "=" : null;
                return val;
            }
            catch (Exception e)
            {
                var ie = e.InnerException ?? e;
                why = "读 " + n + " 取值抛 " + ie.GetType().Name + ": " + ie.Message;
                return null;
            }
        }
        why = "★" + t.Name + " 上这几个名字一个都没有：" + string.Join(" / ", names)
              + (ex == null ? "" : "（取的时候还抛过 " + ex + "）");
        return null;
    }

        static string MemEx(string n, Exception e)
    {
        var ie = e.InnerException ?? e;
        return n + " 上 " + ie.GetType().Name;
    }

        static bool SetX(object o, string[] names, object v, out string which, out string why)
    {
        which = null; why = "对象是 null";
        if (o == null || names == null) return false;
        Type t = o as Type;
        object inst = o;
        if (t != null) inst = null;
        else { try { t = o.GetType(); } catch (Exception e) { why = "GetType 抛 " + e.GetType().Name; return false; } }

        string ex = null;
        foreach (var n in names)
        {
            PropertyInfo p = null; FieldInfo f = null;
            try { p = t.GetProperty(n, F); } catch (Exception e) { ex = MemEx(n, e); }
            try { f = t.GetField(n, F); } catch (Exception e) { ex = MemEx(n, e); }
            if (p == null && f == null)
            {
                try { p = t.GetProperty(n, F | BindingFlags.Static); } catch (Exception e) { ex = MemEx(n, e); }
                try { f = t.GetField(n, F | BindingFlags.Static); } catch (Exception e) { ex = MemEx(n, e); }
            }
            if (p == null && f == null) continue;

            var acc = p != null ? (p.SetMethod ?? p.GetMethod) : null;
            bool isStatic = p != null ? (acc != null && acc.IsStatic) : f.IsStatic;
            if (inst == null && !isStatic) continue;
            if (p != null && !p.CanWrite) { why = "属性 " + n + " 只读（没有 setter）" + Wh2(null, ex); continue; }

            try
            {
                if (p != null) p.SetValue(isStatic ? null : inst, v);
                else f.SetValue(isStatic ? null : inst, v);
                which = (isStatic ? "静态" : "") + (p != null ? "属性 " : "字段 ") + n;
                why = null;
                return true;
            }
            catch (Exception e)
            {
                var ie = e.InnerException ?? e;
                why = "写 " + n + " 抛 " + ie.GetType().Name + ": " + ie.Message;
                return false;
            }
        }
        why = "★" + t.Name + " 上这几个名字一个都没有：" + string.Join(" / ", names)
              + (ex == null ? "" : "（取的时候还抛过 " + ex + "）");
        return false;
    }

        static object RAny(object o, string[] names, out string which, out string why)
    {
        which = null; why = null;
        foreach (var n in names)
        {
            string w1, w2;
            object v = RVA(o, new[] { n }, out w1, out w2);
            if (w1 == null) { if (why == null) why = w2; continue; }
            if (v != null) { which = w1; why = null; return v; }
            if (which == null) which = w1 + "（= <null>）";
        }
        if (which == null) which = "★这几个名字一个都没找到：" + string.Join(" / ", names);
        return null;
    }

        static string Wh2(string which, string why)
    {
        return (which == null ? "" : "  [" + which + "]") + Wh(why);
    }

        static IntPtr Ptr(object o)
    {
        if (o == null) return IntPtr.Zero;
        try
        {
            var ob = o as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase;
            if (ob == null) return IntPtr.Zero;
            return Il2CppInterop.Runtime.IL2CPP.Il2CppObjectBaseToPtr(ob);
        }
        catch { return IntPtr.Zero; }
    }

        static string Hex(IntPtr p)
    {
        return p == IntPtr.Zero ? "<0>" : "0x" + p.ToInt64().ToString("X");
    }

        static string PN(ParameterInfo y)
    {
        Type pt = y.ParameterType;
        return (pt.IsByRef ? "ref " : "") + pt.Name.TrimEnd('&');
    }

        static object CallM(object o, string name, object[] args, string why)
    {
        Type t = o as Type;
        object inst = o;
        if (t != null) inst = null;
        else
        {
            if (o == null) { W($"喂歌： {why} —— ⛔ 目标对象是 null"); return null; }
            try { t = o.GetType(); } catch (Exception e) { W($"喂歌： {why} —— ⛔ GetType 抛 " + EM(e)); return null; }
        }

        int want = args == null ? 0 : args.Length;
        MethodInfo m = null; var cand = new List<string>();
        try
        {
            foreach (var x in t.GetMethods(F | BindingFlags.Static))
            {
                if (x.Name != name || x.IsGenericMethodDefinition) continue;
                var ps = x.GetParameters();
                cand.Add((x.IsStatic ? "static " : "") + x.Name + "(" + string.Join(",",
                    Array.ConvertAll(ps, PN)) + ")");
                if (m == null && ps.Length == want) m = x;
            }
        }
        catch (Exception e) { W($"喂歌： {why} —— ⛔ 枚举 `{name}` 抛 " + EM(e)); return null; }

        if (m == null)
        {
            W($"喂歌： {why} —— ⛔ `{name}`/{want} 参取不到。同名候选："
              + (cand.Count == 0 ? "一个都没有" : string.Join("  ", cand.ToArray())));
            return null;
        }

        string sig = (m.IsStatic ? "静态 " : "") + m.Name + "(" + string.Join(",",
            Array.ConvertAll(m.GetParameters(), PN)) + ")";
        try
        {
            object r = m.Invoke(inst, args);
            W($"喂歌： {why} —— `{sig}` 调通了，返回 " + Fmt(r));
            return r;
        }
        catch (Exception e) { W($"喂歌： {why} —— ⛔ `{sig}` 抛 " + EM(e)); return null; }
    }

        static string Statics(Type ty)
    {
        string w1, y1, w2, y2;
        object rt = RVA(ty, ST_CTX, out w1, out y1);
        object qt = RVA(ty, QT, out w2, out y2);
        return "_rT = " + Fmt(rt) + "  {" + NgKeys(rt) + "}" + Wh2(w1, y1)
             + "   _QT = " + Fmt(qt) + Wh2(w2, y2);
    }

        static string SongKey(object songInfo)
    {
        if (songInfo == null) return "<null>";
        try { return S(P(songInfo, "Id")) + "「" + S(P(songInfo, "BaseName")) + "」"; }
        catch (Exception e) { return "⛔取曲号/曲名抛" + EM(e); }
    }

        static string SongFp(object songInfo)
    {
        if (songInfo == null) return "<null>";
        string id, nm, cn;
        try { id = S(P(songInfo, "Id")); } catch (Exception e) { id = "⛔" + EM(e); }
        try { nm = S(P(songInfo, "BaseName")); } catch (Exception e) { nm = "⛔" + EM(e); }
        try { cn = S(Len(P(songInfo, "ChartInfos"))); } catch (Exception e) { cn = "⛔" + EM(e); }
        return "Id=" + id + "  曲名=「" + nm + "」  谱面 " + cn + " 张";
    }

        static object VidQuiet(object da, string want)
    {
        object vf = da == null ? null : P(da, "VideoFiles");
        object entries = vf == null ? null : P(vf, "entries");
        int n = Len(entries);
        for (int i = 0; i < n; i++)
        {
            object fp = At(entries, i);
            if (fp == null) continue;
            if (string.Equals(S(P(fp, "AssetName")), want, StringComparison.OrdinalIgnoreCase)) return fp;
        }
        return null;
    }

        static object VidEntry(object da, string want, string why)
    {
        object vf = da == null ? null : P(da, "VideoFiles");
        if (vf == null)
        {
            W("喂歌： ⚠ 视频表 `dataAccess.VideoFiles` 读不到（dataAccess=" + Fmt(da) + "）—— " + why + " 跳过");
            return null;
        }
        object entries = P(vf, "entries");
        int n = Len(entries);
        if (n <= 0)
        {
            W("喂歌： ⚠ 视频表 `entries` 空（len=" + n + "）—— " + why + " 跳过");
            return null;
        }
        object hit = VidQuiet(da, want);
        if (hit != null) return hit;
        W("喂歌： ⚠ 视频表（共 " + n + " 条）里没有「" + want + "」—— 把整张表摊开：");
        for (int i = 0; i < n; i++)
        {
            object fp = At(entries, i);
            if (fp == null) { W($"喂歌：   [{i}] <null>"); continue; }
            W($"喂歌：   [{i}] {S(P(fp, "AssetName"))}   音频={S(P(fp, "AudioAsset"))}");
        }
        return null;
    }

        static string NgKeys(object ng)
    {
        if (ng == null) return "<null>";
        string ws, ys, wd, yd;
        object song = RVA(ng, NG_SONG, out ws, out ys);
        object diff = RVA(ng, NG_DIFF, out wd, out yd);
        return "曲=" + SongKey(song) + (ys == null ? "" : "(" + ys + ")")
             + " 难度=" + Fmt(diff) + (yd == null ? "" : "(" + yd + ")");
    }

        static string NgVerdict(object ng)
    {
        if (ng == null) return "回读是个 <null> —— 这一格**读都读不出来**";
        string ws, ys, wd, yd;
        object song = RVA(ng, NG_SONG, out ws, out ys);
        object diff = RVA(ng, NG_DIFF, out wd, out yd);
        string sk = SongKey(song), dk = Fmt(diff);

        string empty = sk.EndsWith("「」") ? "   ⚠ 曲名是空的 —— 这是一份**没填过的** SongInfo" : "";
        return "回读值：曲=" + sk + Wh2(ws, ys) + "   难度=" + dk + Wh2(wd, yd) + empty;
    }

        static readonly string[][] SD_BY_SONG = {
        new[] { "chronophobia",  "_Be" },
        new[] { "codeleviathan", "_de" },
        new[] { "coldsea",       "_Fe" },
        new[] { "enigma",        "_ke" },
        new[] { "kiretsu",       "_pe" },
        new[] { "kuukaku",       "_Qe" },
        new[] { "latentduality", "_se" },
        new[] { "memoire",       "_Te" },
        new[] { "phylaxron",     "_Xe" },
        new[] { "tactom",        "_ze" },
        new[] { "tokonagi",      "_bE" },
        new[] { "zetta",         "_CE" },
    };

        static string SdPick(object ng)
    {
        if (ng == null) return "曲子→特殊背景：⛔ 信封读不出来，判不了（**不是**「这首没有」）";
        string ws, ys;
        object song = RVA(ng, NG_SONG, out ws, out ys);
        string key = SongKey(song);
        foreach (var pair in SD_BY_SONG)
            if (key.IndexOf(pair[0], StringComparison.OrdinalIgnoreCase) >= 0)
                return "曲子→特殊背景：曲名含「" + pair[0] + "」⇒ 该建 `_E." + pair[1] + "`"
                     + "（`_wEA` 若仍是 `_xe` ⇒ **游戏没建**）";
        return "曲子→特殊背景：曲名 " + key + " 不在那 12 首里 ⇒ 这首**本来就没有**特殊背景，`_xe` 是正常值";
    }

    static readonly string[] NG_FIELDS = {
        "_GYA", "_hYA", "_HYA", "_iYA", "_IYA",
        "_jYA", "_JYA", "_kYA", "_KYA", "_lYA",
        "_LYA", "_mYA", "_MYA", "_nYA", "_NYA",
    };
    static readonly string[] NG_FTYPE = {
        "StoryIdentifier", "StoryEntrySongPlayRequirement", "EncounterId", "_wh", "_TG",
        "CardInstance", "CardInstance", "CardInstance", "CardInstance", "CardInstance",
        "SongInfo", "ChartDifficultyFlag", "CharacterIdentifier", "CharacterIdentifier", "_tH",
    };

        static string NgVal(object v)
    {
        if (v == null) return "<null>";
        try { string s = v.ToString(); return s == null ? "<ToString 给 null>" : s; }
        catch (Exception e) { return "⛔ToString 抛 " + EM(e); }
    }

        static void NgDump(object ng, string tag)
    {
        if (ng == null) { W(tag + "：⛔ 信封是 <null>，摊不开（这和「15 格全空」是**两件事**）"); return; }
        W(tag + "：摊开 15 格（" + Kind(ng) + "）");
        for (int i = 0; i < NG_FIELDS.Length; i++)
        {
            string w, y;
            object v;
            try { v = RVA(ng, new[] { NG_FIELDS[i] }, out w, out y); }
            catch (Exception e) { W("  [" + i + "] " + NG_FIELDS[i] + " (" + NG_FTYPE[i] + ") ⛔ 读抛 " + EM(e)); continue; }

            string show = i == 10 ? SongKey(v) : NgVal(v);

            if (i == 1) show += "  " + ReqDump(v);
            W("  [" + i + "] " + NG_FIELDS[i] + " (" + NG_FTYPE[i] + ") = " + show
              + (w == null ? "   ⛔ " + y : "  [" + w + "]"));
        }
    }

        static string ReqDump(object req)
    {
        if (req == null) return "｛<null>｝";
        string id, sid, typ;
        try { id = NgVal(P(req, "StoryIdentifier")); } catch (Exception e) { id = "⛔" + EM(e); }
        try { sid = S(P(req, "SongId")); } catch (Exception e) { sid = "⛔" + EM(e); }
        try { typ = NgVal(P(req, "SongPlayType")); } catch (Exception e) { typ = "⛔" + EM(e); }
        return "｛日期=" + id + "  曲=「" + sid + "」  类型=" + typ + "｝";
    }

        static object StoryTable(object da, string songName, out object storyId, bool quiet = false)
    {
        storyId = null;
        Type tda = T("ifapp.Game.Data.DataAccess");

        string w = null, y = null;
        object sd = null;
        if (tda != null) sd = RVA(tda, new[] { "_jAb" }, out w, out y);
        if (!quiet)
            W("喂歌： 故事表 · DataAccess._jAb(静态) = " + Fmt(sd)
              + (tda == null ? "   ⛔ 类型没加载" : Wh2(w, y)));
        if (sd == null && da != null)
        {
            sd = RVA(da, new[] { "StoryDetails" }, out w, out y);
            if (!quiet) W("喂歌： 故事表 · 退路 da.StoryDetails = " + Fmt(sd) + Wh2(w, y));
        }
        if (sd == null) { W("喂歌： 故事表 ⛔ 两处都拿不到 `StoryDetails` —— 这条查不下去"); return null; }

        object arr = RVA(sd, new[] { "orderedStoryEntrySongPlayRequirements" }, out w, out y);
        if (arr == null) { W("喂歌： 故事表 ⛔ 读不到 `orderedStoryEntrySongPlayRequirements`" + Wh2(w, y)); return null; }
        int n = Len(arr);
        if (n < 0) { W("喂歌： 故事表 ⛔ 那张表读出来了但**数不出条数**（Len = " + n + "）" + Wh2(w, y)); return null; }
        if (!quiet) W("喂歌： 故事表 「曲→故事日期」共 " + n + " 条" + Wh2(w, y));
        object hit = null;
        for (int i = 0; i < n; i++)
        {
            object e = At(arr, i);
            if (e == null) { if (!quiet && i < 60) W("  [" + i + "] <null>"); continue; }
            string sid, date, typ;
            object ident = null;
            try { sid = S(P(e, "SongId")); } catch (Exception ex) { sid = "⛔" + EM(ex); }
            try { ident = P(e, "StoryIdentifier"); date = NgVal(ident); } catch (Exception ex) { date = "⛔" + EM(ex); }
            try { typ = S(P(e, "SongPlayType")); } catch (Exception ex) { typ = "⛔" + EM(ex); }
            bool m = Match2(sid, songName);
            if (!quiet && (i < 60 || m))
                W("  [" + i + "]" + (m ? "★" : " ") + " 曲=「" + sid + "」  日期=" + date + "  类型=" + typ);
            if (hit == null && m) { hit = e; storyId = ident; }
        }
        if (!quiet && n > 60) W("  （只列了前 60 条；★ 标的是命中的那几条）");
        if (hit == null)
            W("喂歌： 故事表 ⚠ 「" + songName + "」在这张表里**没有**条目 ⇒ 故事模式里那份信封也不会有 `_hYA`，"
              + "我们和游戏在这一点上**一样** —— 那特殊背景就不认这封信（这一条到此为止）。");
        else
            W("喂歌： 故事表 ✅ 「" + songName + "」对上一条：日期 = " + NgVal(storyId));
        return hit;
    }

        static bool Match2(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        if (a.StartsWith("<", StringComparison.Ordinal)) return false;
        return a.IndexOf(b, StringComparison.OrdinalIgnoreCase) >= 0
            || b.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0;
    }

}
