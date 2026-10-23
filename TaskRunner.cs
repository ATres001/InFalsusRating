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

public static class TaskRunner
{

    enum St { Idle, Resolve, GoSong, GoDiff, MvIntro, StartChart, Playing, Collect }

    static readonly bool MediaOnlyFinal = true;

    internal const int GAP_SONG_DIFF  = 2;
    internal const int GAP_DIFF_START = 2;

    const int SEL_SETTLE = 10;
    const int SEL_SWITCH = 30;
    const int SEL_APPLY  = 8;

    const int RESEL_MAX = 12;
    const int HUD_CHECK_FRAMES = 15;
    const int HUD_GIVEUP_FRAMES = 180;

    const int COLLECT_MIN_MS = 1000;
    const int COLLECT_MAX_MS = 5000;

    const int COLLECT_LOST_MS = 1200;

    internal const int WAIT_REMIND = 600;
    internal const int CLICK_RETRY = 90;
    const int DISMISS_TRIES = 4;
    internal const int START_TRIES = 3;

    const int KILL_WAIT_FRAMES = 1800;

    public static volatile bool Running = false;
        public static volatile string LastError = "";

        public static volatile Challenge Current = null;

        static volatile Challenge _pending = null;

        static List<TaskSong> _run = new List<TaskSong>();

        static string _runProgKey = "";

    static volatile bool _startReq = false;
    static volatile bool _abortReq = false;

    static St _st = St.Idle;
    static int _idx = -1;
    static int _mark = 0;
    static bool _entered = false;
    static bool _aborted = false;
    static bool _userAbort = false;
    static bool _diveLowLogged = false;
    static bool _hpOut = false;
    static int _n0 = -1;
    static int _lastDive = 9999;
    static int _collectFrom = 0;

    static bool _playerQuit = false;
    static string _playerQuitWhy = "";
    static int _gameSeq = 0;
    static int _seenQuitSeq = 0;
    static int _killUntil = 0;

    static int _dismissTry = 0;
    static int _dismissAt = -100000;
    static bool _dismissDumped = false;
    static bool _dismissFired = false;

    static object _startBtn;
    static int _startTry = 0;
    static int _startAt = -100000;
    static bool _startDumped = false;
    static bool _startFired = false;

    static object _curId;
    static object _curDiff;
        static object _curSong;
    static string _curTitle = "";

    static int _selStage;
    static int _selAt = -100000;
    static bool _selDone;
    static bool _selDumped;
    static bool _selUnreadable;
    static int _reselCount;
    static string _wantRowTitle = "";
    static int _hudAt = -1;
    static bool _hudChecked;
    static readonly List<SettlementRow> _rows = new List<SettlementRow>();
    static readonly Dictionary<string, MethodInfo> _miCache = new Dictionary<string, MethodInfo>();

        static int _unlockNextAt;

        public static void RequestStart(Challenge c)
    {
        if (c == null) { Plugin.Logger.LogWarning("[Exam] 没有选中课题"); return; }
        if (Running || _st != St.Idle) { Plugin.Logger.LogWarning("[Exam] 已经在考了"); return; }

        if (FreeRun.Active) { Plugin.Logger.LogWarning("[Exam] 自由局正在跳转，考不了"); return; }
        _pending = c;
        _startReq = true;
    }

        public static bool Busy => Running || _st != St.Idle;

    public static void RequestAbort()
    {
        if (!Running && _st == St.Idle) return;
        _abortReq = true;
    }

        public static string NeedText(Challenge c)
    {
        if (c == null) return "";
        var parts = new List<string> { $"{c.Count} 首全部通过" };
        if (c.MinAvg >= 0) parts.Add($"均分 > {c.MinAvg:N0}");
        if (c.MinGrade.Length > 0) parts.Add($"档位至少 {c.MinGrade}");
        return string.Join("，", parts);
    }

    public static void Tick()
    {
        TaskConfig.EnsureSeeded();

        try
        {
            if (_startReq)
            {
                _startReq = false;
                var c = _pending;
                _pending = null;
                if (_st == St.Idle) Begin(c);
            }
            if (_abortReq)
            {
                _abortReq = false;
                if (_st != St.Idle) AbortByUser();
            }

            if (_killUntil > 0)
            {
                if (SceneProbe.FrameNo > _killUntil) _killUntil = 0;
                else if (_st == St.Idle && SceneProbe.Game != null)
                {
                    _killUntil = 0;
                    Plugin.Logger.LogWarning("[Exam] 玩家重开出来的那张谱面露头了 —— 直接掐掉（这一场已经判失败了）");
                    SceneProbe.AbortChart();
                }
            }

            switch (_st)
            {
                case St.Resolve:    StepResolve();    break;
                case St.GoSong:     StepGoSong();     break;
                case St.GoDiff:     StepGoDiff();     break;
                case St.MvIntro:    StepMvIntro();    break;
                case St.StartChart: StepStartChart(); break;
                case St.Playing:    StepPlaying();    break;
                case St.Collect:    StepCollect();    break;
            }

            CheckPlayerEscape();

            LiveTick();

            if (SceneProbe.FrameNo >= _unlockNextAt)
            {
                _unlockNextAt = SceneProbe.FrameNo + 120;
                SongInfoCache.ScanUnlocks();
            }
        }
        catch (Exception ex)
        {
            FailTechnical($"状态机抛异常（{_st}）：{ex.GetType().Name}: {ex.Message}");
        }
    }

    static int Frames() => SceneProbe.FrameNo - _mark;
    static void Go(St s) { _st = s; _mark = SceneProbe.FrameNo; }

        static void CheckPlayerEscape()
    {
        if (_playerQuit || !_entered || _aborted) return;

        if (_st == St.Resolve && SceneProbe.Scene == null && SceneProbe.GameSeq != _gameSeq)
        {

            _gameSeq = SceneProbe.GameSeq;

            string why = $"第 {_idx} 首《{_curTitle}》玩家在结算界面上按了「重开」";
            Plugin.Logger.LogWarning($"[Exam] {why} → 这一场到此为止（这一首的成绩照记在结算里）");

            _killUntil = SceneProbe.FrameNo + KILL_WAIT_FRAMES;
            FailPlayer(why);
            return;
        }

        if (_st != St.Playing && _st != St.Collect) return;

        if (SceneProbe.QuitSeq != _seenQuitSeq)
        {
            _seenQuitSeq = SceneProbe.QuitSeq;
            NotePlayerEscape(false);
        }
        else if (SceneProbe.GameSeq != _gameSeq)
        {

            _gameSeq = SceneProbe.GameSeq;
            NotePlayerEscape(true);
        }
    }

        static void NotePlayerEscape(bool restart)
    {
        _playerQuit = true;
        _playerQuitWhy = $"第 {_idx + 1} 首《{_curTitle}》玩家自己{(restart ? "重开了谱面" : "退出了谱面")}";
        Plugin.Logger.LogWarning($"[Exam] {_playerQuitWhy}（当前 dive {_lastDive}，{ExamHp.Status()}）"
            + " → 这一首判失败，这一场到此为止");

        if (restart)
        {
            if (SceneProbe.Game != null) SceneProbe.AbortChart();
            else _killUntil = SceneProbe.FrameNo + KILL_WAIT_FRAMES;
        }
    }

    static void Begin(Challenge c)
    {
        LastError = "";

        if (c == null) { Reject("没有选中课题"); return; }
        if (SceneProbe.Scene == null) { Reject("不在选歌界面 —— 先退到选歌，再从课题页开考"); return; }

        if (!TaskConfig.Unlocked(c))
        {

            Reject($"{c.Name} 还没解锁 —— {TaskConfig.UnlockHint(c)}");
            return;
        }

        string why0 = TaskConfig.WhyNotRunnable(c);
        if (why0.Length > 0)
        {

            Reject(File.Exists(TaskConfig.ConfigFile)
                ? $"{c.Name} 开不了：{why0}（题目在 {TaskConfig.ConfigFile}）"
                : $"{c.Name} 开不了：{why0}（还没有 {TaskConfig.ConfigFile} —— 重开一次让插件写一份默认题库）");
            return;
        }

        var set = TaskConfig.SetlistFor(c);
        if (c.IsPool)

            Plugin.Logger.LogInfo($"[Exam] {c.Name} 抽签："
                + string.Join("、", set.ConvertAll(s => $"{s.Id}/{s.Diff}/{ConstTable.Text(ConstTable.ConstOf(s))}"))
                + $"（{TaskConfig.PoolDiffText(c)} 定数 {c.PoolMin}-{c.PoolMax}，"
                + $"共 {TaskConfig.PoolSize(c)} 张，已按定数升序）");

        var bad = new List<string>();
        for (int i = 0; i < set.Count; i++)
        {
            if (!ResolveOne(SceneProbe.Scene, set[i], out _, out _, out _, out string why, out _))
                bad.Add($"第{i + 1}首 {why}");
        }
        if (bad.Count > 0)
        {
            Reject($"{c.Name} 的曲单有 {bad.Count} 首解析不到：" + string.Join("；", bad));
            return;
        }

        _run = set;

        _runProgKey = TaskConfig.ProgKey(c);
        Current = c;
        _rows.Clear();
        _idx = 0;
        _userAbort = false;
        _playerQuit = false;
        _playerQuitWhy = "";
        _seenQuitSeq = SceneProbe.QuitSeq;
        _killUntil = 0;
        _dismissDumped = false;

        var hp = TaskConfig.HpOf(c);

        TaskMarks.Begin(HistoryLength());
        ExamHp.Begin(hp);
        Running = true;
        Plugin.Logger.LogInfo($"[Exam] ===== 开考 {c.Name}：{set.Count} 首，门槛 {NeedText(c)} =====");
        Plugin.Logger.LogInfo($"[Exam] 血量：{hp.Text()} —— 开考满血 {hp.Max}，"
            + (hp.Fatal
                ? $"掉到 {hp.Min} 整场立即结束"
                : $"掉到 {hp.Min} **不判死**（卡在下限接着打）")
            + $"，全打完 > {hp.Pass} 才算过");

        Plugin.Logger.LogInfo($"[Exam] dive <= {TaskConfig.FailDive} 现在只记日志，不强杀");
        Go(St.Resolve);
    }

    static void Reject(string why)
    {
        LastError = why;
        Plugin.Logger.LogError($"[Exam] 拒绝开考：{why}");
    }

    static readonly string[] CLICK_ORDER = { "_Hjb", "_Ijb", "_ijb", "_Gjb" };
    static readonly string[] CLICK_ALL   = { "_Gjb", "_hjb", "_Hjb", "_ijb", "_Ijb" };

        static void DumpButton(object btnLike, string label)
    {
        object btn = RealButton(btnLike);
        if (btn == null) { Plugin.Logger.LogWarning($"[Exam] 读不到 {label}"); return; }
        foreach (var f in CLICK_ALL)
            Plugin.Logger.LogInfo($"[Exam] {label}.{f} = {FieldDesc(btn, f)}");
    }

        internal static object RealButton(object o)
    {
        if (o == null) return null;
        try { if (o.GetType().Name == "UIButton") return o; } catch { return null; }

        object inner = Prop(o, "button");
        if (inner != null)
        {
            try { if (inner.GetType().Name == "UIButton") return inner; } catch { }
        }
        return null;
    }

        internal static int ClickGap(bool fired) => fired ? WAIT_REMIND : CLICK_RETRY;

        internal static bool ClickButton(object btnLike, int skip, string label)
    {
        object btn = RealButton(btnLike);
        if (btn == null) { Plugin.Logger.LogWarning($"[Exam] {label}：剥不到 UIButton，点不了"); return false; }

        var wired = new List<string>();
        foreach (var f in CLICK_ORDER)
            if (Prop(btn, f) != null) wired.Add(f);

        if (wired.Count == 0)
        {
            Plugin.Logger.LogWarning($"[Exam] {label}：{string.Join(" / ", CLICK_ORDER)} 全是空的，没东西可点");
            return false;
        }

        return FireButton(btn, wired[skip % wired.Count], label);
    }

        static bool FireButton(object btn, string fld, string label)
    {
        object cb = Prop(btn, fld);
        if (cb == null) { Plugin.Logger.LogWarning($"[Exam] {label}.{fld} 读出来是空的，点不了"); return false; }

        try
        {
            var inv = cb.GetType().GetMethod("Invoke", BindingFlags.Public | BindingFlags.Instance);
            if (inv == null) { Plugin.Logger.LogWarning($"[Exam] {label}.{fld} 上没有 Invoke，点不了"); return false; }

            var ps = inv.GetParameters();
            var args = new object[ps.Length];
            args[0] = btn;
            for (int i = 1; i < ps.Length; i++)
            {
                var pt = ps[i].ParameterType;
                if (pt.IsByRef) pt = pt.GetElementType();

                args[i] = pt.IsValueType ? Activator.CreateInstance(pt) : null;
            }
            inv.Invoke(cb, args);
            Plugin.Logger.LogInfo($"[Exam] 已点 {label}.{fld} = {Desc(cb)}");
            return true;
        }
        catch (Exception ex)
        {
            var i = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
            Plugin.Logger.LogWarning($"[Exam] 点 {label}.{fld} 失败: {i.GetType().Name}: {i.Message}");
            return false;
        }
    }

        static string FieldDesc(object host, string fld)
    {
        var p = host.GetType().GetProperty(fld, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (p == null) return "(没有这个属性)";
        try { return Desc(p.GetValue(host)); }
        catch (Exception ex) { return $"(读失败 {ex.GetType().Name})"; }
    }

        static string Desc(object d)
    {
        if (d == null) return "null";
        try
        {
            var inv = d.GetType().GetMethod("Invoke", BindingFlags.Public | BindingFlags.Instance);
            string sig = inv == null
                ? "?"
                : string.Join(", ", Array.ConvertAll(inv.GetParameters(), p => p.ParameterType.Name));

            string target = "";
            try
            {
                var mp = d.GetType().GetProperty("Method", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (mp != null) target = Prop(mp.GetValue(d), "Name") as string ?? "";
            }
            catch { }

            return $"{d.GetType().Name}.Invoke({sig})" + (string.IsNullOrEmpty(target) ? "" : $"  → {target}");
        }
        catch (Exception ex) { return $"(读不出来 {ex.GetType().Name})"; }
    }

    static void TryDismissResults()
    {
        var scene = SceneProbe.Results;
        if (scene == null) return;
        if (_dismissTry >= DISMISS_TRIES) return;
        if (Frames() - _dismissAt < ClickGap(_dismissFired)) return;
        _dismissAt = Frames();

        object btn = Prop(scene, "quitButton");
        if (btn == null)
        {
            if (!_dismissDumped)
            {
                _dismissDumped = true;
                Plugin.Logger.LogWarning("[Exam] 结算界面上读不到 quitButton —— 只好自己点「退出」");
            }
            return;
        }

        if (!_dismissDumped)
        {
            _dismissDumped = true;
            DumpButton(btn, "quitButton");
            object retry = Prop(scene, "retryButton");
            if (retry != null)
                foreach (var f in new[] { "_Hjb", "_Ijb" })
                    Plugin.Logger.LogInfo($"[Exam] retryButton.{f} = {FieldDesc(retry, f)}");
            else
                Plugin.Logger.LogInfo("[Exam] （读不到 retryButton，没法对照）");
        }

        if (ClickButton(btn, _dismissTry, "quitButton")) _dismissFired = true;
        _dismissTry++;
    }

    static void StepResolve()
    {
        if (SceneProbe.Scene == null)
        {

            if (Frames() % WAIT_REMIND == 0)
            {
                Plugin.Logger.LogWarning(SceneProbe.Results != null
                    ? $"[Exam] 第 {_idx + 1} 首打完了，停在游戏自己的结算界面上 —— 看完按「退出」回选歌，我在这儿等你（F4 中止考试）"
                    : $"[Exam] 第 {_idx + 1} 首：还没回到选歌界面 —— 请进到选歌界面（F4 中止考试）");
            }
            return;
        }

        var want = _run[_idx];
        if (!ResolveOne(SceneProbe.Scene, want, out var sid, out var diff, out var title, out string why,
                out var sinfo))
        {
            FailTechnical($"第 {_idx + 1} 首 {why}");
            return;
        }

        _curId = sid;
        _curDiff = diff;
        _curTitle = title;
        _curSong = sinfo;
        _entered = false;
        _aborted = false;
        _diveLowLogged = false;
        _hpOut = false;
        _n0 = -1;
        _lastDive = 9999;

        ResetSelectionGuard();

        ExamHp.ResetSong();

        ExamHp.NoLoss = Current != null && Current.Final && _idx >= _run.Count - 1;
        _dismissTry = 0;
        _dismissAt = -100000;
        _dismissFired = false;
        _startBtn = null;
        _startTry = 0;
        _startAt = -100000;
        _startFired = false;
        _mvFrom = -1;
        _mvSeen = false;
        Go(St.GoSong);
    }

    static void StepGoSong()
    {
        if (SceneProbe.Scene == null) { FailTechnical("选歌界面没了"); return; }
        Invoke(SceneProbe.Scene, "_ON", 1, _curId);
        Go(St.GoDiff);
    }

    static int _mvFrom = -1;
    static int _mvFromMs = 0;
    static bool _mvSeen = false;

        static string MediaOffWhy(Challenge c)
    {
        if (c == null) return "没有正在跑的课题";
        if (MediaOnlyFinal && !c.Final) return "只有神罚（终局课题）才放，普通课题默认关闭";
        return "这个课题的勾没打";
    }

    static void StepGoDiff()
    {
        if (Frames() < GAP_SONG_DIFF) return;
        if (SceneProbe.Scene == null) { FailTechnical("选歌界面没了"); return; }

        if (!EnsureSongSelected(SceneProbe.Scene)) return;

        Invoke(SceneProbe.Scene, "_pN", 1, _curDiff);

        bool mediaOk = Current != null && (!MediaOnlyFinal || Current.Final);

        if (mediaOk && Current.Bga)
        {
            string why;
            GameProbe.FeedBgForExam(_curSong, _curDiff, out why);
            Plugin.Logger.LogInfo($"[Exam] 第 {_idx + 1} 首《{_curTitle}》特殊背景：" + why);
        }
        else if (Current != null && _idx == 0)
        {
            Plugin.Logger.LogInfo("[Exam] 这个课题不放特殊背景（本场只报一次）："
                + (mediaOk ? "这个课题的勾没打（见 Challenge.Bga）" : MediaOffWhy(Current)));
        }

        if (mediaOk && Current.Mv) { Go(St.MvIntro); return; }
        if (_idx == 0)
            Plugin.Logger.LogInfo("[Exam] 这段过场不放（本场只报一次）："
                + (mediaOk ? "这个课题的勾没打" : MediaOffWhy(Current)));
        Go(St.StartChart);
    }

    static void StepMvIntro()
    {
        if (SceneProbe.Scene == null) { MvIntroOut(); FailTechnical("选歌界面没了"); return; }

        if (SceneProbe.Game != null)
        {
            Plugin.Logger.LogInfo("[Exam] 过场还没放，谱面已经起来了（玩家自己开的）—— 直接进谱");
            MvIntroOut(); Go(St.Playing); return;
        }

        if (_mvFrom < 0)
        {
            _mvFrom = Frames();
            _mvFromMs = Environment.TickCount;
            _mvSeen = false;

            GameProbe.MvPick = GameProbe.MvPickUnknown;
            Plugin.Logger.LogInfo($"[Exam] 第 {_idx + 1} 首《{_curTitle}》开谱前先放这段过场（**按任意键跳过**）");
            GameProbe.MvAnyKeySkip = true;
            GameProbe.MvCmd = 4;
            return;
        }

        if (GameProbe.MvPick == GameProbe.MvPickNone)
        {
            Plugin.Logger.LogInfo($"[Exam] 第 {_idx + 1} 首《{_curTitle}》**没有专属 MV** —— 这段过场整段跳过，"
                + "直接开谱（没有的歌不放默认视频）");
            MvIntroOut();
            Go(St.StartChart);
            return;
        }

        int ms = Environment.TickCount - _mvFromMs;
        int st = GameProbe.MvState;
        if (st != GameProbe.MvStateOff) _mvSeen = true;

        if (st == GameProbe.MvStateEnded)
        {
            Plugin.Logger.LogInfo($"[Exam] 过场放完了（这一片 {GameProbe.MvSeconds:0.0} 秒）—— 收掉，开谱");
            MvIntroOut(); Go(St.StartChart); return;
        }

        if (st == GameProbe.MvStateOff)
        {
            if (_mvSeen)
            {
                Plugin.Logger.LogInfo($"[Exam] 过场收了（{(ms / 1000f):0.0} 秒）—— 开谱");
                MvIntroOut(); Go(St.StartChart); return;
            }
            if (ms > 3000)
            {
                Plugin.Logger.LogWarning($"[Exam] 过场 {ms / 1000f:0.0} 秒了还**没起起来**"
                    + "（素材没加载上？日志里 MV： 那几行会说明是哪一步）—— 不等了，直接开谱");
                MvIntroOut(); Go(St.StartChart); return;
            }
        }

        int fuse = GameProbe.MvSeconds > 0.5f
            ? (int)(GameProbe.MvSeconds * 1000f) + 20000
            : 150000;
        if (ms > fuse)
        {
            Plugin.Logger.LogWarning($"[Exam] 过场放了 {ms / 1000f:0.0} 秒还没完"
                + $"（读到的时长是 {GameProbe.MvSeconds:0.0} 秒）—— 熔断，开谱");
            MvIntroOut(); Go(St.StartChart); return;
        }
    }

        static void MvIntroOut()
    {
        GameProbe.MvAnyKeySkip = false;
        GameProbe.MvCmd = 2;
    }

    static void StepStartChart()
    {
        if (SceneProbe.Scene == null) { FailTechnical("选歌界面没了"); return; }
        if (Frames() < GAP_DIFF_START) return;

        if (_n0 < 0)
        {
            _n0 = HistoryLength();
            if (_n0 < 0)
                Plugin.Logger.LogWarning("[Exam] 开谱前读不到 history —— 这一首的成绩可能收不到，面板会用实时 dive 兜底");
        }

        if (!ReadyToStart(SceneProbe.Scene)) return;
        _wantRowTitle = RowTitleOfSelected(SceneProbe.Scene);

        SelectedSongValue(SceneProbe.Scene, out bool sureDiff);
        if (sureDiff)
        {
            string gotDiff = DifficultyName(Prop(SceneProbe.Scene, "_Sr"));
            string wantDiff = DifficultyName(_curDiff);
            if (gotDiff != "?" && wantDiff != "?"
                && !string.Equals(gotDiff, wantDiff, StringComparison.OrdinalIgnoreCase))
                Plugin.Logger.LogWarning($"[Exam] 第 {_idx + 1} 首《{_curTitle}》难度核对："
                    + $"选歌界面现在是 {gotDiff}，目标是 {wantDiff}（先只记日志，不拦）");
        }

        string why = "";
        object raw = Prop(SceneProbe.Scene, "songSelectPlayButton");
        if (raw == null)
        {
            why = "选歌界面上读不到 songSelectPlayButton";
        }
        else
        {
            _startBtn = RealButton(raw);
            if (_startBtn == null)
            {
                why = "songSelectPlayButton 里剥不出 UIButton";
            }
            else
            {
                if (!_startDumped) { _startDumped = true; DumpButton(_startBtn, "songSelectPlayButton"); }
                if (!ClickButton(_startBtn, 0, "songSelectPlayButton"))
                    why = "点「开始」返回 false（详见上一行日志）";
            }
        }

        if (why.Length > 0)
            Plugin.Logger.LogWarning($"[Exam] 第 {_idx + 1} 首自动开谱没成功（{why}）—— 会在 StepPlaying 里再补点几次；都不行就得你自己按「开始」");

        _startAt = Frames();
        _startTry = 0;
        Go(St.Playing);
    }

    static void StepPlaying()
    {
        if (SceneProbe.Game == null)
        {
            if (_entered)
            {

                _collectFrom = Environment.TickCount;
                Go(St.Collect);
                return;
            }

            TryStartChartAgain();
            return;
        }

        if (!_entered)
        {
            _entered = true;

            _gameSeq = SceneProbe.GameSeq;

            _seenQuitSeq = SceneProbe.QuitSeq;

            _hudAt = SceneProbe.FrameNo + HUD_CHECK_FRAMES;
            Plugin.Logger.LogInfo($"[Exam] 第 {_idx + 1} 首进谱");

            if (Current != null && Current.Final) TaskProgress.NoteSeen(_runProgKey, _idx + 1);
        }

        if (!_hudChecked && _hudAt >= 0 && SceneProbe.FrameNo >= _hudAt
            && (SceneProbe.FrameNo - _hudAt) % 5 == 0)
        {
            string hud = HudSongTitle(SceneProbe.Game);
            if (hud.Length > 0)
            {
                _hudChecked = true;
                if (TitleMatches(hud, _curTitle, _wantRowTitle, BaseNameOf(_curSong)))
                {
                    Plugin.Logger.LogInfo($"[Exam] 第 {_idx + 1} 首 HUD 曲名核对通过：《{hud}》");
                }
                else
                {
                    int want = SongIdValueOf(_curId);
                    int other = SongIdOfHud(hud);
                    if (other >= 0 && other != want)
                    {
                        string otherName = SongInfoCache.GetName(
                            other.ToString(CultureInfo.InvariantCulture));
                        FailTechnical($"第 {_idx + 1} 首《{_curTitle}》：进谱后发现开出来的是《{otherName}》"
                            + "（不是目标曲目）—— 这一把的成绩不会被记进这一场，已经退出这张谱面。"
                            + "开考过程中请不要在选歌界面上点别的曲目，重新开考即可");
                        return;
                    }

                    Plugin.Logger.LogWarning($"[Exam] 第 {_idx + 1} 首《{_curTitle}》：HUD 曲名《{hud}》"
                        + "和目录标题 / 选歌行显示名 / BaseName 都对不上，但也认不出是别的哪一首"
                        + "（多半只是本地化差异）—— 这一道核对放行");
                }
            }
            else if (SceneProbe.FrameNo - _hudAt >= HUD_GIVEUP_FRAMES)
            {
                _hudChecked = true;
                Plugin.Logger.LogWarning($"[Exam] 第 {_idx + 1} 首《{_curTitle}》：进谱后一直读不到 HUD 曲名"
                    + "（songInfoContainer.songTitle），这一道核对跳过");
            }
        }

        if (_playerQuit) return;

        int d = SceneProbe.Dive;
        if (d != 9999) _lastDive = d;

        ExamHp.Feed(d);
        ExamHp.SampleCombo(SceneProbe.Combo);

        if (!_aborted && ExamHp.Out)
        {
            _aborted = true;
            _hpOut = true;
            Plugin.Logger.LogWarning($"[Exam] 第 {_idx + 1} 首《{_curTitle}》血量掉到下限 {ExamHp.Min}"
                + $"（{ExamHp.Status()}）→ 整场考试结束");
            SceneProbe.AbortChart();
            return;
        }

        if (!_aborted && d != 9999 && d <= TaskConfig.FailDive && !_diveLowLogged)
        {
            _diveLowLogged = true;
            Plugin.Logger.LogWarning($"[Exam] 第 {_idx + 1} 首《{_curTitle}》dive {d} 触底"
                + $"（<= {TaskConfig.FailDive}）—— 只记一笔，不强杀（判死看血量）");
        }
    }

    static void TryStartChartAgain()
    {
        if (_startTry < START_TRIES && SceneProbe.Scene != null && Frames() - _startAt >= ClickGap(_startFired))
        {

            if (!ReadyToStart(SceneProbe.Scene)) return;
            _wantRowTitle = RowTitleOfSelected(SceneProbe.Scene);

            _startAt = Frames();
            if (ClickButton(_startBtn, _startTry, "songSelectPlayButton(补点)")) _startFired = true;
            _startTry++;
        }

        if (Frames() % WAIT_REMIND == 0)
        {
            Plugin.Logger.LogWarning(_startTry >= START_TRIES
                ? $"[Exam] 第 {_idx + 1} 首还没开起来 —— 自动点「开始」试了 {_startTry} 次都没用，请在选歌界面自己按「开始」（F4 中止考试）"
                : $"[Exam] 第 {_idx + 1} 首还没开起来 —— 正在自动补点「开始」，也可以自己按（F4 中止考试）");
        }
    }

        static bool LostRun() => _aborted || _hpOut || _playerQuit;

    static void StepCollect()
    {

        int waited = unchecked(Environment.TickCount - _collectFrom);

        if (waited < COLLECT_MIN_MS) return;
        int n1 = HistoryLength();
        int cap = LostRun() ? COLLECT_LOST_MS : COLLECT_MAX_MS;
        if (_n0 >= 0 && n1 <= _n0 && waited < cap) return;

        var row = new SettlementRow { Title = _curTitle, Difficulty = DifficultyName(_curDiff), Dive = _lastDive };

        try { row.Const = ConstTable.Get(_curId != null ? _curId.ToString() : "", row.Difficulty); }
        catch { row.Const = -1; }

        if (_n0 >= 0 && n1 > _n0)
        {
            var r = HistoryAt(n1 - 1);
            row.Score  = r.PlayerScore;
            row.Clear  = (int)r.CalculatedResultClear;
            row.Dive   = r.CalculatedPaceValue;

            try
            {

                int bm = RatingEngine.Sum4(r, j => j.Miss);
                int nn = RatingEngine.Sum4(r, j => j.NearEarly) + RatingEngine.Sum4(r, j => j.NearLate);
                int pp = RatingEngine.Sum4(r, j => j.PerfectEarly) + RatingEngine.Sum4(r, j => j.PerfectLate);
                int ss = RatingEngine.Sum4(r, j => j.Shiny);
                row.Miss = bm; row.Near = nn; row.Perfect = pp; row.Shiny = ss;
                row.Combo = r.MaxCombo;
                ExamHp.VerifyJudges($"第 {_idx + 1} 首《{_curTitle}》", bm, nn, pp, ss);
            }
            catch (Exception ex) { Plugin.Logger.LogWarning($"[Exam/判定对账] 没做成：{ex.GetType().Name}: {ex.Message}"); }

            try { row.Detail = ResultDetail.From(r, PlayTimes.StampAt(n1 - 1, r)); }
            catch (Exception ex) { Plugin.Logger.LogWarning($"[Exam/判定明细] 没做成：{ex.GetType().Name}: {ex.Message}"); }

            row.Failed = _aborted || _playerQuit || row.Clear <= 1;
            Plugin.Logger.LogInfo($"[Exam] 第 {_idx + 1} 首《{row.Title}》记录到：分数 {row.Score:N0} / dive {row.Dive} / 判定 {ClearName(row.Clear)}");
        }
        else
        {

            row.Score  = -1;
            row.Clear  = -1;
            row.Failed = _aborted || _playerQuit;

            long sb = ExamHp.SongBreak, sn = ExamHp.SongNear, sp = ExamHp.SongExact, ss2 = ExamHp.SongShiny;
            if (JudgeProbe.Live || sb + sn + sp + ss2 > 0)
            {
                row.Miss = sb; row.Near = sn; row.Perfect = sp; row.Shiny = ss2;
                row.Combo = ExamHp.ComboMax;
                row.LiveStats = true;
            }
            Plugin.Logger.LogWarning($"[Exam] 第 {_idx + 1} 首《{row.Title}》没进 history（{_n0} → {n1}），用实时 dive {_lastDive}"
                + (row.LiveStats
                    ? $"、判定 EXACT+ {row.Shiny}/EXACT {row.Perfect}/NEAR {row.Near}/BREAK {row.Miss}、最大连击 {row.Combo}"
                    : "（实时判定这条路这次没接上，判定明细没有）")
                + (_aborted ? "" : "；没中断，这一首按未记录处理"));
        }

        if (row.Failed && _n0 >= 0 && n1 <= _n0 && _curId != null)
        {
            ExamFails.Add(_curId.ToString(), row.Difficulty, row.LiveStats,
                row.Miss, row.Near, row.Perfect, row.Shiny, row.Combo);
            SceneHooks.StartDelayedRecalculate("课题失败记录", 800);
        }

        _rows.Add(row);

        if (row.Failed)
        {

            FailPlayer(_userAbort
                ? "玩家中止考试"
                : _hpOut
                    ? $"第 {_idx + 1} 首《{row.Title}》血量掉到下限 {ExamHp.Min}"
                    : _playerQuit
                        ? _playerQuitWhy
                        : _aborted

                            ? $"第 {_idx + 1} 首《{row.Title}》被中断"
                            : $"第 {_idx + 1} 首《{row.Title}》未通过");
            return;
        }

        ExamHp.NoteClearedSong($"第 {_idx + 1} 首《{row.Title}》");

        _idx++;
        if (_idx >= _run.Count) { FinishExam(); return; }
        Go(St.Resolve);
    }

        static void Publish(SettlementData d, Challenge c)
    {

        if (c != null && c.Final && !d.Certified
            && !TaskProgress.Cleared(_runProgKey))
            d.Title = "???";

        TaskConfig.SaveResult(d);

        if (c != null) TaskProgress.Record(_runProgKey, c.Name, d);

        Win32Overlay.SettlementPanel.Show(d);
    }

    const int LIVE_PUSH_FRAMES = 15;
    static int _liveNextAt = 0;

    static void LiveTick()
    {

        if (!Busy) return;
        if (SceneProbe.FrameNo < _liveNextAt) return;
        _liveNextAt = SceneProbe.FrameNo + LIVE_PUSH_FRAMES;

        try { Win32Overlay.SettlementPanel.PushLive(BuildLive()); }
        catch (Exception ex) { Plugin.Logger.LogWarning($"[Exam] 课题面板快照没做成：{ex.GetType().Name}: {ex.Message}"); }
    }

    static string PhaseText()
    {
        switch (_st)
        {
            case St.Resolve:    return SceneProbe.Results != null ? "停在游戏自己的结算界面" : "在选歌界面";
            case St.GoSong:
            case St.GoDiff:     return "正在开谱";
            case St.MvIntro:    return "过场动画";
            case St.StartChart: return "正在点「开始」";
            case St.Playing:    return "打歌中";
            case St.Collect:    return "收成绩中";
            default:            return "";
        }
    }

        static SettlementData BuildLive()
    {
        var c = Current;
        int total = _run.Count;
        var d = new SettlementData
        {
            Title = c != null ? c.Name : "",
            Final = c != null && c.Final,
            Live = true,
            TotalSongs = total,
            CurrentIndex = _idx,
            DoneSongs = _rows.Count,
            Rows = new List<SettlementRow>(_rows),
            Phase = PhaseText(),
        };

        if (c != null && c.Final && !TaskProgress.Cleared(_runProgKey)) d.Title = "???";

        if (_idx >= 0 && _idx < total && _rows.Count <= _idx)
        {
            var ts = _run[_idx];

            bool hide = c != null && c.Final && !_entered && _st != St.Collect;
            string nm = hide ? "???" : SongInfoCache.GetName(ts.Id.ToString(CultureInfo.InvariantCulture));
            if (string.IsNullOrEmpty(nm)) nm = hide ? "???" : $"第 {_idx + 1} 首";

            var row = new SettlementRow
            {
                Title = nm,
                Difficulty = hide ? "" : ts.Diff,
                Const = -1,
                Score = -1,
                Clear = -1,
                Dive = _st == St.Playing ? _lastDive : 9999,
            };
            if (!hide)
            {
                try { row.Const = ConstTable.ConstOf(ts); }
                catch { row.Const = -1; }
            }

            if (_st == St.Playing || _st == St.Collect)
            {
                long sb = ExamHp.SongBreak, sn = ExamHp.SongNear;
                long sp = ExamHp.SongExact, ss = ExamHp.SongShiny;
                d.LiveCombo = SceneProbe.Combo;
                d.LiveComboMax = ExamHp.ComboMax;
                if (JudgeProbe.Live || sb + sn + sp + ss > 0)
                {
                    row.Miss = sb; row.Near = sn; row.Perfect = sp; row.Shiny = ss;
                    row.Combo = ExamHp.ComboMax;
                    row.LiveStats = true;
                    d.LiveJudgesOk = true;
                    d.LiveShiny = ss; d.LiveExact = sp;
                    d.LiveNear = sn;  d.LiveBreak = sb;
                }
            }

            d.CurrentRow = d.Rows.Count;
            d.Rows.Add(row);
        }

        d.Hp = ExamHp.Hp; d.HpMax = ExamHp.Max; d.HpPass = ExamHp.Pass;
        d.HpMin = ExamHp.Min; d.HpNotch = ExamHp.Notch; d.HpNoLoss = ExamHp.NoLoss;
        return d;
    }

        static void PushStopped(string why)
    {
        try
        {
            var d = BuildLive();
            d.Ended = true;
            d.EndText = why;
            d.Phase = "已中止";
            Win32Overlay.SettlementPanel.PushLive(d);
        }
        catch (Exception ex) { Plugin.Logger.LogWarning($"[Exam] 中止快照没做成：{ex.Message}"); }
    }

    static void FinishExam()
    {
        Running = false;
        _st = St.Idle;

        TaskMarks.End(HistoryLength());
        var c = Current;
        var d = Judge(true, "");
        Current = null;
        ExamHp.End();

        Publish(d, c);

        Plugin.Logger.LogInfo($"[Exam] ===== {d.Title} {(d.Certified ? "通过：" + d.Rank : "没过：" + d.Verdict)}"
            + $"（均分 {d.AverageScore:N0}，{ExamHp.Status()}）=====");
    }

    static void FailPlayer(string why)
    {
        Running = false;
        _st = St.Idle;

        TaskMarks.End(HistoryLength());
        var c = Current;
        var d = Judge(false, why);
        Current = null;
        ExamHp.End();

        Publish(d, c);

        Plugin.Logger.LogWarning($"[Exam] ===== {d.Title} 没过：{why}（打完 {_rows.Count}/{d.TotalSongs} 首）=====");
    }

        static void FailTechnical(string why)
    {
        Plugin.Logger.LogError($"[Exam] 考试中断（技术原因）：{why}");
        LastError = why;
        Running = false;
        _st = St.Idle;

        TaskMarks.End(HistoryLength());
        ExamHp.End();

        PushStopped(why);

        try { if (SceneProbe.Game != null) SceneProbe.AbortChart(); }
        catch (Exception ex) { Plugin.Logger.LogError($"[Exam] 收尾中断失败: {ex.Message}"); }
    }

    static void AbortByUser()
    {
        if (_userAbort) return;
        _userAbort = true;
        Plugin.Logger.LogWarning("[Exam] 玩家中止考试（F4）");

        if (_st == St.Playing && SceneProbe.Game != null)
        {
            _aborted = true;
            SceneProbe.AbortChart();
            return;
        }
        FailPlayer("玩家中止考试");
    }

    const long GRADE_B = 96_500_000;
    const long GRADE_A = 98_000_000;

    static SettlementData Judge(bool completed, string why)
    {
        var c = Current;
        int total = _run.Count;
        var d = new SettlementData
        {
            Title = c != null ? c.Name : "",
            Final = c != null && c.Final,
            TotalSongs = total,
            PlayedSongs = _rows.Count,
            Rows = new List<SettlementRow>(_rows),
        };

        var scored = _rows.FindAll(r => r.Score >= 0);
        d.AverageScore = scored.Count > 0 ? scored.Average(r => r.Score) : 0;
        d.FullLinkCount = _rows.FindAll(r => r.Clear >= 3).Count;
        d.PerfectDiveCount = _rows.FindAll(r => r.Clear >= 4).Count;

        if (!completed)
        {
            d.Certified = false;
            d.Verdict = why;
            return d;
        }

        if (scored.Count < total)
        {
            d.Certified = false;
            d.Verdict = $"有 {total - scored.Count} 首游戏没记下成绩，没法定档";
            return d;
        }

        bool all = _rows.Count == total;
        string rank;
        if (all && d.PerfectDiveCount == total)      rank = "S+";
        else if (all && d.FullLinkCount == total)    rank = "S";
        else if (d.AverageScore > GRADE_A)           rank = "A";
        else if (d.AverageScore > GRADE_B)           rank = "B";
        else                                         rank = "C";

        d.Rank = rank;

        string missing = "";
        if (c != null)
        {
            if (c.MinGrade.Length > 0 && TaskProgress.GradeRank(rank) < TaskProgress.GradeRank(c.MinGrade))

                missing = $"档位只有 {TaskProgress.GradeWord(rank)}，"
                        + $"这个课题要 {TaskProgress.GradeWord(c.MinGrade)}";
            else if (c.MinAvg >= 0 && !(d.AverageScore > c.MinAvg))
                missing = $"均分 {d.AverageScore:N0} 没超过 {c.MinAvg:N0}";
        }

        if (missing.Length == 0 && !ExamHp.Passed)
            missing = $"{_rows.Count} 首全打完了，但只剩 {ExamHp.Hp} 血，没超过通过线 {ExamHp.Pass}";

        d.Certified = missing.Length == 0;
        d.Verdict = d.Certified ? Shortfall(rank, d) : missing;
        return d;
    }

    static string Shortfall(string rank, SettlementData d)
    {
        switch (rank)
        {
            case "S+": return "";
            case "S":  return $"差 {d.TotalSongs - d.PerfectDiveCount} 首 PerfectDive 到 {TaskProgress.GradeWord("S+")}";
            case "A":  return $"差 {d.TotalSongs - d.FullLinkCount} 首 FullLink 到 {TaskProgress.GradeWord("S")}";
            case "B":  return $"均分差 {GRADE_A - (long)d.AverageScore:N0} 到 {TaskProgress.GradeWord("A")}";
            default:   return $"均分差 {GRADE_B - (long)d.AverageScore:N0} 到 {TaskProgress.GradeWord("B")}";
        }
    }

        internal static bool ResolveOne(object scene, TaskSong want,
        out object songId, out object diffFlag, out string title, out string why,
        out object songInfo)
    {
        songId = null; diffFlag = null; title = ""; why = ""; songInfo = null;

        object list = Prop(scene, "_Qr");
        int n = Length(list);
        if (list == null || n < 0)
        {
            why = "读不到选歌场景的 _Qr 曲目表";
            return false;
        }

        string wantId = want.Id.ToString();

        for (int i = 0; i < n; i++)
        {
            object info = At(list, i);
            if (info == null) continue;

            object id = Prop(info, "Id");
            if (id == null) continue;
            if (!string.Equals(id.ToString(), wantId, StringComparison.Ordinal)) continue;

            object charts = Prop(info, "ChartInfos");
            int cn = Length(charts);
            if (charts == null || cn < 0)
            {
                why = $"曲目 {want.Id} 读不到 ChartInfos";
                return false;
            }

            for (int j = 0; j < cn; j++)
            {
                object ci = At(charts, j);
                if (ci == null) continue;

                object diff = Prop(ci, "Difficulty");
                if (diff == null) continue;
                if (!string.Equals(diff.ToString(), want.Diff,
                        StringComparison.OrdinalIgnoreCase)) continue;

                songId = id;
                diffFlag = diff;

                songInfo = info;
                title = SongInfoCache.GetName(wantId);
                if (string.IsNullOrEmpty(title))
                {
                    var bn = Prop(info, "BaseName") as string;
                    title = string.IsNullOrEmpty(bn) ? wantId : bn;
                }
                return true;
            }

            why = $"曲目 {want.Id} 没有 {want.Diff} 难度";
            return false;
        }

        why = $"曲目 {want.Id} 不在当前曲目表里";
        return false;
    }

        static bool EnsureSongSelected(object scene)
    {
        var r = CheckSelection(scene, _curId, _curDiff, SongLabel(), "[Exam]", false, out string why);
        if (r == SelCheck.Abort) FailTechnical(why);
        return r == SelCheck.Ok;
    }

        static bool ReadyToStart(object scene)
    {
        var r = CheckSelection(scene, _curId, _curDiff, SongLabel(), "[Exam]", true, out string why);
        if (r == SelCheck.Abort) FailTechnical(why);
        return r == SelCheck.Ok;
    }

    static string SongLabel() => $"第 {_idx + 1} 首《{_curTitle}》";

        internal enum SelCheck { Ok, Wait, Abort }

        internal static void ResetSelectionGuard()
    {
        _selStage = 0;
        _selAt = -100000;
        _selDone = false;
        _reselCount = 0;
        _wantRowTitle = "";
        _hudAt = -1;
        _hudChecked = false;
    }

        internal static SelCheck CheckSelection(object scene, object songId, object diffFlag,
        string label, string tag, bool fresh, out string why)
    {
        why = "";
        if (scene == null) return SelCheck.Ok;
        if (!fresh && _selDone) return SelCheck.Ok;
        if (SceneProbe.FrameNo < _selAt) return SelCheck.Wait;

        int want = SongIdValueOf(songId);
        if (want < 0) { _selDone = true; return SelCheck.Ok; }

        int got = SelectedSongValue(scene, out bool sure);
        if (!sure)
        {
            if (!_selUnreadable)
            {
                _selUnreadable = true;
                Plugin.Logger.LogWarning($"{tag} 读不到选歌界面的当前曲目（_sr / _rr）—— 不做选歌核对");
            }
            _selDone = true;
            return SelCheck.Ok;
        }

        if (got == want)
        {
            if (_selStage >= 2 || _reselCount > 0)
                Plugin.Logger.LogInfo($"{tag} {label}：选歌已对正"
                    + (_reselCount > 0 ? $"（玩家抢点后重选了 {_reselCount} 次）"
                                       : $"（第 {_selStage - 1} 手修复生效）"));
            _selDone = true;
            return SelCheck.Ok;
        }

        bool wasConfirmed = _selDone;
        _selDone = false;

        if (fresh && wasConfirmed) { _selStage = 0; _selAt = -100000; }

        if (!_selDumped) { _selDumped = true; DumpSelection(scene, want, got); }

        if (ListHasSong(scene, want))
        {
            _reselCount++;
            if (_reselCount > RESEL_MAX)
            {
                why = $"{label}：选歌界面一直被改到别的曲目上（已经重选了 {RESEL_MAX} 次）—— "
                    + "为了不把别的曲子的成绩记进这一场，这次没按「开始」。"
                    + "开考过程中请不要在选歌界面上另点曲目，重新开考即可";
                return SelCheck.Abort;
            }
            Invoke(scene, "_ON", 1, songId);
            Invoke(scene, "_pN", 1, diffFlag);
            _selAt = SceneProbe.FrameNo + SEL_APPLY;
            Plugin.Logger.LogWarning($"{tag} {label}：选歌被改到《{SongNameOf(got)}》上 —— "
                + $"已重新选回目标（第 {_reselCount} 次），等它落定");
            return SelCheck.Wait;
        }

        switch (_selStage)
        {
            case 0:
                _selStage = 1;
                _selAt = SceneProbe.FrameNo + SEL_SETTLE;
                Plugin.Logger.LogWarning($"{tag} {label}：选歌界面停在"
                    + $"《{SongNameOf(got)}》上（目标《{SongNameOf(want)}》）—— 先等几帧再核对");
                return SelCheck.Wait;

            case 1:
            {

                object layer = Prop(scene, "packSelectLayer");
                object all = Prop(layer, "allSongsButton");
                bool fired = all != null && FireAction(all, "OnPressed", "packSelectLayer.allSongsButton");
                Plugin.Logger.LogWarning($"{tag} {label}：不在当前列表里 —— "
                    + (fired ? "已按「全部歌曲」，等列表重建" : "按不到「全部歌曲」按钮，改走换曲包"));
                _selStage = 2;
                _selAt = SceneProbe.FrameNo + (fired ? SEL_SWITCH : 0);
                return SelCheck.Wait;
            }

            case 2:

                Invoke(scene, "_ON", 1, songId);
                _selStage = 3;
                _selAt = SceneProbe.FrameNo + SEL_APPLY;
                return SelCheck.Wait;

            case 3:
            {

                object pack = FindPackOf(scene, want);
                if (pack == null)
                {
                    Plugin.Logger.LogWarning($"{tag} {label}：曲包层里找不到"
                        + "它所在的曲包 —— 直接再重选一次");
                    _selStage = 4;
                    _selAt = SceneProbe.FrameNo;
                    return SelCheck.Wait;
                }
                Plugin.Logger.LogWarning($"{tag} {label}：重选没生效 —— "
                    + $"整块切到它所在的曲包 {PackDesc(pack)}");
                Invoke(scene, "_VN", 1, pack);
                _selStage = 4;
                _selAt = SceneProbe.FrameNo + SEL_SWITCH;
                return SelCheck.Wait;
            }

            case 4:
                Invoke(scene, "_ON", 1, songId);
                _selStage = 5;
                _selAt = SceneProbe.FrameNo + SEL_APPLY;
                return SelCheck.Wait;

            default:
                why = $"{label} 选歌没成功：选歌界面一直停在"
                    + $"《{SongNameOf(got)}》上（多半是你停在某个曲包的列表里，目标曲目不在这个包里）。"
                    + "为了不把别的曲子的成绩记进这一场，这次没有按「开始」—— 请先在选歌界面切到"
                    + "「全部歌曲」（或目标曲目所在的曲包），再重新开考";
                return SelCheck.Abort;
        }
    }

        static bool ListHasSong(object scene, int want)
    {
        object rows = Prop(scene, "_rr");
        int n = Length(rows);
        for (int i = 0; i < n; i++)
            if (SongIdValueOf(Prop(At(rows, i), "_A")) == want) return true;
        return false;
    }

        static string RowTitleOfSelected(object scene)
    {
        try
        {
            object rows = Prop(scene, "_rr");
            int idx = IntOf(Prop(scene, "_Rr"), -1);
            object row = idx >= 0 ? At(rows, idx) : null;
            return row == null ? "" : (Member(row, "_b") as string) ?? "";
        }
        catch { return ""; }
    }

        static string BaseNameOf(object songInfo)
    {
        try { return songInfo == null ? "" : (Member(songInfo, "BaseName") as string) ?? ""; }
        catch { return ""; }
    }

        static string HudSongTitle(object game)
    {
        try
        {
            object container = Prop(game, "songInfoContainer");
            object arr = Prop(container, "songTitle");
            int n = Math.Min(Length(arr), 4);
            for (int i = 0; i < n; i++)
            {
                object c2d = At(arr, i);
                object ft = Prop(c2d, "_i");
                if (ft == null) continue;
                var m = FindMethod(ft, "GetText", 0);
                if (m == null) continue;
                if (m.Invoke(ft, null) is string s && s.Trim().Length > 0) return s;
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[Exam] 读 HUD 曲名失败: {ex.Message}");
        }
        return "";
    }

        static bool TitleMatches(string hud, params string[] candidates)
    {
        string h = NormTitle(hud);
        if (h.Length == 0) return false;
        foreach (string c in candidates)
        {
            string s = NormTitle(c);
            if (s.Length == 0) continue;
            if (h == s) return true;
            if (h.Length >= 3 && s.Length >= 3 && (h.Contains(s) || s.Contains(h))) return true;
        }
        return false;
    }

        static int SongIdOfHud(string hud)
    {
        try
        {
            string h = NormTitle(hud);
            if (h.Length == 0) return -1;
            foreach (int id in SongInfoCache.SelectableIds())
            {
                string sid = id.ToString(CultureInfo.InvariantCulture);
                if (NormTitle(SongInfoCache.GetName(sid)) == h) return id;
                if (NormTitle(SongInfoCache.GetBaseName(sid)) == h) return id;
            }
        }
        catch { }
        return -1;
    }

        static string NormTitle(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (char ch in s)
            if (!char.IsWhiteSpace(ch)) sb.Append(char.ToLowerInvariant(ch));
        return sb.ToString();
    }

        static object Member(object o, string name)
    {
        if (o == null) return null;
        try
        {
            var t = o.GetType();
            const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var p = t.GetProperty(name, F);
            if (p != null) return p.GetValue(o);
            var f = t.GetField(name, F);
            if (f != null) return f.GetValue(o);
        }
        catch { }
        return null;
    }

        static int SongIdValueOf(object sid)
    {
        if (sid == null) return -1;
        try
        {
            object v = Member(sid, "Value");
            if (v == null) v = Prop(sid, "Value");
            return v == null ? -1 : Convert.ToInt32(v);
        }
        catch { return -1; }
    }

    static int IntOf(object o, int def)
    {
        if (o == null) return def;
        try { return Convert.ToInt32(o); } catch { return def; }
    }

        static int SelectedSongValue(object scene, out bool sure)
    {
        sure = false;
        if (scene == null) return -1;

        int v = SongIdValueOf(Prop(scene, "_sr"));
        if (v >= 0 && v != ushort.MaxValue) { sure = true; return v; }

        object rows = Prop(scene, "_rr");
        int idx = IntOf(Prop(scene, "_Rr"), -1);
        object row = idx >= 0 ? At(rows, idx) : null;
        if (row != null)
        {
            v = SongIdValueOf(Prop(row, "_A"));
            if (v >= 0 && v != ushort.MaxValue) { sure = true; return v; }
        }
        return -1;
    }

        static object FindPackOf(object scene, int want)
    {
        object layer = Prop(scene, "packSelectLayer");
        object packs = Prop(layer, "_Ef");
        int n = Length(packs);
        if (packs == null || n <= 0) return null;

        for (int i = 0; i < n; i++)
        {
            object pack = At(packs, i);
            if (pack == null) continue;
            if (PackContains(pack, want)) return pack;
        }
        return null;
    }

        static bool PackContains(object pack, int want)
    {
        if (pack == null || want < 0) return false;

        object info = Prop(pack, "_a");
        if (info != null)
        {

            try
            {
                var m = FindMethod(info, "_VnA", 1);
                if (m != null && m.Invoke(info, new object[] { _curId }) is bool hit && hit) return true;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[Exam] PackInfo._VnA 调用失败（改比 SongIds）: {ex.Message}");
            }

            object ids = Member(info, "SongIds");
            int n = Length(ids);
            for (int i = 0; i < n; i++)
                if (SongIdValueOf(At(ids, i)) == want) return true;
        }

        object rows = Prop(pack, "_A");
        int m2 = Length(rows);
        for (int i = 0; i < m2; i++)
        {
            object row = At(rows, i);
            if (row != null && SongIdValueOf(Prop(row, "_A")) == want) return true;
        }
        return false;
    }

        static string PackDesc(object pack)
    {
        if (pack == null) return "（空）";
        object info = Prop(pack, "_a");
        if (info == null) return "某个曲包";
        try
        {
            object pid = Member(info, "Id");
            int id = IntOf(Member(pid, "Value"), -1);
            string slug = Member(info, "Slug") as string;
            int n = Length(Member(info, "SongIds"));
            var sb = new StringBuilder();
            if (id >= 0) sb.Append('#').Append(id).Append(' ');
            if (!string.IsNullOrEmpty(slug)) sb.Append('"').Append(slug).Append('"').Append(' ');
            sb.Append('（').Append(n < 0 ? 0 : n).Append(" 首）");
            return sb.ToString();
        }
        catch { return "某个曲包"; }
    }

        static string SongNameOf(int id)
    {
        if (id < 0) return "?";
        try
        {
            string n = SongInfoCache.GetName(id.ToString(CultureInfo.InvariantCulture));
            return string.IsNullOrEmpty(n) ? "#" + id : n;
        }
        catch { return "#" + id; }
    }

        static void DumpSelection(object scene, int want, int got)
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append($"[Exam] 选歌核对：目标 #{want}《{SongNameOf(want)}》，游戏选中 #{got}《{SongNameOf(got)}》");
            sb.Append($"；当前难度 {DifficultyName(Prop(scene, "_Sr"))}");
            sb.Append($"；_wr = #{SongIdValueOf(Prop(Prop(scene, "_wr"), "Id"))}");

            object rows = Prop(scene, "_rr");
            int n = Length(rows);
            int cur = IntOf(Prop(scene, "_Rr"), -1);
            sb.Append($"；列表 {n} 行（_Rr = {cur}）：");
            int shown = Math.Min(n, 12);
            for (int i = 0; i < shown; i++)
            {
                object row = At(rows, i);
                int id = SongIdValueOf(Prop(row, "_A"));
                if (i > 0) sb.Append(", ");
                if (i == cur) sb.Append('*');
                sb.Append('#').Append(id);
                string nm = Prop(row, "_b") as string;
                if (!string.IsNullOrEmpty(nm)) sb.Append('《').Append(nm).Append('》');
            }
            if (n > shown) sb.Append($", …（后面还有 {n - shown} 行）");
            Plugin.Logger.LogWarning(sb.ToString());

            object layer = Prop(scene, "packSelectLayer");
            object packs = Prop(layer, "_Ef");
            int pn = Length(packs);
            object hit = FindPackOf(scene, want);
            object all = Prop(layer, "allSongsButton");
            Plugin.Logger.LogWarning($"[Exam] 选歌核对：曲包层 {pn} 个包；"
                + (hit != null ? $"目标所在的包 = {PackDesc(hit)}" : "曲包层里没有包含目标的包")
                + "；「全部歌曲」按钮 = " + (all == null ? "读不到"
                    : Prop(all, "OnPressed") == null ? "在但回调是空的" : "在"));
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[Exam] 选歌核对的诊断日志没打完: {ex.Message}");
        }
    }

        static bool FireAction(object host, string fld, string label)
    {
        object cb = Prop(host, fld);
        if (cb == null) return false;
        try
        {
            var inv = cb.GetType().GetMethod("Invoke", BindingFlags.Public | BindingFlags.Instance);
            if (inv == null || inv.GetParameters().Length != 0) return false;
            inv.Invoke(cb, null);
            Plugin.Logger.LogInfo($"[Exam] 已触发 {label}.{fld} = {Desc(cb)}");
            return true;
        }
        catch (Exception ex)
        {
            var i = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
            Plugin.Logger.LogWarning($"[Exam] 触发 {label}.{fld} 失败: {i.GetType().Name}: {i.Message}");
            return false;
        }
    }

    static string DifficultyName(object d)
    {
        try { return d == null ? "?" : d.ToString(); } catch { return "?"; }
    }

    static string ClearName(int clear)
    {
        switch (clear)
        {
            case 4:  return "PerfectDive";
            case 3:  return "FullLink";
            case 2:  return "DiveCleared";
            case 1:  return "DiveFailed";
            default: return "未完成";
        }
    }

    static Il2CppArrayBase<GameResultV4> _histArr;
    static int _histLen = -1;

    static bool ReadHistory()
    {
        _histArr = null;
        _histLen = -1;
        var gr = RatingEngine.LastGameResults;
        if (gr == null) return false;

        try
        {
            var t = gr.GetType();
            var getHist = t.GetMethod("get_history", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (getHist == null) return false;
            var histObj = getHist.Invoke(gr, null);
            if (histObj == null) return false;

            var hType = histObj.GetType();
            var lenProp = hType.GetProperty("Length", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var bufProp = hType.GetProperty("buffer", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (lenProp == null || bufProp == null) return false;

            _histLen = Convert.ToInt32(lenProp.GetValue(histObj));
            _histArr = bufProp.GetValue(histObj) as Il2CppArrayBase<GameResultV4>;
            return _histArr != null;
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[Exam] 读 history 失败: {ex.Message}");
            return false;
        }
    }

        static int HistoryLength()
    {
        if (!ReadHistory()) return -1;
        return _histLen;
    }

    static GameResultV4 HistoryAt(int i)
    {
        if (_histArr == null || i < 0 || i >= _histLen) return default;
        return _histArr[i];
    }

    internal static object Prop(object o, string name)
    {
        if (o == null) return null;
        try
        {
            var p = o.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return p?.GetValue(o);
        }
        catch { return null; }
    }

        static int Length(object o)
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

        static object At(object o, int i)
    {
        if (o == null || i < 0) return null;
        try
        {
            var t = o.GetType();
            var p = t.GetProperty("Item", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                 ?? t.GetProperty("get_Item", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (p == null) return null;
            return p.GetValue(o, new object[] { i });
        }
        catch { return null; }
    }

    internal static MethodInfo FindMethod(object target, string name, int argc)
    {
        string key = target.GetType().FullName + "|" + name + "|" + argc;
        if (_miCache.TryGetValue(key, out var hit)) return hit;

        MethodInfo found = null;
        foreach (var m in target.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if (m.Name != name) continue;
            if (m.GetParameters().Length != argc) continue;
            found = m;
            break;
        }
        _miCache[key] = found;
        return found;
    }

    internal static object Invoke(object target, string name, int argc, params object[] args)
    {
        if (target == null) return null;
        var m = FindMethod(target, name, argc);
        if (m == null)
        {
            Plugin.Logger.LogError($"[Exam] 找不到 {target.GetType().Name}.{name}（{argc} 参数）");
            return null;
        }
        try { return m.Invoke(target, args); }
        catch (Exception ex)
        {
            var i = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
            Plugin.Logger.LogError($"[Exam] {name} 抛 {i.GetType().Name}: {i.Message}");
            return null;
        }
    }
}
