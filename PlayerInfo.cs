using System;
using System.Collections.Generic;
using System.Globalization;

namespace InFalsusRating;

public static class PlayerInfo
{
        const long SCORE_MAX = 100_000_000L;

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static readonly string[] NoDiff = MakeNoDiff();

    static string[] MakeNoDiff()
    {
        var a = new string[TaskConfig.DiffOrder.Length];
        for (int i = 0; i < a.Length; i++) a[i] = "—";
        return a;
    }

        public static string Name = "Player";

        public static string RatingText = "0.00";

        public static string ExactText = "0";

        public static double Rating;
    public static int Exact;

        public static string TitleText = "称号：没戴";

        public static string TitleName = "";

        public static int TitleGrade;

        public static bool TitleOn;

        public static int Level = 1;

        public static string LevelText = "1";

        public static double LevelPct;

        public static bool LevelSeeded;

        public static List<TierCell> Tiers = new List<TierCell>();

        public static TierCell TopTier;

        public static List<CatRow> Cats = new List<CatRow>();

        public static string FinalLabel = "";

        public static string FinalBest = "—";

        public static string[] FinalBestByDiff = NoDiff;

    public static int AchDone, AchTotal;

        public static List<DiffRow> Diffs = new List<DiffRow>();

        public static DiffRow Sum = new DiffRow { Short = "合计" };

        public static int AllTotal;

        public static readonly string[] B30Diffs = { "Minimal", "Evolved", "Ultimate", "Forbidden" };

        public static int[] B30Count = new int[B30Diffs.Length];

        public sealed class DiffRow
    {
        public string Diff = "";
        public string Short = "";

                public int Rated;

                public int Total;

                public int Fl;

        public int Pd;

                public int Max;

                public double AccSum;
        public int AccN;

                public double RatingSum;

                public string CountText = "0 / 0", AccText = "—", RatingText = "—";
    }

        public sealed class TierCell
    {
        public string Name = "";

                public string Grade = "";

        public bool Cleared;
    }

        public sealed class CatRow
    {
        public string Label = "";

                public string Best = "—";

                public string[] BestByDiff = NoDiff;
    }

    static int _stamp;

    static volatile bool _dirty = true;

        const int TTL_MS = 500;

        public static void Invalidate() { _dirty = true; }

        public static void Ensure()
    {
        int now = Environment.TickCount;
        if (!_dirty && unchecked(now - _stamp) < TTL_MS) return;
        _dirty = false;
        _stamp = now;
        Refresh();
    }

        static void Refresh()
    {

        string nm = Plugin.PlayerName;
        Name = string.IsNullOrEmpty(nm) ? "Player" : nm;
        RatingText = Plugin.RatingText(Plugin.DisplayRating);
        ExactText = Plugin.DisplayExt.ToString("N0", Inv);
        Rating = Plugin.DisplayRating;
        Exact  = Plugin.DisplayExt;

        var eqd = Achievements.EquippedDef;
        if (eqd == null)
        {
            TitleText = "称号：没戴";
            TitleName = "";
            TitleGrade = 0;
            TitleOn = false;
        }
        else
        {
            string t = !string.IsNullOrEmpty(eqd.Name) ? eqd.Name : eqd.Id;
            int g = Achievements.TitleDisplayGrade(eqd);

            TitleName = t;
            TitleText = "称号：" + TitleName;
            TitleGrade = g;
            TitleOn = true;
        }

        var lv = PlayerLevel.Get();
        Level = lv.Level;
        LevelText = lv.LevelText;
        LevelPct = lv.Pct;
        LevelSeeded = lv.Seeded;

        var tiers = new List<TierCell>();
        var tl = TaskConfig.Tiers;
        if (tl != null)
            foreach (var c in tl)
            {
                if (c == null) continue;

                var rec = TaskProgress.Get(TaskConfig.ProgKey(c));
                bool ok = rec != null && rec.Cleared;
                string w = ok ? TaskProgress.GradeWord(rec.Grade) : "";
                tiers.Add(new TierCell
                {
                    Name = c.Name,
                    Cleared = ok,

                    Grade = w.Length > 0 ? w : (ok ? "通过" : ""),
                });
            }
        Tiers = tiers;

        TierCell top = null;
        foreach (var t in tiers) if (t.Cleared) top = t;
        TopTier = top;

        var cats = new List<CatRow>();
        var cds = TaskConfig.Cats;
        if (cds != null)
            foreach (var cd in cds)
            {
                if (cd == null || cd.Key == TaskConfig.TagRank) continue;
                var list = TaskConfig.Group(cd.Key);
                if (list == null || list.Count == 0) continue;
                cats.Add(new CatRow
                {
                    Label = cd.Label,
                    Best = BestGrade(list),
                    BestByDiff = BestByDiffOf(list),
                });
            }
        Cats = cats;

        FinalLabel = "";
        FinalBest = "—";
        FinalBestByDiff = NoDiff;
        foreach (var c in TaskProgress.All())
        {
            if (c == null || !c.Final) continue;
            if (!TaskConfig.Unlocked(c) && !TaskConfig.AnyCleared(c)) continue;
            FinalLabel = string.IsNullOrEmpty(c.Name) ? "神罚" : c.Name;
            int r = BestOf(c, out var w);
            FinalBest = r < 0 ? "—" : w;

            var fb = new string[TaskConfig.DiffOrder.Length];
            for (int i = 0; i < fb.Length; i++)
            {
                int fr = BestOfAt(c, TaskConfig.DiffOrder[i], out var fw);
                fb[i] = fr < 0 ? "—" : fw;
            }
            FinalBestByDiff = fb;
            break;
        }

        AchDone = Achievements.UnlockedCount;
        AchTotal = Achievements.CountedTotal;

        var rows = new List<DiffRow>();
        var index = new Dictionary<string, DiffRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in TaskConfig.DiffOrder)
        {
            var row = new DiffRow { Diff = d, Short = TaskConfig.DiffShort(d) };

            var all = ConstTable.RowsOf(d);
            row.Total = all != null ? all.Count : 0;
            rows.Add(row);
            index[d] = row;
        }

        var snap = Plugin.ChartRatings;
        if (snap != null)
            foreach (var r in snap)
            {
                if (r == null) continue;

                if (r.Difficulty == null || !index.TryGetValue(r.Difficulty, out var row)) continue;

                row.Rated++;
                if (r.IsFC) row.Fl++;
                if (r.IsPM) row.Pd++;
                if (r.Score >= (ulong)SCORE_MAX) row.Max++;
                row.RatingSum += r.Rating;

                if (r.PaceValue >= 0 && r.Constant > 0)
                {
                    row.AccSum += Math.Clamp(r.ExactRate / (100.0 * r.Constant), 0.0, 1.0);
                    row.AccN++;
                }
            }

        var sum = new DiffRow { Diff = "", Short = "合计" };
        foreach (var row in rows)
        {
            sum.Rated += row.Rated; sum.Total += row.Total;
            sum.Fl += row.Fl; sum.Pd += row.Pd; sum.Max += row.Max;
            sum.AccSum += row.AccSum; sum.AccN += row.AccN;
            sum.RatingSum += row.RatingSum;
            Finish(row);
        }
        Finish(sum);

        Diffs = rows;
        Sum = sum;
        AllTotal = sum.Total;

        var b30 = new int[B30Diffs.Length];
        if (snap != null)
        {
            int take = Math.Min(30, snap.Count);
            for (int i = 0; i < take; i++)
            {
                var cr = snap[i];
                if (cr == null || cr.Difficulty == null) continue;
                for (int k = 0; k < B30Diffs.Length; k++)
                    if (string.Equals(B30Diffs[k], cr.Difficulty, StringComparison.OrdinalIgnoreCase))
                    { b30[k]++; break; }
            }
        }
        B30Count = b30;
    }

        static int BestOf(Challenge c, out string word)
    {
        int best = -1;
        word = "";
        if (c == null) return best;
        foreach (var key in TaskProgress.ProgKeysOf(c))
        {
            var rec = TaskProgress.Get(key);
            if (rec == null || !rec.Cleared) continue;
            int r = TaskProgress.GradeRank(rec.Grade);
            if (r > best) { best = r; word = TaskProgress.GradeWord(rec.Grade); }
        }

        if (best == 0 && word.Length == 0) word = "通过";
        return best;
    }

        static string BestGrade(List<Challenge> list)
    {
        int best = -1;
        string word = "";
        foreach (var c in list)
        {
            if (c == null || c.Final) continue;
            int r = BestOf(c, out var w);
            if (r > best) { best = r; word = w; }
        }
        return best < 0 ? "—" : word;
    }

        static string KeyAt(Challenge c, string diff)
    {
        if (c == null || string.IsNullOrEmpty(diff)) return "";
        var choices = c.DiffChoices();
        if (choices == null || choices.Count == 0) return c.Key;
        foreach (var d in choices)
            if (string.Equals(d, diff, StringComparison.OrdinalIgnoreCase))
                return TaskConfig.ProgKey(c, d);
        return "";
    }

        static int BestOfAt(Challenge c, string diff, out string word)
    {
        int best = -1;
        word = "";
        string key = KeyAt(c, diff);
        if (key.Length == 0) return best;
        var rec = TaskProgress.Get(key);
        if (rec != null && rec.Cleared)
        {

            best = TaskProgress.GradeRank(rec.Grade);
            word = TaskProgress.GradeWord(rec.Grade);
            if (best == 0 && word.Length == 0) word = "通过";
        }
        return best;
    }

        static string BestGradeAt(List<Challenge> list, string diff)
    {
        int best = -1;
        string word = "";
        foreach (var c in list)
        {
            if (c == null || c.Final) continue;
            int r = BestOfAt(c, diff, out var w);
            if (r > best) { best = r; word = w; }
        }
        return best < 0 ? "—" : word;
    }

        static string[] BestByDiffOf(List<Challenge> list)
    {
        var order = TaskConfig.DiffOrder;
        var res = new string[order.Length];
        for (int i = 0; i < order.Length; i++) res[i] = BestGradeAt(list, order[i]);
        return res;
    }

        public static string BestView(CatRow r, int pick)
    {
        if (r == null) return "—";
        return pick >= 1 && r.BestByDiff != null && r.BestByDiff.Length >= pick ? r.BestByDiff[pick - 1] : r.Best;
    }

        public static string FinalBestView(int pick)
        => pick >= 1 && FinalBestByDiff != null && FinalBestByDiff.Length >= pick ? FinalBestByDiff[pick - 1] : FinalBest;

        static void Finish(DiffRow r)
    {
        r.CountText = r.Rated.ToString(Inv) + " / " + r.Total.ToString(Inv);
        r.AccText = r.AccN > 0 ? (r.AccSum / r.AccN * 100.0).ToString("F1", Inv) + "%" : "—";
        r.RatingText = r.Rated > 0 ? Plugin.RatingText(r.RatingSum / r.Rated) : "—";
    }
}
