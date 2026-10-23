using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using BepInEx;

namespace InFalsusRating;

public static class ExamFails
{
        public static string JsonPath => Path.Combine(
        BepInEx.Paths.ConfigPath, "InFalsusRating", "examfails.json");

        const int MaxRecords = 400;

        const ulong DupWindowSec = 2;

        public sealed class Rec
    {
        public string SongId = "";
        public string Difficulty = "";
                public ulong At;
                public bool HasStats;
        public long Break = -1, Near = -1, Exact = -1, Shiny = -1;
        public int Combo = -1;
    }

    static readonly object _lock = new object();

        static readonly List<Rec> _recs = new List<Rec>();

    static bool _dirty;

        public static void Add(string songId, string difficulty, bool hasStats,
        long brk, long near, long exact, long shiny, int combo)
    {
        if (string.IsNullOrEmpty(songId) || string.IsNullOrEmpty(difficulty)) return;

        ulong now = unchecked((ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        lock (_lock)
        {

            if (_recs.Count > 0)
            {
                var last = _recs[_recs.Count - 1];
                if (last.SongId == songId && last.Difficulty == difficulty
                    && now >= last.At && now - last.At <= DupWindowSec)
                    return;
            }

            var rec = new Rec
            {
                SongId = songId, Difficulty = difficulty, At = now,
                HasStats = hasStats,
                Break = hasStats ? brk : -1,
                Near = hasStats ? near : -1,
                Exact = hasStats ? exact : -1,
                Shiny = hasStats ? shiny : -1,
                Combo = hasStats ? combo : -1,
            };
            _recs.Add(rec);
            _dirty = true;

            int dropped = 0;
            while (_recs.Count > MaxRecords)
            {
                _recs.RemoveAt(0);
                dropped++;
            }

            Plugin.Logger.LogInfo($"[课题记录] 记下失败的一首：{songId} / {difficulty}"
                + (hasStats ? $"（实时 EXACT+ {shiny} / EXACT {exact} / NEAR {near} / BREAK {brk}、最大连击 {combo}）"
                            : "（判定数据没接上）")
                + (dropped > 0 ? $" —— 超过 {MaxRecords} 条，丢掉最老的 {dropped} 条" : ""));
        }

        Save(force: true);
    }

        public static List<Rec> Snapshot()
    {
        lock (_lock) return new List<Rec>(_recs);
    }

        public static void MergeInto(List<HistoryEntry> hist)
    {
        if (hist == null) return;
        List<Rec> snap = Snapshot();
        if (snap.Count == 0) return;

        snap.Sort((a, b) => a.At.CompareTo(b.At));

        for (int k = 0; k < snap.Count; k++)
        {
            var r = snap[k];
            var e = new HistoryEntry
            {
                SongId = r.SongId,
                Difficulty = r.Difficulty,
                Score = 0,
                Clear = 1,
                Seconds = r.At,
                Detail = null,
                TaskPlay = true,
                ExamFail = true,
                HasStats = r.HasStats,
                Break = r.Break,
                Near = r.Near,
                Exact = r.Exact,
                Shiny = r.Shiny,
                Combo = r.Combo,
            };

            int at = hist.Count;
            for (int i = 0; i < hist.Count; i++)
            {
                ulong s = hist[i].Seconds;
                if (s == 0 || s < r.At) { at = i; break; }
            }
            hist.Insert(at, e);
        }
    }

        public static void Save(bool force = false)
    {
        lock (_lock)
        {
            if (!_dirty) return;

            try
            {
                var sb = new StringBuilder(256 + _recs.Count * 96);
                sb.AppendLine("{");
                sb.AppendLine("  \"_字段\": \"recs = 课题里被打断/失败、而游戏没有写进 history 的那几首（HISTORY 页签把它们补出来）。song = 游戏曲目 id；difficulty = Minimal/Evolved/Ultimate/Forbidden；at = Unix 秒 UTC；has = 实时判定四档是否可信；break/near/exact/shiny/combo = 插件实时数下来的判定与最大连击。删掉整个文件 = HISTORY 里这些补出来的记录消失（游戏成绩本身不动）。\",");
                sb.AppendLine("  \"v\": 1,");
                sb.Append("  \"recs\": [");
                for (int i = 0; i < _recs.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    var r = _recs[i];
                    sb.Append("\n    {\"song\":\"").Append(JsonEsc(r.SongId))
                      .Append("\",\"diff\":\"").Append(JsonEsc(r.Difficulty))
                      .Append("\",\"at\":").Append(r.At)
                      .Append(",\"has\":").Append(r.HasStats ? "true" : "false")
                      .Append(",\"break\":").Append(r.Break)
                      .Append(",\"near\":").Append(r.Near)
                      .Append(",\"exact\":").Append(r.Exact)
                      .Append(",\"shiny\":").Append(r.Shiny)
                      .Append(",\"combo\":").Append(r.Combo)
                      .Append('}');
                }
                sb.AppendLine();
                sb.AppendLine("  ]");
                sb.AppendLine("}");

                Directory.CreateDirectory(Path.GetDirectoryName(JsonPath));
                File.WriteAllText(JsonPath, sb.ToString());
                _dirty = false;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[课题记录] examfails.json 写不进去（下次再试）: {ex.Message}");
            }
        }
    }

        static string JsonEsc(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

        public static void Load()
    {
        try
        {
            if (File.Exists(JsonPath)) ParseInto(File.ReadAllText(JsonPath));
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[课题记录] examfails.json 读不了（按空账本走）: {ex.Message}");
        }
    }

    static void ParseInto(string text)
    {
        using var doc = JsonDocument.Parse(text);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return;
        var root = doc.RootElement;

        if (!root.TryGetProperty("recs", out var rs) || rs.ValueKind != JsonValueKind.Array)
        {
            Plugin.Logger.LogWarning("[课题记录] examfails.json 缺 recs 数组 —— 当作没有");
            return;
        }

        var parsed = new List<Rec>();
        foreach (var e in rs.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object) continue;

            string song = e.TryGetProperty("song", out var sv) && sv.ValueKind == JsonValueKind.String
                ? sv.GetString() : "";
            string diff = e.TryGetProperty("diff", out var dv) && dv.ValueKind == JsonValueKind.String
                ? dv.GetString() : "";
            if (string.IsNullOrEmpty(song) || string.IsNullOrEmpty(diff)) continue;

            var r = new Rec { SongId = song, Difficulty = diff };
            if (e.TryGetProperty("at", out var av)) r.At = av.TryGetUInt64(out ulong at) ? at : 0;
            if (e.TryGetProperty("has", out var hv)) r.HasStats = hv.ValueKind == JsonValueKind.True;
            if (e.TryGetProperty("break", out var bv)) r.Break = ReadLong(bv);
            if (e.TryGetProperty("near", out var nv)) r.Near = ReadLong(nv);
            if (e.TryGetProperty("exact", out var ev)) r.Exact = ReadLong(ev);
            if (e.TryGetProperty("shiny", out var shv)) r.Shiny = ReadLong(shv);
            if (e.TryGetProperty("combo", out var cv)) r.Combo = (int)ReadLong(cv);

            if (!r.HasStats) { r.Break = r.Near = r.Exact = r.Shiny = -1; r.Combo = -1; }
            parsed.Add(r);
        }

        parsed.Sort((a, b) => a.At.CompareTo(b.At));

        lock (_lock)
        {
            _recs.Clear();
            _dirty = false;
            int start = Math.Max(0, parsed.Count - MaxRecords);
            for (int i = start; i < parsed.Count; i++) _recs.Add(parsed[i]);
            Plugin.Logger.LogInfo($"[课题记录] examfails.json: {_recs.Count} 条课题失败记录"
                + (start > 0 ? $"（另有 {start} 条超过上限已忽略）" : ""));
        }
    }

    static long ReadLong(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Number) return -1;
        if (e.TryGetInt64(out long v)) return v;
        if (e.TryGetUInt64(out ulong u)) return unchecked((long)u);
        return -1;
    }
}
