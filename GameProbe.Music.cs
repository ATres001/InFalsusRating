using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace InFalsusRating;

public static partial class GameProbe
{

        static object _mbBg;

        static bool MbEnsureBg()
    {
        if (_mbBg != null) return true;
        Type bgT = T("_k._BG");
        if (bgT == null) return false;
        _mbBg = SP(bgT, "_RxA");
        return _mbBg != null;
    }

        static MethodInfo MbM(Type t, string name, int argc, out bool isStatic)
    {
        isStatic = false;
        if (t == null) return null;
        foreach (var fl in new[] { F, FS })
        {
            try
            {
                foreach (var m in t.GetMethods(fl))
                {
                    if (m.Name != name || m.IsGenericMethodDefinition) continue;
                    if (m.GetParameters().Length != argc) continue;
                    isStatic = m.IsStatic;
                    return m;
                }
            }
            catch { }
        }
        return null;
    }

        static int MbDja(string name, out string why)
    {
        why = null;
        if (string.IsNullOrEmpty(name)) { why = "名字是空的"; return -1; }
        bool st;
        MethodInfo dj = MbM(T("_k._BG"), "_DJA", 2, out st);
        if (dj == null) { why = "没找到 2 参的 `_DJA`"; return -1; }
        if (!st && !MbEnsureBg()) { why = "`_k._BG` 的单例还没建起来"; return -1; }
        try
        {
            var args = new object[] { name, 0 };
            object r = dj.Invoke(st ? null : _mbBg, args);
            if (S(r) != "True") { why = "报 False（游戏自己也不认这个名字）"; return -1; }
            int idx = -1;
            try { idx = Convert.ToInt32(args[1], InvCult); } catch { }
            why = " → #" + idx;
            return idx;
        }
        catch (Exception e) { why = "抛 " + EM(e); return -1; }
    }

    const ulong MbGold = 0x9E3779B97F4A7C15UL;
    const ulong MbMul1 = 0xD6E8FEB86659FD93UL;
    const ulong MbMul2 = 0xA24BAED4963EE407UL;

    static ulong MbRor(ulong x, int r) { return (x >> r) | (x << (64 - r)); }

        static ulong MbWord(long n, ulong seed)
    {
        ulong t = unchecked(MbMul1 * (ulong)n + seed);
        ulong u = t ^ MbRor(t, 47);
        ulong v = unchecked(MbMul2 * u);
        return v ^ MbRor(v, 23);
    }

        static void MbKeystream(byte[] b)
    {
        ulong seed = unchecked(MbGold * (uint)b.Length);
        for (int i = 0; i < b.Length; i++)
        {
            ulong w = MbWord(i >> 3, seed);
            b[i] ^= (byte)(w >> (8 * (i & 7)));
        }
    }

        static string MbMagic(byte[] b)
    {
        if (b == null || b.Length < 4) return "<太短>";
        string[] known = { "OggS", "RIFF", "fLaC", "FSB5", "ID3", "CRID" };
        string head;
        try { head = Encoding.ASCII.GetString(b, 0, 4); } catch { return "<?>"; }
        foreach (var m in known) if (head == m) return "★★ 魔数 =「" + m + "」✓";
        bool text = true;
        for (int i = 0; i < 4; i++) if (b[i] < 32 || b[i] > 126) { text = false; break; }
        return text ? "头 4 字节 =「" + head + "」（不是认得的魔数）" : "头 4 字节不是文本魔数";
    }

        static byte[] MbMja(byte[] data, ulong x, out string err)
    {
        err = null;
        Type fg = T("_k._FG");
        if (fg == null) { err = "`_k._FG` 类型没加载"; return null; }
        Type spanT = T("Il2CppSystem.Span`1");
        if (spanT == null) { err = "`Il2CppSystem.Span`1` 没加载"; return null; }
        MethodInfo mja = SM(fg, "_MJA", 2);
        if (mja == null) { err = "`_k._FG` 上没有 2 参的 `_MJA`"; return null; }
        try
        {
            var arr = new Il2CppStructArray<byte>(data);
            object span = Activator.CreateInstance(spanT.MakeGenericType(typeof(byte)), new object[] { arr });
            if (span == null) { err = "造 `Span<byte>` 给了 null"; return null; }
            mja.Invoke(null, new object[] { span, x });
            Span<byte> sp = arr;
            return sp.ToArray();
        }
        catch (Exception e) { err = EM(e); return null; }
    }

    static bool MbEqB(byte[] a, byte[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

        static bool MbSetAt(object list, int i, object val, out string why)
    {
        why = null;
        if (list == null) { why = "表是 null"; return false; }
        try
        {
            PropertyInfo pi = list.GetType().GetProperty("Item", F);
            if (pi == null || !pi.CanWrite) { why = "这张表没有可写的 `Item`"; return false; }
            pi.SetValue(list, val, new object[] { i });
            return true;
        }
        catch (Exception e) { why = EM(e); return false; }
    }

        static bool MbInstallPayload(int idx, string payload, long len, string tag)
    {
        if (!MbEnsureBg()) { W("     ⛔ `_k._BG` 的单例还没建起来 —— 不换"); return false; }
        Type fiT = T("Il2CppSystem.IO.FileInfo");
        if (fiT == null) { W("     ⛔ `Il2CppSystem.IO.FileInfo` 类型没加载 —— 不换"); return false; }
        object fi;
        try { fi = Activator.CreateInstance(fiT, new object[] { payload }); }
        catch (Exception ex) { W("     ⛔ 造 `FileInfo` 抛 " + EM(ex) + " —— 不换"); return false; }
        if (fi == null) { W("     ⛔ 造 `FileInfo` 给了 null —— 不换"); return false; }
        W("     [" + tag + "] `FileInfo`：FullName=" + PX(fi, "FullName") + "  Length=" + PX(fi, "Length"));

        object tx = P(_mbBg, "_TxA"), ux = P(_mbBg, "_uxA");
        string why;
        if (!MbSetAt(tx, idx, fi, out why))
        {
            W("     ⛔ 写 `_TxA[" + idx + "]` 失败：" + why + " —— **什么都没换**");
            return false;
        }
        if (!MbSetAt(ux, idx, len, out why))
        {
            W("     ⛔ 写 `_uxA[" + idx + "]` 失败：" + why
              + " —— ⚠ **半换状态**（`_TxA` 已经指过去了，长度还是旧的 ⇒ 那条链现在多半是噪音/没声）。方案：重启游戏。");
            return false;
        }
        W("     ✅ 换好了：`_TxA[" + idx + "]` →「" + payload + "」，`_uxA[" + idx + "]` = " + len);

        object nowFi = At(tx, idx);
        W("     读回来：`_TxA[i].FullName` = " + PX(nowFi, "FullName") + "   `.Length` = " + PX(nowFi, "Length")
          + "   `_uxA[i]` = " + S(At(ux, idx)));

        object dg = At(P(_mbBg, "_vxA"), idx);
        bool openNow = dg != null && P(dg, "_gXA") != null;
        W("     `_vxA[" + idx + "]`（块表）：" + (dg == null ? "**是 null**（没开过）"
            : openNow ? "非 null，`_gXA`（BufferedStream）**开着** ⇒ 此刻正有人在读这一格"
            : "非 null，但 `_gXA` 是 null（没开着）"));
        if (openNow)
            W("     ↳ ⚠ 这一格**现在开着** ⇒ **这一局听到的还是旧的那份**，得**退出这一首再进来**。");
        else
            W("     ↳ 这一格没开着 ⇒ 游戏下次开这一首（或这一局还没开始放）时**直接就是新的**（不用重进）。");
        W("     ↳ 这块表**不动**（只换数据，不碰内存里的活对象 —— 拿没验证过的东西去改它，风险是无声崩溃）。");
        return true;
    }

        internal static string MbMusicDir() { return Path.Combine(Settings.ConfigRoot, "music"); }

        internal static readonly string[] MbSrcExts = { ".ogg", ".wav", ".mp3", ".flac", ".oga", ".opus", ".m4a", ".aiff" };

        static readonly Dictionary<string, string> _mbInstalled = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        const int MB_MISS_MS = 2000;
    static string _mbMissBase, _mbMissVe;
    static int _mbMissAt = -100000;

        const int MB_FP_MS = 500;
    static int _mbFpAt = -100000;

        static void MbAttemptFailed(string baseName)
    {
        _mbMissBase = baseName;
        _mbMissVe = _mbTickVe;
        _mbMissAt = Environment.TickCount;
    }

    static string _mbSayLast;
    static string _mbTickKey;
    static string _mbTickBase;
    static string _mbTickVe;

        static void MbSay(string s)
    {
        if (s == _mbSayLast) return;
        _mbSayLast = s;
        W("     [音乐替换] " + s);
        Flush();
    }

        static string MbFinger(string path)
    {
        try
        {
            var fi = new System.IO.FileInfo(path);
            return fi.FullName + "|" + fi.Length.ToString(InvCult) + "|" + fi.LastWriteTimeUtc.Ticks.ToString(InvCult);
        }
        catch (Exception e) { return path + "|?" + EM(e); }
    }

        static string MbFindSource(string baseName, string ve)
    {
        string dir = MbMusicDir();
        string stem = null;
        if (!string.IsNullOrEmpty(ve))
        {
            string s = ve.Trim();
            int dot = s.LastIndexOf('.');
            if (dot > 0 && s.Length - dot <= 8) s = s.Substring(0, dot);
            stem = s;
        }
        for (int pass = 0; pass < 2; pass++)
        {
            string nm = pass == 0 ? baseName : stem;
            if (string.IsNullOrEmpty(nm)) continue;
            if (pass == 1 && string.Equals(nm, baseName, StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var ex in MbSrcExts)
            {
                string p = Path.Combine(dir, nm + ex);
                try { if (File.Exists(p)) return p; } catch { }
            }
        }
        return null;
    }

        static string MbMagicOfFile(string path)
    {
        try
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var buf = new byte[16];
                int n = fs.Read(buf, 0, buf.Length);
                if (n < 4) return "文件太短（" + n + " 字节）";
                return MbMagic(buf);
            }
        }
        catch (Exception e) { return "读不了头：" + EM(e); }
    }

        static string MbCurrentKeyQuiet()
    {
        object lnp = NotePlayer();
        if (lnp != null)
        {
            string v = S(P(lnp, "_Ve"));
            if (!string.IsNullOrEmpty(v) && v != "<null>") return "c:" + v;
        }
        object ss = SceneProbe.Scene;
        if (ss == null)
        {
            var l = FindAll(T("ifapp.Game.Scenes.SongSelectScene"));
            if (l.Count > 0) ss = l[0];
        }
        if (ss == null) return null;
        string sid = S(PV(ss, "_sr"));
        if (string.IsNullOrEmpty(sid) || sid == "<null>") return null;
        return "s:" + sid;
    }

        static string MbResolveBaseQuiet(string key, out string ve)
    {
        ve = null;
        if (string.IsNullOrEmpty(key) || key.Length < 3) return null;
        if (key[0] == 'c')
        {
            ve = key.Substring(2);
            object da = P(SceneProbe.Game, "dataAccess");
            if (da == null) da = P(SceneProbe.Scene, "dataAccess");
            if (da == null) da = P(SceneProbe.Results, "dataAccess");
            object song; string bn, why;
            McResolveSong(da, ve, out song, out bn, out why);
            return bn;
        }
        string sid = key.Substring(2);
        object ss = SceneProbe.Scene;
        if (ss == null)
        {
            var l = FindAll(T("ifapp.Game.Scenes.SongSelectScene"));
            if (l.Count > 0) ss = l[0];
        }
        if (ss == null) return null;
        object da2 = PV(ss, "dataAccess");
        if (da2 == null) return null;
        foreach (var s in Songs(da2))
        {
            if (S(P(s, "Id")) != sid) continue;
            string bn = S(P(s, "BaseName"));
            return (string.IsNullOrEmpty(bn) || bn == "<null>") ? null : bn;
        }
        return null;
    }

        static void MusicSwapTick()
    {
        try
        {

            string key = MbCurrentKeyQuiet();
            if (key == null) { _mbTickKey = null; _mbTickBase = null; _mbTickVe = null; return; }
            if (key != _mbTickKey)
            {
                _mbTickKey = key;
                _mbTickBase = MbResolveBaseQuiet(key, out _mbTickVe);
            }
            string baseName = _mbTickBase;
            if (string.IsNullOrEmpty(baseName)) return;

            if (baseName == _mbMissBase
                && string.Equals(_mbTickVe, _mbMissVe, StringComparison.Ordinal)
                && unchecked(Environment.TickCount - _mbMissAt) < MB_MISS_MS)
                return;

            string src = MbFindSource(baseName, _mbTickVe);
            if (src == null)
            {
                MbAttemptFailed(baseName);
                _mbInstalled.Remove(baseName);

                MbSay("这一首 =「" + baseName + "」—— 要换就把音频放成 `music\\" + baseName
                      + ".<ogg|wav|mp3|flac…>`（没有源文件，这一首保持原样）");
                return;
            }

            string have;
            bool installed = _mbInstalled.TryGetValue(baseName, out have);
            if (installed && unchecked(Environment.TickCount - _mbFpAt) < MB_FP_MS) return;
            string fp = MbFinger(src);
            if (installed && have == fp) { _mbFpAt = Environment.TickCount; return; }

            if (!MbEnsureBg())
            {
                MbSay("⚠「" + baseName + "」有源文件，但 `_k._BG` 的单例还没建起来（还没进过要流媒体的场景）—— 不换");
                MbAttemptFailed(baseName);
                return;
            }
            string why;
            int idx = MbDja(baseName + ".wav", out why);
            if (idx < 0)
            {
                int idx2 = MbDja(baseName + ".ogg", out why);
                if (idx2 < 0)
                {
                    MbSay("⚠「" + baseName + "」有源文件「" + Path.GetFileName(src)
                          + "」，但游戏的名字表里没有 `<base>.wav` 也没有 `<base>.ogg`（`_DJA` 两个都不认）—— 不换");
                    MbAttemptFailed(baseName);
                    return;
                }
                idx = idx2;
            }

            byte[] plain;
            try { plain = File.ReadAllBytes(src); }
            catch (Exception ex) { MbSay("⛔「" + baseName + "」读源文件失败（" + src + "）：" + EM(ex)); MbAttemptFailed(baseName); return; }
            if (plain.Length < 16) { MbSay("⛔「" + baseName + "」源文件只有 " + plain.Length + " 字节 —— 不换"); MbAttemptFailed(baseName); return; }
            byte[] w2 = (byte[])plain.Clone();
            MbKeystream(w2);
            string e;
            byte[] f = MbMja(w2, 0, out e);
            if (f == null) { MbSay("⛔「" + baseName + "」`_MJA` 调不通：" + e); MbAttemptFailed(baseName); return; }

            byte[] chk = (byte[])f.Clone();
            MbKeystream(chk);
            chk = MbMja(chk, 0, out e);
            if (chk == null || !MbEqB(chk, plain))
            {
                MbSay("⛔「" + baseName + "」载荷自检没过（剥回来 ≠ 源文件"
                      + (chk == null ? "：调不通 " + e : "") + "）—— 不换");
                MbAttemptFailed(baseName);
                return;
            }

            string ext = Path.GetExtension(src);
            string payload = Path.Combine(MbMusicDir(), baseName + "_swap" + ext);
            try
            {
                Directory.CreateDirectory(MbMusicDir());
                string tmp = payload + ".tmp";
                File.WriteAllBytes(tmp, f);
                File.Copy(tmp, payload, true);
                try { File.Delete(tmp); } catch { }
            }
            catch (Exception ex)
            {
                MbSay("⛔「" + baseName + "」写载荷失败（" + payload + "）：" + EM(ex) + " —— 不换");
                MbAttemptFailed(baseName);
                return;
            }
            if (!MbInstallPayload(idx, payload, f.Length, "自动")) { MbAttemptFailed(baseName); return; }
            _mbInstalled[baseName] = fp;
            _mbFpAt = Environment.TickCount;
            MbSay("★「" + baseName + "」已换上「" + Path.GetFileName(src) + "」（" + MbMagicOfFile(src)
                  + "，" + plain.Length + " 字节 ⇒ 载荷 " + f.Length + " 字节 ⇒ 条目 #" + idx + "）");
        }
        catch (Exception e)
        {
            MbSay("⚠ 自动替换抛 " + EM(e));
        }
    }
}
