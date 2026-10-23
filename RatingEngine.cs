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

public static class RatingEngine
{
    public static object LastGameResults = null;
    public static List<ChartRating> LastChartRatings = null;

    static readonly object _dumpLock = new object();

    private static DateTime _lastCalcTime = DateTime.MinValue;
    private static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(500);
    private static string _lastFingerprint = null;

    static int CountShinies(GameResultV4 r)
    {
        return r.TapCounts.Shiny
            + r.HoldCounts.Shiny
            + r.SkyAreaCounts.Shiny
            + r.FlickCounts.Shiny;
    }

        public static int Sum4(GameResultV4 r, Func<JudgementTypeCount, int> pick)
        => pick(r.TapCounts) + pick(r.HoldCounts) + pick(r.SkyAreaCounts) + pick(r.FlickCounts);
    static int CountNotes(GameResultV4 r)
    {
        return CountAll(r.TapCounts)
             + CountAll(r.HoldCounts)
             + CountAll(r.SkyAreaCounts)
             + CountAll(r.FlickCounts);
    }

    static int CountAll(JudgementTypeCount j)
    {
        return j.None
             + j.Miss
             + j.NearEarly
             + j.NearLate
             + j.PerfectEarly
             + j.PerfectLate
             + j.Shiny;
    }

    public static void Recalculate(object gameResults, string reason, bool throttle = true)
    {
        if (gameResults == null) return;

        if (throttle)
        {
            var now = DateTime.Now;
            if (now - _lastCalcTime < MinInterval) return;
            _lastCalcTime = now;
        }

        try
        {
            var t = gameResults.GetType();
            var getHist = t.GetMethod("get_history", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var histObj = getHist.Invoke(gameResults, null);
            var hType = histObj.GetType();
            int validCount = Convert.ToInt32(hType.GetProperty("Length").GetValue(histObj));
            var bufObj = hType.GetProperty("buffer").GetValue(histObj);
            var arr = (Il2CppArrayBase<GameResultV4>)bufObj;

            var all = new List<GameResultV4>();
            for (int i = 0; i < validCount; i++) all.Add(arr[i]);

            PlayCounts.Observe(all);

            PlayerLevel.Observe(all);

            PlayTimes.Observe(all);

            TaskMarks.Observe(all.Count);

            var hist = new List<HistoryEntry>(all.Count);
            for (int i = all.Count - 1; i >= 0; i--)
            {
                var r = all[i];

                ulong stamp = PlayTimes.StampAt(i, r);
                hist.Add(new HistoryEntry
                {
                    SongId = r.SongId.ToString(),
                    Difficulty = r.Difficulty.ToString(),
                    Score = r.PlayerScore,
                    Clear = (int)r.CalculatedResultClear,
                    Seconds = stamp,

                    Detail = ResultDetail.From(r, stamp),

                    TaskPlay = TaskMarks.Contains(i),
                });
            }

            ExamFails.MergeInto(hist);

            Plugin.PlayHistory = hist;

            var bestByScore = all
                .GroupBy(r => (r.SongId.ToString(), r.Difficulty.ToString()))
                .Select(g => g.OrderByDescending(r => r.PlayerScore).First())
                .ToList();

            var maxPaceByChart = all
                .GroupBy(r => (r.SongId.ToString(), r.Difficulty.ToString()))
                .ToDictionary(g => g.Key, g => g.Max(r => (int)r.CalculatedPaceValue));

            var maxClearByChart = all
                .GroupBy(r => (r.SongId.ToString(), r.Difficulty.ToString()))
                .ToDictionary(g => g.Key, g => g.Max(r => (int)r.CalculatedResultClear));

            var passed = all.Where(r => r.CalculatedPaceValue >= 0).ToList();

            var bestShinyPlayByChart = passed
                .GroupBy(r => (r.SongId.ToString(), r.Difficulty.ToString()))
                .ToDictionary(g => g.Key,
                    g => g.OrderByDescending(r => CountShinies(r))
                          .ThenByDescending(r => r.PlayerScore)
                          .First());

            var maxShinyByChart = passed
                .GroupBy(r => (r.SongId.ToString(), r.Difficulty.ToString()))
                .ToDictionary(g => g.Key, g => g.Max(r => CountShinies(r)));

            var noteCountByChart = passed
                .GroupBy(r => (r.SongId.ToString(), r.Difficulty.ToString()))
                .ToDictionary(g => g.Key, g => g.Max(r => CountNotes(r)));

            var exactRates = new List<(string SongId, string Diff, double Rate, int Shiny)>();
            foreach (var kv in maxShinyByChart)
            {
                var (songId, diff) = kv.Key;
                int shiny = kv.Value;
                int notes = noteCountByChart.TryGetValue(kv.Key, out var n) ? n : 0;
                double constant = ConstTable.Get(songId, diff);
                if (notes <= 0 || constant <= 0) continue;

                exactRates.Add((songId, diff, 100.0 * shiny * constant / notes, shiny));
            }

            var allExact = exactRates.OrderByDescending(t => t.Rate).ToList();
            var topExact = allExact.Take(10).ToList();
            Plugin.TotalExacts = (int)Math.Round(topExact.Sum(t => t.Rate));
            Plugin.TopExactCharts = topExact.Select(t => new ExactChart
            {
                SongId = t.SongId, Difficulty = t.Diff, Rate = t.Rate, ExactCount = t.Shiny
            }).ToList();
            Plugin.AllExactCharts = allExact.Select(t =>
            {

                ResultDetail detail = null;
                if (bestShinyPlayByChart.TryGetValue((t.SongId, t.Diff), out var bp))
                    detail = ResultDetail.From(bp, PlayTimes.StampFor(bp));
                return new ExactChart
                {
                    SongId = t.SongId, Difficulty = t.Diff, Rate = t.Rate, ExactCount = t.Shiny,
                    Detail = detail,
                };
            }).ToList();

            string fingerprint = string.Join("|",
                bestByScore.OrderBy(r => r.SongId.ToString() + r.Difficulty.ToString())
                    .Select(r =>
                    {
                        int clear = maxClearByChart.TryGetValue((r.SongId.ToString(), r.Difficulty.ToString()), out var cv) ? cv : 0;
                        return $"{r.SongId}:{r.Difficulty}:{r.PlayerScore}:{r.CalculatedPaceValue}:{clear}";
                    }));

            if (fingerprint == _lastFingerprint) return;
            _lastFingerprint = fingerprint;

            var chartRatings = new List<ChartRating>();
            foreach (var r in bestByScore)
            {
                string songId = r.SongId.ToString();
                string diff = r.Difficulty.ToString();
                double constant = ConstTable.Get(songId, diff);
                if (constant < 0) continue;

                var key = (songId, diff);
                int maxPace = maxPaceByChart.TryGetValue(key, out var mp) ? mp : 0;
                int maxShiny = maxShinyByChart.TryGetValue(key, out var ms) ? ms : 0;
                int noteCount = noteCountByChart.TryGetValue(key, out var nc) ? nc : 0;
                double exactRate = noteCount > 0 ? 100.0 * maxShiny * constant / noteCount : 0;

                int clearVal = maxClearByChart.TryGetValue(key, out var cv) ? cv : 0;
                bool fc = clearVal >= 3;
                bool pm = clearVal >= 4;

                double rating = SingleRating(constant, r.PlayerScore, maxPace, clearVal);

                chartRatings.Add(new ChartRating
                {
                    SongId = songId,
                    Difficulty = diff,
                    Score = r.PlayerScore,
                    Constant = constant,
                    PaceValue = maxPace,
                    ExactCount = maxShiny,
                    ExactRate = exactRate,
                    Rating = rating,
                    IsFC = fc,
                    IsPM = pm,

                    Detail = ResultDetail.From(r, PlayTimes.StampFor(r)),
                });
            }

            chartRatings = chartRatings.OrderByDescending(c => c.Rating).ToList();
            Plugin.ChartRatings = chartRatings;
            LastChartRatings = chartRatings;

            double oldRating = Plugin.PlayerRating;
            double newRating = CalculatePlayerRating(chartRatings);

            if (Plugin.FirstRatingReady && Plugin.SetRatingDelta(oldRating, newRating))
            {
                Plugin.Logger.LogInfo(
                    $"[Rating] delta {Plugin.RatingDeltaValue:+0.00;-0.00} ({oldRating:F3} → {newRating:F3})");
            }

            Plugin.PlayerRating = newRating;
            Plugin.FirstRatingReady = true;

            Plugin.Logger.LogInfo(
                $"[Rating] ({reason}) 谱面 {chartRatings.Count} 个，评级 {Plugin.PlayerRating:F3}，EXACT {Plugin.TotalExacts}");

            Achievements.CheckAll(reason);

            lock (_dumpLock)
            {
                DumpB30(chartRatings);
                DumpAllScores(chartRatings);
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[Rating] Recalculate 出错: {ex.Message}");
        }
    }

    public static void Redump()
    {
        var snapshot = LastChartRatings;
        if (snapshot == null || snapshot.Count == 0)
        {
            Plugin.Logger.LogWarning("[Debug] 暂无成绩缓存，跳过刷新导出");
            return;
        }
        try
        {
            lock (_dumpLock)
            {
                DumpB30(snapshot);
                DumpAllScores(snapshot);
            }
            Plugin.Logger.LogInfo("[Debug] b30.txt / scores.txt 已按当前显示值刷新");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[Debug] 刷新导出失败: {ex.Message}");
        }
    }

    public const double CurveK = 0.8;

        const double CurveFallbackM = 507.0 / 38.0;

    static double _curveM;
    static readonly object _curveLock = new object();

        public static double CurveConstant(double constant)
        => CurveM + CurveK * (constant - CurveM);

    static double CurveM
    {
        get
        {
            double m = Volatile.Read(ref _curveM);
            if (m > 0) return m;

            lock (_curveLock)
            {
                m = Volatile.Read(ref _curveM);
                if (m > 0) return m;

                try
                {

                    var all = ConstTable.AllConstants();
                    if (all.Count >= 30)
                    {
                        all.Sort((a, b) => b.CompareTo(a));
                        double sum = 0, wsum = 0;
                        for (int i = 0; i < 30; i++)
                        {

                            double w = i == 0 ? 4.0 : (i < 6 ? 2.0 : 1.0);
                            sum += all[i] * w;
                            wsum += w;
                        }
                        if (wsum > 0) m = sum / wsum;
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Logger?.LogWarning($"[评级] 曲线枢轴算不出来，用兜底值: {ex.Message}");
                    m = 0;
                }

                if (m > 0) { Volatile.Write(ref _curveM, m); return m; }
                return CurveFallbackM;
            }
        }
    }

    static double ScoreBonus(ulong score)
    {
        const double BaseScore = 95_000_000;
        const double RangeForMax = 5_000_000;
        const double MaxBonus = 2.0;
        return Math.Clamp(((double)score - BaseScore) / RangeForMax * MaxBonus, -2.0, MaxBonus);
    }

    public const double PenFullScore = 95_000_000;
    public const double PenFloorScore = 90_000_000;
    public const double PenFloor = 0.20;

        public static double ScorePenalty(ulong score)
    {
        double s = score;
        if (s >= PenFullScore) return 1.0;
        if (s <= PenFloorScore) return PenFloor;
        return PenFloor + (1.0 - PenFloor) * (s - PenFloorScore) / (PenFullScore - PenFloorScore);
    }

        public static double SingleRating(double constant, ulong score, int pace, int clear)
    {
        double baseRating = CurveConstant(constant) + ScoreBonus(score);
        double penalty = ScorePenalty(score);

        if (pace >= 0)
        {

            bool fc = clear >= 3;
            bool pm = clear >= 4;
            double diveCoef = pm ? 0.05 : (fc ? 0.03 : 0.01);
            return (baseRating + pace * diveCoef) * penalty;
        }

        int n = -pace;
        double sumDive = -(n * (n + 1) / 2.0);
        double penaltyPct = Math.Floor(sumDive / 1.2);
        double factor = 1.0 + penaltyPct / 100.0;
        if (factor < 0.1) factor = 0.1;
        return baseRating * factor * penalty;
    }

    static double CalculatePlayerRating(List<ChartRating> sorted)
    {
        int n = Math.Min(30, sorted.Count);
        if (n == 0) return 0;

        double sum = 0, weightSum = 0;
        for (int i = 0; i < n; i++)
        {
            double w = 1.0;
            if (i == 0) w = 4.0;
            else if (i < 6) w = 2.0;

            sum += sorted[i].Rating * w;
            weightSum += w;
        }
        return sum / weightSum;
    }

    static void DumpB30(List<ChartRating> sorted)
    {
        try
        {

            string dir = Settings.ExportRoot;
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "b30.txt");

            var lines = new List<string>();
            lines.Add("===========================================");
            lines.Add("In Falsus Rating - B30 排行");
            lines.Add($"玩家:     {Plugin.PlayerName}");
            lines.Add($"更新时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            lines.Add($"总评级:   {Plugin.DisplayRating:F3}");
            lines.Add($"总 EXACT (Top 10): {Plugin.DisplayExt}");
            lines.Add($"谱面数:   {sorted.Count}");
            lines.Add("===========================================");
            lines.Add("");
            lines.Add("排名  Rating   曲名                          难度              分数        Dive  EXACT");

            int n = Math.Min(30, sorted.Count);
            for (int i = 0; i < n; i++)
            {
                var c = sorted[i];
                string name = SongInfoCache.GetName(c.SongId);
                if (name.Length > 28) name = name.Substring(0, 28);

                string rank   = $"#{i + 1}".PadRight(5);
                string rating = c.Rating.ToString("F3").PadLeft(6);
                string sName  = name.PadRight(28);
                string diff   = $"{c.Difficulty} ({ConstTable.Text(c.Constant)})".PadRight(16);
                string score  = c.Score.ToString().PadLeft(10);
                string dive   = c.PaceValue.ToString().PadLeft(4);
                string exact  = c.ExactCount.ToString().PadLeft(6);

                string line = $"{rank} {rating}   {sName}  {diff}  {score}  {dive} {exact}";
                lines.Add(line);

                Plugin.Logger.LogInfo($"[B30] {line}");
            }

            File.WriteAllLines(file, lines);
            Plugin.Logger.LogInfo($"[B30] 已导出: {file}");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[B30] 导出失败: {ex.Message}");
        }
    }

    static void DumpAllScores(List<ChartRating> sorted)
    {
        try
        {
            string dir = Settings.ExportRoot;
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "scores.txt");

            var lines = new List<string>();
            lines.Add("===========================================");
            lines.Add("In Falsus Rating - 全部谱面成绩");
            lines.Add($"玩家:     {Plugin.PlayerName}");
            lines.Add($"更新时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            lines.Add($"总评级:   {Plugin.DisplayRating:F3}");
            lines.Add($"总 EXACTIFICATION (Top 10): {Plugin.DisplayExt}");
            lines.Add($"谱面数:   {sorted.Count}");
            lines.Add("===========================================");
            lines.Add("");
            lines.Add("排名  Rating   曲名                          难度              分数        Dive  EXACT");

            for (int i = 0; i < sorted.Count; i++)
            {
                var c = sorted[i];
                string name = SongInfoCache.GetName(c.SongId);
                if (name.Length > 28) name = name.Substring(0, 28);

                string rank   = $"#{i + 1}".PadRight(5);
                string rating = c.Rating.ToString("F3").PadLeft(6);
                string sName  = name.PadRight(28);
                string diff   = $"{c.Difficulty} ({ConstTable.Text(c.Constant)})".PadRight(16);
                string score  = c.Score.ToString().PadLeft(10);
                string dive   = c.PaceValue.ToString().PadLeft(4);
                string exact  = c.ExactCount.ToString().PadLeft(6);

                lines.Add($"{rank} {rating}   {sName}  {diff}  {score}  {dive} {exact}");
            }

            File.WriteAllLines(file, lines);
            Plugin.Logger.LogInfo($"[Scores] 已导出: {file}");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[Scores] 导出失败: {ex.Message}");
        }
    }
}