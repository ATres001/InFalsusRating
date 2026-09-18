using System;
using System.Collections.Generic;
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

[BepInPlugin("com.atres.infalsusrating", "In Falsus Rating", "0.4.0")]
public class Plugin : BasePlugin
{
    internal static ManualLogSource Logger;
    public static double PlayerRating = 0;
    public static int TotalExacts = 0;
    public static string PlayerName = "Player";
    public static List<ChartRating> ChartRatings = new List<ChartRating>();
    public static List<ExactChart> TopExactCharts = new List<ExactChart>();
    public static volatile bool FirstRatingReady = false;
    public static volatile bool OverlayManuallyHidden = false;

    public static volatile bool HasRatingDelta = false;
    public static double RatingDeltaValue = 0;
    public static DateTime RatingDeltaShownAt = DateTime.MinValue;

    public static void SetRatingDelta(double oldRating, double newRating)
    {
        double delta = newRating - oldRating;
        if (Math.Abs(delta) < 0.005) return;
        RatingDeltaValue = delta;
        RatingDeltaShownAt = DateTime.Now;
        HasRatingDelta = true;
    }

    public static void ClearRatingDelta()
    {
        HasRatingDelta = false;
    }

    public override void Load()
    {
        Logger = base.Log;
        Logger.LogInfo("In Falsus Rating Mod 加载中...");
        EnsureResourceFiles();
        ConstTable.Load();
        SongInfoCache.LoadCatalog();
        LoadPlayerName();
        StartScenePatchLoop();
        StartHotkeyListener();
        StartDelayedOverlay();

        var harmony = new Harmony("com.atres.infalsusrating");
        harmony.PatchAll(typeof(Patch_NH_VNA));
        harmony.PatchAll(typeof(Patch_UIManagerUpdate));
        Logger.LogInfo("[Plugin] Harmony patch 已挂载");
    }

    // ---------- 资源文件自动搬运 ----------
    static void EnsureResourceFiles()
    {
        try
        {
            string configDir = Path.Combine(Paths.ConfigPath, "InFalsusRating");
            Directory.CreateDirectory(configDir);

            string[] sourceDirs = new[]
            {
                Paths.PluginPath,
                Path.Combine(Paths.PluginPath, "InFalsusRating"),
                AppDomain.CurrentDomain.BaseDirectory,
                Paths.GameRootPath,
            };

            EnsureFile("font2.ttf", configDir, sourceDirs);
            EnsureFile("bg.png",    configDir, sourceDirs);
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"[Res] 确保资源: {ex.Message}");
        }
    }

    static void EnsureFile(string fileName, string destDir, string[] sourceDirs)
    {
        string destPath = Path.Combine(destDir, fileName);
        if (File.Exists(destPath))
        {
            Logger.LogInfo($"[Res] {fileName} 已存在");
            return;
        }

        foreach (var dir in sourceDirs)
        {
            try
            {
                string src = Path.Combine(dir, fileName);
                if (File.Exists(src))
                {
                    File.Copy(src, destPath);
                    Logger.LogInfo($"[Res] 已复制 {fileName}: {src} → {destPath}");
                    return;
                }
            }
            catch { }
        }

        Logger.LogInfo($"[Res] 未找到 {fileName}，请手动放到: {destPath}");
    }

    // ---------- 读取玩家名字 ----------
    static void LoadPlayerName()
    {
        try
        {
            string dir = Path.Combine(Paths.ConfigPath, "InFalsusRating");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "name.txt");

            if (File.Exists(file))
            {
                string n = File.ReadAllText(file).Trim();
                if (!string.IsNullOrWhiteSpace(n))
                {
                    PlayerName = n;
                    Logger.LogInfo($"[Name] 已载入玩家名字: {PlayerName}");
                    return;
                }

                File.WriteAllText(file, "Player");
                Logger.LogInfo($"[Name] name.txt 为空，写入默认值 \"Player\"");
            }
            else
            {
                File.WriteAllText(file, "Player");
                Logger.LogInfo($"[Name] 已创建配置文件: {file}");
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"[Name] 读取文件失败: {ex.Message}");
        }

        Logger.LogInfo($"[Name] 当前使用: {PlayerName}");
    }

    // ---------- 后台 patch 场景 ----------
    static void StartScenePatchLoop()
    {
        var t = new Thread(() =>
        {
            for (int i = 0; i < 300; i++)
            {
                try
                {
                    if (SceneTracker.TryEnsurePatched())
                    {
                        Logger.LogInfo("[Scene] patch 循环结束");
                        return;
                    }
                }
                catch { }
                Thread.Sleep(100);
            }
            Logger.LogWarning("[Scene] patch 超时（30 秒）");
        });
        t.IsBackground = true;
        t.Start();
    }

    // ---------- F8 全局快捷键 ----------
    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vKey);

    const int VK_F8 = 0x77;
    const int VK_F9 = 0x78;

    static void StartHotkeyListener()
    {
        var t = new Thread(() =>
        {
            bool wasF8Down = false;
            bool wasF9Down = false;

            while (true)
            {
                try
                {
                    bool f8 = (GetAsyncKeyState(VK_F8) & 0x8000) != 0;
                    bool f9 = (GetAsyncKeyState(VK_F9) & 0x8000) != 0;

                    if (f8 && !wasF8Down)
                    {
                        OverlayManuallyHidden = !OverlayManuallyHidden;
                        Logger.LogInfo($"[Hotkey] F8 → overlay {(OverlayManuallyHidden ? "隐藏" : "显示")}");
                    }
                    if (f9 && !wasF9Down)
                    {
                        Logger.LogInfo("[Hotkey] F9 → 导出 B30 图片");
                        ExportB30Image();
                    }

                    wasF8Down = f8;
                    wasF9Down = f9;
                }
                catch { }
                Thread.Sleep(50);
            }
        });
        t.IsBackground = true;
        t.Start();
    }

    // ---------- 延迟启动 overlay ----------
    static void StartDelayedOverlay()
    {
        var t = new Thread(() =>
        {
            try
            {
                int waited = 0;
                const int MAX_WAIT_MS = 30000;
                const int POLL_MS = 100;

                while (waited < MAX_WAIT_MS)
                {
                    if (FirstRatingReady)
                    {
                        Logger.LogInfo($"[Overlay] 首次评级已就绪（等待 {waited}ms），启动 overlay");
                        break;
                    }
                    Thread.Sleep(POLL_MS);
                    waited += POLL_MS;
                }

                if (!FirstRatingReady)
                    Logger.LogInfo($"[Overlay] 等待超时（{MAX_WAIT_MS}ms），强制启动 overlay");

                Win32Overlay.Start(() => $"{PlayerRating:F2}");
                Logger.LogInfo("[Overlay] 已启动");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[Overlay] 启动失败: {ex}");
            }
        });
        t.IsBackground = true;
        t.Start();
    }

    // ---------- 导出 B30 图片 ----------
    public static void ExportB30Image()
    {
        try
        {
            string dir = Path.Combine(Paths.ConfigPath, "InFalsusRating");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "b30.png");

            if (ChartRatings == null || ChartRatings.Count == 0)
            {
                Logger.LogWarning("[B30Img] 暂无成绩，跳过导出");
                return;
            }

            // 曲绘版：仅当存在 jacket 文件夹时导出
            if (B30CardExporter.HasJacketFolder())
            {
                bool ok = B30CardExporter.Export(path, PlayerName, PlayerRating, TotalExacts, ChartRatings, TopExactCharts);
                Logger.LogInfo($"[B30Img] 曲绘版导出 {(ok ? "成功" : "失败")} → {path}");
            }
            else
            {
                Logger.LogInfo("[B30Img] 无 jacket 文件夹，跳过曲绘版");
            }

            // 无曲绘版：始终导出（有 jacket 的玩家也有一份）
            string noJacketPath = Path.Combine(dir, "b30_nojacket.png");
            bool ok2 = B30CardExporter.ExportNoJacket(noJacketPath, PlayerName, PlayerRating, TotalExacts, ChartRatings);
            Logger.LogInfo($"[B30Img] 无曲绘版导出 {(ok2 ? "成功" : "失败")} → {noJacketPath}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[B30Img] 导出异常: {ex.Message}");
        }
    }
}

// ========== 场景活跃追踪 ==========
public static class SceneTracker
{
    public static volatile bool IsInResultsScreen = false;
    public static volatile bool IsPostGameResults = false;

    private const string SCENE_GAME       = "ifapp.Game.Scenes.GameScene";
    private const string SCENE_SONGSELECT = "ifapp.Game.Scenes.SongSelectScene";
    private const string SCENE_RESULTS    = "ifapp.Game.Scenes.ResultsScene";

    private static readonly string[] _candidateScenes = new[]
    {
        "ifapp.Game.Scenes.TitleScene",
        "ifapp.Game.Scenes.HubScene",
        "ifapp.Game.Scenes.SongSelectScene",
        "ifapp.Game.Scenes.ResultsScene",
        "ifapp.Game.Scenes.PackSelectScene",
        "ifapp.Game.Scenes.StoryScene",
        "ifapp.Game.Scenes.TimelineScene",
        "ifapp.Game.Scenes.CreditsScene",
        "ifapp.Game.Scenes.GameScene",
        "ifapp.Game.Scenes.CharacterSelectLayer",
    };

    private static List<string> _sceneStack = new List<string>();
    private static readonly object _lock = new object();
    private static bool _patched = false;
    private static readonly HashSet<string> _handled = new HashSet<string>();

    // 显示状态变化日志
    private static bool _lastShouldShow = false;
    private static bool _shouldShowInit = false;

    public static void SetPostGameResults(bool v)
    {
        if (IsPostGameResults != v)
        {
            IsPostGameResults = v;
            Plugin.Logger.LogInfo($"[Scene] IsPostGameResults → {v}");
        }
    }

    public static bool ShouldShowOverlay()
    {
        bool result;

        if (IsInResultsScreen || IsPostGameResults)
        {
            result = true;
        }
        else
        {
            lock (_lock)
            {
                // ResultsScene 优先级最高（结算时 GameScene 可能还残留）
                if (_sceneStack.Contains(SCENE_RESULTS))
                    result = true;
                // 打歌中（GameScene 在栈里但 ResultsScene 不在）
                else if (_sceneStack.Contains(SCENE_GAME))
                    result = false;
                else if (_sceneStack.Contains(SCENE_SONGSELECT))
                    result = true;
                else
                    result = false;
            }
        }

        if (!_shouldShowInit || result != _lastShouldShow)
        {
            _shouldShowInit = true;
            _lastShouldShow = result;
            Plugin.Logger.LogInfo($"[Scene] ShouldShowOverlay → {result} (stack={GetStackSnapshot()})");
        }

        return result;
    }

    public static bool TryEnsurePatched()
    {
        if (_patched) return true;

        try
        {
            var harmony = new Harmony("com.atres.infalsusrating.scene");
            var enter = typeof(SceneHooks).GetMethod("OnSceneEnable", BindingFlags.Static | BindingFlags.Public);
            var exit  = typeof(SceneHooks).GetMethod("OnSceneDisable", BindingFlags.Static | BindingFlags.Public);

            foreach (var className in _candidateScenes)
            {
                if (_handled.Contains(className)) continue;

                Type t = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    t = asm.GetType(className);
                    if (t != null) break;
                }
                if (t == null) continue;

                try
                {
                    var onEnable  = t.GetMethod("OnEnable",  BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    var onDisable = t.GetMethod("OnDisable", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    var awake     = t.GetMethod("Awake",     BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    var onDestroy = t.GetMethod("OnDestroy", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                    bool didEnter = false, didExit = false;

                    if (onEnable != null) { harmony.Patch(onEnable, postfix: new HarmonyMethod(enter)); didEnter = true; }
                    else if (awake != null) { harmony.Patch(awake, postfix: new HarmonyMethod(enter)); didEnter = true; }

                    if (onDisable != null) { harmony.Patch(onDisable, postfix: new HarmonyMethod(exit)); didExit = true; }
                    else if (onDestroy != null) { harmony.Patch(onDestroy, postfix: new HarmonyMethod(exit)); didExit = true; }

                    if (didEnter && didExit)
                        Plugin.Logger.LogInfo($"[Scene] patch {t.Name}");

                    _handled.Add(className);
                }
                catch (Exception ex)
                {
                    Plugin.Logger.LogWarning($"[Scene] patch {className} 失败: {ex.Message}");
                    _handled.Add(className);
                }
            }

            if (_handled.Count >= 3)
            {
                _patched = true;
                Plugin.Logger.LogInfo($"[Scene] patch 阶段完成，共处理 {_handled.Count} 个类");
                return true;
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[Scene] TryEnsurePatched: {ex}");
        }

        return false;
    }

    public static void Increment(string sceneName)
    {
        lock (_lock)
        {
            if (!_sceneStack.Contains(sceneName))
                _sceneStack.Add(sceneName);

            Plugin.Logger.LogInfo($"[Scene] ▶ {sceneName}");
        }
    }

    public static void Decrement(string sceneName)
    {
        lock (_lock)
        {
            _sceneStack.Remove(sceneName);
            Plugin.Logger.LogInfo($"[Scene] ◀ {sceneName}");
        }
    }

    public static string GetStackSnapshot()
    {
        lock (_lock)
        {
            return string.Join(" > ", _sceneStack);
        }
    }
}

// ========== 场景生命周期 hook ==========
public static class SceneHooks
{
    public static void OnSceneEnable(object __instance)
    {
        if (__instance == null) return;
        string name = "?";
        try { name = __instance.GetType().FullName; } catch { }
        SceneTracker.Increment(name);

        // 离开结算态：回到 Hub / 选曲 / 再次游玩
        if (name == "ifapp.Game.Scenes.HubScene"
            || name == "ifapp.Game.Scenes.SongSelectScene"
            || name == "ifapp.Game.Scenes.GameScene")
        {
            SceneTracker.SetPostGameResults(false);
        }
    }

    public static void OnSceneDisable(object __instance)
    {
        if (__instance == null) return;
        string name = "?";
        try { name = __instance.GetType().FullName; } catch { }
        SceneTracker.Decrement(name);

        // 进入结算态：GameScene 退出
        if (name == "ifapp.Game.Scenes.GameScene")
        {
            SceneTracker.SetPostGameResults(true);
            StartDelayedRecalculate();
        }
    }

    // 结算界面出现后延迟重算（等成绩写入存档）
    static void StartDelayedRecalculate()
    {
        var t = new Thread(() =>
        {
            try
            {
                Thread.Sleep(1000);
                var r = RatingEngine.LastGameResults;
                if (r == null)
                {
                    Plugin.Logger.LogInfo("[Results] 延迟重算：无缓存的 GameResults");
                    return;
                }

                Plugin.Logger.LogInfo("[Results] 延迟重算触发");
                RatingEngine.Recalculate(r, "结算", throttle: false);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[Results] 延迟重算失败: {ex.Message}");
            }
        });
        t.IsBackground = true;
        t.Start();
    }
}

// ========== 结算界面检测 ==========
public static class ResultsDetector
{
    private static readonly string[] _candidates = new[]
    {
        "ResultsGradeContainer",
        "ResultsTopBannerContainer",
        "ResultsBottomBannerContainer",
        "ResultsJacketContainer",
        "ResultsBattleLayer",
        "ResultsClearBadgeRow",
    };

    private static DateTime _lastCheck = DateTime.MinValue;
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(300);

    public static void Tick()
    {
        var now = DateTime.Now;
        if ((now - _lastCheck) < Interval) return;
        _lastCheck = now;

        bool active = false;
        try
        {
            foreach (var name in _candidates)
            {
                var go = UnityEngine.GameObject.Find(name);
                if (go != null && go.activeInHierarchy)
                {
                    active = true;
                    break;
                }
            }
        }
        catch { }

        if (active != SceneTracker.IsInResultsScreen)
        {
            SceneTracker.IsInResultsScreen = active;
            Plugin.Logger.LogInfo($"[Results] 结算界面: {(active ? "进入" : "退出")}");
        }

        ContextDiag(); 
    }

    // 临时诊断：栈或结算对象状态变化时打一行
    private static DateTime _lastCtxDiag = DateTime.MinValue;
    private static string _lastCtxSig = "";

    public static void ContextDiag()
    {
        var now = DateTime.Now;
        if ((now - _lastCtxDiag).TotalSeconds < 1) return;
        _lastCtxDiag = now;

        try
        {
            var stack = SceneTracker.GetStackSnapshot();

            var found = new List<string>();
            foreach (var name in _candidates)
            {
                var go = UnityEngine.GameObject.Find(name);
                if (go != null && go.activeInHierarchy) found.Add(name);
            }

            string sig = stack + "|" + string.Join(",", found);
            if (sig == _lastCtxSig) return;
            _lastCtxSig = sig;

                    Plugin.Logger.LogInfo($"[CtxDiag] stack=[{stack}] results={SceneTracker.IsInResultsScreen} found=[{string.Join(", ", found)}]");
        }
        catch { }
    }
}

// ========== Win32 Overlay ==========
public static class Win32Overlay
{
    const uint WS_EX_LAYERED     = 0x00080000;
    const uint WS_EX_TRANSPARENT = 0x00000020;
    const uint WS_EX_TOOLWINDOW  = 0x00000080;
    const uint WS_EX_NOACTIVATE  = 0x08000000;
    const uint WS_POPUP          = 0x80000000;

    const int WM_DESTROY = 0x0002;
    const int SW_SHOWNOACTIVATE = 4;

    const int GWLP_HWNDPARENT = -8;
    const uint ULW_ALPHA = 0x00000002;

    const int PixelFormat32bppARGB = 0x0026200A;
    const int ImageLockModeRead = 1;
    const int UnitPixel = 2;
    const int FontStyleBold = 1;
    const int StringAlignmentNear = 0;
    const int StringAlignmentCenter = 1;
    const int StringAlignmentLineCenter = 1;
    const int TextRenderingHintAntiAlias = 4;
    const int LinearGradientModeVertical = 1;
    const int WrapModeTile = 0;

    const float NAME_FONT_RATIO  = 0.7f;
    const float MAIN_FONT_RATIO  = 0.72f;
    const float EXACT_FONT_RATIO = 0.46f;

    const float NAME_X      = 0.05f;
    const float NAME_W      = 0.4f;
    const float NAME_Y      = 0.27f;
    const float NAME_H      = 0.50f;

    const float MAIN_X      = 0.43f;
    const float MAIN_W      = 0.34f;
    const float MAIN_Y      = 0.23f;
    const float MAIN_H      = 0.6f;

    const float EXACT_X     = 0.71f;
    const float EXACT_W     = 0.28f;
    const float EXACT_Y     = 0.35f;
    const float EXACT_H     = 0.50f;

    const float DELTA_FONT_RATIO = 0.32f;
    const float DELTA_X = 0.43f;
    const float DELTA_W = 0.34f;
    const float DELTA_Y = 0.06f;
    const float DELTA_H = 0.25f;

    static readonly int COLOR_POSITIVE = unchecked((int)0xFF7FFF7F);
    static readonly int COLOR_NEGATIVE = unchecked((int)0xFFFF7F7F);
    static readonly int GRADIENT_TOP    = unchecked((int)0xFF80AEFF);
    static readonly int GRADIENT_BOTTOM = unchecked((int)0xFFE0C9FF);

    const int OVERLAY_OFFSET_Y = 0;
    const float ANIM_LERP = 0.15f;
    const int ANIM_FRAME_MS = 16;
    const float DIGIT_LERP = 0.10f;
    const int SHOW_CONFIRM_MS = 300;

    const float MOUSE_NEAR_DIST     = 80f;
    const float MOUSE_FULL_FADE_DIST = 20f;
    const float FADE_OUT_SPEED      = 0.35f;
    const float FADE_IN_SPEED       = 0.15f;

    [StructLayout(LayoutKind.Sequential)]
    struct GdiplusStartupInput
    {
        public uint GdiplusVersion;
        public IntPtr DebugEventCallback;
        public int SuppressBackgroundThread;
        public int SuppressExternalCodecs;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct GPRECT { public int X, Y, Width, Height; }

    [StructLayout(LayoutKind.Sequential)]
    struct GPRECTF { public float X, Y, Width, Height; }

    [StructLayout(LayoutKind.Sequential)]
    struct BitmapData
    {
        public int Width;
        public int Height;
        public int Stride;
        public int PixelFormat;
        public IntPtr Scan0;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int x, y; }

    [StructLayout(LayoutKind.Sequential)]
    struct SIZE { public int cx, cy; }

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WNDCLASS
    {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string lpszMenuName;
        public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int pt_x, pt_y;
    }

    delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    static WndProcDelegate _wndProc;

    // GDI+
    [DllImport("gdiplus.dll")]
    static extern int GdiplusStartup(out IntPtr token, ref GdiplusStartupInput input, IntPtr output);
    [DllImport("gdiplus.dll")]
    static extern void GdiplusShutdown(IntPtr token);
    [DllImport("gdiplus.dll", CharSet = CharSet.Unicode)]
    static extern int GdipCreateBitmapFromFile(string filename, out IntPtr bitmap);
    [DllImport("gdiplus.dll")]
    static extern int GdipDisposeImage(IntPtr image);
    [DllImport("gdiplus.dll")]
    static extern int GdipGetImageWidth(IntPtr image, out uint width);
    [DllImport("gdiplus.dll")]
    static extern int GdipGetImageHeight(IntPtr image, out uint height);
    [DllImport("gdiplus.dll")]
    static extern int GdipCloneBitmapAreaI(int x, int y, int width, int height,
        int pixelFormat, IntPtr srcBitmap, out IntPtr dstBitmap);
    [DllImport("gdiplus.dll")]
    static extern int GdipGetImageGraphicsContext(IntPtr image, out IntPtr graphics);
    [DllImport("gdiplus.dll")]
    static extern int GdipDeleteGraphics(IntPtr graphics);
    [DllImport("gdiplus.dll")]
    static extern int GdipBitmapLockBits(IntPtr bitmap, ref GPRECT rect, uint flags,
        int pixelFormat, out BitmapData lockedBitmapData);
    [DllImport("gdiplus.dll")]
    static extern int GdipBitmapUnlockBits(IntPtr bitmap, ref BitmapData lockedBitmapData);
    [DllImport("gdiplus.dll", CharSet = CharSet.Unicode)]
    static extern int GdipCreateFontFamilyFromName(string name, IntPtr fontCollection, out IntPtr fontFamily);
    [DllImport("gdiplus.dll")]
    static extern int GdipCreateFont(IntPtr fontFamily, float emSize, int style, int unit, out IntPtr font);
    [DllImport("gdiplus.dll")]
    static extern int GdipCreateSolidFill(int color, out IntPtr brush);
    [DllImport("gdiplus.dll")]
    static extern int GdipCreateLineBrushFromRect(ref GPRECTF rect,
        int color1, int color2, int mode, int wrapMode, out IntPtr brush);
    [DllImport("gdiplus.dll")]
    static extern int GdipDeleteBrush(IntPtr brush);
    [DllImport("gdiplus.dll")]
    static extern int GdipCreateStringFormat(int formatAttributes, int language, out IntPtr format);
    [DllImport("gdiplus.dll")]
    static extern int GdipSetStringFormatAlign(IntPtr format, int align);
    [DllImport("gdiplus.dll")]
    static extern int GdipSetStringFormatLineAlign(IntPtr format, int align);
    [DllImport("gdiplus.dll")]
    static extern int GdipSetTextRenderingHint(IntPtr graphics, int mode);
    [DllImport("gdiplus.dll", CharSet = CharSet.Unicode)]
    static extern int GdipDrawString(IntPtr graphics, string text, int length,
        IntPtr font, ref GPRECTF layoutRect, IntPtr stringFormat, IntPtr brush);

    [DllImport("gdiplus.dll")]
    static extern int GdipNewPrivateFontCollection(out IntPtr fontCollection);
    [DllImport("gdiplus.dll")]
    static extern int GdipDeletePrivateFontCollection(ref IntPtr fontCollection);
    [DllImport("gdiplus.dll", CharSet = CharSet.Unicode)]
    static extern int GdipPrivateAddFontFile(IntPtr fontCollection, string filename);
    [DllImport("gdiplus.dll")]
    static extern int GdipGetFontCollectionFamilyCount(IntPtr fontCollection, out int numFound);
    [DllImport("gdiplus.dll")]
    static extern int GdipGetFontCollectionFamilyList(IntPtr fontCollection, int numSought,
        [Out] IntPtr[] gpfamilies, out int numFound);
    [DllImport("gdiplus.dll", CharSet = CharSet.Unicode)]
    static extern int GdipGetFamilyName(IntPtr fontFamily, StringBuilder name, int language);

    // Win32
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern ushort RegisterClassW(ref WNDCLASS lpWndClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr CreateWindowExW(uint dwExStyle, string lpClassName, string lpWindowName,
        uint dwStyle, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")]
    static extern int GetMessageW(out MSG msg, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll")]
    static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")]
    static extern IntPtr DispatchMessageW(ref MSG msg);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr FindWindowW(string lpClassName, string lpWindowName);
    [DllImport("user32.dll")]
    static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);
    [DllImport("user32.dll")]
    static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")]
    static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("user32.dll")]
    static extern bool UpdateLayeredWindow(IntPtr hWnd, IntPtr hdcDst,
        ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc,
        uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);
    [DllImport("user32.dll")]
    static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")]
    static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    // GDI
    [DllImport("gdi32.dll")]
    static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")]
    static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi,
        uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);
    [DllImport("gdi32.dll")]
    static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);
    [DllImport("gdi32.dll")]
    static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr hObject);

    static IntPtr _gdiplusToken = IntPtr.Zero;
    static IntPtr _bgBitmap = IntPtr.Zero;
    static int _bgW = 360;
    static int _bgH = 90;

    static IntPtr _memDC = IntPtr.Zero;
    static IntPtr _dibSection = IntPtr.Zero;
    static IntPtr _dibBits = IntPtr.Zero;
    static IntPtr _dibOldObj = IntPtr.Zero;

    static IntPtr _privateFontCollection = IntPtr.Zero;
    static IntPtr _fontFamily = IntPtr.Zero;
    static IntPtr _fontName  = IntPtr.Zero;
    static IntPtr _fontMain  = IntPtr.Zero;
    static IntPtr _fontExact = IntPtr.Zero;
    static IntPtr _fontDelta = IntPtr.Zero;
    static IntPtr _whiteBrush = IntPtr.Zero;
    static IntPtr _brushGreen = IntPtr.Zero;
    static IntPtr _brushRed = IntPtr.Zero;
    static IntPtr _formatLeft   = IntPtr.Zero;
    static IntPtr _formatCenter = IntPtr.Zero;

    static string _nameText  = "Player";
    static string _mainText  = "--.--";
    static string _exactText = "0";
    static string _deltaText = "";
    static bool _deltaIsPositive = true;
    static string _loadedFontName = null;
    static IntPtr _gameHwnd = IntPtr.Zero;

    static int _screenX = 0;
    static int _screenY = 0;
    static int _targetY = 0;
    static string _lastRenderedName  = null;
    static string _lastRenderedMain  = null;
    static string _lastRenderedExact = null;
    static string _lastRenderedDelta = null;
    static double _displayRating = 0;
    static double _displayExact = 0;
    static float _currentAlpha = 1.0f;
    static bool _stableShow = false;
    static bool _pendingShow = false;
    static DateTime _pendingShowAt = DateTime.MinValue;

    static readonly string BG_PATH = Path.Combine(
        BepInEx.Paths.ConfigPath, "InFalsusRating", "bg.png");
    static readonly string FONT_PATH = Path.Combine(
        BepInEx.Paths.ConfigPath, "InFalsusRating", "font2.ttf");

    public static void Start(Func<string> mainProvider)
    {
        var thread = new Thread(() =>
        {
            try { RunOverlay(mainProvider); }
            catch (Exception ex) { Plugin.Logger.LogError($"[Overlay] {ex}"); }
        });
        thread.IsBackground = true;
#pragma warning disable CA1416
        thread.SetApartmentState(ApartmentState.STA);
#pragma warning restore CA1416
        thread.Start();
    }

    static void StartupGdiplus()
    {
        var input = new GdiplusStartupInput { GdiplusVersion = 1 };
        int status = GdiplusStartup(out _gdiplusToken, ref input, IntPtr.Zero);
        if (status != 0)
            Plugin.Logger.LogError($"[Overlay] GdiplusStartup 失败: {status}");
    }

    static void LoadBackgroundImage()
    {
        if (!File.Exists(BG_PATH))
        {
            Plugin.Logger.LogWarning($"[Overlay] 背景图不存在: {BG_PATH}");
            return;
        }

        int status = GdipCreateBitmapFromFile(BG_PATH, out _bgBitmap);
        if (status != 0 || _bgBitmap == IntPtr.Zero)
        {
            Plugin.Logger.LogError($"[Overlay] 加载图片失败: {status}");
            return;
        }

        GdipGetImageWidth(_bgBitmap, out uint w);
        GdipGetImageHeight(_bgBitmap, out uint h);
        _bgW = (int)w;
        _bgH = (int)h;

        Plugin.Logger.LogInfo($"[Overlay] 背景图已加载: {_bgW}x{_bgH}");
    }

    static void LoadCustomFont()
    {
        try
        {
            if (!File.Exists(FONT_PATH))
            {
                Plugin.Logger.LogInfo($"[Overlay] 自定义字体不存在: {FONT_PATH}（用 Segoe UI）");
                return;
            }

            if (GdipNewPrivateFontCollection(out _privateFontCollection) != 0)
            {
                Plugin.Logger.LogWarning("[Overlay] GdipNewPrivateFontCollection 失败");
                _privateFontCollection = IntPtr.Zero;
                return;
            }

            int addStatus = GdipPrivateAddFontFile(_privateFontCollection, FONT_PATH);
            if (addStatus != 0)
            {
                Plugin.Logger.LogWarning($"[Overlay] GdipPrivateAddFontFile 失败: {addStatus}");
                GdipDeletePrivateFontCollection(ref _privateFontCollection);
                _privateFontCollection = IntPtr.Zero;
                return;
            }

            GdipGetFontCollectionFamilyCount(_privateFontCollection, out int familyCount);
            if (familyCount <= 0)
            {
                Plugin.Logger.LogWarning("[Overlay] 字体文件里没找到家族");
                return;
            }

            var families = new IntPtr[familyCount];
            GdipGetFontCollectionFamilyList(_privateFontCollection, familyCount, families, out int found);
            if (found <= 0)
            {
                Plugin.Logger.LogWarning("[Overlay] 获取字体家族列表失败");
                return;
            }

            _fontFamily = families[0];

            var sb = new StringBuilder(64);
            if (GdipGetFamilyName(_fontFamily, sb, 0) == 0)
                _loadedFontName = sb.ToString();

            Plugin.Logger.LogInfo($"[Overlay] 自定义字体已加载: \"{_loadedFontName}\" (家族数 {familyCount})");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[Overlay] LoadCustomFont: {ex}");
        }
    }

    static IntPtr FindGameWindow()
    {
        // 1. 优先：按标题精确匹配游戏窗口（BepInEx Console 标题不是 "In Falsus"）
        var hwnd = FindWindowW(null, "In Falsus");
        if (hwnd != IntPtr.Zero && IsWindow(hwnd))
            return hwnd;

        // 2. 遍历本进程所有顶层可见窗口，取面积最大的（= 游戏窗口）
        try
        {
            uint pid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            IntPtr best = IntPtr.Zero;
            long bestArea = 0;

            EnumWindows((h, l) =>
            {
                try
                {
                    GetWindowThreadProcessId(h, out uint wpid);
                    if (wpid != pid) return true;
                    if (!IsWindowVisible(h)) return true;

                    if (!GetWindowRect(h, out var rc)) return true;
                    long w = rc.right - rc.left;
                    long hh = rc.bottom - rc.top;
                    if (w <= 0 || hh <= 0) return true;

                    long area = w * hh;
                    if (area > bestArea)
                    {
                        bestArea = area;
                        best = h;
                    }
                }
                catch { }
                return true;
            }, IntPtr.Zero);

            if (best != IntPtr.Zero)
                return best;
        }
        catch { }

        // 3. 兜底：Process.MainWindowHandle
        try
        {
            var p = System.Diagnostics.Process.GetCurrentProcess();
            if (p.MainWindowHandle != IntPtr.Zero)
                return p.MainWindowHandle;
        }
        catch { }

        return IntPtr.Zero;
    }

    static IntPtr CreateVerticalGradient(ref GPRECTF rect)
    {
        IntPtr brush = IntPtr.Zero;
        int status = GdipCreateLineBrushFromRect(ref rect,
            GRADIENT_TOP, GRADIENT_BOTTOM,
            LinearGradientModeVertical, WrapModeTile, out brush);
        if (status != 0) return IntPtr.Zero;
        return brush;
    }

    static void SetupGdiplusResources()
    {
        if (_fontFamily == IntPtr.Zero)
        {
            Plugin.Logger.LogInfo("[Overlay] 用默认字体 Segoe UI");
            int status = GdipCreateFontFamilyFromName("Segoe UI", IntPtr.Zero, out _fontFamily);
            if (status != 0 || _fontFamily == IntPtr.Zero)
                GdipCreateFontFamilyFromName("Arial", IntPtr.Zero, out _fontFamily);
        }

        float nameSize  = _bgH * NAME_FONT_RATIO;
        float mainSize  = _bgH * MAIN_FONT_RATIO;
        float exactSize = _bgH * EXACT_FONT_RATIO;
        float deltaSize = _bgH * DELTA_FONT_RATIO;

        GdipCreateFont(_fontFamily, nameSize,  FontStyleBold, UnitPixel, out _fontName);
        GdipCreateFont(_fontFamily, mainSize,  FontStyleBold, UnitPixel, out _fontMain);
        GdipCreateFont(_fontFamily, exactSize, FontStyleBold, UnitPixel, out _fontExact);
        GdipCreateFont(_fontFamily, deltaSize, FontStyleBold, UnitPixel, out _fontDelta);

        GdipCreateSolidFill(unchecked((int)0xFFFFFFFF), out _whiteBrush);
        GdipCreateSolidFill(COLOR_POSITIVE, out _brushGreen);
        GdipCreateSolidFill(COLOR_NEGATIVE, out _brushRed);

        GdipCreateStringFormat(0, 0, out _formatLeft);
        GdipSetStringFormatAlign(_formatLeft, StringAlignmentNear);
        GdipSetStringFormatLineAlign(_formatLeft, StringAlignmentLineCenter);

        GdipCreateStringFormat(0, 0, out _formatCenter);
        GdipSetStringFormatAlign(_formatCenter, StringAlignmentCenter);
        GdipSetStringFormatLineAlign(_formatCenter, StringAlignmentLineCenter);

        Plugin.Logger.LogInfo($"[Overlay] 字号 Name={nameSize:F1} Main={mainSize:F1} Exact={exactSize:F1} Delta={deltaSize:F1}");
    }

    static void SetupDib()
    {
        var screenDC = GetDC(IntPtr.Zero);
        _memDC = CreateCompatibleDC(screenDC);

        var bmi = new BITMAPINFO();
        bmi.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER));
        bmi.bmiHeader.biWidth = _bgW;
        bmi.bmiHeader.biHeight = -_bgH;
        bmi.bmiHeader.biPlanes = 1;
        bmi.bmiHeader.biBitCount = 32;
        bmi.bmiHeader.biCompression = 0;

        _dibSection = CreateDIBSection(screenDC, ref bmi, 0, out _dibBits, IntPtr.Zero, 0);
        _dibOldObj = SelectObject(_memDC, _dibSection);

        ReleaseDC(IntPtr.Zero, screenDC);
        Plugin.Logger.LogInfo($"[Overlay] DIB: {_bgW}x{_bgH}");
    }

    static void RenderToDib()
    {
        IntPtr work = IntPtr.Zero;
        IntPtr gradMain = IntPtr.Zero;
        IntPtr gradExact = IntPtr.Zero;

        try
        {
            int status = GdipCloneBitmapAreaI(0, 0, _bgW, _bgH, PixelFormat32bppARGB,
                _bgBitmap, out work);
            if (status != 0 || work == IntPtr.Zero)
            {
                Plugin.Logger.LogError($"[Overlay] clone 失败: {status}");
                return;
            }

            if (GdipGetImageGraphicsContext(work, out var gfx) == 0)
            {
                GdipSetTextRenderingHint(gfx, TextRenderingHintAntiAlias);

                var rectName = new GPRECTF
                {
                    X = _bgW * NAME_X, Y = _bgH * NAME_Y,
                    Width = _bgW * NAME_W, Height = _bgH * NAME_H
                };
                GdipDrawString(gfx, _nameText, -1, _fontName, ref rectName, _formatLeft, _whiteBrush);

                var rectMain = new GPRECTF
                {
                    X = _bgW * MAIN_X, Y = _bgH * MAIN_Y,
                    Width = _bgW * MAIN_W, Height = _bgH * MAIN_H
                };
                gradMain = CreateVerticalGradient(ref rectMain);
                var brushMain = gradMain != IntPtr.Zero ? gradMain : _whiteBrush;
                GdipDrawString(gfx, _mainText, -1, _fontMain, ref rectMain, _formatCenter, brushMain);

                var rectExact = new GPRECTF
                {
                    X = _bgW * EXACT_X, Y = _bgH * EXACT_Y,
                    Width = _bgW * EXACT_W, Height = _bgH * EXACT_H
                };
                gradExact = CreateVerticalGradient(ref rectExact);
                var brushExact = gradExact != IntPtr.Zero ? gradExact : _whiteBrush;
                GdipDrawString(gfx, _exactText, -1, _fontExact, ref rectExact, _formatCenter, brushExact);

                if (!string.IsNullOrEmpty(_deltaText))
                {
                    var rectDelta = new GPRECTF
                    {
                        X = _bgW * DELTA_X, Y = _bgH * DELTA_Y,
                        Width = _bgW * DELTA_W, Height = _bgH * DELTA_H
                    };
                    var brushDelta = _deltaIsPositive ? _brushGreen : _brushRed;
                    GdipDrawString(gfx, _deltaText, -1, _fontDelta, ref rectDelta, _formatCenter, brushDelta);
                }

                GdipDeleteGraphics(gfx);
            }

            var lockRect = new GPRECT { X = 0, Y = 0, Width = _bgW, Height = _bgH };
            if (GdipBitmapLockBits(work, ref lockRect, ImageLockModeRead,
                PixelFormat32bppARGB, out var bd) != 0)
            {
                Plugin.Logger.LogError("[Overlay] lock 失败");
                return;
            }

            unsafe
            {
                byte* src = (byte*)bd.Scan0;
                byte* dst = (byte*)_dibBits;
                int total = _bgW * _bgH;
                for (int i = 0; i < total; i++)
                {
                    byte b = src[0], g = src[1], r = src[2], a = src[3];
                    if (a == 0) { dst[0] = dst[1] = dst[2] = dst[3] = 0; }
                    else if (a == 255) { dst[0] = b; dst[1] = g; dst[2] = r; dst[3] = 255; }
                    else
                    {
                        dst[0] = (byte)(b * a / 255);
                        dst[1] = (byte)(g * a / 255);
                        dst[2] = (byte)(r * a / 255);
                        dst[3] = a;
                    }
                    src += 4; dst += 4;
                }
            }

            GdipBitmapUnlockBits(work, ref bd);
        }
        finally
        {
            if (gradMain != IntPtr.Zero) GdipDeleteBrush(gradMain);
            if (gradExact != IntPtr.Zero) GdipDeleteBrush(gradExact);
            if (work != IntPtr.Zero) GdipDisposeImage(work);
        }
    }

    static void UpdateMouseAlpha()
    {
        float targetAlpha = 1.0f;

        try
        {
            POINT mouse;
            if (GetCursorPos(out mouse))
            {
                int left = _screenX;
                int top = _screenY;
                int right = _screenX + _bgW;
                int bottom = _screenY + _bgH;

                int dx = 0, dy = 0;
                if (mouse.x < left) dx = left - mouse.x;
                else if (mouse.x > right) dx = mouse.x - right;

                if (mouse.y < top) dy = top - mouse.y;
                else if (mouse.y > bottom) dy = mouse.y - bottom;

                double dist = Math.Sqrt(dx * dx + dy * dy);

                if (dist <= MOUSE_FULL_FADE_DIST)
                    targetAlpha = 0f;
                else if (dist <= MOUSE_NEAR_DIST)
                    targetAlpha = (float)((dist - MOUSE_FULL_FADE_DIST)
                                        / (MOUSE_NEAR_DIST - MOUSE_FULL_FADE_DIST));
                else
                    targetAlpha = 1f;
            }
        }
        catch { }

        float speed = targetAlpha < _currentAlpha ? FADE_OUT_SPEED : FADE_IN_SPEED;
        _currentAlpha += (targetAlpha - _currentAlpha) * speed;

        if (Math.Abs(targetAlpha - _currentAlpha) < 0.005f)
            _currentAlpha = targetAlpha;

        if (_currentAlpha < 0f) _currentAlpha = 0f;
        if (_currentAlpha > 1f) _currentAlpha = 1f;
    }

    static void CommitToWindow(IntPtr hWnd)
    {
        var ptDst = new POINT { x = _screenX, y = _screenY };
        var sz = new SIZE { cx = _bgW, cy = _bgH };
        var ptSrc = new POINT { x = 0, y = 0 };

        byte alphaByte = (byte)Math.Clamp((int)(_currentAlpha * 255f), 0, 255);

        var blend = new BLENDFUNCTION
        {
            BlendOp = 0,
            BlendFlags = 0,
            SourceConstantAlpha = alphaByte,
            AlphaFormat = 1
        };
        UpdateLayeredWindow(hWnd, IntPtr.Zero, ref ptDst, ref sz,
            _memDC, ref ptSrc, 0, ref blend, ULW_ALPHA);
    }

    static void ComputeTargetPosition()
    {
        if (_gameHwnd == IntPtr.Zero || !IsWindow(_gameHwnd)) return;

        var pt = new POINT { x = 0, y = 0 };
        if (!ClientToScreen(_gameHwnd, ref pt)) return;

        RECT rc;
        if (!GetClientRect(_gameHwnd, out rc)) return;

        int clientWidth = rc.right - rc.left;

        _screenX = pt.x + (clientWidth - _bgW) / 2;
        _targetY = pt.y + OVERLAY_OFFSET_Y;
    }

    static void RunOverlay(Func<string> mainProvider)
    {
        StartupGdiplus();
        LoadBackgroundImage();
        LoadCustomFont();
        SetupGdiplusResources();
        SetupDib();

        _gameHwnd = FindGameWindow();
        Plugin.Logger.LogInfo($"[Overlay] 游戏窗口 HWND: 0x{_gameHwnd.ToInt64():X}");

        ComputeTargetPosition();
        bool initialShow = SceneTracker.ShouldShowOverlay();
        _screenY = initialShow ? _targetY : _targetY - _bgH - 40;
        Plugin.Logger.LogInfo($"[Overlay] 初始显示状态: {(initialShow ? "显示" : "隐藏")}");

        _wndProc = WndProcImpl;

        var wc = new WNDCLASS
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            lpszClassName = "InFalsusRatingOverlay"
        };
        RegisterClassW(ref wc);

        uint exStyle = WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
        var hWnd = CreateWindowExW(exStyle, "InFalsusRatingOverlay", "Overlay",
            WS_POPUP, _screenX, _screenY, _bgW, _bgH,
            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (hWnd == IntPtr.Zero) { Plugin.Logger.LogError("[Overlay] CreateWindowEx 失败"); return; }

        if (_gameHwnd != IntPtr.Zero)
        {
            SetWindowLongPtrW(hWnd, GWLP_HWNDPARENT, _gameHwnd);
            Plugin.Logger.LogInfo("[Overlay] owner 已设置");
        }

        ShowWindow(hWnd, SW_SHOWNOACTIVATE);

        _displayRating = Plugin.PlayerRating;
        _displayExact  = Plugin.TotalExacts;

        _nameText  = Plugin.PlayerName;
        _mainText  = _displayRating.ToString("F2");
        _exactText = ((int)Math.Round(_displayExact)).ToString();
        _deltaText = "";
        _lastRenderedName  = _nameText;
        _lastRenderedMain  = _mainText;
        _lastRenderedExact = _exactText;
        _lastRenderedDelta = "";

        RenderToDib();
        CommitToWindow(hWnd);

        var updater = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    if (_gameHwnd == IntPtr.Zero || !IsWindow(_gameHwnd))
                    {
                        _gameHwnd = FindGameWindow();
                        if (_gameHwnd != IntPtr.Zero)
                            SetWindowLongPtrW(hWnd, GWLP_HWNDPARENT, _gameHwnd);
                    }

                    ComputeTargetPosition();
                    UpdateMouseAlpha();

                    bool rawShouldShow = SceneTracker.ShouldShowOverlay() && !Plugin.OverlayManuallyHidden;

                    if (rawShouldShow != _stableShow)
                    {
                        if (!rawShouldShow)
                        {
                            _stableShow = false;
                            _pendingShow = false;
                        }
                        else
                        {
                            if (!_pendingShow)
                            {
                                _pendingShow = true;
                                _pendingShowAt = DateTime.Now;
                            }
                            else if ((DateTime.Now - _pendingShowAt).TotalMilliseconds >= SHOW_CONFIRM_MS)
                            {
                                _stableShow = true;
                                _pendingShow = false;
                            }
                        }
                    }
                    else
                    {
                        _pendingShow = false;
                    }

                    int destY = _stableShow ? _targetY : _targetY - _bgH - 40;

                    if (_screenY != destY)
                    {
                        int diff = destY - _screenY;
                        if (Math.Abs(diff) <= 1)
                            _screenY = destY;
                        else
                            _screenY += (int)(diff * ANIM_LERP);
                    }

                    double targetRating = Plugin.PlayerRating;
                    int    targetExact  = Plugin.TotalExacts;

                    double ratingDiff = targetRating - _displayRating;
                    if (Math.Abs(ratingDiff) < 0.005)
                        _displayRating = targetRating;
                    else
                        _displayRating += ratingDiff * DIGIT_LERP;

                    double exactDiff = targetExact - _displayExact;
                    if (Math.Abs(exactDiff) < 0.5)
                        _displayExact = targetExact;
                    else
                        _displayExact += exactDiff * DIGIT_LERP;

                    _nameText  = Plugin.PlayerName;
                    _mainText  = _displayRating.ToString("F2");
                    _exactText = ((int)Math.Round(_displayExact)).ToString();

                    if (Plugin.HasRatingDelta)
                    {
                        if ((DateTime.Now - Plugin.RatingDeltaShownAt).TotalSeconds > 8)
                        {
                            Plugin.ClearRatingDelta();
                            _deltaText = "";
                        }
                        else
                        {
                            _deltaIsPositive = Plugin.RatingDeltaValue >= 0;
                            string sign = _deltaIsPositive ? "+" : "-";
                            _deltaText = $"{sign}{Math.Abs(Plugin.RatingDeltaValue):F2}";
                        }
                    }
                    else
                    {
                        _deltaText = "";
                    }

                    if (_nameText != _lastRenderedName ||
                        _mainText != _lastRenderedMain ||
                        _exactText != _lastRenderedExact ||
                        _deltaText != _lastRenderedDelta)
                    {
                        RenderToDib();
                        _lastRenderedName  = _nameText;
                        _lastRenderedMain  = _mainText;
                        _lastRenderedExact = _exactText;
                        _lastRenderedDelta = _deltaText;
                    }

                    CommitToWindow(hWnd);
                }
                catch { }
                Thread.Sleep(ANIM_FRAME_MS);
            }
        });
        updater.IsBackground = true;
        updater.Start();

        while (true)
        {
            if (GetMessageW(out var msg, IntPtr.Zero, 0, 0) <= 0) break;
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }
    }

    static IntPtr WndProcImpl(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_DESTROY)
        {
            if (_bgBitmap != IntPtr.Zero) GdipDisposeImage(_bgBitmap);
            if (_privateFontCollection != IntPtr.Zero) GdipDeletePrivateFontCollection(ref _privateFontCollection);
            if (_gdiplusToken != IntPtr.Zero) GdiplusShutdown(_gdiplusToken);
            if (_dibOldObj != IntPtr.Zero) SelectObject(_memDC, _dibOldObj);
            if (_dibSection != IntPtr.Zero) DeleteObject(_dibSection);
            if (_memDC != IntPtr.Zero) DeleteDC(_memDC);
            PostQuitMessage(0);
            return IntPtr.Zero;
        }
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }
}

// ========== 定数表 ==========
public static class ConstTable
{
    private static Dictionary<string, int> _table = new Dictionary<string, int>();

    private static readonly Dictionary<string, int> _defaultsByDifficulty =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        { "Minimal",    4 }, { "Evolved",    7 },
        { "Ultimate",  10 }, { "Forbidden", 13 }
    };

    public static string GetKey(string songId, string difficulty) => $"{songId}|{difficulty}";

    public static int Get(string songId, string difficulty)
    {
        if (_table.TryGetValue(GetKey(songId, difficulty), out int exact)) return exact;
        if (_defaultsByDifficulty.TryGetValue(difficulty, out int def)) return def;
        return -1;
    }

    public static void Load()
    {
        try
        {
            string dir = Path.Combine(Paths.ConfigPath, "InFalsusRating");
            string file = Path.Combine(dir, "const.json");
            Directory.CreateDirectory(dir);

            if (!File.Exists(file))
            {
                File.WriteAllText(file, "{\n  \"12|Evolved\": 9\n}\n");
                return;
            }

            string json = File.ReadAllText(file);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return;

            int count = 0;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Name.StartsWith("_")) continue;
                if (prop.Value.ValueKind == JsonValueKind.Number &&
                    prop.Value.TryGetInt32(out int v))
                {
                    _table[prop.Name] = v;
                    count++;
                }
            }
            Plugin.Logger.LogInfo($"[Const] 载入 {count} 条精确定数");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[Const] 读取失败: {ex.Message}");
        }
    }

    public static void SaveIfIncomplete(Dictionary<string, int> full)
    {
        if (full == null || full.Count == 0)
        {
            Plugin.Logger.LogWarning("[Const] 游戏数据为空，跳过生成");
            return;
        }

        if (_table.Count >= 10)
        {
            Plugin.Logger.LogInfo($"[Const] 已有 {_table.Count} 条定数，跳过自动生成");
            return;
        }

        try
        {
            string dir = Path.Combine(Paths.ConfigPath, "InFalsusRating");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "const.json");

            var ordered = full
                .OrderBy(kv =>
                {
                    var parts = kv.Key.Split('|');
                    return int.TryParse(parts[0], out var id) ? id : int.MaxValue;
                })
                .ThenBy(kv => kv.Key);

            var sb = new StringBuilder();
            sb.AppendLine("{");
            int i = 0;
            int total = full.Count;
            foreach (var kv in ordered)
            {
                sb.Append($"  \"{kv.Key}\": {kv.Value}");
                i++;
                if (i < total) sb.Append(",");
                sb.AppendLine();
            }
            sb.AppendLine("}");

            File.WriteAllText(file, sb.ToString());
            _table = full;

            Plugin.Logger.LogInfo($"[Const] 已生成 {full.Count} 条定数: {file}");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[Const] 自动生成失败: {ex.Message}");
        }
    }
}

// ========== 曲名 + 定数缓存 ==========
public static class SongInfoCache
{
    private static Dictionary<string, string> _songIdToName = new Dictionary<string, string>();
    private static bool _loaded = false;

    // catalog.json 数据（曲名/曲师/曲绘 base_name 的权威来源）
    private static Dictionary<string, string> _baseNameById = new Dictionary<string, string>();
    private static Dictionary<string, string> _titleById    = new Dictionary<string, string>();
    private static Dictionary<string, string> _artistById   = new Dictionary<string, string>();
    private static bool _catalogLoaded = false;

    static string CatalogPath => Path.Combine(Paths.ConfigPath, "InFalsusRating", "catalog.json");

    public static void LoadCatalog()
    {
        if (_catalogLoaded) return;
        _catalogLoaded = true;

        try
        {
            if (!File.Exists(CatalogPath))
            {
                Plugin.Logger.LogInfo($"[Catalog] 未找到 catalog.json，使用游戏内 dump 数据");
                return;
            }

            string json = File.ReadAllText(CatalogPath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return;

            if (!doc.RootElement.TryGetProperty("songs", out var songs)
                || songs.ValueKind != JsonValueKind.Array)
                return;

            foreach (var song in songs.EnumerateArray())
            {
                if (!song.TryGetProperty("id", out var idEl)) continue;

                string id = idEl.ValueKind switch
                {
                    JsonValueKind.Number => idEl.GetInt32().ToString(),
                    JsonValueKind.String => idEl.GetString(),
                    _ => null,
                };
                if (string.IsNullOrEmpty(id)) continue;

                if (song.TryGetProperty("base_name", out var bn) && bn.ValueKind == JsonValueKind.String)
                    _baseNameById[id] = bn.GetString();

                if (song.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String)
                    _titleById[id] = t.GetString();

                if (song.TryGetProperty("artist", out var a) && a.ValueKind == JsonValueKind.String)
                    _artistById[id] = a.GetString();
            }

            Plugin.Logger.LogInfo($"[Catalog] 载入 {_titleById.Count} 条曲目元数据");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[Catalog] 读取失败: {ex.Message}");
        }
    }

    public static void Load(object nhInstance)
    {
        if (_loaded) return;
        _loaded = true;

        try
        {
            var vnaMethod = nhInstance.GetType().GetMethod("_VNA",
                BindingFlags.Public | BindingFlags.Instance);
            var dataAccess = vnaMethod?.Invoke(nhInstance, null);
            if (dataAccess == null) { Plugin.Logger.LogWarning("[Cache] DataAccess null"); return; }

            var sdProp = dataAccess.GetType().GetProperty("SongData",
                BindingFlags.Public | BindingFlags.Instance);
            var songData = sdProp?.GetValue(dataAccess);
            if (songData == null) { Plugin.Logger.LogWarning("[Cache] SongData null"); return; }

            var getter = songData.GetType().GetMethod("get_allSongInfo",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (getter == null) { Plugin.Logger.LogWarning("[Cache] 无 get_allSongInfo"); return; }

            var allSongsObj = getter.Invoke(songData, null);
            if (allSongsObj == null) { Plugin.Logger.LogWarning("[Cache] allSongInfo null"); return; }

            var arrType = allSongsObj.GetType();
            var lenProp = arrType.GetProperty("Length");
            var itemProp = arrType.GetProperty("Item");
            int len = Convert.ToInt32(lenProp.GetValue(allSongsObj));

            var constDict = new Dictionary<string, int>();

            for (int i = 0; i < len; i++)
            {
                object songObj = null;
                try { songObj = itemProp.GetValue(allSongsObj, new object[] { i }); }
                catch { continue; }
                if (songObj == null) continue;

                var songType = songObj.GetType();
                var idProp   = songType.GetProperty("Id",         BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var nameProp = songType.GetProperty("BaseName",   BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var ciProp   = songType.GetProperty("ChartInfos", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                string songId = null, baseName = null;
                try { songId = idProp?.GetValue(songObj)?.ToString(); } catch { }
                try { baseName = nameProp?.GetValue(songObj)?.ToString(); } catch { }

                if (!string.IsNullOrEmpty(songId) && !_songIdToName.ContainsKey(songId))
                    _songIdToName[songId] = baseName ?? songId;

                if (string.IsNullOrEmpty(songId) || ciProp == null) continue;
                object ciArrObj = null;
                try { ciArrObj = ciProp.GetValue(songObj); } catch { }
                if (ciArrObj == null) continue;

                var ciArrType = ciArrObj.GetType();
                var ciLenProp = ciArrType.GetProperty("Length");
                var ciItemProp = ciArrType.GetProperty("Item");
                if (ciLenProp == null || ciItemProp == null) continue;

                int ciLen = Convert.ToInt32(ciLenProp.GetValue(ciArrObj));
                for (int j = 0; j < ciLen; j++)
                {
                    object ci = null;
                    try { ci = ciItemProp.GetValue(ciArrObj, new object[] { j }); }
                    catch { continue; }
                    if (ci == null) continue;

                    var ciType = ci.GetType();
                    var diffProp   = ciType.GetProperty("Difficulty", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    var ratingProp = ciType.GetProperty("Rating",     BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                    string diffStr = null;
                    int rating = 0;
                    try { diffStr = diffProp?.GetValue(ci)?.ToString(); } catch { }
                    try { rating = Convert.ToInt32(ratingProp?.GetValue(ci) ?? 0); } catch { }

                    if (string.IsNullOrEmpty(diffStr)) continue;

                    string key = $"{songId}|{diffStr}";
                    if (!constDict.ContainsKey(key))
                        constDict[key] = rating;
                }
            }

            Plugin.Logger.LogInfo($"[Cache] 曲名 {_songIdToName.Count} 首，定数 {constDict.Count} 条");
            ConstTable.SaveIfIncomplete(constDict);
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[Cache] 加载失败: {ex.Message}");
        }
    }

    // 显示用曲名 —— 优先 catalog 的 title，退回游戏 dump 的 base_name
    public static string GetName(string songId)
    {
        if (_titleById.TryGetValue(songId, out var t) && !string.IsNullOrEmpty(t))
            return t;
        return _songIdToName.TryGetValue(songId, out var name) ? name : songId;
    }

    // 曲绘文件名 —— 优先 catalog 的 base_name
    public static string GetBaseName(string songId)
    {
        if (_baseNameById.TryGetValue(songId, out var bn) && !string.IsNullOrEmpty(bn))
            return bn;
        return _songIdToName.TryGetValue(songId, out var name) ? name : songId;
    }

        // 曲师 —— 优先 catalog
    public static string GetArtist(string songId)
    {
        if (_artistById.TryGetValue(songId, out var a) && !string.IsNullOrEmpty(a))
            return a;
        return "";
    }
}

// ========== 评级计算 ==========
public static class RatingEngine
{
    public static object LastGameResults = null;

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

            // B30：所有成绩都参与（含未通关）
            var bestByScore = all
                .GroupBy(r => (r.SongId.ToString(), r.Difficulty.ToString()))
                .Select(g => g.OrderByDescending(r => r.PlayerScore).First())
                .ToList();

            // 每谱面历史最高 dive（含负值）
            var maxPaceByChart = all
                .GroupBy(r => (r.SongId.ToString(), r.Difficulty.ToString()))
                .ToDictionary(g => g.Key, g => g.Max(r => (int)r.CalculatedPaceValue));

            // 每谱面历史最佳通关状态（独立于最高分）
            // GameResultClear: None=0, DiveFailed=1, DiveCleared=2, FullLink=3, PerfectDive=4
            var maxClearByChart = all
                .GroupBy(r => (r.SongId.ToString(), r.Difficulty.ToString()))
                .ToDictionary(g => g.Key, g => g.Max(r => (int)r.CalculatedResultClear));

            // EXACTIFICATION 只统计通关成绩
            var passed = all.Where(r => r.CalculatedPaceValue >= 0).ToList();

            var maxShinyByChart = passed
                .GroupBy(r => (r.SongId.ToString(), r.Difficulty.ToString()))
                .ToDictionary(g => g.Key, g => g.Max(r => CountShinies(r)));

            var noteCountByChart = passed
                .GroupBy(r => (r.SongId.ToString(), r.Difficulty.ToString()))
                .ToDictionary(g => g.Key, g => g.Max(r => CountNotes(r)));

            // EXACTIFICATION：Top 10 谱面的 EXACTIFICATION 率之和
            // 单曲率 = 100 × EXACT数量 × 谱面定数 / 谱面总物量
            var exactRates = new List<(string SongId, string Diff, double Rate, int Shiny)>();
            foreach (var kv in maxShinyByChart)
            {
                var (songId, diff) = kv.Key;
                int shiny = kv.Value;
                int notes = noteCountByChart.TryGetValue(kv.Key, out var n) ? n : 0;
                int constant = ConstTable.Get(songId, diff);
                if (notes <= 0 || constant <= 0) continue;

                exactRates.Add((songId, diff, 100.0 * shiny * constant / notes, shiny));
            }

            var topExact = exactRates.OrderByDescending(t => t.Rate).Take(10).ToList();
            Plugin.TotalExacts = (int)Math.Round(topExact.Sum(t => t.Rate));
            Plugin.TopExactCharts = topExact.Select(t => new ExactChart
            {
                SongId = t.SongId, Difficulty = t.Diff, Rate = t.Rate, ExactCount = t.Shiny
            }).ToList();

            string fingerprint = string.Join("|",
                bestByScore.OrderBy(r => r.SongId.ToString() + r.Difficulty.ToString())
                    .Select(r => $"{r.SongId}:{r.Difficulty}:{r.PlayerScore}:{r.CalculatedPaceValue}"));

            if (fingerprint == _lastFingerprint) return;
            _lastFingerprint = fingerprint;

            var chartRatings = new List<ChartRating>();
            foreach (var r in bestByScore)
            {
                string songId = r.SongId.ToString();
                string diff = r.Difficulty.ToString();
                int constant = ConstTable.Get(songId, diff);
                if (constant < 0) continue;

                var key = (songId, diff);
                int maxPace = maxPaceByChart.TryGetValue(key, out var mp) ? mp : 0;
                int maxShiny = maxShinyByChart.TryGetValue(key, out var ms) ? ms : 0;
                int noteCount = noteCountByChart.TryGetValue(key, out var nc) ? nc : 0;
                double exactRate = noteCount > 0 ? 100.0 * maxShiny * constant / noteCount : 0;

                double baseRating = constant + ScoreBonus(r.PlayerScore);

                double rating;
                if (maxPace >= 0)
                {
                    rating = baseRating + maxPace * 0.01;
                }
                else
                {
                    // dive 为负：逐级累加惩罚
                    // n = |dive|，sumDive = -(1+2+...+n)，penaltyPct = floor(sumDive / 1.2)
                    // factor = 1 + penaltyPct/100，下限 0.1（最多减 90%）
                    int n = -maxPace;                        // 1..15
                    double sumDive = -(n * (n + 1) / 2.0);
                    double penaltyPct = Math.Floor(sumDive / 1.2);
                    double factor = 1.0 + penaltyPct / 100.0;
                    if (factor < 0.1) factor = 0.1;
                    rating = baseRating * factor;
                }

                int clearVal = maxClearByChart.TryGetValue(key, out var cv) ? cv : 0;
                bool fc = clearVal >= 3;   // FullLink
                bool pm = clearVal >= 4;   // PerfectDive

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
                    IsPM = pm
                });
            }

            chartRatings = chartRatings.OrderByDescending(c => c.Rating).ToList();
            Plugin.ChartRatings = chartRatings;

            double oldRating = Plugin.PlayerRating;
            double newRating = CalculatePlayerRating(chartRatings);

            if (Plugin.FirstRatingReady && Math.Abs(newRating - oldRating) > 0.005)
            {
                Plugin.SetRatingDelta(oldRating, newRating);
                Plugin.Logger.LogInfo(
                    $"[Rating] delta {newRating - oldRating:+0.000;-0.000} ({oldRating:F3} → {newRating:F3})");
            }

            Plugin.PlayerRating = newRating;
            Plugin.FirstRatingReady = true;

            Plugin.Logger.LogInfo(
                $"[Rating] ({reason}) 谱面 {chartRatings.Count} 个，评级 {Plugin.PlayerRating:F3}，EXACT {Plugin.TotalExacts}");

            DumpB30(chartRatings);
            DumpAllScores(chartRatings);
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[Rating] Recalculate 出错: {ex.Message}");
        }
    }

    static double ScoreBonus(ulong score)
    {
        const double BaseScore = 95_000_000;
        const double RangeForMax = 5_000_000;
        const double MaxBonus = 2.0;
        return Math.Clamp(((double)score - BaseScore) / RangeForMax * MaxBonus, -2.0, MaxBonus);
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
            string dir = Path.Combine(Paths.ConfigPath, "InFalsusRating");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "b30.txt");

            var lines = new List<string>();
            lines.Add("===========================================");
            lines.Add("In Falsus Rating - B30 排行");
            lines.Add($"玩家:     {Plugin.PlayerName}");
            lines.Add($"更新时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            lines.Add($"总评级:   {Plugin.PlayerRating:F3}");
            lines.Add($"总 EXACT (Top 10): {Plugin.TotalExacts}");
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
                string diff   = $"{c.Difficulty} ({c.Constant})".PadRight(16);
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

    // 导出全谱面成绩（含未通关），文件名 scores.txt
    static void DumpAllScores(List<ChartRating> sorted)
    {
        try
        {
            string dir = Path.Combine(Paths.ConfigPath, "InFalsusRating");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "scores.txt");

            var lines = new List<string>();
            lines.Add("===========================================");
            lines.Add("In Falsus Rating - 全部谱面成绩");
            lines.Add($"玩家:     {Plugin.PlayerName}");
            lines.Add($"更新时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            lines.Add($"总评级:   {Plugin.PlayerRating:F3}");
            lines.Add($"总 EXACTIFICATION (Top 10): {Plugin.TotalExacts}");
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
                string diff   = $"{c.Difficulty} ({c.Constant})".PadRight(16);
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

// ========== Patches ==========
[HarmonyPatch]
public static class Patch_NH_VNA
{
    static MethodBase TargetMethod()
    {
        Type nhType = null;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            nhType = asm.GetType("_K._NH");
            if (nhType != null) break;
        }
        return nhType?.GetMethod("_vNA", BindingFlags.Public | BindingFlags.Instance);
    }

    static void Postfix(object __instance, object __result)
    {
        if (__instance != null)
            SongInfoCache.Load(__instance);

        if (__result == null) return;
        RatingEngine.LastGameResults = __result;
        RatingEngine.Recalculate(__result, "界面刷新");
    }
}

[HarmonyPatch]
public static class Patch_UIManagerUpdate
{
    static MethodBase TargetMethod()
    {
        Type t = null;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            t = asm.GetType("ifapp.Game.UI.Common.UIManager");
            if (t != null) break;
        }
        return t?.GetMethod("Update", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
    }

    static void Postfix()
    {
        ResultsDetector.Tick();
    }
}

public class ExactChart
{
    public string SongId;
    public string Difficulty;
    public double Rate;
    public int ExactCount;
}

public class ChartRating
{
    public string SongId;
    public string Difficulty;
    public ulong Score;
    public int Constant;
    public int PaceValue;
    public int ExactCount;
    public double ExactRate;
    public double Rating;
    public bool IsFC;
    public bool IsPM;
}

// ========== B30 卡片导出 ==========
public static class B30CardExporter
{
    // 文字整体垂直偏移
    const float TEXT_OFFSET_Y = 14f;

    // ---- 卡片 & 大图尺寸 ----
    const int CARD_W = 220;
    const int CARD_H = 330;
    const int COLS   = 6;
    const int ROWS   = 5;
    const int GAP    = 30;
    const int OUTER  = 30;

    // 曲绘在卡片内的位置与尺寸（卡片局部坐标）
    const int JACKET_X = 10;
    const int JACKET_Y = 10;
    const int JACKET_W = 200;
    const int JACKET_H = 200;

    // 左侧玩家信息面板宽约 33%，卡片区在右侧约 67%
    const int INFO_W = 740;
    const int CARDS_ORIGIN_X = INFO_W + OUTER;

    const int BIG_W = CARDS_ORIGIN_X + COLS * CARD_W + (COLS - 1) * GAP + OUTER;  // 2270
    const int BIG_H = OUTER * 2 + ROWS * CARD_H + (ROWS - 1) * GAP;               // 1830

    // ---- 左下方 EXACTIFICATION B10 区域（770 × 1055，下方留 ~230px） ----
    const int B10_Y      = 545;    // 区域起点
    const int B10_COUNT  = 10;
    const int B10_GAP    = 5;
    const int B10_AREA_H = 1055;
    const int B10_ROW_H  = (B10_AREA_H - (B10_COUNT - 1) * B10_GAP) / B10_COUNT;  // 101

    // 行内卡片按比例缩小（0.8 = 空出约 20% 背景）
    const float EXACT_SCALE  = 0.8f;
    const int B10_CARD_W     = (int)(CARDS_ORIGIN_X * EXACT_SCALE);  // 616
    const int B10_CARD_H     = (int)(B10_ROW_H * EXACT_SCALE);       // 80
    const int B10_CARD_X     = (CARDS_ORIGIN_X - B10_CARD_W) / 2;    // 77（水平居中）
    const int B10_CARD_OFFY  = (B10_ROW_H - B10_CARD_H) / 2;         // 10（垂直居中）

    // 无曲绘版：排名列 + 卡片作为整体，在 770 宽内居中
    const int NJ_RANK_COL_W = 70;
    const int NJ_RANK_GAP   = 20;
    const int NJ_BLOCK_W    = NJ_RANK_COL_W + NJ_RANK_GAP + B10_CARD_W;  // 696
    const int NJ_BLOCK_X    = (CARDS_ORIGIN_X - NJ_BLOCK_W) / 2;         // 37
    const int NJ_CARD_X     = NJ_BLOCK_X + NJ_RANK_COL_W + NJ_RANK_GAP;  // 117

    // ---- 路径 ----
    static string Root        => Path.Combine(Paths.ConfigPath, "InFalsusRating");
    static string CardsDir    => Path.Combine(Root, "b30_cards");
    static string JacketDir   => Path.Combine(Root, "jacket");

    // 大图底图候选（先 jacket/，再 b30_cards/）
    static string[] BigBgCandidates => new[]
    {
        Path.Combine(JacketDir, "background_big.png"),
        Path.Combine(CardsDir, "background_big.png"),
    };

    // ---- 图层顺序（从下往上） ----
    // null 表示曲绘层（从 jacket/ 按 SongId 找）
    static readonly (string SubDir, bool IsJacket)[] LayerOrder = new[]
    {
        ("background", false),   // 背景（220×330）
        (null,         true),    // 曲绘
        ("difficulty", false),   // 难度底板
        ("score",      false),   // 评分底板
        ("fcpm",       false),   // FC / PM 徽章
        ("decor",      false),   // 背景装饰
    };

    // ---- 文字布局（局部坐标，单位 px） ----
    // Align: 0=left（X 是左边界） 1=mid（X 是水平中心） 2=right（X 是右边界）
    // FontId: 0=Furore 1=Segoe UI Bold 2=Segoe UI Regular
    // Color2 != 0 时用 Color→Color2 垂直渐变
    struct TextSlot
    {
        public float X, Y, Size;
        public int Align, FontId, Color, Color2;
    }

    static readonly TextSlot SlotRank   = new TextSlot { X =  20f,   Y =  6f,   Size = 22f, Align = 1, FontId = 0, Color = unchecked((int)0xFFFFFFFF) };
    static readonly TextSlot SlotDiff   = new TextSlot { X = 180f,   Y = 185f,   Size = 16f, Align = 1, FontId = 0, Color = unchecked((int)0xFFFFFFFF) };
    static readonly TextSlot SlotTitle  = new TextSlot { X = 110f,   Y = 216f,   Size = 22f, Align = 1, FontId = 1, Color = unchecked((int)0xFFFFFFFF) };
    static readonly TextSlot SlotArtist = new TextSlot { X = 110f,   Y = 234f,   Size = 13f, Align = 1, FontId = 2, Color = unchecked((int)0xFFDFDFDF) };
    static readonly TextSlot SlotScore  = new TextSlot { X =  136f,   Y = 265f,   Size = 22f, Align = 1, FontId = 0, Color = unchecked((int)0xFFFFFFFF) };
    static readonly TextSlot SlotDive   = new TextSlot { X =  68f,   Y = 302f, Size = 24f, Align = 1, FontId = 0, Color = unchecked((int)0xFFFFFFFF) };
    static readonly TextSlot SlotExact  = new TextSlot { X = 173f, Y = 302f, Size = 24f, Align = 2, FontId = 0, Color = unchecked((int)0xFFFFFFFF) };

    // rank ≥ 10（两位数）用更小字号，宽度约等于 22px 下的单数字
    const float RANK_SMALL_SIZE = 16f;

    // title/artist 超出该长度时按比例缩小字号（上限为槽位默认字号，无下限）
    const int TITLE_MAX_CHARS  = 16;
    const int ARTIST_MAX_CHARS = 26;

    // ---- 左侧玩家信息面板槽位（整图坐标，文字垂直中心 = Y + 14） ----
    // 底图结构：标题板 y 85~407，暗色块 y 476 起
    static readonly TextSlot SlotInfoName   = new TextSlot { X = 10f, Y =  145f, Size = 142f, Align = 0, FontId = 0, Color = unchecked((int)0xFFFFFFFF) };
    // rating / exact 用与 overlay 相同的游戏 UI 渐变
    static readonly TextSlot SlotInfoRating = new TextSlot { X = 185f, Y = 395f, Size = 96f, Align = 1, FontId = 0, Color = unchecked((int)0xFF80AEFF), Color2 = unchecked((int)0xFFE0C9FF) };
    static readonly TextSlot SlotInfoExact  = new TextSlot { X = 480f, Y = 405f, Size = 60f, Align = 1, FontId = 0, Color = unchecked((int)0xFF80AEFF), Color2 = unchecked((int)0xFFE0C9FF) };

    // ---- 无曲绘版玩家信息槽位（比曲绘版整体上移 40px，X 不变） ----
    static readonly TextSlot SlotNJName   = new TextSlot { X = 10f,  Y = 113f, Size = 142f, Align = 0, FontId = 0, Color = unchecked((int)0xFFFFFFFF) };
    static readonly TextSlot SlotNJRating = new TextSlot { X = 185f, Y = 365f, Size = 96f,  Align = 1, FontId = 0, Color = unchecked((int)0xFF80AEFF), Color2 = unchecked((int)0xFFE0C9FF) };
    static readonly TextSlot SlotNJExact  = new TextSlot { X = 480f, Y = 375f, Size = 60f,  Align = 1, FontId = 0, Color = unchecked((int)0xFF80AEFF), Color2 = unchecked((int)0xFFE0C9FF) };

    // ---- EXACTIFICATION B10 行槽位（卡片相对坐标，垂直中心 = Y + 14） ----
    static readonly TextSlot SlotB10Rank  = new TextSlot { X =  12f, Y = 28f, Size = 30f, Align = 0, FontId = 2, Color = unchecked((int)0xFFFFFFFF) };
    static readonly TextSlot SlotB10Title = new TextSlot { X =  72f, Y = 28f, Size = 26f, Align = 0, FontId = 1, Color = unchecked((int)0xFFFFFFFF) };
    static readonly TextSlot SlotB10Count = new TextSlot { X = 400f, Y = 28f, Size = 30f, Align = 1, FontId = 0, Color = unchecked((int)0xFFFFFFFF) };
    static readonly TextSlot SlotB10Rate  = new TextSlot { X = B10_CARD_W - 80f, Y = 28, Size = 30f, Align = 1, FontId = 0, Color = unchecked((int)0xFFFFFFFF) };

    // ---- 无曲绘 B30 行槽位（Y 不再用于纵向定位，曲名+曲师整体按像素居中） ----
    static readonly TextSlot SlotB30RowTitle  = new TextSlot { X = 72f, Y = 18f, Size = 35f, Align = 0, FontId = 1, Color = unchecked((int)0xFFFFFFFF) };
    static readonly TextSlot SlotB30RowArtist = new TextSlot { X = 76f, Y = 43f, Size = 14f, Align = 0, FontId = 2, Color = unchecked((int)0xFFDFDFDF) };

    // ---- 无曲绘版排名数字（排名列内居中对齐，Furore） ----
    static readonly TextSlot SlotNJRank = new TextSlot { X = NJ_BLOCK_X + NJ_RANK_COL_W / 2f, Y = 36f, Size = 30f, Align = 1, FontId = 0, Color = unchecked((int)0xFFFFFFFF) };

    // B10 行 rank/曲名颜色：按难度（从简单到难）
    static readonly Dictionary<string, int> B10DiffColors = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        { "Minimal",   unchecked((int)0xFF17A9BD) },
        { "Evolved",   unchecked((int)0xFFC4E0FF) },
        { "Ultimate",  unchecked((int)0xFFE8C4FF) },
        { "Forbidden", unchecked((int)0xFFFFCCCC) },
    };

    static int GetB10DiffColor(string difficulty)
        => B10DiffColors.TryGetValue(difficulty, out var c) ? c : unchecked((int)0xFFFFFFFF);

    // ---- 颜色 ----
    static readonly int C_BG_TOP    = unchecked((int)0xFF0F1420);
    static readonly int C_BG_BOTTOM = unchecked((int)0xFF1A2238);

    // ---- GDI+ 常量 ----
    const int PixelFormat32bppARGB = 0x0026200A;
    const int UnitPixel = 2;
    const int FontStyleRegular = 0;
    const int FontStyleBold    = 1;
    const int StringAlignmentNear       = 0;
    const int StringAlignmentCenter     = 1;
    const int StringAlignmentFar        = 2;
    const int StringAlignmentLineCenter = 1;
    const int StringFormatFlagsNoWrap   = 0x1000;
    const int TextRenderingHintAntiAlias = 4;
    const int LinearGradientModeVertical = 1;
    const int WrapModeTile = 0;

    // ---- 运行时状态 ----
    static IntPtr _fontCollection = IntPtr.Zero;
    static IntPtr _furoreFamily   = IntPtr.Zero;
    static IntPtr _segoeFamily    = IntPtr.Zero;

    static IntPtr _fontRank      = IntPtr.Zero;
    static IntPtr _fontRankSmall = IntPtr.Zero;
    static IntPtr _fontDiff      = IntPtr.Zero;
    static IntPtr _fontScore  = IntPtr.Zero;
    static IntPtr _fontDive   = IntPtr.Zero;
    static IntPtr _fontExact  = IntPtr.Zero;
    static IntPtr _fontTitle  = IntPtr.Zero;
    static IntPtr _fontArtist = IntPtr.Zero;

    static IntPtr _fontTitleFurore    = IntPtr.Zero;
    static IntPtr _fontB10TitleFurore = IntPtr.Zero;

    static IntPtr _fontInfoName   = IntPtr.Zero;
    static IntPtr _fontInfoRating = IntPtr.Zero;
    static IntPtr _fontInfoExact  = IntPtr.Zero;

    static IntPtr _fontB10Rank  = IntPtr.Zero;
    static IntPtr _fontB10Title = IntPtr.Zero;
    static IntPtr _fontB10Count = IntPtr.Zero;
    static IntPtr _fontB10Rate  = IntPtr.Zero;

    static IntPtr _fontB30Title      = IntPtr.Zero;
    static IntPtr _fontB30TitleFurore = IntPtr.Zero;
    static IntPtr _fontB30Artist     = IntPtr.Zero;

    static IntPtr _brWhite = IntPtr.Zero;
    static IntPtr _fmtLeft       = IntPtr.Zero;
    static IntPtr _fmtMid        = IntPtr.Zero;
    static IntPtr _fmtRight      = IntPtr.Zero;
    static IntPtr _fmtNameNowrap = IntPtr.Zero;

    static readonly Dictionary<string, IntPtr> _imageCache = new Dictionary<string, IntPtr>();

    static Guid _pngClsid = Guid.Empty;

    // ---- P/Invoke ----
    [StructLayout(LayoutKind.Sequential)]
    struct GdiplusStartupInput
    {
        public uint GdiplusVersion;
        public IntPtr DebugEventCallback;
        public int SuppressBackgroundThread;
        public int SuppressExternalCodecs;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct GPRECTF { public float X, Y, Width, Height; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ImageCodecInfo
    {
        public Guid Clsid;
        public Guid FormatID;
        public IntPtr CodecName;
        public IntPtr DllName;
        public IntPtr FormatDescription;
        public IntPtr FilenameExtension;
        public IntPtr MimeType;
        public int Flags;
        public int Version;
        public int SigCount;
        public int SigSize;
        public IntPtr SigPattern;
        public IntPtr SigMask;
    }

    [DllImport("gdiplus.dll")] static extern int GdiplusStartup(out IntPtr token, ref GdiplusStartupInput input, IntPtr output);
    [DllImport("gdiplus.dll")] static extern void GdiplusShutdown(IntPtr token);

    [DllImport("gdiplus.dll")] static extern int GdipCreateBitmapFromScan0(int width, int height, int stride, int format, IntPtr scan0, out IntPtr bitmap);
    [DllImport("gdiplus.dll", CharSet = CharSet.Unicode)] static extern int GdipCreateBitmapFromFile(string filename, out IntPtr bitmap);
    [DllImport("gdiplus.dll")] static extern int GdipGetImageGraphicsContext(IntPtr image, out IntPtr graphics);
    [DllImport("gdiplus.dll")] static extern int GdipDeleteGraphics(IntPtr graphics);
    [DllImport("gdiplus.dll")] static extern int GdipDisposeImage(IntPtr image);
    [DllImport("gdiplus.dll")] static extern int GdipGetImageWidth(IntPtr image, out uint width);
    [DllImport("gdiplus.dll")] static extern int GdipGetImageHeight(IntPtr image, out uint height);
    [DllImport("gdiplus.dll")] static extern int GdipDrawImageRectI(IntPtr graphics, IntPtr image, int x, int y, int w, int h);

    [DllImport("gdiplus.dll")] static extern int GdipNewPrivateFontCollection(out IntPtr fontCollection);
    [DllImport("gdiplus.dll")] static extern int GdipDeletePrivateFontCollection(ref IntPtr fontCollection);
    [DllImport("gdiplus.dll", CharSet = CharSet.Unicode)] static extern int GdipPrivateAddFontFile(IntPtr fontCollection, string filename);
    [DllImport("gdiplus.dll")] static extern int GdipGetFontCollectionFamilyCount(IntPtr fontCollection, out int numFound);
    [DllImport("gdiplus.dll")] static extern int GdipGetFontCollectionFamilyList(IntPtr fontCollection, int numSought, [Out] IntPtr[] gpfamilies, out int numFound);
    [DllImport("gdiplus.dll", CharSet = CharSet.Unicode)] static extern int GdipCreateFontFamilyFromName(string name, IntPtr fontCollection, out IntPtr fontFamily);

    [DllImport("gdiplus.dll")] static extern int GdipCreateFont(IntPtr fontFamily, float emSize, int style, int unit, out IntPtr font);
    [DllImport("gdiplus.dll")] static extern int GdipDeleteFont(IntPtr font);

    [DllImport("gdiplus.dll")] static extern int GdipCreateSolidFill(int color, out IntPtr brush);
    [DllImport("gdiplus.dll")] static extern int GdipCreateLineBrushFromRect(ref GPRECTF rect, int color1, int color2, int mode, int wrapMode, out IntPtr brush);
    [DllImport("gdiplus.dll")] static extern int GdipDeleteBrush(IntPtr brush);

    [DllImport("gdiplus.dll")] static extern int GdipCreateStringFormat(int formatAttributes, int language, out IntPtr format);
    [DllImport("gdiplus.dll")] static extern int GdipSetStringFormatAlign(IntPtr format, int align);
    [DllImport("gdiplus.dll")] static extern int GdipSetStringFormatLineAlign(IntPtr format, int align);
    [DllImport("gdiplus.dll")] static extern int GdipDeleteStringFormat(IntPtr format);
    [DllImport("gdiplus.dll")] static extern int GdipSetTextRenderingHint(IntPtr graphics, int mode);
    [DllImport("gdiplus.dll", CharSet = CharSet.Unicode)] static extern int GdipDrawString(IntPtr graphics, string text, int length, IntPtr font, ref GPRECTF layoutRect, IntPtr stringFormat, IntPtr brush);
    [DllImport("gdiplus.dll", CharSet = CharSet.Unicode)] static extern int GdipMeasureString(IntPtr graphics, string text, int length, IntPtr font, ref GPRECTF layoutRect, IntPtr stringFormat, out GPRECTF boundingBox, out int codepointsFitted, out int linesFilled);

    [DllImport("gdiplus.dll")] static extern int GdipFillRectangleI(IntPtr graphics, IntPtr brush, int x, int y, int w, int h);
    [DllImport("gdiplus.dll")] static extern int GdipSetClipRectI(IntPtr graphics, int x, int y, int width, int height, int combineMode);
    [DllImport("gdiplus.dll")] static extern int GdipResetClip(IntPtr graphics);

    [DllImport("gdiplus.dll")] static extern int GdipGetImageEncodersSize(out int numEncoders, out int size);
    [DllImport("gdiplus.dll")] static extern int GdipGetImageEncoders(int numEncoders, int size, IntPtr encoders);
    [DllImport("gdiplus.dll", CharSet = CharSet.Unicode)] static extern int GdipSaveImageToFile(IntPtr image, string filename, ref Guid clsid, IntPtr encoderParams);

    // ---- PNG CLSID 缓存 ----
    static bool TryGetPngClsid()
    {
        if (_pngClsid != Guid.Empty) return true;
        if (GdipGetImageEncodersSize(out int num, out int size) != 0) return false;
        if (num == 0 || size == 0) return false;

        var buf = Marshal.AllocHGlobal(size);
        try
        {
            if (GdipGetImageEncoders(num, size, buf) != 0) return false;
            int structSize = Marshal.SizeOf<ImageCodecInfo>();
            for (int i = 0; i < num; i++)
            {
                var info = Marshal.PtrToStructure<ImageCodecInfo>(IntPtr.Add(buf, i * structSize));
                string mime = Marshal.PtrToStringUni(info.MimeType);
                if (mime == "image/png") { _pngClsid = info.Clsid; return true; }
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
        return false;
    }

    // ---- 字体初始化 ----
    static bool InitFonts()
    {
        // Furore 私有字体
        string fontPath = Path.Combine(Root, "font2.ttf");
        if (File.Exists(fontPath))
        {
            if (GdipNewPrivateFontCollection(out _fontCollection) == 0
                && GdipPrivateAddFontFile(_fontCollection, fontPath) == 0)
            {
                GdipGetFontCollectionFamilyCount(_fontCollection, out int fc);
                if (fc > 0)
                {
                    var families = new IntPtr[fc];
                    GdipGetFontCollectionFamilyList(_fontCollection, fc, families, out int found);
                    if (found > 0) _furoreFamily = families[0];
                }
            }
        }
        if (_furoreFamily == IntPtr.Zero)
            GdipCreateFontFamilyFromName("Arial", IntPtr.Zero, out _furoreFamily);

        // Segoe UI（系统字体）
        if (GdipCreateFontFamilyFromName("Segoe UI", IntPtr.Zero, out _segoeFamily) != 0
            || _segoeFamily == IntPtr.Zero)
            GdipCreateFontFamilyFromName("Arial", IntPtr.Zero, out _segoeFamily);

        if (_furoreFamily == IntPtr.Zero || _segoeFamily == IntPtr.Zero) return false;

        GdipCreateFont(_furoreFamily, SlotRank.Size,   FontStyleBold,    UnitPixel, out _fontRank);
        GdipCreateFont(_furoreFamily, RANK_SMALL_SIZE, FontStyleBold,    UnitPixel, out _fontRankSmall);
        GdipCreateFont(_furoreFamily, SlotDiff.Size,   FontStyleBold,    UnitPixel, out _fontDiff);
        GdipCreateFont(_furoreFamily, SlotScore.Size,  FontStyleBold,    UnitPixel, out _fontScore);
        GdipCreateFont(_furoreFamily, SlotDive.Size,   FontStyleBold,    UnitPixel, out _fontDive);
        GdipCreateFont(_furoreFamily, SlotExact.Size,  FontStyleBold,    UnitPixel, out _fontExact);
        GdipCreateFont(_segoeFamily,  SlotTitle.Size,  FontStyleBold,    UnitPixel, out _fontTitle);
        GdipCreateFont(_segoeFamily,  SlotArtist.Size, FontStyleRegular, UnitPixel, out _fontArtist);

        GdipCreateFont(_furoreFamily, SlotTitle.Size,    FontStyleBold, UnitPixel, out _fontTitleFurore);
        GdipCreateFont(_furoreFamily, SlotB10Title.Size, FontStyleBold, UnitPixel, out _fontB10TitleFurore);

        GdipCreateFont(_furoreFamily, SlotInfoName.Size,   FontStyleBold, UnitPixel, out _fontInfoName);
        GdipCreateFont(_furoreFamily, SlotInfoRating.Size, FontStyleBold, UnitPixel, out _fontInfoRating);
        GdipCreateFont(_furoreFamily, SlotInfoExact.Size,  FontStyleBold, UnitPixel, out _fontInfoExact);

        GdipCreateFont(_furoreFamily, SlotB10Rank.Size,  FontStyleBold, UnitPixel, out _fontB10Rank);
        GdipCreateFont(_segoeFamily,  SlotB10Title.Size, FontStyleBold, UnitPixel, out _fontB10Title);
        GdipCreateFont(_furoreFamily, SlotB10Count.Size, FontStyleBold, UnitPixel, out _fontB10Count);
        GdipCreateFont(_furoreFamily, SlotB10Rate.Size,  FontStyleBold, UnitPixel, out _fontB10Rate);

        GdipCreateFont(_segoeFamily,  SlotB30RowTitle.Size,  FontStyleBold,    UnitPixel, out _fontB30Title);
        GdipCreateFont(_furoreFamily, SlotB30RowTitle.Size,  FontStyleBold,    UnitPixel, out _fontB30TitleFurore);
        GdipCreateFont(_segoeFamily,  SlotB30RowArtist.Size, FontStyleRegular, UnitPixel, out _fontB30Artist);

        GdipCreateSolidFill(unchecked((int)0xFFFFFFFF), out _brWhite);

        GdipCreateStringFormat(0, 0, out _fmtLeft);
        GdipSetStringFormatAlign(_fmtLeft, StringAlignmentNear);
        GdipSetStringFormatLineAlign(_fmtLeft, StringAlignmentLineCenter);

        GdipCreateStringFormat(0, 0, out _fmtMid);
        GdipSetStringFormatAlign(_fmtMid, StringAlignmentCenter);
        GdipSetStringFormatLineAlign(_fmtMid, StringAlignmentLineCenter);

        GdipCreateStringFormat(0, 0, out _fmtRight);
        GdipSetStringFormatAlign(_fmtRight, StringAlignmentFar);
        GdipSetStringFormatLineAlign(_fmtRight, StringAlignmentLineCenter);

        // 玩家名专用：单行不换行，超出裁剪
        GdipCreateStringFormat(StringFormatFlagsNoWrap, 0, out _fmtNameNowrap);
        GdipSetStringFormatAlign(_fmtNameNowrap, StringAlignmentNear);
        GdipSetStringFormatLineAlign(_fmtNameNowrap, StringAlignmentLineCenter);

        return true;
    }

    static void FreeFonts()
    {
        if (_fontRank      != IntPtr.Zero) GdipDeleteFont(_fontRank);
        if (_fontRankSmall != IntPtr.Zero) GdipDeleteFont(_fontRankSmall);
        if (_fontDiff   != IntPtr.Zero) GdipDeleteFont(_fontDiff);
        if (_fontScore  != IntPtr.Zero) GdipDeleteFont(_fontScore);
        if (_fontDive   != IntPtr.Zero) GdipDeleteFont(_fontDive);
        if (_fontExact  != IntPtr.Zero) GdipDeleteFont(_fontExact);
        if (_fontTitle  != IntPtr.Zero) GdipDeleteFont(_fontTitle);
        if (_fontArtist != IntPtr.Zero) GdipDeleteFont(_fontArtist);
        if (_fontTitleFurore    != IntPtr.Zero) GdipDeleteFont(_fontTitleFurore);
        if (_fontB10TitleFurore != IntPtr.Zero) GdipDeleteFont(_fontB10TitleFurore);
        if (_fontInfoName   != IntPtr.Zero) GdipDeleteFont(_fontInfoName);
        if (_fontInfoRating != IntPtr.Zero) GdipDeleteFont(_fontInfoRating);
        if (_fontInfoExact  != IntPtr.Zero) GdipDeleteFont(_fontInfoExact);
        if (_fontB10Rank  != IntPtr.Zero) GdipDeleteFont(_fontB10Rank);
        if (_fontB10Title != IntPtr.Zero) GdipDeleteFont(_fontB10Title);
        if (_fontB10Count != IntPtr.Zero) GdipDeleteFont(_fontB10Count);
        if (_fontB10Rate  != IntPtr.Zero) GdipDeleteFont(_fontB10Rate);
        if (_fontB30Title       != IntPtr.Zero) GdipDeleteFont(_fontB30Title);
        if (_fontB30TitleFurore != IntPtr.Zero) GdipDeleteFont(_fontB30TitleFurore);
        if (_fontB30Artist      != IntPtr.Zero) GdipDeleteFont(_fontB30Artist);

        if (_brWhite != IntPtr.Zero) GdipDeleteBrush(_brWhite);
        if (_fmtLeft       != IntPtr.Zero) GdipDeleteStringFormat(_fmtLeft);
        if (_fmtMid        != IntPtr.Zero) GdipDeleteStringFormat(_fmtMid);
        if (_fmtRight      != IntPtr.Zero) GdipDeleteStringFormat(_fmtRight);
        if (_fmtNameNowrap != IntPtr.Zero) GdipDeleteStringFormat(_fmtNameNowrap);

        if (_fontCollection != IntPtr.Zero) GdipDeletePrivateFontCollection(ref _fontCollection);
    }

    // ---- 图片缓存 ----
    static IntPtr LoadImageCached(string path)
    {
        if (string.IsNullOrEmpty(path)) return IntPtr.Zero;
        if (_imageCache.TryGetValue(path, out var cached)) return cached;

        if (GdipCreateBitmapFromFile(path, out IntPtr bmp) != 0 || bmp == IntPtr.Zero)
            return IntPtr.Zero;

        _imageCache[path] = bmp;
        return bmp;
    }

    static void DisposeImageCache()
    {
        foreach (var kv in _imageCache)
            if (kv.Value != IntPtr.Zero) GdipDisposeImage(kv.Value);
        _imageCache.Clear();
    }

    // ---- 图层文件查找 ----
    // 优先级：{SongId}_{Difficulty} > {SongId} > {Difficulty}
    static string FindLayerFile(string subDir, string songId, string difficulty)
    {
        if (string.IsNullOrEmpty(subDir)) return null;
        string dir = Path.Combine(CardsDir, subDir);
        if (!Directory.Exists(dir)) return null;

        var exts = new[] { ".png", ".jpg", ".jpeg" };
        var names = new[] { $"{songId}_{difficulty}", songId, difficulty };

        foreach (var n in names)
            foreach (var e in exts)
            {
                string p = Path.Combine(dir, n + e);
                if (File.Exists(p)) return p;
            }
        return null;
    }

    static string FindJacketFile(string songId)
    {
        if (!Directory.Exists(JacketDir)) return null;
        foreach (var ext in new[] { ".png", ".jpg", ".jpeg" })
        {
            string p = Path.Combine(JacketDir, songId + ext);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    // 分数分档：0 = <90M，1 = <95M，2 = <98M，3 = <99M，4 = ≥99M
    static string ScoreTier(ulong score)
    {
        if (score < 90_000_000) return "0";
        if (score < 95_000_000) return "1";
        if (score < 98_000_000) return "2";
        if (score < 99_000_000) return "3";
        return "4";
    }

    // score 层专用查找
    // 1. 分数分档 {tier}.png（如 4.png）
    // 2. 谱面专属 {SongId}_{Difficulty}.png（如 2_Forbidden.png）
    // 注意：不再有 {SongId}.png fallback，避免和 tier 数字冲突
    static string FindScoreLayerFile(string songId, string difficulty, ulong score)
    {
        string dir = Path.Combine(CardsDir, "score");
        if (!Directory.Exists(dir)) return null;

        var exts = new[] { ".png", ".jpg", ".jpeg" };

        // 1. tier
        string tier = ScoreTier(score);
        foreach (var e in exts)
        {
            string p = Path.Combine(dir, tier + e);
            if (File.Exists(p)) return p;
        }

        // 2. 谱面专属
        foreach (var e in exts)
        {
            string p = Path.Combine(dir, $"{songId}_{difficulty}" + e);
            if (File.Exists(p)) return p;
        }

        return null;
    }

    // 底图/装饰按 rank 分档：r1 → 1.png，r2~6 → 2.png，其余 → 3.png
    static string RankTier(int rank)
    {
        if (rank <= 1) return "1";
        if (rank <= 6) return "2";
        return "3";
    }

    static string FindRankLayerFile(string subDir, int rank)
    {
        string dir = Path.Combine(CardsDir, subDir);
        if (!Directory.Exists(dir)) return null;

        foreach (var e in new[] { ".png", ".jpg", ".jpeg" })
        {
            string p = Path.Combine(dir, RankTier(rank) + e);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    // B10 行背景：b30_cards/exact/ 下按难度找图（Minimal.png 等）
    static string FindExactLayerFile(string difficulty)
    {
        string dir = Path.Combine(CardsDir, "exact");
        if (!Directory.Exists(dir)) return null;

        foreach (var e in new[] { ".png", ".jpg", ".jpeg" })
        {
            string p = Path.Combine(dir, difficulty + e);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    // FC / PM 层专用查找（PM 优先，找不到 pm 图就用 fc 图）
    static string FindFcPmLayerFile(bool isFc, bool isPm)
    {
        string dir = Path.Combine(CardsDir, "fcpm");
        if (!Directory.Exists(dir)) return null;

        string[] names;
        if (isPm)      names = new[] { "pm", "fc" };
        else if (isFc) names = new[] { "fc" };
        else           return null;

        foreach (var n in names)
            foreach (var e in new[] { ".png", ".jpg", ".jpeg" })
            {
                string p = Path.Combine(dir, n + e);
                if (File.Exists(p)) return p;
            }
        return null;
    }

    // ---- 难度缩写 ----
    static string DifficultyAbbr(string d) => d switch
    {
        "Minimal"   => "MIN",
        "Evolved"   => "EVO",
        "Ultimate"  => "ULT",
        "Forbidden" => "FBD",
        _           => d.ToUpperInvariant(),
    };

    // ---- 主入口 ----
    public static bool Export(string filePath, string playerName, double rating, int totalExacts, List<ChartRating> charts, List<ExactChart> exactCharts)
    {

        var input = new GdiplusStartupInput { GdiplusVersion = 1 };
        if (GdiplusStartup(out var token, ref input, IntPtr.Zero) != 0) return false;

        IntPtr bigBitmap = IntPtr.Zero;
        IntPtr bigGfx    = IntPtr.Zero;
        IntPtr brBg      = IntPtr.Zero;

        try
        {
            if (!InitFonts()) return false;

            GdipCreateBitmapFromScan0(BIG_W, BIG_H, 0, PixelFormat32bppARGB, IntPtr.Zero, out bigBitmap);
            GdipGetImageGraphicsContext(bigBitmap, out bigGfx);
            GdipSetTextRenderingHint(bigGfx, TextRenderingHintAntiAlias);

            // 大图底色
            IntPtr bigBg = IntPtr.Zero;
            foreach (var p in BigBgCandidates)
                if (File.Exists(p)) { bigBg = LoadImageCached(p); break; }

            if (bigBg != IntPtr.Zero)
            {
                GdipDrawImageRectI(bigGfx, bigBg, 0, 0, BIG_W, BIG_H);
            }
            else
            {
                var rect = new GPRECTF { X = 0, Y = 0, Width = BIG_W, Height = BIG_H };
                GdipCreateLineBrushFromRect(ref rect, C_BG_TOP, C_BG_BOTTOM,
                    LinearGradientModeVertical, WrapModeTile, out brBg);
                GdipFillRectangleI(bigGfx, brBg, 0, 0, BIG_W, BIG_H);
            }

            // 左侧玩家信息面板
            DrawInfoPanel(bigGfx, playerName, rating, totalExacts, SlotInfoName, SlotInfoRating, SlotInfoExact);

            // 逐卡绘制（右侧卡片区）
            int n = Math.Min(COLS * ROWS, charts.Count);
            for (int i = 0; i < n; i++)
            {
                int col = i % COLS;
                int row = i / COLS;
                int cx = CARDS_ORIGIN_X + col * (CARD_W + GAP);
                int cy = OUTER + row * (CARD_H + GAP);

                try { DrawCard(bigGfx, charts[i], i + 1, cx, cy); }
                catch (Exception ex) { Plugin.Logger.LogWarning($"[B30Card] 第 {i + 1} 张绘制失败: {ex.Message}"); }
            }

            // 左下方 EXACTIFICATION B10
            DrawExactB10(bigGfx, exactCharts);

            if (!TryGetPngClsid()) return false;
            return GdipSaveImageToFile(bigBitmap, filePath, ref _pngClsid, IntPtr.Zero) == 0;
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[B30Card] Export: {ex.Message}");
            return false;
        }
        finally
        {
            if (brBg != IntPtr.Zero) GdipDeleteBrush(brBg);
            if (bigGfx != IntPtr.Zero) GdipDeleteGraphics(bigGfx);
            if (bigBitmap != IntPtr.Zero) GdipDisposeImage(bigBitmap);

            FreeFonts();
            DisposeImageCache();
            GdiplusShutdown(token);
        }
    }

    // jacket 文件夹里有没有可用曲绘
    public static bool HasJacketFolder()
    {
        if (!Directory.Exists(JacketDir)) return false;
        try
        {
            return Directory.EnumerateFiles(JacketDir)
                .Any(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                       || f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                       || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    // 无曲绘版背景候选（按顺序找第一个存在的）
    static string FindNoJacketBg()
    {
        foreach (var p in new[]
        {
            Path.Combine(CardsDir, "exact", "b30bg.png"),
            Path.Combine(CardsDir, "b30bg.png"),
            Path.Combine(JacketDir, "background_nojacket.png"),
        })
        {
            if (File.Exists(p)) return p;
        }
        return null;
    }

    // ---- 无曲绘 B30：背景 + 玩家信息 + 30 行简化卡片 ----
    // 每行：难度背景 + 定数 / 曲名 / 曲师 + 单曲 rating / ext
    public static bool ExportNoJacket(string filePath, string playerName, double rating, int totalExacts, List<ChartRating> charts)
    {
        var input = new GdiplusStartupInput { GdiplusVersion = 1 };
        if (GdiplusStartup(out var token, ref input, IntPtr.Zero) != 0) return false;

        IntPtr bitmap = IntPtr.Zero;
        IntPtr gfx    = IntPtr.Zero;
        IntPtr brBg   = IntPtr.Zero;

        try
        {
            if (!InitFonts()) return false;

            int n = Math.Min(30, charts?.Count ?? 0);
            if (n == 0) return false;

            const int NJ_ROWS_Y = 523;   // B30 行起点（背景设计值）
            const int BOTTOM = 330;      // 底部留白（放计算公式，仅无背景时用）

            // 背景：优先 b30bg.png，用其原始尺寸作为整图尺寸（不拉伸）
            IntPtr bg = LoadImageCached(FindNoJacketBg());
            int bigW = CARDS_ORIGIN_X;   // 770
            int bigH = NJ_ROWS_Y + n * (B10_ROW_H + B10_GAP) - B10_GAP + BOTTOM;
            if (bg != IntPtr.Zero)
            {
                GdipGetImageWidth(bg, out uint bgW);
                GdipGetImageHeight(bg, out uint bgH);
                bigW = (int)bgW;
                bigH = (int)bgH;
            }

            GdipCreateBitmapFromScan0(bigW, bigH, 0, PixelFormat32bppARGB, IntPtr.Zero, out bitmap);
            GdipGetImageGraphicsContext(bitmap, out gfx);
            GdipSetTextRenderingHint(gfx, TextRenderingHintAntiAlias);

            if (bg != IntPtr.Zero)
            {
                GdipDrawImageRectI(gfx, bg, 0, 0, bigW, bigH);
            }
            else
            {
                var rectBg = new GPRECTF { X = 0, Y = 0, Width = bigW, Height = bigH };
                GdipCreateLineBrushFromRect(ref rectBg, C_BG_TOP, C_BG_BOTTOM,
                    LinearGradientModeVertical, WrapModeTile, out brBg);
                GdipFillRectangleI(gfx, brBg, 0, 0, bigW, bigH);
            }

            // 顶部玩家信息
            DrawInfoPanel(gfx, playerName, rating, totalExacts, SlotNJName, SlotNJRating, SlotNJExact);

            for (int i = 0; i < n; i++)
            {
                int rowY = NJ_ROWS_Y + i * (B10_ROW_H + B10_GAP);
                DrawB30Row(gfx, charts[i], i + 1, rowY);
            }

            if (!TryGetPngClsid()) return false;
            return GdipSaveImageToFile(bitmap, filePath, ref _pngClsid, IntPtr.Zero) == 0;
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[B30Card] ExportNoJacket: {ex.Message}");
            return false;
        }
        finally
        {
            if (brBg != IntPtr.Zero) GdipDeleteBrush(brBg);
            if (gfx != IntPtr.Zero) GdipDeleteGraphics(gfx);
            if (bitmap != IntPtr.Zero) GdipDisposeImage(bitmap);

            FreeFonts();
            DisposeImageCache();
            GdiplusShutdown(token);
        }
    }

    // 简化行：difficulty 背景 + rank/曲名/value1/value2（B10 与无曲绘 B30 共用）
    static void DrawSimpleRow(IntPtr g, int rank, string title, string songId, string difficulty,
        string value1, string value2, int rowY)
    {
        IntPtr layer = LoadImageCached(FindExactLayerFile(difficulty));
        if (layer != IntPtr.Zero)
            GdipDrawImageRectI(g, layer, B10_CARD_X, rowY + B10_CARD_OFFY, B10_CARD_W, B10_CARD_H);

        int diffColor = GetB10DiffColor(difficulty);

        DrawSlot(g, _fontB10Rank, SlotB10Rank, $"#{rank}", B10_CARD_X, rowY + B10_CARD_OFFY, diffColor);
        DrawSlot(g, _fontB10Count, SlotB10Count, value1, B10_CARD_X, rowY + B10_CARD_OFFY);
        DrawSlot(g, _fontB10Rate, SlotB10Rate, value2, B10_CARD_X, rowY + B10_CARD_OFFY);

        // 标题：单行不换行，按难度着色
        float h = SlotB10Title.Size * 2.5f;
        float rectY = SlotB10Title.Y + TEXT_OFFSET_Y - h / 2f;
        var rect = new GPRECTF
        {
            X = B10_CARD_X + SlotB10Title.X, Y = rowY + B10_CARD_OFFY + rectY,
            Width = 290f, Height = h,
        };
        GdipCreateSolidFill(diffColor, out IntPtr titleBrush);
        IntPtr titleFont = TitleUseFurore(songId) ? _fontB10TitleFurore : _fontB10Title;
        GdipDrawString(g, title, -1, titleFont, ref rect, _fmtNameNowrap, titleBrush);
        GdipDeleteBrush(titleBrush);
    }

    // 无曲绘 B30 行：排名 + difficulty 背景 + 定数/曲名/曲师 + 单曲 rating/ext
    static void DrawB30Row(IntPtr g, ChartRating c, int rank, int rowY)
    {
        IntPtr layer = LoadImageCached(FindExactLayerFile(c.Difficulty));
        if (layer != IntPtr.Zero)
            GdipDrawImageRectI(g, layer, NJ_CARD_X, rowY + B10_CARD_OFFY, B10_CARD_W, B10_CARD_H);

        int diffColor = GetB10DiffColor(c.Difficulty);
        string title  = SongInfoCache.GetName(c.SongId);
        string artist = SongInfoCache.GetArtist(c.SongId);

        // 排名：卡片左侧
        DrawSlot(g, _fontB10Rank, SlotNJRank, $"#{rank}", 0, rowY);

        DrawSlot(g, _fontB10Rank,  SlotB10Rank,  c.Constant.ToString(), NJ_CARD_X, rowY + B10_CARD_OFFY, diffColor);
        DrawSlot(g, _fontB10Count, SlotB10Count, c.Rating.ToString("F2"),    NJ_CARD_X, rowY + B10_CARD_OFFY);
        DrawSlot(g, _fontB10Rate,  SlotB10Rate,  c.ExactRate.ToString("F0"), NJ_CARD_X, rowY + B10_CARD_OFFY);

        // 曲名：单行不换行，按难度着色，按像素宽度自动缩小
        IntPtr titleFont = TitleUseFurore(c.SongId)
            ? FitFontToWidth(g, _furoreFamily, FontStyleBold, SlotB30RowTitle.Size, title, 270, _fontB30TitleFurore)
            : FitFontToWidth(g, _segoeFamily,  FontStyleBold, SlotB30RowTitle.Size, title, 270, _fontB30Title);

        // 实测曲名+曲师高度，让两者整体垂直居中于卡片中心
        var measure = new GPRECTF { X = 0, Y = 0, Width = 10000f, Height = 10000f };
        GdipMeasureString(g, title, -1, titleFont, ref measure, IntPtr.Zero, out var titleBox, out _, out _);
        float titleH = titleBox.Height;

        float artistH = 0f;
        if (!string.IsNullOrEmpty(artist))
        {
            GdipMeasureString(g, artist, -1, _fontB30Artist, ref measure, IntPtr.Zero, out var artistBox, out _, out _);
            artistH = artistBox.Height;
        }

        // 负值：让两个文本框重叠一部分，抵消行高内边距
        const float ROW_TEXT_GAP = -11f;
        float blockH = titleH + (artistH > 0 ? ROW_TEXT_GAP + artistH : 0);
        float blockTop = B10_CARD_OFFY + B10_CARD_H / 2f - blockH / 2f;

        GdipCreateSolidFill(diffColor, out IntPtr titleBrush);
        var titleRect = new GPRECTF
        {
            X = NJ_CARD_X + SlotB30RowTitle.X,
            Y = rowY + blockTop,
            Width = 270f, Height = titleH,
        };
        GdipDrawString(g, title, -1, titleFont, ref titleRect, _fmtNameNowrap, titleBrush);
        GdipDeleteBrush(titleBrush);
        if (titleFont != _fontB30Title && titleFont != _fontB30TitleFurore) GdipDeleteFont(titleFont);

        // 曲师
        if (artistH > 0)
        {
            GdipCreateSolidFill(SlotB30RowArtist.Color, out IntPtr artistBrush);
            var artistRect = new GPRECTF
            {
                X = NJ_CARD_X + SlotB30RowArtist.X,
                Y = rowY + blockTop + titleH + ROW_TEXT_GAP,
                Width = 300f, Height = artistH,
            };
            GdipDrawString(g, artist, -1, _fontB30Artist, ref artistRect, _fmtNameNowrap, artistBrush);
            GdipDeleteBrush(artistBrush);
        }
    }

    // ---- 左下方 EXACTIFICATION B10 ----
    static void DrawExactB10(IntPtr g, List<ExactChart> charts)
    {
        int n = Math.Min(B10_COUNT, charts?.Count ?? 0);
        for (int i = 0; i < n; i++)
        {
            var c = charts[i];
            int rowY = B10_Y + i * (B10_ROW_H + B10_GAP);
            DrawSimpleRow(g, i + 1, SongInfoCache.GetName(c.SongId), c.SongId, c.Difficulty,
                c.ExactCount.ToString(), c.Rate.ToString("F1"), rowY);
        }
    }

    // ---- 左侧玩家信息面板 ----
    static void DrawInfoPanel(IntPtr g, string playerName, double rating, int totalExacts,
        TextSlot slotName, TextSlot slotRating, TextSlot slotExact)
    {
        DrawInfoName(g, playerName, slotName);
        DrawSlot(g, _fontInfoRating, slotRating, rating.ToString("F2"), 0, 0);
        DrawSlot(g, _fontInfoExact,  slotExact,  totalExacts.ToString(), 0, 0);
    }

    // 玩家名：单行不换行，从 slot.X 起画，超出面板右边界直接截断
    static void DrawInfoName(IntPtr g, string text, TextSlot slot)
    {
        if (string.IsNullOrEmpty(text) || _fontInfoName == IntPtr.Zero) return;

        float h = slot.Size * 2.5f;
        float rectY = slot.Y + TEXT_OFFSET_Y - h / 2f;

        int clipX = (int)slot.X;
        int clipW = INFO_W - clipX - 20;

        GdipSetClipRectI(g, clipX, (int)rectY, clipW, (int)h, 0);
        var rect = new GPRECTF { X = clipX, Y = rectY, Width = clipW, Height = h };
        GdipDrawString(g, text, -1, _fontInfoName, ref rect, _fmtNameNowrap, _brWhite);
        GdipResetClip(g);
    }

    // 曲目 54 曲名含特殊字符（▓），Segoe 显示不了，改走 Furore
    static bool TitleUseFurore(string songId) => songId == "54";

    // 按像素宽度缩小字号直到放进 maxWidth（无下限，保证不溢出）
    static IntPtr FitFontToWidth(IntPtr g, IntPtr family, int style, float baseSize, string text, int maxWidth, IntPtr baseFont)
    {
        var layout = new GPRECTF { X = 0, Y = 0, Width = 10000f, Height = 10000f };

        GdipMeasureString(g, text, -1, baseFont, ref layout, IntPtr.Zero, out var box, out _, out _);
        if (box.Width <= maxWidth) return baseFont;

        float size = baseSize * maxWidth / box.Width;

        GdipCreateFont(family, size, style, UnitPixel, out IntPtr font);
        return font;
    }

    // 文字过长时按比例缩小字号（不超过槽位默认字号，无下限）
    static IntPtr GetFitFont(IntPtr family, int style, float baseSize, string text, int maxChars, IntPtr baseFont)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars) return baseFont;

        float size = baseSize * maxChars / text.Length;

        GdipCreateFont(family, size, style, UnitPixel, out IntPtr font);
        return font;
    }

    // ---- 单卡绘制 ----
    static void DrawCard(IntPtr g, ChartRating c, int rank, int cardX, int cardY)
    {
        // 1. 图层叠加
        foreach (var (subDir, isJacket) in LayerOrder)
        {
            string path;
            if (isJacket)
                path = FindJacketFile(SongInfoCache.GetBaseName(c.SongId));
            else if (subDir == "score")
                path = FindScoreLayerFile(c.SongId, c.Difficulty, c.Score);
            else if (subDir == "fcpm")
                path = FindFcPmLayerFile(c.IsFC, c.IsPM);
            else if (subDir == "background" || subDir == "decor")
                path = FindRankLayerFile(subDir, rank);
            else
                path = FindLayerFile(subDir, c.SongId, c.Difficulty);

            if (path == null) continue;
            IntPtr bmp = LoadImageCached(path);
            if (bmp == IntPtr.Zero) continue;

            if (isJacket)
            {
                // 曲绘：卡片局部 (10,10) 处，100×100
                GdipDrawImageRectI(g, bmp,
                    cardX + JACKET_X, cardY + JACKET_Y,
                    JACKET_W, JACKET_H);
            }
            else
            {
                // 其他层：铺满整卡
                GdipDrawImageRectI(g, bmp, cardX, cardY, CARD_W, CARD_H);
            }
        }

        // 2. 文字
        string artist    = SongInfoCache.GetArtist(c.SongId);
        string diffText  = $"{DifficultyAbbr(c.Difficulty)} {c.Constant}";
        string scoreText = c.Score.ToString("N0").Replace(",", "'");
        string diveText  = c.PaceValue >= 0 ? $"+{c.PaceValue}" : c.PaceValue.ToString();
        string exactText = c.ExactRate.ToString("F0");
        string titleText = SongInfoCache.GetName(c.SongId);

        IntPtr rankFont = rank >= 10 ? _fontRankSmall : _fontRank;
        IntPtr titleFont = TitleUseFurore(c.SongId)
            ? GetFitFont(_furoreFamily, FontStyleBold, SlotTitle.Size, titleText, TITLE_MAX_CHARS, _fontTitleFurore)
            : GetFitFont(_segoeFamily,  FontStyleBold, SlotTitle.Size, titleText, TITLE_MAX_CHARS, _fontTitle);
        IntPtr artistFont = GetFitFont(_segoeFamily, FontStyleRegular, SlotArtist.Size, artist,    ARTIST_MAX_CHARS, _fontArtist);

        DrawSlot(g, rankFont, SlotRank, $"#{rank}", cardX, cardY);
        DrawSlot(g, _fontDiff,   SlotDiff,   diffText,   cardX, cardY);
        DrawSlot(g, titleFont,  SlotTitle,  titleText,  cardX, cardY);
        DrawSlot(g, artistFont, SlotArtist, artist,     cardX, cardY);
        DrawSlot(g, _fontScore,  SlotScore,  scoreText,  cardX, cardY);
        DrawSlot(g, _fontDive,   SlotDive,   diveText,   cardX, cardY);
        DrawSlot(g, _fontExact,  SlotExact,  exactText,  cardX, cardY);

        if (titleFont  != _fontTitle && titleFont != _fontTitleFurore) GdipDeleteFont(titleFont);
        if (artistFont != _fontArtist) GdipDeleteFont(artistFont);
    }

    // ---- 单槽文字绘制 ----
    // align=left: X 是左边界；align=mid: X 是水平中心
    // Y 始终是垂直中心
    static void DrawSlot(IntPtr g, IntPtr font, TextSlot slot, string text, int cardX, int cardY, int? colorOverride = null)
    {
        if (string.IsNullOrEmpty(text) || font == IntPtr.Zero) return;

        // 构造一个足够宽的矩形；left 从 X 往右铺，mid 以 X 为中心，right 以 X 为右边界
        float halfW = CARD_W;
        float h     = slot.Size * 2.5f;

        float rectX = slot.Align switch
        {
            0 => slot.X,              // left:  X 是左边界
            1 => slot.X - halfW,      // mid:   X 是水平中心
            _ => slot.X - halfW * 2f, // right: X 是右边界
        };
        float rectY = slot.Y + TEXT_OFFSET_Y - h / 2f;

        var rect = new GPRECTF
        {
            X = cardX + rectX,
            Y = cardY + rectY,
            Width = halfW * 2f,
            Height = h,
        };

        IntPtr fmt = slot.Align switch
        {
            0 => _fmtLeft,
            1 => _fmtMid,
            _ => _fmtRight,
        };

        // 颜色覆盖优先；否则 Color2 != 0 时用垂直渐变（Color → Color2）
        int color = colorOverride ?? slot.Color;
        IntPtr brush;
        if (colorOverride == null && slot.Color2 != 0)
            GdipCreateLineBrushFromRect(ref rect, slot.Color, slot.Color2,
                LinearGradientModeVertical, WrapModeTile, out brush);
        else
            GdipCreateSolidFill(color, out brush);

        GdipDrawString(g, text, -1, font, ref rect, fmt, brush);
        GdipDeleteBrush(brush);
    }
}