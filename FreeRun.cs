using System;

namespace InFalsusRating;

public static class FreeRun
{
        static readonly bool AUTO_START = true;

    enum St { Idle, Resolve, GoSong, GoDiff, StartChart, Wait }

    static volatile bool _active;
        public static bool Active => _active;

        public static volatile string LastError = "";

        public static volatile string Status = "";

    static St _st = St.Idle;
    static int _mark;

    static int _wantId;
    static string _wantDiff = "";

    static object _songId;
    static object _diffFlag;
    static string _title = "";

    static object _startBtn;
    static int _startAt;
    static int _startTry;
    static bool _startFired;

        const int RESOLVE_MAX = 3600;

    static int Frames() => SceneProbe.FrameNo - _mark;
    static void Go(St s) { _st = s; _mark = SceneProbe.FrameNo; }

        public static string RequestStart(int songId, string diff)
    {
        if (_active) return "已经在跳了";
        if (songId <= 0) return "这一格没读出曲目";
        if (string.IsNullOrEmpty(diff)) return "这一格没读出难度";

        if (TaskRunner.Busy) return "正在考试 —— 考完再来";

        _wantId = songId;
        _wantDiff = diff;
        _title = "";
        _startBtn = null;
        _startTry = 0;
        _startFired = false;

        TaskRunner.ResetSelectionGuard();
        LastError = "";
        _active = true;
        Go(St.Resolve);
        Plugin.Logger.LogInfo($"[FreeRun] 请求跳转：曲目 {songId} · {diff}");
        return "";
    }

        public static void RequestAbort()
    {
        if (!_active) return;
        End("已取消", quiet: true);
    }

    public static void Tick()
    {
        if (_st == St.Idle) return;

        try
        {
            switch (_st)
            {
                case St.Resolve:     StepResolve(); break;
                case St.GoSong:      StepGoSong(); break;
                case St.GoDiff:      StepGoDiff(); break;
                case St.StartChart:  StepStartChart(); break;
                case St.Wait:        StepWait(); break;
            }
        }
        catch (Exception ex)
        {

            try { End("跳转时出错：" + ex.GetType().Name + " " + ex.Message); }
            catch { _st = St.Idle; _active = false; }
        }
    }

        static void StepResolve()
    {
        if (SceneProbe.Scene == null)
        {
            if (Frames() >= RESOLVE_MAX)
            { End($"等了 {RESOLVE_MAX / 60} 秒还没进选歌界面 —— 请先在游戏里进到选歌界面，再点「开始」"); return; }

            if (Frames() % TaskRunner.WAIT_REMIND == 0)
            {
                Status = "正在等你进到选歌界面…";
                Plugin.Logger.LogWarning("[FreeRun] 还没进选歌界面 —— 请先进到选歌界面");
            }
            return;
        }

        if (!TaskRunner.ResolveOne(SceneProbe.Scene, new TaskSong { Id = _wantId, Diff = _wantDiff },
                out _songId, out _diffFlag, out _title, out string why, out _))
        { End(why); return; }

        Status = $"正在前往《{_title}》…";
        Plugin.Logger.LogInfo($"[FreeRun] 解析到《{_title}》· 难度标记 {_diffFlag}");
        Go(St.GoSong);
    }

        static void StepGoSong()
    {
        if (SceneProbe.Scene == null) { End("选歌界面没了"); return; }

        if (TaskRunner.FindMethod(SceneProbe.Scene, "_ON", 1) == null)
        { End("选歌界面上找不到 _ON（游戏更新过？）"); return; }

        Plugin.Logger.LogInfo($"[FreeRun] 调 _ON({_songId})");
        TaskRunner.Invoke(SceneProbe.Scene, "_ON", 1, _songId);
        Go(St.GoDiff);
    }

        static void StepGoDiff()
    {
        if (Frames() < TaskRunner.GAP_SONG_DIFF) return;
        if (SceneProbe.Scene == null) { End("选歌界面没了"); return; }

        if (TaskRunner.FindMethod(SceneProbe.Scene, "_pN", 1) == null)
        { End("选歌界面上找不到 _pN（游戏更新过？）"); return; }

        Plugin.Logger.LogInfo($"[FreeRun] 调 _pN({_diffFlag})");
        TaskRunner.Invoke(SceneProbe.Scene, "_pN", 1, _diffFlag);

        if (AUTO_START) { Go(St.StartChart); return; }

        Plugin.Logger.LogInfo($"[FreeRun] 已定位到《{_title}》· {_wantDiff}，自己按「开始」");
        EndSoft();
    }

        static bool CheckSelection(object scene)
    {
        var r = TaskRunner.CheckSelection(scene, _songId, _diffFlag, $"《{_title}》",
            "[FreeRun]", true, out string why);
        if (r == TaskRunner.SelCheck.Abort) { End(why); return false; }
        return r == TaskRunner.SelCheck.Ok;
    }

        static void StepStartChart()
    {
        if (SceneProbe.Scene == null) { End("选歌界面没了"); return; }
        if (Frames() < TaskRunner.GAP_DIFF_START) return;

        if (!CheckSelection(SceneProbe.Scene)) return;

        string why = "";
        object raw = TaskRunner.Prop(SceneProbe.Scene, "songSelectPlayButton");
        if (raw == null) why = "选歌界面上读不到 songSelectPlayButton";
        else
        {
            _startBtn = TaskRunner.RealButton(raw);
            if (_startBtn == null) why = "songSelectPlayButton 里剥不出 UIButton";
            else if (!TaskRunner.ClickButton(_startBtn, 0, "songSelectPlayButton"))
                why = "点「开始」返回 false（详见上一行日志）";
        }

        if (why.Length > 0)
            Plugin.Logger.LogWarning($"[FreeRun] 自动按「开始」没成功（{why}）—— 会在补点里再试几次；都不行就得你自己按");

        _startAt = SceneProbe.FrameNo;
        _startTry = 0;
        Go(St.Wait);
    }

        static void StepWait()
    {

        if (SceneProbe.Game != null)
        {
            Plugin.Logger.LogInfo($"[FreeRun] 《{_title}》开起来了 —— 剩下的交给游戏");
            EndSoft();
            return;
        }

        if (_startTry < TaskRunner.START_TRIES
            && SceneProbe.Scene != null
            && SceneProbe.FrameNo - _startAt >= TaskRunner.ClickGap(_startFired))
        {

            if (!CheckSelection(SceneProbe.Scene)) return;

            _startAt = SceneProbe.FrameNo;
            if (_startBtn != null
                && TaskRunner.ClickButton(_startBtn, _startTry, "songSelectPlayButton(补点)"))
                _startFired = true;
            _startTry++;
            return;
        }

        if (_startTry >= TaskRunner.START_TRIES && SceneProbe.Scene != null)
        {
            End($"点了 {_startTry} 次都没开起来 —— 请你在选歌界面自己按「开始」");
            return;
        }

        if (SceneProbe.Scene == null && SceneProbe.FrameNo - _mark > TaskRunner.WAIT_REMIND)
        { EndSoft(); return; }

        if (SceneProbe.FrameNo % TaskRunner.WAIT_REMIND == 0)
        {
            Status = $"正在等《{_title}》起来…";
            Plugin.Logger.LogWarning($"[FreeRun] 《{_title}》还没起来 —— 正在自动补点「开始」，也可以自己按");
        }
    }

        static void EndSoft() => Reset("");

        static void End(string why, bool quiet = false)
    {
        string msg = why.Length > 0 ? why : "跳不过去";
        Reset(msg);

        Plugin.Logger.LogWarning($"[FreeRun] 中止：{msg}");
        if (!quiet) Win32Overlay.Toast.Queue("无法开始", msg, "曲目推荐");
    }

        static void Reset(string err)
    {
        _st = St.Idle;
        _active = false;
        Status = "";

        _songId = null; _diffFlag = null; _startBtn = null;
        if (err.Length > 0) LastError = err;
    }
}
