using System;
using System.Collections.Generic;
using System.Globalization;

namespace InFalsusRating;

public static class Recommend
{
        public enum Kind { Short = 0, Potential = 1, Milestone = 2, Random = 3 }

        public enum Mile { None = 0, Score = 1, Acc = 2 }

        public sealed class Entry
    {
        public int SongId;
        public string Diff = "";
        public Kind Kind;
        public double Const;

                public bool Played;
                public bool Passed;

        public double Acc;
        public double Rating;
        public long Score;
        public int ExactCount;
        public int Pace;
        public bool IsFC;
        public bool IsPM;

        public double Ceiling;
        public double Headroom;

        public Mile MileKind;
        public double MileGap;
        public long MileNext;
        public int MileMiss;

                public string ShortWhy = "";
                public string[] WhyLines = new string[0];
    }

    public const int SlotCount = 3;

    static readonly Entry[] _slots = new Entry[SlotCount];

        public static Entry Slot(int i) => i >= 0 && i < SlotCount ? _slots[i] : null;

        public enum EmptyKind
    {
                None = 0,
                NoCharts,
                Maxed,
                BelowB30,
    }

        public static bool Empty => EmptyReason != EmptyKind.None;

        public static EmptyKind EmptyReason { get; private set; }

    static bool _rolled;
        public static bool Rolled => _rolled;

        public static void EnsureRolled()
    {
        if (!_rolled) Roll();
    }

        const int K_TOP = 6;

        const long SCORE_STEP = 500_000;
        const long SCORE_MAX = 100_000_000;

        const double ACC_STEP = 0.015;

        const int MIN_SAMPLES = 3;

        const double CEIL_HEAD = 2.0;
        const double CEIL_PD = 0.75;

        const double HEAD_EPS = 0.01;

        const int B30_SIZE = 30;

    static readonly Random _rnd = new Random();
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static void Roll()
    {
        _rolled = true;

        var prev = new string[SlotCount];
        for (int i = 0; i < SlotCount; i++)
            prev[i] = Key(_slots[i]);

        for (int attempt = 0; ; attempt++)
        {
            Build();
            if (Empty || attempt >= 1) return;

            bool same = true;
            for (int i = 0; i < SlotCount; i++)
                if (Key(_slots[i]) != prev[i]) { same = false; break; }
            if (!same) return;
        }
    }

    static string Key(Entry e) => e == null ? "" : e.SongId + "|" + e.Diff;

    static void Build()
    {
        for (int i = 0; i < SlotCount; i++) _slots[i] = null;

        var pool = BuildPool();
        if (pool.Count == 0) return;

        var sum = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var cnt = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in pool)
        {
            if (!c.Passed) continue;
            sum.TryGetValue(c.Diff, out double s); sum[c.Diff] = s + c.Acc;
            cnt.TryGetValue(c.Diff, out int n);    cnt[c.Diff] = n + 1;
        }
        foreach (var kv in cnt)
            if (kv.Value >= MIN_SAMPLES)
            {
                double a = sum[kv.Key] / kv.Value;
                foreach (var c in pool)
                    if (string.Equals(c.Diff, kv.Key, StringComparison.OrdinalIgnoreCase))
                    { c.Avg = a; c.AvgN = kv.Value; }
            }

        var lShort = new List<Cand>();
        var lPot   = new List<Cand>();
        var lMile  = new List<Cand>();

        foreach (var c in pool)
        {
            if (c.Passed && c.AvgN >= MIN_SAMPLES)
            {
                c.ShortGap = c.Avg - c.Acc;
                if (c.ShortGap > 0) lShort.Add(c);
            }

            lPot.Add(c);

            if (c.Passed)
            {
                MileOf(c);
                if (c.MileKind != Mile.None) lMile.Add(c);
            }
        }

        lShort.Sort((x, y) => y.ShortGap.CompareTo(x.ShortGap));
        lPot  .Sort((x, y) => y.Headroom.CompareTo(x.Headroom));
        lMile .Sort((x, y) => x.MileNorm.CompareTo(y.MileNorm));

        var usedSong = new HashSet<int>();
        var picked   = new List<(Cand C, Kind K)>(SlotCount);

        Take(lShort, Kind.Short,     pool, usedSong, picked);
        Take(lPot,   Kind.Potential, pool, usedSong, picked);
        Take(lMile,  Kind.Milestone, pool, usedSong, picked);

        for (int i = 0; i < picked.Count && i < SlotCount; i++)
            _slots[i] = Make(picked[i].C, picked[i].K);
    }

        static List<Cand> BuildPool()
    {
        var snap = Plugin.ChartRatings;
        var rec = new Dictionary<string, ChartRating>(StringComparer.OrdinalIgnoreCase);
        if (snap != null)
            foreach (var r in snap)
                if (r != null && !string.IsNullOrEmpty(r.SongId) && !string.IsNullOrEmpty(r.Difficulty))
                    rec[r.SongId + "|" + r.Difficulty] = r;

        double b30 = B30Floor();

        var pool = new List<Cand>();
        int raw = 0, maxed = 0, below = 0;
        foreach (string d in TaskConfig.DiffOrder)
        {
            foreach (var kv in ConstTable.InRange(d, 0, 999))
            {
                int bar = kv.Key.LastIndexOf('|');
                if (bar <= 0) continue;
                if (!int.TryParse(kv.Key.Substring(0, bar), out int id)) continue;

                var c = new Cand { Id = id, Diff = d, Const = kv.Value };
                if (rec.TryGetValue(ConstTable.GetKey(id.ToString(Inv), d), out var r)) Fill(c, r);

                Common(c);
                raw++;

                if (c.Headroom <= HEAD_EPS) { maxed++; continue; }
                if (c.Ceiling < b30)        { below++; continue; }
                pool.Add(c);
            }
        }

        EmptyReason = pool.Count > 0 ? EmptyKind.None
            : raw == 0        ? EmptyKind.NoCharts
            : maxed >= below  ? EmptyKind.Maxed
                              : EmptyKind.BelowB30;
        return pool;
    }

        static double B30Floor()
    {
        var snap = Plugin.ChartRatings;
        if (snap == null || snap.Count < B30_SIZE) return 0.0;

        var rs = new List<double>(snap.Count);
        for (int i = 0; i < snap.Count; i++)
            if (snap[i] != null) rs.Add(snap[i].Rating);
        if (rs.Count < B30_SIZE) return 0.0;

        rs.Sort((a, b) => b.CompareTo(a));
        return rs[B30_SIZE - 1];
    }

    static void Fill(Cand c, ChartRating r)
    {
        c.Played = true;
        c.R = r;
        c.Score = r.Score > (ulong)long.MaxValue ? long.MaxValue : (long)r.Score;
        c.Rating = r.Rating;
        c.ExactCount = r.ExactCount;
        c.Pace = r.PaceValue;
        c.IsFC = r.IsFC;
        c.IsPM = r.IsPM;
        c.Passed = r.PaceValue >= 0;

        c.Acc = (c.Passed && r.Constant > 0)
            ? Math.Clamp(r.ExactRate / (100.0 * r.Constant), 0.0, 1.0)
            : 0.0;

    }

        static void Common(Cand c)
    {
        c.Ceiling = RatingEngine.CurveConstant(c.Const) + CEIL_HEAD + CEIL_PD;
        c.Headroom = c.Ceiling - (c.Played ? c.Rating : 0.0);
    }

        static void MileOf(Cand c)
    {
        double bestNorm = double.MaxValue;
        var bestKind = Mile.None;
        double bestGap = 0; long bestNext = 0;

        if (c.Score < SCORE_MAX)
        {
            long next = (c.Score / SCORE_STEP + 1) * SCORE_STEP;
            if (next > SCORE_MAX) next = SCORE_MAX;
            double gap = next - c.Score;
            double norm = gap / SCORE_STEP;
            if (norm < bestNorm) { bestNorm = norm; bestKind = Mile.Score; bestGap = gap; bestNext = next; }
        }

        if (!c.R.IsPM)
        {
            double gap = 1.0 - c.Acc;
            if (gap < 0) gap = 0;
            double norm = gap / ACC_STEP;
            if (norm < bestNorm) { bestNorm = norm; bestKind = Mile.Acc; bestGap = gap; bestNext = 0; }
        }

        if (bestKind == Mile.None) return;

        c.MileKind = bestKind;
        c.MileGap = bestGap;
        c.MileNext = bestNext;
        c.MileNorm = bestNorm;
        if (bestKind == Mile.Acc && c.Acc > 0)
            c.MileMiss = (int)Math.Round(c.ExactCount * (1.0 - c.Acc) / c.Acc);
    }

        static void Take(List<Cand> ranked, Kind kind, List<Cand> pool,
                     HashSet<int> usedSong, List<(Cand, Kind)> picked)
    {
        var c = TakeRanked(ranked, usedSong);
        var k = kind;
        if (c == null)
        {
            c = TakeAnyRandom(pool, usedSong);
            k = Kind.Random;
            if (c == null) return;
        }
        usedSong.Add(c.Id);
        picked.Add((c, k));
    }

    static Cand TakeRanked(List<Cand> ranked, HashSet<int> usedSong)
    {
        int n = ranked.Count;
        if (n == 0) return null;
        int start = _rnd.Next(Math.Min(K_TOP, n));
        for (int k = 0; k < n; k++)
        {
            var c = ranked[(start + k) % n];
            if (usedSong.Contains(c.Id)) continue;
            return c;
        }
        return null;
    }

    static Cand TakeAnyRandom(List<Cand> pool, HashSet<int> usedSong)
    {
        var free = new List<Cand>();
        foreach (var c in pool)
        {
            if (usedSong.Contains(c.Id)) continue;
            free.Add(c);
        }
        return free.Count == 0 ? null : free[_rnd.Next(free.Count)];
    }

    static Entry Make(Cand c, Kind kind)
    {
        var e = new Entry
        {
            SongId = c.Id, Diff = c.Diff, Kind = kind, Const = c.Const,
            Played = c.Played, Passed = c.Passed, Acc = c.Acc, Rating = c.Rating,
            Score = c.Score, ExactCount = c.ExactCount,
            Pace = c.Pace, IsFC = c.IsFC, IsPM = c.IsPM,
            Ceiling = c.Ceiling, Headroom = c.Headroom,
            MileKind = c.MileKind, MileGap = c.MileGap, MileNext = c.MileNext, MileMiss = c.MileMiss,
        };

        switch (kind)
        {
            case Kind.Short:
                e.ShortWhy = $"本难度均值 {Pct(c.Avg)} · 这张 {Pct(c.Acc)}";
                e.WhyLines = new[]
                {
                    $"{c.Diff} 有成绩的 {c.AvgN} 张，平均准度 {Pct(c.Avg)}",
                    $"这一张 {Pct(c.Acc)}，是拖得最狠的",
                };
                break;

            case Kind.Potential:
                e.ShortWhy = $"定数 {ConstTable.Text(c.Const)} · 还有 {Num2(c.Headroom)} 空间";
                e.WhyLines = c.Played
                    ? new[]
                      {
                          $"你的评级 {Plugin.RatingText(c.Rating)} / 天花板 {Num2(c.Ceiling)}",
                          $"还差 {Num2(c.Headroom)} 摸到顶",
                      }
                    : new[]
                      {
                          $"天花板 {Num2(c.Ceiling)}（定数 {ConstTable.Text(c.Const)} 折算 {Num2(RatingEngine.CurveConstant(c.Const))} + 2.75）",
                          "这一张你还没打过",
                      };
                break;

            case Kind.Milestone when c.MileKind == Mile.Score:
                e.ShortWhy = $"再 {ScoreGap(c.MileGap)} 就破 {c.MileNext.ToString("N0", Inv)}";
                e.WhyLines = new[]
                {
                    $"现在 {c.Score.ToString("N0", Inv)}（准度 {Pct(c.Acc)}）",
                    $"再拿 {ScoreGap(c.MileGap)} 就上 {c.MileNext.ToString("N0", Inv)}",
                };
                break;

            case Kind.Milestone:
                e.ShortWhy = $"约还差 {c.MileMiss} 个 EXACT 就到 PD";
                e.WhyLines = new[]
                {
                    $"现在准度 {Pct2(c.Acc)}，全 EXACT 就是 PD",
                    $"约还差 {c.MileMiss} 个音符",
                };
                break;

            default:
                e.ShortWhy = c.Played ? $"随机挑的 · 现在 {Plugin.RatingText(c.Rating)}" : "随机挑的 · 这一档还没成绩";
                e.WhyLines = c.Played
                    ? new[]
                      {
                          $"{c.Diff} 这一档你的最好成绩",
                          $"{c.Score.ToString("N0", Inv)} · 评级 {Plugin.RatingText(c.Rating)}",
                      }
                    : new[]
                      {
                          $"你在 {c.Diff} 这一档还没有战绩",
                          "先拿它开个张",
                      };
                break;
        }
        return e;
    }

        public static string KindName(Kind k) => k switch
    {
        Kind.Short     => "短板",
        Kind.Potential => "潜力",
        Kind.Milestone => "里程碑",
        _              => "随机",
    };

    static string Pct(double v) => (v * 100).ToString("0.#", Inv) + "%";
    static string Pct2(double v) => (v * 100).ToString("0.##", Inv) + "%";
    static string Num2(double v) => v.ToString("0.00", Inv);

        static string ScoreGap(double g)
    {
        long v = (long)Math.Round(g);
        return v >= 10000 ? (v / 10000.0).ToString("0.#", Inv) + " 万" : v.ToString("N0", Inv) + " 分";
    }

        sealed class Cand
    {
        public int Id;
        public string Diff = "";
        public double Const;

        public ChartRating R;
        public bool Played;
        public bool Passed;
        public double Acc;
        public double Rating;
        public long Score;
        public int ExactCount;
        public int Pace;
        public bool IsFC;
        public bool IsPM;

        public double Ceiling;
        public double Headroom;

        public double Avg;
        public int AvgN;
        public double ShortGap;

        public Mile MileKind;
        public double MileGap;
        public long MileNext;
        public int MileMiss;
        public double MileNorm = double.MaxValue;
    }
}
