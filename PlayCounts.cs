using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using BepInEx;
using ifapp.Game.Data;

namespace InFalsusRating;

public static class PlayCounts
{
    public static string JsonPath => Path.Combine(Settings.ConfigRoot, "plays.json");

    const int SaveMinMs = 1500;

    static readonly object _lock = new object();

    static Dictionary<string, int> _counts = new Dictionary<string, int>(StringComparer.Ordinal);

    static Dictionary<string, int> _bySong = new Dictionary<string, int>(StringComparer.Ordinal);

    static int _seen;

    static int _highWater;

    static int _total;

    static bool _dirty;

    static int _lastSaveTick = unchecked(Environment.TickCount - SaveMinMs);

    public static int For(string songId, string diff)
    {
        if (string.IsNullOrEmpty(songId) || string.IsNullOrEmpty(diff)) return 0;
        lock (_lock)
        {
            if (_counts.TryGetValue(songId + "|" + diff, out int n)) return n;

            foreach (var kv in _counts)
                if (kv.Key.Length == songId.Length + 1 + diff.Length
                    && string.Compare(kv.Key, 0, songId, 0, songId.Length, StringComparison.OrdinalIgnoreCase) == 0
                    && kv.Key[songId.Length] == '|'
                    && string.Compare(kv.Key, songId.Length + 1, diff, 0, diff.Length,
                        StringComparison.OrdinalIgnoreCase) == 0)
                    return kv.Value;
            return 0;
        }
    }

    public static int SongPlays(string songId)
    {
        if (string.IsNullOrEmpty(songId)) return 0;
        lock (_lock) return _bySong.TryGetValue(songId, out int n) ? n : 0;
    }

    public static int TotalPlays
    {
        get
        {
            lock (_lock) return _total;
        }
    }

    public static int TrackedCharts
    {
        get
        {
            lock (_lock) return _counts.Count;
        }
    }

    public static int SeenCount
    {
        get
        {
            lock (_lock) return _seen;
        }
    }

    public static void Snapshot(out List<KeyValuePair<string, int>> counts, out int highWater)
    {
        lock (_lock)
        {
            counts = new List<KeyValuePair<string, int>>(_counts.Count);
            foreach (var kv in _counts) counts.Add(kv);
            highWater = _highWater;
        }
    }

    public static void Init()
    {
        ExternalSaveManager.RegisterFile(JsonPath, Load);
    }

    public static void Load()
    {
        try
        {
            if (File.Exists(JsonPath)) ParseInto(File.ReadAllText(JsonPath));
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[游玩次数] plays.json 读不了（按空账本走，会从 history 重建）: {ex.Message}");
        }
    }

    public static void Observe(List<GameResultV4> all)
    {
        if (all == null) return;

        bool changed = false;
        int n = all.Count;

        try
        {
            lock (_lock)
            {
                if (n == _seen) return;

                if (n < _seen)
                {
                    Plugin.Logger.LogWarning(
                        $"[游玩次数] history 变小了（{_seen} → {n}）—— 已重新对齐，已有的次数一个不改");
                    _seen = n;
                    return;
                }

                for (int i = _seen; i < n; i++)
                {
                    var r = all[i];
                    string song = r.SongId.ToString();
                    string diff = r.Difficulty.ToString();
                    if (string.IsNullOrEmpty(song) || string.IsNullOrEmpty(diff)) continue;

                    string key = song + "|" + diff;
                    _counts.TryGetValue(key, out int c);
                    _counts[key] = c + 1;
                    _bySong.TryGetValue(song, out int s);
                    _bySong[song] = s + 1;
                    _total++;
                }

                bool seeded = _seen == 0;
                _seen = n;
                if (n > _highWater) _highWater = n;
                _dirty = true;
                changed = true;

                if (seeded)
                    Plugin.Logger.LogInfo(
                        $"[游玩次数] 首次建账：history {n} 条全数入账 ⇒ 共 {_total} 次、{_counts.Count} 张谱");
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[游玩次数] 入账失败（这一次跳过）: {ex.Message}");
            return;
        }

        if (changed) Save();
    }

    public static void Save(bool force = false)
    {
        lock (_lock)
        {
            if (!_dirty) return;
            if (!force && unchecked(Environment.TickCount - _lastSaveTick) < SaveMinMs) return;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine(
                    "  \"_字段\": \"每张谱打了多少次（游玩次数）。seen = 已经入过账的 history 条数，删掉这个键会让整个 history 重新入账（次数不变大也不变小，只是重算一遍）。highWater = 见过的 history 最大条数（只涨不落，等级首次折算兜底用）。\",");
                sb.AppendLine("  \"v\": 1,");
                sb.AppendLine($"  \"seen\": {_seen},");
                sb.AppendLine($"  \"highWater\": {_highWater},");
                sb.AppendLine("  \"counts\": {");

                var keys = new List<string>(_counts.Keys);
                keys.Sort(StringComparer.Ordinal);
                for (int i = 0; i < keys.Count; i++)
                {
                    string k = keys[i];
                    sb.Append("    \"").Append(Esc(k)).Append("\": ").Append(_counts[k]);
                    sb.AppendLine(i + 1 < keys.Count ? "," : "");
                }

                sb.AppendLine("  }");
                sb.AppendLine("}");

                Directory.CreateDirectory(Path.GetDirectoryName(JsonPath));
                File.WriteAllText(JsonPath, sb.ToString());
                _dirty = false;
                _lastSaveTick = Environment.TickCount;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[游玩次数] plays.json 写不进去（下次再试）: {ex.Message}");
            }
        }
    }

    static void ParseInto(string text)
    {
        using var doc = JsonDocument.Parse(text);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return;
        var root = doc.RootElement;

        lock (_lock)
        {
            _counts.Clear();
            _bySong.Clear();
            _total = 0;

            _seen = 0;
            _highWater = 0;
            _dirty = false;

            int seen = 0;
            bool seenOk = root.TryGetProperty("seen", out var sv) && sv.ValueKind == JsonValueKind.Number
                                                                  && sv.TryGetInt32(out seen) && seen >= 0;
            if (seenOk) _seen = seen;

            if (root.TryGetProperty("highWater", out var hv) && hv.ValueKind == JsonValueKind.Number
                                                             && hv.TryGetInt32(out int hw) && hw >= 0)
                _highWater = Math.Max(hw, _seen);
            else
                _highWater = _seen;

            bool countsOk = root.TryGetProperty("counts", out var cs) && cs.ValueKind == JsonValueKind.Object;

            bool countsNonEmpty = false;
            if (countsOk)
                foreach (var p in cs.EnumerateObject())
                {
                    countsNonEmpty = true;
                    break;
                }

            bool trusted = countsOk && seenOk && !(_seen == 0 && countsNonEmpty);
            if (countsOk && !trusted)
            {
                Plugin.Logger.LogWarning(
                    "[游玩次数] counts 与 seen 对不上（seen 缺失/非法）—— 计数视为不可信，等下次 Observe 从 history 重建");
                return;
            }

            if (countsOk)
            {
                foreach (var p in cs.EnumerateObject())
                {
                    if (p.Value.ValueKind != JsonValueKind.Number) continue;
                    if (!p.Value.TryGetInt32(out int c) || c <= 0) continue;

                    string key = p.Name;
                    int bar = key.IndexOf('|');
                    if (bar <= 0 || bar >= key.Length - 1) continue;

                    _counts[key] = c;
                    string song = key.Substring(0, bar);
                    _bySong.TryGetValue(song, out int s);
                    _bySong[song] = s + c;
                    _total += c;
                }
            }
        }

        Plugin.Logger.LogInfo(
            $"[游玩次数] plays.json: {_total} 次 / {_counts.Count} 张谱 / 对齐到第 {_seen} 条");
    }

    static string Esc(string s)
    {
        if (s.IndexOfAny(new[] { '"', '\\', '\n', '\r', '\t' }) < 0) return s;
        var sb = new StringBuilder(s.Length + 8);
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }

        return sb.ToString();
    }
}