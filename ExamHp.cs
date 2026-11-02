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

public static class ExamHp
{
    public static int Max = 100;
    public static int Min = -100;
    public static int Pass = 0;
    public static int PerSong = 0;
    public static bool Regen = true;
    public static bool Fatal = true;

    public static int NearLoss = 0;
    public static int BreakLoss = 1;
    public static int RegenNeed = 5;

    public static volatile bool Active;

    public static volatile int Hp;

    public static int RegenTierNow => _regenTierNow;

    public static volatile bool Out;

    public static volatile bool NoLoss;

    public static volatile int Notch;
    public const int NOTCH_NONE = 0, NOTCH_FL = 1, NOTCH_PD = 2;

    static int _hp;
    static int _comboMax;
    static bool _fullLink, _perfectDive;
    static bool _comboSaid;

    public static long SongBreak, SongNear, SongExact, SongShiny;

    public static int ComboMax => _comboMax;

    const int J_MISS = 1, J_NEAR_E = 2, J_NEAR_L = 3, J_PERF_E = 4, J_PERF_L = 5, J_SHINY = 6;
    public const int TIER_BREAK = 0, TIER_NEAR = 1, TIER_EXACT = 2, TIER_EXACTPLUS = 3;

    static readonly long[] _prevJudge = new long[JudgeProbe.N];
    static bool _judgesSaid;
    static bool _baselineProvisional;
    static bool _diveSaid;
    static bool _noLossSaid;
    static int _diveFrames;
    static long _unknown;

    static volatile int _regenProg, _regenTierNow;

    private static int RegenNeedNow(int tier, int regenNeed, int breakLoss, int nearLoss)
    {
        double rate;
        if (breakLoss > 1 || nearLoss > 1)
        {
            rate = CalculateHp.RegenNeedNowRate(tier);
        }
        else
        {
            rate = tier + 1;
        }

        return (int)Math.Ceiling(regenNeed * rate);
    }

    private static class CalculateHp
    {
        private const int Exponent = 5;
        private const double ZipRate = 2.148;

        private const int N = Exponent;
        private const int K = Exponent - 1;
        private static readonly double InvEk = Math.Exp(-K);
        private static readonly double[] PolyCoeffs;
        private static readonly double Scale;

        // 原始设计是f(t)=a*{[ln(t+e^K)]^N - K^N} + 1，其中K=N-1，t就是tier，+e^K和-K^N主要用处是让f(0)=0+1，同时对a额外设计，确保f(1)<=1+1
        // 针对(ln(x + e^K))^N进行了化简：
        // ln(x + e^K)里面提取e^K作为公因式，化简得到K + ln(1 + x*e^(-K))
        // 于是(K + ln(1 + x*e^(-K)))^N - K^N可以看作少了一项的二项式展开，可以结合霍纳算法进行优化
        public static double RegenNeedNowRate(int tier)
        {
            if (tier <= 0) return 1.0;
            double delta = Log1P(tier * InvEk);
            double num = EvalPoly(PolyCoeffs, delta);
            return num * Scale + 1;
        }

        static CalculateHp()
        {
            System.Diagnostics.Debug.Assert(N >= 1);
            System.Diagnostics.Debug.Assert(ZipRate > 1);

            PolyCoeffs = BuildPolyCoeffs(N, K);
            PolyCoeffs[^1] = 0; // 常数项置0
            double delta1 = Log1P(InvEk);
            double c3 = EvalPoly(PolyCoeffs, delta1);
            Scale = 1.0 / c3 / ZipRate;
        }

        // 构建 (k + x) ^ n将k与二项式系数带入后的总系数数列，按x的降幂排序
        private static double[] BuildPolyCoeffs(int n, int k)
        {
            var coeffs = new double[n + 1];

            // 组合数递推：C(n,0)=1, C(n,i)=C(n,i-1)*(n-i+1)/i
            double binom = 1.0;

            for (int i = 0; i <= n; i++)
            {
                if (i > 0)
                {
                    binom = binom * (n - i + 1) / i;
                }

                coeffs[i] = binom * Math.Pow(k, i);
            }

            return coeffs;
        }

        // 把(k + x) ^ n的x丢进去算，注意系数数列c按x的降幂排序
        private static double EvalPoly(ReadOnlySpan<double> c, double x)
        {
            double p = c[0];
            for (var i = 1; i < c.Length; i++)
            {
                var t = c[i];
                p = p * x + t;
            }

            return p;
        }

        private static double Log1P(double x)
        {
            return Math.Abs(x) > 1e-4 ? Math.Log(1.0 + x) : (-0.5 * x + 1.0) * x;
        }
    }

    const double LOSS_PER_SEC = 3.0;
    static double _tokens = LOSS_PER_SEC;
    static int _tokenAt;

    static int _prevDive = SENTINEL;

    const int SENTINEL = 9999;

    public static int JudgeTier(int v) => v switch
    {
        J_MISS => TIER_BREAK,
        J_NEAR_E or J_NEAR_L => TIER_NEAR,
        J_PERF_E or J_PERF_L => TIER_EXACT,
        J_SHINY => TIER_EXACTPLUS,
        _ => -1,
    };

    public static void Begin(HpRules r)
    {
        Max = r.Max;
        Min = r.Min;
        Pass = r.Pass;
        PerSong = r.PerSong;
        Regen = r.Regen;
        Fatal = r.Fatal;
        NearLoss = r.NearLoss;
        BreakLoss = r.BreakLoss;
        RegenNeed = r.RegenNeed;
        if (RegenNeed <= 0) RegenNeed = 1;
        _hp = r.Max;
        Hp = r.Max;
        Out = false;
        NoLoss = false;
        _regenProg = 0;
        _regenTierNow = 0;
        _tokens = LOSS_PER_SEC;
        _tokenAt = Environment.TickCount;
        _judgesSaid = false;
        _baselineProvisional = false;
        _diveSaid = false;
        Array.Clear(_prevJudge, 0, JudgeProbe.N);
        JudgeProbe.BeginRun();
        ResetSong();
        Active = true;

        Plugin.Logger.LogInfo($"[Exam] 血量开考：{r.Text()} ｜ 判定源：{JudgeProbe.Describe()}");
    }

    public static void End()
    {
        Active = false;
        Out = false;
        NoLoss = false;
        Notch = NOTCH_NONE;

        GameProbe.MvAnyKeySkip = false;
        GameProbe.MvCmd = 2;
    }

    public static void ResetSong()
    {
        _prevDive = SENTINEL;
        _comboMax = 0;

        SongBreak = SongNear = SongExact = SongShiny = 0;
        _fullLink = true;
        _perfectDive = true;
        _noLossSaid = false;
        _diveFrames = 0;
        RefreshNotch();
    }

    public static void Feed(int dive)
    {
        if (JudgeProbe.Poll(out long[] fresh))
        {
            _prevDive = SENTINEL;
            if (fresh != null) FeedJudges(fresh, JudgeProbe.NewDict);
            return;
        }

        FeedDive(dive);
    }

    static void FeedDive(int d)
    {
        if (d == SENTINEL)
        {
            _diveFrames = 0;
            return;
        }

        if (!_diveSaid && ++_diveFrames > 120)
        {
            _diveSaid = true;
            Plugin.Logger.LogWarning($"[Exam] ⚠ 在谱面里连着 2 秒读不到实时判定（{JudgeProbe.Why}）→ "
                                     + "血量退回按 dive 增量算。dive 掉到下限之后血量就不会再动了，这是**旧行为**，不是新规则。");
        }

        if (_prevDive == SENTINEL)
        {
            _prevDive = d;
            return;
        }

        if (d == _prevDive) return;

        int was = _prevDive;
        int delta = d - was;
        _prevDive = d;

        if (delta < 0 && _perfectDive)
        {
            _perfectDive = false;
            Plugin.Logger.LogInfo($"[Exam] dive 掉了 {delta}（{was} → {d}，血 {_hp}）→ 血条凹口熄掉 PD");
        }

        if (delta < 0 && NoLoss)
        {
            RefreshNotch();
            return;
        }

        if (delta > 0 && !Regen)
        {
            RefreshNotch();
            return;
        }

        Set(_hp + delta);
        RefreshNotch();
    }

    static void FeedJudges(long[] now, bool newDict)
    {
        bool empty = JudgeProbe.Entries == 0;
        if (!_judgesSaid || newDict || _baselineProvisional)
        {
            bool first = !_judgesSaid;
            _judgesSaid = true;
            _baselineProvisional = empty;
            Array.Copy(now, _prevJudge, JudgeProbe.N);

            if (first || newDict || !empty)
                Plugin.Logger.LogInfo($"[Exam] 判定基准已记下（{(first ? "开考" : newDict ? "换谱" : "账本装填完")}）"
                                      + (empty ? " —— ⚠ **账本还是空的**，这条只是占位，装填完会再记一次" : "")
                                      + $"：{JudgeProbe.Describe()}");
            return;
        }

        var got = new long[4];
        for (int v = 0; v < JudgeProbe.N; v++)
        {
            long d = now[v] - _prevJudge[v];
            if (d <= 0) continue;
            int tier = JudgeTier(v);
            if (tier >= 0) got[tier] += d;
            else _unknown += d;
        }

        SongBreak += got[TIER_BREAK];
        SongNear += got[TIER_NEAR];
        SongExact += got[TIER_EXACT];
        SongShiny += got[TIER_EXACTPLUS];
        Array.Copy(now, _prevJudge, JudgeProbe.N);
        if (got[TIER_BREAK] + got[TIER_NEAR] + got[TIER_EXACT] + got[TIER_EXACTPLUS] <= 0) return;

        if ((got[TIER_BREAK] > 0 || got[TIER_NEAR] > 0) && _perfectDive)
        {
            _perfectDive = false;
            Plugin.Logger.LogInfo($"[Exam] 打进 {got[TIER_BREAK]} 个 break / {got[TIER_NEAR]} 个 near → 血条凹口熄掉 PD");
        }

        if (got[TIER_BREAK] > 0) Lose(got[TIER_BREAK], BreakLoss, "break");
        if (got[TIER_NEAR] > 0)
        {
            if (NearLoss > 0) Lose(got[TIER_NEAR], NearLoss, "near");
            else Earn(got[TIER_NEAR] * HpRules.WeightOf(TIER_NEAR, NearLoss));
        }

        if (got[TIER_EXACT] > 0) Earn(got[TIER_EXACT] * HpRules.WeightOf(TIER_EXACT, NearLoss));
        if (got[TIER_EXACTPLUS] > 0) Earn(got[TIER_EXACTPLUS] * HpRules.WeightOf(TIER_EXACTPLUS, NearLoss));
        RefreshNotch();
    }

    static void Lose(long n, int per, string what)
    {
        _regenProg = 0;
        _regenTierNow = Math.Max(0, _regenTierNow - 1);
        if (n <= 0 || per <= 0) return;
        if (NoLoss)
        {
            if (!_noLossSaid)
            {
                _noLossSaid = true;
                Plugin.Logger.LogInfo($"[Exam] 这一首不掉血（终局最后一首）"
                                      + $"→ {n} 个 {what} 不记账（回血照旧、进度照样断）");
            }

            return;
        }

        int drop = 0;
        for (long i = 0; i < n; i++)
            if (TakeToken())
                drop++;
        if (drop <= 0) return;
        int before = _hp;
        Set(_hp - drop * per);
        Plugin.Logger.LogInfo($"[Exam] 血量：{n} 个 {what} → -{drop * per}（{before} → {_hp}）"
                              + (drop < n ? $"，{n - drop} 个被「一秒三次」挡了（不扣血，但连续照断）" : ""));
        RefreshNotch();
    }

    static void Earn(long w)
    {
        if (!Regen || w <= 0) return;
        if (_hp >= Max)
        {
            _regenProg = 0;
            return;
        }

        if (_regenTierNow < 0) _regenTierNow = 0;
        _regenProg += (int)Math.Min(w, 1_000_000);
        int plus = 0, before = _hp;
        int regenNeedNow;
        while (_regenProg >= (regenNeedNow = RegenNeedNow(_regenTierNow, RegenNeed, BreakLoss, NearLoss)) &&
               _hp < Max && plus <= 999)
        {
            _regenProg -= regenNeedNow;
            _regenTierNow += 1;
            Set(_hp + 1);
            plus++;
        }

        if (_hp >= Max || plus > 999) _regenProg = 0;
        if (plus > 0)
            Plugin.Logger.LogInfo($"[Exam] 血量：攒满一管 → +{plus}（{before} → {_hp}，下一管要 {regenNeedNow} 点）"
                                  + (_hp >= Max ? "（到上限了，多余的进度不留）" : ""));
    }

    static bool TakeToken()
    {
        int now = Environment.TickCount;
        int dt = unchecked(now - _tokenAt);
        if (dt > 0)
        {
            _tokenAt = now;
            _tokens += dt / 1000.0 * LOSS_PER_SEC;
            if (_tokens > LOSS_PER_SEC) _tokens = LOSS_PER_SEC;
        }

        if (_tokens < 1.0) return false;
        _tokens -= 1.0;
        return true;
    }

    public static void SampleCombo(int c)
    {
        if (c < 0) return;
        if (!_comboSaid)
        {
            _comboSaid = true;
            Plugin.Logger.LogInfo($"[Exam] 连击读到了：{c}（血条凹口那个 FL 色块靠它认断连）");
        }

        if (c >= _comboMax)
        {
            _comboMax = c;
            return;
        }

        if (!_fullLink) return;
        _fullLink = false;
        RefreshNotch();
        Plugin.Logger.LogInfo($"[Exam] 断连了（连击 {_comboMax} → {c}）→ 血条凹口熄掉 FL");
    }

    public static void NoteClearedSong(string what)
    {
        if (PerSong <= 0) return;
        int before = _hp;
        Set(_hp + PerSong);
        Plugin.Logger.LogInfo($"[Exam] 血量：{what} 过了，补 {PerSong} → {_hp}（{before} 起）");
    }

    public static bool Passed => _hp > Pass;

    public static string Status()
        => $"血量 {_hp}（{Min} ~ {Max}，通过线 > {Pass}）"
           + $" · 近-{NearLoss} 破-{BreakLoss} "
           + (Regen ? $"攒 {_regenProg}/{RegenNeedNow(_regenTierNow, RegenNeed, BreakLoss, NearLoss)}" : "不回血")
           + $" · 判定源 {JudgeProbe.Source}"
           + (_unknown > 0 ? $" · ⚠ 不认识的判定值 {_unknown} 个" : "");

    public static void VerifyJudges(string what, int miss, int near, int exact, int shiny)
    {
        long b = JudgeProbe.Counts[J_MISS];
        long n = JudgeProbe.Counts[J_NEAR_E] + JudgeProbe.Counts[J_NEAR_L];
        long e = JudgeProbe.Counts[J_PERF_E] + JudgeProbe.Counts[J_PERF_L];
        long s = JudgeProbe.Counts[J_SHINY];
        bool ok = b == miss && n == near && e == exact && s == shiny;
        Plugin.Logger.LogInfo($"[Exam/判定对账] {what} ｜ 游戏：破 {miss} / 近 {near} / 准 {exact} / 亮 {shiny}"
                              + $" ｜ 我们：破 {b} / 近 {n} / 准 {e} / 亮 {s}"
                              + $" ｜ 逐值 {string.Join("/", JudgeProbe.Counts)}"
                              + (ok
                                  ? " ｜ ✅ 对得上（判定这套读法和档位映射都成立）"
                                  : $" ｜ ⚠ 对不上（差：破 {b - miss} / 近 {n - near} / 准 {e - exact} / 亮 {s - shiny}）"));
        if (ok) JudgeProbe.Verified = true;
    }

    static void Set(int v)
    {
        if (v > Max) v = Max;
        if (v < Min) v = Min;
        _hp = v;
        Hp = v;

        if (Fatal && v <= Min) Out = true;
    }

    static void RefreshNotch()
        => Notch = _perfectDive ? NOTCH_PD : _fullLink ? NOTCH_FL : NOTCH_NONE;
}