using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using BepInEx;

namespace InFalsusRating;

public static class TaskMarks
{
        public static string JsonPath => Path.Combine(
        BepInEx.Paths.ConfigPath, "InFalsusRating", "taskmarks.json");

        const int SaveMinMs = 1000;

        readonly struct Range
    {
        public readonly int Start;
        public readonly int End;
        public Range(int s, int e) { Start = s; End = e; }
    }

    static readonly object _lock = new object();

        static readonly List<Range> _ranges = new List<Range>();

        static bool _active;

        static int _startN = -1;

        static int _seen;

    static bool _dirty;
    static int _lastSaveTick = unchecked(Environment.TickCount - SaveMinMs);

        public static void Begin(int historyLength)
    {
        bool closed = false;
        lock (_lock)
        {

            if (_active) closed = CloseActiveLocked(_seen);

            _active = true;
            if (historyLength >= 0)
            {
                if (historyLength > _seen) _seen = historyLength;
                _startN = historyLength;
            }
            else
            {

                _startN = _seen;
            }
        }

        if (closed) Save(force: true);
    }

        public static void End(int historyLength)
    {
        lock (_lock)
        {
            if (!_active) return;

            if (historyLength >= 0 && historyLength < _seen)
            {

                _ranges.Clear();
                _seen = historyLength;
            }
            else
            {
                CloseActiveLocked(historyLength >= 0 ? historyLength : _seen);
                if (historyLength >= 0) _seen = historyLength;
            }

            _active = false;
            _startN = -1;
        }

        Save(force: true);
    }

        static bool CloseActiveLocked(int endN)
    {
        int s = _startN >= 0 ? _startN : _seen;
        return AppendRange(s, endN);
    }

        public static void Observe(int count)
    {
        if (count < 0) return;

        bool changed = false;
        lock (_lock)
        {
            if (count < _seen)
            {

                if (_ranges.Count > 0)
                {
                    _ranges.Clear();
                    changed = true;
                    Plugin.Logger.LogInfo("[段位记录] history 变短 —— 旧的挑战区间已作废（下标重新对齐）");
                }
                _seen = count;
                if (_active) _startN = count;
            }
            else
            {
                if (_active)
                {
                    if (_startN < 0) _startN = _seen;
                    changed = AppendRange(_startN, count);
                }
                if (count > _seen) _seen = count;
            }
        }

        if (changed) Save(force: false);
    }

        public static bool Contains(int index)
    {
        if (index < 0) return false;
        lock (_lock)
        {
            int lo = 0, hi = _ranges.Count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                var r = _ranges[mid];
                if (index < r.Start) hi = mid - 1;
                else if (index >= r.End) lo = mid + 1;
                else return true;
            }
        }
        return false;
    }

        static bool AppendRange(int s, int e)
    {
        if (e <= s) return false;

        if (_ranges.Count > 0)
        {
            int last = _ranges.Count - 1;
            var r = _ranges[last];
            if (s <= r.End)
            {
                int ns = Math.Min(r.Start, s);
                int ne = Math.Max(r.End, e);
                if (ns == r.Start && ne == r.End) return false;
                _ranges[last] = new Range(ns, ne);
                return true;
            }
        }

        _ranges.Add(new Range(s, e));
        return true;
    }

        public static void Save(bool force = false)
    {
        lock (_lock)
        {
            if (!_dirty) return;
            if (!force && unchecked(Environment.TickCount - _lastSaveTick) < SaveMinMs) return;

            try
            {
                var sb = new StringBuilder(256 + _ranges.Count * 16);
                sb.AppendLine("{");
                sb.AppendLine("  \"_字段\": \"ranges = 每场课题/段位挑战在 history 里的下标区间 [起, 止)：落在这里面的记录就是挑战期间打的（成绩页 HISTORY 页签画框）。seen = 账本已经对齐到的 history 条数。删掉整个文件 = 之前那些挑战标记全部消失（游戏成绩本身不动）。\",");
                sb.AppendLine("  \"v\": 1,");
                sb.AppendLine($"  \"seen\": {_seen},");
                sb.Append("  \"ranges\": [");
                for (int i = 0; i < _ranges.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append('[').Append(_ranges[i].Start).Append(',').Append(_ranges[i].End).Append(']');
                }
                sb.AppendLine("]");
                sb.AppendLine("}");

                Directory.CreateDirectory(Path.GetDirectoryName(JsonPath));
                File.WriteAllText(JsonPath, sb.ToString());
                _dirty = false;
                _lastSaveTick = Environment.TickCount;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[段位记录] taskmarks.json 写不进去（下次再试）: {ex.Message}");
            }
        }
    }

        public static void Load()
    {
        try
        {
            if (File.Exists(JsonPath)) ParseInto(File.ReadAllText(JsonPath));
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[段位记录] taskmarks.json 读不了（按空账本走）: {ex.Message}");
        }
    }

    static void ParseInto(string text)
    {
        using var doc = JsonDocument.Parse(text);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return;
        var root = doc.RootElement;

        if (!root.TryGetProperty("ranges", out var rs) || rs.ValueKind != JsonValueKind.Array)
        {
            Plugin.Logger.LogWarning("[段位记录] taskmarks.json 缺 ranges 数组 —— 当作没有");
            return;
        }

        var parsed = new List<Range>();
        foreach (var e in rs.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() != 2) continue;
            if (!e[0].TryGetInt32(out int s) || !e[1].TryGetInt32(out int t)) continue;
            if (s < 0 || t <= s) continue;
            parsed.Add(new Range(s, t));
        }

        parsed.Sort((a, b) => a.Start.CompareTo(b.Start));

        lock (_lock)
        {
            _ranges.Clear();
            _active = false;
            _startN = -1;
            _dirty = false;

            int lastEnd = 0;
            foreach (var r in parsed)
            {
                if (_ranges.Count > 0)
                {
                    int last = _ranges.Count - 1;
                    var prev = _ranges[last];
                    if (r.Start <= prev.End)
                    {
                        if (r.End > prev.End) _ranges[last] = new Range(prev.Start, r.End);
                        if (r.End > lastEnd) lastEnd = r.End;
                        continue;
                    }
                }
                _ranges.Add(r);
                if (r.End > lastEnd) lastEnd = r.End;
            }

            _seen = lastEnd;
            if (root.TryGetProperty("seen", out var sv) && sv.TryGetInt32(out int seen) && seen > _seen)
                _seen = seen;

            int marked = 0;
            foreach (var r in _ranges) marked += r.End - r.Start;
            Plugin.Logger.LogInfo($"[段位记录] taskmarks.json: {_ranges.Count} 场挑战、共 {marked} 条记录");
        }
    }
}
