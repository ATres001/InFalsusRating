using System;
using System.IO;
using System.Text;
using BepInEx;

namespace InFalsusRating;

public static partial class GameProbe
{

    static void McResolveSong(object da, string ve, out object song, out string baseName, out string why)
    {
        song = null; baseName = null; why = null;
        if (string.IsNullOrEmpty(ve) || ve == "<null>") { why = "`_Ve` 读不到（拿不到谱面标签）"; return; }
        string s = ve.Trim();
        int dot = s.LastIndexOf('.');
        if (dot > 0 && s.Length - dot <= 8) s = s.Substring(0, dot);

        string cat = SongInfoCache.BaseFromVe(ve);
        int scanned = 0;
        foreach (var so in Songs(da))
        {
            scanned++;
            string bn = S(P(so, "BaseName"));
            if (string.IsNullOrEmpty(bn) || bn == "<null>" || bn.Length > s.Length) continue;
            if (string.Compare(s, 0, bn, 0, bn.Length, StringComparison.OrdinalIgnoreCase) != 0) continue;
            bool ok = true;
            for (int i = bn.Length; i < s.Length; i++)
                if (s[i] < '0' || s[i] > '9') { ok = false; break; }
            if (!ok) continue;
            if (baseName == null || bn.Length > baseName.Length) { song = so; baseName = bn; }
        }
        if (song != null)
        {
            if (cat != null && cat != baseName)
                W("    ⚠ 曲名两条路不一致：catalog 说「" + cat + "」，游戏全曲库说「" + baseName
                  + "」—— 以游戏那条为准");
            return;
        }
        if (cat != null)
        {
            baseName = cat;
            why = "catalog 认 base「" + cat + "」，但游戏全曲库（扫了 " + scanned + " 首）里没有 BaseName 相符的 SongInfo";
            return;
        }
        why = "游戏全曲库（扫了 " + scanned + " 首）里没有 BaseName 是「" + s + "」前缀的曲子，catalog 也没认出来";
    }

    static void Flush()
    {
        try
        {
            string dir = Path.Combine(Paths.ConfigPath, "InFalsusRating");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "GameProbe.txt"), _o.ToString(), new UTF8Encoding(false));
        }
        catch (Exception e) { Plugin.Logger.LogWarning("[Probe] 写文件失败: " + e.Message); }
    }
}
