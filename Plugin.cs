using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using BepInEx.Logging;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using ifapp.Game.Data;

namespace InFalsusRating;

[BepInPlugin("com.atres.infalsusrating", "In Falsus Rating", "0.2.0")]
public class Plugin : BasePlugin
{
    internal static ManualLogSource Logger;
    public static double PlayerRating = 0;
    public static int TotalExacts = 0;
    public static string PlayerName = "Player";
    public static List<ChartRating> ChartRatings = new List<ChartRating>();
    public static volatile bool IsPlaying = false;
    public static volatile bool FirstRatingReady = false;
    public static volatile bool OverlayManuallyHidden = false;

    // ⭐ Rating 变化量显示
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
        ConstTable.Load();
        LoadPlayerName();
        StartScenePatchLoop();
        StartHotkeyListener();
        StartDelayedOverlay();

        var harmony = new Harmony("com.atres.infalsusrating");
        harmony.PatchAll(typeof(Patch_NH_VNA));
        harmony.PatchAll(typeof(Patch_UIManagerUpdate));
        Logger.LogInfo("[Plugin] Harmony patch 已挂载");
    }

    // ---------- 读取本地名字 ----------
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
                Logger.LogInfo($"[Name] 请编辑该文件改成你的名字，然后重启游戏");
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"[Name] 读取文件失败: {ex.Message}");
        }

        Logger.LogInfo($"[Name] 当前使用: {PlayerName}");
    }

    // ---------- 后台持续尝试 patch 场景 ----------
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

        // ---------- 全局快捷键：F8 切换 overlay 显示 ----------
    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vKey);

    const int VK_F8 = 0x77;

    static void StartHotkeyListener()
    {
        var t = new Thread(() =>
        {
            bool wasDown = false;

            while (true)
            {
                try
                {
                    bool isDown = (GetAsyncKeyState(VK_F8) & 0x8000) != 0;

                    if (isDown && !wasDown)
                    {
                        OverlayManuallyHidden = !OverlayManuallyHidden;
                        Logger.LogInfo($"[Hotkey] F8 → overlay {(OverlayManuallyHidden ? "隐藏" : "显示")}");
                    }
                    wasDown = isDown;
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
}

// ========== GameScene 探测 ==========
public static class GameScenePatcher
{
    private static bool _tried = false;

    public static void EnsurePatched()
    {
        if (_tried) return;
        _tried = true;

        try
        {
            Type t = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = asm.GetType("ifapp.Game.Scenes.GameScene");
                if (t != null) break;
            }
            if (t == null) { Plugin.Logger.LogWarning("[Probe] GameScene 类型找不到"); return; }

            var harmony = new Harmony("com.atres.infalsusrating.gamescene");
            var enter = typeof(GameSceneHooks).GetMethod("OnEnter", BindingFlags.Static | BindingFlags.Public);
            var exit  = typeof(GameSceneHooks).GetMethod("OnExit",  BindingFlags.Static | BindingFlags.Public);

            var onEnable  = t.GetMethod("OnEnable",  BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var onDisable = t.GetMethod("OnDisable", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var awake     = t.GetMethod("Awake",     BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var onDestroy = t.GetMethod("OnDestroy", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (onEnable  != null) { harmony.Patch(onEnable,  postfix: new HarmonyMethod(enter)); Plugin.Logger.LogInfo("[Probe] GameScene.OnEnable 已 patch"); }
            if (awake     != null) { harmony.Patch(awake,     postfix: new HarmonyMethod(enter)); Plugin.Logger.LogInfo("[Probe] GameScene.Awake 已 patch"); }
            if (onDisable != null) { harmony.Patch(onDisable, postfix: new HarmonyMethod(exit));  Plugin.Logger.LogInfo("[Probe] GameScene.OnDisable 已 patch"); }
            if (onDestroy != null) { harmony.Patch(onDestroy, postfix: new HarmonyMethod(exit));  Plugin.Logger.LogInfo("[Probe] GameScene.OnDestroy 已 patch"); }
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[Probe] 补 patch 失败: {ex}");
        }
    }
}

public static class GameSceneHooks
{
    public static void OnEnter() { Plugin.IsPlaying = true;  Plugin.Logger.LogInfo("[Probe] PLAYING (GameScene 激活)"); }
    public static void OnExit()  { Plugin.IsPlaying = false; Plugin.Logger.LogInfo("[Probe] MENU (GameScene 关闭)"); }
}

// ========== 场景活跃追踪 ==========
public static class SceneTracker
{
    public static volatile bool IsInResultsScreen = false;

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

    private static readonly HashSet<string> _allowedForOverlay = new HashSet<string>
    {
        "ifapp.Game.Scenes.HubScene",
        "ifapp.Game.Scenes.SongSelectScene",
        "ifapp.Game.Scenes.ResultsScene",
    };

    private static readonly HashSet<string> _blockingScenes = new HashSet<string>
    {
        "ifapp.Game.Scenes.TitleScene",
        "ifapp.Game.Scenes.GameScene",
        "ifapp.Game.Scenes.StoryScene",
        "ifapp.Game.Scenes.TimelineScene",
        "ifapp.Game.Scenes.CreditsScene",
        "ifapp.Game.Scenes.PackSelectScene",
    };

    private static List<string> _sceneStack = new List<string>();
    private static readonly object _lock = new object();
    private static bool _patched = false;
    private static readonly HashSet<string> _handled = new HashSet<string>();

    public static bool ShouldShowOverlay()
    {
        if (IsInResultsScreen) return true;

        lock (_lock)
        {
            if (_sceneStack.Count == 0) return true;

            for (int i = _sceneStack.Count - 1; i >= 0; i--)
            {
                string s = _sceneStack[i];
                if (_allowedForOverlay.Contains(s)) return true;
                if (_blockingScenes.Contains(s)) return false;
            }
            return true;
        }
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
                        Plugin.Logger.LogInfo($"[Scene] ✅ patch {t.Name}");

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

            Plugin.Logger.LogInfo($"[Scene] ▶ {sceneName}  stack=[{string.Join(" > ", _sceneStack)}]");
        }
    }

    public static void Decrement(string sceneName)
    {
        lock (_lock)
        {
            _sceneStack.Remove(sceneName);
            Plugin.Logger.LogInfo($"[Scene] ◀ {sceneName}  stack=[{string.Join(" > ", _sceneStack)}]");
        }
    }
}

// ========== Scene 生命周期钩子 ==========
public static class SceneHooks
{
    public static void OnSceneEnable(object __instance)
    {
        if (__instance == null) return;
        string name = __instance.GetType().FullName;
        SceneTracker.Increment(name);
    }

    public static void OnSceneDisable(object __instance)
    {
        if (__instance == null) return;
        string name = __instance.GetType().FullName;
        SceneTracker.Decrement(name);
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
    }
}

// ========== Overlay ==========
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

    // ============ 可调项 ============
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
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    static extern int AddFontResourceExW(string lpszFilename, uint fl, IntPtr pdv);
    const uint FR_PRIVATE = 0x10;

    static IntPtr _gdiplusToken = IntPtr.Zero;
    static IntPtr _bgBitmap = IntPtr.Zero;
    static int _bgW = 360;
    static int _bgH = 90;

    static IntPtr _memDC = IntPtr.Zero;
    static IntPtr _dibSection = IntPtr.Zero;
    static IntPtr _dibBits = IntPtr.Zero;
    static IntPtr _dibOldObj = IntPtr.Zero;

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

    static readonly string BG_PATH = System.IO.Path.Combine(
        BepInEx.Paths.ConfigPath, "InFalsusRating", "bg.png");
    static readonly string FONT_PATH = System.IO.Path.Combine(
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

    static void LoadBackgroundImage()
    {
        if (!File.Exists(BG_PATH))
        {
            Plugin.Logger.LogWarning($"[Overlay] 背景图不存在: {BG_PATH}");
            return;
        }

        var input = new GdiplusStartupInput { GdiplusVersion = 1 };
        GdiplusStartup(out _gdiplusToken, ref input, IntPtr.Zero);

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
            int n = AddFontResourceExW(FONT_PATH, FR_PRIVATE, IntPtr.Zero);
            if (n > 0)
            {
                _loadedFontName = "Furore";
                Plugin.Logger.LogInfo($"[Overlay] 自定义字体已加载: {_loadedFontName}");
            }
            else
            {
                Plugin.Logger.LogWarning("[Overlay] AddFontResourceEx 返回 0");
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[Overlay] LoadCustomFont: {ex.Message}");
        }
    }

    static IntPtr FindGameWindow()
    {
        try
        {
            var p = System.Diagnostics.Process.GetCurrentProcess();
            if (p.MainWindowHandle != IntPtr.Zero) return p.MainWindowHandle;
        }
        catch { }
        return FindWindowW(null, "In Falsus");
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
        string familyName = _loadedFontName ?? "Segoe UI";
        int status = GdipCreateFontFamilyFromName(familyName, IntPtr.Zero, out _fontFamily);
        if (status != 0 || _fontFamily == IntPtr.Zero)
        {
            Plugin.Logger.LogWarning($"[Overlay] 字体 '{familyName}' 找不到，用 Segoe UI");
            GdipCreateFontFamilyFromName("Segoe UI", IntPtr.Zero, out _fontFamily);
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

        Plugin.Logger.LogInfo($"[Overlay] 字体大小: Name={nameSize:F1} Main={mainSize:F1} Exact={exactSize:F1} Delta={deltaSize:F1}");
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

        // 首次：直接 snap 到当前值，不做过渡
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

                    // ⭐ 显示状态稳定化：显示需 300ms 确认，隐藏立即生效
                    bool rawShouldShow = SceneTracker.ShouldShowOverlay() && !Plugin.OverlayManuallyHidden;

                    if (rawShouldShow != _stableShow)
                    {
                        if (!rawShouldShow)
                        {
                            // 隐藏：立即
                            _stableShow = false;
                            _pendingShow = false;
                        }
                        else
                        {
                            // 显示：需确认 300ms
                            if (!_pendingShow)
                            {
                                _pendingShow = true;
                                _pendingShowAt = DateTime.Now;
                            }
                            else if ((DateTime.Now - _pendingShowAt).TotalMilliseconds >= SHOW_CONFIRM_MS)
                            {
                                _stableShow = true;
                                _pendingShow = false;
                                Plugin.Logger.LogInfo("[Overlay] 显示确认");
                            }
                        }
                    }
                    else
                    {
                        _pendingShow = false;
                    }

                    bool shouldShow = _stableShow;
                    int destY = shouldShow
                        ? _targetY
                        : _targetY - _bgH - 40;

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

                    // ⭐ 更新所有要渲染的文本
                    _nameText  = Plugin.PlayerName;
                    _mainText  = _displayRating.ToString("F2");
                    _exactText = ((int)Math.Round(_displayExact)).ToString();

                    // ⭐ Delta 显示（8 秒超时清除）
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

                    // ⭐ 只在有变化时重渲染
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
}

// ========== 曲名缓存 ==========
public static class SongInfoCache
{
    private static Dictionary<string, string> _songIdToName = new Dictionary<string, string>();
    private static bool _loaded = false;

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

            for (int i = 0; i < len; i++)
            {
                object songObj = null;
                try { songObj = itemProp.GetValue(allSongsObj, new object[] { i }); }
                catch { continue; }
                if (songObj == null) continue;

                var songType = songObj.GetType();
                var idProp   = songType.GetProperty("Id",       BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var nameProp = songType.GetProperty("BaseName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                string songId = null, baseName = null;
                try { songId = idProp?.GetValue(songObj)?.ToString(); } catch { }
                try { baseName = nameProp?.GetValue(songObj)?.ToString(); } catch { }

                if (!string.IsNullOrEmpty(songId) && !_songIdToName.ContainsKey(songId))
                    _songIdToName[songId] = baseName ?? songId;
            }

            Plugin.Logger.LogInfo($"[Cache] 曲名缓存完成: {_songIdToName.Count} 首");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[Cache] 加载失败: {ex.Message}");
        }
    }

    public static string GetName(string songId)
    {
        return _songIdToName.TryGetValue(songId, out var name) ? name : songId;
    }
}

// ========== 评级计算核心 ==========
public static class RatingEngine
{
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

            var passed = all.Where(r => r.CalculatedPaceValue >= 0).ToList();

            var bestByScore = passed
                .GroupBy(r => (r.SongId.ToString(), r.Difficulty.ToString()))
                .Select(g => g.OrderByDescending(r => r.PlayerScore).First())
                .ToList();

            var maxPaceByChart = passed
                .GroupBy(r => (r.SongId.ToString(), r.Difficulty.ToString()))
                .ToDictionary(g => g.Key, g => g.Max(r => (int)r.CalculatedPaceValue));

            var maxShinyByChart = passed
                .GroupBy(r => (r.SongId.ToString(), r.Difficulty.ToString()))
                .ToDictionary(g => g.Key, g => g.Max(r => CountShinies(r)));

            int totalExacts = maxShinyByChart.Values
                .OrderByDescending(v => v)
                .Take(10)
                .Sum();
            Plugin.TotalExacts = totalExacts;

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

                double baseRating = constant + ScoreBonus(r.PlayerScore);
                double paceBonus = maxPace * 0.01;

                chartRatings.Add(new ChartRating
                {
                    SongId = songId,
                    Difficulty = diff,
                    Score = r.PlayerScore,
                    Constant = constant,
                    PaceValue = maxPace,
                    ExactCount = maxShiny,
                    Rating = baseRating + paceBonus
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
                    $"[Rating] delta: {newRating - oldRating:+0.000;-0.000}  ({oldRating:F3} → {newRating:F3})");
            }

            Plugin.PlayerRating = newRating;
            Plugin.FirstRatingReady = true;

            Plugin.Logger.LogInfo(
                $"[Rating] ({reason}) 谱面 {chartRatings.Count} 个，⭐ 评级: {Plugin.PlayerRating:F3}，EXACT: {Plugin.TotalExacts}");

            DumpB30(chartRatings);
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
}

// ========== Patch ==========
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

public class ChartRating
{
    public string SongId;
    public string Difficulty;
    public ulong Score;
    public int Constant;
    public int PaceValue;
    public int ExactCount;
    public double Rating;
}