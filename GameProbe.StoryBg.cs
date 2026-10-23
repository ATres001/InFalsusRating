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

        class Pick
    {
                public object Scene;
                public object Da;
                public object Song;
                public object Diff;
                public string Id;
                public string Name;
    }

        static bool CurrentPick(string tag, bool quiet, out Pick p)
    {
        p = null;

        object ss = SceneProbe.Scene;
        if (ss == null)
        {
            var ssl = FindAll(T("ifapp.Game.Scenes.SongSelectScene"));
            if (ssl.Count > 0)
            {
                ss = ssl[0];
                W(tag + " ⚠ `SceneProbe.Scene` 是空的，现找了 " + ssl.Count + " 个 SongSelectScene，拿第一个顶上");
            }
        }
        if (ss == null)
        {
            W(tag + " ⛔ 现在不在选曲场景（按类型也一个都没找到）—— 没有源，什么都没写");
            Flush();
            return false;
        }

        object diff = PV(ss, "_Sr");
        string sid = S(PV(ss, "_sr"));
        object da = PV(ss, "dataAccess");
        if (!quiet) W(tag + " 选曲界面当前选中 → SongId = " + sid + "，难度 = " + Fmt(diff));

        if (!quiet) W(tag + "   对照 · 选曲场景的 `_wr` = " + SongFp(PV(ss, "_wr")));
        if (da == null) { W(tag + " ⛔ 选曲场景的 `dataAccess` 是空的 —— 取不到全曲库，没有源"); Flush(); return false; }

        object song = null;
        int scanned = 0;
        foreach (var s in Songs(da))
        {
            scanned++;
            if (S(P(s, "Id")) == sid) { song = s; break; }
        }
        if (!quiet) W(tag + "   采用 · 全曲库（扫了 " + scanned + " 首）里 Id=" + sid + " 的那一首 = " + SongFp(song));
        if (song == null)
        {
            W(tag + " ⛔ 全曲库里没有 Id = " + sid + " 的曲子 —— 没有源，什么都没写");
            Flush();
            return false;
        }
        string sname = S(P(song, "BaseName"));
        if (string.IsNullOrEmpty(sname) || sname == "<null>")
        {
            W(tag + " ⛔ 查到的那一首**曲名是空的** —— 视频是按曲名命名的，没有曲名就取不到素材。到此为止。");
            Flush();
            return false;
        }

        Pick q = new Pick();
        q.Scene = ss; q.Da = da; q.Song = song; q.Diff = diff; q.Id = sid; q.Name = sname;
        p = q;
        return true;
    }

        static void FeedBgOnly()
    {
        W("Alt+F1·背景： ===== 只喂特殊背景（不碰 `_rT`、不叫 `_ok`）=====");
        Pick q;
        if (!CurrentPick("Alt+F1·背景：", false, out q)) return;

        FeedSpecialBg(q.Da, q.Song, q.Diff, q.Name, "Alt+F1·背景：", false, false);
        W("Alt+F1·背景： 【下一步】这一条**不用过场场景** —— 直接开这首曲子进谱面，"
          + "在里面按 F5 看 `[G]` 的 `_VD._wEA`；顺便留意**血条**和**退出去落在哪个界面**。");
        Flush();
    }

        public static bool FeedBgForExam(object songInfo, object diff, out string why)
    {
        why = "";
        object ss = SceneProbe.Scene;
        if (ss == null) { why = "⛔ 选曲场景没了 —— 取不到 `dataAccess`，这一首没喂"; return false; }
        object da = PV(ss, "dataAccess");
        if (da == null) { why = "⛔ 选曲场景的 `dataAccess` 是空的 —— 这一首没喂"; return false; }

        string sname = songInfo == null ? "" : S(P(songInfo, "BaseName"));

        bool ok = FeedSpecialBg(da, songInfo, diff, sname, "课题·背景", true, true);
        why = ok ? "信封已写进 `GameScene._dN` 并开始保压（收摊时给 `_wEA` 判决）"
                 : "**没有**（这首没有故事条目 / 哪一步没成立 —— GameProbe.txt 里「课题·背景」那几行写了是哪一条）";
        return ok;
    }

    class BgRecipe
    {
                public bool Restore = true;
                public bool Early;
                public bool HpLock = true;
                public string[] Zero = new string[0];
    }

    static string BgExpFile => Path.Combine(Paths.ConfigPath, "InFalsusRating", "bg_exp.txt");

        static BgRecipe BgRec()
    {
        var r = new BgRecipe();
        string path = BgExpFile;
        if (!File.Exists(path))
        {
            try
            {
                File.WriteAllText(path,
                    "# 特殊背景实验配方（2026-10-05）。\r\n" +
                    "# Alt+F1 **每按一次都重读这个文件** —— 改完直接按，不用重开游戏。\r\n" +
                    "# 每一行 `键=值`，`#` 开头是注释。\r\n" +
                    "#\r\n" +
                    "# restore=1   收摊时把 `GameScene._dN` **还原成游戏自己写的那份**（默认 1）\r\n" +
                    "#             0 = 不还原（留着我们的故事信封）—— 这是对照\r\n" +
                    "# early=1     只要 `_VD._wEA` 一被认领（不再是 `_xe`）就立刻收摊（默认 0）\r\n" +
                    "#             0 = 照原样压到「谱面起来 + 3 秒」\r\n" +
                    "# hp=1        谱面里把这一局的 `canHardFailed` 按住不放（默认 1）\r\n" +
                    "#             1 = 剧情血条归零也不会强制退出（血条照掉，但不再判死）\r\n" +
                    "#             0 = 不按 —— 对照用，那时会看见「会死的故事血条」\r\n" +
                    "# zero=...    喂之前把这几个格子**归零**（默认空 = 不动）。\r\n" +
                    "#             15 格全名：_GYA _hYA _HYA _iYA _IYA _jYA _JYA _kYA _KYA _lYA\r\n" +
                    "#                        _LYA _mYA _MYA _nYA _NYA\r\n" +
                    "#             例：zero=_GYA   或   zero=_IYA,_nYA\r\n" +
                    "#\r\n" +
                    "restore=1\r\n" +
                    "early=0\r\n" +
                    "hp=1\r\n" +
                    "zero=\r\n");
            }
            catch (Exception e) { W("配方： ⚠ 想写一份默认的 `" + path + "` 没写成 " + EM(e)); }
            return r;
        }

        try
        {
            foreach (string raw in File.ReadAllLines(path))
            {
                string ln = raw.Trim();
                if (ln.Length == 0 || ln[0] == '#' || ln[0] == ';') continue;
                int eq = ln.IndexOf('=');
                if (eq <= 0) { W("配方： ⚠ 「" + ln + "」没有 `=` —— 跳过"); continue; }
                string k = ln.Substring(0, eq).Trim().ToLowerInvariant();
                string v = ln.Substring(eq + 1).Trim();
                if (k == "restore") r.Restore = !(v == "0" || v.Equals("false", StringComparison.OrdinalIgnoreCase));
                else if (k == "early") r.Early = (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));

                else if (k == "hp") r.HpLock = !(v == "0" || v.Equals("false", StringComparison.OrdinalIgnoreCase));
                else if (k == "zero")
                {
                    var list = new List<string>();
                    foreach (string one in v.Split(','))
                    { string f = one.Trim(); if (f.Length > 0) list.Add(f); }
                    r.Zero = list.ToArray();
                }
                else W("配方： ⚠ 不认识的键「" + k + "」—— 跳过（只有 restore / early / hp / zero 四个）");
            }
        }
        catch (Exception e)
        { W("配方： ⚠ 读 `" + path + "` 炸了 " + EM(e) + " —— 这一下用默认值（restore=1 early=0 zero=空）"); }
        return r;
    }

        static bool SdClaimed(object wEA)
    {
        if (wEA == null) return false;
        Type tb = T("_E._xe");
        if (tb == null) return false;
        try { return !tb.IsInstanceOfType(wEA); } catch { return false; }
    }

        static bool FeedSpecialBg(object da, object song, object diff, string sname,
        string tag, bool quiet, bool fromExam)
    {
        if (da == null || song == null || diff == null)
        {
            W(tag + " ⛔ 参数不全（DataAccess / 曲子 / 难度 里有空的）—— 什么都没写");
            return false;
        }
        if (string.IsNullOrEmpty(sname) || sname.StartsWith("<", StringComparison.Ordinal))
        {
            W(tag + " ⛔ 曲名是空的或者读炸了（「" + sname + "」）—— 故事表是按**曲名**查的，到此为止");
            return false;
        }

        object storyId;
        object hit = StoryTable(da, sname, out storyId, quiet);
        if (hit == null || storyId == null)
        {
            W(tag + " ⚠ 「" + sname + "」在故事表里**没有**条目 ⇒ 造不出故事形状的信封，"
              + "特殊背景不会认它 —— **这一首跳过**（12 首有背景的曲子全都在表里，所以这一条"
              + "命中的都是本来就没背景的）。");
            return false;
        }

        Type ng = T("_b._nG");
        if (ng == null) { W(tag + " ⛔ `_b._nG` 这个类型没加载 —— 造不出信封"); return false; }

        object ctx;
        try { ctx = Activator.CreateInstance(ng); }
        catch (Exception e) { W(tag + " ⛔ new _b._nG() 抛 " + EM(e)); return false; }
        if (ctx == null) { W(tag + " ⛔ new _b._nG() 给了 null"); return false; }

        CallM(ctx, "_fKA", new object[] { da, storyId }, tag + " · `_fKA(DataAccess, 日期)`");
        CallM(ctx, "_eKA", new object[] { song, diff }, tag + " · `_eKA(曲, 难度)`");

        BgRecipe rec = BgRec();
        W(tag + " 配方： restore=" + (rec.Restore ? "1" : "0") + "  early=" + (rec.Early ? "1" : "0")
          + "  hp=" + (rec.HpLock ? "1" : "0")
          + "  zero=" + (rec.Zero.Length == 0 ? "(空)" : string.Join(",", rec.Zero))
          + "   ← 文件 " + BgExpFile + "（每按一次都重读）");
        foreach (string f in rec.Zero)
        {
            if (Array.IndexOf(NG_FIELDS, f) < 0)
            { W(tag + "   ⚠ zero 里的「" + f + "」不是那 15 格里的名字 —— 跳过"); continue; }

            try
            {
                string zw, zy;
                object cur = RVA(ctx, new[] { f }, out zw, out zy);
                if (cur == null) { W(tag + "   ⚠ 「" + f + "」本来就 <null> —— 跳过"); continue; }
                Type ft = cur.GetType();
                object zero;
                try { zero = Activator.CreateInstance(ft); }
                catch (Exception e)
                { W(tag + "   ⛔ 「" + f + "」造不出默认值（托管类型 " + ft.Name + "）" + EM(e) + " —— 跳过"); continue; }
                string xw, xy;
                if (SetX(ctx, new[] { f }, zero, out xw, out xy))
                    W(tag + "   ★ 已归零 " + (xw ?? f) + "（原值 " + NgVal(cur) + "）");
                else
                    W(tag + "   ⛔ 归零 " + f + " 失败：" + xy);
            }
            catch (Exception e) { W(tag + "   ⛔ 归零 " + f + " 抛 " + EM(e) + " —— 跳过"); }
        }

        string wq, yq, wp, yp;
        object s2 = RVA(ctx, NG_SONG, out wq, out yq);
        object d2 = RVA(ctx, NG_DIFF, out wp, out yp);
        string gotSong = SongKey(s2), wantSong = SongKey(song);
        string gotDiff = Fmt(d2), wantDiff = Fmt(diff);
        if (gotSong != wantSong || gotDiff != wantDiff)
        {
            W(tag + " ⛔ 造出来的信封**值对不上**：曲=" + gotSong + "（要 " + wantSong + "）"
              + "，难度=" + gotDiff + "（要 " + wantDiff + "）—— **不喂**"
              + Wh2(wq ?? wp, yq ?? yp));
            return false;
        }

        Type tg = T("ifapp.Game.Scenes.GameScene");
        if (tg == null) { W(tag + " ⛔ `GameScene` 这个类型没加载 —— 没写成"); return false; }

        string pw = null, py = null;
        object prev = null;
        try { prev = RVA(tg, new[] { "_dN" }, out pw, out py); }
        catch (Exception e) { W(tag + "   ⚠ 抄写前那份 `_dN` 时抛 " + EM(e)); }
        W(tag + "   写之前 `_dN` 里坐着的是：" + DnKey(prev) + Wh2(pw, py));

        try
        {
            string tw, ty;
            object rt = RVA(T("ifapp.Game.Scenes.StorySongTransitionScene"), new[] { "_rT" }, out tw, out ty);
            W(tag + "   对照 · `_rT`（过场那格）现在 = " + DnKey(rt) + Wh2(tw, ty));
        }
        catch (Exception e) { W(tag + "   对照 · 读 `_rT` 抛 " + EM(e)); }

        string ws, wy;
        if (!SetX(tg, new[] { "_dN" }, ctx, out ws, out wy))
        {
            W(tag + " ⛔ 写 `GameScene._dN` 失败：" + wy);
            return false;
        }

        W(tag + " ✅ 曲=" + gotSong + " / " + gotDiff + "  " + SdPick(ctx));
        W(tag + "   信封已写进 `GameScene._dN`，从现在起**每帧重写压住**"
          + "（游戏会在进谱路上把它顶掉一次 —— 那时日志里会报帧号）。");
        W(tag + "   验票：进谱后按 F5，看 `[G]` 的 `_VD._wEA` —— 从 `_xe` 变成 `_E._bE` 那一族 = 成了。");

        DnHoldStart(ctx, fromExam, prev, rec);

        if (rec.HpLock) HpLockArm();

        Flush();
        return true;
    }

    static object _dnHold;
    static string _dnHoldKey;
    static int _dnHoldUntil;

    static int _dnHoldGameAt;
    static int _dnChartFrame;
    static int _dnClaimFrame;
    static int _dnHoldN;
    static int _dnHoldClob;
    static bool _dnHoldSaid;
    static string _dnHoldTag = "喂歌·保压";
        static bool _dnHoldFromExam;
        static object _dnHoldShell;
        static object _dnHoldPrev;
        static BgRecipe _dnHoldRec;

        static string DnKey(object ng)
    {
        if (ng == null) return "<null>";
        string w1, y1, w2, y2, w3, y3;
        object s, d, g;
        try { s = RVA(ng, NG_SONG, out w1, out y1); } catch (Exception e) { return "⛔曲 " + EM(e); }
        try { d = RVA(ng, NG_DIFF, out w2, out y2); } catch (Exception e) { return "⛔难度 " + EM(e); }
        try { g = RVA(ng, new[] { "_GYA" }, out w3, out y3); } catch (Exception e) { return "⛔日期 " + EM(e); }
        return SongKey(s) + " / " + Fmt(d) + " / " + NgVal(g);
    }

        static void DnHoldStart(object ctx, bool fromExam = false, object prev = null, BgRecipe rec = null)
    {
        _dnHold = ctx;
        _dnHoldKey = DnKey(ctx);
        _dnHoldFromExam = fromExam;
        _dnHoldPrev = prev;
        _dnHoldRec = rec;
        _dnHoldShell = null;
        _dnChartFrame = 0;
        _dnClaimFrame = 0;
        _dnHoldTag = fromExam ? "课题·背景" : "喂歌·保压";

        _dnHoldUntil = Environment.TickCount + (fromExam ? 600000 : 120000);
        _dnHoldGameAt = 0; _dnHoldN = 0; _dnHoldClob = 0; _dnHoldSaid = false;
        W(_dnHoldTag + "：★ 从现在起**每帧重写 `GameScene._dN`**，压到「谱面起来 + 3 秒」"
          + (fromExam ? "或考试结束" : "或 120 秒死线") + "为止。"
          + (fromExam ? "（考试那条的死线是 10 分钟 —— 过场能放 150 秒，120 秒会在半路放掉保压）" : ""));
        W(_dnHoldTag + "：   压在下面这份指纹上 = " + _dnHoldKey);
        W(_dnHoldTag + "：   （游戏顶掉它的话会报出帧号；收摊那一行直接给 `_wEA` 判决）");
        Flush();
    }

    static void DnHoldStop(string why)
    {
        if (_dnHold == null) return;
        _dnHold = null;
        _dnHoldFromExam = false;
        if (_dnHoldSaid) return;
        _dnHoldSaid = true;
        string tg3 = _dnHoldTag;
        W(tg3 + "：收摊" + (why == null ? "" : "（" + why + "）")
          + " —— 一共压了 " + _dnHoldN + " 帧，期间发现「被顶掉」" + _dnHoldClob + " 次");

        if (_dnHoldRec == null || _dnHoldRec.Restore)
        {
            object back = _dnHoldShell != null ? _dnHoldShell : _dnHoldPrev;
            string src = _dnHoldShell != null ? "保压期间**游戏自己写的那份**"
                       : (_dnHoldPrev != null ? "喂之前坐着的那份（兜底）" : null);
            if (back == null)
                W(tg3 + "：还原 ⚠ **没得还**（既没抓到游戏自己写的那份，喂之前那格也是 <null>）"
                  + " —— `_dN` 留着我们的故事信封（⚠ 下一首开谱前会被顶掉，但这一局剩下的时间里它是我们的）");
            else
            {
                Type tg4 = T("ifapp.Game.Scenes.GameScene");
                string rw = null, ry = null;
                bool okr = false;
                try { okr = tg4 != null && SetX(tg4, new[] { "_dN" }, back, out rw, out ry); }
                catch (Exception e) { W(tg3 + "：还原 ⛔ 抛 " + EM(e)); }
                if (okr)
                {

                    string want = DnKey(back);
                    string got;
                    try
                    {
                        string g1, g2;
                        object chk = RVA(tg4, new[] { "_dN" }, out g1, out g2);
                        got = chk == null ? "<null>" : DnKey(chk);
                    }
                    catch (Exception e) { got = "⛔回读抛 " + EM(e); }
                    W(tg3 + "：还原 ✅ `_dN` ← " + src + "（" + want + "）");
                    W(tg3 + "：    回读校验：" + (got == want ? "✅ 一致 —— 还原确实落下去了"
                                                              : "⛔ 不一致，现在读到的是 " + got));
                }
                else if (tg4 == null) W(tg3 + "：还原 ⛔ `GameScene` 类型不见了");
                else W(tg3 + "：还原 ⛔ 写回去失败：" + ry);
            }
        }
        else W(tg3 + "：还原 —— 配方 restore=0，**故意不还**（`_dN` 留着我们的故事信封，对照组）");
        _dnHoldShell = null; _dnHoldPrev = null; _dnHoldRec = null;

        try
        {
            Type tg2 = T("ifapp.Game.Scenes.GameScene");
            if (tg2 == null) W(tg3 + "：⛔ `GameScene` 类型不见了，判决读不出来");
            else
            {
                string vw, vy;
                object vd = RVA(tg2, new[] { "_DN" }, out vw, out vy);
                if (vd == null) W(tg3 + "：判决 · `_VD` 还是 <null>（谱面没起来？）");
                else
                {
                    string sw, sy;
                    object ss = RVA(vd, new[] { "_wEA" }, out sw, out sy);
                    W(tg3 + "：判决 · `_VD._wEA` = " + Kind(ss) + "   托管类=" + FN(ss) + Wh2(sw, sy));
                    W(tg3 + "：       " + SdWhich(ss));
                    W(tg3 + "：   ✅ `_bE` 那一族 = **成了**；还是 `_xe` = 这一格没压住，看上面的「被顶掉」帧号");
                }
            }
        }
        catch (Exception e) { W(tg3 + "：⛔ 判决读炸了 " + EM(e)); }
        Flush();
    }

        static void DnHoldTick()
    {
        if (_dnHold == null) return;
        int now = Environment.TickCount;

        if (_dnHoldFromExam && !TaskRunner.Busy)
        { DnHoldStop("考试停了"); return; }

        Type tg = T("ifapp.Game.Scenes.GameScene");
        if (tg == null) { DnHoldStop("`GameScene` 类型不见了"); return; }

        if (SceneProbe.Game != null)
        {
            if (_dnHoldGameAt == 0)
            {
                _dnHoldGameAt = now;
                _dnChartFrame = SceneProbe.FrameNo;
                W(_dnHoldTag + "：★ 谱面起来了（帧 " + SceneProbe.FrameNo + "）—— `_dN` 再压 3 秒就收");
                Flush();
            }
            else if (now - _dnHoldGameAt > 3000) { DnHoldStop("谱面起来 3 秒了"); return; }
        }

        if (_dnHoldGameAt != 0 && _dnClaimFrame == 0)
        {
            object wEA = null;
            try
            {
                string aw, ay;
                object vd = RVA(tg, new[] { "_DN" }, out aw, out ay);
                if (vd != null)
                {
                    string bw, by;
                    wEA = RVA(vd, new[] { "_wEA" }, out bw, out by);
                }
            }
            catch (Exception e) { W(_dnHoldTag + "：⚠ 认领那一问抛 " + EM(e)); }
            if (SdClaimed(wEA))
            {
                _dnClaimFrame = SceneProbe.FrameNo;
                W(_dnHoldTag + "：★ `_VD._wEA` 被认领了 → " + SdWhich(wEA)
                  + "（谱面起来 +" + (_dnClaimFrame - _dnChartFrame) + " 帧）");
                W(_dnHoldTag + "：   ⇒ 血条那个副作用如果定死得比这一帧早，早还 `_dN` 就没得救，"
                  + "只剩 `zero=` 那一栏逐格拆（哪一格让游戏当成故事对局）");
                Flush();
                if (_dnHoldRec != null && _dnHoldRec.Early)
                { DnHoldStop("配方 early=1：`_wEA` 已被认领（" + SdWhich(wEA) + "）"); return; }
            }
        }

        if (now - _dnHoldUntil > 0)
        { DnHoldStop(_dnHoldFromExam ? "到 10 分钟硬上限了（考试还挂着？）" : "到 120 秒死线了"); return; }

        string w, y;
        object cur = RVA(tg, new[] { "_dN" }, out w, out y);
        string curKey = DnKey(cur);
        if (curKey != _dnHoldKey)
        {

            _dnHoldShell = cur;
            if (_dnHoldClob < 5)
            {
                _dnHoldClob++;
                W(_dnHoldTag + "：⚠ 第 " + _dnHoldClob + " 次发现 `_dN` **被顶掉了**（帧 " + SceneProbe.FrameNo
                  + "，此时已压 " + _dnHoldN + " 帧）");
                W(_dnHoldTag + "：     现在 = " + curKey);
                W(_dnHoldTag + "：     我们 = " + _dnHoldKey);
                W(_dnHoldTag + "：     ⇒ 已**存下这一份**，收摊时还它（配方 restore）");
            }
        }

        string ws, why;
        bool ok = SetX(tg, new[] { "_dN" }, _dnHold, out ws, out why);
        _dnHoldN++;
        if (!ok && _dnHoldClob < 6)
        {
            _dnHoldClob++;
            W(_dnHoldTag + "：⛔ 写 `_dN` 失败：" + why);
        }
    }

    static bool _hpLock;
    static string _hpLockTag = "锁血";
    static int _hpLockUntil;
    static int _hpLockWrites;
    static int _hpLockBad;
    static int _hpLockFrame1;
    static bool _hpLockSaid;
    static string _hpLockFail;
    static string _hpLockLast = "";
        static bool _hpLockSawGame;
        static int _hpLockGone;
        static int _hpLockMiss;

        static void HpLockArm()
    {
        _hpLock = true;
        _hpLockTag = _dnHoldTag;

        _hpLockUntil = Environment.TickCount + (_dnHoldFromExam ? 600000 : 120000);
        _hpLockWrites = 0; _hpLockBad = 0; _hpLockFrame1 = 0;
        _hpLockSaid = false; _hpLockFail = null; _hpLockLast = "";
        _hpLockSawGame = false; _hpLockGone = 0; _hpLockMiss = 0;
        W(_hpLockTag + "：★★ **锁血已开** —— 谱面一起来就每帧把记分册的"
          + " `canHardFailed`(`_K._Uh._aCb`) 按成 false（背景和「会真死的故事对局」是同一封信带来的，"
          + "退出落故事界面那一半已经由 `restore` 治好，这一半只能进谱后按住）。"
          + " 关掉：`bg_exp.txt` 里 `hp=0`。");
        W(_hpLockTag + "：   现在是在**等谱面**（还没进谱 —— 这一段的死线是 "
          + (_dnHoldFromExam ? "10 分钟" : "120 秒") + "，不是 15 分钟）");
        Flush();
    }

        static void HpLockFail(string msg)
    {
        if (_hpLockFail != null) return;
        _hpLockFail = msg;
        W(_hpLockTag + "：锁血 ⚠ " + msg);
        Flush();
    }

        static object FindMemberOfKind(object o, string cls, out string which, out string why)
    {
        which = null; why = "对象是 null";
        if (o == null) return null;
        Type t;
        try { t = o.GetType(); } catch (Exception e) { why = "GetType 抛 " + EM(e); return null; }
        var names = new List<string>();
        try { foreach (var p in t.GetProperties(F)) if (p.GetIndexParameters().Length == 0) names.Add(p.Name); } catch { }
        try { foreach (var f in t.GetFields(F)) names.Add(f.Name); } catch { }
        int tried = 0;
        foreach (string n in names)
        {

            if (n.StartsWith("NativeFieldInfoPtr_", StringComparison.Ordinal)
             || n.StartsWith("NativeMethodInfoPtr_", StringComparison.Ordinal)) continue;
            object v = null;
            try { v = RVA(o, new[] { n }, out _, out _); } catch { }
            tried++;
            if (v == null) continue;
            string k = null;
            try { k = Kind(v); } catch { }
            if (k == cls) { which = "扫出来的成员「" + n + "」"; why = null; return v; }
        }
        why = "★在 " + t.Name + " 的 " + tried + " 个实例成员里**没有一个**的原生类是「" + cls + "」";
        return null;
    }

        static void HpLockTick()
    {
        if (!_hpLock) return;

        if (SceneProbe.Game == null)
        {
            if (!_hpLockSawGame)
            {

                if (Environment.TickCount - _hpLockUntil > 0)
                {
                    W(_hpLockTag + "：锁血撤了 —— 开锁之后一直**没进谱**（等到等谱面那条死线了）。"
                      + " 路上要是没开谱，这一条就是正常的。");
                    _hpLock = false; Flush();
                }
                return;
            }

            if (++_hpLockGone < 3) return;
            W(_hpLockTag + "：锁血收摊（谱面没了）—— 一共按下去 " + _hpLockWrites + " 次"
              + (_hpLockFrame1 == 0 ? "（**一次都没按到**）" : "，第一次在帧 " + _hpLockFrame1)
              + (_hpLockBad > 0 ? "，另有 " + _hpLockBad + " 次按下去了没生效 ⛔" : ""));
            if (_hpLockLast.Length > 0) W(_hpLockTag + "：   收摊时记分册最后一次读数 |" + _hpLockLast);
            _hpLock = false;
            Flush();
            return;
        }
        _hpLockGone = 0;
        if (!_hpLockSawGame)
        {
            _hpLockSawGame = true;
            _hpLockUntil = Environment.TickCount + 900000;
            W(_hpLockTag + "：锁血 · **谱面起来了**（帧 " + SceneProbe.FrameNo + "）—— 开始找记分册 `_K._Uh`");
            Flush();
        }
        if (Environment.TickCount - _hpLockUntil > 0) { W(_hpLockTag + "：锁血到 15 分钟硬上限，收摊"); _hpLock = false; return; }

        Type tg = T("ifapp.Game.Scenes.GameScene");
        if (tg == null) { HpLockFail("`GameScene` 这个类型不见了"); return; }

        object vd = null, uh = null;
        string missWhy = null;
        try
        {
            string w1, y1;
            vd = RVA(tg, new[] { "_DN" }, out w1, out y1);
            if (vd == null) missWhy = "`GameScene._DN`（会话对象 `_VD`）还是 <null> —— " + y1;
        }
        catch (Exception e) { missWhy = "读 `GameScene._DN` 抛 " + EM(e); }

        if (vd != null)
        {
            try
            {
                string w2, y2;
                uh = RVA(vd, new[] { "_UEA", "_uEA" }, out w2, out y2);
                if (uh == null)

                    uh = FindMemberOfKind(vd, "_Uh", out w2, out y2);
                if (uh == null) missWhy = "`_VD` 上没找到记分册 —— " + y2;
            }
            catch (Exception e) { missWhy = "读 `_VD._UEA` 抛 " + EM(e); }
        }

        if (uh == null)
        {

            if (++_hpLockMiss == 13) HpLockFail("谱面起来 13 帧了还找不到记分册 —— " + missWhy);
            return;
        }
        _hpLockMiss = 0;

        string w3, y3; object cur = null; bool okRead;
        try { cur = RVA(uh, new[] { "_aCb" }, out w3, out y3); okRead = w3 != null; }
        catch (Exception e) { HpLockFail("读 `_aCb` 抛 " + EM(e)); return; }
        if (!okRead) { HpLockFail("记分册上读不到 `_aCb` —— " + y3); return; }

        bool hard;
        try { hard = Convert.ToBoolean(cur); }
        catch { HpLockFail("`_aCb` 不是 bool（读到 " + Fmt(cur) + "）"); return; }

        var sb = new StringBuilder();
        foreach (string n in HP_WATCH)
        {
            string a = null, b = null; object v = null;
            try { v = RVA(uh, new[] { n }, out a, out b); } catch { }
            if (a == null) continue;
            sb.Append(' ').Append(n).Append('=').Append(v == null ? "<null>" : S(v)).Append(';');
        }
        if (sb.Length > 0) _hpLockLast = sb.ToString();

        if (!_hpLockSaid)
        {
            _hpLockSaid = true;
            W(_hpLockTag + "：锁血 · 找到记分册了（原生类 " + Kind(uh) + "，帧 " + SceneProbe.FrameNo + "），"
              + "`_aCb`(`canHardFailed`) 初值 = **"
              + (hard ? "True** —— ✅ 它这一局**会**真判死，找对地方了"
                      : "False** —— ⚠ 它**本来就不判死** ⇒ 会死就不是这一位管的，别再往这条路上修"));
            if (_hpLockLast.Length > 0) W(_hpLockTag + "：   进谱第一眼的记分册读数 |" + _hpLockLast);
            Flush();
        }

        if (hard)
        {
            string xw, xy;
            bool ok = false;
            try { ok = SetX(uh, new[] { "_aCb" }, false, out xw, out xy); }
            catch (Exception e) { HpLockFail("写 `_aCb` 抛 " + EM(e)); return; }

            string rw, ry; object back = null;
            try { back = RVA(uh, new[] { "_aCb" }, out rw, out ry); } catch (Exception e) { ry = EM(e); }
            bool landed = false;
            try { landed = back != null && !Convert.ToBoolean(back); } catch { }

            if (landed)
            {
                _hpLockWrites++;
                _hpLockFail = null;
                if (_hpLockWrites == 1)
                {
                    _hpLockFrame1 = SceneProbe.FrameNo;
                    W(_hpLockTag + "：★ 锁血第 1 次按下去 —— 帧 " + _hpLockFrame1
                      + "（`_aCb` True → False，回读一致 ✅）");
                    Flush();
                }
            }
            else
            {
                _hpLockBad++;
                if (_hpLockBad <= 3)
                {
                    W(_hpLockTag + "：⛔ 锁血按下去**没生效**（写" + (ok ? "没抛，" : "抛了：" + xy + "，")
                      + "命中「" + (xw ?? "-") + "」，回读还是 " + Fmt(back)
                      + (ry == null ? "" : "（" + ry + "）") + "）⇒ 这一位写不动，别再往它上面使劲");
                    Flush();
                }
            }
        }

    }

        static readonly string[] HP_WATCH = new[]
    {
        "_cCb", "_CCb", "_dCb", "_fCb", "_gCb", "_hCb", "_iCb", "_NCb", "_oCb",
    };

    static int ClampIdx(float lum)
    {
        int k = (int)(lum * 9.999f);
        if (k < 0) k = 0;
        if (k > 9) k = 9;
        return k;
    }

        static float F1(object o)
    {
        if (o == null) return 0f;
        try { return Convert.ToSingle(o); } catch { return 0f; }
    }

        static object CompOf(object go, Type t)
    {
        if (go == null || t == null) return null;
        var m = MT(go, "GetComponent", typeof(Type));
        if (m == null) { W("喂歌：     ⚠ `GetComponent(Type)` 一个重载都没取到 —— **不是「没有这个组件」**"); return null; }
        object a0 = Fit(t, m.GetParameters()[0].ParameterType);
        if (a0 == null) { W("喂歌：     ⚠ `GetComponent` 的 Type 参数转换失败 —— **不是「没有这个组件」**"); return null; }
        try { return m.Invoke(go, new object[] { a0 }); }
        catch (Exception e) { W("喂歌：     ⚠ `GetComponent` 抛 " + (e.InnerException ?? e).GetType().Name + " —— **不是「没有这个组件」**"); return null; }
    }

        static bool SameObj(object a, object b)
    {
        string ia = IdOf(a), ib = IdOf(b);
        return ia != null && ia == ib;
    }

        static string IdOf(object o)
    {
        if (o == null) return null;
        object id = CallQ(o, "GetInstanceID", new object[0]);
        if (id == null) return null;
        return S(id);
    }

        static int IntOfV(object v, int dflt)
    {
        if (v == null) return dflt;
        try { return Convert.ToInt32(v); } catch { return dflt; }
    }

        static object CallQ(object o, string name, object[] args)
    {
        if (o == null) return null;
        Type t;
        try { t = o.GetType(); } catch { return null; }
        MethodInfo loose = null;
        foreach (var m in t.GetMethods(F))
        {
            if (m.Name != name || m.IsGenericMethodDefinition) continue;
            var ps = m.GetParameters();
            if (ps.Length != args.Length) continue;
            bool ok = true;
            for (int i = 0; i < ps.Length; i++)
            {
                Type pt = ps[i].ParameterType;
                if (args[i] is string) { if (pt != typeof(string)) { ok = false; break; } }
                else if (args[i] is int) { if (pt != typeof(int)) { ok = false; break; } }
                else if (!pt.IsAssignableFrom(args[i].GetType())) { ok = false; break; }
            }
            if (!ok) { if (loose == null) loose = m; continue; }
            try { return m.Invoke(o, args); } catch { return null; }
        }
        if (loose != null) { try { return loose.Invoke(o, args); } catch { } }
        return null;
    }

        static string ObjName(object o)
    {
        if (o == null) return "<null>";
        try { var v = P(o, "name"); return v == null ? "<无名>" : S(v); }
        catch (Exception e) { return "<读名字抛 " + (e.InnerException ?? e).GetType().Name + ">"; }
    }

        static object PV(object o, string n) { if (o == null) return null; return P(o, n) ?? PF(o, n); }

        static string Fmt(object v)
    {
        if (v == null) return "<null>";
        try
        {
            if (v is string sv) return "「" + sv + "」";
            if (v.GetType().IsValueType) return S(v);
        }
        catch { }
        return Kind(v) + "「" + ObjName(v) + "」";
    }

        static object Retip(object o, Type want)
    {
        if (o == null || want == null) return o;
        try { if (want.IsInstanceOfType(o)) return o; }
        catch { }
        var ob = o as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase;
        if (ob == null) return o;

        try
        {
            var m = typeof(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)
                    .GetMethod("TryCast", BindingFlags.Public | BindingFlags.Instance);
            if (m != null && m.IsGenericMethodDefinition)
            {
                object r = m.MakeGenericMethod(want).Invoke(ob, null);
                if (r != null) return r;
            }
        }
        catch { }

        try
        {
            IntPtr ptr = Il2CppInterop.Runtime.IL2CPP.Il2CppObjectBaseToPtr(ob);
            if (ptr != IntPtr.Zero) return Activator.CreateInstance(want, new object[] { ptr });
        }
        catch { }

        return o;
    }

        static string FN(object o)
    {
        if (o == null) return "<null>";
        try { var t = o.GetType(); return t.FullName ?? t.Name; } catch { return "<?>"; }
    }

        static object RV(object o, string n, out string why)
    {
        why = "对象是null";
        if (o == null) return null;
        Type t;
        try { t = o.GetType(); } catch (Exception e) { why = "GetType抛" + e.GetType().Name; return null; }
        PropertyInfo p = null; FieldInfo f = null;
        try { p = t.GetProperty(n, F); }
        catch (Exception e) { why = "GetProperty抛" + (e.InnerException ?? e).GetType().Name; }
        if (p == null)
        {
            try { f = t.GetField(n, F); }
            catch (Exception e) { why = "GetField抛" + (e.InnerException ?? e).GetType().Name; }
        }
        if (p == null && f == null) { why = "★" + t.Name + " 上没有 [" + n + "] 这个名字"; return null; }
        try
        {
            object v = p != null ? p.GetValue(o) : f.GetValue(o);
            why = v == null ? "=" : null;
            return v;
        }
        catch (Exception e) { why = "取值抛" + (e.InnerException ?? e).GetType().Name; return null; }
    }

        static string Wh(string why)
    {
        return string.IsNullOrEmpty(why) || why == "=" ? "" : "   ⚠ " + why;
    }

        static string DumpMembers(object o)
    {
        if (o == null) return "\n        （对象是 null）";
        Type t = null;
        try { t = o.GetType(); } catch { }
        if (t == null) return "\n        （GetType 抛了）";

        var flags = F | BindingFlags.DeclaredOnly;
        var sb = new StringBuilder();
        int nNull = 0, nErr = 0;

        Action<string, Func<object>> one = (nm, get) =>
        {
            object v;
            try { v = get(); }
            catch (Exception e)
            {
                nErr++;
                sb.Append("\n          ").Append(nm).Append(" = <取值抛 ")
                  .Append((e.InnerException ?? e).GetType().Name).Append('>');
                return;
            }
            if (v == null) { nNull++; return; }
            sb.Append("\n          ").Append(nm).Append(" = ").Append(Fmt(v));
        };

        foreach (var p in t.GetProperties(flags))
        {
            if (p.GetIndexParameters().Length > 0) continue;
            var pp = p;
            one(pp.Name, () => pp.GetValue(o));
        }
        foreach (var f in t.GetFields(flags))
        {
            var ff = f;
            one(ff.Name, () => ff.GetValue(o));
        }

        sb.Append("\n          （另有 ").Append(nNull).Append(" 个成员是 null");
        if (nErr > 0) sb.Append("，").Append(nErr).Append(" 个取值抛了");
        sb.Append('）');
        return sb.ToString();
    }

        static bool SameType(Type a, Type b)
    {
        if (a == null || b == null) return false;
        if (a == b) return true;
        if (a.Name != b.Name) return false;
        string x = a.Namespace ?? "", y = b.Namespace ?? "";
        return x == y || x.EndsWith(y, StringComparison.Ordinal) || y.EndsWith(x, StringComparison.Ordinal);
    }

        static bool WantIl2CppType(Type want)
    {
        return want != null && want.Name == "Type" &&
               (want.Namespace ?? "") == "Il2CppSystem";
    }

        static object Il2CppTypeOf(Type t)
    {
        if (t == null) return null;
        try
        {
            object r = Il2CppInterop.Runtime.Il2CppType.From(t);
            if (r == null) W($"        ⚠ Il2CppType.From({t.Name}) 返回了 null");
            return r;
        }
        catch (Exception e)
        {
            W("        ⚠ Il2CppType.From 抛 " + (e.InnerException ?? e).GetType().Name + ": " +
              (e.InnerException ?? e).Message);
            return null;
        }
    }

        static object Fit(object arg, Type want)
    {
        if (arg is Type mt && WantIl2CppType(want)) return Il2CppTypeOf(mt);
        return arg;
    }

        static int IncludeFirst(Type enumT)
    {
        try
        {
            foreach (var nm in Enum.GetNames(enumT))
                if (nm.IndexOf("Include", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    nm.IndexOf("Inactive", StringComparison.OrdinalIgnoreCase) < 0)
                    return Convert.ToInt32(Enum.Parse(enumT, nm));
        }
        catch { }
        return 1;
    }

        static List<object> FindAll(Type t)
    {
        var found = new List<object>();
        if (t == null) { W("        ⚠ FindAll —— 要查的那个类型没加载（T(...) 返回了 null）"); return found; }
        Type uo = T("UnityEngine.Object");
        if (uo == null) { W("        ⚠ UnityEngine.Object 没加载"); return found; }

        var tries = new[]
        {
            new object[] { "FindObjectsByType", 3 },
            new object[] { "FindObjectsOfType", 2 },
            new object[] { "FindObjectsByType", 2 },
            new object[] { "FindObjectsOfTypeAll", 1 },
        };
        int hit = 0;

        var order = new List<int>();
        if (_findPref >= 0 && _findPref < tries.Length) order.Add(_findPref);
        for (int i = 0; i < tries.Length; i++) if (i != _findPref) order.Add(i);

        foreach (var si in order)
        {
            var spec = tries[si];
            string name = (string)spec[0];
            int argc = (int)spec[1];
            foreach (var m in uo.GetMethods(FS))
            {
                if (m.Name != name || m.IsGenericMethodDefinition) continue;
                var ps = m.GetParameters();
                if (ps.Length != argc || !SameType(ps[0].ParameterType, typeof(Type))) continue;
                hit++;

                var args = new object[argc];
                args[0] = Fit(t, ps[0].ParameterType);
                if (args[0] == null) { W($"        {name}/{argc}: Type 参数转换失败，跳过"); continue; }
                for (int i = 1; i < argc; i++)
                {
                    var pt = ps[i].ParameterType;
                    if (pt.IsEnum) args[i] = Enum.ToObject(pt,
                        pt.Name.IndexOf("Inactive", StringComparison.Ordinal) >= 0 ? IncludeFirst(pt) : 0);
                    else if (pt == typeof(bool)) args[i] = true;
                    else args[i] = null;
                }
                try
                {
                    object arr = m.Invoke(null, args);
                    _findPref = si;
                    int len = Len(arr);
                    for (int i = 0; i < len; i++)
                    {
                        var o = At(arr, i);
                        if (o != null) found.Add(Retip(o, t));
                    }
                    goto done;
                }
                catch (Exception e) { W($"        {name}/{argc} 抛 {(e.InnerException ?? e).GetType().Name}"); }
                break;
            }
        }
        done:
        if (hit == 0)
            W($"        ⚠ {t.Name}: 4 种 Find 重载**一个都没匹配上**（参数类型对不上？）—— 所以「没有」这个结论不成立");
        return found;
    }

        static MethodInfo MT(object target, string name, params Type[] sig)
    {
        if (target == null) return null;
        var cand = new List<string>();
        foreach (var m in target.GetType().GetMethods(F))
        {
            if (m.Name != name || m.IsGenericMethodDefinition) continue;
            var ps = m.GetParameters();
            cand.Add(ps.Length + "参(" + string.Join(",", Array.ConvertAll(ps, x => x.ParameterType.FullName ?? x.ParameterType.Name)) + ")");
            if (ps.Length != sig.Length) continue;
            bool ok = true;
            for (int i = 0; i < sig.Length; i++)
                if (sig[i] != null && !SameType(ps[i].ParameterType, sig[i])) { ok = false; break; }
            if (ok) return m;
        }
        W($"        ⚠ {target.GetType().Name} 上没取到 {name}/{sig.Length} 参。实际有：" +
          (cand.Count == 0 ? "一个都没有" : string.Join("  ", cand)));
        return null;
    }

        static void DumpRenderers(object comp)
    {
        if (comp == null) { W("        （组件是 null）"); return; }
        object go = null;
        try { go = P(comp, "gameObject"); } catch (Exception e) { W("        gameObject 抛 " + (e.InnerException ?? e).GetType().Name); }
        if (go == null) { W("        （gameObject 读不到 —— 这不是个 Component？）"); return; }

        Type rt = T("UnityEngine.Renderer");
        if (rt == null) { W("        ⚠ UnityEngine.Renderer 类型没加载"); return; }

        object arr = null;
        string how = null;
        var m1 = MT(go, "GetComponentsInChildren", typeof(Type), typeof(bool));
        if (m1 != null)
        {
            object a0 = Fit(rt, m1.GetParameters()[0].ParameterType);
            if (a0 == null) W("        GetComponentsInChildren: Type 参数转换失败");
            else
            {
                try { arr = m1.Invoke(go, new object[] { a0, true }); how = "GetComponentsInChildren(Type,bool)"; }
                catch (Exception e) { W("        GetComponentsInChildren 抛 " + (e.InnerException ?? e).GetType().Name); }
            }
        }
        if (arr == null)
        {
            var m2 = MT(go, "GetComponents", typeof(Type));
            if (m2 != null)
            {
                object a0 = Fit(rt, m2.GetParameters()[0].ParameterType);
                if (a0 == null) W("        GetComponents: Type 参数转换失败");
                else
                {
                    try { arr = m2.Invoke(go, new object[] { a0 }); how = "GetComponents(Type)"; }
                    catch (Exception e) { W("        GetComponents 抛 " + (e.InnerException ?? e).GetType().Name); }
                }
            }
        }
        int n = Len(arr);
        W($"        渲染器 {n} 个（{how ?? "一个重载都没取到"}）");
        for (int i = 0; i < n && i < 6; i++)
        {
            object r = At(arr, i);
            object mat = P(r, "sharedMaterial");
            if (mat == null) mat = P(r, "material");
            W($"          [{i}] {Kind(r)} 「{ObjName(r)}」 material=「{ObjName(mat)}」");
            if (mat != null) W($"               shader=「{ObjName(P(mat, "shader"))}」");
        }
    }

        static void DumpChildTree(object tr, int depth)
    {
        if (tr == null || depth <= 0) return;
        string pad = new string(' ', (3 - depth) * 4 + 6);

        int n = -1;
        try { n = Convert.ToInt32(P(tr, "childCount")); } catch { }
        if (n < 0) { W(pad + "（childCount 读不到 —— 这不是 Transform？）"); return; }
        W($"{pad}子物体 {n} 个");

        var gc = MT(tr, "GetChild", typeof(int));
        for (int i = 0; i < n && i < 8; i++)
        {
            object c = null;
            try { c = gc?.Invoke(tr, new object[] { i }); } catch { }
            if (c == null) { W($"{pad}  [{i}] <取不到>"); continue; }
            W($"{pad}  [{i}] 「{ObjName(c)}」");
            object cgo = null;
            try { cgo = P(c, "gameObject"); } catch { }
            DumpCompNames(cgo, pad + "       ");
            if (depth > 1) DumpChildTree(c, depth - 1);
        }
    }

        static void DumpCompNames(object go, string pad)
    {
        if (go == null) return;
        Type ct = T("UnityEngine.Component");
        if (ct == null) { W(pad + "（UnityEngine.Component 没加载）"); return; }
        var m = MT(go, "GetComponents", typeof(Type));
        if (m == null) { W(pad + "（没有 GetComponents(Type)）"); return; }
        object a0 = Fit(ct, m.GetParameters()[0].ParameterType);
        if (a0 == null) { W(pad + "GetComponents: Type 参数转换失败"); return; }
        object arr = null;
        try { arr = m.Invoke(go, new object[] { a0 }); }
        catch (Exception e) { W(pad + "GetComponents 抛 " + (e.InnerException ?? e).GetType().Name); return; }
        int n = Len(arr);
        var sb = new StringBuilder();
        for (int i = 0; i < n && i < 16; i++) sb.Append(Kind(At(arr, i))).Append(' ');
        W($"{pad}组件 {n}: {sb}");
    }

        static void DumpVideoPlayer(object vp, int depth = 0)
    {
        if (vp == null) { W("        （null）"); return; }
        W("        " + Kind(vp) + " 「" + ObjName(vp) + "」");
        object uvp = P(vp, "videoPlayer");
        W("          videoPlayer = " + Kind(uvp) + " 「" + ObjName(uvp) + "」");
        if (uvp != null)
        {
            W("            clip = " + Kind(P(uvp, "clip")) + " 「" + ObjName(P(uvp, "clip")) + "」");
            W("            url  = 「" + S(P(uvp, "url")) + "」");
            W("            isPlaying=" + S(P(uvp, "isPlaying")) + "  isPrepared=" + S(P(uvp, "isPrepared")));
        }
        object to = P(vp, "targetOutput");
        W("          targetOutput = " + Kind(to) + " 「" + ObjName(to) + "」");

        object st = P(vp, "_rG") ?? P(vp, "_XG") ?? P(vp, "_b");
        W("          状态枚举 = " + (st == null ? "<三个名字（_rG/_XG/_b）都没读到>" : S(st)));

        object pairs = P(vp, "videoFilePairs");
        if (pairs != null)
        {
            int pn = Len(pairs);
            W($"          videoFilePairs {pn} 条（TextKey → 视频）:");
            for (int i = 0; i < pn && i < 12; i++)
            {
                object p = At(pairs, i);
                W($"            [{i}] TextKey=「{S(P(p, "TextKey"))}」  {Ref(P(p, "VideoClipAss"))}");
            }
        }

        if (uvp == null && depth < 3)
        {
            object inner = P(vp, "introC2dVideoPlayer");
            if (inner != null)
            {
                W("          └─ 这一层不是播放器，往下钻一层 ↓");
                DumpVideoPlayer(inner, depth + 1);
            }
        }
    }

}