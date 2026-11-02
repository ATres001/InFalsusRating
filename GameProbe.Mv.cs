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

public static partial class GameProbe
{
    static int _mvStage;
    static int _mvAt;
    static int _mvTick0;
    static int _mvShotN;
    static int _mvNextLog;
    static int _mvVerifyAt;
    static int _mvKillFrames;
    const int MV_KILL_FRAMES = 4;
    static long _mvPrevFrame = -1;

    static object _mvAref, _mvHandle, _mvClip;

    static readonly Dictionary<string, object> _mvClipCache = new Dictionary<string, object>();
    static object _mvGo, _mvVideo, _mvRT;
    static object _mvCanvasGo, _mvCanvas, _mvRawGo, _mvRaw;
    static object _mvAudio;
    static readonly List<object[]> _mvFmodBuses = new List<object[]>();
    static object _mvTombCanvas, _mvTombGo;
    static string _mvName = "";
    static bool _mvWantGeneric;

    static bool _mvForExam;
    static bool _mvEnded;

    static string _mvBaseName;
    static object _mvMusicHandle;
    static string _mvMusicName = "";
    static object _mvMusicGroup;

    static int _mvPvSweepUntil;
    static int _mvPvSweepNext;
    static readonly List<string> _mvPvSweepNames = new List<string>();

    static void MvTick()
    {
        if (_mvKillFrames > 0)
        {
            _mvKillFrames--;
            if (_mvKillFrames == 0) MvKillNow();
        }

        int cmd = MvCmd;
        if (cmd != 0)
        {
            MvCmd = 0;
            try
            {
                if (cmd == 2) MvStop();
                else MvStart(cmd == 3, cmd == 4);
            }
            catch (Exception e)
            {
                W("MV： ⛔ 命令抛 " + EM(e));
                Flush();
            }
        }

        MvPvSweepTick();

        MvSyncState();

        if (_mvVerifyAt != 0 && Environment.TickCount >= _mvVerifyAt)
        {
            _mvVerifyAt = 0;
            W("MV： 拆除回读（`Destroy` 是**帧末**生效的，所以隔了两百毫秒才读）："
              + " canvas id=" + (IdOf(_mvTombCanvas) ?? "<?读不到>")
              + "   播放器物体 id=" + (IdOf(_mvTombGo) ?? "<?读不到>")
              + "   ←  ⚠ Unity 里**被销毁**的对象 `GetInstanceID()` 读回 **0**（别再读别的，会假 null）");
            _mvTombCanvas = null;
            _mvTombGo = null;
            Flush();
        }

        if (_mvStage == 1)
        {
            try
            {
                MvPoll();
            }
            catch (Exception e)
            {
                W("MV： ⛔ 轮询抛 " + EM(e));
                Flush();
            }

            return;
        }

        if (_mvStage < 2) return;

        if (SceneProbe.Game != null)
        {
            W("MV： ⚠ 谱面开起来了 —— 自建画布置顶铺满，会把整个游戏盖住，**自动收场**");
            MvStop();
            return;
        }

        int el = Environment.TickCount - _mvTick0;

        if (_mvStage == 2)
        {
            if (Environment.TickCount < _mvAt) return;
            MvSample(el);
            return;
        }

        if (_mvForExam && !_mvEnded)
        {
            long fr = -1;
            bool playing = true, prepared = false;
            object o;
            o = PV(_mvVideo, "frame");
            if (o != null)
            {
                try
                {
                    fr = Convert.ToInt64(o);
                }
                catch
                {
                }
            }

            o = PV(_mvVideo, "isPlaying");
            if (o != null)
            {
                try
                {
                    playing = Convert.ToBoolean(o);
                }
                catch
                {
                }
            }

            o = PV(_mvVideo, "isPrepared");
            if (o != null)
            {
                try
                {
                    prepared = Convert.ToBoolean(o);
                }
                catch
                {
                }
            }

            if (prepared && fr > 0 && !playing)
            {
                _mvEnded = true;
                W("MV： ▶ 这一片**放完了**（帧=" + fr + "/" + MvFrames
                  + "  时长=" + MvSeconds.ToString("0.0") + " 秒）—— 课题那边可以往下走了");
                Flush();
            }
        }

        if (Environment.TickCount >= _mvNextLog)
        {
            _mvNextLog = Environment.TickCount + 3000;
            W("MV： [稳态 " + (el / 1000f).ToString("F1") + "s]  帧=" + S(PV(_mvVideo, "frame"))
              + "/" + S(PV(_mvVideo, "frameCount"))
              + "  isPlaying=" + S(PV(_mvVideo, "isPlaying"))
              + "  isPrepared=" + S(PV(_mvVideo, "isPrepared"))
              + (_mvForExam ? "  放完=" + _mvEnded + "（课题放的，到头会自己报）" : "")
              + "  ⚠ 内容量看那三次采样；这里只看它还在不在动");
            Flush();
        }
    }

    static void MvSyncState()
    {
        MvState = _mvStage == 0 ? MvStateOff
            : _mvStage == 1 ? MvStateStarting
            : (_mvEnded ? MvStateEnded : MvStatePlaying);
    }

    public static void MvAbort(string why)
    {
        if (_mvStage == 0 && _mvMusicHandle == null && _mvVideo == null
            && _mvCanvasGo == null && _mvAudio == null && _mvFmodBuses.Count == 0)
            return;
        try
        {
            W("MV： ⚠ " + why + " —— 场景已经切走，强制收场（音乐先停）");
            MvStop();
        }
        catch (Exception e)
        {
            try { MvMusicHalt(); } catch { }
            try { MvRestoreAudio(); } catch { }
            _mvStage = 0;
            MvSyncState();
            W("MV： ⚠ 强制收场抛 " + EM(e));
            Flush();
        }
    }

    static int MvInt(object o, string name)
    {
        try
        {
            object v = PV(o, name);
            return v == null ? 0 : Convert.ToInt32(v);
        }
        catch
        {
            return 0;
        }
    }

    static float MvFloat(object o, string name)
    {
        try
        {
            object v = PV(o, name);
            return v == null ? 0f : Convert.ToSingle(v);
        }
        catch
        {
            return 0f;
        }
    }

    static void MvStart(bool forceGeneric, bool forExam = false)
    {
        if (_mvStage != 0)
        {
            if (!_mvWantGeneric && forceGeneric)
            {
                W("MV： 🔁 换片 —— 「" + _mvName + "」→ 通用过场。先拆掉正在播的那一个：");
                MvStop();
            }
            else if (_mvWantGeneric && !forceGeneric)
            {
                W("MV： 🔁 换片 —— 通用过场 → 这首歌自己的视频。先拆掉正在播的那一个：");
                MvStop();
            }
            else
            {
                W("MV： ⏭ **已经播着了**（stage=" + _mvStage + "，片名「" + _mvName + "」）—— 这一下什么都不做。"
                  + "**要拆按 Shift+F2**；要看另一片按 " + (_mvWantGeneric ? "F2" : "Alt+F2") + "。");
                Flush();
                return;
            }
        }

        if (_mvKillFrames > 0)
        {
            W("MV： ⏩ 上一轮的黑幕还剩 " + _mvKillFrames + " 帧 —— 立刻收掉旧画布再起新的");
            MvKillNow();
        }

        _mvForExam = forExam;
        _mvEnded = false;
        MvFrames = 0;
        MvSeconds = 0;

        Type goT = T("UnityEngine.GameObject");
        Type vpT = T("UnityEngine.Video.VideoPlayer");
        Type rtT = T("UnityEngine.RenderTexture");
        Type canvasT = T("UnityEngine.Canvas");
        Type rawT = T("UnityEngine.UI.RawImage");
        Type rtrT = T("UnityEngine.RectTransform");
        Type scT = T("UnityEngine.ScreenCapture");
        if (goT == null || vpT == null || rtT == null || canvasT == null || rawT == null || rtrT == null)
        {
            W("MV： ⛔ 类型不齐 —— GameObject=" + (goT != null) + " VideoPlayer=" + (vpT != null)
              + " RenderTexture=" + (rtT != null) + " Canvas=" + (canvasT != null)
              + " RawImage=" + (rawT != null) + " RectTransform=" + (rtrT != null)
              + "   ⇒ **一个都不差才能往下走**（缺的那个后面会读成一片静默的 `<null>`）");
            Flush();
            return;
        }

        if (scT == null) W("MV： ⚠ `UnityEngine.ScreenCapture` 没加载 —— 照样播，只是**这一轮的验票仪器少一半**");

        W("MV： ================= 基线：起播**前**的整屏 =================");
        MvShot("0-起播前");

        object aref = MvPickAsset(forceGeneric);
        if (aref == null)
        {
            Flush();
            return;
        }

        MethodInfo pick = null;
        var lines = new List<string>();
        try
        {
            foreach (var m in aref.GetType().GetMethods(F))
            {
                if (m.Name != "LoadAssetAsync") continue;
                var ps = m.GetParameters();
                lines.Add((m.IsGenericMethodDefinition ? "★泛型定义 " : "")
                          + (m.DeclaringType == null ? "?" : m.DeclaringType.Name) + "("
                          + string.Join(",", Array.ConvertAll(ps, x => x.ParameterType.Name)) + ")"
                          + "→" + m.ReturnType.Name);
                if (m.IsGenericMethodDefinition || ps.Length != 0) continue;
                if (pick == null) pick = m;
            }
        }
        catch (Exception e)
        {
            W("MV： ⛔ 枚举 LoadAssetAsync 抛 " + EM(e));
            Flush();
            return;
        }

        W("MV： `LoadAssetAsync` 运行时可见 " + lines.Count + " 个："
          + (lines.Count == 0 ? "**一个都没有**" : string.Join("  |  ", lines.ToArray())));
        if (pick == null)
        {
            W("MV： ⛔ **没有一个能调的**（带 ★ 的那种泛型定义不能直接 Invoke）—— 这条路不通");
            Flush();
            return;
        }

        W("MV： 采用 " + (pick.DeclaringType == null ? "?" : pick.DeclaringType.Name)
                    + "::LoadAssetAsync()  返回 " + pick.ReturnType.Name
                    + "  （泛型参数 " +
                    string.Join(",", Array.ConvertAll(pick.ReturnType.GetGenericArguments(), x => x.Name)) + "）");

        object h = null;

        object cached;
        if (!string.IsNullOrEmpty(_mvName) && _mvClipCache.TryGetValue(_mvName, out cached) && cached != null)
        {
            W("MV： ♻ 这一片**已经在内存里了**（表内名「" + _mvName + "」）—— 直接用，不调 `LoadAssetAsync`。"
              + "⚠ 这不是优化：实测第二次加载会返回**无效句柄**，视频就再也放不出来");
            MvUseClip(cached, "缓存");
            return;
        }

        for (int attempt = 1; attempt <= 2 && h == null; attempt++)
        {
            object use = aref;
            if (attempt > 1)
            {
                W("MV： ↻ 上一次没拿到能用的句柄 —— **重新从表里读一个 `AssetReference` 再试一次**");
                use = MvPickAsset(forceGeneric);
                if (use == null)
                {
                    W("MV： ⛔ 重读素材引用失败 —— 不试了");
                    break;
                }
            }

            object got = null;
            try
            {
                got = pick.Invoke(use, null);
            }
            catch (Exception e)
            {
                W("MV： ⛔ 第 " + attempt + " 次调 `LoadAssetAsync()` 抛 " + EM(e));
                continue;
            }

            if (got == null)
            {
                W("MV： ⛔ 第 " + attempt + " 次 `LoadAssetAsync()` 返回 null（这个重载没受理）");
                continue;
            }

            W("MV： 第 " + attempt + " 次拿到句柄：" + MvHandleTell(got));
            if (MvHandleOk(got))
            {
                h = got;
                aref = use;
                break;
            }

            W("MV： ⚠ 第 " + attempt + " 次的句柄**不能用**（`IsValid()` 报假）——"
              + " 这一格**不算「加载失败」**，那两种情形要往两个方向查");
        }

        if (h == null)
        {
            W("MV： ⛔ 两次都没拿到能用的句柄 —— 这一片不放了（上面每一行的读数就是账单）");
            Flush();
            return;
        }

        _mvAref = aref;
        _mvHandle = h;
        _mvStage = 1;
        _mvAt = Environment.TickCount;
        W("MV： 句柄 = " + FN(h) + " —— 开始轮询。"
          + "⚠ Addressables 的**失败是静默的**（不抛异常、只在句柄里记 `Status`），只能自己回来读");
        Flush();
    }

    static bool BaseWrap(object o)
    {
        string fn = FN(o), k = Kind(o);
        if (fn == "<null>" || string.IsNullOrEmpty(k) || k == "?") return false;
        if (fn == k) return false;
        return fn == "UnityEngine.Object" || fn == "UnityEngine.Component"
                                          || fn == "UnityEngine.Behaviour" || fn == "UnityEngine.MonoBehaviour";
    }

    static object MvPickAsset(bool forceGeneric)
    {
        MvPick = MvPickUnknown;

        _mvBaseName = null;

        object ss = SceneProbe.Scene;
        if (ss == null)
        {
            var ssl = FindAll(T("ifapp.Game.Scenes.SongSelectScene"));
            if (ssl.Count > 0)
            {
                ss = ssl[0];
                W("MV： ⚠ `SceneProbe.Scene` 是空的，现找了 " + ssl.Count + " 个 SongSelectScene，拿第一个顶上");
            }
        }

        if (ss == null)
        {
            W("MV： ⛔ 现在不在选曲场景（按类型也一个都没找到）—— 没有源头，什么都没建");
            return null;
        }

        object da = PV(ss, "dataAccess");
        if (da == null)
        {
            W("MV： ⛔ 选曲场景的 `dataAccess` 是空的 —— 取不到视频表");
            return null;
        }

        string sid = S(PV(ss, "_sr"));
        string diffKey = Fmt(PV(ss, "_Sr"));
        W("MV： 选曲界面当前选中 SongId=" + sid + "  难度=" + diffKey
          + (forceGeneric ? "   ← Alt+F2：**点名要通用过场**，不查专属那一格" : ""));

        object fp = null;

        if (!forceGeneric)
        {
            string sname = null;
            int scanned = 0;
            foreach (var s in Songs(da))
            {
                scanned++;
                if (S(P(s, "Id")) == sid)
                {
                    sname = S(P(s, "BaseName"));
                    break;
                }
            }

            if (string.IsNullOrEmpty(sname) || sname == "<null>")
            {
                W("MV：  ⚠ 全曲库（扫了 " + scanned + " 首）里没查到 Id=" + sid + " 的曲名 —— "
                  + "查不到曲名就查不到专属视频");
            }
            else
            {
                _mvBaseName = sname;
                string want = sname + "_bga.mp4";
                fp = VidQuiet(da, want);
                if (fp != null)
                {
                    W("MV：  ◆ 这一首**有专属视频**：「" + want + "」"
                      + "（全库 11 首有专属，分别是 codeleviathan / enigma / kiretsu / latentduality / "
                      + "memoire / phylaxron / tactom / tokonagi / zetta / albedo / kuukaku）");
                }
                else
                {
                    W("MV：  这一首没有专属视频（按曲名找了「" + want + "」，表里没有）。");
                }

                MvProbeMusic(sname);
            }

            if (fp == null)
            {
                MvPick = MvPickNone;
                _mvBaseName = null;
                W("MV：  ⛔ ⇒ **这一首不播过场**（没有专属视频；没有的歌不放默认视频）。");
                W("MV：     课题走到这儿会**直接开谱、不等**（`StepMvIntro` 看到 `MvPick=None`）。");
                Flush();
                return null;
            }
        }

        if (fp == null)
        {
            string mvName = diffKey == "Forbidden" ? "boss_song_reveal_bga.mp4" : "normal_song_reveal_bga.mp4";
            W("MV：  通用过场 = 「" + mvName + "」（" + (diffKey == "Forbidden" ? "boss" : "normal") + " 那一版"
              + "；⚠ 难度读出来若不是 Forbidden 就走 normal —— 难度那一格本身还有个「只落一半」的老账）");
            fp = VidEntry(da, mvName, "MV");
            if (fp == null)
            {
                W("MV： ⛔ 连通用那条都没取到 —— 上面摊出来的表就是对账单");
                return null;
            }
        }

        _mvName = S(P(fp, "AssetName"));
        _mvWantGeneric = forceGeneric;

        MvPick = MvPickHas;
        object aref = P(fp, "Asset");
        if (aref == null)
        {
            W("MV： ⛔ 「" + _mvName + "」这一条的 `Asset` 是空的");
            return null;
        }

        W("MV： 素材 = 「" + _mvName + "」  托管类=" + FN(aref) + "   原生类=" + Kind(aref)
          + (BaseWrap(aref) ? "   ⚠⚠ 托管那边是**基类包装** ⇒ 下面的 `LoadAssetAsync` 会静默失败" : ""));
        return aref;
    }

    static void MvProbeMusic(string baseName)
    {
        try
        {
            W("MV： ================ 找音乐：探针 ================");
            Type zfT = T("_J._zf");
            Type ifT = T("_J._IF");
            if (zfT == null)
            {
                W("MV： ⛔ `_J._zf`（按名字播放的那层门面）没加载 —— 这个探针做不了。"
                  + "（它是 Game.Audio.dll 里的，要么名字不对，要么这个程序集还没被用到）");
                return;
            }

            W("MV： `_J._zf` = " + S(zfT.FullName) + "   实例成员 "
              + SafeCount(zfT.GetMethods(F)) + " 个方法，静态字段 " + SafeCount(zfT.GetFields(FS)) + " 个");

            string wq, yq;
            object zf = RVA(zfT, new[] { "_psA" }, out wq, out yq);
            W("MV： 门面实例 `_J._zf._psA` → "
              + (zf == null ? "⛔ 没读到（" + yq + "）" : "有  " + FN(zf) + "   命中 " + wq));

            try
            {
                var sl = new List<string>();
                foreach (var f in zfT.GetFields(FS))
                    sl.Add(f.Name + ":" + f.FieldType.Name);
                W("MV： `_J._zf` 的静态字段（" + sl.Count + " 个）："
                  + (sl.Count == 0 ? "**一个都没有**" : string.Join(" ", sl.ToArray())));
            }
            catch (Exception e)
            {
                W("MV： ⚠ 摊 `_zf` 静态字段抛 " + EM(e));
            }

            if (zf == null)
            {
                try
                {
                    foreach (var f in zfT.GetFields(FS))
                    {
                        if (f.FieldType != zfT) continue;
                        object v = null;
                        try
                        {
                            v = f.GetValue(null);
                        }
                        catch
                        {
                        }

                        if (v != null)
                        {
                            zf = v;
                            W("MV：   ↳ 兜底：静态字段 `" + f.Name + "` 就是门面实例，拿它顶上");
                            break;
                        }
                    }
                }
                catch (Exception e)
                {
                    W("MV： ⚠ 兜底找单例抛 " + EM(e));
                }
            }

            MethodInfo yfa = null, xfa = null;
            try
            {
                foreach (var m in zfT.GetMethods(F | BindingFlags.Static))
                {
                    if (!m.IsGenericMethodDefinition && m.Name == "_yFA"
                                                     && m.GetParameters().Length == 1 && yfa == null) yfa = m;
                    if (!m.IsGenericMethodDefinition && m.Name == "_XFA"
                                                     && m.GetParameters().Length == 1 && xfa == null) xfa = m;
                }
            }
            catch (Exception e)
            {
                W("MV： ⛔ 枚举 `_yFA`/`_XFA` 抛 " + EM(e));
            }

            W("MV： `_yFA(String)`=" + (yfa == null ? "⛔ 没有" : (yfa.IsStatic ? "静态" : "实例"))
                                    + "   `_XFA(String)`=" + (xfa == null ? "⛔ 没有" : (xfa.IsStatic ? "静态" : "实例")));

            if (xfa != null)
            {
                foreach (var nm in new[] { "SP_MainTitle.wav", "SP_MainTitle", "_bga.wav", "tokonagi_bga.wav" })
                {
                    try
                    {
                        object r = xfa.Invoke(xfa.IsStatic ? null : zf,
                            MvFit(xfa.GetParameters(), new object[] { nm }));
                        W("MV：   `_XFA(\"" + nm + "\")` → " + S(r));
                    }
                    catch (Exception e)
                    {
                        W("MV：   `_XFA(\"" + nm + "\")` ⛔ " + EM(e));
                    }
                }
            }

            if (yfa == null)
            {
                W("MV： ⛔ 没有可用的 `_yFA` —— 到此为止");
                return;
            }

            object yfaInst = yfa.IsStatic ? null : zf;
            if (!yfa.IsStatic && yfaInst == null)
            {
                W("MV： ⛔ `_yFA` 是实例方法而门面实例是空的 —— 到此为止");
                return;
            }

            var plan = new List<string[]>();
            plan.Add(new[] { "正对照", "SP_MainTitle.wav" });
            plan.Add(new[] { "正对照", "SP_Dive2.wav" });
            plan.Add(new[] { "正对照", "SP_musicselect_SelfMaster_Loop.wav" });
            plan.Add(new[] { "正对照", "IF_OpeningSFX_final.wav" });
            plan.Add(new[] { "正对照·不带扩展名", "SP_MainTitle" });
            plan.Add(new[] { "负对照(**必须报「不在」**)", "__no_such_sound_zzq__.wav" });
            if (!string.IsNullOrEmpty(baseName) && baseName != "<null>")
            {
                foreach (var stem in new[] { baseName, baseName.ToLowerInvariant(), baseName.ToUpperInvariant() })
                {
                    foreach (var pat in new[]
                             {
                                 "_bga.wav", ".wav", "_bga", "_music.wav", "_bgm.wav", "_song.wav",
                                 "_full.wav", "_preview.wav", "_bga_audio.wav", "_inst.wav",
                             })
                        plan.Add(new[] { "本曲", stem + pat });
                    plan.Add(new[] { "本曲", "SP_" + stem + ".wav" });
                }
            }
            else
            {
                W("MV： ⚠ 没拿到曲名（走的是通用过场那条路）—— 只跑对照，不猜本曲的名字");
            }

            int nYes = 0, nNo = 0, nErr = 0;
            var yes = new List<string>();
            foreach (var row in plan)
            {
                string tag = row[0], nm = row[1], r;
                try
                {
                    object v = yfa.Invoke(yfaInst, MvFit(yfa.GetParameters(), new object[] { nm }));
                    r = S(v);
                }
                catch (Exception e)
                {
                    r = "⛔" + EM(e);
                }

                if (r == "True")
                {
                    nYes++;
                    yes.Add(nm);
                }
                else if (r == "False") nNo++;
                else nErr++;

                W("MV：   [" + tag + "] `_yFA(\"" + nm + "\")` → " + r);
            }

            W("MV： 探针小结：报「在」" + nYes + " 个，报「不在」" + nNo + " 个，读不出来 " + nErr + " 个");
            if (yes.Count > 0) W("MV：   报「在」的是：" + string.Join(" ｜ ", yes.ToArray()));
            W("MV： ⚠ 这一段的**结论要看正对照**：正对照报「不在」⇒ `_yFA` 不是「在不在」这个意思，"
              + "本曲那几行**全部作废**（换 `_XFA` 那几行看命名约定）");
        }
        catch (Exception e)
        {
            W("MV： ⛔ 音乐探针整个抛了 " + EM(e) + " —— **不影响播放**，路照样往下走");
        }
    }

    static int SafeCount(Array a)
    {
        try
        {
            return a == null ? -1 : a.Length;
        }
        catch
        {
            return -1;
        }
    }

    static void MvPoll()
    {
        if (_mvHandle == null) return;

        string st = PX(_mvHandle, "Status");
        bool done = false;
        try
        {
            object d = P(_mvHandle, "IsDone");
            done = d != null && Convert.ToBoolean(d);
        }
        catch
        {
        }

        if (!done) done = st == "Succeeded" || st == "Failed";

        if (!done)
        {
            if (Environment.TickCount - _mvAt > 30000)
            {
                W("MV： ⛔ 30 秒没落定，放弃轮询（Status 停在 " + st + "）");
                _mvHandle = null;
                _mvStage = 0;
                Flush();
            }

            return;
        }

        if (st != "Succeeded")
        {
            object oe = P(_mvHandle, "OperationException");
            W("MV： ⛔ 加载**失败** Status=" + st
                                       + (oe == null ? "" : "   " + S(P(oe, "Message"))));
            W("MV：   句柄体检：" + MvHandleTell(_mvHandle));
            W("MV：   常见原因：地址名和 catalog 里那个对不上、或者那个包不在 StreamingAssets/aa 里");
            _mvHandle = null;
            _mvStage = 0;
            Flush();
            return;
        }

        object clip = P(_mvHandle, "Result");
        if (clip == null)
        {
            W("MV： ⛔ Status=Succeeded 但 `Result` 是 `<null>` —— 这一格不能当成「加载到空资产」");
            W("MV：   句柄体检：" + MvHandleTell(_mvHandle));
            _mvHandle = null;
            _mvStage = 0;
            Flush();
            return;
        }

        _mvHandle = null;
        MvUseClip(clip, "新加载");
    }

    static string MvHandleTell(object h)
    {
        if (h == null) return "<null>";
        string valid;
        try
        {
            var m = h.GetType().GetMethod("IsValid", F);
            valid = m == null ? "（没有这个方法）" : S(m.Invoke(h, null));
        }
        catch (Exception e)
        {
            valid = "抛 " + EM(e);
        }

        return FN(h) + "   IsValid=" + valid
               + "   Status=" + PX(h, "Status")
               + "   IsDone=" + PX(h, "IsDone")
               + "   InternalOp=" + PX(h, "InternalOp");
    }

    static bool MvHandleOk(object h)
    {
        if (h == null) return false;
        try
        {
            var m = h.GetType().GetMethod("IsValid", F);
            if (m == null) return true;
            object r = m.Invoke(h, null);
            return r == null || Convert.ToBoolean(r);
        }
        catch
        {
            return true;
        }
    }

    static void MvUseClip(object clip, string why)
    {
        _mvClip = clip;
        if (!string.IsNullOrEmpty(_mvName)) _mvClipCache[_mvName] = clip;
        W("MV： ✅ 资产到手（" + why + "）：" + Fmt(clip) + "   托管类=" + FN(clip) + "  原生类=" + Kind(clip));
        string cy;
        W("MV：   体检  帧数=" + S(RV(clip, "frameCount", out cy))
                          + "  帧率=" + S(RV(clip, "frameRate", out cy))
                          + "  尺寸=" + S(RV(clip, "width", out cy)) + "×" + S(RV(clip, "height", out cy))
                          + "  时长=" + S(RV(clip, "length", out cy)) + " 秒"
                          + "  文件=" + S(RV(clip, "originalPath", out cy)));

        MvFrames = MvInt(clip, "frameCount");
        MvSeconds = MvFloat(clip, "length");
        W("MV：   → 给课题看的：帧数=" + MvFrames + "  时长=" + MvSeconds.ToString("0.00") + " 秒"
          + (MvSeconds > 0.5f ? "" : "   ⚠ **时长读不到** ⇒ 课题那边会走「很宽的保险丝」那一档"));
        Flush();
        MvBuild();
    }

    static void MvBuild()
    {
        Type goT = T("UnityEngine.GameObject");
        Type vpT = T("UnityEngine.Video.VideoPlayer");
        Type rtT = T("UnityEngine.RenderTexture");
        Type rmodeT = T("UnityEngine.Video.VideoRenderMode");
        Type aomT = T("UnityEngine.Video.VideoAudioOutputMode");
        Type canvasT = T("UnityEngine.Canvas");
        Type crmodeT = T("UnityEngine.RenderMode");
        Type rawT = T("UnityEngine.UI.RawImage");
        Type texT = T("UnityEngine.Texture");
        Type vec2T = T("UnityEngine.Vector2");
        _ = texT;

        object rt = null;
        try
        {
            rt = Activator.CreateInstance(rtT, new object[] { 1920, 1080, 0 });
        }
        catch (Exception e)
        {
            W("MV： ⛔ `new RenderTexture(1920,1080,0)` 抛 " + EM(e));
        }

        if (rt == null)
        {
            W("MV： ⛔ RenderTexture 建不出来 —— 到此为止");
            MvStop();
            return;
        }

        _mvRT = rt;
        MvCall(rt, "Create", 0, new object[0]);
        W("MV： ① 自建 RenderTexture  id=" + IdOf(rt)
                                        + "  尺寸=" + S(PV(rt, "width")) + "×" + S(PV(rt, "height")));
        if (IdOf(rt) == null)
            W("MV：   ⚠ `GetInstanceID()` 读不到 —— 下面靠实例 ID 认「是不是同一张」的那几行会**恒为假**");

        object go = null;
        try
        {
            go = Activator.CreateInstance(goT, new object[] { "InFalsusRating_MV" });
        }
        catch (Exception e)
        {
            W("MV： ⛔ `new GameObject` 抛 " + EM(e));
        }

        if (go == null)
        {
            W("MV： ⛔ GameObject 建不出来 —— 到此为止");
            MvStop();
            return;
        }

        _mvGo = go;

        object vp = MvAdd(go, vpT, "VideoPlayer");
        if (vp == null)
        {
            W("MV： ⛔ VideoPlayer 加不上 —— 到此为止");
            MvStop();
            return;
        }

        _mvVideo = vp;

        string w1, y1;
        bool okPa = SetX(vp, new[] { "playOnAwake" }, false, out w1, out y1);

        bool loop = !_mvForExam;
        bool okLp = SetX(vp, new[] { "isLooping" }, loop, out w1, out y1);
        SetX(vp, new[] { "waitForFirstFrame" }, true, out w1, out y1);
        W("MV： ② playOnAwake←false " + (okPa ? "✅" : "⛔ " + y1)
                                     + "   isLooping←" + (loop ? "true（热键放的：循环）" : "false（**课题放的：放一遍就完**）")
                                     + (okLp ? " ✅" : " ⛔ " + y1));

        object asrc = MvAdd(go, T("UnityEngine.AudioSource"), "AudioSource");
        _mvAudio = asrc;
        if (asrc != null)
        {
            SetX(asrc, new[] { "playOnAwake" }, false, out w1, out y1);
            SetX(asrc, new[] { "volume" }, 1f, out w1, out y1);

            bool okIgn = SetX(asrc, new[] { "ignoreListenerPause" }, true, out w1, out y1);
            W("MV：   +AudioSource  id=" + IdOf(asrc) + "   playOnAwake←false  volume←1"
              + "   ignoreListenerPause←" + (okIgn ? "true ✅（`AudioListener.pause` 冻不住它）" : "⛔ " + y1));
        }

        string wA = "（`VideoAudioOutputMode` 没加载，跳过）";
        if (aomT == null)
        {
        }
        else if (asrc == null)
        {
            wA = "⛔ AudioSource 没挂上 ⇒ 只能维持 None";
            SetX(vp, new[] { "audioOutputMode" }, Enum.ToObject(aomT, 0), out w1, out y1);
        }
        else
        {
            wA = SetX(vp, new[] { "audioOutputMode" }, Enum.ToObject(aomT, 1), out w1, out y1)
                ? "AudioSource(1)"
                : "⛔ " + y1;
            object st = MvCall(vp, "SetTargetAudioSource", 2, new object[] { 0, asrc });
            W("MV：   `SetTargetAudioSource(0, 自建AudioSource)` 叫了（返回 " + (st == null ? "void" : S(st)) + "）");
        }

        string ct = S(RV(vp, "audioTrackCount", out y1));
        string cc = S(RV(_mvClip, "audioTrackCount", out y1));
        W("MV：   音频轨数： 播放器报 " + ct + "   视频资产报 " + cc
          + "   ← **0 = 这个文件本身就是默片**，接不接都没声音");
        W("MV：    audioOutputMode = " + wA);

        bool unknownTracks = ct == "<null>" && cc == "<null>";
        bool hasAudio = unknownTracks || ct != "0" || cc != "0";
        MvRestoreAudio();
        MvAudioCensus("起播前");
        if (unknownTracks)
            W("MV： ⚠ 轨数**两处都读不出来** ⇒ 按「有」处理");
        if (!hasAudio)
            W("MV： ⏭ 这一片没有音轨 —— 但**照样**把游戏 BGM 让开：" + "宁可安静，也好过两套声音打架");

        MvMusicPlay();
        MvMuteGameAudio();

        bool okRm = false;
        if (rmodeT == null) W("MV： ⛔ `UnityEngine.Video.VideoRenderMode` 没加载 ⇒ renderMode 没设");
        else
        {
            okRm = SetX(vp, new[] { "renderMode" }, Enum.ToObject(rmodeT, 2), out w1, out y1);
            if (!okRm) W("MV： ⛔ 写 renderMode 失败：" + y1);
        }

        string wT = SetX(vp, new[] { "targetTexture" }, rt, out w1, out y1) ? null : y1;
        string wC = SetX(vp, new[] { "clip" }, _mvClip, out w1, out y1) ? null : y1;

        object rtBack = PV(vp, "targetTexture");
        object clipBack = PV(vp, "clip");
        W("MV： ③ 配置回读： renderMode=" + S(PV(vp, "renderMode"))
                                    + "（要 **RenderTexture**）"
                                    + "   targetTexture=" + (SameObj(rtBack, rt)
                                        ? "✅ 就是自建那张（id=" + IdOf(rtBack) + "）"
                                        : "⛔ **不是**自建那张：" + Fmt(rtBack) + "  vs 自建 id=" + IdOf(rt))
                                    + "   clip=" + (SameObj(clipBack, _mvClip) ? "✅" : "⛔ " + Fmt(clipBack))
                                    + "   isLooping=" + S(PV(vp, "isLooping"))
                                    + "   playOnAwake=" + S(PV(vp, "playOnAwake")));
        if (wT != null || wC != null)
            W("MV：   ⛔ 写的时候就没成： targetTexture=" + (wT ?? "ok") + " / clip=" + (wC ?? "ok"));

        object cgo = null;
        try
        {
            cgo = Activator.CreateInstance(goT, new object[] { "InFalsusRating_MVCanvas" });
        }
        catch (Exception e)
        {
            W("MV： ⛔ `new GameObject`（画布）抛 " + EM(e));
        }

        if (cgo == null)
        {
            W("MV： ⛔ 画布物体建不出来");
            MvStop();
            return;
        }

        _mvCanvasGo = cgo;

        object canvas = MvAdd(cgo, canvasT, "Canvas");
        if (canvas == null)
        {
            W("MV： ⛔ Canvas 加不上");
            MvStop();
            return;
        }

        _mvCanvas = canvas;

        if (crmodeT == null) W("MV： ⛔ `UnityEngine.RenderMode` 没加载 ⇒ 画布模式没设（默认就是 Overlay，多半没事）");
        else if (!SetX(canvas, new[] { "renderMode" }, Enum.ToObject(crmodeT, 0), out w1, out y1))
            W("MV： ⛔ 写 canvas.renderMode 失败：" + y1);

        if (!SetX(canvas, new[] { "sortingOrder" }, 32000, out w1, out y1))
            W("MV： ⛔ 写 canvas.sortingOrder 失败：" + y1);

        object rgo = null;
        try
        {
            rgo = Activator.CreateInstance(goT, new object[] { "InFalsusRating_MVImage" });
        }
        catch (Exception e)
        {
            W("MV： ⛔ `new GameObject`（图）抛 " + EM(e));
        }

        if (rgo == null)
        {
            W("MV： ⛔ 图物体建不出来");
            MvStop();
            return;
        }

        _mvRawGo = rgo;

        object raw = MvAdd(rgo, rawT, "RawImage");
        if (raw == null)
        {
            W("MV： ⛔ RawImage 加不上");
            MvStop();
            return;
        }

        _mvRaw = raw;

        object rtr = PV(raw, "rectTransform") ?? CompOf(rgo, T("UnityEngine.RectTransform"));
        object ctr = P(canvas, "transform");
        if (rtr == null || ctr == null)
            W("MV： ⛔ rectTransform=" + (rtr == null ? "<null>" : "ok") + "  canvas.transform=" +
              (ctr == null ? "<null>" : "ok")
              + " ⇒ **铺不满屏**（物体可能叠在屏幕左下角一个点里）");
        else
        {
            object st = MvCall(rtr, "SetParent", 2, new object[] { ctr, false });

            object v00 = null, v11 = null;
            try
            {
                v00 = Activator.CreateInstance(vec2T, new object[] { 0f, 0f });
                v11 = Activator.CreateInstance(vec2T, new object[] { 1f, 1f });
            }
            catch
            {
            }

            if (v00 == null || v11 == null) W("MV： ⛔ `Vector2` 构造失败 ⇒ 锚点没设");
            else
            {
                SetX(rtr, new[] { "anchorMin" }, v00, out w1, out y1);
                SetX(rtr, new[] { "anchorMax" }, v11, out w1, out y1);
                SetX(rtr, new[] { "offsetMin" }, v00, out w1, out y1);
                SetX(rtr, new[] { "offsetMax" }, v00, out w1, out y1);
                W("MV： ④ 图铺满屏： SetParent=" + (st == null ? "叫过了" : "返回 " + S(st))
                                           + "  anchorMin=" + S(PV(rtr, "anchorMin")) + "  anchorMax=" +
                                           S(PV(rtr, "anchorMax"))
                                           + "  offsetMin=" + S(PV(rtr, "offsetMin")) + "  offsetMax=" +
                                           S(PV(rtr, "offsetMax")));
            }
        }

        SetX(raw, new[] { "raycastTarget" }, false, out w1, out y1);
        string wTex = SetX(raw, new[] { "texture" }, rt, out w1, out y1) ? null : y1;

        object texBack = PV(raw, "texture");
        W("MV： ⑤ 上屏链： RawImage.texture=" + (SameObj(texBack, rt)
                                             ? "✅ 就是我们那张 RT（id=" + IdOf(texBack) + "）"
                                             : "⛔ **不是**（" + Fmt(texBack) + "）" +
                                               (wTex == null ? "" : "  写的时候：" + wTex))
                                         + "   raycastTarget=" + S(PV(raw, "raycastTarget")));
        object rootC = PV(canvas, "rootCanvas");
        W("MV：   canvas.renderMode=" + S(PV(canvas, "renderMode")) + "（**0 = ScreenSpaceOverlay**）"
          + "   sortingOrder=" + S(PV(canvas, "sortingOrder"))
          + "   canvas.enabled=" + S(PV(canvas, "enabled"))
          + "   rootCanvas=" + (SameObj(rootC, canvas) ? "✅ 就是它自己（已经进 UI 系统了）" : Fmt(rootC)));
        W("MV：   activeInHierarchy  画布=" + S(PV(cgo, "activeInHierarchy"))
                                         + "  图=" + S(PV(rgo, "activeInHierarchy"))
                                         + "  播放器=" + S(PV(go, "activeInHierarchy"))
                                         + "   ← ⚠ 这一条**任一环 false 就是「照播不误、一个像素没有」**（上一轮 MV 的教训）");

        object r = MvCall(vp, "Play", 0, new object[0]);
        W("MV： ⑥ `Play()` 叫了（" + (r == null ? "void" : "返回 " + S(r)) + "）"
          + "   一帧后回读 isPlaying / isPrepared");

        _mvStage = 2;
        _mvTick0 = Environment.TickCount;
        _mvAt = _mvTick0 + 1000;
        _mvShotN = 0;
        _mvPrevFrame = -1;
        _mvNextLog = 0;
        Flush();
    }

    static object MvAdd(object go, Type compT, string label)
    {
        if (go == null || compT == null) return null;
        var m = MT(go, "AddComponent", typeof(Type));
        if (m == null)
        {
            W("MV： ⛔ `AddComponent(Type)` 一个重载都没取到（**不是「加不上这个组件」**）");
            return null;
        }

        object a0 = Fit(compT, m.GetParameters()[0].ParameterType);
        if (a0 == null)
        {
            W("MV： ⛔ `AddComponent` 的 Type 参数转换失败（**不是「加不上」**）");
            return null;
        }

        object raw = null;
        try
        {
            raw = m.Invoke(go, new object[] { a0 });
        }
        catch (Exception e)
        {
            W("MV： ⛔ `AddComponent(" + label + ")` 抛 " + EM(e));
            return null;
        }

        if (raw == null)
        {
            W("MV： ⛔ `AddComponent(" + label + ")` 返回 <null>");
            return null;
        }

        object tip = Retip(raw, compT);

        bool okT = false;
        try
        {
            okT = compT.IsInstanceOfType(tip);
        }
        catch
        {
        }

        W("MV：    +" + label + "  原样 wrapper=" + FN(raw) + "   原生类=" + Kind(raw)
          + "   ⇒ `Retip` 之后 wrapper=" + FN(tip) + "  id=" + IdOf(tip)
          + (okT
              ? "   ✅ 是 " + compT.Name + " 的包装"
              : "   ⚠ **还是基类包装** —— 下面往它身上写的全都会读成「没这个名字」"));
        return tip;
    }

    static void MvSample(int el)
    {
        _mvShotN++;
        long fr = -1;
        try
        {
            object f = PV(_mvVideo, "frame");
            if (f != null) fr = Convert.ToInt64(f);
        }
        catch
        {
        }

        string tag = _mvShotN + "-第" + (el / 1000f).ToString("F1") + "s";

        W("MV： ================= 采样 [" + tag + "] =================");
        W("MV：   状态量  帧=" + fr + "/" + S(PV(_mvVideo, "frameCount"))
          + "  isPlaying=" + S(PV(_mvVideo, "isPlaying"))
          + "  isPrepared=" + S(PV(_mvVideo, "isPrepared"))
          + "  上一次采样的帧=" + _mvPrevFrame
          + "   ← ⚠ **帧号不动 ⇒ 下面的比对全部作废**（先看这一行，再看图）");

        float[] rm = new float[144];
        int nr = MvRtMap(_mvRT, tag + " 自建RT", rm);
        if (nr == 144) MvPrintMap(tag + " 自建RT", rm);
        else W("MV：   ⛔ 自建 RT 这一格**没验到**（采到 " + nr + " 个点）—— 别把它读成「RT 是黑的」");

        MvShot(tag);

        _mvPrevFrame = fr;
        if (_mvShotN >= 3)
        {
            _mvStage = 3;
            _mvNextLog = Environment.TickCount + 3000;
            W("MV： 【采样结束】三次采完，进稳态。**要拆按 Shift+F2**。"
              + "拆不掉或想全撤就按 **F11**（强行收场，它也管这一块）");
        }
        else _mvAt = Environment.TickCount + 2000;

        Flush();
    }

    static void MvStop()
    {
        MvAnyKeySkip = false;

        if (_mvKillFrames > 0)
        {
            W("MV： ⏭ 拆除已经在收尾（黑幕还剩 " + _mvKillFrames + " 帧）—— 这一下什么都不做");
            Flush();
            return;
        }

        if (_mvStage == 0 && _mvVideo == null && _mvCanvasGo == null
            && _mvMusicHandle == null && _mvFmodBuses.Count == 0)
        {
            W("MV： ⏭ 本来就没播，这一下什么都不做");
            Flush();
            return;
        }

        W("MV： ================= 拆除 =================");
        try { MvMusicHalt(); } catch (Exception e) { W("MV： ⚠ 先停音乐时抛 " + EM(e)); }
        try { MvRestoreAudio(); } catch (Exception e) { W("MV： ⚠ 还原音量时抛 " + EM(e)); }

        try
        {
        if (_mvVideo != null)
        {
            MvCall(_mvVideo, "Stop", 0, new object[0]);
            W("MV： `VideoPlayer.Stop()` 叫了");
        }

        if (_mvAudio != null)
        {
            MvCall(_mvAudio, "Stop", 0, new object[0]);
            string aw, ay;
            SetX(_mvAudio, new[] { "clip" }, null, out aw, out ay);
            W("MV： `AudioSource.Stop()` 叫了，clip 摘掉");
        }

        if (_mvCanvasGo != null && _mvRaw != null && MvNeedsBlackout())
        {
            var black = default(UnityEngine.Color);
            black.r = 0f;
            black.g = 0f;
            black.b = 0f;
            black.a = 1f;
            string cw, cy;
            bool ok = SetX(_mvRaw, new[] { "color" }, black, out cw, out cy);
            if (ok)
            {
                string tw, ty;
                bool tok = SetX(_mvRaw, new[] { "texture" }, null, out tw, out ty);
                _mvKillFrames = MV_KILL_FRAMES;
                W("MV： 🌑 窗口宽高比 " + MvAspectText() + "（不是 16:9）⇒ 画布先压 " + MV_KILL_FRAMES
                  + " 帧**全屏不透明黑**再拆（边条只有我们自己重绘得到）"
                  + "   color 回读=" + S(PV(_mvRaw, "color"))
                  + "   texture 摘掉=" + (tok ? "✅" : "⚠ " + ty));
            }
            else
                W("MV： ⛔ 黑幕写 `RawImage.color` 没成（" + cy + "）—— 退回当帧拆；"
                  + "边条可能还会留着最后一帧，切一下分辨率就干净");
        }

        bool defer = _mvKillFrames > 0;
        if (!defer)
        {
            _mvTombCanvas = _mvCanvasGo;
            bool d1 = MvDestroy(_mvCanvasGo);
            W("MV： 拆物体  画布=" + (d1 ? "已叫 `Destroy`" : "（没有 / 没拆成）"));
            if (_mvRT != null)
            {
                MvCall(_mvRT, "Release", 0, new object[0]);
                W("MV： `RenderTexture.Release()` 叫了（id=" + IdOf(_mvRT) + "）");
            }

            _mvCanvasGo = null;
            _mvRawGo = null;
            _mvCanvas = null;
            _mvRaw = null;
            _mvRT = null;
            _mvVerifyAt = Environment.TickCount + 200;
        }
        else W("MV： 画布 / RT **先留着**（黑幕），" + MV_KILL_FRAMES + " 帧后由 `MvKillNow` 拆");

        _mvTombGo = _mvGo;
        bool d2 = MvDestroy(_mvGo);
        W("MV： 拆物体  播放器=" + (d2 ? "已叫 `Destroy`" : "（没有 / 没拆成）"));

        }
        catch (Exception e)
        {
            W("MV： ⚠ 拆视频 / 画布时抛 " + EM(e) + "（音乐已先停）");
        }

        try { MvAudioCensus("拆除后"); } catch { }
        _mvStage = 0;
        _mvShotN = 0;
        _mvPrevFrame = -1;
        _mvNextLog = 0;
        _mvVideo = null;
        _mvGo = null;
        _mvAudio = null;
        _mvHandle = null;
        _mvClip = null;
        _mvForExam = false;
        _mvEnded = false;

        MvAnyKeySkip = false;
        MvSyncState();
        Flush();
    }

    static void MvKillNow()
    {
        _mvKillFrames = 0;
        _mvTombCanvas = _mvCanvasGo;
        bool d1 = MvDestroy(_mvCanvasGo);
        W("MV： 🌑 黑幕结束 —— 拆画布=" + (d1 ? "已叫 `Destroy`" : "（没有 / 没拆成）")
                                + "   回读安排在两百毫秒后");
        if (_mvRT != null)
        {
            MvCall(_mvRT, "Release", 0, new object[0]);
            W("MV： `RenderTexture.Release()` 叫了（id=" + IdOf(_mvRT) + "，黑幕之后才放）");
        }

        _mvCanvasGo = null;
        _mvRawGo = null;
        _mvCanvas = null;
        _mvRaw = null;
        _mvRT = null;
        _mvVerifyAt = Environment.TickCount + 200;
        Flush();
    }

    static bool MvNeedsBlackout()
    {
        double a = MvAspect();
        if (a <= 0) return true;
        return Math.Abs(a - 16.0 / 9.0) > 0.01;
    }

    static double MvAspect()
    {
        try
        {
            Type st = T("UnityEngine.Screen");
            if (st == null) return 0;
            var pw = st.GetProperty("width", FS);
            var ph = st.GetProperty("height", FS);
            if (pw == null || ph == null) return 0;
            int w = Convert.ToInt32(pw.GetValue(null));
            int h = Convert.ToInt32(ph.GetValue(null));
            if (w <= 0 || h <= 0) return 0;
            return (double)w / h;
        }
        catch
        {
            return 0;
        }
    }

    static string MvAspectText()
    {
        double a = MvAspect();
        return a <= 0 ? "<读不到>" : a.ToString("0.000", CultureInfo.InvariantCulture);
    }

    static void MvAudioCensus(string when)
    {
        Type asT = T("UnityEngine.AudioSource");
        if (asT == null)
        {
            W("MV： 声音普查[" + when + "]：`UnityEngine.AudioSource` 没加载");
            return;
        }

        var l = FindAll(asT);
        int playing = 0;
        var show = new List<string>();
        foreach (var a in l)
        {
            bool p = false;
            try
            {
                object x = PV(a, "isPlaying");
                p = x != null && Convert.ToBoolean(x);
            }
            catch
            {
            }

            if (!p) continue;
            playing++;
            if (show.Count < 6)
            {
                object cl = PV(a, "clip");
                show.Add((cl == null ? "（无 clip）" : "「" + S(P(cl, "name")) + "」")
                         + " vol=" + S(PV(a, "volume")) + " id=" + IdOf(a));
            }
        }

        W("MV： 声音普查[" + when + "]：场上 AudioSource 共 " + l.Count + " 个，其中正在播 " + playing + " 个"
          + (show.Count == 0 ? "" : "  →  " + string.Join("  ｜  ", show.ToArray()))
          + (l.Count == 0 ? "   ← ⚠ 一个都没有 ⇒ 游戏的声音**不走 Unity AudioSource**" : ""));
    }

    static int MvIfNamed;

    static bool MvLooksFmod(Type t)
    {
        if (t == null) return false;
        try
        {
            foreach (var p in t.GetProperties(FS))
            {
                var pt = p.PropertyType;
                if (pt != null && pt.FullName != null &&
                    pt.FullName.IndexOf("FMOD", StringComparison.Ordinal) >= 0) return true;
            }

            foreach (var f in t.GetFields(FS))
            {
                var ft = f.FieldType;
                if (ft != null && ft.FullName != null &&
                    ft.FullName.IndexOf("FMOD", StringComparison.Ordinal) >= 0) return true;
            }
        }
        catch
        {
        }

        return false;
    }

    static Type MvScanForIF()
    {
        MvIfNamed = 0;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] ts;
            try
            {
                ts = asm.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                ts = ex.Types;
            }
            catch
            {
                continue;
            }

            if (ts == null) continue;
            foreach (var t in ts)
            {
                if (t == null || t.Name != "_IF") continue;
                MvIfNamed++;
                if (MvLooksFmod(t)) return t;
            }
        }

        return null;
    }

    static Type MvFmodManager()
    {
        Type hit = MvScanForIF();
        if (hit != null) return hit;

        bool has = false;
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                if (a.GetName().Name == "Game.Audio")
                {
                    has = true;
                    break;
                }
            }
            catch
            {
            }
        }

        if (!has)
        {
            string load;
            try
            {
                Assembly.Load("Game.Audio");
                load = "`Assembly.Load(\"Game.Audio\")` 叫成了";
            }
            catch (Exception e)
            {
                load = "⛔ `Assembly.Load(\"Game.Audio\")` 抛 " + EM(e);
            }

            hit = MvScanForIF();
            W("MV：   （`Game.Audio` 不在场 ⇒ " + load + "，重扫后 " + (hit != null ? "**找到了**" : "还是没有") + "）");
            if (hit != null) return hit;
        }

        W("MV： ⛔ 翻遍 " + AppDomain.CurrentDomain.GetAssemblies().Length + " 个在场程序集都没找到 FMOD 音频管理器："
          + "名字叫 `_IF` 的类型有 " + MvIfNamed + " 个，其中带 `FMOD` 静态成员的 **0** 个");
        return null;
    }

    static string MvMusicWanted()
    {
        if (string.IsNullOrEmpty(_mvBaseName) || _mvBaseName == "<null>") return null;

        return _mvBaseName.ToLowerInvariant() + "_bga.wav";
    }

    static IntPtr MvHandleOf(object g)
    {
        if (g == null) return IntPtr.Zero;
        try
        {
            var f = g.GetType().GetField("handle", F);
            if (f == null) return IntPtr.Zero;
            object v = f.GetValue(g);
            if (v is IntPtr ip) return ip;
            if (v is long l) return new IntPtr(l);
            return IntPtr.Zero;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    static object MvBusGroup(object bus)
    {
        if (bus == null) return null;
        try
        {
            Type ifT = MvFmodManager();
            if (ifT == null) return null;
            string wq, yq;
            object lf = RVA(ifT, new[] { "_DtA" }, out wq, out yq);
            if (lf == null)
            {
                W("MV： ⛔ 音乐：`_DtA` 没读到（" + yq + "）⇒ 总线映射查不了");
                return null;
            }

            Type lfT = lf.GetType();

            MethodInfo chA = null;
            foreach (var m in lfT.GetMethods(F | BindingFlags.Static))
                if (m.Name == "_chA" && !m.IsGenericMethodDefinition && m.GetParameters().Length == 2)
                {
                    chA = m;
                    break;
                }

            if (chA == null)
            {
                W("MV： ⛔ 音乐：`_lF` 上没有 2 参的 `_chA` ⇒ 总线映射查不了");
                return null;
            }

            object g = null;
            try
            {
                g = chA.Invoke(null, MvFit(chA.GetParameters(), new object[] { lf, bus }));
            }
            catch (Exception e)
            {
                W("MV： ⛔ 音乐：调 `_chA` 抛 " + EM(e) + " ⇒ 总线映射查不了");
                return null;
            }

            if (g == null)
            {
                W("MV： ⛔ 音乐：`_chA` 返回 null ⇒ 总线映射查不了");
                return null;
            }

            IntPtr want = MvHandleOf(g);
            if (want == IntPtr.Zero)
            {
                W("MV： ⛔ 音乐：`_chA` 拿回来的 `ChannelGroup` 读不出 `handle` ⇒ 映射没法验");
                return null;
            }

            foreach (var f in lfT.GetFields(F))
            {
                if (f.FieldType.Name != "ChannelGroup") continue;
                object x = null;
                try
                {
                    x = f.GetValue(lf);
                }
                catch
                {
                }

                if (x == null) continue;
                if (MvHandleOf(x) == want)
                {
                    W("MV： 音乐：总线映射**验票通过** —— `_jF." + S(bus) + "` 就是字段 `" + f.Name
                      + "`（handle 0x" + want.ToInt64().ToString("X") + " 逐位相同）");
                    return g;
                }
            }

            W("MV： ⚠ 音乐：`_chA` 的 handle 0x" + want.ToInt64().ToString("X")
                                            + " 和 `_DtA` 那 10 个字段**一个都不匹配** ⇒ **映射没验出来**（字段名或结构可能变过）");
            return null;
        }
        catch (Exception e)
        {
            W("MV： ⛔ 音乐：验总线映射抛 " + EM(e));
            return null;
        }
    }

    static bool _mvVolLogged;

    static bool MvBusMapDump()
    {
        _mvKeepFields.Clear();
        _mvMapOk = false;
        _mvRootBus = null;
        _mvRootField = "";
        _mvBgmField = "";
        try
        {
            Type ifT = MvFmodManager();
            if (ifT == null) return false;
            string wq, yq;
            object lf = RVA(ifT, new[] { "_DtA" }, out wq, out yq);
            if (lf == null)
            {
                W("MV： ⛔ 总线全表：`_DtA` 没读到（" + yq + "）");
                return false;
            }

            Type lfT = lf.GetType();

            var names = new List<string>();
            var hand = new List<IntPtr>();
            foreach (var f in lfT.GetFields(F))
            {
                if (f.FieldType.Name != "ChannelGroup") continue;
                object x = null;
                try
                {
                    x = f.GetValue(lf);
                }
                catch
                {
                }

                names.Add(f.Name);
                hand.Add(MvHandleOf(x));
            }

            if (names.Count == 0)
            {
                W("MV： ⛔ 总线全表：`_DtA` 上一个 `ChannelGroup` 字段都没有");
                return false;
            }

            MethodInfo chA = null;
            foreach (var m in lfT.GetMethods(F | BindingFlags.Static))
                if (m.Name == "_chA" && !m.IsGenericMethodDefinition && m.GetParameters().Length == 2)
                {
                    chA = m;
                    break;
                }

            if (chA == null)
            {
                W("MV： ⛔ 总线全表：找不到 2 参的 `_chA`");
                return false;
            }

            Type jfT = null;
            try
            {
                jfT = chA.GetParameters()[1].ParameterType;
            }
            catch
            {
            }

            if (jfT == null || !jfT.IsEnum)
            {
                W("MV： ⛔ 总线全表：`_chA` 第 1 参不是枚举");
                return false;
            }

            Array vals = Enum.GetValues(jfT);
            string[] vnames = Enum.GetNames(jfT);

            var hit = new bool[names.Count];
            var lines = new List<string>();
            object firstField = null;
            for (int i = 0; i < vals.Length; i++)
            {
                object v = vals.GetValue(i);
                string fld = "?";
                try
                {
                    object g = chA.Invoke(null, MvFit(chA.GetParameters(), new object[] { lf, v }));
                    IntPtr h = MvHandleOf(g);
                    for (int k = 0; k < names.Count; k++)
                        if (hand[k] != IntPtr.Zero && hand[k] == h)
                        {
                            fld = names[k];
                            hit[k] = true;
                            break;
                        }
                }
                catch (Exception e)
                {
                    fld = "⛔" + EM(e);
                }

                lines.Add((i < vnames.Length ? vnames[i] : "?") + "→" + fld);

                if (i == 0)
                {
                    firstField = fld;
                }

                if (fld == names[0])
                {
                    _mvRootBus = v;
                }
            }

            _mvRootField = names[0];
            _mvBgmField = firstField as string ?? "";
            W("MV： 总线全表（" + lines.Count + " 条）：" + string.Join("  ", lines.ToArray()));

            var hl = new List<string>();
            for (int k = 0; k < names.Count; k++)
                hl.Add(names[k] + "=0x" + hand[k].ToInt64().ToString("X"));
            W("MV： 总线 handle：" + string.Join("  ", hl.ToArray()));

            var orphans = new List<string>();
            for (int k = 0; k < names.Count; k++)
                if (!hit[k])
                    orphans.Add(names[k]);
            if (orphans.Count == 0)
                W("MV： 总线全表：**每个字段都有枚举值能取到** ⇒ 没有「取不到的那个」（这根判据在这儿是空的）");
            else
                W("MV： ⚠ 这 " + orphans.Count + " 个字段**没有任何 `_jF` 取值能取到**："
                  + string.Join(" ", orphans.ToArray())
                  + "  ← 最像混音树的根 ⇒ **永不按**");
            _mvKeepFields.AddRange(orphans);

            if (!_mvKeepFields.Contains(names[0]))
                _mvKeepFields.Add(names[0]);
            W("MV： 根 = `" + names[0] + "`（**第一个声明的 `ChannelGroup` 字段**，也是 `_atA` 里 =1.0 的那条）；"
              + "映射到它的 `_jF` 取值 = " + S(_mvRootBus) + " ⇒ **我们的音乐挂它**（音量不走别人，直出主控）");
            W("MV： 游戏自己放音乐用的是 `_jF` 第一个取值 → 字段 `" + _mvBgmField + "`"
              + "（**界面 BGM 就在这条上** —— 同样是两趟实测咬出来的：留 `_ZtA`+它 ⇒ BGM 响；"
              + "留 `_ZtA`+`_cTA` ⇒ BGM 没）⇒ 让开时**只按这一条**");
            _mvMapOk = true;
            return true;
        }
        catch (Exception e)
        {
            W("MV： ⛔ 总线全表整个抛了 " + EM(e));
            return false;
        }
    }

    static void MvMusicPlay()
    {
        if (_mvMusicHandle != null)
        {
            try { MvCgDispose(_mvMusicHandle); } catch { }
        }
        _mvMusicHandle = null;
        _mvMusicName = "";
        _mvMusicGroup = null;
        try
        {
            string want = MvMusicWanted();
            if (want == null)
            {
                W("MV： ⏭ 音乐：这一片没有「自己的歌」（通用过场那条路）⇒ 不放音乐");
                return;
            }

            Type zfT = MvAudioType("_J._zf");
            if (zfT == null)
            {
                W("MV： ⛔ 音乐：「" + want + "」找不找得到都问不了 —— `_J._zf` 不在场 ⇒ 这一片静音播");
                return;
            }

            string wq, yq;
            object zf = RVA(zfT, new[] { "_psA" }, out wq, out yq);
            if (zf == null)
            {
                W("MV： ⛔ 音乐：门面实例没读到（" + yq + "）⇒ 这一片静音播");
                return;
            }

            MethodInfo yfa = MvPickM(zfT, "_yFA", 1, true);
            if (yfa != null)
            {
                string got;
                try
                {
                    got = S(yfa.Invoke(zf, MvFit(yfa.GetParameters(), new object[] { want })));
                }
                catch (Exception e)
                {
                    got = "⛔" + EM(e);
                }

                W("MV： 音乐：「" + want + "」`_yFA` = " + got);
                if (got != "True")
                {
                    W("MV： ⏭ 这个名字不在库里 ⇒ 这一片静音播（**不拿别的名字顶替**）");
                    return;
                }
            }
            else W("MV： ⚠ 音乐：`_yFA` 没找到 —— 不问在不在，直接试放");

            MethodInfo ofa = MvPickM(zfT, "_OFA", 3, true);
            if (ofa == null)
            {
                W("MV： ⛔ 音乐：`_zf` 上没有 3 参的 `_OFA` ⇒ 这一片静音播");
                return;
            }

            _mvMusicGroup = null;
            MvBusMapDump();
            object bus = _mvRootBus;
            if (bus == null)
            {
                try
                {
                    Type jfT = ofa.GetParameters()[1].ParameterType;
                    if (jfT != null && jfT.IsEnum)
                    {
                        Array v = Enum.GetValues(jfT);
                        if (v.Length > 0) bus = v.GetValue(0);
                    }
                }
                catch
                {
                }

                if (bus != null) W("MV： ⚠ 根的枚举取值没认出来 ⇒ 退回 `_jF` 第一个取值（那是**游戏放音乐那条**，界面 BGM 会和它撞车）");
            }

            if (bus == null)
            {
                W("MV： ⛔ 音乐：`_jF` 的取值读不出来 ⇒ 这一片静音播");
                return;
            }

            _mvMusicGroup = MvBusGroup(bus);

            object h = null;
            string err = null;
            try
            {
                h = ofa.Invoke(zf, MvFit(ofa.GetParameters(), new object[] { want, bus, 1f }));
            }
            catch (Exception e)
            {
                err = EM(e);
            }

            if (err != null)
            {
                W("MV： ⛔ 音乐：`_OFA` 抛 " + err + " ⇒ 这一片静音播");
                return;
            }

            if (h == null)
            {
                W("MV： ⛔ 音乐：`_OFA` 返回 null（受理了却没给句柄）⇒ 这一片静音播");
                return;
            }

            _mvMusicHandle = h;
            _mvMusicName = want;
            W("MV： ▶ 音乐起播：「" + want + "」  总线 `_jF." + S(bus) + "`  音量 1  →  " + FN(h));
        }
        catch (Exception e)
        {
            W("MV： ⛔ 音乐：起播整个抛了 " + EM(e) + " —— **不影响视频**，这一片静音播");
        }
    }

    static void MvMusicHalt()
    {
        string nm = _mvMusicName;
        if (string.IsNullOrEmpty(nm) && !string.IsNullOrEmpty(_mvBaseName) && _mvBaseName != "<null>")
            nm = _mvBaseName.ToLowerInvariant() + "_bga.wav";

        object h = _mvMusicHandle;
        _mvMusicHandle = null;
        _mvMusicName = "";
        _mvMusicGroup = null;

        if (h != null)
        {
            try
            {
                if (MvCgDispose(h))
                    W("MV： ⏹ 音乐停了并释放：「" + nm + "」（`_Cg.Dispose`，同本体 `C2DVideoPlayer._Ke` / `OnDestroy`）");
                else
                    W("MV： ⚠ 音乐：`Dispose` / `_ZFA` 都没成 ⇒ 「" + nm + "」可能继续放");
            }
            catch (Exception e)
            {
                W("MV： ⚠ 音乐：停「" + nm + "」时抛 " + EM(e));
            }
        }

        try { MvMusicSweepByName(nm, h); }
        catch (Exception e) { W("MV： ⚠ 音乐清扫时抛 " + EM(e)); }
    }

    static bool MvCgDispose(object cg)
    {
        if (cg == null) return false;

        // _Cg+0x90（_UFA 里的 a5）：1 = 循环。_ZFA/Dispose 看到 1 时不会立刻 stop，
        // 而是排到「这一遍放完」才停 —— 跳过 PV 后就会继续响很久。先清零走立即 stop。
        try
        {
            IntPtr p = Ptr(cg);
            if (p != IntPtr.Zero) Marshal.WriteInt32(p + 0x90, 0);
        }
        catch { }

        try
        {
            MethodInfo d = cg.GetType().GetMethod("Dispose", F);
            if (d != null)
            {
                d.Invoke(cg, null);
                return true;
            }
        }
        catch { }

        try
        {
            Type zfT = MvAudioType("_J._zf");
            if (zfT == null) return false;
            string wq, yq;
            object zf = RVA(zfT, new[] { "_psA" }, out wq, out yq);
            MethodInfo z = MvPickM(zfT, "_ZFA", 1, true);
            if (z == null || zf == null) return false;
            z.Invoke(zf, MvFit(z.GetParameters(), new object[] { cg }));
            return true;
        }
        catch
        {
            return false;
        }
    }

    static string MvCgName(object cg)
    {
        if (cg == null) return null;
        try
        {
            string s = S(P(cg, "_d"));
            if (!string.IsNullOrEmpty(s) && s != "<null>") return s;
        }
        catch { }
        try
        {
            object v = PF(cg, "_uUA");
            string s = S(v);
            if (!string.IsNullOrEmpty(s) && s != "<null>") return s;
        }
        catch { }
        return null;
    }

    static void MvMusicSweepByName(string nm, object skip)
    {
        if (string.IsNullOrEmpty(nm)) return;
        Type zfT = MvAudioType("_J._zf");
        if (zfT == null) return;
        string wq, yq;
        object zf = RVA(zfT, new[] { "_psA" }, out wq, out yq);
        if (zf == null) return;

        object list = PF(zf, "_SsA");
        int n = Len(list);
        if (n <= 0) return;

        int killed = 0;
        for (int i = n - 1; i >= 0; i--)
        {
            object cg = At(list, i);
            if (cg == null || ReferenceEquals(cg, skip)) continue;
            string name = MvCgName(cg);
            if (!string.Equals(name, nm, StringComparison.OrdinalIgnoreCase)) continue;
            if (MvCgDispose(cg)) killed++;
        }

        if (killed > 0)
            W("MV： ⏹ 音乐清扫：按名字又停掉 " + killed + " 个「" + nm + "」残留句柄");
    }

    public static void AbortStoryPvAudio(string why, object scene)
    {
        if (!TaskRunner.Busy) return;

        var names = new List<string>();
        int killed = 0;

        try { killed += MvStopCgField(scene, "_ST", names); } catch { }
        try { killed += MvStopCgField(scene, "_tT", names); } catch { }
        try { killed += MvStopVideoAudio(scene, "bossIntroBgaPlayer", names); } catch { }
        try { killed += MvStopVideoAudio(scene, "videoPlayer", names); } catch { }
        try { killed += MvStopVideoAudio(scene, "c2dVideoPlayer", names); } catch { }
        try { killed += MvStopVideoAudio(scene, "introC2dVideoPlayer", names); } catch { }

        try { killed += MvMusicSweepSuffix("_bga.wav"); } catch { }

        _mvPvSweepNames.Clear();
        _mvPvSweepNames.AddRange(names);
        _mvPvSweepUntil = Environment.TickCount + 3000;
        _mvPvSweepNext = Environment.TickCount + 250;

        if (killed > 0)
            W("PV： ⏹ " + why + " —— 已收掉 " + killed + " 个 PV 音频句柄，"
              + "之后 3 秒继续按「_bga.wav」清扫延迟起播的");
        else
            W("PV： ⏳ " + why + " —— 这一刻还没抓到 PV 音频句柄，"
              + "之后 3 秒按「_bga.wav」清扫延迟起播的");
    }

    static int MvStopCgField(object holder, string field, List<string> names)
    {
        object cg = PF(holder, field);
        if (cg == null) return 0;

        string nm = MvCgName(cg);
        if (!string.IsNullOrEmpty(nm) && nm != "<null>" && !names.Contains(nm))
            names.Add(nm);

        return MvCgDispose(cg) ? 1 : 0;
    }

    static int MvStopVideoAudio(object holder, string field, List<string> names)
    {
        object vp = PF(holder, field);
        if (vp == null) return 0;
        return MvStopCgField(vp, "_QG", names);
    }

    static int MvMusicSweepSuffix(string suffix)
    {
        if (string.IsNullOrEmpty(suffix)) return 0;

        Type zfT = MvAudioType("_J._zf");
        if (zfT == null) return 0;

        string wq, yq;
        object zf = RVA(zfT, new[] { "_psA" }, out wq, out yq);
        if (zf == null) return 0;

        object list = PF(zf, "_SsA");
        int n = Len(list);
        if (n <= 0) return 0;

        int killed = 0;
        for (int i = n - 1; i >= 0; i--)
        {
            object cg = At(list, i);
            if (cg == null) continue;

            string name = MvCgName(cg);
            if (string.IsNullOrEmpty(name) || name == "<null>") continue;
            if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;

            if (MvCgDispose(cg))
            {
                killed++;
                W("PV： ⏹ 延迟清扫停掉「" + name + "」");
            }
        }

        return killed;
    }

    static void MvPvSweepTick()
    {
        if (_mvPvSweepUntil == 0) return;

        int now = Environment.TickCount;
        if (now >= _mvPvSweepUntil)
        {
            _mvPvSweepUntil = 0;
            _mvPvSweepNext = 0;
            _mvPvSweepNames.Clear();
            return;
        }

        if (now < _mvPvSweepNext) return;
        _mvPvSweepNext = now + 250;

        try { MvMusicSweepSuffix("_bga.wav"); } catch { }

        for (int i = 0; i < _mvPvSweepNames.Count; i++)
        {
            try { MvMusicSweepByName(_mvPvSweepNames[i], null); } catch { }
        }
    }

    static float MvGetVol(object g)
    {
        var gv = M(g, "getVolume", 1);
        if (gv == null) return float.NaN;
        Type pt;
        try
        {
            pt = gv.GetParameters()[0].ParameterType;
        }
        catch
        {
            return float.NaN;
        }

        if (pt == typeof(float).MakeByRefType())
        {
            var a = new object[] { 0f };
            try
            {
                gv.Invoke(g, a);
                return Convert.ToSingle(a[0]);
            }
            catch
            {
                return float.NaN;
            }
        }

        if (pt == typeof(IntPtr))
        {
            IntPtr buf = Marshal.AllocHGlobal(4);
            try
            {
                Marshal.WriteInt32(buf, 0);
                gv.Invoke(g, new object[] { buf });
                return Marshal.PtrToStructure<float>(buf);
            }
            catch
            {
                return float.NaN;
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }

        return float.NaN;
    }

    static void MvMuteExcept(IntPtr keepHandle, string keepWhy, string onlyField = null)
    {
        MvRestoreAudio();

        Type ifT = MvFmodManager();
        if (ifT == null) return;

        string wq, yq;
        object holder = RVA(ifT, new[] { "_DtA" }, out wq, out yq);
        if (holder == null)
        {
            W("MV： ⛔ `_J._IF._DtA`（总线表）没读到（" + yq + "）⇒ **声音让不开**");
            return;
        }

        float[] kf = MvBusVolumes(ifT);

        int found = 0, okN = 0, kept = 0, at = -1, untouched = 0;
        var lines = new List<string>();
        foreach (var f in holder.GetType().GetFields(F))
        {
            if (f.FieldType.Name != "ChannelGroup") continue;
            at++;
            found++;
            object g = null;
            try
            {
                g = f.GetValue(holder);
            }
            catch
            {
            }

            if (g == null)
            {
                lines.Add(f.Name + "=<null>");
                continue;
            }

            if (keepHandle != IntPtr.Zero && MvHandleOf(g) == keepHandle)
            {
                kept++;
                lines.Add(f.Name + " **留着**（" + (string.IsNullOrEmpty(keepWhy) ? "这一条" : keepWhy) + "）");
                continue;
            }

            if (_mvKeepFields.Contains(f.Name))
            {
                kept++;
                lines.Add(f.Name + " **留着**（混音树的根/上游，按了会连我们一起哑）");
                continue;
            }

            if (onlyField != null && f.Name != onlyField)
            {
                untouched++;
                continue;
            }

            float before = MvGetVol(g);
            string src = "";
            if (float.IsNaN(before) && kf != null && at >= 0 && at < kf.Length)
            {
                before = kf[at];
                src = "（`_atA` 那本账）";
            }

            if (float.IsNaN(before))
            {
                before = 1f;
                src = "（猜的）";
            }

            bool wrote = false;
            string why = "找不到 1 参的 setVolume";
            var sv = M(g, "setVolume", 1);
            if (sv != null)
            {
                try
                {
                    sv.Invoke(g, MvFit(sv.GetParameters(), new object[] { 0f }));
                    wrote = true;
                }
                catch (Exception e)
                {
                    why = EM(e);
                }
            }

            if (wrote)
            {
                okN++;
                _mvFmodBuses.Add(new object[] { g, before, src });
            }

            float after = MvGetVol(g);
            lines.Add(f.Name + " " + before.ToString("0.##") + src + "→"
                      + (float.IsNaN(after) ? "?" : after.ToString("0.##"))
                      + (wrote
                          ? (after == 0f ? "✅" : (float.IsNaN(after) ? "（写成了，但读不回来）" : "⚠ 写了但不是 0"))
                          : "⛔ " + why));
        }

        W("MV： BGM 让开：找到 " + found + " 条 FMOD 总线，按下去 " + okN + " 条"
          + (kept > 0 ? "，**留着 " + kept + " 条**" : "")
          + (untouched > 0 ? "，**不碰 " + untouched + " 条**（只让开 " + onlyField + " 这一条）" : "")
          + "   ← " + string.Join(" ｜ ", lines.ToArray()));
        if (found == 0) W("MV： ⚠ 一条 `ChannelGroup` 都没找到 —— 总线表的结构和我离线看到的（`_lF`）不一样");
    }

    static void MvMuteGameAudio()
    {
        IntPtr ours = MvHandleOf(_mvMusicGroup);
        if (_mvMusicHandle != null && (ours == IntPtr.Zero || !_mvMapOk))
        {
            W("MV： ⚠⚠ 我们放了音乐，但**"
              + (ours == IntPtr.Zero ? "没验出它走哪条总线" : "没摊出总线全表（不知道哪条是混音树的根）")
              + "** ⇒ **这一轮一条总线都不按**：界面 BGM 会和它叠着响。这是**故意的** —— "
              + "全按下去会连我们自己的音乐一起按哑，而「哑了」和「根本没接通」在耳朵里分不出来");
            return;
        }

        MvMuteExcept(ours, "我们的音乐走这条",
            string.IsNullOrEmpty(_mvBgmField) ? null : _mvBgmField);
    }

    static float[] MvBusVolumes(Type ifT)
    {
        try
        {
            string wq, yq;
            object kf = RVA(ifT, new[] { "_atA" }, out wq, out yq);
            if (kf == null)
            {
                W("MV：   （`_atA` 那本音量账没读到：" + yq + " ⇒ 还原值只能猜 1.0）");
                return null;
            }

            var l = new List<float>();
            foreach (var f in kf.GetType().GetFields(F))
            {
                if (f.FieldType != typeof(float)) continue;
                object v = null;
                try
                {
                    v = f.GetValue(kf);
                }
                catch
                {
                }

                if (v == null) continue;
                l.Add(Convert.ToSingle(v));
            }

            if (l.Count == 0)
            {
                W("MV：   （`_atA` 上一个 float 都没读到 ⇒ 还原值只能猜 1.0）");
                return null;
            }

            bool allSame = true;
            for (int i = 1; i < l.Count; i++)
                if (Math.Abs(l[i] - l[0]) > 0.0001f)
                {
                    allSame = false;
                    break;
                }

            if (!_mvVolLogged)
            {
                _mvVolLogged = true;
                W("MV：   游戏自己的音量账 `_atA`（" + l.Count + " 个）："
                  + string.Join(" ", l.ConvertAll(x => x.ToString("0.##")).ToArray())
                  + (allSame
                      ? "   ← **全一样** ⇒ 对不齐也无所谓，还回去就是原值"
                      : "   ← ⚠ **有不等的** ⇒ 存疑：这个向量和 `_lF` 的字段是**按声明顺序**对上的（没验过）"));
            }

            return l.ToArray();
        }
        catch (Exception e)
        {
            W("MV：   （读 `_atA` 抛 " + EM(e) + " ⇒ 还原值只能猜 1.0）");
            return null;
        }
    }

    static void MvRestoreAudio()
    {
        if (_mvFmodBuses.Count == 0) return;
        var lines = new List<string>();
        int okN = 0;
        foreach (var b in _mvFmodBuses)
        {
            object g = b[0];
            float before = (float)b[1];
            string src = b.Length > 2 ? (b[2] as string ?? "") : "";
            var sv = M(g, "setVolume", 1);
            if (sv == null)
            {
                lines.Add("⛔ 找不到 setVolume");
                continue;
            }

            try
            {
                sv.Invoke(g, MvFit(sv.GetParameters(), new object[] { before }));
                float after = MvGetVol(g);
                bool good = !float.IsNaN(after) && Math.Abs(after - before) < 0.001f;
                if (good) okN++;
                lines.Add(before.ToString("0.##") + src + "→"
                          + (float.IsNaN(after) ? "?" : after.ToString("0.##")) + (good ? "✅" : "⚠"));
            }
            catch (Exception e)
            {
                lines.Add("⛔ " + EM(e));
            }
        }

        int n = _mvFmodBuses.Count;
        _mvFmodBuses.Clear();
        W("MV： BGM 还原：" + n + " 条里还成 " + okN + " 条   ← " + string.Join(" ｜ ", lines.ToArray()));
    }

    static readonly List<string> _mvKeepFields = new List<string>();
    static string _mvRootField = "";
    static object _mvRootBus;
    static string _mvBgmField = "";
    static bool _mvMapOk;

    static Type MvAudioType(string full)
    {
        Type t = T(full);
        if (t != null) return t;
        try
        {
            Assembly.Load("Game.Audio");
        }
        catch
        {
        }

        return T(full);
    }

    static MethodInfo MvPickM(Type t, string name, int argc, bool wantInstance)
    {
        if (t == null) return null;
        MethodInfo first = null;
        try
        {
            foreach (var m in t.GetMethods(F | BindingFlags.Static))
            {
                if (m.Name != name || m.IsGenericMethodDefinition) continue;
                if (m.GetParameters().Length != argc) continue;
                if (first == null) first = m;
                if (m.IsStatic != wantInstance) return m;
            }
        }
        catch
        {
        }

        return first;
    }

    static object MvCall(object target, string name, int argc, object[] args)
    {
        if (target == null) return null;
        Type t = target as Type;
        object inst = t == null ? target : null;
        if (t == null)
        {
            try
            {
                t = target.GetType();
            }
            catch
            {
                return null;
            }
        }

        try
        {
            foreach (var m in t.GetMethods(F | BindingFlags.Static))
            {
                if (m.Name != name || m.IsGenericMethodDefinition) continue;
                if (m.GetParameters().Length != argc) continue;
                if (!m.IsStatic && inst == null) continue;
                object[] a = MvFit(m.GetParameters(), args);
                try
                {
                    return m.Invoke(m.IsStatic ? null : inst, a);
                }
                catch
                {
                    return null;
                }
            }
        }
        catch
        {
        }

        return null;
    }

    static object[] MvFit(ParameterInfo[] ps, object[] args)
    {
        if (args == null || args.Length == 0) return args;
        var a = (object[])args.Clone();
        for (int i = 0; i < ps.Length && i < a.Length; i++)
        {
            if (a[i] == null) continue;
            Type pt = ps[i].ParameterType;
            Type at;
            try
            {
                at = a[i].GetType();
            }
            catch
            {
                continue;
            }

            if (pt.IsAssignableFrom(at)) continue;
            if (pt == typeof(IntPtr) || pt == typeof(string)) continue;
            try
            {
                a[i] = Convert.ChangeType(a[i], pt);
            }
            catch
            {
            }
        }

        return a;
    }

    static bool MvDestroy(object o)
    {
        if (o == null) return false;
        Type objT = T("UnityEngine.Object");
        if (objT == null) return false;
        MethodInfo m = null;
        foreach (var x in objT.GetMethods(F | BindingFlags.Static))
            if (x.Name == "Destroy" && !x.IsGenericMethodDefinition && x.GetParameters().Length == 1)
            {
                m = x;
                break;
            }

        if (m == null) return false;
        try
        {
            m.Invoke(null, new object[] { o });
            return true;
        }
        catch
        {
            return false;
        }
    }

    static void MvShot(string tag)
    {
        Type scT = T("UnityEngine.ScreenCapture");
        if (scT == null)
        {
            W("MV：   ⚠ `UnityEngine.ScreenCapture` 没加载 ⇒ 【" + tag + "】这一格**没验到**"
              + "（⚠ 这**不等于**「屏幕上没有」）。有 ReadPixels 那条路的数就别拿它当结论。");
            return;
        }

        string dir = Settings.ConfigRoot;
        string png = Path.Combine(dir, "mv_" + tag + "_整屏.png");
        bool wrote = false;
        try
        {
            foreach (var m in scT.GetMethods(F | BindingFlags.Static))
            {
                if (m.Name != "CaptureScreenshot" || m.IsGenericMethodDefinition) continue;
                var ps = m.GetParameters();
                if (ps.Length != 1 || ps[0].ParameterType != typeof(string)) continue;
                m.Invoke(null, new object[] { png });
                wrote = true;
                break;
            }
        }
        catch (Exception e)
        {
            W("MV：   ⚠ `CaptureScreenshot(string)` 抛 " + EM(e));
        }

        if (!wrote) W("MV：   ⚠ 没有 `CaptureScreenshot(string)` 重载（这一版没落 PNG）");
        else W("MV：   📷 PNG 落盘（帧末写）→ " + png);

        object tex = null;
        try
        {
            foreach (var m in scT.GetMethods(F | BindingFlags.Static))
            {
                if (m.Name != "CaptureScreenshotAsTexture" || m.IsGenericMethodDefinition) continue;
                if (m.GetParameters().Length != 0) continue;
                tex = m.Invoke(null, null);
                break;
            }
        }
        catch (Exception e)
        {
            W("MV：   ⛔ `CaptureScreenshotAsTexture()` 抛 " + EM(e));
        }

        if (tex == null)
        {
            W("MV：   ⛔ 【" + tag + "】整屏**没抓到**（返回 null / 没这个重载）—— "
              + "**这一格没验到**，别读成「屏幕上没有」");
            return;
        }

        tex = Retip(tex, T("UnityEngine.Texture2D"));
        int tw = IntOfV(PV(tex, "width"), -1), th = IntOfV(PV(tex, "height"), -1);
        W("MV：   整屏（**合成后**）" + tw + "×" + th + "   托管类=" + FN(tex));
        if (tw <= 0 || th <= 0)
            W("MV：   ⛔ 整屏尺寸读不到（" + tw + "×" + th + "）⇒ 这一格没验到");
        else
        {
            float[] sm = new float[144];
            int n = MvTexMap(tex, tw, th, sm);
            if (n == 144) MvPrintMap(tag + " 整屏", sm);
            else W("MV：   ⛔ 整屏只采到 " + n + " 个点 ⇒ 这一格不作数");
        }

        MvDestroy(tex);
    }

    static int MvTexMap(object tex, int w, int h, float[] outp)
    {
        if (tex == null || outp == null || w <= 0 || h <= 0) return 0;
        int n = 0;
        for (int j = 0; j < 9; j++)
        for (int i = 0; i < 16; i++)
        {
            int x = (int)((i + 0.5) * w / 16.0);
            int y = (int)((j + 0.5) * h / 9.0);
            if (x > w - 1) x = w - 1;
            if (y > h - 1) y = h - 1;
            object c = CallQ(tex, "GetPixel", new object[] { x, y });
            if (c == null) continue;
            float r = F1(PF(c, "r")), g = F1(PF(c, "g")), b = F1(PF(c, "b"));
            outp[j * 16 + i] = 0.299f * r + 0.587f * g + 0.114f * b;
            n++;
        }

        return n;
    }

    static int MvRtMap(object rt, string tag, float[] outp)
    {
        Type rtT = T("UnityEngine.RenderTexture");
        Type tt = T("UnityEngine.Texture2D");
        Type rectT = T("UnityEngine.Rect");
        if (rtT == null || tt == null || rectT == null || rt == null)
        {
            W("MV：   ⛔ 类型不齐 / RT 是 null ⇒ 【" + tag + "】没验到");
            return 0;
        }

        int rw = IntOfV(PV(rt, "width"), -1), rh = IntOfV(PV(rt, "height"), -1);
        if (rw <= 0 || rh <= 0)
        {
            W("MV：   ⛔ RT 尺寸读不到 ⇒ 【" + tag + "】没验到");
            return 0;
        }

        object tex = null;
        try
        {
            tex = Activator.CreateInstance(tt, new object[] { rw, rh });
        }
        catch
        {
        }

        if (tex == null)
        {
            W("MV：   ⛔ 建 " + rw + "×" + rh + " 读回贴图失败");
            return 0;
        }

        string w, y;
        object oldActive = RVA(rtT, new[] { "active" }, out w, out y);
        if (!SetX(rtT, new[] { "active" }, rt, out w, out y))
        {
            W("MV：   ⛔ `RenderTexture.active` 没设成（" + y + "）⇒ 【" + tag + "】没验到");
            return 0;
        }

        object back = RVA(rtT, new[] { "active" }, out w, out y);
        if (!SameObj(back, rt))
            W("MV：   ⚠ `active` 回读**不是那张**（" + Fmt(back) + " vs id=" + IdOf(rt) + "）⇒ 这一格打问号");
        object rect = null;
        try
        {
            rect = Activator.CreateInstance(rectT, new object[] { 0f, 0f, (float)rw, (float)rh });
        }
        catch
        {
        }

        if (rect != null) CallQ(tex, "ReadPixels", new object[] { rect, 0, 0, false });
        SetX(rtT, new[] { "active" }, oldActive, out w, out y);

        int n = MvTexMap(tex, rw, rh, outp);
        MvDestroy(tex);
        return n;
    }

    static void MvPrintMap(string tag, float[] m)
    {
        const string CH = " .:-=+*#%@";
        W("MV：   【" + tag + "】亮度图 16×9（`" + CH + "` 越靠右越亮，**上面那行先印**）");
        for (int j = 8; j >= 0; j--)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 16; i++) sb.Append(CH[ClampIdx(m[j * 16 + i])]);
            W("MV：     |" + sb + "|");
        }
    }
}