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

public static class SongInfoCache
{
    private static Dictionary<string, string> _songIdToName = new Dictionary<string, string>();
    private static bool _loaded = false;

    private static Dictionary<string, string> _baseNameById = new Dictionary<string, string>();
    private static Dictionary<string, string> _titleById    = new Dictionary<string, string>();
    private static Dictionary<string, string> _artistById   = new Dictionary<string, string>();
    private static bool _catalogLoaded = false;

        private static HashSet<int> _selectable = new HashSet<int>();

        public static bool Selectable(int songId)
        => _selectable.Count == 0 || _selectable.Contains(songId);

        public static int SelectableCount => _selectable.Count;

        public static List<int> SelectableIds()
    {
        var set = new SortedSet<int>();
        if (_selectable.Count > 0)
        {
            foreach (int id in _selectable) set.Add(id);
        }
        else
        {
            foreach (var k in _titleById.Keys) if (int.TryParse(k, out int a)) set.Add(a);
            foreach (var k in _songIdToName.Keys) if (int.TryParse(k, out int b)) set.Add(b);
        }
        return new List<int>(set);
    }

    static string CatalogPath => Path.Combine(Paths.ConfigPath, "InFalsusRating", "catalog.json");

    public static void LoadCatalog()
    {
        if (_catalogLoaded) return;
        _catalogLoaded = true;

        try
        {
            if (!File.Exists(CatalogPath))
            {
                Plugin.Logger.LogInfo($"[Catalog] 未找到 catalog.json，使用游戏内 dump 数据");
                return;
            }

            string json = File.ReadAllText(CatalogPath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return;

            if (!doc.RootElement.TryGetProperty("songs", out var songs)
                || songs.ValueKind != JsonValueKind.Array)
                return;

            foreach (var song in songs.EnumerateArray())
            {
                if (!song.TryGetProperty("id", out var idEl)) continue;

                string id = idEl.ValueKind switch
                {
                    JsonValueKind.Number => idEl.GetInt32().ToString(),
                    JsonValueKind.String => idEl.GetString(),
                    _ => null,
                };
                if (string.IsNullOrEmpty(id)) continue;
                if (int.TryParse(id, out int sid)) _selectable.Add(sid);

                if (song.TryGetProperty("base_name", out var bn) && bn.ValueKind == JsonValueKind.String)
                    _baseNameById[id] = bn.GetString();

                if (song.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String)
                    _titleById[id] = t.GetString();

                if (song.TryGetProperty("artist", out var a) && a.ValueKind == JsonValueKind.String)
                    _artistById[id] = a.GetString();
            }

            Plugin.Logger.LogInfo($"[Catalog] 载入 {_titleById.Count} 条曲目元数据（选歌列表里的 {_selectable.Count} 首）");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[Catalog] 读取失败: {ex.Message}");
        }
    }

        static object AllSongInfo(object nhInstance)
    {
        if (nhInstance == null) return null;
        var vnaMethod = nhInstance.GetType().GetMethod("_VNA",
            BindingFlags.Public | BindingFlags.Instance);
        var dataAccess = vnaMethod?.Invoke(nhInstance, null);
        if (dataAccess == null) return null;

        var sdProp = dataAccess.GetType().GetProperty("SongData",
            BindingFlags.Public | BindingFlags.Instance);
        var songData = sdProp?.GetValue(dataAccess);
        if (songData == null) return null;

        var getter = songData.GetType().GetMethod("get_allSongInfo",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        return getter?.Invoke(songData, null);
    }

    static string _unlockSaid;
    static bool   _unlockWarned;

        public static void ScanUnlocks()
    {
        var scene = SceneProbe.Scene;
        if (scene == null) return;

        try
        {
            object songs = PropOf(scene, "_Qr");
            int sn = LenOf(songs);
            if (songs == null || sn <= 0)
            {
                SongUnlock.Publish(null, null, null, false, "选歌页的 `_Qr` 是空的");
                return;
            }

            var rnM = scene.GetType().GetMethod("_RN",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (rnM == null)
            {
                SongUnlock.Publish(null, null, null, false, "选歌场景上没有 `_RN` 这个方法（游戏更新了？）");
                return;
            }

            var seen   = new HashSet<int>();
            var exists = new HashSet<string>();
            var avail  = new HashSet<string>();

            var recorded = new HashSet<string>();
            var ratings = Plugin.ChartRatings;
            if (ratings != null)
            {
                foreach (var r in ratings)
                {
                    if (r == null || !int.TryParse(r.SongId, out int rid)) continue;
                    recorded.Add(SongUnlock.Key(rid, r.Difficulty));
                }
            }

            int contradicted = 0;
            int unknown = 0;
            var contraSample = new List<string>();
            var openSample = new List<string>();

            for (int i = 0; i < sn; i++)
            {
                object song = ItemOf(songs, i);
                if (song == null) continue;

                string idStr = PropOf(song, "Id")?.ToString();
                if (string.IsNullOrEmpty(idStr) || !int.TryParse(idStr, out int id)) continue;

                object charts = PropOf(song, "ChartInfos");
                int cn = LenOf(charts);
                if (charts == null || cn < 0) continue;
                seen.Add(id);

                for (int j = 0; j < cn; j++)
                {
                    object ci = ItemOf(charts, j);
                    if (ci == null) continue;

                    object diffObj = PropOf(ci, "Difficulty");
                    string diff = diffObj?.ToString();
                    if (string.IsNullOrEmpty(diff)) continue;

                    int code = RnCode(rnM, scene, song, diffObj);
                    if (code < 0) continue;

                    string key = SongUnlock.Key(id, diff);

                    if (code == 2)
                    {
                        exists.Add(key);
                        avail.Add(key);
                        if (openSample.Count < 12) openSample.Add($"{GetName(idStr)} {key}");
                        continue;
                    }

                    if (code == 0) { unknown++; continue; }

                    exists.Add(key);
                    if (recorded.Contains(key))
                    {
                        contradicted++;
                        if (contraSample.Count < 8) contraSample.Add($"{GetName(idStr)} {key}");
                    }
                }
            }

            bool trusted = contradicted == 0 && avail.Count > 0;
            string why;
            if (contradicted > 0)
                why = $"有 {contradicted} 张**玩家明明打过**的谱面被说成没解锁（{string.Join("、", contraSample)}）"
                    + " —— `_RN` 不是解锁标记";
            else if (avail.Count == 0)
                why = "一张已解锁的谱面都没扫到（`_RN` 全是未解锁）—— 任何存档都不该是这样，不认";
            else
                why = $"{seen.Count} 首 / {exists.Count} 张里已解锁 {avail.Count} 张";

            SongUnlock.Publish(seen, exists, avail, trusted, why);

            string line = $"[Unlock] 曲 {SongUnlock.SeenSongs}/谱 {SongUnlock.ChartCount}"
                + $"/已解锁 {SongUnlock.UnlockedCharts}"
                + $" —— 没解锁 {SongUnlock.ChartCount - SongUnlock.UnlockedCharts} 张"
                + (unknown > 0 ? $"，另有 {unknown} 张 `_TZA`(0) 语义不明、已放行" : "")
                + (openSample.Count > 0 ? $"，例：{string.Join("、", openSample)}" : "")
                + (recorded.Count > 0
                    ? $"（自检基准 {recorded.Count} 张成绩）"
                    : " ⚠ 自检基准是 0 张成绩（还没拿到玩家的成绩表）—— 这一轮「有成绩的谱没被锁」这条没验到")
                + $" ⇒ {(trusted ? "认这份数据，闸生效" : "不认，一律放行")}（{why}）";
            if (line != _unlockSaid)
            {
                _unlockSaid = line;
                Plugin.Logger.LogInfo(line);
            }

            if (!trusted && !_unlockWarned)
            {
                _unlockWarned = true;
                Plugin.Logger.LogWarning("[Unlock] 曲单闸这一轮**不生效**（原因见上一行）。"
                    + " 这不影响开考 —— 只是「曲单没解锁就不让开」这条规则暂时没在管。");
            }
            else if (trusted)
            {
                _unlockWarned = false;
            }
        }
        catch (Exception ex)
        {
            SongUnlock.Publish(null, null, null, false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    static object PropOf(object o, string name)
    {
        if (o == null) return null;
        try
        {
            return o.GetType()
                .GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(o);
        }
        catch { return null; }
    }

        static int LenOf(object o)
    {
        if (o == null) return -1;
        var t = o.GetType();
        const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        try
        {
            var p = t.GetProperty("Length", F) ?? t.GetProperty("Count", F);
            if (p != null) return Convert.ToInt32(p.GetValue(o));
            var f = t.GetField("Length", F) ?? t.GetField("Count", F);
            if (f != null) return Convert.ToInt32(f.GetValue(o));
        }
        catch { }
        return -1;
    }

    static object ItemOf(object o, int i)
    {
        if (o == null || i < 0) return null;
        try
        {
            var t = o.GetType();
            var p = t.GetProperty("Item", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                 ?? t.GetProperty("get_Item", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return p?.GetValue(o, new object[] { i });
        }
        catch { return null; }
    }

        static int RnCode(MethodInfo m, object scene, object song, object diff)
    {
        try
        {
            object v = m.Invoke(scene, new object[] { song, diff });
            return v == null ? -1 : Convert.ToInt32(v);
        }
        catch { return -1; }
    }

    public static void Load(object nhInstance)
    {
        if (_loaded) return;
        _loaded = true;

        try
        {
            var allSongsObj = AllSongInfo(nhInstance);
            if (allSongsObj == null) { Plugin.Logger.LogWarning("[Cache] 全曲库读不到（_VNA/SongData/allSongInfo 某一环断了）"); return; }
            if (allSongsObj == null) { Plugin.Logger.LogWarning("[Cache] allSongInfo null"); return; }

            var arrType = allSongsObj.GetType();
            var lenProp = arrType.GetProperty("Length");
            var itemProp = arrType.GetProperty("Item");
            int len = Convert.ToInt32(lenProp.GetValue(allSongsObj));

            var constDict = new Dictionary<string, int>();

            for (int i = 0; i < len; i++)
            {
                object songObj = null;
                try { songObj = itemProp.GetValue(allSongsObj, new object[] { i }); }
                catch { continue; }
                if (songObj == null) continue;

                var songType = songObj.GetType();
                var idProp   = songType.GetProperty("Id",         BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var nameProp = songType.GetProperty("BaseName",   BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var ciProp   = songType.GetProperty("ChartInfos", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                string songId = null, baseName = null;
                try { songId = idProp?.GetValue(songObj)?.ToString(); } catch { }
                try { baseName = nameProp?.GetValue(songObj)?.ToString(); } catch { }

                if (!string.IsNullOrEmpty(songId) && !_songIdToName.ContainsKey(songId))
                    _songIdToName[songId] = baseName ?? songId;

                if (string.IsNullOrEmpty(songId) || ciProp == null) continue;
                object ciArrObj = null;
                try { ciArrObj = ciProp.GetValue(songObj); } catch { }
                if (ciArrObj == null) continue;

                var ciArrType = ciArrObj.GetType();
                var ciLenProp = ciArrType.GetProperty("Length");
                var ciItemProp = ciArrType.GetProperty("Item");
                if (ciLenProp == null || ciItemProp == null) continue;

                int ciLen = Convert.ToInt32(ciLenProp.GetValue(ciArrObj));
                for (int j = 0; j < ciLen; j++)
                {
                    object ci = null;
                    try { ci = ciItemProp.GetValue(ciArrObj, new object[] { j }); }
                    catch { continue; }
                    if (ci == null) continue;

                    var ciType = ci.GetType();
                    var diffProp   = ciType.GetProperty("Difficulty", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    var ratingProp = ciType.GetProperty("Rating",     BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                    string diffStr = null;
                    int rating = 0;
                    try { diffStr = diffProp?.GetValue(ci)?.ToString(); } catch { }
                    try { rating = Convert.ToInt32(ratingProp?.GetValue(ci) ?? 0); } catch { }

                    if (string.IsNullOrEmpty(diffStr)) continue;

                    string key = $"{songId}|{diffStr}";
                    if (!constDict.ContainsKey(key))
                        constDict[key] = rating;
                }
            }

            Plugin.Logger.LogInfo($"[Cache] 曲名 {_songIdToName.Count} 首，定数 {constDict.Count} 条");
            ConstTable.SaveIfIncomplete(constDict);
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[Cache] 加载失败: {ex.Message}");
        }

    }

    public static string GetName(string songId)
    {
        if (_titleById.TryGetValue(songId, out var t) && !string.IsNullOrEmpty(t))
            return t;
        return _songIdToName.TryGetValue(songId, out var name) ? name : songId;
    }

    public static string GetBaseName(string songId)
    {
        if (_baseNameById.TryGetValue(songId, out var bn) && !string.IsNullOrEmpty(bn))
            return bn;
        return _songIdToName.TryGetValue(songId, out var name) ? name : songId;
    }

        public static string BaseFromVe(string ve)
    {
        if (string.IsNullOrEmpty(ve)) return null;
        string s = ve.Trim();
        int dot = s.LastIndexOf('.');
        if (dot > 0 && s.Length - dot <= 8) s = s.Substring(0, dot);
        if (s.Length == 0) return null;
        string best = null;
        foreach (var kv in _baseNameById)
        {
            string bn = kv.Value;
            if (string.IsNullOrEmpty(bn) || bn.Length > s.Length) continue;
            if (string.Compare(s, 0, bn, 0, bn.Length, StringComparison.OrdinalIgnoreCase) != 0) continue;
            bool digitsOk = true;
            for (int i = bn.Length; i < s.Length; i++)
                if (s[i] < '0' || s[i] > '9') { digitsOk = false; break; }
            if (!digitsOk) continue;
            if (best == null || bn.Length > best.Length) best = bn;
        }
        return best;
    }

    public static string GetArtist(string songId)
    {
        if (_artistById.TryGetValue(songId, out var a) && !string.IsNullOrEmpty(a))
            return a;
        return "";
    }
}