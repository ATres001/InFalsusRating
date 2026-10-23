using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using BepInEx;
using ifapp.Game.Data;

namespace InFalsusRating;

public static class PlayerLevel
{
        public static string JsonPath => Path.Combine(
        BepInEx.Paths.ConfigPath, "InFalsusRating", "level.json");

        const int SaveMinMs = 1000;

        const double ScoreMax = 100_000_000.0;

        const long XpMax = 1_000_000_000_000_000L;

    static double BaseOf(string diff)
    {
        switch (diff?.ToUpperInvariant())
        {
            case "MINIMAL": case "MIN":     return 10;
            case "EVOLVED": case "EVO":     return 18;
            case "ULTIMATE": case "ULT":    return 28;
            case "FORBIDDEN": case "FBD":   return 40;
            default:                        return 0;
        }
    }

        static long GainOf(in GameResultV4 r)
        => GainFor(r.Difficulty.ToString(), r.PlayerScore, (int)r.CalculatedResultClear);

        public static int GainFor(string diff, ulong score, int clear)
    {
        double b = BaseOf(diff);
        if (b <= 0) return 0;

        double s = score;
        if (s < 0) s = 0;
        if (s > ScoreMax) s = ScoreMax;

        double mult = 1.0;
        if (clear >= 4) mult = 1.20;
        else if (clear >= 3) mult = 1.10;

        double g = b * (0.4 + 0.6 * s / ScoreMax) * mult;
        long v = (long)Math.Round(g, MidpointRounding.AwayFromZero);
        return v < 1 ? 1 : (int)Math.Min(v, int.MaxValue);
    }

        public static long NeedOf(int level) => level < 1 ? 0 : 50L + 6L * (level - 1);

        public static long CumOf(int level)
    {
        if (level <= 1) return 0;
        long l = level;
        return 3L * l * l + 41L * l - 44L;
    }

        public static int LevelOf(long xp)
    {
        if (xp <= 0) return 1;
        double d = (-41.0 + Math.Sqrt(2209.0 + 12.0 * xp)) / 6.0;
        int L = (int)Math.Floor(d);
        if (L < 1) L = 1;

        while (L > 1 && CumOf(L) > xp) L--;
        while (CumOf(L + 1) <= xp) L++;
        return L;
    }

    static readonly object _lock = new object();

    static long _xp;
    static int _seen;
    static ulong _lastTs;
    static bool _seeded;
    static bool _dirty;
    static int _lastSaveTick = unchecked(Environment.TickCount - SaveMinMs);

        public readonly struct Snapshot
    {
                public readonly int Level;

                public readonly bool Seeded;

                public readonly double Pct;

                public readonly string LevelText;

        public Snapshot(int level, bool seeded, double pct, string text)
        {
            Level = level; Seeded = seeded; Pct = pct; LevelText = text;
        }
    }

        public static Snapshot Get()
    {
        lock (_lock)
        {
            long xp = _xp;
            int L = LevelOf(xp);
            long need = NeedOf(L);
            double pct = need > 0 ? (double)(xp - CumOf(L)) / need : 0;
            if (pct < 0) pct = 0;
            if (pct >= 1) pct = 0.999999;
            return new Snapshot(L, _seeded, pct,
                L.ToString(CultureInfo.InvariantCulture));
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
            lock (_lock) ResetLocked();
            Plugin.Logger.LogWarning($"[等级] level.json 读不了（按首次折算走）: {ex.Message}");
        }
    }

    static void ResetLocked()
    {
        _xp = 0;
        _seen = 0;
        _lastTs = 0;
        _seeded = false;
        _dirty = false;
    }

        public static void Observe(List<GameResultV4> all)
    {
        if (all == null) return;

        bool doSave = false;
        string seedLog = null;

        try
        {

            bool needSeed;
            lock (_lock) needSeed = !_seeded && all.Count > 0;

            List<KeyValuePair<string, int>> counts = null;
            int highWater = 0;
            if (needSeed)
                PlayCounts.Snapshot(out counts, out highWater);

            lock (_lock)
            {
                int n = all.Count;

                if (!_seeded)
                {

                    double sum = 0;
                    int plays = 0, skipped = 0, charts = 0;

                    if (counts != null)
                    {
                        charts = counts.Count;
                        foreach (var kv in counts)
                        {
                            int bar = kv.Key.IndexOf('|');
                            if (bar <= 0 || bar >= kv.Key.Length - 1) continue;
                            plays += kv.Value;
                            double b = BaseOf(kv.Key.Substring(bar + 1));
                            if (b <= 0) { skipped += kv.Value; continue; }
                            sum += kv.Value * b;
                        }
                    }

                    int validInAll = 0;
                    for (int i = 0; i < n; i++)
                        if (BaseOf(all[i].Difficulty.ToString()) > 0) validInAll++;

                    if (n > 0 && plays == 0 && validInAll > 0)
                    {
                        Plugin.Logger.LogError(
                            "[等级] 首次折算失败：history 有成绩但次数账本是空的 —— 本次跳过，下次再试");
                        return;
                    }

                    long gain = (long)Math.Round(sum, MidpointRounding.AwayFromZero);
                    if (gain < 0) gain = 0;
                    _xp = ClampXp(_xp + gain);

                    ulong maxTs = 0;
                    for (int i = 0; i < n; i++)
                    {
                        ulong ts = all[i].SecondsSinceEpochUtc;
                        if (ts > maxTs) maxTs = ts;
                    }
                    _lastTs = maxTs;

                    _seen = Math.Max(n, highWater);
                    _seeded = true;
                    _dirty = true;
                    doSave = true;

                    int lv = LevelOf(_xp);
                    seedLog = $"[等级] 首次折算：{charts} 张谱 / {plays} 次 → {gain} XP"
                            + (skipped > 0 ? $"（认不出难度的 {skipped} 次已跳过）" : "")
                            + $" ⇒ 总 {_xp} XP（Lv.{lv}）";
                }
                else
                {
                    if (n == _seen) return;

                    if (n < _seen)
                    {

                        Plugin.Logger.LogWarning(
                            $"[等级] history 变小了（{_seen} → {n}）—— 已重新对齐，XP 一个不改");
                        _seen = n;
                        _dirty = true;
                        doSave = true;
                        return;
                    }

                    long gained = 0;
                    int used = 0;
                    for (int i = _seen; i < n; i++)
                    {
                        var r = all[i];
                        ulong ts = r.SecondsSinceEpochUtc;
                        if (ts <= _lastTs) continue;
                        long g = GainOf(r);
                        if (g <= 0) continue;
                        _xp = ClampXp(_xp + g);
                        gained += g;
                        used++;
                        if (ts > _lastTs) _lastTs = ts;
                    }

                    _seen = n;
                    _dirty = true;
                    doSave = true;

                    if (used > 0)
                        Plugin.Logger.LogInfo(
                            $"[等级] +{gained} XP（{used} 局）⇒ 总 {_xp} XP（Lv.{LevelOf(_xp)}）");
                }
            }
        }
        catch (Exception ex)
        {

            Plugin.Logger.LogWarning($"[等级] 入账失败（这一次跳过）: {ex.Message}");
            return;
        }

        if (seedLog != null) Plugin.Logger.LogInfo(seedLog);
        if (doSave) Save(force: seedLog != null);
    }

    static long ClampXp(long v)
    {
        if (v < 0) return 0;
        return v > XpMax ? XpMax : v;
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
                sb.AppendLine("  \"_字段\": \"xp = 总经验；seen = 已经入过账的 history 条数；ts = 最后入账那条的时间戳（SecondsSinceEpochUtc）；seeded = 是否已按旧游玩次数折算过。删掉整个文件 = 下次启动重新折算。\",");
                sb.AppendLine("  \"v\": 1,");
                sb.AppendLine($"  \"xp\": {_xp},");
                sb.AppendLine($"  \"seen\": {_seen},");
                sb.AppendLine($"  \"ts\": {_lastTs},");
                sb.AppendLine($"  \"seeded\": {(_seeded ? "true" : "false")}");
                sb.AppendLine("}");

                Directory.CreateDirectory(Path.GetDirectoryName(JsonPath));
                File.WriteAllText(JsonPath, sb.ToString());
                _dirty = false;
                _lastSaveTick = Environment.TickCount;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[等级] level.json 写不进去（下次再试）: {ex.Message}");
            }
        }
    }

    static void ParseInto(string text)
    {
        using var doc = JsonDocument.Parse(text);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return;
        var root = doc.RootElement;

        long xp = 0;
        bool hasXp = false;
        if (root.TryGetProperty("xp", out var xv) && xv.ValueKind == JsonValueKind.Number
            && xv.TryGetInt64(out long x)) { hasXp = true; xp = x; }

        int seen = 0;
        bool hasSeen = root.TryGetProperty("seen", out var sv) && sv.ValueKind == JsonValueKind.Number
            && sv.TryGetInt32(out seen) && seen >= 0;

        ulong ts = 0;
        bool hasTs = root.TryGetProperty("ts", out var tv) && tv.ValueKind == JsonValueKind.Number
            && tv.TryGetUInt64(out ts);

        bool seeded = root.TryGetProperty("seeded", out var dv)
            && dv.ValueKind == JsonValueKind.True;

        if (seeded && !hasXp)
        {

            Plugin.Logger.LogWarning("[等级] level.json 缺 xp 字段 —— 按首次折算走");
            lock (_lock) ResetLocked();
            return;
        }
        if (!hasSeen) seen = 0;
        if (!hasTs) ts = 0;

        lock (_lock)
        {
            _xp = ClampXp(xp);
            _seen = seen;
            _lastTs = ts;
            _seeded = seeded;
            _dirty = false;
        }

        Plugin.Logger.LogInfo(
            $"[等级] level.json: {_xp} XP（Lv.{LevelOf(_xp)}）/ 对齐到第 {_seen} 条"
            + (_seeded ? "" : " / 还没折算过（等第一次观察）"));
    }
}
