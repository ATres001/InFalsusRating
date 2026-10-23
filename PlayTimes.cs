using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using BepInEx;
using ifapp.Game.Data;

namespace InFalsusRating;

public static class PlayTimes
{
        public static string JsonPath => Path.Combine(
        BepInEx.Paths.ConfigPath, "InFalsusRating", "playtimes.json");

        const int SaveMinMs = 1500;

    static readonly object _lock = new object();

        static ulong[] _stamps = Array.Empty<ulong>();

        static Dictionary<string, ulong> _bySig = new Dictionary<string, ulong>(StringComparer.Ordinal);

        static int _seen;

        static bool _inited;

    static bool _dirty;
    static int _lastSaveTick = unchecked(Environment.TickCount - SaveMinMs);

        static readonly StringBuilder _sb = new StringBuilder(64);

        public static ulong StampAt(int index, in GameResultV4 r)
    {
        ulong s = r.SecondsSinceEpochUtc;
        if (s != 0) return s;
        lock (_lock)
        {
            if (index >= 0 && index < _stamps.Length) return _stamps[index];
        }
        return 0;
    }

        public static ulong StampFor(in GameResultV4 r)
    {
        ulong s = r.SecondsSinceEpochUtc;
        if (s != 0) return s;
        try
        {
            lock (_lock)
            {
                return _bySig.TryGetValue(Sig(r), out ulong v) ? v : 0;
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[游玩时间] 反查时间失败（这一次按 0 画）: {ex.Message}");
            return 0;
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

                if (_inited && n == _seen) return;

                if (!_inited || n < _seen)
                {

                    var arr = new ulong[n];
                    for (int i = 0; i < n; i++) arr[i] = all[i].SecondsSinceEpochUtc;
                    _stamps = arr;
                    _seen = n;
                    _inited = true;
                    RebuildSigMap(all);
                    _dirty = true;
                    changed = true;

                    if (_seen > 0)
                        Plugin.Logger.LogInfo(
                            $"[游玩时间] 账本对齐到第 {n} 条（老记录没有时间戳，之后新打的才记）");
                }
                else if (_stamps.Length == 0 && n > 4)
                {

                    var arr = new ulong[n];
                    for (int i = 0; i < n; i++) arr[i] = all[i].SecondsSinceEpochUtc;
                    _stamps = arr;
                    _seen = n;
                    RebuildSigMap(all);
                    _dirty = true;
                    changed = true;
                    Plugin.Logger.LogInfo(
                        $"[游玩时间] history 一次性出现 {n} 条（疑似装载完成）—— 只对齐，不记时间");
                }
                else
                {

                    ulong now = unchecked((ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                    int old = _stamps.Length;
                    Array.Resize(ref _stamps, n);
                    int stamped = 0;
                    for (int i = _seen; i < n; i++)
                    {
                        ulong s = all[i].SecondsSinceEpochUtc;
                        if (s == 0) { s = now; stamped++; }
                        _stamps[i] = s;
                    }
                    _seen = n;
                    RebuildSigMap(all);
                    _dirty = true;
                    changed = true;

                    if (stamped > 0)
                        Plugin.Logger.LogInfo(
                            $"[游玩时间] 新记 {stamped} 条（第 {old} → {n} 条）");
                }
            }
        }
        catch (Exception ex)
        {

            Plugin.Logger.LogWarning($"[游玩时间] 记账失败（这一次跳过）: {ex.Message}");
            return;
        }

        if (changed) Save(force: true);
    }

        public static void Save(bool force = false)
    {
        lock (_lock)
        {
            if (!_dirty) return;
            if (!force && unchecked(Environment.TickCount - _lastSaveTick) < SaveMinMs) return;

            try
            {
                var sb = new StringBuilder(256 + _stamps.Length * 12);
                sb.AppendLine("{");
                sb.AppendLine("  \"_字段\": \"stamps[i] = history 第 i 条（0 = 最老）第一次被插件看到时的时间，Unix 秒 UTC；0 = 不知道（这个功能上线前打的老记录，画空）。init = 账本是否已经对齐过一次。删掉整个文件 = 下次启动重新对齐（老记录仍然没有时间，之后新打的照常记）。\",");
                sb.AppendLine("  \"v\": 1,");
                sb.AppendLine($"  \"init\": {(_inited ? "true" : "false")},");
                sb.AppendLine($"  \"seen\": {_seen},");
                sb.Append("  \"stamps\": [");
                for (int i = 0; i < _stamps.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(_stamps[i]);
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
                Plugin.Logger.LogWarning($"[游玩时间] playtimes.json 写不进去（下次再试）: {ex.Message}");
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
            Plugin.Logger.LogWarning($"[游玩时间] playtimes.json 读不了（按空账本走）: {ex.Message}");
        }
    }

    static void ParseInto(string text)
    {
        using var doc = JsonDocument.Parse(text);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return;
        var root = doc.RootElement;

        lock (_lock)
        {
            _stamps = Array.Empty<ulong>();
            _bySig.Clear();
            _seen = 0;
            _inited = false;
            _dirty = false;

            bool inited = root.TryGetProperty("init", out var iv) && iv.ValueKind == JsonValueKind.True;

            if (!root.TryGetProperty("stamps", out var st) || st.ValueKind != JsonValueKind.Array)
            {

                Plugin.Logger.LogWarning("[游玩时间] playtimes.json 缺 stamps 数组 —— 当作没有，下次重新对齐");
                return;
            }

            var list = new List<ulong>(st.GetArrayLength());
            foreach (var e in st.EnumerateArray())
            {
                if (e.ValueKind == JsonValueKind.Number && e.TryGetUInt64(out ulong v)) list.Add(v);
                else list.Add(0);
            }

            _stamps = list.ToArray();
            _seen = _stamps.Length;
            _inited = inited;

            int known = 0;
            for (int i = 0; i < _stamps.Length; i++) if (_stamps[i] != 0) known++;
            Plugin.Logger.LogInfo(
                $"[游玩时间] playtimes.json: {_seen} 条（有时间的 {known} 条）");
        }
    }

        static void RebuildSigMap(List<GameResultV4> all)
    {
        _bySig.Clear();
        int n = Math.Min(all.Count, _stamps.Length);
        for (int i = 0; i < n; i++)
        {
            if (_stamps[i] == 0) continue;
            _bySig[Sig(all[i])] = _stamps[i];
        }
    }

        static string Sig(in GameResultV4 r)
    {
        var sb = _sb;
        sb.Clear();
        sb.Append(r.SongId.ToString()).Append('|')
          .Append(r.Difficulty.ToString()).Append('|')
          .Append(r.PlayerScore).Append('|')
          .Append(r.MaxCombo).Append('|')
          .Append((int)r.CalculatedResultClear).Append('|')
          .Append((int)r.CalculatedPaceValue);
        return sb.ToString();
    }
}
