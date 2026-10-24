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

public static partial class Win32Overlay
{

    public static class PlayerPanel
    {
        const int PW = 1280, PH = 720;
        const int CONTENT_X = 260, CONTENT_Y = 72, CONTENT_W = 1020, CONTENT_H = 648;
        const int CLOSE_X = 1228, CLOSE_Y = 18, CLOSE_W = 36, CLOSE_H = 36;

        const uint WM_LBUTTONDOWN_P = 0x0201;
        const uint WM_RBUTTONDOWN_P = 0x0204;
        const uint WM_MOUSEWHEEL_P  = 0x020A;
        const int VK_UP = 0x26, VK_DOWN = 0x28, VK_ESCAPE = 0x1B, VK_LBUTTON = 0x01;

        const int VK_CONTROL = 0x11, VK_SHIFT = 0x10, VK_MENU = 0x12;

        static readonly string GuiDir = Plugin.AssetPath("GUI");

        static readonly string CjkFontPath = Plugin.AssetPath("font3.ttf");

        const int NAV_W = 196, NAV_H = 44;

        const int LABEL_TEXT_DY = 3;
        const int PAGE_TEXT_DY  = 4;

        const int ACTIVE_TEXT_ARGB = unchecked((int)0xFF294459);
        const int ARTIST_TEXT_ARGB = unchecked((int)0xFFDFDFDF);

        static readonly string[] TAB_LABELS = { "PROTENTIAL", "EXACTIFICATION", "HISTORY" };
        const int TAB_W = 240, TAB_H = 48, TAB_GAP = 12;
        const int TAB_TEXT_DY = 2;

        const int B30_DX = -189;

        const int TABROW_Y = CONTENT_Y;

        const int TABROW_X = CONTENT_X + (CONTENT_W - (3 * TAB_W + 2 * TAB_GAP)) / 2 + B30_DX;
        const int LIST_X = CONTENT_X, LIST_Y = CONTENT_Y + TAB_H + 20, LIST_W = CONTENT_W;
        const int LIST_H = CONTENT_H - TAB_H - 20;

        const int EXBTN_W = 200, EXBTN_H = 48, EXBTN_GAP = 12;
        const int EXBTN_R = CONTENT_X + CONTENT_W - 24;
        const int EXBTN_B = CONTENT_Y + CONTENT_H - 24;
        static readonly string[] EXBTN_LABELS = { "导出", "导出图片" };

        const int ROW_W = 560, ROW_H = 81, ROW_GAP = 10;
        const int ROW_PITCH = ROW_H + ROW_GAP;
        const float ROW_S = ROW_W / 616f;
        const int ROW_TEXT_DY = 2;
        const int ROW_BLOCK_W = (int)((616 + 70 + 20) * ROW_S);
        const int ROW_BLOCK_X = CONTENT_X + (CONTENT_W - ROW_BLOCK_W) / 2 + B30_DX;
        const int RANK_COL_W = (int)(70 * ROW_S);
        const int ROW_CARD_X = ROW_BLOCK_X + RANK_COL_W + (int)(20 * ROW_S);

        const int ROW_TASK_FRAME_T = 2;
        const int ROW_TASK_FRAME_ARGB = unchecked((int)0xFFA8F0F4);
        const int WHEEL_STEP_ROWS = 3;

        const int B30_RATE_TOP  = 30;
        const int B30_EXACT_TOP = 10;
        const int B30_CUT_ARGB  = unchecked((int)0x99A8F0F4);

        const int FLT_X = 906, FLT_Y = 132, FLT_W = 350, FLT_H = 348;
        const int FLT_FX = 920, FLT_FW = 322;
        const int FLT_PAD = 14;
        const int FLT_ROW_H = 28;
        const int FLT_TITLE_CY = 156;
        const int FLT_RESET_X = 1190, FLT_RESET_Y = 144, FLT_RESET_W = 52, FLT_RESET_H = 24;

        const int FLT_DIFF_Y = 170, FLT_DIFF_SIZE = 40, FLT_DIFF_PITCH = 70;
        const int FLT_DIFF_W = 61, FLT_DIFF_GAP = 4;
        const int FLT_SRCH_X = 920, FLT_SRCH_Y = 216, FLT_SRCH_W = 272, FLT_SRCH_H = 30;
        const int FLT_SRCH_CLR_X = 1196, FLT_SRCH_CLR_W = 46;

        const int FLT_RANGE_Y0 = 264;
        const int FLT_RANGE_PITCH = 36;
        const int FLT_LBL_W = 40;
        const int FLT_TRACK_X = 970, FLT_TRACK_W = 262;
        const int FLT_TRACK_H = 5;
        const int FLT_HANDLE_W = 10, FLT_HANDLE_H = 18;
        const int FLT_SLIDE_UP = 13, FLT_SLIDE_DN = 23;
        const int FLT_VAL_DY = 15;
        const int FLT_SORT_Y = 364;
        const int FLT_SORT_KEY_X = 964, FLT_SORT_KEY_W = 150;
        const int FLT_SORT_DIR_X = 1118, FLT_SORT_DIR_W = 124;
        const int FLT_SUM_CY = 408;
        const int FLT_LEGEND_CY = 430, FLT_LEGEND2_CY = 452;
        const int FLT_CHIP_RESET = 5, FLT_CHIP_SRCH = 6, FLT_CHIP_CLR = 7;
        const int FLT_CHIP_SCORE_LO = 8, FLT_CHIP_PTT_LO = 10, FLT_CHIP_EXT_LO = 12;
        const int FLT_CHIP_SORT = 14, FLT_CHIP_DIR = 15;
        const int FLT_SCORE_MAX = 100000000;
        const int FLT_PTT_MAX = 2000;
        const int FLT_EXT_MAX = 2000;

        const int FLT_SCORE_STEP = 500000;
        const int FLT_PTT_STEP = 10;
        const int FLT_EXT_STEP = 10;
        static readonly string[] FLT_SORT_NAMES = { "默认", "评级", "分数", "EXACT", "曲名" };
        static readonly string[] FLT_DIFF_LABELS = { "ALL", "MIN", "EVO", "ULT", "FBD" };
        static readonly string[] FLT_RANGE_LABELS = { "分数", "PTT", "EXT" };

        const int FLT_BG_ARGB         = unchecked((int)0x4D0B1020);
        const int FLT_FIELD_ARGB      = unchecked((int)0x2EFFFFFF);
        const int FLT_OFF_ARGB        = unchecked((int)0x14FFFFFF);
        const int FLT_OFF_HOVER_ARGB  = unchecked((int)0x30FFFFFF);
        const int FLT_ON_ARGB         = unchecked((int)0x99A8F0F4);
        const int FLT_ON_HOVER_ARGB   = unchecked((int)0xB3A8F0F4);
        const int FLT_KNOB_ARGB       = unchecked((int)0xFFA8F0F4);
        const int FLT_KNOB_HOVER_ARGB = unchecked((int)0xFFFFFFFF);
        const int FLT_EDGE_ARGB       = unchecked((int)0x33A8F0F4);
        const int FLT_DIM_ARGB        = unchecked((int)0xFF8A93A6);

        const int DETAIL_W = 556, DETAIL_H = 309;

        const int DETAIL_GAP = -8;

        const int DETAIL_BLOCK = DETAIL_GAP + DETAIL_H + ROW_GAP;

        const int DETAIL_X = ROW_CARD_X + (ROW_W - DETAIL_W) / 2;

        const int DT_SLOT_X = 22, DT_SLOT_Y = 70, DT_SLOT = 64;
        const int DT_SCORE_CX = 211, DT_SCORE_CY = 84, DT_SCORE_MAXW = 230;

        const int DT_COMBO_CX = 384, DT_COMBO_CY = 84, DT_COMBO_MAXW = 88;
        const int DT_CLEAR_CX = 493, DT_CLEAR_CY = 80, DT_CLEAR_MAXW = 85;

        const int DT_CLEAR_X = 448, DT_CLEAR_Y = 67, DT_CLEAR_W = 91, DT_CLEAR_H = 28;

        const int DT_EXACT_CX = 265, DT_EXACT_CY = 123, DT_EXACT_MAXW = 100;
        const int DT_RATE_CX = 493, DT_RATE_CY = 117, DT_RATE_MAXW = 85;
        const int DT_PROT_CX = 149, DT_PROT_CY = 123, DT_PROT_MAXW = 96;
        const int DT_EXP_CX = 378, DT_EXP_CY = 123, DT_EXP_MAXW = 88;

        const int DT_TIME_RIGHT = 546, DT_TIME_CY = 48, DT_TIME_MAXW = 140;
        static readonly int[] DT_TABLE_COLS = { 111, 182, 252, 323, 393 };
        static readonly int[] DT_TABLE_ROWS = { 178, 206, 234, 262, 288 };
        static readonly int[] DT_FL_ROWS = { 206, 234, 262, 288 };
        const int DT_FL_FAST_X = 466, DT_FL_LATE_X = 515, DT_FL_MAXW = 42;

        const int DT_DARK_ARGB = unchecked((int)0xFF23262E);

        const int DT_TIME_ARGB = unchecked((int)0xFF007187);

        const int DT_SHADOW_ARGB = unchecked((int)0x8A101820);

        const int DT_FS_BIG = 22, DT_FS_COMBO = 17, DT_FS_NUM = 19, DT_FS_WORD = 17, DT_FS_FL = 14, DT_FS_GRADE = 26;
        const int DT_FS_TIME = 14;

        const int B30_OPEN_MS = 200;
        static float _b30OpenT = 1f;
        static long _b30OpenAt = 0;

        const float FS_TAB        = 24f;
        const float FS_ROW_TITLE  = 35f * ROW_S;
        const float FS_ROW_ARTIST = 17f * ROW_S;

        const float ROW_LINE_PITCH = 25f * ROW_S;

        const float ROW_INK_GAP = 5f;

        static readonly string[] GRADE_NAMES = { "B", "A", "AA", "EX", "EX+" };

        sealed class NavItem
        {
            public readonly string Key;
            public readonly string Label;
            public readonly string Page;
            public readonly int X, Y;
            public NavItem(string key, string label, string page, int x, int y)
            { Key = key; Label = label; Page = page; X = x; Y = y; }
        }

        static readonly NavItem[] NAV =
        {
            new NavItem("info",      "玩家信息", "page_info",       32, 152),

            new NavItem("b30",       "成绩", "page_b30",        32, 260),
            new NavItem("recommend", "曲目推荐", "page_recommend",  32, 312),
            new NavItem("task",      "课题",     "page_task",       32, 420),
            new NavItem("achieve",   "成就",     "page_achieve",    32, 472),

            new NavItem("config",    "设置",     "page_config",     32, 632),
        };

        static IntPtr _hWnd = IntPtr.Zero;
        static IntPtr _pDC = IntPtr.Zero, _pDib = IntPtr.Zero, _pBits = IntPtr.Zero, _pDibOld = IntPtr.Zero;
        static IntPtr _pToken = IntPtr.Zero;
        static IntPtr _pFontFamily = IntPtr.Zero;
        static IntPtr _pCjkCollection = IntPtr.Zero, _pCjkFamily = IntPtr.Zero;
        static IntPtr _pFontCjkLabel = IntPtr.Zero, _pFontCjkPage = IntPtr.Zero;
        private static Dictionary<float, IntPtr> cjkConstFontDictionary = new();

        static IntPtr _pFontInfoName = IntPtr.Zero, _pFontInfoTitle = IntPtr.Zero;
        static IntPtr _pFontInfoRat = IntPtr.Zero, _pFontInfoVal = IntPtr.Zero;
        static IntPtr _pBrWhite = IntPtr.Zero, _pBrShade = IntPtr.Zero;
        static IntPtr _pBrActiveText = IntPtr.Zero;
        static IntPtr _pBrArtist = IntPtr.Zero;
        static WndProcDelegate _pWndProc;
        static readonly Dictionary<string, IntPtr> _tex = new Dictionary<string, IntPtr>();
        static bool _texLoaded = false;

        static int _screenX, _screenY;
        static volatile bool _visible = false;

        static int _viewW = PW, _viewH = PH;
        static IntPtr _pScaleBmp = IntPtr.Zero, _pScaleGfx = IntPtr.Zero;
        static int _scaleW, _scaleH;

        const uint SWP_NOZORDER_P = 0x0004;

                static int LayoutX(int dx) => _viewW == PW ? dx : (int)Math.Round(dx * (double)PW / _viewW);
                static int LayoutY(int dy) => _viewH == PH ? dy : (int)Math.Round(dy * (double)PH / _viewH);

                static void SyncViewSize()
        {
            float s = Settings.PanelScale;
            if (s < 0.6f) s = 0.6f;
            if (s > 2f) s = 2f;
            int w = (int)Math.Round(PW * s), h = (int)Math.Round(PH * s);
            if (w < 640) w = 640;
            if (h < 360) h = 360;
            _viewW = w; _viewH = h;
        }

                static void ApplyPanelScale()
        {
            int ow = _viewW, oh = _viewH;
            SyncViewSize();
            if (ow == _viewW && oh == _viewH) return;

            if (_pDibOld != IntPtr.Zero && _pDC != IntPtr.Zero) SelectObject(_pDC, _pDibOld);
            if (_pDib != IntPtr.Zero) DeleteObject(_pDib);

            if (_pDC != IntPtr.Zero) DeleteDC(_pDC);
            _pDib = _pDC = _pBits = _pDibOld = IntPtr.Zero;
            SetupDib();

            if (_hWnd != IntPtr.Zero)
                SetWindowPos(_hWnd, IntPtr.Zero, 0, 0, _viewW, _viewH,
                    SWP_NOMOVE | SWP_NOZORDER_P | SWP_NOACTIVATE);
            ComputePos();
            MarkDirty();
        }

                static bool EnsureScaleCanvas()
        {
            if (_pScaleBmp != IntPtr.Zero && _pScaleGfx != IntPtr.Zero
                && _scaleW == _viewW && _scaleH == _viewH) return true;

            if (_pScaleGfx != IntPtr.Zero) { GdipDeleteGraphics(_pScaleGfx); _pScaleGfx = IntPtr.Zero; }
            if (_pScaleBmp != IntPtr.Zero) { GdipDisposeImage(_pScaleBmp); _pScaleBmp = IntPtr.Zero; }
            _scaleW = _scaleH = 0;

            if (GdipCreateBitmapFromScan0(_viewW, _viewH, 0, PixelFormat32bppARGB, IntPtr.Zero, out _pScaleBmp) != 0
                || _pScaleBmp == IntPtr.Zero)
            { _pScaleBmp = IntPtr.Zero; return false; }

            if (GdipGetImageGraphicsContext(_pScaleBmp, out _pScaleGfx) != 0 || _pScaleGfx == IntPtr.Zero)
            {
                GdipDisposeImage(_pScaleBmp);
                _pScaleBmp = IntPtr.Zero;
                _pScaleGfx = IntPtr.Zero;
                return false;
            }

            GdipSetInterpolationMode(_pScaleGfx, _viewW < PW ? 6 : 7);
            _scaleW = _viewW;
            _scaleH = _viewH;
            return true;
        }

                public static bool Visible => _visible;

        static volatile bool _toggleRequested = false;
        static volatile bool _clickPending = false;
        static volatile int _clickX = 0, _clickY = 0;

        static volatile bool _rclickPending = false;
        static volatile int _rclickX = 0, _rclickY = 0;

        const int IDLE_REDRAW_MS = 200;
                static int _renderDirty = 1;
        static int _lastMx = int.MinValue, _lastMy = int.MinValue;
        static bool _lastDown = false;
        static long _lastRenderMs = 0;

        static readonly System.Diagnostics.Stopwatch _frSw = new System.Diagnostics.Stopwatch();
        static long _frN = 0;
        static double _frSum = 0, _frMax = 0;

        const int PANEL_OPEN_MS = 260;
        const int PANEL_CLOSE_MS = 180;
        static long _panelAnimAt = 0;
        static bool _panelAnimClosing = false;
                static double _panelFxP = 1;

        const int PAR_BG_MAX = 5;
        const int PAR_FG_MAX = 2;
        const double PAR_LERP = 0.22;
        static double _parX = 0, _parY = 0;
        static double _parTX = 0, _parTY = 0;

                static IntPtr _pFrameBmp = IntPtr.Zero, _pFrameGfx = IntPtr.Zero;
        static int _activeNav = 0;
        static int _hoverNav = -1;
        static bool _hoverClose = false;

        static bool _infoHoverName = false;
        static int _infoNameRX, _infoNameRY, _infoNameRW, _infoNameRH;

        static bool _infoHoverEdit = false;

        static bool _infoHoverDiff = false;
        static int _infoDiffPick = 0;

        static bool _infoAchPct = false;

        static int _infoAchRX, _infoAchRY, _infoAchRW, _infoAchRH;
        static bool _wasUp, _wasDown, _wasEsc;

        static int _activeTab = 0, _hoverTab = -1;
        static float _scroll = 0f, _scrollMax = 0f;
        static bool _dragging = false;
        static int _dragStartY = 0, _dragMoved = 0;
        static float _dragStartScroll = 0f;
        static volatile int _wheelAccum = 0;
        static Dictionary<float, IntPtr> rowConstFontDictionary = new();
        static IntPtr _pFontRowNum = IntPtr.Zero;
        static IntPtr _pFontTab = IntPtr.Zero;
        static IntPtr _pBrTabOn = IntPtr.Zero, _pBrTabOff = IntPtr.Zero, _pBrTabHover = IntPtr.Zero;
        static IntPtr _pRowNumFamily = IntPtr.Zero;

        static IntPtr _pRowTitleFam = IntPtr.Zero, _pRowFuroreFam = IntPtr.Zero;

        private static IntPtr _pCjkFamSrc = IntPtr.Zero;
        static IntPtr _pRowFamSrc = IntPtr.Zero;

        static int _hoverExBtn = -1;
        static volatile bool _exBusy = false;
        static volatile bool _exDone = false;
        static DateTime _exDoneAt = DateTime.MinValue;
        static int _exRunningIdx = -1, _exDoneIdx = -1;
        static int _exBtnCount = 0;

        static readonly Dictionary<IntPtr, IntPtr> _famTitleBase  = new Dictionary<IntPtr, IntPtr>();
        static readonly Dictionary<IntPtr, IntPtr> _famArtistBase = new Dictionary<IntPtr, IntPtr>();

        static readonly Dictionary<string, IntPtr> _texAbs = new Dictionary<string, IntPtr>();
        static readonly Dictionary<string, string> _rowBgPath = new Dictionary<string, string>();
        static readonly Dictionary<int, IntPtr> _brCache = new Dictionary<int, IntPtr>();

        static readonly Dictionary<string, float[]> _inkCache = new Dictionary<string, float[]>();
        static readonly Dictionary<string, double> _constByChart = new Dictionary<string, double>();
        static List<ChartRating> _constSource = null;

        static int _b30OpenRow = -1;

        static object _b30OpenSrc;

        static readonly Dictionary<string, IntPtr> _dtGradeTex = new Dictionary<string, IntPtr>();

        static readonly Dictionary<int, IntPtr> _dtFonts = new Dictionary<int, IntPtr>();

        static int _b30DiffMask = 0xF;
        static volatile string _b30Search = "";
        static int _b30ScoreLo = 0, _b30ScoreHi = FLT_SCORE_MAX;
        static int _b30PttLo = 0, _b30PttHi = FLT_PTT_MAX;
        static int _b30ExtLo = 0, _b30ExtHi = FLT_EXT_MAX;
        static int _b30Sort = 0;
        static bool _b30SortDesc = true;
        static int _b30HoverChip = -1;

        static int _fltDragChip = -1;

        static readonly List<int> _b30View = new List<int>();
        static object _b30ViewSrc;
        static int _b30ViewTab = -1;

        static int _b30ViewDiffMask = -1;
        static int _b30ViewScoreLo = -1, _b30ViewScoreHi = -1;
        static int _b30ViewPttLo = -1, _b30ViewPttHi = -1;
        static int _b30ViewExtLo = -1, _b30ViewExtHi = -1;
        static int _b30ViewSort = -1;
        static bool _b30ViewSortDesc;
        static string _b30ViewTerm;
        static int _b30CutView;
        static string _b30SearchBefore;

        static readonly Dictionary<string, IntPtr> _bdSmall = new Dictionary<string, IntPtr>();
        static IntPtr _infoBd = IntPtr.Zero;
                static readonly HashSet<string> _bdAsked = new HashSet<string>();
                static string _bdPend = "";

        static readonly Dictionary<string, IntPtr> _decoTex = new Dictionary<string, IntPtr>();
        static bool _infoBdDirty = true;
        static readonly Random _bdRnd = new Random();
                static string _bdMissName = null;

        public static void Toggle() => _toggleRequested = true;

        public static void Start()
        {
            var t = new Thread(() =>
            {
                try { RunPanel(); }
                catch (Exception ex) { Plugin.Logger.LogError($"[Panel] {ex}"); }
            });
            t.IsBackground = true;
#pragma warning disable CA1416
            t.SetApartmentState(ApartmentState.STA);
#pragma warning restore CA1416
            t.Start();
        }

        static void RunPanel()
        {
            var input = new GdiplusStartupInput { GdiplusVersion = 1 };
            if (GdiplusStartup(out _pToken, ref input, IntPtr.Zero) != 0)
            {
                Plugin.Logger.LogError("[Panel] GdiplusStartup 失败");
                return;
            }
            SyncViewSize();
            SetupDib();

            _pWndProc = WndProcImpl;
            var wc = new WNDCLASS
            {
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_pWndProc),
                lpszClassName = "InFalsusRatingPanel"
            };
            RegisterClassW(ref wc);

            uint exStyle = WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            _hWnd = CreateWindowExW(exStyle, "InFalsusRatingPanel", "Panel", WS_POPUP,
                0, 0, _viewW, _viewH, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (_hWnd == IntPtr.Zero) { Plugin.Logger.LogError("[Panel] CreateWindowEx 失败"); return; }

            var game = FindGameWindow();
            if (game != IntPtr.Zero) SetWindowLongPtrW(_hWnd, GWLP_HWNDPARENT, game);

            Plugin.Logger.LogInfo("[Panel] 玩家面板就绪（F6 开关）");

            var updater = new Thread(() =>
            {
                while (true)
                {
                    try { Tick(); } catch { }
                    Thread.Sleep(ANIM_FRAME_MS);
                }
            });
            updater.IsBackground = true;
            updater.Start();

            MSG msg;
            while (GetMessageW(out msg, IntPtr.Zero, 0, 0) > 0)
            {

                if (msg.message == WM_KEYDOWN && _hNameEdit != IntPtr.Zero && msg.hwnd == _hNameEdit
                    && (msg.wParam == (IntPtr)VK_RETURN || msg.wParam == (IntPtr)VK_ESCAPE)
                    && !ImeComposing(_hNameEdit))
                {
                    EndNameDlg(msg.wParam == (IntPtr)VK_RETURN, false);
                    continue;
                }
                TranslateMessage(ref msg);
                DispatchMessageW(ref msg);
            }
        }

        static void Tick()
        {

            ApplyPanelScale();

            SetActTick();

            if (_toggleRequested)
            {
                _toggleRequested = false;
                SetVisible(!_visible);
            }

            PublishBottom();

            if (!_visible)
            {

                if (_panelAnimClosing && _panelAnimAt != 0)
                {
                    _clickPending = false;
                    _rclickPending = false;
                    _fltDragChip = -1;
                    Interlocked.Exchange(ref _wheelAccum, 0);
                    _frSw.Restart();
                    Render();
                    Commit();
                    _frSw.Stop();
                    double frMs = _frSw.Elapsed.TotalMilliseconds;
                    _frN++;
                    _frSum += frMs;
                    if (frMs > _frMax) _frMax = frMs;

                    if (_panelAnimAt == 0) FinishClose();
                }
                return;
            }

            bool wheelDirty = _wheelAccum != 0;
            bool downNow = Key(VK_LBUTTON);
            bool btnDirty = downNow != _lastDown;

            bool haveMouse = GetCursorPos(out var mp);
            bool curDirty = haveMouse && (mp.x != _lastMx || mp.y != _lastMy);

            int px = _screenX, py = _screenY;
            ComputePos();
            bool posDirty = _screenX != px || _screenY != py;

            if (haveMouse) ParallaxAim(mp.x, mp.y);
            ParallaxSmooth();

            B30OpenAnimTick();

            S0TagCutTick();

            RowWipeTick();

            UpdateHover();
            if (_clickPending) { _clickPending = false; HandleClick(_clickX, _clickY); }

            if (_rclickPending) { _rclickPending = false; HandleRightClick(_rclickX, _rclickY); }
            HandleDrag();
            HandleWheelScroll();

            HotKeyCapTick();
            HandleKeys();

            SkinTick();

            if (wheelDirty || btnDirty || curDirty || posDirty || Animating() || TakeDirty())
            {
                _frSw.Restart();
                Render();
                Commit();
                _frSw.Stop();

                double frMs = _frSw.Elapsed.TotalMilliseconds;
                _frN++;
                _frSum += frMs;
                if (frMs > _frMax) _frMax = frMs;
                _lastRenderMs = Environment.TickCount64;
            }

            _lastDown = downNow;
            _lastMx = mp.x;
            _lastMy = mp.y;
        }

                static void MarkDirty() => Interlocked.Exchange(ref _renderDirty, 1);

                public static void RequestRedraw() => MarkDirty();

                static bool TakeDirty() => Interlocked.Exchange(ref _renderDirty, 0) != 0;

                static bool Animating()
        {

            if (_panelAnimAt != 0) return true;

            if (_b30OpenAt != 0) return true;

            if (_s0TagVis != _s0TagVisTarget || _s0TagCutDirty) return true;

            if (_rowWipes.Count > 0) return true;

            if (_parX != _parTX || _parY != _parTY) return true;
            var now = DateTime.Now;
            if (_hNameDlg != IntPtr.Zero) return true;
            if (_shakeTarget != SHAKE_NONE
                && (now - _shakeAt).TotalMilliseconds < SHAKE_MS) return true;

            if (OnTaskPage && Masked(FinalChallenge())) return true;
            return ThemeAt(THEME_FIN, now) != _thTo[THEME_FIN]
                || ThemeAt(THEME_INS, now) != _thTo[THEME_INS];
        }

        static void SetVisible(bool on)
        {
            if (_hWnd == IntPtr.Zero) return;

            if (!on)
            {

                if (!_visible) return;
                _visible = false;
                _panelAnimClosing = true;
                _panelAnimAt = Environment.TickCount64;
                _clickPending = false;
                _rclickPending = false;
                _rowWipes.Clear();
                _fltDragChip = -1;
                Interlocked.Exchange(ref _wheelAccum, 0);
                CloseNameDlg();

                _hotCap = -1;
                Hotkeys.Capturing = false;
                return;
            }

            _panelAnimClosing = false;
            _panelAnimAt = Environment.TickCount64;
            _visible = true;
            LoadTextures();
            _scroll = 0f;
            _dragging = false;
            _fltDragChip = -1;
            _rclickPending = false;
            _b30OpenRow = -1;
            _b30OpenSrc = null;
            B30OpenAnimReset();
            _frN = 0; _frSum = 0; _frMax = 0;
            Interlocked.Exchange(ref _wheelAccum, 0);

            ApplyPanelScale();

            if (Settings.RememberTab)
            {
                int want = Settings.LastTab;

                if (want >= NAV.Length) want = NAV.Length - 1;
                if (want >= 0 && want < NAV.Length && want != _activeNav)
                {
                    _activeNav = want;
                    _scroll = 0f;
                    _b30OpenRow = -1;
                    _b30OpenSrc = null;
                    B30OpenAnimReset();
                    _dragging = false;
                    _fltDragChip = -1;
                    InfoBdInvalidate();
                }
            }
            ComputePos();

            if (GetCursorPos(out var mp1)) ParallaxAim(mp1.x, mp1.y);
            _parX = _parTX;
            _parY = _parTY;
            Render();
            Commit();

            _lastRenderMs = Environment.TickCount64;
            _lastDown = Key(VK_LBUTTON);
            if (GetCursorPos(out var mp0)) { _lastMx = mp0.x; _lastMy = mp0.y; }
            ShowWindow(_hWnd, SW_SHOWNOACTIVATE);
            Plugin.Logger.LogInfo("[Panel] 显示");
        }

                static void FinishClose()
        {
            if (_hWnd == IntPtr.Zero) return;
            _panelAnimClosing = false;
            _panelAnimAt = 0;
            _panelFxP = 1;
            ShowWindow(_hWnd, 0 );

            CloseSkinSheet();
            DisposeTextures();

            CloseNameDlg();

            if (_frN > 0)
                Plugin.Logger.LogInfo($"[Panel] 这一开的绘制：{_frN} 帧 · 平均 {_frSum / _frN:0.0} ms · 最慢 {_frMax:0.0} ms");

            if (Settings.RememberTab) Settings.Save();
            Plugin.Logger.LogInfo("[Panel] 隐藏");
        }

        static void ComputePos()
        {
            var game = FindGameWindow();
            if (game == IntPtr.Zero || !IsWindow(game)) return;

            var pt = new POINT { x = 0, y = 0 };
            if (!ClientToScreen(game, ref pt)) return;
            RECT rc;
            if (!GetClientRect(game, out rc)) return;

            _screenX = pt.x + (rc.right - rc.left - _viewW) / 2;
            _screenY = pt.y + (rc.bottom - rc.top - _viewH) / 2;
        }

        static bool OnB30Page => NAV[_activeNav].Key == "b30";

                static bool OnInfoPage => NAV[_activeNav].Key == "info";

        static int HitTest(int x, int y)        {
            for (int i = 0; i < NAV.Length; i++)
            {
                var it = NAV[i];
                if (x >= it.X && x < it.X + NAV_W && y >= it.Y && y < it.Y + NAV_H) return i;
            }
            return -1;
        }

                static bool HitTestInfoName(int x, int y)
            => _infoNameRW > 0
               && x >= _infoNameRX && x < _infoNameRX + _infoNameRW
               && y >= _infoNameRY && y < _infoNameRY + _infoNameRH;

                static bool HitTestInfoEdit(int x, int y)
            => x >= INFO_EDIT_X && x < INFO_EDIT_X + INFO_EDIT_W
               && y >= INFO_EDIT_Y && y < INFO_EDIT_Y + INFO_EDIT_H;

                static int InfoDiffBtnX() => INFO_PANEL_X + INFO_PANEL_W - INFO_PANEL_PAD - INFO_DIFF_BTN_W;

                static int InfoDiffBtnY() => INFO_PANEL_TOP;

                static bool HitTestInfoDiff(int x, int y)
            => x >= InfoDiffBtnX() && x < InfoDiffBtnX() + INFO_DIFF_BTN_W
               && y >= InfoDiffBtnY() && y < InfoDiffBtnY() + INFO_DIFF_BTN_H;

                static bool HitTestInfoAch(int x, int y)
            => _infoAchRW > 0
               && x >= _infoAchRX && x < _infoAchRX + _infoAchRW
               && y >= _infoAchRY && y < _infoAchRY + _infoAchRH;

                static string InfoDiffName(int pick)
            => pick <= 0 ? INFO_DIFF_ANY : TaskConfig.DiffShort(TaskConfig.DiffOrder[pick - 1]);

                static IntPtr InfoDiffArt(int idx)
        {
            if (idx < 0 || idx >= TaskConfig.DiffOrder.Length) return IntPtr.Zero;
            string key = TaskConfig.DiffKey(TaskConfig.DiffOrder[idx]);
            return key.Length == 0 ? IntPtr.Zero : Tex("diff/" + key + "_normal.png");
        }

                static IntPtr InfoDiffArtActive(int idx)
        {
            if (idx < 0 || idx >= TaskConfig.DiffOrder.Length) return IntPtr.Zero;
            string key = TaskConfig.DiffKey(TaskConfig.DiffOrder[idx]);
            return key.Length == 0 ? IntPtr.Zero : Tex("diff/" + key + "_active.png");
        }

                static IntPtr InfoDiffAllArt() => Tex("diff/all_normal.png");

                static IntPtr InfoDiffAllArtActive() => Tex("diff/all_active.png");

                static IntPtr InfoDiffBrush(int pick)
            => pick <= 0 ? _pBrWhite : Brush(B30CardExporter.GetB10DiffColor(TaskConfig.DiffOrder[pick - 1]));

                static void DrawInfoDiffBtn(IntPtr gfx, IntPtr brTag, bool withTag = true)
        {
            int bx = InfoDiffBtnX(), by = InfoDiffBtnY();
            int cy = by + INFO_DIFF_BTN_H / 2;
            bool hov = _infoHoverDiff;
            int pick = _infoDiffPick;

            bool drew = false;
            if (pick <= 0)
            {

                IntPtr all = InfoDiffAllArt();
                if (all != IntPtr.Zero)
                {
                    drew = true;
                    if (hov) DrawBoost(gfx, all, INFO_DIFF_HOVER_BOOST, bx, by, INFO_DIFF_BTN_W, INFO_DIFF_BTN_H);
                    else Draw(gfx, all, bx, by, INFO_DIFF_BTN_W, INFO_DIFF_BTN_H);
                }
                else
                {

                    int q = INFO_DIFF_BTN_H / 2;
                    drew = InfoDiffArt(0) != IntPtr.Zero && InfoDiffArt(1) != IntPtr.Zero
                        && InfoDiffArt(2) != IntPtr.Zero && InfoDiffArt(3) != IntPtr.Zero;
                    if (drew)
                    {
                        for (int i = 0; i < 4; i++)
                        {
                            int qx = bx + (i % 2) * q, qy = by + (i / 2) * q;
                            IntPtr a = InfoDiffArt(i);
                            if (hov) DrawBoost(gfx, a, INFO_DIFF_HOVER_BOOST, qx, qy, q, q);
                            else Draw(gfx, a, qx, qy, q, q);
                        }
                    }
                }
            }
            else
            {
                IntPtr art = InfoDiffArt(pick - 1);
                if (art != IntPtr.Zero)
                {
                    drew = true;
                    if (hov) DrawBoost(gfx, art, INFO_DIFF_HOVER_BOOST, bx, by, INFO_DIFF_BTN_W, INFO_DIFF_BTN_H);
                    else Draw(gfx, art, bx, by, INFO_DIFF_BTN_W, INFO_DIFF_BTN_H);
                }
            }

            if (!drew)
            {

                FillRoundRect(gfx, hov ? _pBrTabHover : _pBrTabOff,
                    bx, by, INFO_DIFF_BTN_W, INFO_DIFF_BTN_H, INFO_DIFF_BTN_R);
                RowTextC(gfx, InfoDiffName(pick), _pFontCjkLabel, InfoDiffBrush(pick),
                    bx + INFO_DIFF_BTN_W / 2, cy + LABEL_TEXT_DY);
            }

            if (withTag)
                RowTextR(gfx, INFO_DIFF_TAG, _pFontCjkLabel, brTag, bx - INFO_DIFF_GAP, cy + LABEL_TEXT_DY);
        }

        static void UpdateHover()
        {
            _hoverNav = -1;
            _hoverClose = false;
            _hoverTab = -1;
            _hoverExBtn = -1;
            _b30HoverChip = -1;
            _hoverCat = -1;
            _hoverRow = -1;
            _hoverGoBtn = -1;
            _hoverDiffBtn = -1;
            _achHover = -1;
            _achHoverCat = -1;
            _achHoverTitG = -1;
            _setHoverRow = -1;
            _setHoverChip = -1;
            _setHoverTab = -1;
            _recHover = -1;
            _recHoverRefresh = false;
            _infoHoverName = false;
            _infoHoverEdit = false;
            _infoHoverDiff = false;
            _hotHoverSlot = -1;
            _hotHoverReset = false;
            _sheetHoverTab = -1; _sheetHoverCell = -1; _sheetHoverBtn = -1;
            _sheetHoverClose = false; _sheetHoverBar = false;
            if (!GetCursorPos(out var pt)) return;

            int cx = LayoutX(pt.x - _screenX), cy = LayoutY(pt.y - _screenY);
            if (cx < 0 || cy < 0 || cx >= PW || cy >= PH) return;

            if (cx >= CLOSE_X && cx < CLOSE_X + CLOSE_W && cy >= CLOSE_Y && cy < CLOSE_Y + CLOSE_H)
            {
                _hoverClose = true;
                return;
            }

            if (_sheetOpen) { UpdateSheetHover(cx, cy); return; }

            if (OnB30Page)
            {

                _hoverExBtn = HitTestExBtn(cx, cy);
                if (_hoverExBtn >= 0) return;

                _b30HoverChip = HitTestB30Filter(cx, cy);
                if (_b30HoverChip >= 0) return;
                if (InB30Filter(cx, cy)) return;

                _hoverTab = HitTestTab(cx, cy);
                if (_hoverTab >= 0) return;
            }
            else if (OnTaskPage)
            {

                _hoverCat = HitTestCat(cx, cy);
                if (_hoverCat >= 0) return;

                _hoverGoBtn = HitTestGoBtn(cx, cy);
                if (_hoverGoBtn >= 0) return;

                _hoverDiffBtn = HitTestDiffBtn(cx, cy);
                if (_hoverDiffBtn >= 0) return;

                _hoverRow = HitTestRow(cx, cy);
                if (_hoverRow >= 0) return;
            }
            else if (OnAchievePage)
            {

                _achHoverCat = HitCatAt(cx, cy, AchCats().Count);
                if (_achHoverCat >= 0) return;

                _hoverGoBtn = HitTestGoBtn(cx, cy);
                if (_hoverGoBtn >= 0) return;

                var chipDef = AchSelDef();
                _achHoverTitG = chipDef == null ? -1 : HitTestTitleChip(cx, cy, chipDef);
                if (_achHoverTitG >= 0) return;

                _achHover = HitTestAchRow(cx, cy);
                if (_achHover >= 0) return;
            }
            else if (OnConfigPage)
            {

                _setHoverTab = HitTestSetTab(cx, cy);
                if (_setHoverTab >= 0) return;

                if (SET_TABS[_setTab] == "按键")
                {

                    _hotHoverSlot = HitTestHotKey(cx, cy);
                    if (_hotHoverSlot >= 0) return;
                    _hotHoverReset = HitTestHotReset(cx, cy);

                }

                HitTestSetChip(cx, cy, out _setHoverRow, out _setHoverChip);
                if (_setHoverRow >= 0) return;
            }
            else if (OnRecommendPage)
            {

                _recHover = HitTestRecCard(cx, cy);
                if (_recHover >= 0) return;

                _hoverGoBtn = HitTestGoBtn(cx, cy);
                if (_hoverGoBtn >= 0) return;

                _recHoverRefresh = HitTestRecRefresh(cx, cy);
                if (_recHoverRefresh) return;
            }
            else if (OnInfoPage)
            {

                _infoHoverName = HitTestInfoName(cx, cy);
                if (_infoHoverName) return;

                _infoHoverEdit = HitTestInfoEdit(cx, cy);
                if (_infoHoverEdit) return;

                _infoHoverDiff = HitTestInfoDiff(cx, cy);
                if (_infoHoverDiff) return;
            }
            _hoverNav = HitTest(cx, cy);
        }

        static int HitTestTab(int x, int y)
        {
            if (y < TABROW_Y || y >= TABROW_Y + TAB_H) return -1;
            for (int i = 0; i < TAB_LABELS.Length; i++)
            {
                int tx = TABROW_X + i * (TAB_W + TAB_GAP);
                if (x >= tx && x < tx + TAB_W) return i;
            }
            return -1;
        }

        static bool InList(int x, int y)
            => x >= LIST_X && x < LIST_X + LIST_W && y >= LIST_Y && y < LIST_Y + LIST_H;

        static int ExBtnCount()
        {
            if (_exBtnCount == 0)
                _exBtnCount = B30CardExporter.HasJacketFolder() ? 2 : 1;
            return _exBtnCount;
        }

        static int ExBtnX() => EXBTN_R - EXBTN_W;
        static int ExBtnY(int i) => EXBTN_B - EXBTN_H - i * (EXBTN_H + EXBTN_GAP);

        static int HitTestExBtn(int x, int y)
        {
            if (x < ExBtnX() || x >= ExBtnX() + EXBTN_W) return -1;
            for (int i = 0; i < ExBtnCount(); i++)
            {
                int by = ExBtnY(i);
                if (y >= by && y < by + EXBTN_H) return i;
            }
            return -1;
        }

        static void TriggerExport(int i)
        {
            if (_exBusy) return;
            _exBusy = true;
            _exRunningIdx = i;
            bool withJacket = i == 1;
            var t = new Thread(() =>
            {
                try { Plugin.ExportFromPanel(withJacket); }
                catch (Exception ex) { Plugin.Logger.LogError($"[Panel] 导出失败: {ex.Message}"); }

                finally { _exBusy = false; _exDone = true; MarkDirty(); }
            });
            t.IsBackground = true;
            t.Start();
        }

        const int THEME_NORMAL = 0;
        const int THEME_FIN = 1;
        const int THEME_INS = 2;

        static IntPtr NavPlate(string state, int theme = THEME_NORMAL)
        {
            if (theme != THEME_NORMAL)
            {
                string tag = theme == THEME_INS ? "ins" : "fin";
                IntPtr f = Tex($"nav/sub_{tag}_{state}.png");
                if (f == IntPtr.Zero) f = Tex($"nav/nav_{tag}_{state}.png");
                if (f != IntPtr.Zero) return f;
                if (theme == THEME_INS) return NavPlate(state, THEME_FIN);
            }
            IntPtr p = Tex($"nav/sub_{state}.png");
            if (p == IntPtr.Zero) p = Tex($"nav/nav_{state}.png");
            return p;
        }

        static IntPtr TaskPlate(string kind, string state, int theme = THEME_NORMAL)
        {
            if (theme == THEME_NORMAL)
            {
                IntPtr p = Tex($"task/{kind}_{state}.png");
                if (p != IntPtr.Zero) return p;
            }
            return NavPlate(state, theme);
        }

                static IntPtr ActiveLabelBrush()
            => Math.Max(FinBgMix(), InsBgMix()) >= 0.5f ? _pBrWhite : _pBrActiveText;

                static void FlatTab(IntPtr gfx, int x, int y, int w, int h, int r, bool on, bool hov)
            => FillRoundRect(gfx, on ? _pBrTabOn : (hov ? _pBrTabHover : _pBrTabOff), x, y, w, h, r);

                static IntPtr FlatTabInk(bool on) => on ? _pBrActiveText : _pBrWhite;

                static void DrawNavPlate(IntPtr gfx, IntPtr ins, IntPtr fin, IntPtr normal, int x, int y, int w, int h)
        {
            Draw(gfx, normal, x, y, w, h);
            DrawOver(gfx, fin, normal, FinBgMix(), x, y, w, h);
            DrawOver(gfx, ins, normal, InsBgMix(), x, y, w, h);
        }

                static void DrawOver(IntPtr gfx, IntPtr over, IntPtr under, float alpha, int x, int y, int w, int h)
        {
            if (over == IntPtr.Zero || over == under) return;
            if (alpha <= 0f) return;
            if (GdipGetImageWidth(over, out uint sw) != 0) return;
            if (GdipGetImageHeight(over, out uint sh) != 0) return;

            if (alpha > 1f) alpha = 1f;
            var cm = new ColorMatrix { m00 = 1f, m11 = 1f, m22 = 1f, m33 = alpha, m44 = 1f };
            if (GdipCreateImageAttributes(out var attr) != 0) return;
            GdipSetImageAttributesColorMatrix(attr, 0, true, ref cm, IntPtr.Zero, 0);
            GdipDrawImageRectRectI(gfx, over, x, y, w, h, 0, 0, (int)sw, (int)sh, 2, attr, IntPtr.Zero, IntPtr.Zero);
            GdipDisposeImageAttributes(attr);
        }

                static void DrawBoost(IntPtr gfx, IntPtr img, float boost, int x, int y, int w, int h)
        {
            if (img == IntPtr.Zero || boost <= 0f) return;
            if (GdipGetImageWidth(img, out uint sw) != 0) return;
            if (GdipGetImageHeight(img, out uint sh) != 0) return;

            if (boost > 1f) boost = 1f;
            var cm = new ColorMatrix
            {
                m00 = 1f, m11 = 1f, m22 = 1f, m33 = 1f, m44 = 1f,
                m40 = boost, m41 = boost, m42 = boost
            };
            if (GdipCreateImageAttributes(out var attr) != 0) return;
            GdipSetImageAttributesColorMatrix(attr, 0, true, ref cm, IntPtr.Zero, 0);
            GdipDrawImageRectRectI(gfx, img, x, y, w, h, 0, 0, (int)sw, (int)sh, 2, attr, IntPtr.Zero, IntPtr.Zero);
            GdipDisposeImageAttributes(attr);
        }

                static void DrawSlantWipe(IntPtr gfx, IntPtr img, int x, int y, int w, int h,
            float vis, float deg, bool keepRight)
        {
            if (img == IntPtr.Zero || vis <= 0f) return;
            if (vis > 1f) vis = 1f;

            int half = (int)Math.Round(h * Math.Tan(deg * Math.PI / 180.0) / 2.0);

            double cut = keepRight
                ? (x - half) + (w + half * 2) * (1.0 - vis)
                : (x + w + half) - (w + half * 2) * (1.0 - vis);

            float xt = (float)(cut + half);
            float xb = (float)(cut - half);

            IntPtr path = IntPtr.Zero;
            if (GdipCreatePath(0 , out path) != 0 || path == IntPtr.Zero) return;
            if (keepRight)
            {
                GdipAddPathLine(path, xt, y, x + w, y);
                GdipAddPathLine(path, x + w, y, x + w, y + h);
                GdipAddPathLine(path, x + w, y + h, xb, y + h);
                GdipAddPathLine(path, xb, y + h, xt, y);
            }
            else
            {
                GdipAddPathLine(path, x, y, xt, y);
                GdipAddPathLine(path, xt, y, xb, y + h);
                GdipAddPathLine(path, xb, y + h, x, y + h);

            }
            GdipClosePathFigure(path);

            GdipSetClipPath(gfx, path, 1 );
            Draw(gfx, img, x, y, w, h);
            GdipResetClip(gfx);
            GdipDeletePath(path);
        }

                static void DrawCropRight(IntPtr gfx, IntPtr img, int x, int y, int w, int h, float vis)
            => DrawSlantWipe(gfx, img, x, y, w, h, vis, S0TAG_CUT_DEG, true);

                static bool DrawPlate(IntPtr gfx, string prefix, string st, int x, int y, int w, int h)
        {
            IntPtr p = PlateTex(prefix, st);
            if (p == IntPtr.Zero) return false;

            Draw(gfx, p, x, y, w, h);
            return true;
        }

                static IntPtr PlateTex(string prefix, string st)
        {
            IntPtr p = Tex($"{prefix}_{st}.png");
            if (p == IntPtr.Zero && st != "normal") p = Tex($"{prefix}_normal.png");
            return p;
        }

                const int NAME_DX = 28;

                const int HARD_FROM = 7;

        static int _shakeTarget = SHAKE_NONE;
        static DateTime _shakeAt = DateTime.MinValue;

                const int SHAKE_NONE = -1;
                const int SHAKE_GO = -2;
        const int SHAKE_MS = 420;
        const int SHAKE_AMP = 9;
        const int SHAKE_CYCLES = 4;

                static readonly char[] GARBLE_POOL = BuildGarblePool();

        static char[] BuildGarblePool()
        {
            var pool = new List<char>
                { '▓', '▒', '░', '█', '▚', '▞', '╳', '〓', '※', '＊', '＃', '◇' };
            for (char c = 'Ａ'; c <= 'Ｚ'; c++) pool.Add(c);
            for (char c = 'ａ'; c <= 'ｚ'; c++) pool.Add(c);
            return pool.ToArray();
        }

                static readonly char[] DIGIT_POOL = { '0','1','2','3','4','5','6','7','8','9' };

                static string Garble(string key, int len, bool digits = false)
        {
            if (len <= 0) return "";
            var pool = digits ? DIGIT_POOL : GARBLE_POOL;
            var sb = new StringBuilder(len);
            for (int i = 0; i < len; i++) sb.Append(pool[GarbleIdx(key, i, pool.Length)]);
            return sb.ToString();
        }

                static int GarbleIdx(string key, int i, int n)
        {
            int h = Mix(key, i);
            int period = 70 + (h % 360);
            long tick = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond / period;
            int h2 = Mix(key, i * 31 + 7) ^ (int)(tick * 2654435761L >> 13);
            return (h2 & 0x7FFFFFFF) % n;
        }

                static int Mix(string key, int i)
        {
            unchecked
            {

                int h = unchecked((int)2166136261);

                if (key != null) foreach (char ch in key) h = (h ^ (int)ch) * 16777619;
                h = (h ^ i) * 16777619;
                return h & 0x7FFFFFFF;
            }
        }

                static int ShakeOffset(int what)
        {
            if (what != _shakeTarget) return 0;
            double t = (DateTime.Now - _shakeAt).TotalMilliseconds;
            if (t < 0 || t >= SHAKE_MS) return 0;
            double p = t / SHAKE_MS;
            return (int)Math.Round(Math.Sin(p * Math.PI * 2 * SHAKE_CYCLES) * SHAKE_AMP * (1 - p));
        }

                static void ShakeGo(string why)
            => Shake(SHAKE_GO, why.Length > 0 ? why : "这个课题现在开不了");

                static void Shake(int what, string why)
        {
            _shakeTarget = what;
            _shakeAt = DateTime.Now;
            TaskRunner.LastError = why;
            Plugin.Logger.LogWarning($"[Exam] 按不动（{what}）：{why}");
        }

                static void DrawRowPlate(IntPtr gfx, Challenge c, bool isSel, bool isHover, int x, int y, int w, int h)
            => DrawRowState(gfx, c, isSel ? "active" : (isHover ? "hover" : "normal"), x, y, w, h);

                static void DrawRowState(IntPtr gfx, Challenge c, string st, int x, int y, int w, int h)
        {
            IntPtr p = RowPlateTex(c, st);
            if (p != IntPtr.Zero) { Draw(gfx, p, x, y, w, h); return; }
            GdipFillRectangleI(gfx, st == "active" ? _pBrTabOn : (st == "hover" ? _pBrTabHover : _pBrTabOff),
                x, y, w, h);
        }

                static IntPtr RowPlateTex(Challenge c, string st)
        {

            if (!TaskConfig.Unlocked(c))
            {
                IntPtr lk = Tex("task/row_locked.png");
                if (lk != IntPtr.Zero) return lk;

            }
            else
            {

                string art = FacingArt(c);
                if (art.Length > 0)
                {
                    IntPtr p = PlateTex("task/row_" + art, st);
                    if (p != IntPtr.Zero) return p;
                }

                if (c.N >= HARD_FROM)
                {
                    IntPtr p = PlateTex("task/row_hard", st);
                    if (p != IntPtr.Zero) return p;
                }
            }

            IntPtr q = PlateTex("task/row", st);
            if (q != IntPtr.Zero) return q;

            return TaskPlate("row", st);
        }

                static string FacingArt(Challenge c)
        {
            if (c == null) return "";
            if (!c.Final) return c.Art;
            return TaskProgress.Cleared(TaskConfig.ProgKey(c)) ? c.Art : "unfin";
        }

                static bool Masked(Challenge c)
            => c != null && c.Final && TaskConfig.Unlocked(c)
               && !TaskProgress.Cleared(TaskConfig.ProgKey(c));

                static bool DrawNameInCode(Challenge c)
            => c.Art.Length == 0 || Masked(c);

        static void DrawExButtons(IntPtr gfx)
        {

            if (_exDone)
            {
                _exDone = false;
                _exDoneAt = DateTime.Now;
                _exDoneIdx = _exRunningIdx;
                _exRunningIdx = -1;
            }
            bool flashed = (DateTime.Now - _exDoneAt).TotalSeconds < 2.0;

            for (int i = 0; i < ExBtnCount(); i++)
            {
                int by = ExBtnY(i);
                bool lit = _hoverExBtn == i || (_exBusy && _exRunningIdx == i);
                IntPtr plate = NavPlate(lit ? "hover" : "normal");
                if (plate != IntPtr.Zero) Draw(gfx, plate, ExBtnX(), by, EXBTN_W, EXBTN_H);
                else GdipFillRectangleI(gfx, lit ? _pBrTabHover : _pBrTabOff, ExBtnX(), by, EXBTN_W, EXBTN_H);

                string label = EXBTN_LABELS[i];
                if (_exBusy) label = "导出中…";
                else if (flashed && _exDoneIdx == i) label = "已导出";

                RowTextC(gfx, label, _pFontCjkPage, _pBrWhite,
                    ExBtnX() + EXBTN_W / 2, by + EXBTN_H / 2 + PAGE_TEXT_DY);
            }
        }

        static void HandleClick(int x, int y)
        {
            if (x >= CLOSE_X && x < CLOSE_X + CLOSE_W && y >= CLOSE_Y && y < CLOSE_Y + CLOSE_H)
            {
                SetVisible(false);
                return;
            }

            if (_sheetOpen) { SheetClick(x, y); return; }

            if (OnB30Page)
            {

                int eb = HitTestExBtn(x, y);
                if (eb >= 0) { TriggerExport(eb); return; }

                if (InB30Filter(x, y))
                {
                    int chip = HitTestB30Filter(x, y);
                    if (chip >= 0 && chip != FLT_CHIP_SRCH) B30FilterClick(chip, x);
                    return;
                }

                int tab = HitTestTab(x, y);
                if (tab >= 0)
                {
                    if (tab != _activeTab) { _activeTab = tab; _scroll = 0f; _b30OpenRow = -1; _b30OpenSrc = null; B30OpenAnimReset(); }
                    return;
                }

                if (InList(x, y))
                {
                    _dragging = true;
                    _dragStartY = y;
                    _dragStartScroll = _scroll;
                    _dragMoved = 0;
                    return;
                }
            }
            else if (OnTaskPage)
            {
                int cat = HitTestCat(x, y);
                if (cat >= 0)
                {
                    var cs = Cats;
                    if (cat < cs.Count && cs[cat] != null && cs[cat].Key != _catKey)
                    {
                        _catKey = cs[cat].Key;
                        _selIdx = 0;
                        _rowWipes.Clear();
                        _hoverRow = -1;
                        _hoverGoBtn = -1;
                    }
                    return;
                }

                int db = HitTestDiffBtn(x, y);
                if (db >= 0)
                {
                    var dl = CurChallenges;
                    int ds = SelIdx(dl);
                    if (ds >= 0)
                    {
                        var choices2 = dl[ds].DiffChoices();
                        if (db < choices2.Count)
                        {
                            string pick = choices2[db];
                            TaskProgress.SetPick(dl[ds].Key, pick);

                            TaskRunner.LastError = "";
                        }
                    }
                    return;
                }

                var rowList = CurChallenges;
                int row = HitTestRow(x, y);
                if (row >= 0)
                {
                    if (row < rowList.Count && !TaskConfig.Unlocked(rowList[row]))
                    {
                        _shakeTarget = row;
                        _shakeAt = DateTime.Now;
                        return;
                    }
                    StartRowWipe(rowList, SelIdx(rowList), row);
                    _selIdx = row;
                    return;
                }

                if (HitTestGoBtn(x, y) >= 0)
                {

                    if (TaskRunner.Running) { TaskRunner.RequestAbort(); return; }

                    var list = CurChallenges;
                    int sel = SelIdx(list);
                    if (sel < 0) { ShakeGo("这个分类里还没有课题"); return; }

                    var c = list[sel];

                    if (!TaskConfig.Unlocked(c)) { ShakeGo(TaskConfig.UnlockHint(c)); return; }

                    string why = TaskConfig.WhyNotRunnable(c);
                    if (why.Length > 0) { ShakeGo(why); return; }

                    TaskRunner.RequestStart(c);
                    SetVisible(false);
                    return;
                }
            }
            else if (OnAchievePage)
            {

                int cat = HitCatAt(x, y, AchCats().Count);
                if (cat >= 0)
                {
                    var cs = AchCats();
                    if (cat < cs.Count && cs[cat] != _achCatKey)
                    {
                        _achCatKey = cs[cat];
                        _scroll = 0f;
                        _achHover = -1;
                    }
                    return;
                }

                int row = HitTestAchRow(x, y);
                if (row >= 0)
                {
                    var v = AchView();
                    if (row < v.Count && v[row] != null)
                    {
                        _achKey = v[row].Id;

                        Achievements.MarkSeen(_achKey);
                    }
                    return;
                }

                if (HitTestGoBtn(x, y) >= 0)
                {
                    var av = AchView();

                    if (av.Count == 0) { ShakeAch("这个分类里还没有成就"); return; }

                    var ad = av[Math.Min(Math.Max(AchSelIn(av), 0), av.Count - 1)];
                    if (ad == null) { ShakeAch("这一条读不出来"); return; }
                    if (!Achievements.IsUnlocked(ad.Id)) { ShakeAch("还没拿到这一条，不能当称号"); return; }

                    string want = Achievements.IsEquipped(ad.Id) ? "" : ad.Id;
                    if (!Achievements.SetEquipped(want)) { ShakeAch("这一条还没解锁"); return; }
                    return;
                }

                var cd = AchSelDef();
                int chip = cd == null ? -1 : HitTestTitleChip(x, y, cd);
                if (chip >= 0)
                {
                    if (chip >= 1 && chip > Achievements.GradeOf(cd))
                    { ShakeAch("还没拿到第 " + Achievements.GradeRoman(chip) + " 级，挑不了"); return; }
                    if (!Achievements.SetTitleGrade(chip)) { ShakeAch("这一级现在还挑不了"); return; }
                    return;
                }
            }
            else if (OnConfigPage)
            {

                if (_hotCap >= 0) { HotKeyCapCancel(); return; }

                int tab = HitTestSetTab(x, y);
                if (tab >= 0)
                {
                    if (tab != _setTab)
                    {
                        _setTab = tab;
                        _setHoverRow = -1;
                        _setHoverChip = -1;
                        _hotHint = "";
                        _hotHintBad = false;
                        MarkDirty();
                    }
                    return;
                }

                if (SET_TABS[_setTab] == "按键")
                {
                    int slot = HitTestHotKey(x, y);
                    if (slot >= 0)
                    {
                        int d = SetKeyDefOfSlot(slot);
                        if (d >= 0) HotKeyCapBegin(d);
                        return;
                    }
                    if (HitTestHotReset(x, y))
                    {
                        Hotkeys.ResetAll();
                        Settings.Save();
                        _hotHint = "已全部恢复出厂键位";
                        _hotHintBad = false;
                        MarkDirty();
                        Plugin.Logger.LogInfo("[设置] 快捷键已全部恢复出厂");
                        return;
                    }

                }

                HitTestSetChip(x, y, out int sr, out int sc);
                if (sr >= 0) { SetChipApply(sr, sc); return; }
            }
            else if (OnRecommendPage)
            {

                int card = HitTestRecCard(x, y);
                if (card >= 0)
                {
                    if (card == _recIdx) { StartRec(); return; }
                    _recIdx = card;
                    return;
                }

                if (HitTestGoBtn(x, y) >= 0) { StartRec(); return; }

                if (HitTestRecRefresh(x, y))
                {

                    if (FreeRun.Active) { ShakeRec(SHAKE_REFRESH, "正在跳转 —— 等这一把结束再换"); return; }
                    RollRec();
                }
            }
            else if (OnInfoPage)
            {

                if (HitTestInfoEdit(x, y)) { OpenSkinSheet(); return; }

                if (HitTestInfoDiff(x, y))
                {
                    _infoDiffPick = (_infoDiffPick + 1) % (TaskConfig.DiffOrder.Length + 1);
                    return;
                }

                if (HitTestInfoAch(x, y))
                {
                    _infoAchPct = !_infoAchPct;
                    MarkDirty();
                    return;
                }
            }
            int idx = HitTest(x, y);
            if (idx >= 0) Select(idx);
        }

        static void HandleDrag()
        {

            if (SheetDragTick()) return;

            if (_fltDragChip >= 0)
            {
                if (!Key(VK_LBUTTON)) { _fltDragChip = -1; return; }
                if (GetCursorPos(out var fpt)) B30SliderApply(_fltDragChip, LayoutX(fpt.x - _screenX));
                return;
            }

            if (!_dragging) return;
            if (!Key(VK_LBUTTON))
            {
                _dragging = false;

                if (_dragMoved <= 6 && OnB30Page) B30RowClick(_dragStartY);
                return;
            }
            if (!GetCursorPos(out var pt)) return;

            int dy = LayoutY(pt.y - _screenY) - _dragStartY;
            int moved = Math.Abs(dy);
            if (moved > _dragMoved) _dragMoved = moved;
            SetScroll(_dragStartScroll - dy);
        }

        static void HandleWheelScroll()
        {
            int acc = Interlocked.Exchange(ref _wheelAccum, 0);
            if (acc == 0) return;

            if (_sheetOpen)
            {
                if (!GetCursorPos(out var spt)) return;
                SheetWheel(acc, LayoutX(spt.x - _screenX), LayoutY(spt.y - _screenY));
                return;
            }

            if (OnB30Page)
            {
                if (!GetCursorPos(out var pt)) return;
                int wx = LayoutX(pt.x - _screenX), wy = LayoutY(pt.y - _screenY);
                if (InB30Filter(wx, wy)) return;
                if (!InList(wx, wy)) return;

                SetScroll(_scroll - acc / 120f * ROW_PITCH * WHEEL_STEP_ROWS);
            }
            else if (OnAchievePage)
            {
                if (!GetCursorPos(out var pt)) return;
                if (!InAchList(LayoutX(pt.x - _screenX), LayoutY(pt.y - _screenY))) return;
                _scrollMax = Math.Max(0, AchView().Count * CK_PITCH - CK_MAX * CK_PITCH);
                SetScroll(_scroll - acc / 120f * CK_PITCH * WHEEL_STEP_ROWS);
            }
        }

        static void SetScroll(float v)
        {
            if (_scrollMax <= 0f) { _scroll = 0f; return; }
            if (v < 0f) v = 0f;
            if (v > _scrollMax) v = _scrollMax;
            _scroll = v;
        }

        static void B30OpenAnimStart()
        {
            _b30OpenT = 0f;
            _b30OpenAt = Environment.TickCount64;
        }

        static void B30OpenAnimReset()
        {
            _b30OpenT = 1f;
            _b30OpenAt = 0;
        }

        static void B30OpenAnimTick()
        {
            if (_b30OpenAt == 0) return;
            float t = (Environment.TickCount64 - _b30OpenAt) / (float)B30_OPEN_MS;
            if (t >= 1f)
            {
                _b30OpenT = 1f;
                _b30OpenAt = 0;

                MarkDirty();
                return;
            }
            float u = 1f - t;
            _b30OpenT = 1f - u * u * u;
        }

                static void B30RowClick(int clientY)
        {
            int n = RowCount();
            if (_b30OpenRow >= n) _b30OpenRow = -1;

            float rel = clientY - LIST_Y + _dragStartScroll;

            if (_b30OpenRow >= 0)
            {
                float top = RowOffset(_b30OpenRow) + ROW_H + DETAIL_GAP;
                if (rel >= top && rel < top + DETAIL_H)
                {
                    _b30OpenRow = -1;
                    _b30OpenSrc = null;
                    B30OpenAnimReset();
                    EnsureRowVisible();
                    MarkDirty();
                    return;
                }
            }

            for (int i = 0; i < n; i++)
            {
                int top = RowOffset(i);
                if (rel >= top && rel < top + ROW_H)
                {
                    _b30OpenRow = _b30OpenRow == i ? -1 : i;
                    _b30OpenSrc = _b30OpenRow >= 0 ? B30ListRef() : null;
                    if (_b30OpenRow >= 0) B30OpenAnimStart(); else B30OpenAnimReset();
                    EnsureRowVisible();
                    MarkDirty();
                    return;
                }
            }

        }

                static void EnsureRowVisible()
        {
            int n = RowCount();
            _scrollMax = Math.Max(0, ListContentH(n) - LIST_H);
            if (_b30OpenRow >= 0 && _b30OpenRow < n)
            {
                float top = RowOffset(_b30OpenRow);
                float bottom = top + ROW_H + DETAIL_GAP + DETAIL_H;
                if (bottom > _scroll + LIST_H) _scroll = bottom - LIST_H;
                if (top < _scroll) _scroll = top;
            }
            SetScroll(_scroll);
        }

        static void Select(int idx)
        {
            if (_activeNav == idx) return;
            _activeNav = idx;
            _activeTab = 0;
            _catKey = TaskConfig.TagRank;
            _achCatKey = "";
            _scroll = 0f;
            _dragging = false;
            _fltDragChip = -1;
            _b30OpenRow = -1;
            _b30OpenSrc = null;
            B30OpenAnimReset();

            PlayerInfo.Invalidate();

            _infoDiffPick = 0;

            InfoBdInvalidate();

            Settings.LastTab = idx;
        }

        static bool Key(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

        static void HandleKeys()
        {
            bool up = Key(VK_UP), down = Key(VK_DOWN), esc = Key(VK_ESCAPE);

            if (NameDlgOpen) { _wasUp = up; _wasDown = down; _wasEsc = esc; return; }

            if (_sheetOpen)
            {
                if (esc && !_wasEsc)
                {
                    if (_cropOn) CropCancel();
                    else CloseSkinSheet();
                }
                _wasUp = up; _wasDown = down; _wasEsc = esc;
                return;
            }

            if (_hotCap >= 0)
            {
                if (esc && !_wasEsc) HotKeyCapCancel();
                _wasUp = up; _wasDown = down; _wasEsc = esc;
                return;
            }

            if (esc && !_wasEsc) SetVisible(false);
            if (down && !_wasDown) Move(1);
            if (up && !_wasUp) Move(-1);

            _wasUp = up; _wasDown = down; _wasEsc = esc;
        }

        static void Move(int dir)
        {
            int next = _activeNav + dir;
            if (next < 0) next = NAV.Length - 1;
            if (next >= NAV.Length) next = 0;
            _activeNav = next;
            InfoBdInvalidate();
            Settings.LastTab = next;
        }

        static IntPtr Tex(string rel)
        {
            if (_tex.TryGetValue(rel, out var cached)) return cached;

            IntPtr bmp = IntPtr.Zero;
            string full = Path.Combine(GuiDir, rel);
            if (File.Exists(full) && GdipCreateBitmapFromFile(full, out bmp) != 0) bmp = IntPtr.Zero;

            _tex[rel] = bmp;
            return bmp;
        }

        static void LoadTextures()
        {
            if (_texLoaded) return;
            _texLoaded = true;
            _tex.Clear();

            SetupFonts();
            if (_pBrWhite   == IntPtr.Zero) GdipCreateSolidFill(unchecked((int)0xFFFFFFFF), out _pBrWhite);
            if (_pBrShade   == IntPtr.Zero) GdipCreateSolidFill(unchecked((int)0x50101828), out _pBrShade);
            if (_pBrActiveText == IntPtr.Zero) GdipCreateSolidFill(ACTIVE_TEXT_ARGB, out _pBrActiveText);
            if (_pBrArtist  == IntPtr.Zero) GdipCreateSolidFill(ARTIST_TEXT_ARGB, out _pBrArtist);

            if (_pBrTabOn    == IntPtr.Zero) GdipCreateSolidFill(unchecked((int)0xFFE1E2FE), out _pBrTabOn);
            if (_pBrTabOff   == IntPtr.Zero) GdipCreateSolidFill(unchecked((int)0x2EFFFFFF), out _pBrTabOff);
            if (_pBrTabHover == IntPtr.Zero) GdipCreateSolidFill(unchecked((int)0x4DFFFFFF), out _pBrTabHover);

            int found = 0;
            string[] fixedNames =
            {
                "base/panel_base.png", "base/close_normal.png", "base/close_hover.png",
                "nav/nav_normal.png", "nav/nav_hover.png", "nav/nav_active.png",
                "nav/sub_normal.png", "nav/sub_hover.png", "nav/sub_active.png",
            };
            foreach (var n in fixedNames) if (Tex(n) != IntPtr.Zero) found++;
            foreach (var it in NAV)
            {
                if (Tex($"nav/label_{it.Key}.png") != IntPtr.Zero) found++;
                if (Tex($"pages/{it.Page}.png") != IntPtr.Zero) found++;
            }

            Plugin.Logger.LogInfo($"[Panel] GUI 素材 {found} 张（{GuiDir}）");
        }

        static void DisposeTextures()
        {
            foreach (var kv in _tex)
                if (kv.Value != IntPtr.Zero) GdipDisposeImage(kv.Value);
            _tex.Clear();

            foreach (var kv in _texAbs)
                if (kv.Value != IntPtr.Zero) GdipDisposeImage(kv.Value);
            _texAbs.Clear();
            _rowBgPath.Clear();

            foreach (var kv in _bdSmall)
                if (kv.Value != IntPtr.Zero) GdipDisposeImage(kv.Value);
            _bdSmall.Clear();
            _bdAsked.Clear();
            if (_infoBd != IntPtr.Zero) { GdipDisposeImage(_infoBd); _infoBd = IntPtr.Zero; }
            InfoBdInvalidate();

            foreach (var kv in _decoTex)
                if (kv.Value != IntPtr.Zero) GdipDisposeImage(kv.Value);
            _decoTex.Clear();

            foreach (var kv in _dtGradeTex)
                if (kv.Value != IntPtr.Zero) GdipDisposeImage(kv.Value);
            _dtGradeTex.Clear();
            foreach (var kv in _dtFonts)
                if (kv.Value != IntPtr.Zero) GdipDeleteFont(kv.Value);
            _dtFonts.Clear();
            _b30OpenRow = -1;
            _b30OpenSrc = null;
            B30OpenAnimReset();

            DisposeSkinSheet();

            if (_pFrameGfx != IntPtr.Zero) { GdipDeleteGraphics(_pFrameGfx); _pFrameGfx = IntPtr.Zero; }
            if (_pFrameBmp != IntPtr.Zero) { GdipDisposeImage(_pFrameBmp); _pFrameBmp = IntPtr.Zero; }

            if (_pScaleGfx != IntPtr.Zero) { GdipDeleteGraphics(_pScaleGfx); _pScaleGfx = IntPtr.Zero; }
            if (_pScaleBmp != IntPtr.Zero) { GdipDisposeImage(_pScaleBmp); _pScaleBmp = IntPtr.Zero; }
            _scaleW = _scaleH = 0;
            MarkDirty();

            _texLoaded = false;
        }

        static void SetupFonts()
        {
            if (_pFontFamily == IntPtr.Zero &&
                GdipCreateFontFamilyFromName("Segoe UI", IntPtr.Zero, out _pFontFamily) != 0)
                GdipCreateFontFamilyFromName("Arial", IntPtr.Zero, out _pFontFamily);

            if (_pCjkFamily == IntPtr.Zero && File.Exists(CjkFontPath))
            {
                if (_pCjkCollection == IntPtr.Zero) GdipNewPrivateFontCollection(out _pCjkCollection);
                if (_pCjkCollection != IntPtr.Zero &&
                    GdipPrivateAddFontFile(_pCjkCollection, CjkFontPath) != 0)
                    Plugin.Logger.LogWarning($"[Panel] 中文字体加载失败: {CjkFontPath}");

                if (_pCjkCollection != IntPtr.Zero)
                {
                    if (GdipCreateFontFamilyFromName("Alimama ShuHeiTi", _pCjkCollection, out _pCjkFamily) != 0)
                        GdipCreateFontFamilyFromName("阿里妈妈数黑体", _pCjkCollection, out _pCjkFamily);

                    if (_pCjkFamily != IntPtr.Zero) Plugin.Logger.LogInfo("[Panel] 中文字体就绪: Alimama ShuHeiTi");
                    else Plugin.Logger.LogWarning("[Panel] 找不到字体族 Alimama ShuHeiTi，中文退回 Segoe UI");
                }
            }

            IntPtr cjk = CjkFamSrc();
            float constSize = ConstTable.TextSizeScale();
            cjkConstFontDictionary[constSize] = RegisterCjkFont(cjk, constSize);
            if (_pFontCjkLabel == IntPtr.Zero) _pFontCjkLabel = RegisterCjkFont(cjk, 1);
            if (_pFontCjkPage  == IntPtr.Zero) GdipCreateFont(cjk, 28f, 0 , UnitPixel, out _pFontCjkPage);

            MakeNameFont();
            if (_pFontInfoTitle == IntPtr.Zero) GdipCreateFont(cjk, INFO_TITLE_SZ, 0 , UnitPixel, out _pFontInfoTitle);

            MakePlateFonts();

            IntPtr num = RowFamSrc();
            _pRowNumFamily = num;

            _pRowTitleFam  = _pFontFamily;
            _pRowFuroreFam = num;
            _pRowFamSrc    = num;
            if (_pFontRowNum == IntPtr.Zero)
            {
                _pFontRowNum = RegisterRowFont(num, 1);
                rowConstFontDictionary[constSize] = RegisterRowFont(num, constSize);
            }

            if (_pFontTab     == IntPtr.Zero) GdipCreateFont(num, FS_TAB, 1 , UnitPixel, out _pFontTab);
        }

        static IntPtr RegisterRowFont(IntPtr fontFamily, float scale)
        {
            GdipCreateFont(fontFamily, 30f * ROW_S * scale, 1 , UnitPixel, out var tmp);
            return tmp;
        }

        static IntPtr RegisterCjkFont(IntPtr fontFamily, float scale)
        {
            GdipCreateFont(fontFamily, 20f, 0 , UnitPixel, out var tmp);
            return tmp;
        }

        static IntPtr RowFamSrc() => _fontFamily != IntPtr.Zero ? _fontFamily : _pFontFamily;

        static IntPtr CjkFamSrc() => _pCjkFamily != IntPtr.Zero ? _pCjkFamily : _pFontFamily;

                static void MakeNameFont()
        {
            if (_pFontInfoName != IntPtr.Zero) { GdipDeleteFont(_pFontInfoName); _pFontInfoName = IntPtr.Zero; }
            IntPtr fam = RowFamSrc();
            if (GdipCreateFont(fam, INFO_NAME_SZ, 1 , UnitPixel, out _pFontInfoName) != 0
                || _pFontInfoName == IntPtr.Zero)
                GdipCreateFont(fam, INFO_NAME_SZ, 0 , UnitPixel, out _pFontInfoName);
        }

                static void MakePlateFonts()
        {
            if (_pFontInfoRat != IntPtr.Zero) { GdipDeleteFont(_pFontInfoRat); _pFontInfoRat = IntPtr.Zero; }
            if (_pFontInfoVal != IntPtr.Zero) { GdipDeleteFont(_pFontInfoVal); _pFontInfoVal = IntPtr.Zero; }
            IntPtr fam = RowFamSrc();
            if (GdipCreateFont(fam, INFO_RAT_SZ, 1 , UnitPixel, out _pFontInfoRat) != 0
                || _pFontInfoRat == IntPtr.Zero)
                GdipCreateFont(fam, INFO_RAT_SZ, 0 , UnitPixel, out _pFontInfoRat);
            if (GdipCreateFont(fam, INFO_VAL_SZ, 1 , UnitPixel, out _pFontInfoVal) != 0
                || _pFontInfoVal == IntPtr.Zero)
                GdipCreateFont(fam, INFO_VAL_SZ, 0 , UnitPixel, out _pFontInfoVal);
        }

        static void SyncRowFonts()
        {
            IntPtr src = RowFamSrc();
            if (src == _pRowFamSrc) return;

            var arr = rowConstFontDictionary.ToArray();
            foreach (var pair in arr)
            {
                if (pair.Value != IntPtr.Zero) GdipDeleteFont(_pFontTab);
                rowConstFontDictionary.Remove(pair.Key);
            }
            if (_pFontRowNum != IntPtr.Zero) { GdipDeleteFont(_pFontRowNum); _pFontRowNum = IntPtr.Zero; }
            if (_pFontTab    != IntPtr.Zero) { GdipDeleteFont(_pFontTab);    _pFontTab    = IntPtr.Zero; }
            foreach (var kv in _famTitleBase)  if (kv.Value != IntPtr.Zero) GdipDeleteFont(kv.Value);
            foreach (var kv in _famArtistBase) if (kv.Value != IntPtr.Zero) GdipDeleteFont(kv.Value);
            _famTitleBase.Clear();
            _famArtistBase.Clear();
            _inkCache.Clear();

            foreach (var kv in _dtFonts)
                if (kv.Value != IntPtr.Zero) GdipDeleteFont(kv.Value);
            _dtFonts.Clear();

            _pRowFamSrc    = src;
            _pRowNumFamily = src;
            _pRowFuroreFam = src;
            _pFontRowNum = RegisterRowFont(src, 1);
            float constSize = ConstTable.TextSizeScale();
            rowConstFontDictionary[constSize] = RegisterRowFont(src, constSize);
            GdipCreateFont(src, FS_TAB,      1 , UnitPixel, out _pFontTab);

            MakeNameFont();

            MakePlateFonts();
        }

        static void Draw(IntPtr gfx, IntPtr img, int x, int y, int w, int h)
        {
            if (img != IntPtr.Zero) GdipDrawImageRectI(gfx, img, x, y, w, h);
        }

                static IntPtr RoundRectPath(int x, int y, int w, int h, int r)
        {
            if (w <= 0 || h <= 0) return IntPtr.Zero;
            int lim = Math.Min(w, h) / 2;
            if (r > lim) r = lim;
            if (r <= 0) return IntPtr.Zero;
            if (GdipCreatePath(1 , out var path) != 0 || path == IntPtr.Zero) return IntPtr.Zero;
            float d = r * 2f;
            GdipAddPathArc(path, x, y, d, d, 180f, 90f);
            GdipAddPathArc(path, x + w - d, y, d, d, 270f, 90f);
            GdipAddPathArc(path, x + w - d, y + h - d, d, d, 0f, 90f);
            GdipAddPathArc(path, x, y + h - d, d, d, 90f, 90f);
            GdipClosePathFigure(path);
            return path;
        }

                static IntPtr RingPath(int x, int y, int w, int h, int t)
        {
            if (t <= 0 || w <= t * 2 || h <= t * 2) return IntPtr.Zero;
            if (GdipCreatePath(0 , out var path) != 0 || path == IntPtr.Zero) return IntPtr.Zero;
            GdipAddPathRectangle(path, x, y, w, h);
            GdipAddPathRectangle(path, x + t, y + t, w - t * 2, h - t * 2);
            return path;
        }

                static IntPtr AccentCapPath(int x, int y, int h, int r, int t)
        {
            if (h <= 0 || t <= 0) return IntPtr.Zero;
            if (r > h / 2) r = h / 2;
            if (r < t) r = t;
            if (GdipCreatePath(1 , out var path) != 0 || path == IntPtr.Zero) return IntPtr.Zero;
            float d = r * 2f;
            GdipAddPathLine(path, x + t, y, x + r, y);
            GdipAddPathArc(path, x, y, d, d, 270f, -90f);
            GdipAddPathLine(path, x, y + r, x, y + h - r);
            GdipAddPathArc(path, x, y + h - d, d, d, 180f, -90f);
            GdipAddPathLine(path, x + r, y + h, x + t, y + h);
            GdipClosePathFigure(path);
            return path;
        }

                static void FillPathAA(IntPtr gfx, IntPtr brush, IntPtr path)
        {
            if (path == IntPtr.Zero) return;
            if (brush != IntPtr.Zero)
            {
                GdipSetSmoothingMode(gfx, 4 );
                GdipFillPath(gfx, brush, path);
                GdipSetSmoothingMode(gfx, 3 );
            }
            GdipDeletePath(path);
        }

                static void FillRoundRect(IntPtr gfx, IntPtr brush, int x, int y, int w, int h, int r)
        {
            if (brush == IntPtr.Zero) return;
            var path = RoundRectPath(x, y, w, h, r);
            if (path == IntPtr.Zero) { GdipFillRectangleI(gfx, brush, x, y, w, h); return; }
            FillPathAA(gfx, brush, path);
        }

                static void FillAccentCap(IntPtr gfx, IntPtr brush, int x, int y, int h, int r, int t)
        {
            if (brush == IntPtr.Zero) return;
            var path = AccentCapPath(x, y, h, r, t);
            if (path == IntPtr.Zero) { GdipFillRectangleI(gfx, brush, x, y, t, h); return; }
            FillPathAA(gfx, brush, path);
        }

        static void DrawText(IntPtr gfx, string text, IntPtr font, IntPtr brush, int x, int y, int w, int h, int dy)
        {
            if (font == IntPtr.Zero) return;
            var r = new GPRECTF { X = x, Y = y + dy, Width = w, Height = h };
            GdipDrawString(gfx, text, -1, font, ref r, _formatCenter, brush);
        }

        const int CAT_W = 200, CAT_H = 44, CAT_GAP = 12;
        const int CAT_MIN_W = 76;
        const int CATROW_X = CONTENT_X + 32;
        const int CATROW_Y = CONTENT_Y + 76;
        const int PAGE_TOP = CATROW_Y + CAT_H + 24;

        static string _catKey = TaskConfig.TagRank;
        static int _hoverCat = -1;
                static bool _catOverflowLogged = false;

                static List<TaskConfig.CatDef> Cats => TaskConfig.Cats;

                static int ActiveCatIdx()
        {
            var cs = Cats;
            for (int i = 0; i < cs.Count; i++)
                if (cs[i] != null && cs[i].Key == _catKey) return i;
            if (cs.Count > 0) _catKey = cs[0].Key;
            else _catKey = TaskConfig.TagRank;
            return 0;
        }

        const int CK_X = CONTENT_X + 24;
        const int CK_W = 460;
        const int CK_TOP = PAGE_TOP;
        const int CK_PITCH = 42;
        const int CK_MAX = 10;
        const int CK_FOOT = CK_TOP + CK_MAX * CK_PITCH + 12;

        const int S0TAG_W = 105, S0TAG_H = 22, S0TAG_PAD = 1;
        const int S0TAG_ROW_DY = 10;
        const int S0TAG_NEAR = 24;
        const int S0TAG_CUT_FRAMES = 6;
        const float S0TAG_CUT_DEG = 26f;
        const int S0TAG_DET_GAP = 12;
        const int S0TAG_DET_DY = 6;

        const int DET_X = CONTENT_X + 520;
        const int DET_W = CONTENT_W - 520 - 24;
        const int DETB_X = DET_X, DETB_Y = CONTENT_Y + CONTENT_H - 76;
        const int DETB_W = 240, DETB_H = 48;

        const int SONGNUM_W = 34, SONGNUM_GAP = 12;

        const int DIFFB_W = 40, DIFFB_H = 40, DIFFB_GAP = 8;

        const int DIFFB_PAD = 12;

                static int _diffBarY = -1;

        static int _selIdx = 0;
        static int _hoverRow = -1;
        static int _hoverGoBtn = -1;
        static int _hoverDiffBtn = -1;

        static float _s0TagVis = 1f, _s0TagVisTarget = 1f;
        static float _s0TagFrom = 1f, _s0TagTo = 1f;
        static int _s0TagCutT;
        static bool _s0TagCutDirty;

        sealed class RowWipe
        {
            public string From = "normal", To = "active";
            public int T, Frames;
            public bool KeepRight;
        }
        static readonly Dictionary<string, RowWipe> _rowWipes = new Dictionary<string, RowWipe>();
                const int ROW_WIPE_FRAMES = 9;
                const float ROW_WIPE_DEG = 26f;

        static bool OnTaskPage => NAV[_activeNav].Key == "task";

                static void S0TagCutTick()
        {
            _s0TagCutDirty = false;
            if (_s0TagTo != _s0TagVisTarget)
            {
                _s0TagFrom = _s0TagVis;
                _s0TagTo = _s0TagVisTarget;
                _s0TagCutT = 0;
            }

            if (_s0TagVis == _s0TagTo) return;

            if (_s0TagCutT < S0TAG_CUT_FRAMES) _s0TagCutT++;
            float t = (float)_s0TagCutT / S0TAG_CUT_FRAMES;
            if (t > 1f) t = 1f;

            float e = (float)Math.Sqrt(t * (2f - t));
            _s0TagVis = _s0TagFrom + (_s0TagTo - _s0TagFrom) * e;
            if (_s0TagCutT >= S0TAG_CUT_FRAMES) _s0TagVis = _s0TagTo;
            _s0TagCutDirty = true;
        }

                static bool S0TagNear(int x, int y)
        {
            if (!GetCursorPos(out var pt)) return false;
            int cx = LayoutX(pt.x - _screenX), cy = LayoutY(pt.y - _screenY);
            return cx >= x - S0TAG_NEAR && cx < x + S0TAG_W + S0TAG_NEAR
                && cy >= y - S0TAG_NEAR && cy < y + S0TAG_H + S0TAG_NEAR;
        }

                static void RowWipeTick()
        {
            if (_rowWipes.Count == 0) return;
            List<string> done = null;
            foreach (var kv in _rowWipes)
            {
                var wp = kv.Value;
                if (wp.T >= wp.Frames)
                {
                    if (done == null) done = new List<string>();
                    done.Add(kv.Key);
                    continue;
                }
                wp.T++;
            }
            if (done != null) foreach (var k in done) _rowWipes.Remove(k);
        }

                static void StartRowWipe(List<Challenge> list, int oldSel, int newSel)
        {
            if (list == null || oldSel == newSel) return;
            if (oldSel >= 0 && oldSel < list.Count)
                _rowWipes[list[oldSel].Key] = new RowWipe
                {
                    From = "active",
                    To = _hoverRow == oldSel ? "hover" : "normal",
                    Frames = ROW_WIPE_FRAMES,
                    KeepRight = false,
                };
            if (newSel >= 0 && newSel < list.Count)
                _rowWipes[list[newSel].Key] = new RowWipe
                {
                    From = _hoverRow == newSel ? "hover" : "normal",
                    To = "active",
                    Frames = ROW_WIPE_FRAMES,
                    KeepRight = true,
                };
        }

                static void DrawRowWipe(IntPtr gfx, Challenge c, RowWipe wp, int x, int y, int w, int h)
        {
            DrawRowState(gfx, c, wp.To, x, y, w, h);
            IntPtr old = RowPlateTex(c, wp.From);
            if (old == IntPtr.Zero) return;
            float t = wp.Frames > 0 ? (float)wp.T / wp.Frames : 1f;
            if (t > 1f) t = 1f;
            float e = (float)Math.Sqrt(t * (2f - t));
            DrawSlantWipe(gfx, old, x, y, w, h, 1f - e, ROW_WIPE_DEG, wp.KeepRight);
        }

        static bool OnAchievePage => NAV[_activeNav].Key == "achieve";

                const int ACH_ROW_H = CK_PITCH - 6;

                static string _achKey = "";
        static int _achHover = -1;
                static int _achHoverTitG = -1;

        static string _achCatKey = "";
        static int _achHoverCat = -1;
        static List<string> _achCats = null;
        static List<Achievements.Def> _achCatsSrc = null;
        static int _achCatsRev = -1;

        static List<string> AchCats()
        {
            var ds = Achievements.All;
            int rev = Achievements.StatesRevision;
            if (!ReferenceEquals(ds, _achCatsSrc) || _achCatsRev != rev)
            {
                var cs = new List<string> { "" };
                for (int i = 0; i < ds.Count; i++)
                {
                    var d = ds[i];

                    if (!Achievements.IsVisible(d)) continue;
                    if (!string.IsNullOrEmpty(d.Cat) && !cs.Contains(d.Cat)) cs.Add(d.Cat);
                }
                _achCats = cs;
                _achCatsSrc = ds;
                _achCatsRev = rev;

                if (!cs.Contains(_achCatKey)) _achCatKey = "";
            }
            return _achCats;
        }

        static string AchCatLabel(string key) => string.IsNullOrEmpty(key) ? "全部" : key;

        static List<Achievements.Def> _achView = new List<Achievements.Def>();
        static List<Achievements.Def> _achViewSrc = null;
        static string _achViewKey = null;
        static int _achViewRev = -1;

                static List<Achievements.Def> AchView()
        {
            var ds = Achievements.All;
            int rev = Achievements.StatesRevision;
            if (ReferenceEquals(ds, _achViewSrc) && _achViewKey == _achCatKey && _achViewRev == rev)
                return _achView;

            var cs = AchCats();
            var rank = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < cs.Count; i++) rank[cs[i]] = i;

            var v = new List<Achievements.Def>();
            var unlocked = new List<bool>();
            var catRank = new List<int>();
            for (int i = 0; i < ds.Count; i++)
            {
                var d = ds[i];
                if (!Achievements.IsVisible(d)) continue;
                if (_achCatKey.Length > 0 && d.Cat != _achCatKey) continue;
                v.Add(d);

                unlocked.Add(Achievements.IsUnlocked(d.Id));
                catRank.Add(rank.TryGetValue(d.Cat ?? "", out int x) ? x : int.MaxValue);
            }

            var order = new List<int>(v.Count);
            for (int i = 0; i < v.Count; i++) order.Add(i);
            order.Sort((ia, ib) =>
            {
                if (unlocked[ia] != unlocked[ib]) return unlocked[ia] ? -1 : 1;
                if (catRank[ia] != catRank[ib]) return catRank[ia] < catRank[ib] ? -1 : 1;
                return ia.CompareTo(ib);
            });
            var sorted = new List<Achievements.Def>(v.Count);
            for (int i = 0; i < order.Count; i++) sorted.Add(v[order[i]]);

            _achView = sorted;
            _achViewSrc = ds;
            _achViewKey = _achCatKey;
            _achViewRev = rev;
            return _achView;
        }

                static int AchSelIn(List<Achievements.Def> v)
        {
            for (int i = 0; i < v.Count; i++)
                if (v[i] != null && v[i].Id == _achKey) return i;
            if (v.Count > 0) _achKey = v[0].Id;
            else _achKey = "";
            return 0;
        }

                static Achievements.Def AchSelDef()
        {
            var v = AchView();
            if (v.Count == 0) return null;
            return v[Math.Min(Math.Max(AchSelIn(v), 0), v.Count - 1)];
        }

                static bool InAchList(int x, int y)
            => x >= CK_X && x < CK_X + CK_W && y >= CK_TOP && y < CK_TOP + CK_MAX * CK_PITCH;

        static int HitTestAchRow(int x, int y)
        {
            if (!InAchList(x, y)) return -1;
            var v = AchView();
            int i = (int)((y - CK_TOP + _scroll) / CK_PITCH);
            if (i < 0 || i >= v.Count) return -1;
            return i;
        }

                static void DrawAchPlate(IntPtr gfx, Achievements.Def d, bool isSel, bool isHover,
            int x, int y, int w, int h)
        {
            string st = isSel ? "active" : (isHover ? "hover" : "normal");

            int lv = d == null ? 1
                : (Achievements.GradeOf(d) > 0 ? Achievements.MaxGrade(d) : Achievements.FromLevel(d));
            if (lv < 1) lv = 1;
            if (lv > 4) lv = 4;
            IntPtr tier = Tex($"overlay/arch_tier{lv}.png");
            if (tier != IntPtr.Zero)
            {
                DrawAchTier(gfx, tier, isSel ? 0.22f : (isHover ? 0.10f : 0f), x, y, w, h);
                return;
            }

            IntPtr own = Tex($"task/ach_{st}.png");
            if (own == IntPtr.Zero && st != "normal") own = Tex("task/ach_normal.png");
            if (own != IntPtr.Zero) { Draw(gfx, own, x, y, w, h); return; }

            if (DrawPlate(gfx, "task/row", st, x, y, w, h)) return;
            IntPtr nav = TaskPlate("row", st);
            if (nav != IntPtr.Zero) { Draw(gfx, nav, x, y, w, h); return; }
            GdipFillRectangleI(gfx,
                st == "active" ? _pBrTabOn : (st == "hover" ? _pBrTabHover : _pBrTabOff), x, y, w, h);
        }

                static void DrawAchTier(IntPtr gfx, IntPtr img, float boost, int x, int y, int w, int h)
        {
            if (img == IntPtr.Zero || w <= 0 || h <= 0) return;
            if (GdipGetImageWidth(img, out uint iw) != 0 || iw == 0) return;
            if (GdipGetImageHeight(img, out uint ih) != 0 || ih == 0) return;

            int sw = (int)iw, sh = (int)ih;
            if ((long)iw * h > (long)ih * w) sw = (int)Math.Round((double)ih * w / h);
            else                              sh = (int)Math.Round((double)iw * h / w);
            if (sw < 1) sw = 1;
            if (sh < 1) sh = 1;
            int sx = ((int)iw - sw) / 2, sy = ((int)ih - sh) / 2;

            IntPtr attr = IntPtr.Zero;
            if (boost > 0f)
            {
                if (boost > 1f) boost = 1f;
                var cm = new ColorMatrix
                {
                    m00 = 1f, m11 = 1f, m22 = 1f, m33 = 1f, m44 = 1f,
                    m40 = boost, m41 = boost, m42 = boost
                };
                if (GdipCreateImageAttributes(out attr) != 0) attr = IntPtr.Zero;
                else GdipSetImageAttributesColorMatrix(attr, 0, true, ref cm, IntPtr.Zero, 0);
            }
            GdipDrawImageRectRectI(gfx, img, x, y, w, h, sx, sy, sw, sh, 2 ,
                attr, IntPtr.Zero, IntPtr.Zero);
            if (attr != IntPtr.Zero) GdipDisposeImageAttributes(attr);
        }

                static IntPtr BrAchDim() => Brush(unchecked((int)0xB0101828));

                static IntPtr BrAchNew() => Brush(unchecked((int)0xFFFF5D5D));

                static IntPtr BrAchGold() => Brush(unchecked((int)0xFFF0C24B));

        static IntPtr BrAchLv(int g) => Brush(Achievements.GradeArgb(g));

                static IntPtr BrAchLvDim(int g)
            => Brush((Achievements.GradeArgb(g) & 0x00FFFFFF) | unchecked((int)0x66000000));

                static void DrawDot(IntPtr gfx, IntPtr brush, int cx, int cy, int r)
        {
            if (brush == IntPtr.Zero) return;
            GdipFillEllipseI(gfx, brush, cx - r, cy - r, r * 2, r * 2);
        }

                static IntPtr _pFontAchBadge = IntPtr.Zero;
        static IntPtr AchBadgeFont()
        {
            if (_pFontAchBadge == IntPtr.Zero && _pFontFamily != IntPtr.Zero
                && GdipCreateFont(_pFontFamily, 13f, 1 , UnitPixel, out _pFontAchBadge) != 0)
                _pFontAchBadge = IntPtr.Zero;
            return _pFontAchBadge;
        }

        static void RenderAchieve(IntPtr gfx)
        {
            var brDim = Brush(unchecked((int)0xFF9AA3B8));
            var brVal = Brush(unchecked((int)0xFFEDF1F7));

            var brHdr = Brush(unchecked((int)0xFFB8DCF5));

            int total = Achievements.CountedTotal, done = Achievements.UnlockedCount;

            RowTextL(gfx, "成就", _pFontCjkPage, _pBrWhite, CONTENT_X + 32, CONTENT_Y + 36);
            RowTextR(gfx, $"已解锁 {done} / {total}", _pFontCjkLabel, brHdr,
                CONTENT_X + CONTENT_W - 32, CONTENT_Y + 42);

            if (total == 0)
            {
                RowTextL(gfx, "还没有成就", _pFontCjkLabel, brHdr,
                    CK_X, CK_TOP);
                RowTextL(gfx, "（也可以手写 " + Achievements.DefFile + "，见 ART_成就.md）", _pFontCjkLabel, brDim,
                    CK_X, CK_TOP + 30);
                return;
            }

            var cs = AchCats();
            int cols = CatColsFor(cs.Count), cw = CatWFor(cs.Count);
            for (int i = 0; i < cols; i++)
            {
                string key = cs[i];
                bool on = key == _achCatKey;
                bool hov = i == _achHoverCat;
                int cx = CATROW_X + i * (cw + CAT_GAP);

                FlatTab(gfx, cx, CATROW_Y, cw, CAT_H, 8, on, hov);
                RowTextC(gfx, Clip(AchCatLabel(key), cw), _pFontCjkLabel, FlatTabInk(on),
                    cx + cw / 2, CATROW_Y + CAT_H / 2 + LABEL_TEXT_DY);
            }

            var v = AchView();
            int sel = AchSelIn(v);
            int n = v.Count;

            _scrollMax = Math.Max(0, n * CK_PITCH - CK_MAX * CK_PITCH);
            if (_scroll > _scrollMax) _scroll = _scrollMax;
            if (_scroll < 0f) _scroll = 0f;

            int y0 = (int)_scroll;
            int first = y0 / CK_PITCH;
            int last  = Math.Min(n - 1, (y0 + CK_MAX * CK_PITCH) / CK_PITCH);

            GdipSetClipRect(gfx, CK_X, CK_TOP, CK_W, CK_MAX * CK_PITCH, 0 );
            for (int i = first; i <= last; i++)
            {
                var d = v[i];
                if (d == null) continue;
                int ry = CK_TOP + i * CK_PITCH - y0;
                bool isSel = i == sel;
                bool isHov = i == _achHover;
                var st = Achievements.GetState(d.Id);
                bool got = st != null;
                int h = ACH_ROW_H;

                DrawAchPlate(gfx, d, isSel, isHov, CK_X, ry, CK_W, h);

                if (!got)
                {
                    IntPtr dim = Tex("task/ach_dim.png");
                    if (dim != IntPtr.Zero) Draw(gfx, dim, CK_X, ry, CK_W, h);
                    else GdipFillRectangleI(gfx, BrAchDim(), CK_X, ry, CK_W, h);
                }

                if (got && st.Unseen) DrawDot(gfx, BrAchNew(), CK_X + 15, ry + h / 2, 4);

                const int ACH_NAME_MAXW = CK_W - NAME_DX - 18 - 164;
                bool masked = d.Hidden && !got;
                RowTextL(gfx, ClipTitleLine(masked ? "???" : d.Name, ACH_NAME_MAXW),
                    _pFontCjkLabel, got ? _pBrActiveText : brVal, CK_X + NAME_DX, ry + h / 2f);

                if (got)
                {

                    int g = Achievements.GradeOf(d);
                    string right = st.At.Length > 0 ? st.At : "已解锁";
                    if (g >= 2) right = Achievements.GradeRoman(g) + "  " + right;
                    RowTextR(gfx, right, _pFontCjkLabel,
                        _pBrActiveText, CK_X + CK_W - 18, ry + h / 2f);
                }
                else
                {

                    var p = Achievements.ProgressNext(d);
                    string txt = p.Need > 0 ? $"{p.Now} / {p.Need}" : "—";
                    RowTextR(gfx, txt, _pFontCjkLabel, brVal, CK_X + CK_W - 18, ry + h / 2f);
                }
            }

            GdipResetClip(gfx);

            if (n > 0)
            {
                var selD = v[Math.Min(Math.Max(sel, 0), n - 1)];
                RenderAchDetail(gfx, selD, brDim, brVal, brHdr);

                DrawAchTitleBtn(gfx, selD, brDim, Brush(unchecked((int)0xFFFF7B7B)));
            }
        }

        static void RenderAchDetail(IntPtr gfx, Achievements.Def d, IntPtr brDim, IntPtr brVal, IntPtr brHdr)
        {
            if (d == null) return;
            int x = DET_X;
            int y = PAGE_TOP + 4;

            bool got = Achievements.IsUnlocked(d.Id);
            bool masked = d.Hidden && !got;
            bool ladder = Achievements.HasLadder(d);
            int g = got ? Achievements.GradeOf(d) : 0;

            RowTextL(gfx, masked ? "???" : d.Name, _pFontCjkPage, _pBrWhite, x, y); y += 42;
            string desc = masked ? "？？？" : d.Desc;
            if (desc.Length > 0) { RowTextL(gfx, desc, _pFontCjkLabel, brVal, x, y); y += 32; }

            if (!masked)
            {
                if (!ladder)
                {
                    RowTextL(gfx, "条件", _pFontCjkLabel, brHdr, x, y); y += 30;
                    RowTextL(gfx, Achievements.Describe(d), _pFontCjkLabel, brVal, x, y); y += 38;
                }
                else
                {
                    y = DrawAchLadder(gfx, d, g, x, y, brDim, brVal, brHdr);
                }
            }

            y += 6;
            if (got)
            {
                var st = Achievements.GetState(d.Id);
                RowTextL(gfx, "已解锁", _pFontCjkLabel, brHdr, x, y); y += 30;

                RowTextL(gfx, (ladder && g >= 2 ? Achievements.GradeRoman(g) + " · " : "")
                    + (st != null && st.At.Length > 0 ? st.At : "（没记下时间）"),
                    _pFontCjkLabel, brVal, x, y);

                if (!ladder) return;

                if (g + 1 > Achievements.PublicGrade(d)) return;
                y += 30;
            }

            var p = Achievements.ProgressNext(d);
            RowTextL(gfx, got ? "下一级" : "进度", _pFontCjkLabel, brHdr, x, y); y += 30;

            const int BAR_W = 300, BAR_H = 12;
            int fill = p.Need > 0 ? (int)Math.Clamp(p.Now * (long)BAR_W / p.Need, 0, BAR_W) : 0;
            int barY = y - BAR_H / 2;

            IntPtr barBg = Tex("task/ach_bar_bg.png");
            if (barBg != IntPtr.Zero) Draw(gfx, barBg, x, barY, BAR_W, BAR_H);
            else GdipFillRectangleI(gfx, _pBrTabOff, x, barY, BAR_W, BAR_H);

            if (fill > 0)
            {
                IntPtr barFg = Tex("task/ach_bar_fill.png");

                if (barFg != IntPtr.Zero
                    && GdipGetImageWidth(barFg, out uint fw) == 0
                    && GdipGetImageHeight(barFg, out uint fh) == 0 && fw > 0 && fh > 0)
                {
                    GdipDrawImageRectRectI(gfx, barFg, x, barY, fill, BAR_H,
                        0, 0, (int)(fw * (uint)fill / BAR_W), (int)fh,
                        UnitPixel, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                }
                else GdipFillRectangleI(gfx, _pBrTabOn, x, barY, fill, BAR_H);
            }

            string num = p.Note != null && p.Note.Length > 0
                ? p.Note
                : (p.Need > 0 ? $"{p.Now} / {p.Need}" : "—");
            RowTextL(gfx, num, _pFontCjkLabel, brVal, x + BAR_W + 20, y);
        }

                static int DrawAchLadder(IntPtr gfx, Achievements.Def d, int cur, int x, int y,
            IntPtr brDim, IntPtr brVal, IntPtr brHdr)
        {
            int first = Achievements.FromLevel(d);
            int showMax = Achievements.PublicGrade(d);
            if (first > showMax) return y;

            string[] text = new string[6];
            int[] lo = new int[6];
            for (int g = first; g <= showMax; g++)
            {
                text[g] = Achievements.DescribeRung(d, g);
                lo[g] = g;
                for (int k = first; k < g; k++)
                    if (string.Equals(text[k], text[g], StringComparison.Ordinal)) { lo[g] = lo[k]; break; }
            }

            RowTextL(gfx, "等级", _pFontCjkLabel, brHdr, x, y); y += 30;

            int next = cur <= 0 ? first : cur + 1;
            for (int g = first; g <= showMax; g++)
            {

                bool dup = false;
                for (int k = g + 1; k <= showMax; k++)
                    if (string.Equals(text[k], text[g], StringComparison.Ordinal)) { dup = true; break; }
                if (dup) continue;

                bool met = cur >= lo[g];
                bool nxt = !met && next >= lo[g] && next <= g;
                DrawDot(gfx, met || nxt ? BrAchLv(g) : BrAchLvDim(g),
                    x + 10, y, met ? 5 : 4);
                RowTextL(gfx, Achievements.GradeRoman(g), _pFontCjkLabel,
                    met ? _pBrWhite : nxt ? brHdr : brDim, x + 28, y);
                RowTextL(gfx, Clip(text[g] ?? "", DET_W - 72), _pFontCjkLabel,
                    met ? _pBrWhite : nxt ? brVal : brDim, x + 68, y);
                y += 30;
            }
            return y + 8;
        }

                static string _achTitleMsg = "";
        static DateTime _achTitleMsgAt = DateTime.MinValue;
        const double ACH_TITLE_MSG_MS = 4000.0;

        static void ShakeAch(string why)
        {
            _shakeTarget = SHAKE_GO;
            _shakeAt = DateTime.Now;
            _achTitleMsg = why;
            _achTitleMsgAt = DateTime.Now;
            Plugin.Logger.LogInfo($"[Achieve] 称号按钮按不动：{why}");
        }

                static void DrawAchTitleBtn(IntPtr gfx, Achievements.Def d, IntPtr brDim, IntPtr brBad)
        {
            bool got = Achievements.IsUnlocked(d.Id);
            bool on = Achievements.IsEquipped(d.Id);
            string label = !got ? "未解锁" : (on ? "卸下称号" : "装备为称号");

            bool lit = _hoverGoBtn == 0;
            int gx = DETB_X + ShakeOffset(SHAKE_GO);
            IntPtr plate = TaskPlate("go", lit ? "hover" : "normal");
            if (plate != IntPtr.Zero) DrawNavPlate(gfx, IntPtr.Zero, IntPtr.Zero, plate, gx, DETB_Y, DETB_W, DETB_H);
            else GdipFillRectangleI(gfx, lit ? _pBrTabHover : _pBrTabOff, gx, DETB_Y, DETB_W, DETB_H);

            if (!got) GdipFillRectangleI(gfx, BrAchDim(), gx, DETB_Y, DETB_W, DETB_H);

            RowTextC(gfx, label, _pFontCjkPage, _pBrWhite,
                gx + DETB_W / 2, DETB_Y + DETB_H / 2 + PAGE_TEXT_DY);

            bool chips = TitleChipsShown(d);
            var eqd = Achievements.EquippedDef;

            string cur = eqd == null ? null : (eqd.Name.Length > 0 ? eqd.Name : eqd.Id);
            string curText = cur == null ? "当前称号：没戴" : "当前称号：" + cur;
            if (chips) curText = ClipTitleLine(curText, TitGChipX(0, TitGChipCount(d)) - 16 - DET_X);
            RowTextL(gfx, curText, _pFontCjkLabel,
                cur == null ? brDim : BrAchGold(), DET_X, DETB_Y - 30);

            if (chips) DrawTitleChips(gfx, d, brDim);

            bool showing = _achTitleMsg.Length > 0
                && (DateTime.Now - _achTitleMsgAt).TotalMilliseconds < ACH_TITLE_MSG_MS;
            string hint = showing ? _achTitleMsg
                : !got ? "没拿到的不能当称号"
                : on ? (chips ? "点右边格子挑显示等级" : "再按一次卸下")
                : "会挂在屏幕顶那条下面";
            RowTextL(gfx, hint, _pFontCjkLabel, showing || !got ? brBad : brDim,
                DETB_X + DETB_W + 20, DETB_Y + DETB_H / 2);
        }

        const int TITG_H = 30;
        const int TITG_W = 34;
        const int TITG_AUTO_W = 48;
        const int TITG_GAP = 6;
        const int TITG_CY = DETB_Y - 30;
        const int TITG_TOP = TITG_CY - TITG_H / 2;

                static bool TitleChipsShown(Achievements.Def d)
            => d != null && Achievements.IsEquipped(d.Id) && Achievements.MaxGrade(d) >= 2;

                static int TitGChipCount(Achievements.Def d)
        {
            int lo = Achievements.FromLevel(d);
            int hi = Achievements.PublicGrade(d);
            return hi < lo ? 1 : 2 + (hi - lo);
        }

                static int TitGChipGrade(int i, Achievements.Def d)
            => i <= 0 ? 0 : Achievements.FromLevel(d) + i - 1;

                static int TitGChipW(int i) => i == 0 ? TITG_AUTO_W : TITG_W;

                static int TitGTotalW(int n)
        {
            int w = 0;
            for (int k = 0; k < n; k++) w += TitGChipW(k) + TITG_GAP;
            return w - TITG_GAP;
        }

                static int TitGChipX(int i, int n)
        {
            int x = DET_X + DET_W - TitGTotalW(n);
            for (int k = 0; k < i; k++) x += TitGChipW(k) + TITG_GAP;
            return x;
        }

                static string ClipTitleLine(string s, int w)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (w <= 0) return "";
            int px = 0;
            for (int i = 0; i < s.Length; i++)
            {
                px += s[i] < 0x2E80 ? 10 : 20;
                if (px > w) return s.Substring(0, i) + "…";
            }
            return s;
        }

                static void DrawTitleChips(IntPtr gfx, Achievements.Def d, IntPtr brDim)
        {
            int n = TitGChipCount(d);
            int got = Achievements.GradeOf(d);
            int sel = Achievements.TitleGradeChoice;

            for (int i = 0; i < n; i++)
            {
                int lv = TitGChipGrade(i, d);
                int cx = TitGChipX(i, n), cw = TitGChipW(i);
                bool can = lv == 0 || lv <= got;

                bool on = can && (lv == 0 ? sel <= 0 : sel == lv);

                FlatTab(gfx, cx, TITG_TOP, cw, TITG_H, 6, on, lv == _achHoverTitG);

                if (!can) FillRoundRect(gfx, BrAchDim(), cx, TITG_TOP, cw, TITG_H, 6);

                RowTextC(gfx, lv == 0 ? "自动" : Achievements.GradeRoman(lv), _pFontCjkLabel,
                    !can ? brDim : FlatTabInk(on),
                    cx + cw / 2, TITG_TOP + TITG_H / 2 + LABEL_TEXT_DY);
            }
        }

                static int HitTestTitleChip(int x, int y, Achievements.Def d)
        {
            if (!TitleChipsShown(d)) return -1;
            if (y < TITG_TOP || y >= TITG_TOP + TITG_H) return -1;
            int n = TitGChipCount(d);
            for (int i = 0; i < n; i++)
            {
                int cx = TitGChipX(i, n);
                if (x >= cx && x < cx + TitGChipW(i)) return TitGChipGrade(i, d);
            }
            return -1;
        }

                static List<Challenge> CurChallenges
        {
            get { ActiveCatIdx(); return TaskConfig.Group(_catKey); }
        }

                static int SelIdx(List<Challenge> list)
            => list.Count == 0 ? -1 : Math.Min(Math.Max(_selIdx, 0), list.Count - 1);

                static int CatWFor(int n)
        {
            if (n <= 0) return CAT_W;
            int avail = CONTENT_X + CONTENT_W - CATROW_X;
            return Math.Min(CAT_W, Math.Max((avail - (n - 1) * CAT_GAP) / n, CAT_MIN_W));
        }

                static int CatColsFor(int n)
        {
            if (n <= 0) return 0;
            int avail = CONTENT_X + CONTENT_W - CATROW_X;
            int k = (avail + CAT_GAP) / (CAT_MIN_W + CAT_GAP);
            if (k < 1) k = 1;
            return Math.Min(k, n);
        }

        static int HitCatAt(int x, int y, int n)
        {
            if (y < CATROW_Y || y >= CATROW_Y + CAT_H) return -1;
            int cols = CatColsFor(n), w = CatWFor(n);
            for (int i = 0; i < cols; i++)
            {
                int cx = CATROW_X + i * (w + CAT_GAP);
                if (x >= cx && x < cx + w) return i;
            }
            return -1;
        }

        static int CatW() => CatWFor(Cats.Count);
        static int CatCols() => CatColsFor(Cats.Count);
        static int HitTestCat(int x, int y) => HitCatAt(x, y, Cats.Count);

                static int HitTestRow(int x, int y)
        {
            if (x < CK_X || x >= CK_X + CK_W) return -1;
            if (y < CK_TOP) return -1;
            int i = (y - CK_TOP) / CK_PITCH;
            if (i < 0 || i >= Math.Min(CurChallenges.Count, CK_MAX)) return -1;
            return i;
        }

                static int HitTestGoBtn(int x, int y)
            => (x >= DETB_X && x < DETB_X + DETB_W && y >= DETB_Y && y < DETB_Y + DETB_H) ? 0 : -1;

                static int HitTestDiffBtn(int x, int y)
        {
            if (_diffBarY < 0) return -1;
            if (y < _diffBarY - DIFFB_H / 2 || y >= _diffBarY + DIFFB_H / 2) return -1;

            var list = CurChallenges;
            int sel = SelIdx(list);
            if (sel < 0) return -1;

            var choices = list[sel].DiffChoices();
            if (choices.Count == 0) return -1;

            int bx = DET_X + DET_W - (choices.Count * DIFFB_W + (choices.Count - 1) * DIFFB_GAP);
            for (int i = 0; i < choices.Count; i++)
            {
                int px = bx + i * (DIFFB_W + DIFFB_GAP);
                if (x >= px && x < px + DIFFB_W) return i;
            }
            return -1;
        }

                static string StatusOf(Challenge c)
        {
            if (!TaskConfig.Unlocked(c))
            {

                if (!TaskConfig.RatingOk(c)) return $"需评级 {TaskConfig.FmtRating(c.MinRating)}";
                return "未解锁";
            }

            var choices = c.DiffChoices();
            if (choices.Count > 0)
            {
                int done = 0;
                foreach (var d in choices)
                    if (TaskProgress.Cleared(TaskConfig.ProgKey(c, d))) done++;
                if (done > 0) return $"已过 {done}/{choices.Count}";

                var cur = TaskProgress.Get(TaskConfig.ProgKey(c));
                return cur == null || string.IsNullOrEmpty(cur.Grade) ? "" : "最好 " + cur.Grade;
            }

            var r = TaskProgress.Get(c.Key);
            if (r == null) return "";
            string g = string.IsNullOrEmpty(r.Grade) ? "" : " " + r.Grade;
            return r.Cleared ? "已过" + g : "最好" + g;
        }

        static void RenderTask(IntPtr gfx)
        {
            RowTextL(gfx, "课题", _pFontCjkPage, _pBrWhite, CONTENT_X + 32, CONTENT_Y + 36);

            int cols = CatCols(), cw = CatW(), active = ActiveCatIdx();
            var cs = Cats;

            for (int i = 0; i < cols; i++)
            {
                bool on = i == active;
                bool hov = i == _hoverCat;
                int cx = CATROW_X + i * (cw + CAT_GAP);

                FlatTab(gfx, cx, CATROW_Y, cw, CAT_H, 8, on, hov);

                RowTextC(gfx, Clip(CatLabelAt(cs, i), cw), _pFontCjkLabel, FlatTabInk(on),
                    cx + cw / 2, CATROW_Y + CAT_H / 2 + LABEL_TEXT_DY);
            }

            if (cs.Count > cols)
            {
                RowTextL(gfx, $"+{cs.Count - cols}", _pFontCjkLabel,
                    Brush(unchecked((int)0xFF9AA3B8)), CATROW_X + cols * (cw + CAT_GAP), CATROW_Y + CAT_H / 2);
                if (!_catOverflowLogged)
                {
                    _catOverflowLogged = true;
                    Plugin.Logger.LogWarning($"[Debug] 分类有 {cs.Count} 个，这一排只画得下 {cols} 个"
                        + " —— 多出来的分类在游戏里点不到，请删掉或合并几个");
                }
            }
            else _catOverflowLogged = false;

            RenderChallengeList(gfx);
        }

                static string CatLabelAt(List<TaskConfig.CatDef> cs, int i)
            => i >= 0 && i < cs.Count && cs[i] != null ? cs[i].Label : "";

                static string Clip(string s, int w)
        {
            if (string.IsNullOrEmpty(s)) return s;
            int room = w - 16;
            if (room <= 0) return "";
            int px = 0;
            for (int i = 0; i < s.Length; i++)
            {
                px += s[i] < 0x2E80 ? 8 : 14;
                if (px > room) return s.Substring(0, i) + "…";
            }
            return s;
        }

        static void RenderChallengeList(IntPtr gfx)
        {
            var brDim  = Brush(unchecked((int)0xFF9AA3B8));
            var brVal  = Brush(unchecked((int)0xFFEDF1F7));
            var brBad  = Brush(unchecked((int)0xFFFF7B7B));

            var brHdr = Brush(unchecked((int)0xFFB8DCF5));

            var list = CurChallenges;
            if (list.Count == 0)
            {
                RowTextL(gfx, "还没有课题 —— 编辑 " + TaskConfig.ConfigFile, _pFontCjkLabel, brBad, CK_X, CK_TOP);
                RowTextL(gfx, "（把这个文件删掉、重开游戏，会重新写一份默认题库）", _pFontCjkLabel, brDim, CK_X, CK_TOP + 30);
                return;
            }

            int sel = SelIdx(list);

            int shown = Math.Min(list.Count, CK_MAX);
            for (int i = 0; i < shown; i++)
            {
                var c = list[i];
                int ry = CK_TOP + i * CK_PITCH;
                bool isSel = i == sel;
                bool ok = TaskConfig.Unlocked(c);
                int rx = CK_X + ShakeOffset(i);

                string rowSt = isSel ? "active" : (i == _hoverRow ? "hover" : "normal");
                _rowWipes.TryGetValue(c.Key, out var wp);
                if (wp != null && wp.To != rowSt)
                {
                    wp.To = rowSt;
                    if (wp.From == wp.To) { _rowWipes.Remove(c.Key); wp = null; }
                }
                if (wp != null) DrawRowWipe(gfx, c, wp, rx, ry, CK_W, CK_PITCH - 6);
                else DrawRowPlate(gfx, c, isSel, i == _hoverRow, rx, ry, CK_W, CK_PITCH - 6);

                IntPtr nameBr = isSel ? _pBrActiveText
                                       : (!ok ? brDim : (c.N >= HARD_FROM ? brVal : _pBrActiveText));

                bool masked = Masked(c);
                if (masked) nameBr = brVal;

                float cy = ry + (CK_PITCH - 6) / 2f;

                if (DrawNameInCode(c))
                    RowTextL(gfx, masked ? Garble("fin.row", 2) : c.Name,
                        _pFontCjkLabel, nameBr, rx + NAME_DX, cy);

                var rec = TaskProgress.Get(TaskConfig.ProgKey(c));
                string grade = rec == null || rec.Grade == null ? "" : rec.Grade.Trim().ToUpperInvariant();

                if (TaskProgress.GradeRank(grade) > 0)
                {
                    IntPtr rk = Tex($"task/row_rank_{grade}.png");
                    if (rk != IntPtr.Zero) Draw(gfx, rk, rx, ry, CK_W, CK_PITCH - 6);
                }

                if (Season.IsSeasonFinal(c))
                {
                    IntPtr tag = Tex("task/s0tag.png");
                    if (tag != IntPtr.Zero)
                    {
                        int tx = rx + CK_W - S0TAG_W - S0TAG_PAD;
                        int ty = ry + (CK_PITCH - 6) - S0TAG_H - S0TAG_PAD + S0TAG_ROW_DY;

                        _s0TagVisTarget = S0TagNear(tx, ty) ? 0f : 1f;
                        if (_s0TagVis >= 0.999f) Draw(gfx, tag, tx, ty, S0TAG_W, S0TAG_H);
                        else if (_s0TagVis > 0.001f) DrawCropRight(gfx, tag, tx, ty, S0TAG_W, S0TAG_H, _s0TagVis);
                    }
                }
            }
            if (list.Count > CK_MAX)
                RowTextL(gfx, $"… 还有 {list.Count - CK_MAX} 个没显示", _pFontCjkLabel, brDim,
                    CK_X + 18, CK_TOP + shown * CK_PITCH + 10);

            {
                string err = TaskRunner.LastError;
                if (!string.IsNullOrEmpty(err))
                    RowTextL(gfx, "上次开考被拒：" + err, _pFontCjkLabel, brBad, CK_X, CK_FOOT);
                else
                {
                    string last = TaskConfig.LastResultLine();
                    if (!string.IsNullOrEmpty(last)) RowTextL(gfx, last, _pFontCjkLabel, brDim, CK_X, CK_FOOT);
                }
            }

            if (sel >= 0) RenderChallengeDetail(gfx, list[sel], brDim, brVal, brBad, brHdr);
        }

                static int DrawDiffBar(IntPtr gfx, Challenge c, int y)
        {
            var choices = c.DiffChoices();
            if (choices.Count == 0) { _diffBarY = -1; return 0; }
            _diffBarY = y;

            string cur = TaskConfig.DiffFor(c);
            int bx = DET_X + DET_W - (choices.Count * DIFFB_W + (choices.Count - 1) * DIFFB_GAP);
            int by = y - DIFFB_H / 2;

            for (int i = 0; i < choices.Count; i++)
            {
                string d = choices[i];
                int px = bx + i * (DIFFB_W + DIFFB_GAP);
                bool on = string.Equals(d, cur, StringComparison.OrdinalIgnoreCase);
                bool hov = _hoverDiffBtn == i;

                string st = on ? "active" : (hov ? "hover" : "normal");

                if (DrawPlate(gfx, "diff/" + TaskConfig.DiffKey(d), st, px, by, DIFFB_W, DIFFB_H))
                    continue;

                GdipFillRectangleI(gfx, on ? _pBrTabOn : (hov ? _pBrTabHover : _pBrTabOff),
                    px, by, DIFFB_W, DIFFB_H);
                RowTextC(gfx, TaskConfig.DiffShort(d), _pFontCjkLabel,
                    on ? _pBrActiveText : Brush(B30CardExporter.GetB10DiffColor(d)),
                    px + DIFFB_W / 2, y + LABEL_TEXT_DY);
            }
            return DIFFB_PAD;
        }

        static void RenderChallengeDetail(IntPtr gfx, Challenge c,
            IntPtr brDim, IntPtr brVal, IntPtr brBad, IntPtr brHdr)
        {
            int x = DET_X;
            int y = PAGE_TOP + 4;

            bool ok = TaskConfig.Unlocked(c);
            string why = TaskConfig.WhyNotRunnable(c);

            bool masked = Masked(c);

            int seen = masked ? TaskProgress.Seen(TaskConfig.ProgKey(c)) : 0;

            string nameText = masked ? Garble("fin.det.name", 2) : c.Name;
            RowTextL(gfx, nameText, _pFontCjkPage, _pBrWhite, x, y);

            if (Season.IsSeasonFinal(c))
            {
                IntPtr tag = Tex("task/s0tag.png");
                if (tag != IntPtr.Zero)
                {
                    int nameW = 0;
                    if (_pFontCjkPage != IntPtr.Zero)
                    {
                        var layout = new GPRECTF { X = 0, Y = 0, Width = 10000f, Height = 10000f };
                        if (GdipMeasureString(gfx, nameText, -1, _pFontCjkPage, ref layout,
                                IntPtr.Zero, out var box, out _, out _) == 0)
                            nameW = (int)Math.Ceiling(box.Width);
                    }
                    Draw(gfx, tag, x + nameW + S0TAG_DET_GAP, y - S0TAG_H / 2 + S0TAG_DET_DY,
                        S0TAG_W, S0TAG_H);
                }
            }
            y += 42;

            string note = masked ? Garble("fin.det.note1", 7) + "，" + Garble("fin.det.note2", 7) : c.Note;
            if (note.Length > 0) { RowTextL(gfx, note, _pFontCjkLabel, brVal, x, y); y += 30; }

            if (ok && why.Length > 0)
            {
                RowTextL(gfx, "题目有问题：" + why, _pFontCjkLabel, brBad, x, y); y += 30;
            }

            y += 10;
            RowTextL(gfx, "挑战曲目", _pFontCjkLabel, brHdr, x, y);

            y += 30 + DrawDiffBar(gfx, c, y);

            string picked = TaskConfig.DiffFor(c);

            if (c.IsPool)
            {

                string poolHint = c.PoolDiff == ConstTable.AnyDiff
                    ? $"{c.PoolMin}-{c.PoolMax}"
                    : $"{TaskConfig.DiffShort(c.PoolDiff)} {c.PoolMin}-{c.PoolMax}";
                for (int i = 0; i < c.Count; i++)
                {
                    RowTextL(gfx, $"{i + 1}. ？？？", _pFontCjkLabel, brDim, x + 14, y);

                    RowTextR(gfx, poolHint, _pFontCjkLabel, brDim, x + DET_W, y);
                    y += 26;
                }
            }
            else
            {

                int rows = Math.Min(c.Songs.Count, c.Count);

                if (masked && seen + 1 < rows) rows = seen + 1;

                int nx = x + DET_W;
                int dx = nx - SONGNUM_W - SONGNUM_GAP;

                for (int i = 0; i < rows; i++)
                {
                    var s = c.Songs[i];
                    string id = s.Id.ToString();
                    string diff = picked.Length > 0 ? picked : s.Diff;

                    bool shown = !masked || i < seen;

                    if (shown)
                    {
                        string title = SongInfoCache.GetName(id);
                        if (string.IsNullOrEmpty(title)) title = $"(未知曲目 {id})";
                        RowTextL(gfx, $"{i + 1}. {title}", _pFontCjkLabel, brVal, x + 14, y);

                        IntPtr dc = Brush(B30CardExporter.GetB10DiffColor(diff));
                        RowTextR(gfx, TaskConfig.DiffShort(diff), _pFontRowNum, dc, dx, y);
                        float constSize = ConstTable.TextSizeScale();
                        if (!rowConstFontDictionary.TryGetValue(constSize, out var _pFontConst))
                        {
                            rowConstFontDictionary[constSize] = _pFontConst = RegisterRowFont(_pRowFamSrc, constSize);
                        }
                        RowTextR(gfx, ConstTable.Text(ConstTable.Get(id, diff)), _pFontConst, dc, nx, y);
                    }
                    else
                    {
                        RowTextL(gfx, $"{i + 1}. {Garble("fin.song" + i, 6)}", _pFontCjkLabel, brDim, x + 14, y);

                        RowTextR(gfx, "？？？", _pFontRowNum, brDim, dx, y);
                        RowTextR(gfx, Garble("fin.songnum" + i, 2, digits: true),
                            _pFontRowNum, brDim, nx, y);
                    }
                    y += 26;
                }

                if (c.Songs.Count < c.Count)
                {
                    RowTextL(gfx, $"（曲单只有 {c.Songs.Count} 首，打不满 {c.Count} 首）",
                        _pFontCjkLabel, brBad, x + 14, y);
                    y += 26;
                }
            }

            y += 18;
            string recHdr = c.Kind == "rank" ? "该段位最佳成绩" : "该主题最佳成绩";
            string pk = TaskConfig.ProgKey(c);
            if (c.Switchable && picked.Length > 0)
                recHdr += $"（{TaskConfig.DiffShort(picked)}）";
            RowTextL(gfx, recHdr, _pFontCjkLabel, brHdr, x, y); y += 30;

            var r = TaskProgress.Get(pk);
            if (r == null)
            {
                RowTextL(gfx, "还没打过", _pFontCjkLabel, brDim, x + 14, y);
            }
            else
            {
                RowTextL(gfx, r.Cleared ? "已通过" : "未通过", _pFontCjkLabel,
                    r.Cleared ? brHdr : brDim, x + 14, y); y += 26;

                string sub = "";

                if (!string.IsNullOrEmpty(r.Grade)) sub = "最好 " + TaskProgress.GradeWord(r.Grade);
                if (r.Avg > 0) sub += (sub.Length > 0 ? " · " : "") + $"均分 {r.Avg:N0}";
                if (!string.IsNullOrEmpty(r.At)) sub += (sub.Length > 0 ? " · " : "") + r.At;
                if (sub.Length > 0) RowTextL(gfx, sub, _pFontCjkLabel, brDim, x + 14, y);
            }

            bool running = TaskRunner.Running;
            bool lit = _hoverGoBtn == 0;
            string goSt = lit ? "hover" : "normal";

            int gx = DETB_X + ShakeOffset(SHAKE_GO);
            IntPtr plate  = TaskPlate("go", goSt);
            IntPtr plateF = TaskPlate("go", goSt, THEME_FIN);
            IntPtr plateI = TaskPlate("go", goSt, THEME_INS);
            if (plate != IntPtr.Zero) DrawNavPlate(gfx, plateI, plateF, plate, gx, DETB_Y, DETB_W, DETB_H);
            else GdipFillRectangleI(gfx, lit ? _pBrTabHover : _pBrTabOff, gx, DETB_Y, DETB_W, DETB_H);

            RowTextC(gfx, running ? "中止课题" : "开始", _pFontCjkPage, _pBrWhite,
                gx + DETB_W / 2, DETB_Y + DETB_H / 2 + PAGE_TEXT_DY);

            string hint = running ? (TaskRunner.Current != null ? "正在打：" + TaskRunner.Current.Name : "进行中…")
                : !ok ? "未解锁"
                : TaskConfig.WhyNotRunnable(c).Length > 0 ? "这题打不了"
                : "开考后面板会收起";
            RowTextL(gfx, hint, _pFontCjkLabel, (running || ok) ? brDim : brBad,
                DETB_X + DETB_W + 20, DETB_Y + DETB_H / 2);
        }

                static Challenge FinalChallenge()
        {
            var t = TaskConfig.Tiers;
            if (t != null) foreach (var c in t) if (c != null && c.Final) return c;
            var m = TaskConfig.Themes;
            if (m != null) foreach (var c in m) if (c != null && c.Final) return c;
            return null;
        }

        const int THEMEFADE_MS = 420;
        const int THEME_GLITCH_MS = 55;
        const int THEME_SLICES = 4;

        static readonly float[] _thFrom = new float[3];
        static readonly DateTime[] _thAt = { DateTime.MinValue, DateTime.MinValue, DateTime.MinValue };
        static readonly int[] _thTo = new int[3];
        static readonly int[] _thRoll = { -1, -1, -1 };
        static readonly int[][] _thSliceY  = { null, new int[THEME_SLICES], new int[THEME_SLICES] };
        static readonly int[][] _thSliceH  = { null, new int[THEME_SLICES], new int[THEME_SLICES] };
        static readonly int[][] _thSliceDX = { null, new int[THEME_SLICES], new int[THEME_SLICES] };
        static readonly string[] _thBase =
            { null, "base/panel_fin_base.png", "base/panel_ins_base.png" };

                static bool FinSelected()
        {
            if (!OnTaskPage) return false;
            var list = CurChallenges;
            int sel = SelIdx(list);
            return sel >= 0 && list[sel].Final;
        }

                static int ThemeWant(int theme)
        {
            if (theme == THEME_NORMAL) return 1;

            if (!OnTaskPage) return 0;
            var list = CurChallenges;
            int sel = SelIdx(list);
            if (sel < 0) return 0;

            if (theme == THEME_INS) return 0;
            return list[sel].Final ? 1 : 0;
        }

                static float ThemeMix(int theme)
        {
            int want = ThemeWant(theme);
            if (want != _thTo[theme])
            {
                _thFrom[theme] = ThemeAt(theme, DateTime.Now);
                _thAt[theme] = DateTime.Now;
                _thTo[theme] = want;
            }
            return ThemeAt(theme, DateTime.Now);
        }

                static float FinBgMix() => ThemeMix(THEME_FIN);

                static float InsBgMix() => ThemeMix(THEME_INS);

                static float ThemeAt(int theme, DateTime now)
        {
            if (_thAt[theme] == DateTime.MinValue) return _thTo[theme];
            double t = (now - _thAt[theme]).TotalMilliseconds / THEMEFADE_MS;
            if (t >= 1.0) return _thTo[theme];
            if (t <= 0.0) return _thFrom[theme];
            double e = 1.0 - Math.Pow(1.0 - t, 3.0);
            return (float)(_thFrom[theme] + (_thTo[theme] - _thFrom[theme]) * e);
        }

                static void DrawThemeBackground(IntPtr gfx, int theme)
        {

            if (theme == THEME_NORMAL) return;

            float mix = ThemeMix(theme);
            if (mix <= 0f) return;

            IntPtr img = Tex(_thBase[theme]);
            if (img == IntPtr.Zero)
            {

                if (theme == THEME_FIN)
                    DrawAlpha(gfx, Tex("base/panel_base.png"), 0, 0, PW, PH, 1f - mix);
                return;
            }

            float g = 1f - Math.Abs(mix - 0.5f) * 2f;
            if (g > 0.15f)
            {
                int roll = (int)(DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond / THEME_GLITCH_MS);
                if (roll != _thRoll[theme]) { _thRoll[theme] = roll; RollThemeSlices(theme, roll); }
                for (int i = 0; i < THEME_SLICES; i++)
                {
                    int y = _thSliceY[theme][i];
                    DrawSlice(gfx, img, _thSliceDX[theme][i], y, _thSliceH[theme][i], mix);
                }
            }

            DrawAlpha(gfx, img, 0, 0, PW, PH, mix);
        }

        static void RollThemeSlices(int theme, int roll)
        {
            for (int i = 0; i < THEME_SLICES; i++)
            {
                int h1 = Mix("fy", roll * 131 + i * 17);
                int h2 = Mix("fh", roll * 131 + i * 17);
                int h3 = Mix("fx", roll * 131 + i * 17);
                _thSliceH[theme][i]  = 6 + h2 % 40;
                _thSliceY[theme][i]  = h1 % Math.Max(1, PH - _thSliceH[theme][i]);
                _thSliceDX[theme][i] = (h3 % 25) - 12;
            }
        }

                static void DrawSlice(IntPtr gfx, IntPtr img, int dx, int y, int h, float alpha)
        {
            var cm = new ColorMatrix { m00 = 1f, m11 = 1f, m22 = 1f, m33 = alpha, m44 = 1f };
            if (GdipCreateImageAttributes(out var attr) != 0) return;
            GdipSetImageAttributesColorMatrix(attr, 0, true, ref cm, IntPtr.Zero, 0);
            GdipDrawImageRectRectI(gfx, img, dx, y, PW, h, 0, y, PW, h, 2, attr, IntPtr.Zero, IntPtr.Zero);
            GdipDisposeImageAttributes(attr);
        }

                static void DrawAlpha(IntPtr gfx, IntPtr img, int x, int y, int w, int h, float alpha)
        {
            if (img == IntPtr.Zero || alpha <= 0f) return;
            if (alpha > 1f) alpha = 1f;
            var cm = new ColorMatrix { m00 = 1f, m11 = 1f, m22 = 1f, m33 = alpha, m44 = 1f };
            if (GdipCreateImageAttributes(out var attr) != 0) return;
            GdipSetImageAttributesColorMatrix(attr, 0, true, ref cm, IntPtr.Zero, 0);
            GdipDrawImageRectRectI(gfx, img, x, y, w, h, 0, 0, PW, PH, 2, attr, IntPtr.Zero, IntPtr.Zero);
            GdipDisposeImageAttributes(attr);
        }

        static volatile int _botBase, _botFin, _botIns;
                static volatile int _botShow;

                static void PublishBottom()
        {

            _botShow = (_visible && Settings.BottomLayerOn) ? 1 : 0;

            float f = FinBgMix(), i = InsBgMix();
            _botBase = 255;
            _botFin  = Q(f);
            _botIns  = Q(i);
        }

                static int Q(float v)
        {
            int n = (int)(v * 255f + 0.5f);
            return n < 0 ? 0 : (n > 255 ? 255 : n);
        }

                public static bool ParticlesOn = true;

                public static class BottomLayer
        {

            const int ART_W = 1920, ART_H = 1000;

            const int ENV_MS = 200;

            const int REVEAL_MS = 520;
            const float REVEAL_BAND = 0.55f;

            const int RESTACK_MS = 2000;

            static readonly string[] FILES =
                { "base/bottom.png", "base/bottom_fin.png", "base/bottom_ins.png" };

            static Thread _thread = null;
            static IntPtr _hWnd = IntPtr.Zero;
            static IntPtr _dc = IntPtr.Zero, _dib = IntPtr.Zero, _bits = IntPtr.Zero, _dibOld = IntPtr.Zero;
            static IntPtr _token = IntPtr.Zero;
            static WndProcDelegate _wndProc;
            static IntPtr _game = IntPtr.Zero;
            static int _dibW, _dibH;

            static int _screenX, _screenY, _w, _h;
            static bool _shown;

            static readonly IntPtr[] _cDib  = { IntPtr.Zero, IntPtr.Zero, IntPtr.Zero };
            static readonly IntPtr[] _cDc   = { IntPtr.Zero, IntPtr.Zero, IntPtr.Zero };
            static readonly IntPtr[] _cOld  = { IntPtr.Zero, IntPtr.Zero, IntPtr.Zero };
            static readonly IntPtr[] _cBits = { IntPtr.Zero, IntPtr.Zero, IntPtr.Zero };
            static readonly bool[] _cTried = { false, false, false };

            static float _lastB = -1f, _lastF = -1f, _lastI = -1f, _lastR = -1f;
            static bool _dirty = true;

            static bool _stacked;
            static bool _panelWasVisible;
            static IntPtr _panelAt = IntPtr.Zero;
            static long _stackAt;

                        public static void Start()
            {
                if (_thread != null) return;
                var t = new Thread(() =>
                {
                    try { Run(); }
                    catch (Exception ex) { Plugin.Logger.LogError($"[屏底] {ex}"); }
                });
                t.IsBackground = true;
#pragma warning disable CA1416
                t.SetApartmentState(ApartmentState.STA);
#pragma warning restore CA1416
                t.Start();
            }

            static void Run()
            {
                var input = new GdiplusStartupInput { GdiplusVersion = 1 };
                if (GdiplusStartup(out _token, ref input, IntPtr.Zero) != 0)
                {
                    Plugin.Logger.LogError("[屏底] GdiplusStartup 失败");
                    return;
                }

                _wndProc = WndProcImpl;
                var wc = new WNDCLASS
                {
                    lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                    lpszClassName = "InFalsusRatingBottom"
                };
                RegisterClassW(ref wc);

                uint exStyle = WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                _hWnd = CreateWindowExW(exStyle, "InFalsusRatingBottom", "Bottom", WS_POPUP,
                    0, 0, ART_W, ART_H, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                if (_hWnd == IntPtr.Zero) { Plugin.Logger.LogError("[屏底] CreateWindowEx 失败"); return; }

                _game = FindGameWindow();
                if (_game != IntPtr.Zero) SetWindowLongPtrW(_hWnd, GWLP_HWNDPARENT, _game);

                Plugin.Logger.LogInfo("[屏底] 就绪（铺满客户区整宽、贴下沿，z 序在游戏与面板之间）");

                var updater = new Thread(() =>
                {
                    while (true)
                    {
                        try { Tick(); } catch { }
                        Thread.Sleep(ANIM_FRAME_MS);
                    }
                });
                updater.IsBackground = true;
                updater.Start();

                MSG msg;
                while (GetMessageW(out msg, IntPtr.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref msg);
                    DispatchMessageW(ref msg);
                }
            }

            static void Tick()
            {
                if (_hWnd == IntPtr.Zero) return;
                long now = Environment.TickCount64;

                float eb = Env(0, PlayerPanel._botBase / 255f, ENV_MS, now);
                float ef = Env(1, PlayerPanel._botFin  / 255f, ENV_MS, now);
                float ei = Env(2, PlayerPanel._botIns  / 255f, ENV_MS, now);

                float rv = Env(3, PlayerPanel._botShow != 0 ? 1f : 0f, REVEAL_MS, now);

                bool particles = PlayerPanel.ParticlesOn && ei > 0f;

                if (rv <= 0f || (eb <= 0f && ef <= 0f && ei <= 0f))
                {
                    if (_shown) { ShowWindow(_hWnd, 0 ); _shown = false; }
                    return;
                }

                TrackGameWindow();
                if (_w < 2 || _h < 2) return;

                if (_w != _dibW || _h != _dibH)
                {
                    ResetCaches();
                    EnsureDib(_w, _h);
                    _dibW = _w; _dibH = _h;
                    for (int i = 0; i < BT_N; i++) _btGen[i] = long.MinValue;
                    _dirty = true;
                }

                ReStack(now);

                if (_shown && !_dirty && !particles
                    && eb == _lastB && ef == _lastF && ei == _lastI && rv == _lastR) return;

                PaintGradient((int)(eb * 255f + 0.5f), (int)(ef * 255f + 0.5f), (int)(ei * 255f + 0.5f));
                if (particles) DrawParticles(_w, _h, ei, now);

                ApplyReveal(rv);
                Commit();

                _lastB = eb; _lastF = ef; _lastI = ei; _lastR = rv;
                _dirty = false;

                if (!_shown)
                {
                    ShowWindow(_hWnd, SW_SHOWNOACTIVATE);
                    _shown = true;
                    Plugin.Logger.LogInfo($"[屏底] 显示（{_w}×{_h}）");
                }
            }

                        static float Env(int k, float want, int ms, long now)
            {
                if (want != _envTo[k])
                {
                    _envFrom[k] = _env[k];
                    _envTo[k] = want;
                    _envAt[k] = now;
                }
                float t = (now - _envAt[k]) / (float)ms;
                if (t >= 1f) { _env[k] = want; return want; }
                if (t <= 0f) return _envFrom[k];
                float u = 1f - t;
                _env[k] = _envFrom[k] + (want - _envFrom[k]) * (1f - u * u * u);
                return _env[k];
            }

            static readonly float[] _env = new float[4];
            static readonly float[] _envFrom = new float[4];
            static readonly float[] _envTo = { -2f, -2f, -2f, -2f };
            static readonly long[] _envAt = new long[4];

                        static void ApplyReveal(float r)
            {
                if (_bits == IntPtr.Zero) return;
                if (r >= 1f) return;

                int band = (int)(_h * REVEAL_BAND);
                if (band < 1) band = 1;

                float front = (_h + band) * (1f - r);

                unsafe
                {
                    var px = new Span<uint>((uint*)_bits, _w * _h);
                    for (int y = 0; y < _h; y++)
                    {
                        int m = (int)((y + band - front) * 255f / band + 0.5f);
                        var row = px.Slice(y * _w, _w);
                        if (m >= 255) continue;
                        if (m <= 0) { row.Clear(); continue; }
                        uint mu = (uint)m;
                        for (int x = 0; x < row.Length; x++)
                        {
                            uint p = row[x];
                            uint lo = (((p & 0x00FF00FF) * mu) >> 8) & 0x00FF00FF;
                            uint hi = ((((p >> 8) & 0x00FF00FF) * mu) >> 8) & 0x00FF00FF;
                            row[x] = lo | (hi << 8);
                        }
                    }
                }
            }

            static void TrackGameWindow()
            {
                if (_game != IntPtr.Zero && !IsWindow(_game)) _game = IntPtr.Zero;
                if (_game == IntPtr.Zero)
                {
                    _game = FindGameWindow();
                    if (_game != IntPtr.Zero && _hWnd != IntPtr.Zero)
                        SetWindowLongPtrW(_hWnd, GWLP_HWNDPARENT, _game);
                }
                if (_game == IntPtr.Zero) return;

                var pt = new POINT { x = 0, y = 0 };
                if (!ClientToScreen(_game, ref pt)) return;
                RECT rc;
                if (!GetClientRect(_game, out rc)) return;

                int cw = rc.right - rc.left, ch = rc.bottom - rc.top;
                if (cw < 2 || ch < 2) return;

                int h = (int)((long)ART_H * cw / ART_W);
                if (h > ch) h = ch;
                if (h < 2) h = 2;

                _w = cw; _h = h;
                _screenX = pt.x;
                _screenY = pt.y + ch - h;
            }

                        static void ReStack(long now)
            {
                IntPtr panel = PlayerPanel._hWnd;
                bool panelVis = PlayerPanel._visible;
                if (_stacked && panelVis == _panelWasVisible && panel == _panelAt
                    && now - _stackAt < RESTACK_MS) return;
                _stacked = true;
                _panelWasVisible = panelVis;
                _panelAt = panel;
                _stackAt = now;

                if (panel != IntPtr.Zero && IsWindow(panel))
                    SetWindowPos(_hWnd, panel, 0, 0, 0, 0,
                        SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);

            }

            static void EnsureDib(int w, int h)
            {
                FreeDib(ref _dib, ref _dc, ref _dibOld, ref _bits);
                MakeDib(w, h, out _dib, out _dc, out _dibOld, out _bits);
            }

                        static void MakeDib(int w, int h, out IntPtr dib, out IntPtr dc, out IntPtr oldBmp, out IntPtr bits)
            {
                var sdc = GetDC(IntPtr.Zero);
                dc = CreateCompatibleDC(sdc);

                var bmi = new BITMAPINFO();
                bmi.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER));
                bmi.bmiHeader.biWidth = w;
                bmi.bmiHeader.biHeight = -h;
                bmi.bmiHeader.biPlanes = 1;
                bmi.bmiHeader.biBitCount = 32;
                bmi.bmiHeader.biCompression = 0;

                dib = CreateDIBSection(sdc, ref bmi, 0, out bits, IntPtr.Zero, 0);
                ReleaseDC(IntPtr.Zero, sdc);

                oldBmp = SelectObject(dc, dib);
            }

            static void FreeDib(ref IntPtr dib, ref IntPtr dc, ref IntPtr oldBmp, ref IntPtr bits)
            {
                if (dib != IntPtr.Zero)
                {
                    if (oldBmp != IntPtr.Zero && dc != IntPtr.Zero) SelectObject(dc, oldBmp);
                    DeleteObject(dib);
                }

                if (dc != IntPtr.Zero) DeleteDC(dc);
                dib = oldBmp = bits = IntPtr.Zero;
                dc = IntPtr.Zero;
            }

            static void Commit()
            {
                if (_hWnd == IntPtr.Zero || _dc == IntPtr.Zero) return;
                var ptDst = new POINT { x = _screenX, y = _screenY };
                var sz = new SIZE { cx = _w, cy = _h };
                var ptSrc = new POINT { x = 0, y = 0 };
                var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
                UpdateLayeredWindow(_hWnd, IntPtr.Zero, ref ptDst, ref sz, _dc, ref ptSrc, 0, ref blend, ULW_ALPHA);
            }

                        static void ResetCaches()
            {
                for (int i = 0; i < 3; i++)
                {
                    FreeDib(ref _cDib[i], ref _cDc[i], ref _cOld[i], ref _cBits[i]);
                    _cTried[i] = false;
                }
            }

                        static bool EnsureCache(int theme)
            {
                if (_cDib[theme] != IntPtr.Zero) return true;
                if (_cTried[theme]) return false;
                _cTried[theme] = true;

                string full = Path.Combine(GuiDir, FILES[theme]);
                if (!File.Exists(full))
                {
                    Plugin.Logger.LogWarning($"[屏底] 缺 {FILES[theme]}，这一套不画");
                    return false;
                }
                if (GdipCreateBitmapFromFile(full, out IntPtr img) != 0 || img == IntPtr.Zero)
                {
                    Plugin.Logger.LogWarning($"[屏底] {FILES[theme]} 读不出，这一套不画");
                    return false;
                }

                MakeDib(_w, _h, out _cDib[theme], out _cDc[theme], out _cOld[theme], out _cBits[theme]);

                bool ok = false;
                if (_cBits[theme] != IntPtr.Zero
                    && GdipCreateBitmapFromScan0(_w, _h, _w * 4, PixelFormat32bppPARGB,
                                                 _cBits[theme], out IntPtr bmp) == 0)
                {
                    if (GdipGetImageGraphicsContext(bmp, out IntPtr gfx) == 0)
                    {
                        GdipSetInterpolationMode(gfx, 6);
                        GdipDrawImageRectI(gfx, img, 0, 0, _w, _h);
                        GdipDeleteGraphics(gfx);
                        ok = true;
                    }
                    GdipDisposeImage(bmp);
                }
                GdipDisposeImage(img);

                if (!ok)
                {
                    FreeDib(ref _cDib[theme], ref _cDc[theme], ref _cOld[theme], ref _cBits[theme]);
                    Plugin.Logger.LogWarning($"[屏底] {FILES[theme]} 处理失败，这一套不画");
                    return false;
                }
                _dirty = true;
                return true;
            }

                        static void PaintGradient(int mb, int mf, int mi)
            {
                if (_dc == IntPtr.Zero) return;

                bool bOk = mb > 0 && EnsureCache(THEME_NORMAL);
                bool fOk = mf > 0 && EnsureCache(THEME_FIN);
                bool iOk = mi > 0 && EnsureCache(THEME_INS);

                if (bOk && mb >= 255)
                {

                    BitBlt(_dc, 0, 0, _w, _h, _cDc[THEME_NORMAL], 0, 0, SRCCOPY);
                }
                else
                {

                    Clear();
                    if (bOk) Blend(_cDc[THEME_NORMAL], mb);
                }
                if (fOk) Blend(_cDc[THEME_FIN], mf);
                if (iOk) Blend(_cDc[THEME_INS], mi);
            }

                        static void Blend(IntPtr dcSrc, int m)
            {
                var bf = new BLENDFUNCTION
                {
                    BlendOp = 0,
                    BlendFlags = 0,
                    SourceConstantAlpha = (byte)(m < 0 ? 0 : (m > 255 ? 255 : m)),
                    AlphaFormat = 1
                };
                AlphaBlend(_dc, 0, 0, _w, _h, dcSrc, 0, 0, _w, _h, bf);
            }

                        static void Clear()
            {
                if (_bits == IntPtr.Zero) return;
                unsafe { new Span<uint>((uint*)_bits, _w * _h).Clear(); }
            }

            const int BT_N = 56;
            const int BT_LIFE_MS = 5600;
            const int BT_TOP = 340;
            const int BT_TAIL = 90;
            const int BT_PAD = 70;
            const int BT_SIZE_MIN = 12, BT_SIZE_MAX = 58;
            const int BT_ALPHA = 150;

            static bool _btInit;
            static bool _pfSaid;
            static IntPtr _btBrush = IntPtr.Zero;
            static readonly long[] _btPhase = new long[BT_N];
            static readonly int[] _btSize = new int[BT_N];
            static readonly int[] _btLife = new int[BT_N];
            static readonly int[] _btX = new int[BT_N];
            static readonly long[] _btGen = new long[BT_N];
            static readonly int[] _btShape = new int[BT_N * 6];
            static readonly POINT[] _btPts = new POINT[3];

            static void BtInit()
            {
                _btInit = true;
                for (int i = 0; i < BT_N; i++)
                {

                    _btPhase[i] = (long)i * BT_LIFE_MS / BT_N
                                + Mix("bt.p" + i, 0) % (BT_LIFE_MS / BT_N);
                    _btSize[i] = BT_SIZE_MIN + Mix("bt.s" + i, 1) % (BT_SIZE_MAX - BT_SIZE_MIN + 1);
                    _btGen[i] = long.MinValue;

                    int szT = (_btSize[i] - BT_SIZE_MIN) * 100 / (BT_SIZE_MAX - BT_SIZE_MIN);
                    _btLife[i] = BT_LIFE_MS * (135 - szT * 65 / 100) / 100;
                    _btLife[i] = _btLife[i] * (85 + Mix("bt.l" + i, 0) % 31) / 100;

                    int a1 = Mix("bt.a" + i, 0) % 360;
                    int a2 = a1 + 60 + Mix("bt.a" + i, 1) % 121;
                    int a3 = a2 + 60 + Mix("bt.a" + i, 2) % 121;
                    int[] ang = { a1, a2, a3 };
                    for (int k = 0; k < 3; k++)
                    {
                        double rad = ang[k] * Math.PI / 180.0;
                        int r = 70 + Mix("bt.r" + i, k) % 31;
                        _btShape[i * 6 + k * 2]     = (int)Math.Round(Math.Cos(rad) * r);
                        _btShape[i * 6 + k * 2 + 1] = (int)Math.Round(Math.Sin(rad) * r);
                    }
                }
            }

                        static int BtRgb(float f, float n)
            {
                int r = 0x15, g = 0x05, b = 0x30;
                r += (int)((0x60 - r) * f);
                g += (int)((0x08 - g) * f);
                b += (int)((0x08 - b) * f);
                r += (int)((0x28 - r) * n);
                g += (int)((0x06 - g) * n);
                b += (int)((0x60 - b) * n);
                return (r << 16) | (g << 8) | b;
            }

                        static void DrawParticles(int w, int h, float env, long now)
            {
                if (_bits == IntPtr.Zero) return;
                if (GdipCreateBitmapFromScan0(w, h, w * 4, PixelFormat32bppPARGB, _bits, out IntPtr bmp) != 0
                    || bmp == IntPtr.Zero)
                {

                    if (!_pfSaid)
                    {
                        _pfSaid = true;
                        Plugin.Logger.LogError("[屏底] GDI+ 接不了这块 PARGB 画布，三角粒子不画（渐变照画）");
                    }
                    return;
                }
                if (GdipGetImageGraphicsContext(bmp, out IntPtr gfx) != 0)
                { GdipDisposeImage(bmp); return; }

                if (_btBrush == IntPtr.Zero) GdipCreateSolidFill(unchecked((int)0xFFFFFFFF), out _btBrush);
                if (_btBrush == IntPtr.Zero) { GdipDeleteGraphics(gfx); GdipDisposeImage(bmp); return; }
                if (!_btInit) BtInit();

                GdipSetSmoothingMode(gfx, 4);

                int span = h + BT_TAIL - BT_TOP;
                int rgb = BtRgb(_env[1], _env[2]);

                for (int i = 0; i < BT_N; i++)
                {
                    int life = _btLife[i];
                    long t = now + _btPhase[i];
                    long gen = t / life;

                    if (gen != _btGen[i])
                    {
                        _btGen[i] = gen;
                        _btX[i] = Mix("bt.x" + i, (int)(gen & 0xFFFF)) % (w + 2 * BT_PAD) - BT_PAD;
                    }

                    int u = (int)((t % life) * 256 / life);
                    int y = h + BT_TAIL - span * u / 256;
                    int a = (int)(BT_ALPHA * (256 - u) / 256 * env);
                    if (a < 3) continue;

                    int half = _btSize[i] / 2;
                    int cx = _btX[i];
                    for (int k = 0; k < 3; k++)
                    {
                        _btPts[k].x = cx + _btShape[i * 6 + k * 2]     * half / 100;
                        _btPts[k].y = y  + _btShape[i * 6 + k * 2 + 1] * half / 100;
                    }
                    GdipSetSolidFillColor(_btBrush, unchecked((int)((uint)(a << 24) | (uint)rgb)));
                    GdipFillPolygonI(gfx, _btBrush, _btPts, 3, 1);
                }

                GdipDeleteGraphics(gfx);
                GdipDisposeImage(bmp);
            }

            static IntPtr WndProcImpl(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
            {
                if (msg == WM_DESTROY)
                {
                    if (_btBrush != IntPtr.Zero) { GdipDeleteBrush(_btBrush); _btBrush = IntPtr.Zero; }

                    ResetCaches();
                    FreeDib(ref _dib, ref _dc, ref _dibOld, ref _bits);
                    PostQuitMessage(0);
                    return IntPtr.Zero;
                }
                return DefWindowProcW(hWnd, msg, wParam, lParam);
            }
        }

        static bool OnConfigPage => NAV[_activeNav].Key == "config";

                sealed class SetRow
        {
            public readonly string Section;
            public readonly string Label;
            public readonly string[] Chips;
            public SetRow(string section, string label, params string[] chips)
            { Section = section; Label = label; Chips = chips; }
        }

        static readonly SetRow[] SET_ROWS =
        {
            new SetRow("横幅",         "显示",         "开", "关"),
            new SetRow("",             "缩放",         "自动", "0.75", "1", "1.25", "1.5", "2"),
            new SetRow("",             "称号条缩放",   "0.5", "0.6", "0.7", "0.8", "0.9", "1.0"),

            new SetRow("",             "鼠标靠近",     "开", "关"),
            new SetRow("",             "离开游戏淡出", "开", "关"),

            new SetRow("面板",         "面板大小",     "80%", "90%", "100%", "110%", "125%"),
            new SetRow("",             "面板不透明度", "85%", "92%", "100%"),
            new SetRow("",             "屏底三角粒子", "开", "关"),
            new SetRow("",             "屏底装饰层",   "开", "关"),

            new SetRow("文件",         "导出位置",     "默认", "选择", "打开"),
            new SetRow("",             "导入素材",     "头像", "名片", "背景"),
            new SetRow("玩法",         "成就弹条",     "开", "关"),
            new SetRow("",             "弹条时长",     "短", "标准", "长"),
            new SetRow("",             "记住页签",     "开", "关"),
        };

        static readonly string[] SET_TABS = { "横幅", "面板", "文件", "玩法", "按键" };
        static int _setTab = 0;
        static int _setHoverTab = -1;

        const int SET_L_X = CONTENT_X + 32;
        const int SET_LABEL_W = 128;

        const int SET_CHIP_W = 56, SET_CHIP_H = 30, SET_CHIP_GAP = 6;

        const int SET_TAB_Y = CONTENT_Y + 68;
        const int SET_TAB_W = 150, SET_TAB_H = 44, SET_TAB_GAP = 10;

        const int SET_ROW_TOP = CONTENT_Y + 152;
        const int SET_ROW_PITCH = 44;

                const int SET_ROW_PARTICLES = 7;

                const int SET_NOW_Y = CONTENT_Y + 384;

        const int SET_KEY_COL0 = SET_L_X;
        const int SET_KEY_COL_PITCH = 480;
        const int SET_KEY_PERCOL = 8;
        const int SET_KEY_PITCH = 46;
        const int SET_KEY_LABEL_W = 132;
        const int SET_KEY_CHIP_W = 210, SET_KEY_CHIP_H = 30;
        const int SET_KEY_RESET_W = 210, SET_KEY_RESET_H = 34;
        const int SET_KEY_RESET_R = CONTENT_X + CONTENT_W - 32;
        const int SET_KEY_RESET_X = SET_KEY_RESET_R - SET_KEY_RESET_W;

        const int SET_KEY_RESET_Y = SET_ROW_TOP + 7 * SET_KEY_PITCH;

                static bool SetRowShown(int r)
            => r != SET_ROW_PARTICLES;

                static int SetRowCat(int r)
        {
            string sec = "";
            for (int i = 0; i <= r; i++)
                if (SET_ROWS[i].Section.Length > 0) sec = SET_ROWS[i].Section;
            for (int i = 0; i < SET_TABS.Length; i++)
                if (SET_TABS[i] == sec) return i;
            return 0;
        }

                static void SetRowLayout(int r, out int top)
        {
            int cat = SetRowCat(r);
            int y = SET_ROW_TOP;
            for (int i = 0; i < r; i++)
            {
                if (SetRowCat(i) != cat) continue;
                if (!SetRowShown(i)) continue;
                y += SET_ROW_PITCH;
            }
            top = y;
        }

                static int SetChipX(int c)
            => SET_L_X + SET_LABEL_W + c * (SET_CHIP_W + SET_CHIP_GAP);

                static int HitTestSetTab(int x, int y)
        {
            if (y < SET_TAB_Y || y >= SET_TAB_Y + SET_TAB_H) return -1;
            for (int i = 0; i < SET_TABS.Length; i++)
            {
                int tx = SET_L_X + i * (SET_TAB_W + SET_TAB_GAP);
                if (x >= tx && x < tx + SET_TAB_W) return i;
            }
            return -1;
        }

        static int _setHoverRow = -1, _setHoverChip = -1;

        static readonly float[] SET_SCALE_VALS = { 0f, 0.75f, 1f, 1.25f, 1.5f, 2f };
        static readonly float[] SET_TSCALE_VALS = { 0.5f, 0.6f, 0.7f, 0.8f, 0.9f, 1.0f };

        static readonly float[] SET_PSCALE_VALS = { 0.8f, 0.9f, 1f, 1.1f, 1.25f };
        static readonly float[] SET_OPA_VALS    = { 85f, 92f, 100f };
        static readonly float[] SET_TOAST_VALS  = { 1200f, 2600f, 4500f };

                static int NearestChip(float[] table, float v)
        {
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < table.Length; i++)
            {
                float d = Math.Abs(table[i] - v);
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

                static int SetChipOf(int r)
        {
            switch (r)
            {
                case 0: return Plugin.OverlayManuallyHidden ? 1 : 0;

                case 1: return NearestChip(SET_SCALE_VALS, ActiveScaleOverride);
                case 2: return NearestChip(SET_TSCALE_VALS, ActiveTitleScale);
                case 3: return Settings.HideOnMouseAway ? 0 : 1;
                case 4: return Settings.HideOnLeaveGame ? 0 : 1;
                case 5: return NearestChip(SET_PSCALE_VALS, Settings.PanelScale);
                case 6: return NearestChip(SET_OPA_VALS, Settings.PanelOpacity);
                case 7: return ParticlesOn ? 0 : 1;
                case 8: return Settings.BottomLayerOn ? 0 : 1;

                case 9: return string.IsNullOrEmpty(Settings.ExportDir) ? 0 : -1;
                case 10: return -1;
                case 11: return Settings.ToastOn ? 0 : 1;
                case 12: return NearestChip(SET_TOAST_VALS, Settings.ToastHoldMs);
                case 13: return Settings.RememberTab ? 0 : 1;
            }
            return 0;
        }

                static void SetChipApply(int r, int c)
        {
            switch (r)
            {
                case 0:

                    Plugin.OverlayManuallyHidden = c == 1;
                    Settings.OverlayHidden = Plugin.OverlayManuallyHidden;
                    break;
                case 1:
                    Settings.HasOverlayScale = true;
                    Settings.OverlayScale = SET_SCALE_VALS[c];
                    RequestScaleReload();
                    break;
                case 2:
                    Settings.HasTitleScale = true;
                    Settings.TitleScale = SET_TSCALE_VALS[c];
                    RequestScaleReload();
                    break;
                case 3:
                    Settings.HideOnMouseAway = c == 0;
                    break;
                case 4:
                    Settings.HideOnLeaveGame = c == 0;
                    break;
                case 5:

                    Settings.PanelScale = SET_PSCALE_VALS[c];
                    break;
                case 6:

                    Settings.PanelOpacity = (int)SET_OPA_VALS[c];
                    break;
                case 7:
                    ParticlesOn = c == 0;
                    Settings.ParticlesOn = ParticlesOn;
                    break;
                case 8:
                    Settings.BottomLayerOn = c == 0;
                    break;
                case 9:

                    if (c == 0) Settings.ExportDir = "";
                    else PostSetAct(c == 1 ? SETACT_PICK_EXPORT : SETACT_OPEN_EXPORT);
                    break;
                case 10:

                    PostSetAct(c == 0 ? SETACT_IMP_AVATAR : c == 1 ? SETACT_IMP_CARD : SETACT_IMP_BG);
                    break;
                case 11:
                    Settings.ToastOn = c == 0;
                    break;
                case 12:
                    Settings.ToastHoldMs = (int)SET_TOAST_VALS[c];
                    break;
                case 13:
                    Settings.RememberTab = c == 0;

                    if (Settings.RememberTab) Settings.LastTab = _activeNav;
                    break;
            }
            Settings.Save();
            MarkDirty();
        }

                static void HitTestSetChip(int x, int y, out int row, out int chip)
        {
            row = -1; chip = -1;
            for (int r = 0; r < SET_ROWS.Length; r++)
            {
                if (SetRowCat(r) != _setTab) continue;
                if (!SetRowShown(r)) continue;
                SetRowLayout(r, out int top);
                if (y < top || y >= top + SET_CHIP_H) continue;
                for (int c = 0; c < SET_ROWS[r].Chips.Length; c++)
                {
                    int cx = SetChipX(c);
                    if (x >= cx && x < cx + SET_CHIP_W) { row = r; chip = c; return; }
                }
            }
        }

        const int WM_PANEL_SETACT = 0x8000 + 42;
        const int SETACT_PICK_EXPORT = 1, SETACT_OPEN_EXPORT = 2,
                  SETACT_IMP_AVATAR = 3, SETACT_IMP_CARD = 4, SETACT_IMP_BG = 5;
        static volatile int _setActPending = 0;
        static volatile string _setActPath = null;

                static void PostSetAct(int act)
        {
            if (_hWnd == IntPtr.Zero) return;
            if (!PostMessageW(_hWnd, (uint)WM_PANEL_SETACT, (IntPtr)act, IntPtr.Zero))
                Plugin.Logger.LogWarning("[面板] 设置页：系统动作投不过去（act " + act + "）");
        }

                static void RunSetActMsg(int act)
        {
            string picked = null;
            try
            {
                if (act == SETACT_PICK_EXPORT)
                {

                    picked = NativeDialogs.PickFolder("选择导出位置", _hWnd);
                }
                else if (act == SETACT_OPEN_EXPORT)
                {
                    string d = Settings.ExportRoot;
                    Directory.CreateDirectory(d);
                    System.Diagnostics.Process.Start("explorer.exe", "\"" + d + "\"");
                    return;
                }
                else if (act >= SETACT_IMP_AVATAR && act <= SETACT_IMP_BG)
                {

                    string cfg = Path.Combine(Paths.ConfigPath, "InFalsusRating");
                    string folder = act == SETACT_IMP_BG
                        ? Path.Combine(cfg, "jacket", "scene")
                        : Path.Combine(cfg, "jacket");
                    picked = NativeDialogs.PickFile("选择素材图片",
                        "图片 (*.png;*.jpg;*.jpeg;*.bmp)\0*.png;*.jpg;*.jpeg;*.bmp\0所有文件 (*.*)\0*.*\0\0",
                        folder, _hWnd);
                }
                else return;
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("[面板] 设置页对话框出错：" + e.Message);
                return;
            }
            if (string.IsNullOrEmpty(picked)) return;
            _setActPath = picked;
            _setActPending = act;
            MarkDirty();
        }

                static void SetActTick()
        {
            int act = _setActPending;
            if (act == 0) return;
            _setActPending = 0;
            string path = _setActPath;
            _setActPath = null;
            try
            {
                if (act == SETACT_PICK_EXPORT)
                {
                    Settings.ExportDir = path;
                    Settings.Save();
                    Toast.Queue("导出位置已改", ClipTitleLine(path, 380), "设置");
                }
                else if (act >= SETACT_IMP_AVATAR && act <= SETACT_IMP_BG)
                {
                    ImportSkinImage(act - SETACT_IMP_AVATAR, path);
                }
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("[面板] 设置页动作执行失败：" + e.Message);
            }
            MarkDirty();
        }

                static void ImportSkinImage(int kind, string src)
        {
            string label = kind == 0 ? "头像" : kind == 1 ? "名片" : "背景";
            try
            {
                string cfg = Path.Combine(Paths.ConfigPath, "InFalsusRating");
                string folder = kind == 2
                    ? Path.Combine(cfg, "jacket", "scene")
                    : Path.Combine(cfg, "jacket");
                Directory.CreateDirectory(folder);

                string stem = Path.GetFileNameWithoutExtension(src);
                var sb = new StringBuilder();
                foreach (char ch in stem)
                    sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), ch) >= 0 ? '_' : ch);
                stem = sb.ToString().Trim();
                if (stem.Length == 0 || stem.TrimStart('.').Length == 0) stem = "import";
                if (stem.Length > 64) stem = stem.Substring(0, 64);

                string ext = Path.GetExtension(src);
                if (string.IsNullOrEmpty(ext)) ext = ".png";
                ext = ext.ToLowerInvariant();

                string dst = Path.Combine(folder, stem + ext);
                for (int n = 2; File.Exists(dst); n++)
                    dst = Path.Combine(folder, stem + "_" + n + ext);

                File.Copy(src, dst);
                string name = Path.GetFileNameWithoutExtension(dst);

                if (kind == 0) PlayerSkin.Avatar = name;
                else if (kind == 1)
                {
                    PlayerSkin.Card = name;

                    PlayerSkin.CardCx = 0.5; PlayerSkin.CardCy = 0.5; PlayerSkin.CardZoom = 1.0;
                }
                else PlayerSkin.Bg = name;

                PlayerSkin.Bump();
                if (!PlayerSkin.Save()) Plugin.Logger.LogWarning("[面板] 导入素材：player.json 写不进去");

                _sheetItems = null; _sheetItemsTab = -1;
                Plugin.Logger.LogInfo($"[面板] 导入素材：{label} ← {dst}");

                Toast.Queue("素材已导入", label + "：" + name, "设置");
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning($"[面板] 导入素材（{label}）失败：{e.Message}");
                Toast.Queue("导入失败", label + "：" + e.Message, "设置");
            }
        }

        static void RenderConfig(IntPtr gfx)
        {
            var brDim = Brush(unchecked((int)0xFF9AA3B8));
            var brVal = Brush(unchecked((int)0xFFEDF1F7));
            var brHdr = Brush(unchecked((int)0xFFB8DCF5));

            RowTextL(gfx, "设置", _pFontCjkPage, _pBrWhite, SET_L_X, CONTENT_Y + 36);

            for (int i = 0; i < SET_TABS.Length; i++)
            {
                int tx = SET_L_X + i * (SET_TAB_W + SET_TAB_GAP);
                bool on = i == _setTab;
                bool hov = i == _setHoverTab;
                FlatTab(gfx, tx, SET_TAB_Y, SET_TAB_W, SET_TAB_H, 8, on, hov);
                RowTextC(gfx, SET_TABS[i], _pFontCjkLabel, FlatTabInk(on),
                    tx + SET_TAB_W / 2, SET_TAB_Y + SET_TAB_H / 2);
            }

            for (int r = 0; r < SET_ROWS.Length; r++)
            {
                if (SetRowCat(r) != _setTab) continue;
                if (!SetRowShown(r)) continue;
                var row = SET_ROWS[r];
                SetRowLayout(r, out int top);
                int cy = top + SET_CHIP_H / 2;

                RowTextL(gfx, row.Label, _pFontCjkLabel, brVal, SET_L_X, cy + LABEL_TEXT_DY);

                int sel = SetChipOf(r);
                for (int c = 0; c < row.Chips.Length; c++)
                {
                    int cx = SetChipX(c);
                    bool on = c == sel;
                    bool hov = r == _setHoverRow && c == _setHoverChip;

                    FlatTab(gfx, cx, top, SET_CHIP_W, SET_CHIP_H, 6, on, hov);
                    RowTextC(gfx, row.Chips[c], _pFontCjkLabel, FlatTabInk(on),
                        cx + SET_CHIP_W / 2, cy + LABEL_TEXT_DY);
                }
            }

            if (SET_TABS[_setTab] == "按键") RenderSetKeys(gfx, brDim, brHdr, brVal);

            RenderSetNow(gfx, brDim, brHdr, brVal);

            RowTextL(gfx, SET_TABS[_setTab] == "按键"
                    ? "点一个键位格子重新按键 · Esc 取消 · 改过的才写进 settings.json 的 hotkeys 段"
                    : "点一格立刻生效并存进 settings.json",
                _pFontCjkLabel, brDim, SET_L_X, CONTENT_Y + CONTENT_H - 32);
        }

                static void RenderSetNow(IntPtr gfx, IntPtr brDim, IntPtr brHdr, IntPtr brVal)
        {
            int x = SET_L_X;

            if (SET_TABS[_setTab] == "按键") return;
            if (SET_TABS[_setTab] == "文件")
            {
                const int BW = 900, BH = 60;
                int bt = SET_NOW_Y + 22;
                string root = Settings.ExportRoot;
                bool dflt = string.IsNullOrEmpty(Settings.ExportDir);

                RowTextL(gfx, "当前导出位置", _pFontCjkLabel, brHdr, x, SET_NOW_Y);

                FillRoundRect(gfx, _pBrTabHover, x, bt, BW, BH, 10);
                FillRoundRect(gfx, _pBrTabOff, x + 2, bt + 2, BW - 4, BH - 4, 8);
                RowTextL(gfx, ClipTitleLine(root, BW - 36), _pFontCjkLabel, brVal,
                    x + 18, bt + BH / 2);
                RowTextL(gfx, dflt
                        ? "默认夹子 · B30 图片 / b30.txt / scores.txt 都往这里写"
                        : "自定义位置 · 点「默认」换回 config\\InFalsusRating",
                    _pFontCjkLabel, brDim, x, bt + BH + 22);
                RowTextL(gfx, "素材　" + SkinNowText(), _pFontCjkLabel, brDim, x, bt + BH + 52);
                return;
            }

            RowTextL(gfx, "当前", _pFontCjkLabel, brHdr, x, SET_NOW_Y);

            string tab = SET_TABS[_setTab];
            string line;
            if (tab == "面板") line = $"面板 {_viewW}×{_viewH}　不透明度 {Settings.PanelOpacity}%";
            else if (tab == "玩法") line = $"弹条 {(Settings.ToastOn ? "开" : "关")}　停留 {Settings.ToastHoldMs} 毫秒";
            else line = $"横幅 ×{ActiveScale:0.00}　称号条 ×{ActiveTitleScale:0.00}";
            RowTextL(gfx, line, _pFontCjkLabel, brDim, x, SET_NOW_Y + 32);

            if (tab == "横幅")
                RowTextL(gfx, $"鼠标靠近 {(Settings.HideOnMouseAway ? "开" : "关")}　离开游戏淡出 {(Settings.HideOnLeaveGame ? "开" : "关")}",
                    _pFontCjkLabel, brDim, x, SET_NOW_Y + 60);
        }

                static string SkinNowText()
        {
            string a = string.IsNullOrEmpty(PlayerSkin.Avatar) ? "默认" : PlayerSkin.Avatar;
            string c = string.IsNullOrEmpty(PlayerSkin.Card) ? "默认" : PlayerSkin.Card;
            string b = string.IsNullOrEmpty(PlayerSkin.Bg) ? "默认" : PlayerSkin.Bg;
            return ClipTitleLine($"头像 {a} · 名片 {c} · 背景 {b}", 400);
        }

                static bool SetKeyShown(int d) => true;

                static int SetKeySlotCount()
        {
            int n = 0;
            for (int d = 0; d < Hotkeys.Count; d++) if (SetKeyShown(d)) n++;
            return n;
        }

                static int SetKeyDefOfSlot(int k)
        {
            int n = 0;
            for (int d = 0; d < Hotkeys.Count; d++)
            {
                if (!SetKeyShown(d)) continue;
                if (n == k) return d;
                n++;
            }
            return -1;
        }

                static int SetKeySlotTop(int k) => SET_ROW_TOP + (k % SET_KEY_PERCOL) * SET_KEY_PITCH;
        static int SetKeyChipX(int k)
            => SET_KEY_COL0 + (k / SET_KEY_PERCOL) * SET_KEY_COL_PITCH + SET_KEY_LABEL_W;

                static int HitTestHotKey(int x, int y)
        {
            int n = SetKeySlotCount();
            for (int k = 0; k < n; k++)
            {
                int top = SetKeySlotTop(k);
                if (y < top || y >= top + SET_KEY_CHIP_H) continue;
                int cx = SetKeyChipX(k);
                if (x >= cx && x < cx + SET_KEY_CHIP_W) return k;
            }
            return -1;
        }

        static bool HitTestHotReset(int x, int y)
            => x >= SET_KEY_RESET_X && x < SET_KEY_RESET_X + SET_KEY_RESET_W
            && y >= SET_KEY_RESET_Y && y < SET_KEY_RESET_Y + SET_KEY_RESET_H;

        static int _hotCap = -1;
        static readonly bool[] _hotWas = new bool[256];
        static string _hotHint = "";
        static bool _hotHintBad = false;
        static int _hotHoverSlot = -1;
        static bool _hotHoverReset = false;

                static void HotKeyCapBegin(int defIdx)
        {
            _hotCap = defIdx;
            for (int vk = 0; vk < 256; vk++) _hotWas[vk] = Key(vk);
            Hotkeys.Capturing = true;
            _hotHint = "按一个键…（Esc 取消）";
            _hotHintBad = false;
            MarkDirty();
        }

        static void HotKeyCapEnd()
        {
            _hotCap = -1;
            Hotkeys.Capturing = false;
            MarkDirty();
        }

        static void HotKeyCapCancel()
        {
            if (_hotCap < 0) return;
            HotKeyCapEnd();
            _hotHint = "已取消，键位没动";
            _hotHintBad = false;
        }

                static void HotKeyCapTick()
        {
            if (_hotCap < 0) return;
            for (int vk = 0x08; vk <= 0xFE; vk++)
            {
                bool d = Key(vk);
                bool e = d && !_hotWas[vk];
                _hotWas[vk] = d;
                if (!e) continue;
                if (vk == VK_ESCAPE) continue;
                if (!Hotkeys.IsBindable(vk)) continue;
                int mods = 0;
                if (Key(VK_CONTROL)) mods |= Hotkeys.ModCtrl;
                if (Key(VK_SHIFT)) mods |= Hotkeys.ModShift;
                if (Key(VK_MENU)) mods |= Hotkeys.ModAlt;
                int idx = _hotCap;
                if (Hotkeys.Set(idx, vk, mods, out string err))
                {
                    HotKeyCapEnd();
                    _hotHint = $"已改成 {Hotkeys.Text(idx)}";
                    _hotHintBad = false;
                    Settings.Save();
                    Plugin.Logger.LogInfo(
                        $"[设置] 快捷键「{Hotkeys.Defs[idx].Name}」→ {Hotkeys.Text(idx)}（已存 settings.json）");
                }
                else
                {

                    _hotHint = err;
                    _hotHintBad = true;
                    MarkDirty();
                }
                return;
            }
        }

                static void RenderSetKeys(IntPtr gfx, IntPtr brDim, IntPtr brHdr, IntPtr brVal)
        {
            var brBad = Brush(unchecked((int)0xFFFF7B7B));

            int n = SetKeySlotCount();
            int cols = n > SET_KEY_PERCOL ? 2 : 1;
            for (int col = 0; col < cols; col++)
            {
                int x = SET_KEY_COL0 + col * SET_KEY_COL_PITCH;
                RowTextL(gfx, "动作", _pFontCjkLabel, brHdr, x, 206);
                RowTextL(gfx, "键位", _pFontCjkLabel, brHdr, x + SET_KEY_LABEL_W, 206);
            }

            for (int k = 0; k < n; k++)
            {
                int d = SetKeyDefOfSlot(k);
                if (d < 0) continue;
                var df = Hotkeys.Defs[d];
                int top = SetKeySlotTop(k);
                int cy = top + SET_KEY_CHIP_H / 2;

                RowTextL(gfx, df.Name, _pFontCjkLabel, brVal,
                    SET_KEY_COL0 + (k / SET_KEY_PERCOL) * SET_KEY_COL_PITCH, cy + LABEL_TEXT_DY);

                int cx = SetKeyChipX(k);
                bool cap = d == _hotCap;
                bool hov = k == _hotHoverSlot;
                FillRoundRect(gfx, cap ? _pBrTabOn : (hov ? _pBrTabHover : _pBrTabOff),
                    cx, top, SET_KEY_CHIP_W, SET_KEY_CHIP_H, 6);

                RowTextC(gfx, cap ? "按一个键…" : Hotkeys.Text(d), _pFontCjkLabel,
                    cap ? _pBrActiveText : (Hotkeys.IsDefault(d) ? _pBrWhite : BrAchGold()),
                    cx + SET_KEY_CHIP_W / 2, cy + LABEL_TEXT_DY);
            }

            FillRoundRect(gfx, _hotHoverReset ? _pBrTabHover : _pBrTabOff,
                SET_KEY_RESET_X, SET_KEY_RESET_Y, SET_KEY_RESET_W, SET_KEY_RESET_H, 8);

            RowTextC(gfx, "全部恢复默认", _pFontCjkLabel, _pBrWhite,
                SET_KEY_RESET_X + SET_KEY_RESET_W / 2, SET_KEY_RESET_Y + SET_KEY_RESET_H / 2);

            RowTextL(gfx, string.IsNullOrEmpty(_hotHint) ? "每种动作一个键，撞车的不让存" : _hotHint,
                _pFontCjkLabel, _hotHintBad ? brBad : brDim, SET_L_X, 604);
            RowTextL(gfx, "鼠标键 / 修饰键 / Win 键不能绑 · 金字的键是改过的",
                _pFontCjkLabel, brDim, SET_L_X, 632);
        }

        const int BD_SMALL_W = 128, BD_SMALL_H = 81;
        const int BD_MID_W = BD_SMALL_W * 3, BD_MID_H = BD_SMALL_H * 3;

                const int BD_DIM_ARGB = unchecked((int)0x8C0B1020);

                static void DrawInfoBackdrop(IntPtr gfx)
        {
            if (_infoBdDirty) BuildInfoBackdrop();
            if (_infoBd == IntPtr.Zero) return;
            GdipDrawImageRectI(gfx, _infoBd, CONTENT_X, CONTENT_Y, CONTENT_W, CONTENT_H);

            var dim = DimBrush();
            if (dim != IntPtr.Zero) GdipFillRectangleI(gfx, dim, CONTENT_X, CONTENT_Y, CONTENT_W, CONTENT_H);
        }

                static void InfoBdInvalidate()
        {
            _infoBdDirty = true;
            _bdPend = "";
            _charPend = ""; _charPendDone = false;
        }

                static void BuildInfoBackdrop()
        {
            _infoBdDirty = false;
            if (_infoBd != IntPtr.Zero) { GdipDisposeImage(_infoBd); _infoBd = IntPtr.Zero; }

            string file = _bdPend.Length > 0 ? _bdPend : PickInfoScene();
            if (file == null) return;

            bool cached = _bdSmall.ContainsKey(file);
            IntPtr small = SmallOf(file);
            if (small == IntPtr.Zero) { _bdPend = file; return; }
            _bdPend = "";
            Plugin.Logger.LogInfo($"[Panel] 玩家信息背景: {Path.GetFileName(file)}"
                + (cached ? "（复用已糊好的小图）" : ""));

            if (GdipCreateBitmapFromScan0(CONTENT_W, CONTENT_H, 0, PixelFormat32bppARGB,
                    IntPtr.Zero, out var full) != 0 || full == IntPtr.Zero) return;
            if (GdipGetImageGraphicsContext(full, out var g) != 0 || g == IntPtr.Zero)
            { GdipDisposeImage(full); return; }

            GdipSetInterpolationMode(g, 7);
            GdipDrawImageRectI(g, small, 0, 0, CONTENT_W, CONTENT_H);

            GdipDeleteGraphics(g);

            _infoBd = full;
        }

                static string PickInfoScene()
        {
            try
            {
                string dir = Plugin.AssetPath(Path.Combine("jacket", "scene"));

                if (PlayerSkin.Bg.Length > 0)
                {
                    string want = SkinFileOf(PlayerSkin.Bg, SHEET_TAB_BG);
                    if (want != null) return want;
                    if (_bdMissName != PlayerSkin.Bg)
                    {
                        _bdMissName = PlayerSkin.Bg;
                        Plugin.Logger.LogWarning($"[Panel] 玩家信息背景：选的 {PlayerSkin.Bg} 在 scene 夹子里找不到了（这一页先随机抽）");
                    }
                }
                else _bdMissName = null;

                if (!Directory.Exists(dir)) return null;

                var files = new List<string>();
                foreach (var f in Directory.GetFiles(dir))
                {
                    string ext = Path.GetExtension(f);
                    if (ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
                        || ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                        || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
                        || ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase))
                        files.Add(f);
                }
                if (files.Count == 0) return null;

                files.Sort(StringComparer.OrdinalIgnoreCase);
                return files[_bdRnd.Next(files.Count)];
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[Panel] 背景图夹子读不了: {ex.Message}");
                return null;
            }
        }

                static IntPtr SmallOf(string file)
        {
            if (_bdSmall.TryGetValue(file, out var c)) return c;
            if (!_bdAsked.Add(file)) return IntPtr.Zero;

            QueueSkinJob(new SkinJob { Kind = SKIN_SMALL, Path = file });
            return IntPtr.Zero;
        }

                static IntPtr ScaleTo(IntPtr src, int sx, int sy, int sw, int sh, int dw, int dh)
        {
            if (GdipCreateBitmapFromScan0(dw, dh, 0, PixelFormat32bppARGB, IntPtr.Zero, out var dst) != 0
                || dst == IntPtr.Zero) return IntPtr.Zero;
            if (GdipGetImageGraphicsContext(dst, out var g) != 0 || g == IntPtr.Zero)
            { GdipDisposeImage(dst); return IntPtr.Zero; }

            GdipSetInterpolationMode(g, dw < sw ? 6 : 7);
            GdipDrawImageRectRectI(g, src, 0, 0, dw, dh, sx, sy, sw, sh,
                2 , IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            GdipDeleteGraphics(g);
            return dst;
        }

        const int SHEET_X = 32, SHEET_W = 1216;
        const int SHEET_Y = 176, SHEET_H = 530;
        const int SHEET_R = 16;
        const int SHEET_TAB_X = SHEET_X + 24, SHEET_TAB_Y = SHEET_Y + 18;
        const int SHEET_TAB_W = 140, SHEET_TAB_H = 44, SHEET_TAB_GAP = 10;
        const int SHEET_CLOSE_X = SHEET_X + SHEET_W - 60, SHEET_CLOSE_Y = SHEET_TAB_Y, SHEET_CLOSE_S = 36;
        const int SGRID_X = SHEET_X + 24, SGRID_W = SHEET_W - 48, SGRID_Y = SHEET_Y + 78;
        const int SGRID_BOT = SHEET_Y + SHEET_H - 58;
        const int SGRID_BOT_BG = SHEET_Y + SHEET_H - 96;
        const int SCELL = 135, SCELL_GAP = 12, SCELL_PITCH = SCELL + SCELL_GAP;
        const int SCELL_COLS = 8, SCELL_R = 10;
        const int SHEET_BR_CY = SHEET_Y + SHEET_H - 58;
        const int SHEET_BR_X = SGRID_X + 72, SHEET_BR_W = 420, SHEET_BR_H = 10;
        const int SHEET_HINT_CY = SHEET_Y + SHEET_H - 22;
        const int SHEET_BTN_Y = SHEET_Y + SHEET_H - 68, SHEET_BTN_W = 160, SHEET_BTN_H = 48;
        const int SHEET_BTN_OK_X = SHEET_X + SHEET_W - 24 - SHEET_BTN_W;
        const int SHEET_BTN_NO_X = SHEET_BTN_OK_X - SHEET_BTN_W - 12;
                const int SHEET_DRAG_SLOP = 6;

        const int CROP_FX = SHEET_X + (SHEET_W - CROP_FW) / 2, CROP_FY = SGRID_Y + 5;
        const int CROP_FW = 1073, CROP_FH = 360, CROP_R = 33;
                const int CROP_BR_CY = CROP_FY + CROP_FH + 30;

        const int SHEET_TAB_AVA = 0, SHEET_TAB_CARD = 1, SHEET_TAB_CHAR = 2, SHEET_TAB_BG = 3;
        static readonly string[] SHEET_TABS = { "头像", "名片", "角色", "背景" };

                static readonly string[] SHEET_EXTS = { ".png", ".jpg", ".jpeg", ".bmp" };

                sealed class SheetItem
        {
            public string Name = "";
            public string Path = "";
            public string Title = "";
        }

        static bool _sheetOpen = false;
        static int _sheetTab = SHEET_TAB_AVA;
        static List<SheetItem> _sheetItems;
        static int _sheetItemsTab = -1;
        static float _sheetScroll = 0f, _sheetMax = 0f;
        static int _sheetHoverTab = -1, _sheetHoverCell = -1, _sheetHoverBtn = -1;
        static bool _sheetHoverClose = false, _sheetHoverBar = false;
        static bool _sheetDrag = false;
        static int _sheetDragCell = -1, _sheetDragY = 0, _sheetDragMoved = 0;
        static float _sheetDragScroll0 = 0f;
        static bool _brDrag = false;
        static bool _bdrDrag = false;
        static string _sheetNote = "";
        static bool _sheetNoteBad = false;
                static string _charPend = "";
                static bool _charPendDone = false;

        static bool _cropOn = false, _cropDrag = false;
        static string _cropName = "";
        static string _cropPath = "";
        static int _cropDragX = 0, _cropDragY = 0;
        static double _cropCx = 0.5, _cropCy = 0.5, _cropZoom = 1.0;
                static double _cropBlur = PlayerSkin.CardBlur;
        static IntPtr _cropSrc = IntPtr.Zero;
        static IntPtr _cropPrev = IntPtr.Zero;
        static int _cropSW = 0, _cropSH = 0;
        static bool _cropPrevDirty = false;

        static readonly Dictionary<string, IntPtr> _sheetThumb = new Dictionary<string, IntPtr>();
        static readonly HashSet<string> _sheetAsked = new HashSet<string>();
        static Thread _thumbThread = null;
        static volatile bool _thumbQuit = false;
        static volatile ThumbJob _thumbJob = null;
        static readonly object _thumbLock = new object();
        static readonly List<ThumbDone> _thumbDone = new List<ThumbDone>();

                const int THUMB_JACKET = 0, THUMB_SCENE = 1, THUMB_CHAR = 2;
        sealed class ThumbJob { public string[] Paths; public int Mode; }
        struct ThumbDone { public string Key; public IntPtr Bmp; public IntPtr Small; }

                static int ThumbModeOf(int tab)
            => tab == SHEET_TAB_BG ? THUMB_SCENE : (tab == SHEET_TAB_CHAR ? THUMB_CHAR : THUMB_JACKET);

                static string ThumbKeyPrefix(int mode)
            => mode == THUMB_SCENE ? "s_" : (mode == THUMB_CHAR ? "h2_" : "j_");

        static readonly object _skinLock = new object();
        static readonly List<SkinJob> _skinQueue = new List<SkinJob>();
        static readonly List<SkinDone> _skinDone = new List<SkinDone>();

        const int SKIN_AVA = 0, SKIN_CARD = 1, SKIN_CROP = 2, SKIN_SMALL = 3, SKIN_CHAR = 4;
        sealed class SkinJob
        {
            public int Kind, Serial;
            public string Path = "";
            public double Cx = 0.5, Cy = 0.5, Zoom = 1.0;
            public double Blur = PlayerSkin.CardBlur;
        }
        sealed class SkinDone
        {
            public int Kind, Serial, SW, SH;
            public string Path = "";
            public IntPtr Bmp;
        }

        static IntPtr _skinAvaBmp = IntPtr.Zero, _skinCardBmp = IntPtr.Zero, _skinCharBmp = IntPtr.Zero;
        static bool _avaBuilt = false, _cardBuilt = false, _charBuilt = false;
        static string _avaName = null, _cardName = null;
                static string _charName = null;
        static double _cardCx = 0.5, _cardCy = 0.5, _cardZoom = 1.0;
                static double _cardBlur = PlayerSkin.CardBlur;

        static int _avaSer = 0, _cardSer = 0, _cropSer = 0, _charSer = 0;

        static IntPtr _bdDimBr = IntPtr.Zero;
        static int _bdDimA = -1;

        static Dictionary<string, string> _songByBase = null;

        static void OpenSkinSheet()
        {
            if (_sheetOpen) return;
            _sheetOpen = true;
            _sheetScroll = 0f; _sheetMax = 0f;
            _sheetHoverTab = -1; _sheetHoverCell = -1; _sheetHoverBtn = -1;
            _sheetHoverClose = false; _sheetHoverBar = false;
            _sheetDrag = false; _brDrag = false; _bdrDrag = false; _sheetDragCell = -1;
            _sheetNote = ""; _sheetNoteBad = false;
            CropDrop();
            EnsureThumbThread();
            Plugin.Logger.LogInfo("[Panel] 玩家编辑：抽屉打开");
        }

                static void CloseSkinSheet()
        {
            if (!_sheetOpen) return;
            _sheetOpen = false;
            CropDrop();
            _sheetDrag = false; _brDrag = false; _bdrDrag = false; _sheetDragCell = -1;
            _thumbJob = null;
            Plugin.Logger.LogInfo("[Panel] 玩家编辑：抽屉关上");
        }

                static string SheetDir(int tab)
            => tab == SHEET_TAB_BG ? Plugin.AssetPath(Path.Combine("jacket", "scene"))
             : tab == SHEET_TAB_CHAR ? Path.Combine(GuiDir, "playerinfo", "char")
             : Plugin.AssetPath("jacket");

                static string SkinFileOf(string name, int tab)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (name.IndexOf('/') >= 0 || name.IndexOf('\\') >= 0 || name.IndexOf(':') >= 0) return null;
            if (name.Contains("..")) return null;

            string dir = SheetDir(tab);
            foreach (var ext in SHEET_EXTS)
            {
                string f = Path.Combine(dir, name + ext);
                if (File.Exists(f)) return f;
            }
            return null;
        }

                static List<string> SheetListNames(int tab)
        {
            var names = new List<string>();
            try
            {
                string dir = SheetDir(tab);
                if (!Directory.Exists(dir)) return names;
                foreach (var f in Directory.GetFiles(dir))
                {
                    string ext = Path.GetExtension(f);
                    bool ok = false;
                    foreach (var e in SHEET_EXTS)
                        if (ext.Equals(e, StringComparison.OrdinalIgnoreCase)) { ok = true; break; }
                    if (!ok) continue;

                    string nm = Path.GetFileNameWithoutExtension(f);
                    if (tab == SHEET_TAB_AVA || tab == SHEET_TAB_CARD)
                        if (nm.StartsWith("background", StringComparison.OrdinalIgnoreCase)) continue;
                    names.Add(nm);
                }

                names.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex) { Plugin.Logger.LogWarning($"[Panel] 玩家编辑：图片夹子读不了（{ex.Message}）"); }
            return names;
        }

                static List<SheetItem> SheetItems()
        {
            if (_sheetItems != null && _sheetItemsTab == _sheetTab) return _sheetItems;

            var list = new List<SheetItem>();
            if (_sheetTab == SHEET_TAB_CHAR)
            {

                list.Add(new SheetItem { Name = "", Path = "", Title = "不显示" });
                list.Add(new SheetItem { Name = PlayerSkin.RandomKey, Path = "", Title = "随机" });
            }
            else
            {

                list.Add(new SheetItem
                {
                    Name = "",
                    Path = "",
                    Title = _sheetTab == SHEET_TAB_BG ? "随机" : "默认",
                });
            }

            foreach (var nm in SheetListNames(_sheetTab))
            {
                string full = SkinFileOf(nm, _sheetTab);
                if (full == null) continue;
                list.Add(new SheetItem
                {
                    Name = nm,
                    Path = full,

                    Title = (_sheetTab == SHEET_TAB_AVA || _sheetTab == SHEET_TAB_CARD) ? SongTitleOf(nm) : "",
                });
            }
            _sheetItems = list;
            _sheetItemsTab = _sheetTab;
            _sheetScroll = 0f;
            return list;
        }

                static string SongTitleOf(string baseName)
        {
            if (string.IsNullOrEmpty(baseName)) return "";
            if (_songByBase == null)
            {
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    foreach (var id in SongInfoCache.SelectableIds())
                    {
                        string b = SongInfoCache.GetBaseName(id.ToString());
                        if (string.IsNullOrEmpty(b) || map.ContainsKey(b)) continue;
                        string nm = SongInfoCache.GetName(id.ToString());
                        if (!string.IsNullOrEmpty(nm)) map[b] = nm;
                    }
                }
                catch (Exception ex) { Plugin.Logger.LogWarning($"[Panel] 曲名表建不起来（{ex.Message}）"); }
                _songByBase = map;
            }
            return _songByBase.TryGetValue(baseName, out var t) ? t : "";
        }

                static string SheetLabel(SheetItem it)
            => it.Title.Length > 0 ? it.Title : (it.Name.Length == 0 ? "默认" : it.Name);

                static bool SheetIsSelected(SheetItem it)
            => string.Equals(SheetCurName(), it.Name, StringComparison.OrdinalIgnoreCase);

        static string SheetCurName()
            => _sheetTab == SHEET_TAB_BG ? PlayerSkin.Bg
             : _sheetTab == SHEET_TAB_CHAR ? PlayerSkin.Char
             : (_sheetTab == SHEET_TAB_AVA ? PlayerSkin.Avatar : PlayerSkin.Card);

                static int SheetSelIndex(List<SheetItem> items)
        {
            string cur = SheetCurName();
            if (cur.Length == 0) return 0;
            for (int i = 0; i < items.Count; i++)
                if (string.Equals(items[i].Name, cur, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        static string ThumbCacheDir => Path.Combine(BepInEx.Paths.ConfigPath, "InFalsusRating", "cache", "thumb");
        const int THUMB_CACHE_MAX = 600;
        const int THUMB_CACHE_KEEP = 400;

                static string CacheKeyOf(string path, string prefix, bool small)
        {
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists) return null;
                string nm = Path.GetFileNameWithoutExtension(path);
                var sb = new StringBuilder(64);
                sb.Append(prefix);
                for (int i = 0; i < nm.Length && i < 24; i++)
                {
                    char c = nm[i];
                    sb.Append((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') ? c : '_');
                }
                sb.Append('_').Append(fi.Length).Append('_').Append(fi.LastWriteTimeUtc.Ticks);
                sb.Append(small ? ".s.png" : ".png");
                return sb.ToString();
            }
            catch { return null; }
        }

        static IntPtr CacheLoad(string key)
        {
            if (string.IsNullOrEmpty(key)) return IntPtr.Zero;
            try
            {
                string full = Path.Combine(ThumbCacheDir, key);
                if (!File.Exists(full)) return IntPtr.Zero;
                if (GdipCreateBitmapFromFile(full, out var bmp) != 0) return IntPtr.Zero;
                return bmp;
            }
            catch { return IntPtr.Zero; }
        }

                static void CacheSave(IntPtr bmp, string key)
        {
            if (bmp == IntPtr.Zero || string.IsNullOrEmpty(key)) return;
            try
            {
                Directory.CreateDirectory(ThumbCacheDir);
                Guid clsid = PngEncoderClsid();
                if (clsid == Guid.Empty) return;

                string full = Path.Combine(ThumbCacheDir, key);
                string tmp = full + ".tmp";
                if (GdipSaveImageToFile(bmp, tmp, ref clsid, IntPtr.Zero) != 0) return;
                if (File.Exists(full)) File.Delete(full);
                File.Move(tmp, full);
            }
            catch { }
        }

                static void ThumbCachePrune()
        {
            try
            {
                if (!Directory.Exists(ThumbCacheDir)) return;
                var fs = Directory.GetFiles(ThumbCacheDir);
                if (fs.Length <= THUMB_CACHE_MAX) return;

                Array.Sort(fs, (a, b) => File.GetLastWriteTimeUtc(a).CompareTo(File.GetLastWriteTimeUtc(b)));
                int kill = fs.Length - THUMB_CACHE_KEEP;
                for (int i = 0; i < kill; i++) { try { File.Delete(fs[i]); } catch { } }
                Plugin.Logger.LogInfo($"[Panel] 缩略图缓存清了一轮：{kill} 张（原来 {fs.Length} 张）");
            }
            catch (Exception ex) { Plugin.Logger.LogWarning($"[Panel] 缩略图缓存清理出错: {ex.Message}"); }
        }

                static Guid _pngClsid = Guid.Empty;
        static Guid PngEncoderClsid()
        {
            if (_pngClsid != Guid.Empty) return _pngClsid;
            IntPtr buf = IntPtr.Zero;
            try
            {
                if (GdipGetImageEncodersSize(out uint num, out uint size) != 0 || num == 0 || size == 0)
                    return Guid.Empty;
                buf = Marshal.AllocHGlobal((int)size);
                if (GdipGetImageEncoders(num, size, buf) != 0) return Guid.Empty;

                int ps = IntPtr.Size;
                int mimeOff = 32 + ps * 4;
                int stride = 48 + ps * 7;
                for (int i = 0; i < (int)num; i++)
                {
                    IntPtr p = (IntPtr)((long)buf + i * stride);
                    string mime = Marshal.PtrToStringUni(Marshal.ReadIntPtr(p, mimeOff));
                    if (mime == null || !mime.Equals("image/png", StringComparison.OrdinalIgnoreCase)) continue;
                    var raw = new byte[16];
                    Marshal.Copy(p, raw, 0, 16);
                    _pngClsid = new Guid(raw);
                    break;
                }
                if (_pngClsid == Guid.Empty)
                    Plugin.Logger.LogWarning("[Panel] GDI+ 里没找到 image/png 编码器 —— 缩略图不落盘（只影响速度）");
            }
            catch (Exception ex) { Plugin.Logger.LogWarning($"[Panel] 找 PNG 编码器出错: {ex.Message}"); }
            finally { if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf); }
            return _pngClsid;
        }

        static void EnsureThumbThread()
        {
            if (_thumbThread != null && _thumbThread.IsAlive) return;
            _thumbQuit = false;
            _thumbThread = new Thread(ThumbWorker) { IsBackground = true, Name = "InFalsusRating.Thumbs" };

            try { _thumbThread.Priority = ThreadPriority.BelowNormal; }
            catch (Exception ex) { Plugin.Logger.LogWarning($"[Panel] 缩略图线程降优先级失败: {ex.Message}"); }
            _thumbThread.Start();
        }

                static void StopThumbWorker()
        {
            _thumbQuit = true;
            _thumbJob = null;
            var t = _thumbThread;
            _thumbThread = null;
            if (t == null) return;
            try
            {
                if (t.IsAlive && !t.Join(3000))
                    Plugin.Logger.LogWarning("[Panel] 缩略图线程 3 秒没停下来（它手里那张会自己扔掉）");
            }
            catch (Exception ex) { Plugin.Logger.LogWarning($"[Panel] 等缩略图线程时出错: {ex.Message}"); }
        }

        static void ThumbWorker()
        {
            ThumbCachePrune();

            string[] cur = null;
            int mode = THUMB_JACKET;
            int idx = 0;
            while (!_thumbQuit)
            {

                var sk = TakeSkinJob();
                if (sk != null)
                {
                    var res = DecodeSkin(sk);
                    lock (_skinLock)
                    {
                        if (_thumbQuit)
                        {
                            if (res.Bmp != IntPtr.Zero) GdipDisposeImage(res.Bmp);
                            break;
                        }
                        _skinDone.Add(res);
                    }
                    continue;
                }

                var job = _thumbJob;
                if (job == null || !ReferenceEquals(job.Paths, cur))
                {
                    cur = job?.Paths;

                    mode = job?.Mode ?? THUMB_JACKET;
                    idx = 0;
                }
                if (cur == null || idx >= cur.Length) { Thread.Sleep(20); continue; }

                var done = DecodeThumb(cur[idx], mode);
                idx++;
                lock (_thumbLock)
                {
                    if (_thumbQuit)
                    {
                        if (done.Bmp != IntPtr.Zero) GdipDisposeImage(done.Bmp);
                        if (done.Small != IntPtr.Zero) GdipDisposeImage(done.Small);
                        break;
                    }
                    _thumbDone.Add(done);
                }

                Thread.Sleep(THUMB_GAP_MS);
            }
        }

                const int THUMB_GAP_MS = 4;

                static ThumbDone DecodeThumb(string path, int mode)
        {
            var d = new ThumbDone { Key = path };
            bool scene = mode == THUMB_SCENE;
            string kThumb = CacheKeyOf(path, ThumbKeyPrefix(mode), false);
            string kSmall = scene ? CacheKeyOf(path, ThumbKeyPrefix(mode), true) : null;

            d.Bmp = CacheLoad(kThumb);
            if (scene) d.Small = CacheLoad(kSmall);
            if (d.Bmp != IntPtr.Zero && (!scene || d.Small != IntPtr.Zero)) return d;

            IntPtr src = IntPtr.Zero;
            try
            {
                if (GdipCreateBitmapFromFile(path, out src) != 0 || src == IntPtr.Zero) return d;
                if (GdipGetImageWidth(src, out uint sw) != 0 || GdipGetImageHeight(src, out uint sh) != 0
                    || sw == 0 || sh == 0) return d;

                if (d.Bmp == IntPtr.Zero)
                {

                    d.Bmp = mode == THUMB_CHAR
                          ? MakeCharInto(src, (int)sw, (int)sh, SCELL, SCELL)
                          : BuildCover(src, (int)sw, (int)sh, 0.5, 0.5, 1.0, SCELL, SCELL);
                    CacheSave(d.Bmp, kThumb);
                }
                if (scene && d.Small == IntPtr.Zero)
                {
                    d.Small = MakeSceneSmall(src, (int)sw, (int)sh);
                    CacheSave(d.Small, kSmall);
                }
                return d;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[Panel] 缩略图解不了（{Path.GetFileName(path)}）: {ex.Message}");
                return d;
            }
            finally { if (src != IntPtr.Zero) GdipDisposeImage(src); }
        }

                static void QueueSkinJob(SkinJob j)
        {
            EnsureThumbThread();
            lock (_skinLock)
            {
                if (j.Kind != SKIN_SMALL)
                    for (int i = _skinQueue.Count - 1; i >= 0; i--)
                        if (_skinQueue[i].Kind == j.Kind) _skinQueue.RemoveAt(i);

                int p = SkinPrio(j.Kind), at = _skinQueue.Count;
                for (int i = 0; i < _skinQueue.Count; i++)
                    if (SkinPrio(_skinQueue[i].Kind) > p) { at = i; break; }
                _skinQueue.Insert(at, j);
            }
        }

                static int SkinPrio(int kind) => kind == SKIN_SMALL ? 0 : (kind == SKIN_CROP ? 1 : 2);

        static SkinJob TakeSkinJob()
        {
            lock (_skinLock)
            {
                if (_skinQueue.Count == 0) return null;
                var j = _skinQueue[0];
                _skinQueue.RemoveAt(0);
                return j;
            }
        }

                static SkinDone DecodeSkin(SkinJob j)
        {
            var d = new SkinDone { Kind = j.Kind, Serial = j.Serial, Path = j.Path };
            string key = j.Kind == SKIN_CROP ? null : SkinCacheKeyOf(j);

            if (key != null)
            {
                d.Bmp = CacheLoad(key);
                if (d.Bmp != IntPtr.Zero) return d;
            }

            IntPtr src = IntPtr.Zero;
            try
            {
                if (GdipCreateBitmapFromFile(j.Path, out src) != 0 || src == IntPtr.Zero) return d;
                if (GdipGetImageWidth(src, out uint sw) != 0 || GdipGetImageHeight(src, out uint sh) != 0
                    || sw == 0 || sh == 0) return d;
                d.SW = (int)sw; d.SH = (int)sh;

                if (j.Kind == SKIN_AVA)
                    d.Bmp = BuildCover(src, d.SW, d.SH, 0.5, 0.5, 1.0, INFO_AVA_IMG, INFO_AVA_IMG);
                else if (j.Kind == SKIN_CARD)
                    d.Bmp = BuildCardCover(src, d.SW, d.SH, j.Cx, j.Cy, j.Zoom, INFO_CARD_W, INFO_CARD_H, j.Blur);
                else if (j.Kind == SKIN_CHAR)

                    d.Bmp = MakeCharInto(src, d.SW, d.SH, INFO_CHAR_W, INFO_CHAR_H);
                else if (j.Kind == SKIN_SMALL)
                    d.Bmp = MakeSceneSmall(src, d.SW, d.SH);
                else
                {

                    d.Bmp = src;
                    src = IntPtr.Zero;
                }
                CacheSave(d.Bmp, key);
                return d;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[Panel] 玩家编辑：解不了 {Path.GetFileName(j.Path)} —— {ex.Message}");
                return d;
            }
            finally { if (src != IntPtr.Zero) GdipDisposeImage(src); }
        }

                static string SkinCacheKeyOf(SkinJob j)
        {
            if (j.Kind == SKIN_SMALL) return CacheKeyOf(j.Path, "s_", true);

            if (j.Kind == SKIN_CHAR)
                return CacheKeyOf(j.Path, "h" + INFO_CHAR_W + "x" + INFO_CHAR_H + "_", false);
            string k = CacheKeyOf(j.Path, "j_", false);
            if (k == null) return null;
            var sb = new StringBuilder(k.Length + 28);
            sb.Append(k, 0, k.Length - 4);
            if (j.Kind == SKIN_AVA) sb.Append("_a");
            else sb.Append("_c").Append(IRnd(j.Cx * 10000)).Append('_').Append(IRnd(j.Cy * 10000))
                    .Append('_').Append(IRnd(j.Zoom * 100)).Append('_').Append(IRnd(j.Blur));
            sb.Append(".png");
            return sb.ToString();
        }

                static int IRnd(double v) { int n = (int)Math.Round(v); return n < 0 ? 0 : n; }

                static void DrainSkinDone()
        {
            List<SkinDone> take = null;
            lock (_skinLock)
            {
                if (_skinDone.Count == 0) return;
                take = new List<SkinDone>(_skinDone);
                _skinDone.Clear();
            }
            foreach (var d in take) ApplySkinDone(d);
        }

        static void ApplySkinDone(SkinDone d)
        {
            if (d.Kind == SKIN_AVA)
            {
                if (d.Serial != _avaSer) { FreeBmp(d.Bmp); return; }
                FreeBmp(_skinAvaBmp); _skinAvaBmp = d.Bmp;
                MarkDirty();
                return;
            }
            if (d.Kind == SKIN_CARD)
            {
                if (d.Serial != _cardSer) { FreeBmp(d.Bmp); return; }
                FreeBmp(_skinCardBmp); _skinCardBmp = d.Bmp;
                MarkDirty();
                return;
            }
            if (d.Kind == SKIN_CHAR)
            {

                if (d.Serial != _charSer) { FreeBmp(d.Bmp); return; }
                FreeBmp(_skinCharBmp); _skinCharBmp = d.Bmp;
                MarkDirty();
                return;
            }
            if (d.Kind == SKIN_CROP)
            {
                if (d.Serial != _cropSer || !_cropOn) { FreeBmp(d.Bmp); return; }
                if (d.Bmp == IntPtr.Zero)
                {

                    SetSheetNote($"这张图解不开：{_cropName}", true);
                    CropDrop();
                    MarkDirty();
                    return;
                }
                FreeBmp(_cropSrc); _cropSrc = d.Bmp;
                _cropSW = d.SW; _cropSH = d.SH;
                CropClamp();
                _cropPrevDirty = true;
                MarkDirty();
                return;
            }

            if (d.Bmp == IntPtr.Zero) { _infoBdDirty = true; MarkDirty(); return; }
            if (_bdSmall.TryGetValue(d.Path, out var old) && old != IntPtr.Zero) FreeBmp(old);
            _bdSmall[d.Path] = d.Bmp;
            _infoBdDirty = true;
            MarkDirty();
        }

                static void FreeBmp(IntPtr bmp) { if (bmp != IntPtr.Zero) GdipDisposeImage(bmp); }

                static void SheetPumpThumbs()
        {
            var items = _sheetItems;
            if (items == null) return;

            List<string> pend = null;
            for (int i = 0; i < items.Count; i++)
            {
                string p = items[i].Path;
                if (p.Length == 0 || _sheetAsked.Contains(p)) continue;
                (pend ??= new List<string>()).Add(p);
            }
            if (pend == null) return;

            EnsureThumbThread();
            foreach (var p in pend) _sheetAsked.Add(p);

            _thumbJob = new ThumbJob { Paths = pend.ToArray(), Mode = ThumbModeOf(_sheetTab) };
        }

                static void DrainThumbDone(bool all)
        {
            if (_thumbDone.Count == 0) return;

            MarkDirty();
            List<ThumbDone> take;
            lock (_thumbLock)
            {
                take = new List<ThumbDone>(_thumbDone);
                _thumbDone.Clear();
            }
            foreach (var d in take)
            {
                if (d.Small != IntPtr.Zero)
                {

                    if (!_bdSmall.ContainsKey(d.Key) && !all)
                    {
                        _bdSmall[d.Key] = d.Small;

                        _infoBdDirty = true;
                    }
                    else GdipDisposeImage(d.Small);
                }
                if (d.Bmp == IntPtr.Zero) continue;
                if (all || _sheetThumb.ContainsKey(d.Key)) GdipDisposeImage(d.Bmp);
                else _sheetThumb[d.Key] = d.Bmp;
            }
        }

                static void CropRectOf(int sw, int sh, double cx, double cy, double zoom, int dw, int dh,
            out double sx, out double sy, out double w, out double h)
        {
            if (zoom < 1.0) zoom = 1.0;
            if (zoom > 8.0) zoom = 8.0;
            double aspect = (double)dw / dh;
            bool wider = (double)sw / sh > aspect;
            double w0 = wider ? sh * aspect : sw;
            double h0 = wider ? sh : sw / aspect;
            w = w0 / zoom;
            h = h0 / zoom;
            if (w > sw) w = sw;
            if (h > sh) h = sh;
            sx = cx * sw - w / 2;
            sy = cy * sh - h / 2;
            double mx = sw - w, my = sh - h;
            if (sx < 0) sx = 0; else if (sx > mx) sx = mx;
            if (sy < 0) sy = 0; else if (sy > my) sy = my;
        }

                static IntPtr BuildCover(IntPtr src, int sw, int sh, double cx, double cy, double zoom, int dw, int dh)
        {
            if (src == IntPtr.Zero || sw <= 0 || sh <= 0 || dw <= 0 || dh <= 0) return IntPtr.Zero;
            CropRectOf(sw, sh, cx, cy, zoom, dw, dh, out double sx, out double sy, out double w, out double h);
            if (w < 1 || h < 1) return IntPtr.Zero;
            return ScaleTo(src, (int)Math.Round(sx), (int)Math.Round(sy),
                Math.Max(1, (int)Math.Round(w)), Math.Max(1, (int)Math.Round(h)), dw, dh);
        }

                static IntPtr MakeSceneSmall(IntPtr src, int sw, int sh)
        {
            if (src == IntPtr.Zero || sw <= 0 || sh <= 0) return IntPtr.Zero;
            IntPtr mid = IntPtr.Zero;
            try
            {
                mid = BuildCover(src, sw, sh, 0.5, 0.5, 1.0, BD_MID_W, BD_MID_H);
                if (mid == IntPtr.Zero) return IntPtr.Zero;
                return ScaleTo(mid, 0, 0, BD_MID_W, BD_MID_H, BD_SMALL_W, BD_SMALL_H);
            }
            finally { if (mid != IntPtr.Zero) GdipDisposeImage(mid); }
        }

                const int CHAR_PROBE = 128;
                const int CHAR_ALPHA_MIN = 8;

                static bool AlphaBoxOf(IntPtr src, int sw, int sh, out int bx, out int by, out int bw, out int bh)
        {
            bx = 0; by = 0; bw = Math.Max(1, sw); bh = Math.Max(1, sh);
            if (src == IntPtr.Zero || sw <= 0 || sh <= 0) return false;

            if (sw <= CHAR_PROBE && sh <= CHAR_PROBE)
                return ScanAlphaBox(src, sw, sh, sw, sh, out bx, out by, out bw, out bh);

            int pw, ph;
            if (sw >= sh) { pw = CHAR_PROBE; ph = Math.Max(1, (int)Math.Round((double)sh * CHAR_PROBE / sw)); }
            else { ph = CHAR_PROBE; pw = Math.Max(1, (int)Math.Round((double)sw * CHAR_PROBE / sh)); }

            IntPtr probe = IntPtr.Zero;
            try
            {
                probe = ScaleTo(src, 0, 0, sw, sh, pw, ph);
                if (probe == IntPtr.Zero) return false;
                return ScanAlphaBox(probe, pw, ph, sw, sh, out bx, out by, out bw, out bh);
            }
            finally { if (probe != IntPtr.Zero) GdipDisposeImage(probe); }
        }

                static unsafe bool ScanAlphaBox(IntPtr bmp, int w, int h, int sw, int sh,
            out int bx, out int by, out int bw, out int bh)
        {
            bx = 0; by = 0; bw = Math.Max(1, sw); bh = Math.Max(1, sh);
            if (bmp == IntPtr.Zero || w <= 0 || h <= 0 || sw <= 0 || sh <= 0) return false;

            var lockRect = new GPRECT { X = 0, Y = 0, Width = w, Height = h };
            if (GdipBitmapLockBits(bmp, ref lockRect, ImageLockModeRead, PixelFormat32bppARGB, out var bd) != 0)
                return false;

            int x0 = w, y0 = h, x1 = -1, y1 = -1;
            try
            {
                byte* p0 = (byte*)bd.Scan0;
                for (int y = 0; y < h; y++)
                {
                    byte* row = p0 + (long)y * bd.Stride;
                    for (int x = 0; x < w; x++)
                    {
                        if (row[x * 4 + 3] <= CHAR_ALPHA_MIN) continue;
                        if (x < x0) x0 = x;
                        if (x > x1) x1 = x;
                        if (y < y0) y0 = y;
                        if (y > y1) y1 = y;
                    }
                }
            }
            finally { GdipBitmapUnlockBits(bmp, ref bd); }

            if (x1 < x0 || y1 < y0) return false;

            double kx = (double)sw / w, ky = (double)sh / h;

            int lo = (int)Math.Floor((x0 - 1) * kx), hi = (int)Math.Ceiling((x1 + 1) * kx);
            int to = (int)Math.Floor((y0 - 1) * ky), bo = (int)Math.Ceiling((y1 + 1) * ky);
            bx = lo < 0 ? 0 : lo;
            by = to < 0 ? 0 : to;
            int ex = hi > sw ? sw : hi, ey = bo > sh ? sh : bo;
            bw = ex - bx; bh = ey - by;
            return bw >= 1 && bh >= 1;
        }

                static IntPtr MakeCharInto(IntPtr src, int sw, int sh, int dw, int dh)
        {
            if (src == IntPtr.Zero || sw <= 0 || sh <= 0 || dw <= 0 || dh <= 0) return IntPtr.Zero;

            int bx, by, bw, bh;
            AlphaBoxOf(src, sw, sh, out bx, out by, out bw, out bh);

            int vh = (int)Math.Round((double)bw * dh / dw);
            if (vh > bh) vh = bh;
            if (vh < 1) vh = 1;

            if (GdipCreateBitmapFromScan0(dw, dh, 0, PixelFormat32bppARGB, IntPtr.Zero, out var canvas) != 0
                || canvas == IntPtr.Zero) return IntPtr.Zero;
            if (GdipGetImageGraphicsContext(canvas, out var g) != 0 || g == IntPtr.Zero)
            { GdipDisposeImage(canvas); return IntPtr.Zero; }

            try
            {
                GdipGraphicsClear(g, 0x00000000);

                GdipSetInterpolationMode(g, bw > dw ? 6 : 7);

                GdipDrawImageRectRectI(g, src, 0, 0, dw, dh, bx, by, bw, vh,
                    2 , IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            }
            finally { GdipDeleteGraphics(g); }
            return canvas;
        }

                static void SkinTick()
        {
            DrainThumbDone(false);
            DrainSkinDone();

            if (!_avaBuilt || _avaName != PlayerSkin.Avatar) RequestAvatar();

            if (!_cardBuilt || _cardName != PlayerSkin.Card
                || _cardCx != PlayerSkin.CardCx || _cardCy != PlayerSkin.CardCy
                || _cardZoom != PlayerSkin.CardZoom || _cardBlur != PlayerSkin.CardBlur)
                RequestCard();

            string wantChar = CharWantName();
            if (!_charBuilt || _charName != wantChar) RequestChar(wantChar);
        }

                static void RequestAvatar()
        {
            _avaBuilt = true;
            _avaName = PlayerSkin.Avatar;
            _avaSer++;
            FreeBmp(_skinAvaBmp); _skinAvaBmp = IntPtr.Zero;
            MarkDirty();
            if (PlayerSkin.Avatar.Length == 0) return;

            string full = SkinFileOf(PlayerSkin.Avatar, SHEET_TAB_AVA);
            if (full == null)
            {
                Plugin.Logger.LogWarning($"[Panel] 玩家编辑：头像 {PlayerSkin.Avatar} 找不到了（回默认）");
                return;
            }
            QueueSkinJob(new SkinJob { Kind = SKIN_AVA, Serial = _avaSer, Path = full });
        }

                static double ClampBlur(double b)
        {
            if (double.IsNaN(b) || b < PlayerSkin.BlurMin) return PlayerSkin.BlurMin;
            return b > PlayerSkin.BlurMax ? PlayerSkin.BlurMax : b;
        }

                static IntPtr BuildCardCover(IntPtr src, int sw, int sh, double cx, double cy, double zoom,
            int dw, int dh, double blur)
        {
            var cover = BuildCover(src, sw, sh, cx, cy, zoom, dw, dh);
            if (cover == IntPtr.Zero) return IntPtr.Zero;

            int n = (int)Math.Round(ClampBlur(blur));
            int bw = Math.Max(1, dw / n), bh = Math.Max(1, dh / n);
            if (bw >= dw || bh >= dh) return cover;

            IntPtr small = IntPtr.Zero, back = IntPtr.Zero;
            try
            {
                small = ScaleTo(cover, 0, 0, dw, dh, bw, bh);
                if (small != IntPtr.Zero) back = ScaleTo(small, 0, 0, bw, bh, dw, dh);
            }
            finally { if (small != IntPtr.Zero) GdipDisposeImage(small); }
            if (back == IntPtr.Zero) return cover;
            GdipDisposeImage(cover);
            return back;
        }

                static void RequestCard()
        {
            _cardBuilt = true;
            _cardName = PlayerSkin.Card;
            _cardCx = PlayerSkin.CardCx; _cardCy = PlayerSkin.CardCy; _cardZoom = PlayerSkin.CardZoom;
            _cardBlur = PlayerSkin.CardBlur;
            _cardSer++;
            FreeBmp(_skinCardBmp); _skinCardBmp = IntPtr.Zero;
            MarkDirty();
            if (PlayerSkin.Card.Length == 0) return;

            string full = SkinFileOf(PlayerSkin.Card, SHEET_TAB_CARD);
            if (full == null)
            {
                Plugin.Logger.LogWarning($"[Panel] 玩家编辑：名片 {PlayerSkin.Card} 找不到了（回默认）");
                return;
            }
            QueueSkinJob(new SkinJob
            {
                Kind = SKIN_CARD, Serial = _cardSer, Path = full,
                Cx = PlayerSkin.CardCx, Cy = PlayerSkin.CardCy, Zoom = PlayerSkin.CardZoom,
                Blur = PlayerSkin.CardBlur,
            });
        }

                static string CharWantName()
        {
            string cur = PlayerSkin.Char;
            if (cur.Length == 0) return "";
            if (cur != PlayerSkin.RandomKey) return cur;

            if (!_charPendDone)
            {
                _charPendDone = true;
                var names = SheetListNames(SHEET_TAB_CHAR);
                if (names.Count == 0)
                {
                    _charPend = "";
                    Plugin.Logger.LogWarning("[Panel] 玩家编辑：char 夹子里一张图都没有（这张立绘先空着）");
                }
                else _charPend = names[_bdRnd.Next(names.Count)];
            }
            return _charPend;
        }

                static void RequestChar(string want)
        {
            _charBuilt = true;
            _charName = want;
            _charSer++;
            FreeBmp(_skinCharBmp); _skinCharBmp = IntPtr.Zero;
            MarkDirty();
            if (want.Length == 0) return;

            string full = SkinFileOf(want, SHEET_TAB_CHAR);
            if (full == null)
            {
                Plugin.Logger.LogWarning($"[Panel] 玩家编辑：角色 {want} 在 char 夹子里找不到了（不显示）");
                return;
            }
            QueueSkinJob(new SkinJob { Kind = SKIN_CHAR, Serial = _charSer, Path = full });
        }

                static IntPtr DimBrush()
        {
            int a = (int)Math.Round((1.0 - PlayerSkin.BgBright) * 255.0);
            if (a < 0) a = 0;
            if (a > 255) a = 255;
            if (a == _bdDimA) return _bdDimBr;

            if (_bdDimBr != IntPtr.Zero) { GdipDeleteBrush(_bdDimBr); _bdDimBr = IntPtr.Zero; }
            _bdDimA = a;
            if (a > 0)
                GdipCreateSolidFill(unchecked((int)((uint)a << 24 | ((uint)BD_DIM_ARGB & 0x00FFFFFFu))), out _bdDimBr);
            return _bdDimBr;
        }

                static void DrawSkinSheet(IntPtr gfx)
        {
            if (!_sheetOpen) return;
            var brDim = Brush(unchecked((int)0xFF9AA3B8));
            var brVal = Brush(unchecked((int)0xFFEDF1F7));
            var brHdr = Brush(unchecked((int)0xFFB8DCF5));
            var brBad = Brush(unchecked((int)0xFFFF7B7B));
            var brInk = Brush(unchecked((int)0xFF23262E));

            FillRoundRect(gfx, Brush(unchecked((int)0xF20B1020)), SHEET_X, SHEET_Y, SHEET_W, SHEET_H, SHEET_R);
            GdipFillRectangleI(gfx, brHdr, SHEET_X + SHEET_R, SHEET_Y + 1, SHEET_W - SHEET_R * 2, 2);

            for (int i = 0; i < SHEET_TABS.Length; i++)
            {
                int tx = SHEET_TAB_X + i * (SHEET_TAB_W + SHEET_TAB_GAP);
                bool on = !_cropOn && i == _sheetTab;
                bool hov = i == _sheetHoverTab;

                FlatTab(gfx, tx, SHEET_TAB_Y, SHEET_TAB_W, SHEET_TAB_H, 8, on, hov);
                RowTextC(gfx, SHEET_TABS[i], _pFontCjkLabel, FlatTabInk(on),
                    tx + SHEET_TAB_W / 2, SHEET_TAB_Y + SHEET_TAB_H / 2);
            }

            Draw(gfx, Tex(_sheetHoverClose ? "base/close_hover.png" : "base/close_normal.png"),
                SHEET_CLOSE_X, SHEET_CLOSE_Y, SHEET_CLOSE_S, SHEET_CLOSE_S);

            string cap;
            if (_cropOn) cap = "裁剪中：" + _cropName;
            else
            {
                var items = SheetItems();
                int hi = _sheetHoverCell >= 0 ? _sheetHoverCell : SheetSelIndex(items);
                cap = hi >= 0 && hi < items.Count ? SheetLabel(items[hi]) : "";
            }
            if (cap.Length > 0)
            {

                int capW = SHEET_CLOSE_X - 16 - 12
                         - (SHEET_TAB_X + SHEET_TABS.Length * (SHEET_TAB_W + SHEET_TAB_GAP));
                RowTextR(gfx, ClipTitleLine(cap, capW), _pFontCjkLabel, brDim,
                    SHEET_CLOSE_X - 16, SHEET_TAB_Y + SHEET_TAB_H / 2);
            }

            if (_cropOn) DrawCropMode(gfx, brDim, brVal, brHdr, brInk);
            else DrawSheetGrid(gfx, brDim, brVal, brHdr, brInk);

            string note = _sheetNote.Length > 0 ? _sheetNote
                : (_cropOn ? "拖动 = 平移 · 滚轮 = 缩放 · 模糊滑杆：数字越大越糊"
                           : "点一下立刻生效 · Esc 或点板外面关掉");
            RowTextL(gfx, note, _pFontCjkLabel, _sheetNoteBad ? brBad : brDim, SGRID_X, SHEET_HINT_CY);
        }

                static void DrawSheetGrid(IntPtr gfx, IntPtr brDim, IntPtr brVal, IntPtr brHdr, IntPtr brInk)
        {
            var items = SheetItems();
            SheetPumpThumbs();

            int bot = _sheetTab == SHEET_TAB_BG ? SGRID_BOT_BG : SGRID_BOT;
            int rows = (items.Count + SCELL_COLS - 1) / SCELL_COLS;
            _sheetMax = Math.Max(0f, rows * SCELL_PITCH - SCELL_GAP - (bot - SGRID_Y));
            if (_sheetScroll > _sheetMax) _sheetScroll = _sheetMax;
            if (_sheetScroll < 0f) _sheetScroll = 0f;

            int special = _sheetTab == SHEET_TAB_CHAR ? 2 : 1;

            bool dimThumbs = _sheetTab == SHEET_TAB_BG;
            IntPtr dimBr = dimThumbs ? DimBrush() : IntPtr.Zero;

            GdipSetClipRect(gfx, SGRID_X, SGRID_Y, SGRID_W, bot - SGRID_Y, 0 );
            for (int i = 0; i < items.Count; i++)
            {
                int x = SGRID_X + (i % SCELL_COLS) * SCELL_PITCH;
                int y = SGRID_Y + (i / SCELL_COLS) * SCELL_PITCH - (int)_sheetScroll;
                if (y + SCELL < SGRID_Y || y > bot) continue;
                DrawSheetCell(gfx, items[i], i, x, y, brHdr, dimBr);

                GdipSetClipRect(gfx, SGRID_X, SGRID_Y, SGRID_W, bot - SGRID_Y, 0 );
            }
            GdipResetClip(gfx);

            if (items.Count <= special)
                RowTextC(gfx, _sheetTab == SHEET_TAB_CHAR
                        ? "char 夹子里还没有图（放几张进去，关掉面板再开就认）"
                        : "这个夹子里还没有图（放几张进去，关掉面板再开就认）",
                    _pFontCjkLabel, brDim, SGRID_X + SGRID_W / 2, SGRID_Y + SCELL / 2);

            if (_sheetTab != SHEET_TAB_BG) return;

            DrawSlider(gfx, brVal, brHdr, "亮度", PlayerSkin.BgBright,
                $"{(int)Math.Round(PlayerSkin.BgBright * 100)}%", SHEET_BR_CY, _sheetHoverBar || _brDrag);
        }

                static void DrawSlider(IntPtr gfx, IntPtr brVal, IntPtr brHdr, string label, double v01,
            string valText, int cy, bool hot)
        {
            RowTextL(gfx, label, _pFontCjkLabel, brVal, SGRID_X, cy);
            if (v01 < 0.0) v01 = 0.0;
            if (v01 > 1.0) v01 = 1.0;
            int by = cy - SHEET_BR_H / 2;
            GdipFillRectangleI(gfx, _pBrTabOff, SHEET_BR_X, by, SHEET_BR_W, SHEET_BR_H);
            int fillW = (int)Math.Round(SHEET_BR_W * v01);
            if (fillW > 0) GdipFillRectangleI(gfx, hot ? _pBrTabOn : brHdr, SHEET_BR_X, by, fillW, SHEET_BR_H);
            int knobX = SHEET_BR_X + fillW - 6;
            if (knobX < SHEET_BR_X - 6) knobX = SHEET_BR_X - 6;
            if (knobX > SHEET_BR_X + SHEET_BR_W - 6) knobX = SHEET_BR_X + SHEET_BR_W - 6;
            FillRoundRect(gfx, _pBrWhite, knobX, cy - 14, 12, 28, 6);
            RowTextL(gfx, valText, _pFontCjkLabel, brVal, SHEET_BR_X + SHEET_BR_W + 20, cy);
        }

                static void DrawSheetCell(IntPtr gfx, SheetItem it, int idx, int x, int y, IntPtr brHdr, IntPtr dimBr)
        {
            bool sel = SheetIsSelected(it);
            bool hov = idx == _sheetHoverCell;
            if (sel || hov)
            {
                int t = sel ? 3 : 1;
                FillRoundRect(gfx, sel ? brHdr : _pBrWhite,
                    x - t, y - t, SCELL + t * 2, SCELL + t * 2, SCELL_R + t);
            }

            IntPtr bmp = IntPtr.Zero;
            if (it.Path.Length > 0 && _sheetThumb.TryGetValue(it.Path, out var b)) bmp = b;

            var clip = RoundRectPath(x, y, SCELL, SCELL, SCELL_R);

            if (clip != IntPtr.Zero) GdipSetClipPath(gfx, clip, 1 );

            GdipFillRectangleI(gfx, _pBrTabOff, x, y, SCELL, SCELL);
            if (bmp != IntPtr.Zero) Draw(gfx, bmp, x, y, SCELL, SCELL);
            else if (it.Path.Length == 0)

                RowTextC(gfx, SheetLabel(it), _pFontCjkLabel, _pBrWhite, x + SCELL / 2, y + SCELL / 2);

            if (dimBr != IntPtr.Zero) GdipFillRectangleI(gfx, dimBr, x, y, SCELL, SCELL);

            if (clip != IntPtr.Zero) { GdipResetClip(gfx); GdipDeletePath(clip); }
        }

                static void DrawCropMode(IntPtr gfx, IntPtr brDim, IntPtr brVal, IntPtr brHdr, IntPtr brInk)
        {
            if (_cropPrevDirty) CropRebuild();

            FillRoundRect(gfx, _cropDrag ? _pBrWhite : brHdr,
                CROP_FX - 2, CROP_FY - 2, CROP_FW + 4, CROP_FH + 4, CROP_R + 2);
            var clip = RoundRectPath(CROP_FX, CROP_FY, CROP_FW, CROP_FH, CROP_R);
            if (clip != IntPtr.Zero) GdipSetClipPath(gfx, clip, 0 );
            if (_cropPrev != IntPtr.Zero) Draw(gfx, _cropPrev, CROP_FX, CROP_FY, CROP_FW, CROP_FH);
            else GdipFillRectangleI(gfx, _pBrTabOff, CROP_FX, CROP_FY, CROP_FW, CROP_FH);
            if (clip != IntPtr.Zero) { GdipResetClip(gfx); GdipDeletePath(clip); }

            DrawSheetButton(gfx, "取消", SHEET_BTN_NO_X, false, brInk);
            DrawSheetButton(gfx, "确定", SHEET_BTN_OK_X, true, brInk);

            int bn = (int)Math.Round(ClampBlur(_cropBlur));
            DrawSlider(gfx, brVal, brHdr, "模糊", BlurToSlider(bn), bn.ToString(),
                CROP_BR_CY, _sheetHoverBar || _bdrDrag);
        }

                static double BlurToSlider(double blur)
            => (Math.Round(ClampBlur(blur)) - PlayerSkin.BlurMin) / (PlayerSkin.BlurMax - PlayerSkin.BlurMin);

                static double SliderToBlur(double v01)
        {
            if (v01 < 0.0) v01 = 0.0;
            if (v01 > 1.0) v01 = 1.0;
            return Math.Round(PlayerSkin.BlurMin + v01 * (PlayerSkin.BlurMax - PlayerSkin.BlurMin));
        }

                static void DrawSheetButton(IntPtr gfx, string label, int x, bool primary, IntPtr brInk)
        {
            bool hov = (_sheetHoverBtn == (primary ? 1 : 0));
            IntPtr plate = primary ? (hov ? _pBrTabOn : brInk) : (hov ? _pBrTabHover : _pBrTabOff);
            FillRoundRect(gfx, plate, x, SHEET_BTN_Y, SHEET_BTN_W, SHEET_BTN_H, 8);

            bool darkText = primary && hov;
            RowTextC(gfx, label, _pFontCjkLabel, darkText ? _pBrActiveText : _pBrWhite,
                x + SHEET_BTN_W / 2, SHEET_BTN_Y + SHEET_BTN_H / 2);
        }

                static void CropRebuild()
        {
            _cropPrevDirty = false;
            if (_cropPrev != IntPtr.Zero) { GdipDisposeImage(_cropPrev); _cropPrev = IntPtr.Zero; }

            if (_cropSrc != IntPtr.Zero && _cropSW > 0 && _cropSH > 0)
            {
                _cropPrev = BuildCardCover(_cropSrc, _cropSW, _cropSH, _cropCx, _cropCy, _cropZoom,
                    CROP_FW, CROP_FH, _cropBlur);
                return;
            }

            if (_cropPath.Length > 0 && _sheetThumb.TryGetValue(_cropPath, out var thumb) && thumb != IntPtr.Zero)
                _cropPrev = BuildCardCover(thumb, SCELL, SCELL, _cropCx, _cropCy, _cropZoom,
                    CROP_FW, CROP_FH, _cropBlur);

        }

        static bool InSheet(int x, int y)
            => x >= SHEET_X && x < SHEET_X + SHEET_W && y >= SHEET_Y && y < SHEET_Y + SHEET_H;

        static int SheetTabAt(int x, int y)
        {
            if (y < SHEET_TAB_Y || y >= SHEET_TAB_Y + SHEET_TAB_H) return -1;
            for (int i = 0; i < SHEET_TABS.Length; i++)
            {
                int tx = SHEET_TAB_X + i * (SHEET_TAB_W + SHEET_TAB_GAP);
                if (x >= tx && x < tx + SHEET_TAB_W) return i;
            }
            return -1;
        }

        static bool HitSheetClose(int x, int y)
            => x >= SHEET_CLOSE_X && x < SHEET_CLOSE_X + SHEET_CLOSE_S
               && y >= SHEET_CLOSE_Y && y < SHEET_CLOSE_Y + SHEET_CLOSE_S;

                static bool HitSlider(int x, int y, int cy)
            => x >= SHEET_BR_X - 8 && x < SHEET_BR_X + SHEET_BR_W + 8
               && y >= cy - 14 && y < cy + 14;

        static bool HitBrightBar(int x, int y) => HitSlider(x, y, SHEET_BR_CY);

                static bool HitBlurBar(int x, int y) => HitSlider(x, y, CROP_BR_CY);

                static int SheetCellAt(int x, int y)
        {
            if (x < SGRID_X || x >= SGRID_X + SGRID_W) return -1;
            int bot = _sheetTab == SHEET_TAB_BG ? SGRID_BOT_BG : SGRID_BOT;
            if (y < SGRID_Y || y >= bot) return -1;

            double fx = x - SGRID_X;
            double fy = y - SGRID_Y + _sheetScroll;
            if (fy < 0) return -1;
            int col = (int)(fx / SCELL_PITCH), row = (int)(fy / SCELL_PITCH);
            if (col < 0 || col >= SCELL_COLS) return -1;
            if (fx - col * SCELL_PITCH >= SCELL) return -1;
            if (fy - row * SCELL_PITCH >= SCELL) return -1;

            int idx = row * SCELL_COLS + col;
            return idx < SheetItems().Count ? idx : -1;
        }

        static bool InCropFrame(int x, int y)
            => x >= CROP_FX && x < CROP_FX + CROP_FW && y >= CROP_FY && y < CROP_FY + CROP_FH;

        static bool InSheetBtn(int x, int y, int bx)
            => x >= bx && x < bx + SHEET_BTN_W && y >= SHEET_BTN_Y && y < SHEET_BTN_Y + SHEET_BTN_H;

                static void UpdateSheetHover(int x, int y)
        {
            _sheetHoverTab = -1; _sheetHoverCell = -1; _sheetHoverBtn = -1;
            _sheetHoverClose = false; _sheetHoverBar = false;
            if (!InSheet(x, y)) return;

            if (_cropOn)
            {
                if (InSheetBtn(x, y, SHEET_BTN_OK_X)) _sheetHoverBtn = 1;
                else if (InSheetBtn(x, y, SHEET_BTN_NO_X)) _sheetHoverBtn = 0;
                else if (HitBlurBar(x, y)) _sheetHoverBar = true;
                return;
            }
            if (HitSheetClose(x, y)) { _sheetHoverClose = true; return; }
            _sheetHoverTab = SheetTabAt(x, y);
            if (_sheetHoverTab >= 0) return;
            if (_sheetTab == SHEET_TAB_BG && HitBrightBar(x, y)) { _sheetHoverBar = true; return; }
            _sheetHoverCell = SheetCellAt(x, y);
        }

                static void SheetClick(int x, int y)
        {
            if (_cropOn)
            {
                if (InSheetBtn(x, y, SHEET_BTN_OK_X)) { CropCommit(); return; }
                if (InSheetBtn(x, y, SHEET_BTN_NO_X)) { CropCancel(); return; }

                if (HitBlurBar(x, y)) { _bdrDrag = true; CropBlurFromMouse(x); return; }
                if (InCropFrame(x, y)) { _cropDrag = true; _cropDragX = x; _cropDragY = y; }
                return;
            }

            if (!InSheet(x, y)) { CloseSkinSheet(); return; }
            if (HitSheetClose(x, y)) { CloseSkinSheet(); return; }

            int tab = SheetTabAt(x, y);
            if (tab >= 0) { SheetTabSwitch(tab); return; }

            if (_sheetTab == SHEET_TAB_BG && HitBrightBar(x, y))
            {
                _brDrag = true;
                SheetBrightFromMouse(x);
                return;
            }

            int cell = SheetCellAt(x, y);
            if (cell >= 0)
            {

                _sheetDrag = true; _sheetDragCell = cell; _sheetDragY = y;
                _sheetDragMoved = 0; _sheetDragScroll0 = _sheetScroll;
            }
        }

                static bool SheetDragTick()
        {
            if (!_sheetOpen) return false;
            bool down = Key(VK_LBUTTON);

            if (_sheetDrag)
            {
                if (!down)
                {
                    _sheetDrag = false;
                    int cell = _sheetDragCell; _sheetDragCell = -1;
                    if (_sheetDragMoved <= SHEET_DRAG_SLOP && cell >= 0) SheetPickCell(cell);
                    return false;
                }
                if (GetCursorPos(out var pt))
                {
                    int cy = LayoutY(pt.y - _screenY);
                    int dy = cy - _sheetDragY;
                    if (Math.Abs(dy) > _sheetDragMoved) _sheetDragMoved = Math.Abs(dy);
                    if (_sheetDragMoved > SHEET_DRAG_SLOP)
                    {
                        float v = _sheetDragScroll0 - dy;
                        if (v < 0f) v = 0f;
                        if (v > _sheetMax) v = _sheetMax;
                        _sheetScroll = v;
                    }
                }
                return true;
            }
            if (_brDrag)
            {
                if (!down)
                {
                    _brDrag = false;

                    bool ok = PlayerSkin.Save();
                    if (!ok) SetSheetNote("写不进 player.json（看日志）", true);
                    return false;
                }
                if (GetCursorPos(out var pt)) SheetBrightFromMouse(LayoutX(pt.x - _screenX));
                return true;
            }
            if (_bdrDrag)
            {
                if (!down)
                {
                    _bdrDrag = false;

                    return false;
                }
                if (GetCursorPos(out var pt)) CropBlurFromMouse(LayoutX(pt.x - _screenX));
                return true;
            }
            if (_cropDrag)
            {
                if (!down) { _cropDrag = false; return false; }
                if (GetCursorPos(out var pt))
                {
                    int nx = LayoutX(pt.x - _screenX), ny = LayoutY(pt.y - _screenY);
                    CropPan(nx - _cropDragX, ny - _cropDragY);
                    _cropDragX = nx; _cropDragY = ny;
                }
                return true;
            }
            return false;
        }

                static void SheetWheel(int acc, int x, int y)
        {
            if (!InSheet(x, y)) return;
            if (_cropOn) { CropZoomStep(acc / 120.0 * 0.08); return; }
            float v = _sheetScroll - acc / 120f * SCELL_PITCH;
            if (v < 0f) v = 0f;
            if (v > _sheetMax) v = _sheetMax;
            _sheetScroll = v;
        }

        static void SheetTabSwitch(int tab)
        {
            if (tab == _sheetTab) return;
            _sheetTab = tab;
            _sheetItemsTab = -1;
            _sheetScroll = 0f; _sheetMax = 0f;
            _sheetHoverCell = -1; _sheetHoverBar = false;
            CropDrop();
            _sheetNote = ""; _sheetNoteBad = false;
        }

                static void SheetPickCell(int idx)
        {
            var items = SheetItems();
            if (idx < 0 || idx >= items.Count) return;
            var it = items[idx];

            if (_sheetTab == SHEET_TAB_BG)
            {
                PlayerSkin.Bg = it.Name;
                PlayerSkin.Bump();
                _infoBdDirty = true;
                _bdPend = "";
                PlayerSkin.Save();
                SetSheetNote(it.Name.Length == 0 ? "背景：恢复成每次随机" : $"背景：{SheetLabel(it)}", false);
                return;
            }
            if (_sheetTab == SHEET_TAB_AVA)
            {
                PlayerSkin.Avatar = it.Name;
                PlayerSkin.Bump();
                PlayerSkin.Save();
                SetSheetNote(it.Name.Length == 0 ? "头像：回到默认" : $"头像：{SheetLabel(it)}", false);
                return;
            }

            if (_sheetTab == SHEET_TAB_CHAR)
            {
                PlayerSkin.Char = it.Name;
                _charPend = ""; _charPendDone = false;
                PlayerSkin.Bump();
                PlayerSkin.Save();
                SetSheetNote(it.Name.Length == 0 ? "角色：不显示"
                    : (it.Name == PlayerSkin.RandomKey ? "角色：每次进这一页随机抽一张" : $"角色：{SheetLabel(it)}"), false);
                return;
            }

            if (it.Name.Length == 0)
            {
                PlayerSkin.Card = "";
                PlayerSkin.Bump();
                PlayerSkin.Save();
                SetSheetNote("名片：回到默认", false);
                return;
            }
            CropOpen(it);
        }

        static void SetSheetNote(string s, bool bad)
        {
            _sheetNote = s; _sheetNoteBad = bad;
            if (bad) Plugin.Logger.LogWarning($"[Panel] 玩家编辑：{s}");
            else Plugin.Logger.LogInfo($"[Panel] 玩家编辑：{s}");
        }

                static void SheetBrightFromMouse(int x)
        {
            double v = (double)(x - SHEET_BR_X) / SHEET_BR_W;
            if (v < 0.0) v = 0.0;
            if (v > 1.0) v = 1.0;
            PlayerSkin.BgBright = v;

        }

                static void CropBlurFromMouse(int x)
        {
            double nb = SliderToBlur((double)(x - SHEET_BR_X) / SHEET_BR_W);
            if (nb == Math.Round(ClampBlur(_cropBlur))) return;
            _cropBlur = nb;
            _cropPrevDirty = true;
        }

                static void CropOpen(SheetItem it)
        {
            CropDrop();
            _cropName = it.Name;
            _cropPath = it.Path;

            if (string.Equals(PlayerSkin.Card, it.Name, StringComparison.OrdinalIgnoreCase))
            {
                _cropCx = PlayerSkin.CardCx; _cropCy = PlayerSkin.CardCy; _cropZoom = PlayerSkin.CardZoom;
            }
            else { _cropCx = 0.5; _cropCy = 0.5; _cropZoom = 1.0; }

            _cropBlur = PlayerSkin.CardBlur;

            _cropOn = true; _cropPrevDirty = true;
            _sheetNote = ""; _sheetNoteBad = false;
            _cropSer++;
            QueueSkinJob(new SkinJob { Kind = SKIN_CROP, Serial = _cropSer, Path = it.Path });
            Plugin.Logger.LogInfo($"[Panel] 玩家编辑：进裁剪（{it.Name}，源图交给工人解）");
        }

                static void CropSrcDims(out int sw, out int sh)
        {
            if (_cropSrc != IntPtr.Zero && _cropSW > 0 && _cropSH > 0) { sw = _cropSW; sh = _cropSH; return; }
            sw = SCELL; sh = SCELL;
        }

                static void CropClamp()
        {
            if (!_cropOn) return;
            CropSrcDims(out int sw, out int sh);
            if (sw <= 0 || sh <= 0) return;
            if (_cropZoom < 1.0) _cropZoom = 1.0;
            if (_cropZoom > 8.0) _cropZoom = 8.0;
            CropRectOf(sw, sh, _cropCx, _cropCy, _cropZoom, CROP_FW, CROP_FH,
                out double sx, out double sy, out double w, out double h);
            _cropCx = (sx + w / 2) / sw;
            _cropCy = (sy + h / 2) / sh;
        }

                static void CropPan(int dxScreen, int dyScreen)
        {
            if (!_cropOn) return;
            CropSrcDims(out int sw, out int sh);
            if (sw <= 0 || sh <= 0) return;
            CropRectOf(sw, sh, _cropCx, _cropCy, _cropZoom, CROP_FW, CROP_FH,
                out double sx, out double sy, out double w, out double h);
            sx -= dxScreen * (w / CROP_FW);
            sy -= dyScreen * (h / CROP_FH);
            _cropCx = (sx + w / 2) / sw;
            _cropCy = (sy + h / 2) / sh;
            CropClamp();
            _cropPrevDirty = true;
        }

                static void CropZoomStep(double d)
        {
            if (!_cropOn) return;
            _cropZoom += d;
            CropClamp();
            _cropPrevDirty = true;
        }

        static void CropCancel()
        {
            if (!_cropOn) return;
            CropDrop();
            SetSheetNote("已取消（名片一个像素都没动）", false);
        }

        static void CropCommit()
        {
            if (!_cropOn) return;
            string nm = _cropName;
            PlayerSkin.Card = _cropName;
            PlayerSkin.CardCx = _cropCx; PlayerSkin.CardCy = _cropCy; PlayerSkin.CardZoom = _cropZoom;

            PlayerSkin.CardBlur = ClampBlur(_cropBlur);
            PlayerSkin.Bump();
            CropDrop();
            bool ok = PlayerSkin.Save();
            SetSheetNote(ok ? $"名片：{nm}（已裁剪）" : "写不进 player.json（看日志）", !ok);
        }

                static void CropDrop()
        {
            _cropOn = false; _cropDrag = false; _bdrDrag = false; _cropName = ""; _cropPath = "";
            _cropSW = _cropSH = 0;
            _cropBlur = PlayerSkin.CardBlur;
            _cropPrevDirty = false;
            _cropSer++;
            if (_cropSrc != IntPtr.Zero) { GdipDisposeImage(_cropSrc); _cropSrc = IntPtr.Zero; }
            if (_cropPrev != IntPtr.Zero) { GdipDisposeImage(_cropPrev); _cropPrev = IntPtr.Zero; }
        }

                static void DisposeSkinSheet()
        {
            CloseSkinSheet();
            CropDrop();
            DrainThumbDone(true);

            foreach (var kv in _sheetThumb)
                if (kv.Value != IntPtr.Zero) GdipDisposeImage(kv.Value);
            _sheetThumb.Clear();
            _sheetAsked.Clear();

            _avaSer++; _cardSer++; _cropSer++; _charSer++;
            lock (_skinLock)
            {
                _skinQueue.Clear();
                foreach (var d in _skinDone) FreeBmp(d.Bmp);
                _skinDone.Clear();
            }

            if (_skinAvaBmp != IntPtr.Zero) { GdipDisposeImage(_skinAvaBmp); _skinAvaBmp = IntPtr.Zero; }
            if (_skinCardBmp != IntPtr.Zero) { GdipDisposeImage(_skinCardBmp); _skinCardBmp = IntPtr.Zero; }
            if (_skinCharBmp != IntPtr.Zero) { GdipDisposeImage(_skinCharBmp); _skinCharBmp = IntPtr.Zero; }
            _avaBuilt = _cardBuilt = _charBuilt = false;
            _avaName = _cardName = _charName = null;
            _charPend = ""; _charPendDone = false;

            _sheetItems = null; _sheetItemsTab = -1;
        }

        const int INFO_CARD_X = CK_X;
        const int INFO_CARD_Y = 144;
        const int INFO_CARD_W = 453, INFO_CARD_H = 152;
        const int INFO_CARD_R = 14;
        const int INFO_CARD_ACCENT = 4;

        const int INFO_PAD = 20;
        const int INFO_COL_X = INFO_CARD_X + 300;
        const int INFO_COL_R = INFO_CARD_X + INFO_CARD_W - INFO_PAD;

        const int INFO_AVA_S = 111;
        const int INFO_AVA_X = INFO_CARD_X + INFO_PAD;
        const int INFO_AVA_Y = INFO_CARD_Y + (INFO_CARD_H - INFO_AVA_S) / 2;

        const int INFO_AVA_IMG = 100;
        const int INFO_AVA_IMG_X = INFO_AVA_X + (INFO_AVA_S - INFO_AVA_IMG) / 2;
        const int INFO_AVA_IMG_Y = INFO_AVA_Y + (INFO_AVA_S - INFO_AVA_IMG) / 2;

        const int INFO_AVA_RING = 3;
        const int INFO_RIGHT_X = INFO_AVA_X + INFO_AVA_S + 24;
        const int INFO_RIGHT_W = INFO_COL_R - INFO_RIGHT_X;

        const int INFO_RIGHT_DY = 6;
        const int INFO_NAME_CY = INFO_CARD_Y + 60 + INFO_RIGHT_DY;
        const int INFO_BAR_W = 225, INFO_BAR_H = 22;
        const int INFO_BAR_CY = INFO_CARD_Y + 106 + INFO_RIGHT_DY;
        const int INFO_BAR_PAD = 12;

        const float INFO_INK_INSET = 0.2f;

        const int INFO_NAME_SZ = 60;
        const int INFO_TITLE_SZ = 18;

        const float INFO_RAT_SZ = LAYOUT_REF_H * MAIN_FONT_RATIO;
        const float INFO_VAL_SZ = LAYOUT_REF_H * EXACT_FONT_RATIO;

        const int INFO_NAME_HOVER_BG = unchecked((int)0x8CC8C8C8);
        const int INFO_NAME_HOVER_PAD = 8;

        const int INFO_NAME_MIN_INK = 24;

        const int INFO_NAME_SHADOW_DX = 3;
        const int INFO_NAME_SHADOW_DY = 3;
        const int INFO_NAME_SHADOW_ARGB = unchecked((int)0x26000000);
        const int INFO_BAR_X = INFO_RIGHT_X;
        const int INFO_BAR_CX = INFO_BAR_X + INFO_BAR_W / 2;

        const int INFO_RB_W = 474, INFO_RB_H = 79;
        const int INFO_RB_HIDE = 35;
        const int INFO_RB_X = INFO_CARD_X + INFO_CARD_W - INFO_RB_HIDE;

        const int INFO_RB_LIFT = 22;
        const int INFO_RB_Y = INFO_CARD_Y + (INFO_CARD_H - INFO_RB_H) / 2 - INFO_RB_LIFT;

        const int INFO_RB_ART_DX = -284, INFO_RB_ART_DY = -7;
        const int INFO_RB_RAT_DECO_DY = 5;

        const int INFO_RB_RAT_NUM_DY = 5;

        const float INFO_RB_RAT_X = OVERLAY_W * MAIN_X + INFO_RB_ART_DX;
        const float INFO_RB_RAT_Y = LAYOUT_REF_H * MAIN_Y + INFO_RB_ART_DY;
        const float INFO_RB_RAT_W = OVERLAY_W * MAIN_W;
        const float INFO_RB_RAT_H = LAYOUT_REF_H * MAIN_H;
        const float INFO_RB_EXT_X = OVERLAY_W * EXACT_X + INFO_RB_ART_DX;
        const float INFO_RB_EXT_Y = LAYOUT_REF_H * EXACT_Y + INFO_RB_ART_DY;
        const float INFO_RB_EXT_W = OVERLAY_W * EXACT_W;
        const float INFO_RB_EXT_H = LAYOUT_REF_H * EXACT_H;

        const int INFO_EDIT_W = 74, INFO_EDIT_H = 38;
        const int INFO_EDIT_HIDE = 20;
        const int INFO_EDIT_X = INFO_CARD_X + INFO_CARD_W - INFO_EDIT_HIDE;
        const int INFO_EDIT_GAP = 8;
        const int INFO_EDIT_Y = INFO_RB_Y + INFO_RB_H + INFO_EDIT_GAP;
                const float INFO_EDIT_HOVER_BOOST = 0.35f;

                const int INFO_BELOW_Y = INFO_CARD_Y + INFO_CARD_H + 24;

        const int INFO_GRID_X = CATROW_X;
        const int INFO_GRID_N = 5;
        const int INFO_GRID_PITCH = 92;
        const int INFO_GRID_CELL = 84;

        const int INFO_PITCH = 24;
        const int INFO_STAT_X = 120;

        const int INFO_PAIR_DX = 226;
                const int INFO_TBL_X = CATROW_X;
        const int INFO_TBL_W = 452;
        const int INFO_TBL_R = INFO_TBL_X + INFO_TBL_W;

        const int INFO_TS_W = 467;
        const int INFO_TS_H = 241;
                const int INFO_TS_BOT = CONTENT_Y + CONTENT_H - 44;
        const int INFO_TS_Y = INFO_TS_BOT - INFO_TS_H;

        const int INFO_TS_CLEAR = INFO_TBL_X + 123, INFO_TS_FL = INFO_TBL_X + 184;
        const int INFO_TS_PD = INFO_TBL_X + 226, INFO_TS_AVG = INFO_TBL_X + 287;

        const int INFO_TS_CLEAR_W = 77, INFO_TS_AVG_W = 77;

        static readonly int[] INFO_TS_ROWS_INS = { 89, 116, 143, 170, 197, 224 };
        static readonly int[] INFO_TS_ROWS_NOINS = { 92, 126, 160, 193, 224 };

                const int INFO_TS_TEXT_DY = 2;

        const int INFO_B30_X = INFO_TBL_X + 340;
        const int INFO_B30_W = 122;
        const int INFO_B30_Y = INFO_TS_Y + 79;

        const int INFO_B30_H4 = 30;
        const int INFO_B30_GAP4 = 3;

        const int INFO_B30_ULT = 2;
        const int INFO_B30_FBD = 3;
        const int INFO_B30_EXTRA = 1;
        const int INFO_B30_DEN = 30;
        const int INFO_B30_PAD_R = 5;

        const int INFO_PC_CY = INFO_TS_Y + 224;
        const int INFO_PC_R = INFO_B30_X + INFO_B30_W;

        static readonly string[] INFO_B30_FILES = { "min", "evo", "ult", "fbd" };

                const int INFO_LINE_MAX = INFO_TS_Y - 16;

                const int INFO_PANEL_PAD = 12;
                const int INFO_PANEL_X = INFO_TBL_X - INFO_PANEL_PAD;
                const int INFO_PANEL_W = INFO_TS_W + INFO_PANEL_PAD;
                const int INFO_PANEL_VPAD = INFO_PITCH / 2;
                const int INFO_PANEL_ARGB = unchecked((int)0xD90D2745);
                const int INFO_PANEL_TOP = INFO_BELOW_Y - INFO_PANEL_VPAD;

                const int INFO_DIFF_BTN_W = 40;
                const int INFO_DIFF_BTN_H = 40;
                const int INFO_DIFF_BTN_R = 10;
                const int INFO_DIFF_GAP = 8;
                const float INFO_DIFF_HOVER_BOOST = 0.35f;
                const string INFO_DIFF_TAG = "难度";
                const string INFO_DIFF_ANY = "全部";

                const int INFO_PROC_X = INFO_TBL_X;
                const int INFO_PROC_Y = 302;
                const int INFO_PROC_W = 467;
                const int INFO_PROC_H = 115;
                const int INFO_PROC_BOX_L = INFO_PROC_X + 148;

                const int INFO_PROC_LV_CX = INFO_PROC_X + 82;
                const int INFO_PROC_LV_CY = 360;
                const int INFO_PROC_LV_N = 5;
                const int INFO_PROC_LV_CELLS = INFO_PROC_LV_N * INFO_PROC_LV_N;
                const int INFO_PROC_LV_BLOCK = 9;
                const int INFO_PROC_LV_GAP = 2;
                const int INFO_PROC_LV_LIT_ARGB = unchecked((int)0xFFA8F0F4);
                const int INFO_PROC_LV_HALF_ARGB = unchecked((int)0x99A8F0F4);
                const int INFO_PROC_LV_BG_ARGB = unchecked((int)0x88000000);
                const int INFO_PROC_LV_TEXT_MAXW = 66;

                const int INFO_PROC_ACH_CY = INFO_PROC_BAR_Y + INFO_PROC_BAR_H / 2;
                const int INFO_PROC_ACH_PAD = 14;
                const int INFO_PROC_ACH_HIT_PAD = 12;
                const int INFO_PROC_ACH_ARGB = unchecked((int)0xFF23262E);
                const int INFO_PROC_ACH_BG_ARGB = unchecked((int)0xC0182438);
                const int INFO_PROC_ACH_BG_PAD = 10;
                const int INFO_PROC_ACH_BG_H = 26;
                const int INFO_PROC_ACH_BG_DX = -6;

                const int INFO_PROC_BAR_DX = 92;
        const int INFO_PROC_BAR_DY = 10;
                const int INFO_PROC_BAR_X = INFO_PROC_X + INFO_PROC_BAR_DX;
        const int INFO_PROC_BAR_Y = INFO_PROC_Y + INFO_PROC_BAR_DY;
                const int INFO_PROC_BAR_W = 312;
                const int INFO_PROC_BAR_H = 31;
                const double INFO_PROC_BAR_POW = 1.25;
                const int INFO_PROC_CAT_CY = 369;
                const int INFO_PROC_CAT_PITCH = 24;
                const int INFO_PROC_CAT_LABEL_DX = 16;
                const int INFO_PROC_CAT_LABEL_W = 80;
                const int INFO_PROC_CAT_VALUE_DX = 88;
                const int INFO_PROC_NOTE_CY = 425;

                const int INFO_CHAR_GAP = 16;
        const int INFO_CHAR_X = INFO_TBL_R + INFO_CHAR_GAP;
                const int INFO_CHAR_PAD_R = 0;
        const int INFO_CHAR_R = CONTENT_X + CONTENT_W - INFO_CHAR_PAD_R;
        const int INFO_CHAR_W = INFO_CHAR_R - INFO_CHAR_X;
                const int INFO_CHAR_OVER = 0;
        const int INFO_CHAR_Y = INFO_RB_Y + INFO_RB_H - INFO_CHAR_OVER;
                const int INFO_CHAR_BOT = CONTENT_Y + CONTENT_H;
        const int INFO_CHAR_H = INFO_CHAR_BOT - INFO_CHAR_Y;
                const int INFO_FOOT_MAX = INFO_CHAR_X - CATROW_X - 16;

                static bool _infoCardOnly = false;

                static bool _tierGrid = false;

                static IntPtr TitleBarTex(int grade)
        {
            if (grade <= 0) return IntPtr.Zero;
            var t = Tex("playerinfo/archt" + grade + ".png");
            return t != IntPtr.Zero ? t : Tex("playerinfo/archt4.png");
        }

                static IntPtr DecoImage(string file)
        {
            if (string.IsNullOrEmpty(file)) return IntPtr.Zero;
            if (_decoTex.TryGetValue(file, out var cached)) return cached;
            IntPtr bmp = IntPtr.Zero;
            try
            {
                string path = Path.Combine(DecoDir, file);
                if (File.Exists(path) && GdipCreateBitmapFromFile(path, out bmp) != 0) bmp = IntPtr.Zero;
            }
            catch (Exception ex) { Plugin.Logger.LogWarning($"[Panel] 装饰图 {file} 读取出错: {ex.Message}"); bmp = IntPtr.Zero; }
            _decoTex[file] = bmp;
            return bmp;
        }

                static IntPtr ResolvePlateDeco((double Min, string File)[] tiers, double value)
        {
            IntPtr pick = IntPtr.Zero;
            foreach (var t in tiers)
                if (value >= t.Min - 1e-9)
                {
                    var img = DecoImage(t.File);
                    if (img != IntPtr.Zero) pick = img;
                }
            return pick;
        }

                static void PlateText(IntPtr gfx, string text, IntPtr font, int topRgb, int bottomRgb,
            IntPtr fallback, float x, float y, float w, float h)
        {
            if (font == IntPtr.Zero || string.IsNullOrEmpty(text)) return;
            var r = new GPRECTF { X = x, Y = y, Width = w, Height = h };
            var brush = CreateGradient(ref r, topRgb, bottomRgb);
            if (brush == IntPtr.Zero) brush = fallback;
            if (brush == IntPtr.Zero) return;
            GdipDrawString(gfx, text, -1, font, ref r, _formatCenter, brush);
            if (brush != fallback) GdipDeleteBrush(brush);
        }

                static string InfoProcAchText()
        {
            int done = PlayerInfo.AchDone;
            if (done < 0) done = 0;
            int total = PlayerInfo.AchTotal;
            if (total < 0) total = 0;
            if (_infoAchPct)
            {
                int pct = total > 0 ? (int)Math.Round(100.0 * done / total) : 0;
                if (pct < 0) pct = 0;
                if (pct > 100) pct = 100;
                return pct + "%";
            }
            return done + " / " + total;
        }

                static void RenderInfoProc(IntPtr gfx, IntPtr procArt, IntPtr brDim, IntPtr brVal)
        {

            Draw(gfx, procArt, INFO_PROC_X, INFO_PROC_Y, INFO_PROC_W, INFO_PROC_H);

            IntPtr BrGrade(string g) => g == "PD" ? BrAchGold() : (g == "—" ? brDim : brVal);

            {
                const int span = INFO_PROC_LV_BLOCK * INFO_PROC_LV_N
                               + INFO_PROC_LV_GAP * (INFO_PROC_LV_N - 1);
                int lvL = INFO_PROC_LV_CX - span / 2;
                int lvT = INFO_PROC_LV_CY - span / 2;
                int full = 0;
                bool half = false;
                if (PlayerInfo.LevelSeeded)
                {
                    double cells = PlayerInfo.LevelPct * INFO_PROC_LV_CELLS;
                    full = (int)Math.Floor(cells);
                    if (full < 0) full = 0;
                    if (full > INFO_PROC_LV_CELLS - 1) full = INFO_PROC_LV_CELLS - 1;
                    if (cells - full >= 0.5) half = true;
                }

                const int pitch = INFO_PROC_LV_BLOCK + INFO_PROC_LV_GAP;
                for (int i = 0; i < INFO_PROC_LV_CELLS; i++)
                {
                    int bx = lvL + (i % INFO_PROC_LV_N) * pitch;
                    int by = lvT + (i / INFO_PROC_LV_N) * pitch;
                    IntPtr cellBr = i < full ? Brush(INFO_PROC_LV_LIT_ARGB)
                                  : (i == full && half) ? Brush(INFO_PROC_LV_HALF_ARGB)
                                  : _pBrTabOff;
                    GdipFillRectangleI(gfx, cellBr, bx, by, INFO_PROC_LV_BLOCK, INFO_PROC_LV_BLOCK);
                }

                GdipFillRectangleI(gfx, Brush(INFO_PROC_LV_BG_ARGB), lvL, lvT, span, span);

                if (PlayerInfo.LevelSeeded && !string.IsNullOrEmpty(PlayerInfo.LevelText)
                    && _pFontRowNum != IntPtr.Zero)
                {
                    IntPtr lvFont = _pFontRowNum;
                    var lvBox0 = new GPRECTF { X = 0, Y = 0, Width = 10000f, Height = 10000f };
                    if (GdipMeasureString(gfx, PlayerInfo.LevelText, -1, _pFontRowNum, ref lvBox0,
                            IntPtr.Zero, out var lvBox, out _, out _) == 0
                        && lvBox.Width > INFO_PROC_LV_TEXT_MAXW)
                        lvFont = FitFontSz(gfx, PlayerInfo.LevelText, RowFamSrc(), 1 ,
                            30f * ROW_S, INFO_PROC_LV_TEXT_MAXW, _pFontRowNum, out _);

                    var lvShBr = Brush(INFO_NAME_SHADOW_ARGB);
                    RowTextC(gfx, PlayerInfo.LevelText, lvFont, lvShBr, INFO_PROC_LV_CX, INFO_PROC_LV_CY);
                    RowTextC(gfx, PlayerInfo.LevelText, lvFont, lvShBr, INFO_PROC_LV_CX - 1, INFO_PROC_LV_CY);
                    RowTextC(gfx, PlayerInfo.LevelText, lvFont, lvShBr, INFO_PROC_LV_CX + 1, INFO_PROC_LV_CY);
                    RowTextC(gfx, PlayerInfo.LevelText, lvFont, lvShBr, INFO_PROC_LV_CX, INFO_PROC_LV_CY - 1);
                    RowTextC(gfx, PlayerInfo.LevelText, lvFont, lvShBr, INFO_PROC_LV_CX, INFO_PROC_LV_CY + 1);
                    RowTextC(gfx, PlayerInfo.LevelText, lvFont, _pBrWhite, INFO_PROC_LV_CX, INFO_PROC_LV_CY);

                    if (lvFont != _pFontRowNum) GdipDeleteFont(lvFont);
                }
            }

            DrawProcAchBar(gfx);

            string achText = InfoProcAchText();
            int achRight = INFO_PROC_BAR_X + INFO_PROC_BAR_W - INFO_PROC_ACH_PAD;
            if (_pFontCjkLabel != IntPtr.Zero)
            {
                var achBox0 = new GPRECTF { X = 0, Y = 0, Width = 10000f, Height = 10000f };
                int achW = 0;
                if (GdipMeasureString(gfx, achText, -1, _pFontCjkLabel, ref achBox0, IntPtr.Zero,
                        out var achBox, out _, out _) == 0)
                    achW = (int)Math.Round(achBox.Width);

                IntPtr achBg = Tex("playerinfo/textbg.png");
                bool bgDrawn = false;
                if (achBg != IntPtr.Zero
                    && GdipGetImageWidth(achBg, out uint tbW) == 0
                    && GdipGetImageHeight(achBg, out uint tbH) == 0 && tbW > 0 && tbH > 0)
                {
                    Draw(gfx, achBg, INFO_PROC_BAR_X + INFO_PROC_BAR_W - (int)tbW + INFO_PROC_ACH_BG_DX,
                        INFO_PROC_ACH_CY - (int)tbH / 2, (int)tbW, (int)tbH);
                    bgDrawn = true;
                }
                if (!bgDrawn)
                {

                    FillRoundRect(gfx, Brush(INFO_PROC_ACH_BG_ARGB),
                        achRight - achW - INFO_PROC_ACH_BG_PAD,
                        INFO_PROC_ACH_CY - INFO_PROC_ACH_BG_H / 2,
                        achW + INFO_PROC_ACH_BG_PAD * 2, INFO_PROC_ACH_BG_H,
                        INFO_PROC_ACH_BG_H / 2);
                }
                RowTextR(gfx, achText, _pFontCjkLabel, _pBrWhite, achRight, INFO_PROC_ACH_CY);

                _infoAchRW = Math.Max(1, achW + INFO_PROC_ACH_HIT_PAD * 2);
                _infoAchRX = achRight - achW - INFO_PROC_ACH_HIT_PAD;
                _infoAchRY = INFO_PROC_BAR_Y;
                _infoAchRH = INFO_PROC_BAR_H;
            }

            int ncat = PlayerInfo.Cats.Count;
            int catShow = Math.Min(ncat, 2);
            for (int k = 0; k < catShow; k++)
            {
                var cat = PlayerInfo.Cats[k];
                int px = INFO_PROC_BOX_L + INFO_PROC_CAT_LABEL_DX;
                int py = INFO_PROC_CAT_CY + k * INFO_PROC_CAT_PITCH;
                string best = PlayerInfo.BestView(cat, _infoDiffPick);
                RowTextL(gfx, ProcCatLabel(cat.Label), _pFontCjkLabel, brVal, px, py);
                RowTextL(gfx, best, _pFontCjkLabel, BrGrade(best),
                    px + INFO_PROC_CAT_VALUE_DX, py);
            }
            if (ncat > catShow)
                RowTextL(gfx, $"（还有 {ncat - catShow} 个分类放不下 —— 分类太多了）",
                    _pFontCjkLabel, brDim, INFO_TBL_X, INFO_PROC_NOTE_CY);

            DrawInfoDiffBtn(gfx, brVal, false);
        }

                static void DrawProcAchBar(IntPtr gfx)
        {
            int total = PlayerInfo.AchTotal;
            if (total <= 0) return;
            int done = PlayerInfo.AchDone;
            if (done < 0) done = 0;
            if (done > total) done = total;

            int fill = 0;
            if (done > 0)
            {
                double x = (double)done / total;
                double f = 1.0 - Math.Pow(1.0 - x, INFO_PROC_BAR_POW);
                fill = (int)Math.Round(INFO_PROC_BAR_W * f);
                if (fill < 1) fill = 1;
                if (fill > INFO_PROC_BAR_W) fill = INFO_PROC_BAR_W;
            }
            if (fill <= 0) return;
            IntPtr arch = Tex("playerinfo/archbar.png");
            if (arch == IntPtr.Zero) return;
            if (GdipGetImageWidth(arch, out uint aw) == 0
                && GdipGetImageHeight(arch, out uint ah) == 0 && aw > 0 && ah > 0)
            {
                GdipDrawImageRectRectI(gfx, arch, INFO_PROC_BAR_X, INFO_PROC_BAR_Y, fill, INFO_PROC_BAR_H,
                    0, 0, (int)(aw * (uint)fill / INFO_PROC_BAR_W), (int)ah,
                    UnitPixel, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            }
            else
            {

                GdipFillRectangleI(gfx, Brush(unchecked((int)0xFFA8F0F4)),
                    INFO_PROC_BAR_X, INFO_PROC_BAR_Y, fill, INFO_PROC_BAR_H);
            }
        }

                static string ProcCatLabel(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            int px = 0;
            for (int i = 0; i < s.Length; i++)
            {
                int w = s[i] < 0x2E80 ? 10 : 20;
                if (px + w > INFO_PROC_CAT_LABEL_W)
                {
                    int cut = 0, p2 = 0;
                    while (cut < s.Length)
                    {
                        int w2 = s[cut] < 0x2E80 ? 10 : 20;
                        if (p2 + w2 > INFO_PROC_CAT_LABEL_W - 20) break;
                        p2 += w2; cut++;
                    }
                    return s.Substring(0, cut) + "…";
                }
                px += w;
            }
            return s;
        }

                static void RenderInfo(IntPtr gfx)
        {

            SyncRowFonts();
            PlayerInfo.Ensure();

            var brDim = Brush(unchecked((int)0xFF9AA3B8));
            var brVal = Brush(unchecked((int)0xFFEDF1F7));
            var brHdr = Brush(unchecked((int)0xFFB8DCF5));
            var brBad = Brush(unchecked((int)0xFFFF7B7B));

            var brInk = Brush(unchecked((int)0xFF23262E));

            if (!_infoCardOnly)
            {
                RowTextL(gfx, "玩家信息", _pFontCjkPage, _pBrWhite, CONTENT_X + 32, CONTENT_Y + 36);
                RowTextR(gfx, "数据来自本机存档，边打边更新", _pFontCjkLabel, brHdr,
                    CONTENT_X + CONTENT_W - 32, CONTENT_Y + 42);
            }

            var rbg = Tex("playerinfo/ratingbg.png");
            if (rbg != IntPtr.Zero) Draw(gfx, rbg, INFO_RB_X, INFO_RB_Y, INFO_RB_W, INFO_RB_H);
            else

                FillRoundRect(gfx, _pBrTabOff, INFO_RB_X, INFO_RB_Y, INFO_RB_W, INFO_RB_H, INFO_RB_H / 2);

            void DrawCardRightBits()
            {
                var editBtn = Tex("playerinfo/playeredit.png");
                if (editBtn != IntPtr.Zero)
                {

                    GdipSetClipRect(gfx, INFO_EDIT_X + INFO_EDIT_HIDE, INFO_EDIT_Y,
                        INFO_EDIT_W - INFO_EDIT_HIDE, INFO_EDIT_H, 0 );
                    Draw(gfx, editBtn, INFO_EDIT_X, INFO_EDIT_Y, INFO_EDIT_W, INFO_EDIT_H);

                    if (_infoHoverEdit)
                        DrawBoost(gfx, editBtn, INFO_EDIT_HOVER_BOOST,
                            INFO_EDIT_X, INFO_EDIT_Y, INFO_EDIT_W, INFO_EDIT_H);

                    GdipResetClip(gfx);
                }
            }

            var card = _skinCardBmp != IntPtr.Zero ? _skinCardBmp : Tex("info/card.png");
            if (card != IntPtr.Zero)
            {

                var clip = RoundRectPath(INFO_CARD_X, INFO_CARD_Y, INFO_CARD_W, INFO_CARD_H, INFO_CARD_R);
                if (clip != IntPtr.Zero)
                {
                    GdipSetClipPath(gfx, clip, 0 );
                    Draw(gfx, card, INFO_CARD_X, INFO_CARD_Y, INFO_CARD_W, INFO_CARD_H);
                    GdipResetClip(gfx);
                    GdipDeletePath(clip);
                }
                else Draw(gfx, card, INFO_CARD_X, INFO_CARD_Y, INFO_CARD_W, INFO_CARD_H);
            }
            else FillRoundRect(gfx, _pBrTabOff, INFO_CARD_X, INFO_CARD_Y, INFO_CARD_W, INFO_CARD_H, INFO_CARD_R);

            var avaBg = Tex("playerinfo/avatar_bg.png");
            if (avaBg != IntPtr.Zero)
            {
                if (_skinAvaBmp == IntPtr.Zero)
                {
                    Draw(gfx, avaBg, INFO_AVA_X, INFO_AVA_Y, INFO_AVA_S, INFO_AVA_S);
                }
                else
                {
                    var ring = RingPath(INFO_AVA_X, INFO_AVA_Y, INFO_AVA_S, INFO_AVA_S, INFO_AVA_RING);
                    if (ring != IntPtr.Zero)
                    {
                        GdipSetClipPath(gfx, ring, 0 );
                        Draw(gfx, avaBg, INFO_AVA_X, INFO_AVA_Y, INFO_AVA_S, INFO_AVA_S);
                        GdipResetClip(gfx);
                        GdipDeletePath(ring);
                    }
                }
            }
            if (_skinAvaBmp != IntPtr.Zero)
            {
                Draw(gfx, _skinAvaBmp, INFO_AVA_IMG_X, INFO_AVA_IMG_Y, INFO_AVA_IMG, INFO_AVA_IMG);
                var avaFr = Tex("playerinfo/avatar_frame.png");
                if (avaFr != IntPtr.Zero) Draw(gfx, avaFr, INFO_AVA_X, INFO_AVA_Y, INFO_AVA_S, INFO_AVA_S);
            }

            string nmLive = (_nameDlgMode == 0) ? NameDlgLive : null;
            string nmComp = (_nameDlgMode == 0) ? NameDlgComp : null;
            string nm = nmLive ?? PlayerInfo.Name;
            bool editing = nmLive != null;

            if (editing && !string.IsNullOrEmpty(nmComp) && !nm.EndsWith(nmComp, StringComparison.Ordinal))
                nm = nm + nmComp;
            float nsz;
            IntPtr nf = FitFontSz(gfx, nm, RowFamSrc(), 1 , INFO_NAME_SZ, INFO_RIGHT_W, _pFontInfoName, out nsz);

            var nbox0 = new GPRECTF { X = 0, Y = 0, Width = 10000f, Height = 10000f };
            int nInset = (int)Math.Round(nsz * INFO_INK_INSET);
            int nw = 0;
            if (GdipMeasureString(gfx, nm, -1, nf, ref nbox0, IntPtr.Zero, out var nbox, out _, out _) == 0)
                nw = (int)Math.Round(nbox.Width);
            int inkW = Math.Max(0, nw - nInset * 2);

            if (editing && inkW < INFO_NAME_MIN_INK) inkW = INFO_NAME_MIN_INK;
            _infoNameRH = Math.Max(1, (int)Math.Round(nsz));
            _infoNameRW = Math.Max(1, inkW + INFO_NAME_HOVER_PAD * 2);
            _infoNameRX = INFO_RIGHT_X - INFO_NAME_HOVER_PAD;
            _infoNameRY = INFO_NAME_CY - _infoNameRH / 2;

            if (_infoHoverName || editing)
                GdipFillRectangleI(gfx, Brush(INFO_NAME_HOVER_BG), _infoNameRX, _infoNameRY, _infoNameRW, _infoNameRH);

            int nx = INFO_RIGHT_X - nInset;

            var shBr = Brush(INFO_NAME_SHADOW_ARGB);
            int sx = nx + INFO_NAME_SHADOW_DX;
            float sy = INFO_NAME_CY + INFO_NAME_SHADOW_DY;
            RowTextL(gfx, nm, nf, shBr, sx, sy);
            RowTextL(gfx, nm, nf, shBr, sx - 1, sy);
            RowTextL(gfx, nm, nf, shBr, sx + 1, sy);
            RowTextL(gfx, nm, nf, shBr, sx, sy - 1f);
            RowTextL(gfx, nm, nf, shBr, sx, sy + 1f);
            RowTextL(gfx, nm, nf, _pBrWhite, nx, INFO_NAME_CY);

            if (editing && ((Environment.TickCount >> 9) & 1) == 0)
                GdipFillRectangleI(gfx, _pBrWhite, INFO_RIGHT_X + inkW + 3, _infoNameRY, 3, _infoNameRH);
            if (nf != _pFontInfoName) GdipDeleteFont(nf);

            if (PlayerInfo.TitleOn)
            {
                var bar = TitleBarTex(PlayerInfo.TitleGrade);
                int by = INFO_BAR_CY - INFO_BAR_H / 2;
                if (bar != IntPtr.Zero) Draw(gfx, bar, INFO_BAR_X, by, INFO_BAR_W, INFO_BAR_H);
                else

                    FillRoundRect(gfx, _pBrTabOff, INFO_BAR_X, by, INFO_BAR_W, INFO_BAR_H, INFO_BAR_H / 2);

                RowTextC(gfx, ClipTitleLine(PlayerInfo.TitleName, (INFO_BAR_W - INFO_BAR_PAD * 2) * 20 / INFO_TITLE_SZ),
                    _pFontInfoTitle, bar != IntPtr.Zero ? brInk : BrAchGold(),
                    INFO_BAR_CX, INFO_BAR_CY);
            }

            else RowTextC(gfx, PlayerInfo.TitleText, _pFontInfoTitle, brDim, INFO_BAR_CX, INFO_BAR_CY);

            if (editing)
            {
                bool blank = nm.Trim().Length == 0;
                string hint = _nameDlgHint ?? (blank ? "名字不能为空（Esc 放弃）" : "回车保存 · Esc 放弃");

                RowTextL(gfx, hint, _pFontCjkLabel, _nameDlgHint != null || blank ? brBad : brDim,
                    INFO_RIGHT_X, INFO_BAR_CY + 30);
            }

            if (rbg == IntPtr.Zero)
            {

                RowTextC(gfx, "PROTENTIAL", _pFontInfoTitle, brDim,
                    (int)(INFO_RB_X + INFO_RB_RAT_X + INFO_RB_RAT_W / 2f),
                    (int)(INFO_RB_Y + INFO_RB_RAT_Y + INFO_RB_RAT_H + 5));
                RowTextC(gfx, "EXACTIFICATION", _pFontInfoTitle, brDim,
                    (int)(INFO_RB_X + INFO_RB_EXT_X + INFO_RB_EXT_W / 2f),
                    (int)(INFO_RB_Y + INFO_RB_EXT_Y - 5));
            }

            RatingGradient(PlayerInfo.Rating, out int ratTop, out int ratBot);

            PlateText(gfx, PlayerInfo.RatingText, _pFontInfoRat, ratTop, ratBot, _pBrWhite,
                INFO_RB_X + INFO_RB_RAT_X, INFO_RB_Y + INFO_RB_RAT_Y + INFO_RB_RAT_NUM_DY,
                INFO_RB_RAT_W, INFO_RB_RAT_H);

            PlateText(gfx, PlayerInfo.ExactText, _pFontInfoVal, GRADIENT_TOP, GRADIENT_BOTTOM, brVal,
                INFO_RB_X + INFO_RB_EXT_X, INFO_RB_Y + INFO_RB_EXT_Y, INFO_RB_EXT_W, INFO_RB_EXT_H);

            var decoRat = ResolvePlateDeco(DECO_TIERS, PlayerInfo.Rating);
            if (decoRat != IntPtr.Zero)
                Draw(gfx, decoRat, INFO_RB_X + INFO_RB_ART_DX,
                    INFO_RB_Y + INFO_RB_ART_DY + INFO_RB_RAT_DECO_DY, OVERLAY_W, OVERLAY_H);
            var decoExt = ResolvePlateDeco(EXT_DECO_TIERS, PlayerInfo.Exact);
            if (decoExt != IntPtr.Zero)
                Draw(gfx, decoExt, INFO_RB_X + INFO_RB_ART_DX, INFO_RB_Y + INFO_RB_ART_DY, OVERLAY_W, OVERLAY_H);

            if (_infoCardOnly)
            {
                DrawCardRightBits();
                return;
            }

            IntPtr procArt = Tex("playerinfo/Process.png");

            _infoAchRW = 0;
            if (procArt != IntPtr.Zero)
            {
                RenderInfoProc(gfx, procArt, brDim, brVal);
            }
            else
            {

                int gy = INFO_BELOW_Y;
                RowTextL(gfx, "进度", _pFontCjkLabel, brHdr, CATROW_X, gy);

                IntPtr BrGrade(string g) => g == "PD" ? BrAchGold() : (g == "—" ? brDim : brVal);

                int ly;

                if (_tierGrid)
                {
                    var tiers = PlayerInfo.Tiers;
                    int tRows = (tiers.Count + INFO_GRID_N - 1) / INFO_GRID_N;
                    int ty = gy + 34;
                    for (int i = 0; i < tiers.Count; i++)
                    {
                        var t = tiers[i];
                        int x = INFO_GRID_X + i % INFO_GRID_N * INFO_GRID_PITCH;
                        int cy = ty + i / INFO_GRID_N * INFO_PITCH;
                        RowTextL(gfx, t.Name, _pFontCjkLabel, t.Cleared ? _pBrWhite : brDim, x, cy);

                        RowTextR(gfx, t.Grade.Length > 0 ? t.Grade : "—", _pFontCjkLabel,
                            t.Grade == "PD" ? BrAchGold() : (t.Cleared ? brVal : brDim), x + INFO_GRID_CELL, cy);
                    }
                    ly = ty + Math.Max(0, tRows - 1) * INFO_PITCH + 34;
                }
                else
                {

                    var tp = PlayerInfo.TopTier;
                    bool on = tp != null;

                    ly = gy + INFO_PITCH;

                    RowTextL(gfx, "段位", _pFontCjkLabel, brVal, CATROW_X, ly);
                    RowTextL(gfx, on ? tp.Name + " " + tp.Grade : "—", _pFontCjkLabel,
                        on ? BrGrade(tp.Grade) : brDim, CATROW_X + INFO_STAT_X, ly);
                    ly += INFO_PITCH;
                }

                int catRows = (PlayerInfo.Cats.Count + 1) / 2;
                int room = (INFO_LINE_MAX - ly) / INFO_PITCH + 1;
                if (room < 0) room = 0;
                int need = 1 + catRows;
                bool trunc = need > room;
                int budget = trunc ? room - 1 : room;
                if (budget < 0) budget = 0;
                int catShow = Math.Min(catRows, Math.Max(0, budget - 1));

                int panelRows = catShow + (budget >= 1 ? 1 : 0) + (trunc ? 1 : 0);
                int panelTop = INFO_PANEL_TOP;
                int panelBot = ly + (panelRows > 0 ? panelRows - 1 : 0) * INFO_PITCH + INFO_PANEL_VPAD;
                FillRoundRect(gfx, Brush(INFO_PANEL_ARGB), INFO_PANEL_X, panelTop,
                    INFO_PANEL_W, panelBot - panelTop, INFO_CARD_R);

                DrawInfoDiffBtn(gfx, brVal);

                for (int r = 0; r < catShow; r++)
                {
                    for (int k = 0; k < 2; k++)
                    {
                        int i = r * 2 + k;
                        if (i >= PlayerInfo.Cats.Count) break;
                        int px = CATROW_X + k * INFO_PAIR_DX;

                        string best = PlayerInfo.BestView(PlayerInfo.Cats[i], _infoDiffPick);
                        RowTextL(gfx, PlayerInfo.Cats[i].Label, _pFontCjkLabel, brVal, px, ly);
                        RowTextL(gfx, best, _pFontCjkLabel, BrGrade(best), px + INFO_STAT_X, ly);
                    }
                    ly += INFO_PITCH;
                }

                if (budget >= 1)
                {
                    RowTextL(gfx, "成就", _pFontCjkLabel, brVal, CATROW_X, ly);
                    RowTextL(gfx, $"已解锁 {PlayerInfo.AchDone} / {PlayerInfo.AchTotal}", _pFontCjkLabel, brVal,
                        CATROW_X + INFO_STAT_X, ly);
                    ly += INFO_PITCH;
                }

                if (trunc)
                {
                    int hidden = PlayerInfo.Cats.Count - catShow * 2;
                    RowTextL(gfx, $"（还有 {hidden} 行 —— 分类太多了，屏幕上放不下）",
                        _pFontCjkLabel, brDim, CATROW_X, ly);
                }
            }

            bool tsIns = PlayerInfo.Diffs.Count > 4;
            int[] tsRows = tsIns ? INFO_TS_ROWS_INS : INFO_TS_ROWS_NOINS;
            IntPtr tsArt = Tex(tsIns ? "playerinfo/TotalScorebg.png" : "playerinfo/TotalScoreNoinsbg.png");
            if (tsArt != IntPtr.Zero)
                Draw(gfx, tsArt, INFO_TBL_X, INFO_TS_Y, INFO_TS_W, INFO_TS_H);
            else
            {

                GdipFillRectangleI(gfx, _pBrTabOff, INFO_TBL_X, INFO_TS_Y, INFO_TS_W, INFO_TS_H);
                RowTextL(gfx, "成绩统计", _pFontCjkLabel, brHdr, INFO_TBL_X + 12, INFO_TS_Y + 25);
                RowTextC(gfx, "CLEAR", _pFontCjkLabel, brDim, INFO_TS_CLEAR, INFO_TS_Y + 62);
                RowTextC(gfx, "FL", _pFontCjkLabel, brDim, INFO_TS_FL, INFO_TS_Y + 62);
                RowTextC(gfx, "PD", _pFontCjkLabel, brDim, INFO_TS_PD, INFO_TS_Y + 62);
                RowTextC(gfx, "AVG", _pFontCjkLabel, brDim, INFO_TS_AVG, INFO_TS_Y + 62);

                GdipFillRectangleI(gfx, Brush(unchecked((int)0xFFE3E3E3)), INFO_TBL_X + 9,
                    INFO_TS_Y + tsRows[tsRows.Length - 1] - 12, INFO_TS_W - 18, 25);
                for (int i = 0; i < tsRows.Length - 1; i++)
                {
                    if (i >= PlayerInfo.Diffs.Count) break;

                    RowTextL(gfx, PlayerInfo.Diffs[i].Short, _pFontCjkLabel,
                        Brush(B30CardExporter.GetB10DiffColor(PlayerInfo.Diffs[i].Diff)),
                        INFO_TBL_X + 20, INFO_TS_Y + tsRows[i]);
                }
                RowTextL(gfx, PlayerInfo.Sum.Short, _pFontCjkLabel, BrSumText(), INFO_TBL_X + 20,
                    INFO_TS_Y + tsRows[tsRows.Length - 1]);
            }

            int tsN = Math.Min(PlayerInfo.Diffs.Count, tsRows.Length - 1);
            for (int i = 0; i < tsN; i++)
                TsRow(gfx, PlayerInfo.Diffs[i], INFO_TS_Y + tsRows[i] + INFO_TS_TEXT_DY, brVal);
            TsRow(gfx, PlayerInfo.Sum, INFO_TS_Y + tsRows[tsRows.Length - 1] + INFO_TS_TEXT_DY, BrSumText());

            const int b30H = INFO_B30_H4;
            const int b30Gap = INFO_B30_GAP4;
            int b30y = INFO_B30_Y;
            for (int i = 0; i < INFO_B30_FILES.Length; i++)
            {

                int bh = b30H + (i == INFO_B30_ULT || i == INFO_B30_FBD ? INFO_B30_EXTRA : 0);

                int cnt = i < PlayerInfo.B30Count.Length ? PlayerInfo.B30Count[i] : 0;
                if (cnt > INFO_B30_DEN) cnt = INFO_B30_DEN;

                int dw = cnt * INFO_B30_W / INFO_B30_DEN;
                IntPtr b30bar = Tex("playerinfo/RatingCheckBar/" + INFO_B30_FILES[i] + ".png");
                if (cnt > 0 && dw > 0 && b30bar != IntPtr.Zero
                    && GdipGetImageWidth(b30bar, out uint b30w) == 0
                    && GdipGetImageHeight(b30bar, out uint b30h) == 0 && b30w > 0 && b30h > 0)
                {

                    int sw = (int)(b30w * (uint)cnt / INFO_B30_DEN);
                    if (sw < 1) sw = 1;
                    GdipDrawImageRectRectI(gfx, b30bar, INFO_B30_X, b30y, dw, bh,
                        0, 0, sw, (int)b30h, UnitPixel, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                }

                RowTextR(gfx, cnt.ToString(CultureInfo.InvariantCulture), _pFontCjkLabel, brVal,
                    INFO_B30_X + INFO_B30_W - INFO_B30_PAD_R, b30y + bh / 2);

                b30y += bh + b30Gap;
            }

            RowTextR(gfx, PlayCounts.TotalPlays.ToString("N0", CultureInfo.InvariantCulture),
                _pFontCjkLabel, brVal, INFO_PC_R, INFO_PC_CY);

            if (_skinCharBmp != IntPtr.Zero)
                Draw(gfx, _skinCharBmp, INFO_CHAR_X, INFO_CHAR_Y, INFO_CHAR_W, INFO_CHAR_H);

            DrawCardRightBits();

            const string FOOT = "点名字改名 · 称号在成就页装备";
            IntPtr ff = FitFont(gfx, FOOT, _pCjkFamily, 0, 20f, INFO_FOOT_MAX, _pFontCjkLabel);
            RowTextL(gfx, FOOT, ff, brDim, CATROW_X, CONTENT_Y + CONTENT_H - 32);
            if (ff != _pFontCjkLabel) GdipDeleteFont(ff);
        }

                static void TsRow(IntPtr gfx, PlayerInfo.DiffRow d, int y, IntPtr br)
        {
            IntPtr fc = FitFont(gfx, d.CountText, CjkFamSrc(), 0, 20f, INFO_TS_CLEAR_W, _pFontCjkLabel);
            RowTextC(gfx, d.CountText, fc, br, INFO_TS_CLEAR, y);
            if (fc != _pFontCjkLabel) GdipDeleteFont(fc);

            RowTextC(gfx, d.Fl.ToString(CultureInfo.InvariantCulture), _pFontCjkLabel, br, INFO_TS_FL, y);
            RowTextC(gfx, d.Pd.ToString(CultureInfo.InvariantCulture), _pFontCjkLabel, br, INFO_TS_PD, y);

            IntPtr fa = FitFont(gfx, d.AccText, CjkFamSrc(), 0, 20f, INFO_TS_AVG_W, _pFontCjkLabel);
            RowTextC(gfx, d.AccText, fa, br, INFO_TS_AVG, y);
            if (fa != _pFontCjkLabel) GdipDeleteFont(fa);
        }

                static IntPtr BrSumText() => Brush(unchecked((int)0xFF0D2745));

        const int ND_EDIT_ID = 100;
        const int ND_NAME_MAX = 24;

        const int ND_SEARCH_MAX = 48;
        const uint WM_SETFONT = 0x0030, WM_COMMAND = 0x0111, WM_CLOSE = 0x0010,
                     WM_KEYDOWN = 0x0100, WM_SETFOCUS = 0x0007, WM_ACTIVATE = 0x0006,
                     EM_SETLIMITTEXT = 0x00C5;

        const uint WM_CHAR = 0x0102, WM_IME_STARTCOMPOSITION = 0x010D,
                     WM_IME_ENDCOMPOSITION = 0x010E, WM_IME_COMPOSITION = 0x010F,
                     EM_GETSEL = 0x00B0, EM_SETSEL = 0x00B1, WM_CLEAR = 0x0303;
        const int VK_BACK = 0x08;
        const int GWLP_WNDPROC = -4;
        const int ND_TRACE_MAX = 120;
        const int EN_CHANGE = 0x0300;
        const int WA_INACTIVE = 0;
        const uint LWA_ALPHA = 0x00000002;
        const uint WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, WS_EX_TOPMOST = 0x00000008;
        const uint ES_LEFT = 0x0000, ES_AUTOHSCROLL = 0x0080;
        const uint GCS_COMPSTR = 0x0008;
        const int SW_SHOW = 5;
        const int VK_RETURN = 0x0D;
        const int COLOR_BTNFACE = 15;

        const int ND_FALLBACK_W = INFO_RIGHT_W + INFO_NAME_HOVER_PAD * 2, ND_FALLBACK_H = 64;

        static volatile int _nameDlgMode;
        static IntPtr _hNameDlg = IntPtr.Zero;
        static IntPtr _hNameEdit = IntPtr.Zero;
        static IntPtr _hNameFont = IntPtr.Zero;
        static bool _nameDlgReg = false;
        static bool _nameDlgEnding = false;
        static WndProcDelegate _pNameDlgProc;

        static volatile string _nameDlgLive;

        static volatile string _nameDlgHint;

        static IntPtr _hNameEditOld = IntPtr.Zero;
        static WndProcDelegate _pNameEditProc;
        static bool _nameEditAteBack = false;

        static volatile string _nameDlgComp;
        static int _nameTraceLeft = ND_TRACE_MAX;

                static bool NameDlgOpen => _hNameDlg != IntPtr.Zero;

                static string NameDlgLive => _nameDlgLive;

                static string NameDlgComp => _nameDlgComp;

                static bool ImeComposing(IntPtr hEdit)
        {
            var himc = ImmGetContext(hEdit);
            if (himc == IntPtr.Zero) return false;
            int len;
            try { len = ImmGetCompositionStringW(himc, GCS_COMPSTR, IntPtr.Zero, 0); }
            finally { ImmReleaseContext(hEdit, himc); }
            return len > 0;
        }

                static void NameEditTrace(string what)
        {
            if (_nameTraceLeft <= 0) return;
            if (_nameTraceLeft == ND_TRACE_MAX)
                Plugin.Logger.LogInfo("[Panel] 键迹开始（改名输入框收到的键，探针，最多 " + ND_TRACE_MAX + " 行）");
            _nameTraceLeft--;
            Plugin.Logger.LogInfo("[Panel] 键迹：" + what);
            if (_nameTraceLeft == 0) Plugin.Logger.LogInfo("[Panel] 键迹到此为止（探针已静音）");
        }

                static void RefreshNameComp(IntPtr hEdit)
        {
            var himc = ImmGetContext(hEdit);
            if (himc == IntPtr.Zero) { _nameDlgComp = null; return; }
            string prev = _nameDlgComp;
            try
            {
                int len = ImmGetCompositionStringW(himc, GCS_COMPSTR, IntPtr.Zero, 0);
                if (len <= 0) _nameDlgComp = null;
                else
                {
                    var buf = Marshal.AllocHGlobal(len + 2);
                    try
                    {
                        int got = ImmGetCompositionStringW(himc, GCS_COMPSTR, buf, len);
                        _nameDlgComp = got > 0 ? Marshal.PtrToStringUni(buf, got / 2) : null;
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                }
            }
            finally { ImmReleaseContext(hEdit, himc); }

            if (!string.Equals(prev, _nameDlgComp, StringComparison.Ordinal))
                NameEditTrace("IME 拼字 → " + (_nameDlgComp == null ? "（空）" : "「" + _nameDlgComp + "」"));
        }

                static void NameEditBack()
        {
            if (_hNameEdit == IntPtr.Zero) return;
            long sel = SendMessageW(_hNameEdit, EM_GETSEL, IntPtr.Zero, IntPtr.Zero).ToInt64();
            int a = (int)(sel & 0xFFFF), b = (int)((sel >> 16) & 0xFFFF);
            if (a == b)
            {
                if (a <= 0) return;
                SendMessageW(_hNameEdit, EM_SETSEL, (IntPtr)(a - 1), (IntPtr)b);
            }
            SendMessageW(_hNameEdit, WM_CLEAR, IntPtr.Zero, IntPtr.Zero);
            _nameDlgLive = NameDlgText();
        }

                static IntPtr NameEditProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {

            if (msg == WM_KEYDOWN) NameEditTrace("KEYDOWN vk=0x" + wParam.ToInt64().ToString("X2"));
            else if (msg == WM_CHAR) NameEditTrace("CHAR 0x" + (wParam.ToInt64() & 0xFFFF).ToString("X2"));

            if (msg == WM_KEYDOWN && wParam.ToInt64() == VK_BACK && !ImeComposing(hWnd))
            {
                _nameEditAteBack = false;
                NameEditBack();
                _nameEditAteBack = true;
                return IntPtr.Zero;
            }
            if (msg == WM_CHAR && (wParam.ToInt64() & 0xFFFF) == 0x08 && _nameEditAteBack)
            {
                _nameEditAteBack = false;
                return IntPtr.Zero;
            }

            if (msg == WM_IME_STARTCOMPOSITION || msg == WM_IME_COMPOSITION) RefreshNameComp(hWnd);
            else if (msg == WM_IME_ENDCOMPOSITION) { _nameDlgComp = null; NameEditTrace("IME 拼字结束"); }

            return _hNameEditOld != IntPtr.Zero
                ? CallWindowProcW(_hNameEditOld, hWnd, msg, wParam, lParam)
                : DefWindowProcW(hWnd, msg, wParam, lParam);
        }

                static void OpenNameDlg(bool search = false)
        {
            int mode = search ? 1 : 0;
            if (_hNameDlg != IntPtr.Zero)
            {

                if (_nameDlgMode == mode)
                {
                    SetForegroundWindow(_hNameDlg);
                    if (_hNameEdit != IntPtr.Zero) SetFocus(_hNameEdit);
                    return;
                }
                EndNameDlg(true, true);
                if (_hNameDlg != IntPtr.Zero) return;
            }

            if (!_nameDlgReg)
            {

                _pNameDlgProc = NameDlgProc;
                var wc = new WNDCLASS
                {
                    lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_pNameDlgProc),
                    hbrBackground = (IntPtr)(COLOR_BTNFACE + 1),
                    lpszClassName = "InFalsusRatingNameDlg"
                };
                if (RegisterClassW(ref wc) == 0)
                {
                    Plugin.Logger.LogError("[Panel] 改名窗窗口类注册失败");
                    return;
                }
                _nameDlgReg = true;
            }

            int rx, ry, rw, rh;
            if (search)
            {

                rx = FLT_SRCH_X; ry = FLT_SRCH_Y; rw = FLT_SRCH_W; rh = FLT_SRCH_H;
            }
            else
            {
                rx = _infoNameRX; ry = _infoNameRY; rw = _infoNameRW; rh = _infoNameRH;
                if (rw <= 0 || rh <= 0)
                {
                    rw = ND_FALLBACK_W;
                    rh = ND_FALLBACK_H;
                    rx = INFO_RIGHT_X - INFO_NAME_HOVER_PAD;
                    ry = INFO_NAME_CY - rh / 2;
                }
            }

            rx = (int)Math.Round(rx * (double)_viewW / PW);
            ry = (int)Math.Round(ry * (double)_viewH / PH);
            rw = Math.Max(1, (int)Math.Round(rw * (double)_viewW / PW));
            rh = Math.Max(1, (int)Math.Round(rh * (double)_viewH / PH));
            var pt = new POINT { x = rx, y = ry };
            int x = 0, y = 0;
            if (ClientToScreen(_hWnd, ref pt)) { x = pt.x; y = pt.y; }
            else Plugin.Logger.LogInfo("[Panel] 改名窗量不到屏幕坐标，先摆在 (0,0)");

            var rc = new RECT { left = 0, top = 0, right = rw, bottom = rh };
            if (!AdjustWindowRectEx(ref rc, WS_POPUP, false, WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_TOPMOST))
            {
                Plugin.Logger.LogError("[Panel] 改名窗量不出整窗尺寸");
                return;
            }
            int ww = rc.right - rc.left, wh = rc.bottom - rc.top;

            _hNameDlg = CreateWindowExW(WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_TOPMOST,
                "InFalsusRatingNameDlg", "修改玩家名字（隐形输入窗）", WS_POPUP,
                x, y, ww, wh, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (_hNameDlg == IntPtr.Zero) { Plugin.Logger.LogError("[Panel] 改名窗建不出来"); return; }

            if (!SetLayeredWindowAttributes(_hNameDlg, 0, 0, LWA_ALPHA))
                Plugin.Logger.LogError("[Panel] 改名窗压不透明（照样开着，会看得见）");

            _hNameFont = CreateFontW(-14, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Microsoft YaHei UI");
            if (_hNameFont == IntPtr.Zero)
                _hNameFont = CreateFontW(-14, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "SimSun");

            string cur = search ? (_b30Search ?? "") : (Plugin.PlayerName ?? "");

            _nameDlgMode = mode;
            if (search) _b30SearchBefore = _b30Search;
            _hNameEdit = CreateWindowExW(0, "EDIT", "",
                WS_CHILD | WS_VISIBLE | ES_LEFT | ES_AUTOHSCROLL,
                0, 0, ww, wh,
                _hNameDlg, (IntPtr)ND_EDIT_ID, IntPtr.Zero, IntPtr.Zero);
            if (_hNameEdit == IntPtr.Zero) Plugin.Logger.LogError("[Panel] 改名窗的输入框建不出来");
            else
            {
                if (_hNameFont != IntPtr.Zero) SendMessageW(_hNameEdit, WM_SETFONT, _hNameFont, (IntPtr)1);

                SetWindowTextW(_hNameEdit, cur);
                SendMessageW(_hNameEdit, EM_SETLIMITTEXT,
                    (IntPtr)(search ? ND_SEARCH_MAX : ND_NAME_MAX), IntPtr.Zero);

                _pNameEditProc = NameEditProc;
                _hNameEditOld = SetWindowLongPtrW(_hNameEdit, GWLP_WNDPROC,
                    Marshal.GetFunctionPointerForDelegate(_pNameEditProc));
                if (_hNameEditOld == IntPtr.Zero)
                    Plugin.Logger.LogError("[Panel] 输入框子类化失败（Backspace 和拼字显示会不灵，其余照常）");
            }

            _nameDlgLive = cur;
            _nameDlgHint = null;
            _nameDlgComp = null;
            _nameEditAteBack = false;

            ShowWindow(_hNameDlg, SW_SHOW);
            SetForegroundWindow(_hNameDlg);
            if (_hNameEdit != IntPtr.Zero)
            {
                SetFocus(_hNameEdit);

                int end = cur.Length;
                SendMessageW(_hNameEdit, EM_SETSEL, (IntPtr)end, (IntPtr)end);
            }
            Plugin.Logger.LogInfo(search
                ? "[Panel] 搜索已开始（隐形输入窗，打字实时过滤成绩列表）"
                : "[Panel] 改名已开始（隐形输入窗，打字实时显示在名字上）");
        }

                static string NameDlgText()
        {
            if (_hNameEdit == IntPtr.Zero) return "";

            var sb = new StringBuilder(Math.Max(ND_NAME_MAX, ND_SEARCH_MAX) + 8);
            GetWindowTextW(_hNameEdit, sb, sb.Capacity);
            return sb.ToString();
        }

                static bool CommitNameDlg()
        {
            string name = NameDlgText().Trim();
            if (name.Length == 0)
            {
                _nameDlgHint = "名字不能为空（Esc 放弃）";
                return false;
            }
            if (string.Equals(name, Plugin.PlayerName, StringComparison.Ordinal)) return true;
            if (!Plugin.SavePlayerName(name))
            {
                _nameDlgHint = "写 name.txt 失败，看日志";
                return false;
            }

            PlayerInfo.Invalidate();
            return true;
        }

                static void EndNameDlg(bool save, bool force)
        {
            if (_nameDlgEnding) return;
            var h = _hNameDlg;
            if (h == IntPtr.Zero) return;

            int mode = _nameDlgMode;
            if (mode == 1)
            {

                _b30Search = (save ? (NameDlgText() ?? "") : (_b30SearchBefore ?? "")).Trim();
                MarkDirty();
            }
            else if (save)
            {

                if (!CommitNameDlg())
                {
                    if (!force) return;
                    Plugin.Logger.LogInfo("[Panel] 改名收工：这一串没存下去（上面那条提示里有原因）");
                }
            }
            _nameDlgEnding = true;
            try
            {

                _nameDlgLive = null;
                _nameDlgHint = null;
                _nameDlgComp = null;
                DestroyWindow(h);
            }
            finally { _nameDlgEnding = false; }
        }

                static void CloseNameDlg()
        {
            var h = Interlocked.CompareExchange(ref _hNameDlg, IntPtr.Zero, IntPtr.Zero);
            if (h != IntPtr.Zero) PostMessageW(h, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

                static IntPtr NameDlgProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_COMMAND)
            {

                long wp = wParam.ToInt64();
                int id = (int)(wp & 0xFFFF), code = (int)((wp >> 16) & 0xFFFF);
                if (code == EN_CHANGE && id == ND_EDIT_ID)
                {
                    string t = NameDlgText();
                    _nameDlgLive = t;

                    if (_nameDlgHint != null && t.Trim().Length > 0) _nameDlgHint = null;
                }
                return IntPtr.Zero;
            }

            if (msg == WM_SETFOCUS)
            {
                if (_hNameEdit != IntPtr.Zero) SetFocus(_hNameEdit);
                return IntPtr.Zero;
            }

            if (msg == WM_ACTIVATE)
            {
                if ((int)(wParam.ToInt64() & 0xFFFF) == WA_INACTIVE) EndNameDlg(true, true);
                return IntPtr.Zero;
            }

            if (msg == WM_CLOSE) { EndNameDlg(true, true); return IntPtr.Zero; }
            if (msg == WM_DESTROY)
            {

                if (_hNameFont != IntPtr.Zero) { DeleteObject(_hNameFont); _hNameFont = IntPtr.Zero; }
                _hNameEdit = IntPtr.Zero;
                _hNameEditOld = IntPtr.Zero;
                _nameEditAteBack = false;
                _nameDlgComp = null;
                _hNameDlg = IntPtr.Zero;

                _nameDlgLive = null;
                _nameDlgHint = null;

                _nameDlgMode = 0;
                _b30SearchBefore = null;
                Plugin.Logger.LogInfo("[Panel] 改名收工");
                return IntPtr.Zero;
            }
            return DefWindowProcW(hWnd, msg, wParam, lParam);
        }

                static bool EnsureFrameCanvas()
        {
            if (_pFrameBmp != IntPtr.Zero && _pFrameGfx != IntPtr.Zero) return true;

            if (_pFrameBmp != IntPtr.Zero) { GdipDisposeImage(_pFrameBmp); _pFrameBmp = IntPtr.Zero; }
            _pFrameGfx = IntPtr.Zero;

            if (GdipCreateBitmapFromScan0(PW, PH, 0, PixelFormat32bppARGB, IntPtr.Zero, out _pFrameBmp) != 0
                || _pFrameBmp == IntPtr.Zero)
            { _pFrameBmp = IntPtr.Zero; return false; }

            if (GdipGetImageGraphicsContext(_pFrameBmp, out _pFrameGfx) != 0 || _pFrameGfx == IntPtr.Zero)
            {
                GdipDisposeImage(_pFrameBmp);
                _pFrameBmp = IntPtr.Zero;
                _pFrameGfx = IntPtr.Zero;
                return false;
            }
            return true;
        }

                static bool ParallaxPush(IntPtr gfx, int dx, int dy)
        {
            if (dx == 0 && dy == 0) return false;
            if (GdipCreateMatrix2(1f, 0f, 0f, 1f, dx, dy, out var m) != 0 || m == IntPtr.Zero)
                return false;
            int st = GdipSetWorldTransform(gfx, m);
            GdipDeleteMatrix(m);
            return st == 0;
        }

                static void ParallaxReset(IntPtr gfx)
        {
            if (GdipCreateMatrix2(1f, 0f, 0f, 1f, 0f, 0f, out var m) != 0 || m == IntPtr.Zero) return;
            GdipSetWorldTransform(gfx, m);
            GdipDeleteMatrix(m);
        }

                static void ParallaxAim(int mx, int my)
        {

            double nx = (mx - _screenX - _viewW * 0.5) / (_viewW * 0.5);
            double ny = (my - _screenY - _viewH * 0.5) / (_viewH * 0.5);
            _parTX = nx < -1 ? -1 : (nx > 1 ? 1 : nx);
            _parTY = ny < -1 ? -1 : (ny > 1 ? 1 : ny);
        }

                static void ParallaxSmooth()
        {
            _parX += (_parTX - _parX) * PAR_LERP;
            _parY += (_parTY - _parY) * PAR_LERP;
            if (Math.Abs(_parTX - _parX) < 0.002) _parX = _parTX;
            if (Math.Abs(_parTY - _parY) < 0.002) _parY = _parTY;
        }

        static void Render()
        {
            if (!EnsureFrameCanvas()) return;
            IntPtr bmp = _pFrameBmp, gfx = _pFrameGfx;

            GdipGraphicsClear(gfx, 0);
            GdipResetClip(gfx);
            GdipSetSmoothingMode(gfx, 3 );

            ParallaxReset(gfx);

            {

                var cur = NAV[_activeNav];

                int parBgDx = (int)Math.Round(-_parX * PAR_BG_MAX);
                int parBgDy = (int)Math.Round(-_parY * PAR_BG_MAX);
                int parFgDx = (int)Math.Round(_parX * PAR_FG_MAX);
                int parFgDy = (int)Math.Round(_parY * PAR_FG_MAX);

                bool parBgOn = ParallaxPush(gfx, parBgDx, parBgDy);

                Draw(gfx, Tex("base/panel_base.png"), 0, 0, PW, PH);

                DrawThemeBackground(gfx, THEME_FIN);
                DrawThemeBackground(gfx, THEME_INS);
                if (OnInfoPage) DrawInfoBackdrop(gfx);

                var pg = Tex($"pages/{cur.Page}.png");
                if (pg != IntPtr.Zero) Draw(gfx, pg, CONTENT_X, CONTENT_Y, CONTENT_W, CONTENT_H);

                for (int i = 0; i < NAV.Length; i++)
                {
                    var it = NAV[i];
                    string state = (i == _activeNav) ? "active" : (i == _hoverNav ? "hover" : "normal");

                    IntPtr plate = NavPlate(state);
                    IntPtr plateF = NavPlate(state, THEME_FIN);
                    IntPtr plateI = NavPlate(state, THEME_INS);
                    DrawNavPlate(gfx, plateI, plateF, plate, it.X, it.Y, NAV_W, NAV_H);

                    var label = Tex($"nav/label_{it.Key}.png");
                    if (label != IntPtr.Zero) Draw(gfx, label, it.X, it.Y, NAV_W, NAV_H);
                    else DrawText(gfx, it.Label, _pFontCjkLabel,
                        i == _activeNav ? ActiveLabelBrush() : _pBrWhite,
                        it.X, it.Y, NAV_W, NAV_H, LABEL_TEXT_DY);

                    if (it.Key == "achieve")
                    {
                        int unseen = Achievements.UnseenCount;
                        if (unseen > 0)
                        {
                            int bx = it.X + NAV_W - 16, by = it.Y + 14;
                            DrawDot(gfx, BrAchNew(), bx, by, 11);

                            RowTextC(gfx, unseen > 9 ? "9+" : unseen.ToString(),
                                AchBadgeFont(), _pBrWhite, bx, by + 1);
                        }
                    }
                }

                Draw(gfx, Tex(_hoverClose ? "base/close_hover.png" : "base/close_normal.png"),
                    CLOSE_X, CLOSE_Y, CLOSE_W, CLOSE_H);

                if (parBgOn) ParallaxReset(gfx);

                bool parFgOn = ParallaxPush(gfx, parFgDx, parFgDy);
                if (OnInfoPage) RenderInfo(gfx);
                else if (OnB30Page) RenderB30(gfx);
                else if (OnRecommendPage) RenderRecommend(gfx);
                else if (OnTaskPage) RenderTask(gfx);
                else if (OnAchievePage) RenderAchieve(gfx);
                else if (OnConfigPage) RenderConfig(gfx);
                else if (pg == IntPtr.Zero)
                {
                    GdipFillRectangleI(gfx, _pBrShade, CONTENT_X, CONTENT_Y, CONTENT_W, CONTENT_H);
                    DrawText(gfx, cur.Label + " · 页面素材待补", _pFontCjkPage, _pBrWhite,
                        CONTENT_X, CONTENT_Y, CONTENT_W, CONTENT_H, PAGE_TEXT_DY);
                }

                if (OnB30Page) DrawExButtons(gfx);

                if (OnB30Page) DrawB30Filter(gfx);

                if (parFgOn) ParallaxReset(gfx);

                if (_sheetOpen) DrawSkinSheet(gfx);

                IntPtr outBmp = bmp;
                int outW = PW, outH = PH;
                if (_viewW != PW || _viewH != PH)
                {
                    if (!EnsureScaleCanvas()) return;
                    GdipGraphicsClear(_pScaleGfx, 0);
                    GdipDrawImageRectI(_pScaleGfx, bmp, 0, 0, _viewW, _viewH);
                    outBmp = _pScaleBmp; outW = _viewW; outH = _viewH;
                }
                CompositeToDib(outBmp, outW, outH);

                _panelFxP = PanelAnimP();
                PanelFxDib(_panelFxP);

                if (parBgOn) DibClipToShell(
                    (int)Math.Round(parBgDx * (double)_viewW / PW),
                    (int)Math.Round(parBgDy * (double)_viewH / PH));
            }

        }

        static int RowCount()
        {

            EnsureB30View();
            return _b30View.Count;
        }

        static object B30ListRef()
        {
            if (_activeTab == 0) return Plugin.ChartRatings;
            if (_activeTab == 1) return Plugin.AllExactCharts;
            return Plugin.PlayHistory;
        }

        static int SourceCount()
        {
            if (_activeTab == 0) return Plugin.ChartRatings?.Count ?? 0;
            if (_activeTab == 1) return Plugin.AllExactCharts?.Count ?? 0;
            return Plugin.PlayHistory?.Count ?? 0;
        }

        static string B30EffectiveSearch()
        {
            string s;
            if (_nameDlgMode == 1 && _hNameDlg != IntPtr.Zero) s = NameDlgLive;
            else s = _b30Search;
            s = (s ?? "").Trim();
            return s.Length == 0 ? "" : s.ToLowerInvariant();
        }

        static int B30DiffBit(string difficulty)
        {
            switch ((difficulty ?? "").Trim().ToLowerInvariant())
            {
                case "minimal": return 1;
                case "evolved": return 2;
                case "ultimate": return 4;
                case "forbidden": return 8;
                default: return 0;
            }
        }

        sealed class B30Row
        {
            public int Src;
            public string SongId = "";
            public string Difficulty = "";
            public double Constant;
            public bool HasScore;
            public long Score;
            public double Rating = double.NaN;
            public double Ext = double.NaN;
            public string Title = "";
            public string Hay = "";
        }

        static double B30ExtOf(ResultDetail d, double constant)
        {
            if (d == null || constant <= 0) return double.NaN;
            int all = d.SumAll();
            if (all <= 0) return double.NaN;
            return 100.0 * d.Sum(ResultDetail.BShiny) * constant / all;
        }

        static bool B30BuildEnt(int src, out B30Row e)
        {
            e = null;
            ResultDetail d;
            string songId, diff;
            double constant;
            bool hasScore = false;
            ulong score = 0;
            double rating = double.NaN;
            double ext = double.NaN;

            if (_activeTab == 0)
            {
                var list = Plugin.ChartRatings;
                if (list == null || src < 0 || src >= list.Count) return false;
                var c = list[src];
                songId = c.SongId; diff = c.Difficulty; constant = c.Constant;
                d = c.Detail; score = c.Score; hasScore = true;
                rating = c.Rating;
                ext = c.ExactRate;
            }
            else if (_activeTab == 1)
            {
                var list = Plugin.AllExactCharts;
                if (list == null || src < 0 || src >= list.Count) return false;
                var c = list[src];
                songId = c.SongId; diff = c.Difficulty;
                constant = ConstantOf(c.SongId, c.Difficulty);
                d = c.Detail;
                if (d != null) { score = d.Score; hasScore = true; }
                ext = c.Rate;
                if (d != null && constant > 0)
                    rating = RatingEngine.SingleRating(constant, d.Score, d.Pace, d.Clear);
            }
            else
            {
                var list = Plugin.PlayHistory;
                if (list == null || src < 0 || src >= list.Count) return false;
                var c = list[src];
                songId = c.SongId; diff = c.Difficulty;
                constant = ConstantOf(c.SongId, c.Difficulty);
                d = c.Detail;

                hasScore = !c.ExamFail;
                if (hasScore) score = c.Score;
                ext = B30ExtOf(d, constant);
                if (d != null && constant > 0)
                    rating = RatingEngine.SingleRating(constant, d.Score, d.Pace, d.Clear);
            }

            songId = songId ?? "";
            diff = diff ?? "";
            string name = SongInfoCache.GetName(songId) ?? songId;
            string artist = SongInfoCache.GetArtist(songId) ?? "";

            e = new B30Row
            {
                Src = src,
                SongId = songId,
                Difficulty = diff,
                Constant = constant,
                HasScore = hasScore,
                Score = hasScore ? (long)score : 0,
                Rating = rating,
                Ext = ext,
                Title = name.ToLowerInvariant(),
                Hay = (name + "\n" + artist + "\n" + songId).ToLowerInvariant(),
            };
            return true;
        }

                static bool B30RangePass(int v, int lo, int hi)
        {
            if (v < 0) return lo <= 0;
            return v >= lo && v <= hi;
        }

        static bool B30RowPass(B30Row e, string term)
        {
            int bit = B30DiffBit(e.Difficulty);
            if (bit != 0 && (_b30DiffMask & bit) == 0) return false;

            if (!B30RangePass(e.HasScore ? (int)e.Score : -1, _b30ScoreLo, _b30ScoreHi)) return false;
            if (!B30RangePass(double.IsNaN(e.Rating) ? -1 : (int)Math.Round(e.Rating * 100.0),
                    _b30PttLo, _b30PttHi)) return false;
            if (!B30RangePass(double.IsNaN(e.Ext) ? -1 : (int)Math.Round(e.Ext),
                    _b30ExtLo, _b30ExtHi)) return false;

            if (term.Length > 0 && e.Hay.IndexOf(term, StringComparison.Ordinal) < 0) return false;
            return true;
        }

        static int B30CmpNum(double a, double b)
        {
            double va = double.IsNaN(a) ? (_b30SortDesc ? double.NegativeInfinity : double.PositiveInfinity) : a;
            double vb = double.IsNaN(b) ? (_b30SortDesc ? double.NegativeInfinity : double.PositiveInfinity) : b;
            int c = va.CompareTo(vb);
            return _b30SortDesc ? -c : c;
        }

        static int B30CmpScore(B30Row a, B30Row b)
        {
            long va = a.HasScore ? a.Score : (_b30SortDesc ? long.MinValue : long.MaxValue);
            long vb = b.HasScore ? b.Score : (_b30SortDesc ? long.MinValue : long.MaxValue);
            int c = va.CompareTo(vb);
            return _b30SortDesc ? -c : c;
        }

        static int B30Compare(B30Row a, B30Row b)
        {
            int c;
            switch (_b30Sort)
            {
                case 1: c = B30CmpNum(a.Rating, b.Rating); break;
                case 2: c = B30CmpScore(a, b); break;
                case 3: c = B30CmpNum(a.Ext, b.Ext); break;
                case 4:
                    c = string.Compare(a.Title, b.Title, StringComparison.Ordinal);
                    if (_b30SortDesc) c = -c;
                    break;
                default:
                    c = a.Src.CompareTo(b.Src);
                    if (_b30SortDesc) c = -c;
                    break;
            }
            return c != 0 ? c : a.Src.CompareTo(b.Src);
        }

                static void EnsureB30View()
        {
            object src0 = B30ListRef();
            string term = B30EffectiveSearch();

            if (ReferenceEquals(src0, _b30ViewSrc)
                && _activeTab == _b30ViewTab
                && _b30DiffMask == _b30ViewDiffMask
                && _b30ScoreLo == _b30ViewScoreLo && _b30ScoreHi == _b30ViewScoreHi
                && _b30PttLo == _b30ViewPttLo && _b30PttHi == _b30ViewPttHi
                && _b30ExtLo == _b30ViewExtLo && _b30ExtHi == _b30ViewExtHi
                && _b30Sort == _b30ViewSort && _b30SortDesc == _b30ViewSortDesc
                && string.Equals(term, _b30ViewTerm ?? "", StringComparison.Ordinal))
                return;

            bool filterDirty =
                _activeTab != _b30ViewTab
                || _b30DiffMask != _b30ViewDiffMask
                || _b30ScoreLo != _b30ViewScoreLo || _b30ScoreHi != _b30ViewScoreHi
                || _b30PttLo != _b30ViewPttLo || _b30PttHi != _b30ViewPttHi
                || _b30ExtLo != _b30ViewExtLo || _b30ExtHi != _b30ViewExtHi
                || _b30Sort != _b30ViewSort || _b30SortDesc != _b30ViewSortDesc
                || !string.Equals(term, _b30ViewTerm ?? "", StringComparison.Ordinal);

            _b30ViewSrc = src0;
            _b30ViewTab = _activeTab;
            _b30ViewDiffMask = _b30DiffMask;
            _b30ViewScoreLo = _b30ScoreLo; _b30ViewScoreHi = _b30ScoreHi;
            _b30ViewPttLo = _b30PttLo; _b30ViewPttHi = _b30PttHi;
            _b30ViewExtLo = _b30ExtLo; _b30ViewExtHi = _b30ExtHi;
            _b30ViewSort = _b30Sort; _b30ViewSortDesc = _b30SortDesc;
            _b30ViewTerm = term ?? "";

            if (_b30OpenRow >= 0) { _b30OpenRow = -1; B30OpenAnimReset(); }
            _b30OpenSrc = null;
            if (filterDirty) { _scroll = 0f; _dragging = false; }

            _b30View.Clear();
            _b30CutView = 0;

            int n = SourceCount();
            if (n <= 0) return;

            bool passAll = _b30DiffMask == 0xF
                && _b30ScoreLo <= 0 && _b30ScoreHi >= FLT_SCORE_MAX
                && _b30PttLo <= 0 && _b30PttHi >= FLT_PTT_MAX
                && _b30ExtLo <= 0 && _b30ExtHi >= FLT_EXT_MAX
                && term.Length == 0;

            if (passAll && _b30Sort == 0)
            {
                for (int i = 0; i < n; i++) _b30View.Add(i);
            }
            else
            {
                var rows = new List<B30Row>(n);
                for (int i = 0; i < n; i++)
                {
                    if (!B30BuildEnt(i, out var e)) continue;
                    if (!B30RowPass(e, term)) continue;
                    rows.Add(e);
                }
                if (_b30Sort != 0 && rows.Count > 1) rows.Sort(B30Compare);
                for (int i = 0; i < rows.Count; i++) _b30View.Add(rows[i].Src);
            }

            if (_b30Sort == 0)
            {
                int cut = _activeTab == 0 ? B30_RATE_TOP : (_activeTab == 1 ? B30_EXACT_TOP : 0);
                if (cut > 0)
                {
                    int c = 0;
                    for (int i = 0; i < _b30View.Count; i++) if (_b30View[i] < cut) c++;
                    if (c > 0 && c < _b30View.Count) _b30CutView = c;
                }
            }
        }

        static int B30SrcOf(int i)
            => i >= 0 && i < _b30View.Count ? _b30View[i] : -1;

        static int RowOffset(int i)
            => i * ROW_PITCH + (_b30OpenRow >= 0 && i > _b30OpenRow ? DETAIL_BLOCK : 0);

        static int ListContentH(int n)
        {
            if (n <= 0) return 0;
            int h = n * ROW_PITCH - ROW_GAP;
            if (_b30OpenRow < 0 || _b30OpenRow >= n) return h;
            return _b30OpenRow == n - 1 ? h + DETAIL_BLOCK - ROW_GAP : h + DETAIL_BLOCK;
        }

        static int FirstVisibleRow(int n, int y0)
        {
            int g = _b30OpenRow >= 0 ? (y0 - DETAIL_BLOCK) / ROW_PITCH : y0 / ROW_PITCH;
            if (g < 0) g = 0;
            if (g >= n) g = n - 1;
            while (g > 0 && RowOffset(g - 1) + ROW_H > y0) g--;
            while (g < n && RowOffset(g) + ROW_H <= y0) g++;
            return g < n ? g : n - 1;
        }

        static string HistClearWord(int clear)
        {
            switch (clear)
            {
                case 4: return "PD";
                case 3: return "FL";
                case 2: return "CLEAR";
                case 1: return "FAIL";
                default: return "-";
            }
        }

        static string HistSubLine(HistoryEntry c)
        {
            string artist = SongInfoCache.GetArtist(c.SongId) ?? "";
            string time = HistTime(c.Seconds);
            if (artist.Length == 0) return time;
            if (time.Length == 0) return artist;
            return artist + " · " + time;
        }

        internal static string HistTime(ulong sec)
        {
            if (sec == 0) return "";

            if (sec > 253402300799UL) return "";
            try
            {
                return DateTimeOffset.FromUnixTimeSeconds((long)sec)
                    .LocalDateTime.ToString("yyyy-MM-dd HH:mm");
            }
            catch { return ""; }
        }

        static double ConstantOf(string songId, string difficulty)
        {
            var src = Plugin.ChartRatings;
            if (src == null) return 0;
            if (!ReferenceEquals(src, _constSource))
            {
                _constSource = src;
                _constByChart.Clear();
                foreach (var c in src) _constByChart[c.SongId + "|" + c.Difficulty] = c.Constant;
            }
            return _constByChart.TryGetValue(songId + "|" + difficulty, out var v) ? v : 0;
        }

        static IntPtr RowBg(string difficulty)
        {
            if (!_rowBgPath.TryGetValue(difficulty, out var path))
            {
                path = B30CardExporter.FindExactLayerFile(difficulty);
                _rowBgPath[difficulty] = path;
            }
            return TexAbs(path);
        }

        static IntPtr TexAbs(string full)
        {
            if (string.IsNullOrEmpty(full)) return IntPtr.Zero;
            if (_texAbs.TryGetValue(full, out var c)) return c;

            IntPtr bmp = IntPtr.Zero;
            if (File.Exists(full) && GdipCreateBitmapFromFile(full, out bmp) != 0) bmp = IntPtr.Zero;
            _texAbs[full] = bmp;
            return bmp;
        }

        static IntPtr Brush(int argb)
        {
            if (_brCache.TryGetValue(argb, out var b)) return b;
            if (GdipCreateSolidFill(argb, out b) != 0) b = IntPtr.Zero;
            _brCache[argb] = b;
            return b;
        }

        static float[] InkBounds(IntPtr gfx, string text, IntPtr font, IntPtr family, int style, float size)
        {
            if (font == IntPtr.Zero || string.IsNullOrEmpty(text)) return null;

            string key = $"{family}|{style}|{size:F1}|{text}";
            if (_inkCache.TryGetValue(key, out var cached)) return cached;

            var probe = new GPRECTF { X = 0, Y = 0, Width = 10000f, Height = 10000f };
            if (GdipMeasureString(gfx, text, -1, font, ref probe, IntPtr.Zero, out var m, out _, out _) != 0) return null;

            int w = (int)Math.Ceiling(m.Width) + 12, h = (int)Math.Ceiling(m.Height) + 12;
            if (w < 12 || h < 12 || w > 4096 || h > 512) return null;

            float[] res = null;
            IntPtr bmp = IntPtr.Zero, bg = IntPtr.Zero;
            unsafe
            {
                if (GdipCreateBitmapFromScan0(w, h, 0, PixelFormat32bppARGB, IntPtr.Zero, out bmp) == 0 && bmp != IntPtr.Zero &&
                    GdipGetImageGraphicsContext(bmp, out bg) == 0 && bg != IntPtr.Zero)
                {
                    GdipGraphicsClear(bg, 0);

                    GdipSetTextRenderingHint(bg, TextRenderingHintAntiAlias);
                    var r = new GPRECTF { X = 0, Y = 0, Width = w, Height = h };
                    GdipDrawString(bg, text, -1, font, ref r, _formatLeft, _pBrWhite);
                    GdipDeleteGraphics(bg);
                    bg = IntPtr.Zero;

                    var lockRect = new GPRECT { X = 0, Y = 0, Width = w, Height = h };
                    if (GdipBitmapLockBits(bmp, ref lockRect, ImageLockModeRead, PixelFormat32bppARGB, out var bd) == 0)
                    {
                        byte* src = (byte*)bd.Scan0;
                        int first = -1, last = -1;
                        for (int y = 0; y < h && first < 0; y++)
                            if (RowHasInk(src + y * bd.Stride, w)) first = y;
                        for (int y = h - 1; y >= 0 && last < 0; y--)
                            if (RowHasInk(src + y * bd.Stride, w)) last = y;
                        GdipBitmapUnlockBits(bmp, ref bd);

                        if (first >= 0 && last >= first)
                        {
                            float lineTop = (h - m.Height) / 2f;
                            res = new[] { first - lineTop, last - lineTop, m.Height };
                        }
                    }
                }
                if (bg != IntPtr.Zero) GdipDeleteGraphics(bg);
                if (bmp != IntPtr.Zero) GdipDisposeImage(bmp);
            }
            if (res != null) _inkCache[key] = res;
            return res;
        }

        static unsafe bool RowHasInk(byte* row, int w)
        {
            for (int x = 0; x < w; x++)
                if (row[x * 4 + 3] > 24) return true;
            return false;
        }

        static IntPtr FitFont(IntPtr gfx, string text, IntPtr family, int style, float baseSize, int maxW, IntPtr baseFont)
        {
            return FitFontSz(gfx, text, family, style, baseSize, maxW, baseFont, out _);
        }

                static IntPtr FitFontSz(IntPtr gfx, string text, IntPtr family, int style, float baseSize, int maxW, IntPtr baseFont, out float usedSize)
        {
            usedSize = baseSize;
            if (baseFont == IntPtr.Zero || string.IsNullOrEmpty(text)) return baseFont;

            var layout = new GPRECTF { X = 0, Y = 0, Width = 10000f, Height = 10000f };
            if (GdipMeasureString(gfx, text, -1, baseFont, ref layout, IntPtr.Zero, out var box, out _, out _) != 0) return baseFont;
            if (box.Width <= maxW || box.Width <= 0f) return baseFont;

            usedSize = baseSize * maxW / box.Width;
            if (GdipCreateFont(family, usedSize, style, UnitPixel, out var f) != 0) { usedSize = baseSize; return baseFont; }
            return f;
        }

        static void RowTextL(IntPtr gfx, string text, IntPtr font, IntPtr brush, int x, float cy)
        {
            if (font == IntPtr.Zero || brush == IntPtr.Zero) return;
            var r = new GPRECTF { X = x, Y = cy - 100f, Width = 1000f, Height = 200f };
            GdipDrawString(gfx, text, -1, font, ref r, _formatLeft, brush);
        }

        static void RowTextC(IntPtr gfx, string text, IntPtr font, IntPtr brush, int cx, int cy)
        {
            if (font == IntPtr.Zero || brush == IntPtr.Zero) return;
            var r = new GPRECTF { X = cx - 500f, Y = cy - 100f, Width = 1000f, Height = 200f };
            GdipDrawString(gfx, text, -1, font, ref r, _formatCenter, brush);
        }

        static void RowTextR(IntPtr gfx, string text, IntPtr font, IntPtr brush, int right, float cy)
        {
            if (font == IntPtr.Zero || brush == IntPtr.Zero || string.IsNullOrEmpty(text)) return;
            var layout = new GPRECTF { X = 0, Y = 0, Width = 10000f, Height = 10000f };
            if (GdipMeasureString(gfx, text, -1, font, ref layout, IntPtr.Zero, out var box, out _, out _) != 0) return;
            RowTextL(gfx, text, font, brush, (int)(right - box.Width), cy);
        }

                static bool InB30Filter(int x, int y)
            => x >= FLT_X && x < FLT_X + FLT_W && y >= FLT_Y && y < FLT_Y + FLT_H;

                static bool HitTestB30Search(int x, int y)
            => x >= FLT_SRCH_X && x < FLT_SRCH_X + FLT_SRCH_W
            && y >= FLT_SRCH_Y && y < FLT_SRCH_Y + FLT_SRCH_H;

                static int HitTestB30Filter(int x, int y)
        {
            if (!InB30Filter(x, y)) return -1;

            if (y >= FLT_DIFF_Y && y < FLT_DIFF_Y + FLT_DIFF_SIZE)
                for (int i = 0; i < FLT_DIFF_LABELS.Length; i++)
                {
                    int bx = FLT_FX + i * FLT_DIFF_PITCH;
                    if (x >= bx && x < bx + FLT_DIFF_SIZE) return i;
                }

            if (x >= FLT_RESET_X && x < FLT_RESET_X + FLT_RESET_W
                && y >= FLT_RESET_Y && y < FLT_RESET_Y + FLT_RESET_H) return FLT_CHIP_RESET;
            if (HitTestB30Search(x, y)) return FLT_CHIP_SRCH;
            if (x >= FLT_SRCH_CLR_X && x < FLT_SRCH_CLR_X + FLT_SRCH_CLR_W
                && y >= FLT_SRCH_Y && y < FLT_SRCH_Y + FLT_SRCH_H) return FLT_CHIP_CLR;

            int slide = B30SliderHit(x, y);
            if (slide >= 0) return slide;

            if (y >= FLT_SORT_Y && y < FLT_SORT_Y + FLT_ROW_H)
            {
                if (x >= FLT_SORT_KEY_X && x < FLT_SORT_KEY_X + FLT_SORT_KEY_W) return FLT_CHIP_SORT;
                if (x >= FLT_SORT_DIR_X && x < FLT_SORT_DIR_X + FLT_SORT_DIR_W) return FLT_CHIP_DIR;
            }
            return -1;
        }

                static string FltScoreText(int v)
        {
            if (v >= FLT_SCORE_MAX) v = FLT_SCORE_MAX;
            if (v <= 0) v = 0;
            int m = v / 1000000;
            int d = (v % 1000000) / 100000;
            return d == 0 ? m + "M" : m + "." + d + "M";
        }

                static string FltPttText(int v)
        {
            if (v < 0) v = 0;
            return (v / 100) + "." + ((v % 100) / 10) + (v % 10);
        }

                static string FltBoundText(int r, int v, bool lo)
        {
            int max = B30RangeMax(r);
            if (lo ? v <= 0 : v >= max) return "不限";
            string s = r == 0 ? FltScoreText(v) : (r == 1 ? FltPttText(v) : v.ToString());
            return (lo ? ">= " : "<= ") + s;
        }

        static int B30RangeMax(int r) => r == 0 ? FLT_SCORE_MAX : (r == 1 ? FLT_PTT_MAX : FLT_EXT_MAX);

        static int B30RangeVal(int r, bool lo)
        {
            if (r == 0) return lo ? _b30ScoreLo : _b30ScoreHi;
            if (r == 1) return lo ? _b30PttLo : _b30PttHi;
            return lo ? _b30ExtLo : _b30ExtHi;
        }

        static void B30RangeSet(int r, bool lo, int v)
        {
            if (r == 0) { if (lo) _b30ScoreLo = v; else _b30ScoreHi = v; }
            else if (r == 1) { if (lo) _b30PttLo = v; else _b30PttHi = v; }
            else { if (lo) _b30ExtLo = v; else _b30ExtHi = v; }
        }

                static int B30HandleX(int r, bool lo)
        {
            int max = B30RangeMax(r);
            int v = B30RangeVal(r, lo);
            if (v < 0) v = 0; else if (v > max) v = max;
            return FLT_TRACK_X + (int)Math.Round(v * (double)(FLT_TRACK_W - 1) / max);
        }

                static int B30SliderValue(int r, int x)
        {
            int t = x - FLT_TRACK_X;
            if (t < 0) t = 0;
            if (t >= FLT_TRACK_W) t = FLT_TRACK_W - 1;
            int max = B30RangeMax(r);
            double v = (double)t * max / (FLT_TRACK_W - 1);
            int step = r == 0 ? FLT_SCORE_STEP : (r == 1 ? FLT_PTT_STEP : FLT_EXT_STEP);
            int q = (int)Math.Round(v / step) * step;
            if (q < 0) q = 0; else if (q > max) q = max;
            return q;
        }

                static int B30SliderHit(int x, int y)
        {
            for (int r = 0; r < 3; r++)
            {
                int cy = FLT_RANGE_Y0 + r * FLT_RANGE_PITCH;
                if (y < cy - FLT_SLIDE_UP || y >= cy + FLT_SLIDE_DN) continue;
                if (x < FLT_TRACK_X - 8 || x >= FLT_TRACK_X + FLT_TRACK_W + 8) continue;
                int xl = B30HandleX(r, true), xh = B30HandleX(r, false);
                return FLT_CHIP_SCORE_LO + r * 2 + (Math.Abs(x - xh) < Math.Abs(x - xl) ? 1 : 0);
            }
            return -1;
        }

                static void B30SliderDown(int chip, int x)
        {
            _fltDragChip = chip;
            B30FilterChanged();
            B30SliderApply(chip, x);
        }

                static void B30SliderApply(int chip, int x)
        {
            int r = (chip - FLT_CHIP_SCORE_LO) / 2;
            bool lo = (chip & 1) == 0;
            int v = B30SliderValue(r, x);
            int other = B30RangeVal(r, !lo);
            if (lo ? v > other : v < other) v = other;
            B30RangeSet(r, lo, v);
        }

                static void DrawB30Slider(IntPtr gfx, int r, int cy, IntPtr dim)
        {
            int xl = B30HandleX(r, true), xh = B30HandleX(r, false);

            FillRoundRect(gfx, Brush(FLT_OFF_ARGB), FLT_TRACK_X, cy - FLT_TRACK_H / 2,
                FLT_TRACK_W, FLT_TRACK_H, 2);
            if (xh > xl)
                FillRoundRect(gfx, Brush(FLT_ON_ARGB), xl, cy - FLT_TRACK_H / 2,
                    xh - xl, FLT_TRACK_H, 2);

            int id = FLT_CHIP_SCORE_LO + r * 2;
            bool hl = _b30HoverChip == id || _fltDragChip == id;
            bool hh = _b30HoverChip == id + 1 || _fltDragChip == id + 1;
            FillRoundRect(gfx, Brush(hl ? FLT_KNOB_HOVER_ARGB : FLT_KNOB_ARGB),
                xl - FLT_HANDLE_W / 2, cy - FLT_HANDLE_H / 2, FLT_HANDLE_W, FLT_HANDLE_H, 5);
            FillRoundRect(gfx, Brush(hh ? FLT_KNOB_HOVER_ARGB : FLT_KNOB_ARGB),
                xh - FLT_HANDLE_W / 2, cy - FLT_HANDLE_H / 2, FLT_HANDLE_W, FLT_HANDLE_H, 5);

            RowTextL(gfx, FltBoundText(r, B30RangeVal(r, true), true), _pFontInfoTitle, dim,
                FLT_TRACK_X - 6, cy + FLT_VAL_DY + LABEL_TEXT_DY);
            RowTextR(gfx, FltBoundText(r, B30RangeVal(r, false), false), _pFontInfoTitle, dim,
                FLT_TRACK_X + FLT_TRACK_W + 6, cy + FLT_VAL_DY + LABEL_TEXT_DY);
        }

                static void DrawB30DiffRow(IntPtr gfx)
        {
            IntPtr all = InfoDiffAllArt();
            IntPtr[] arts = new IntPtr[4];
            bool ok = all != IntPtr.Zero;
            for (int i = 0; i < 4; i++)
            {
                arts[i] = InfoDiffArt(i);
                if (arts[i] == IntPtr.Zero) ok = false;
            }

            if (!ok)
            {
                for (int i = 0; i < FLT_DIFF_LABELS.Length; i++)
                {
                    bool on = i == 0 ? _b30DiffMask == 0xF : (_b30DiffMask & (1 << (i - 1))) != 0;
                    FltChip(gfx, i, FLT_FX + i * (FLT_DIFF_W + FLT_DIFF_GAP), FLT_DIFF_Y + 6,
                        FLT_DIFF_W, FLT_ROW_H, FLT_DIFF_LABELS[i], on, _pFontCjkLabel, 19f);
                }
                return;
            }

            for (int i = 0; i < 5; i++)
            {
                int x = FLT_FX + i * FLT_DIFF_PITCH;
                bool on = i == 0 ? _b30DiffMask == 0xF : (_b30DiffMask & (1 << (i - 1))) != 0;
                IntPtr art = i == 0 ? all : arts[i - 1];
                if (on)
                {

                    IntPtr act = i == 0 ? InfoDiffAllArtActive() : InfoDiffArtActive(i - 1);
                    if (act != IntPtr.Zero) art = act;
                    else
                        FillRoundRect(gfx, Brush(B30_CUT_ARGB), x - 2, FLT_DIFF_Y - 2,
                            FLT_DIFF_SIZE + 4, FLT_DIFF_SIZE + 4, 9);
                }
                if (_b30HoverChip == i) DrawBoost(gfx, art, INFO_DIFF_HOVER_BOOST,
                    x, FLT_DIFF_Y, FLT_DIFF_SIZE, FLT_DIFF_SIZE);
                else Draw(gfx, art, x, FLT_DIFF_Y, FLT_DIFF_SIZE, FLT_DIFF_SIZE);
            }
        }

                static void HandleRightClick(int x, int y)
        {
            if (_sheetOpen || !OnB30Page) return;
            if (!InB30Filter(x, y)) return;
            if (HitTestB30Filter(x, y) != FLT_CHIP_SORT) return;

            int n = FLT_SORT_NAMES.Length;
            _b30Sort = (_b30Sort + n - 1) % n;
            if (_b30Sort != 0) _b30SortDesc = _b30Sort != 4;
            B30FilterChanged();
        }

                static void FltChip(IntPtr gfx, int id, int x, int y, int w, int h,
            string label, bool on, IntPtr font, float size, bool enabled = true)
        {
            bool hov = enabled && _b30HoverChip == id;
            IntPtr bg = !enabled ? Brush(FLT_OFF_ARGB)
                : on ? Brush(hov ? FLT_ON_HOVER_ARGB : FLT_ON_ARGB)
                     : Brush(hov ? FLT_OFF_HOVER_ARGB : FLT_OFF_ARGB);
            FillRoundRect(gfx, bg, x, y, w, h, Math.Min(8, h / 2));

            IntPtr col = !enabled ? Brush(FLT_DIM_ARGB) : (on ? _pBrActiveText : _pBrWhite);
            IntPtr f = FitFont(gfx, label, CjkFamSrc(), 0, size, w - 12, font);
            RowTextC(gfx, label, f, col, x + w / 2, y + h / 2 + LABEL_TEXT_DY);

            if (f != font) GdipDeleteFont(f);
        }

                static void DrawB30SearchBox(IntPtr gfx)
        {
            bool editing = _nameDlgMode == 1 && _hNameDlg != IntPtr.Zero;
            bool hov = _b30HoverChip == FLT_CHIP_SRCH;

            FillRoundRect(gfx, Brush(hov || editing ? FLT_ON_HOVER_ARGB : FLT_EDGE_ARGB),
                FLT_SRCH_X, FLT_SRCH_Y, FLT_SRCH_W, FLT_SRCH_H, 8);
            FillRoundRect(gfx, Brush(FLT_FIELD_ARGB),
                FLT_SRCH_X + 1, FLT_SRCH_Y + 1, FLT_SRCH_W - 2, FLT_SRCH_H - 2, 7);

            string text = editing ? (NameDlgLive ?? "") : (_b30Search ?? "");
            string comp = editing ? (NameDlgComp ?? "") : "";
            int cy = FLT_SRCH_Y + FLT_SRCH_H / 2 + LABEL_TEXT_DY;

            IntPtr col = _pBrWhite;
            string shown;
            if (text.Length == 0 && comp.Length == 0)
            {
                shown = "搜索 曲名 / 曲师";
                col = Brush(FLT_DIM_ARGB);
            }
            else shown = text + comp + (editing ? "|" : "");

            int tx = FLT_SRCH_X + 8;
            int avail = FLT_SRCH_W - 16;
            var layout = new GPRECTF { X = 0f, Y = 0f, Width = 10000f, Height = 10000f };
            if (GdipMeasureString(gfx, shown, -1, _pFontInfoTitle, ref layout, IntPtr.Zero,
                    out var box, out _, out _) == 0 && box.Width > avail)
                tx -= (int)(box.Width - avail);

            GdipSetClipRect(gfx, FLT_SRCH_X + 2, FLT_SRCH_Y + 1, FLT_SRCH_W - 4, FLT_SRCH_H - 2, 0 );
            RowTextL(gfx, shown, _pFontInfoTitle, col, tx, cy);
            GdipResetClip(gfx);
        }

                static void DrawB30Filter(IntPtr gfx)
        {

            FillRoundRect(gfx, Brush(FLT_BG_ARGB), FLT_X, FLT_Y, FLT_W, FLT_H, 10);
            GdipFillRectangleI(gfx, Brush(B30_CUT_ARGB), FLT_X + FLT_PAD, FLT_Y, FLT_W - FLT_PAD * 2, 2);

            RowTextL(gfx, "筛选", _pFontCjkLabel, _pBrWhite, FLT_FX, FLT_TITLE_CY + LABEL_TEXT_DY);
            FltChip(gfx, FLT_CHIP_RESET, FLT_RESET_X, FLT_RESET_Y, FLT_RESET_W, FLT_RESET_H,
                "重置", false, _pFontCjkLabel, 17f);

            DrawB30DiffRow(gfx);

            DrawB30SearchBox(gfx);
            FltChip(gfx, FLT_CHIP_CLR, FLT_SRCH_CLR_X, FLT_SRCH_Y, FLT_SRCH_CLR_W, FLT_SRCH_H,
                "清空", false, _pFontCjkLabel, 17f);

            IntPtr dim = Brush(FLT_DIM_ARGB);
            for (int r = 0; r < 3; r++)
            {
                int cy = FLT_RANGE_Y0 + r * FLT_RANGE_PITCH;
                RowTextL(gfx, FLT_RANGE_LABELS[r], _pFontCjkLabel, dim, FLT_FX,
                    cy + LABEL_TEXT_DY);
                DrawB30Slider(gfx, r, cy, dim);
            }

            RowTextL(gfx, "排序", _pFontCjkLabel, dim, FLT_FX,
                FLT_SORT_Y + FLT_ROW_H / 2 + LABEL_TEXT_DY);
            FltChip(gfx, FLT_CHIP_SORT, FLT_SORT_KEY_X, FLT_SORT_Y, FLT_SORT_KEY_W, FLT_ROW_H,
                FLT_SORT_NAMES[_b30Sort], _b30Sort != 0, _pFontCjkLabel, 18f);

            FltChip(gfx, FLT_CHIP_DIR, FLT_SORT_DIR_X, FLT_SORT_Y, FLT_SORT_DIR_W, FLT_ROW_H,
                _b30SortDesc ? "降序" : "升序", false, _pFontCjkLabel, 18f, _b30Sort != 0);

            int cx = FLT_X + FLT_W / 2;
            RowTextC(gfx, "显示 " + RowCount() + " / " + SourceCount() + " 条",
                _pFontInfoTitle, dim, cx, FLT_SUM_CY + LABEL_TEXT_DY);

            string lg;
            if (_activeTab == 2) lg = _b30Sort != 0 ? "自定义排序" : "历史记录按时间从新到旧";
            else if (_b30Sort != 0) lg = "自定义排序 · 分界线已隐藏";
            else lg = _activeTab == 1 ? "青线以上 = 计入 EXACT 总评" : "青线以上 = 计入评级";
            RowTextC(gfx, lg, _pFontInfoTitle, dim, cx, FLT_LEGEND_CY + LABEL_TEXT_DY);
            RowTextC(gfx, "搜索回车确认 · Esc 取消", _pFontInfoTitle, dim, cx,
                FLT_LEGEND2_CY + LABEL_TEXT_DY);
        }

                static void B30FilterChanged()
        {
            _b30OpenRow = -1;
            _b30OpenSrc = null;
            B30OpenAnimReset();
            _scroll = 0f;
            _dragging = false;
            MarkDirty();
        }

                static void B30FilterClick(int chip, int x)
        {
            if (chip >= 0 && chip <= 4)
            {

                if (chip == 0) _b30DiffMask = 0xF;
                else _b30DiffMask ^= 1 << (chip - 1);
            }
            else if (chip == FLT_CHIP_RESET)
            {
                _b30DiffMask = 0xF;
                _b30Search = "";
                _b30ScoreLo = 0; _b30ScoreHi = FLT_SCORE_MAX;
                _b30PttLo = 0; _b30PttHi = FLT_PTT_MAX;
                _b30ExtLo = 0; _b30ExtHi = FLT_EXT_MAX;
                _b30Sort = 0; _b30SortDesc = true;
            }
            else if (chip == FLT_CHIP_CLR)
            {
                _b30Search = "";
            }
            else if (chip >= FLT_CHIP_SCORE_LO && chip <= FLT_CHIP_EXT_LO + 1)
            {

                B30SliderDown(chip, x);
                return;
            }
            else if (chip == FLT_CHIP_SORT)
            {
                _b30Sort = (_b30Sort + 1) % FLT_SORT_NAMES.Length;

                if (_b30Sort != 0) _b30SortDesc = _b30Sort != 4;
            }
            else if (chip == FLT_CHIP_DIR)
            {
                if (_b30Sort == 0) return;
                _b30SortDesc = !_b30SortDesc;
            }
            else return;

            B30FilterChanged();
        }

        static void RenderB30(IntPtr gfx)
        {

            SyncRowFonts();

            for (int i = 0; i < TAB_LABELS.Length; i++)
            {
                int x = TABROW_X + i * (TAB_W + TAB_GAP);
                bool on = i == _activeTab;
                bool hov = i == _hoverTab;
                FlatTab(gfx, x, TABROW_Y, TAB_W, TAB_H, 8, on, hov);

                int cy = TABROW_Y + TAB_H / 2 + TAB_TEXT_DY;
                IntPtr tf = FitFont(gfx, TAB_LABELS[i], _pRowNumFamily, 1, FS_TAB, TAB_W - 24, _pFontTab);
                RowTextC(gfx, TAB_LABELS[i], tf, FlatTabInk(on), x + TAB_W / 2, cy);
                if (tf != _pFontTab) GdipDeleteFont(tf);
            }

            int n = RowCount();

            if (_b30OpenRow >= 0 && !ReferenceEquals(_b30OpenSrc, B30ListRef())) { _b30OpenRow = -1; B30OpenAnimReset(); }
            if (_b30OpenRow >= n) { _b30OpenRow = -1; B30OpenAnimReset(); }
            int totalH = ListContentH(n);
            _scrollMax = Math.Max(0, totalH - LIST_H);
            if (_scroll > _scrollMax) _scroll = _scrollMax;

            if (n == 0)
            {

                DrawText(gfx, SourceCount() == 0 ? "还没有成绩数据" : "没有符合筛选条件的成绩",
                    _pFontCjkPage, _pBrWhite, LIST_X, LIST_Y, LIST_W, LIST_H, PAGE_TEXT_DY);
                return;
            }

            int y0 = (int)_scroll;
            int first = FirstVisibleRow(n, y0);
            int last  = first;
            while (last + 1 < n && RowOffset(last + 1) < y0 + LIST_H) last++;

            GdipSetClipRect(gfx, LIST_X, LIST_Y, LIST_W, LIST_H, 0 );
            for (int i = first; i <= last; i++)
                DrawRow(gfx, i, LIST_Y + RowOffset(i) - y0);

            int cut = _b30CutView;
            if (cut > 0 && cut < n)
            {

                int ly = LIST_Y + RowOffset(cut) - y0 - ROW_GAP / 2;
                GdipFillRectangleI(gfx, Brush(B30_CUT_ARGB),
                    ROW_BLOCK_X, ly, ROW_CARD_X + ROW_W - ROW_BLOCK_X, 2);
            }

            if (_b30OpenRow >= 0 && _b30OpenRow < n)
            {
                int rowBottom = LIST_Y + RowOffset(_b30OpenRow) + ROW_H - y0;
                int by = rowBottom + DETAIL_GAP;
                if (_b30OpenAt != 0 && _b30OpenT < 1f)
                {

                    int revealH = (int)((DETAIL_GAP + DETAIL_H) * _b30OpenT);
                    if (revealH > 0
                        && rowBottom < LIST_Y + LIST_H
                        && rowBottom + revealH > LIST_Y)
                    {
                        int slide = (int)((1f - _b30OpenT) * 10f);

                        GdipSetClipRect(gfx, DETAIL_X, rowBottom, DETAIL_W, revealH, 1 );
                        DrawDetail(gfx, _b30OpenRow, by - slide);
                        if (revealH > 2)
                            GdipFillRectangleI(gfx, Brush(B30_CUT_ARGB),
                                DETAIL_X, rowBottom + revealH - 2, DETAIL_W, 2);
                        GdipSetClipRect(gfx, LIST_X, LIST_Y, LIST_W, LIST_H, 0 );
                    }
                }
                else if (by < LIST_Y + LIST_H && by + DETAIL_H > LIST_Y)
                {
                    DrawDetail(gfx, _b30OpenRow, by);

                    DrawRow(gfx, _b30OpenRow, LIST_Y + RowOffset(_b30OpenRow) - y0);
                }
            }
            GdipResetClip(gfx);
        }

        static void DrawRow(IntPtr gfx, int i, int y)
        {

            int src = B30SrcOf(i);
            if (src < 0) return;
            if (_activeTab == 0)
            {
                var list = Plugin.ChartRatings;
                if (list == null || src >= list.Count) return;
                var c = list[src];

                DrawRowBase(gfx, src + 1, c.SongId, c.Difficulty, c.Constant,
                    SongInfoCache.GetName(c.SongId), SongInfoCache.GetArtist(c.SongId),
                    GRADE_NAMES[B30CardExporter.ScoreTierIndex(c.Score)], Plugin.RatingText(c.Rating), y);
            }
            else if (_activeTab == 1)
            {
                var list = Plugin.AllExactCharts;
                if (list == null || src >= list.Count) return;
                var c = list[src];

                DrawRowBase(gfx, src + 1, c.SongId, c.Difficulty, ConstantOf(c.SongId, c.Difficulty),
                    SongInfoCache.GetName(c.SongId), SongInfoCache.GetArtist(c.SongId),
                    c.ExactCount.ToString(), c.Rate.ToString("F0"), y);
            }
            else
            {
                var list = Plugin.PlayHistory;
                if (list == null || src >= list.Count) return;
                var c = list[src];

                DrawRowBase(gfx, src + 1, c.SongId, c.Difficulty, ConstantOf(c.SongId, c.Difficulty),
                    SongInfoCache.GetName(c.SongId), HistSubLine(c),
                    c.ExamFail ? "-" : GRADE_NAMES[B30CardExporter.ScoreTierIndex(c.Score)],
                    HistClearWord(c.Clear), y);

                if (c.TaskPlay)
                    FillPathAA(gfx, Brush(ROW_TASK_FRAME_ARGB),
                        RingPath(ROW_CARD_X, y, ROW_W, ROW_H, ROW_TASK_FRAME_T));
            }
        }

        static void DrawRowBase(IntPtr gfx, int rank, string songId, string difficulty, double constant,
            string title, string artist, string v1, string v2, int y)
        {
            if (string.IsNullOrEmpty(title)) title = "(未知曲目)";
            int cy = y + ROW_H / 2 + ROW_TEXT_DY;

            RowTextC(gfx, $"#{rank}", _pFontRowNum, _pBrWhite, ROW_BLOCK_X + RANK_COL_W / 2, cy);

            Draw(gfx, RowBg(difficulty), ROW_CARD_X, y, ROW_W, ROW_H);

            int diffColor = B30CardExporter.GetB10DiffColor(difficulty);
            float constSize = ConstTable.TextSizeScale();
            if (!rowConstFontDictionary.TryGetValue(constSize, out var _pFontConst))
            {
                rowConstFontDictionary[constSize] = _pFontConst = RegisterRowFont(_pRowFamSrc, constSize);
            }
            RowTextL(gfx, ConstTable.Text(constant), _pFontConst, Brush(diffColor), ROW_CARD_X + (int)(12 * ROW_S), cy);
            RowTextC(gfx, v1, _pFontRowNum, _pBrWhite, ROW_CARD_X + (int)(400 * ROW_S), cy);
            RowTextC(gfx, v2, _pFontRowNum, _pBrWhite, ROW_CARD_X + (int)(536 * ROW_S), cy);

            int tx = ROW_CARD_X + (int)(72 * ROW_S);
            int ax = ROW_CARD_X + (int)(76 * ROW_S);

            IntPtr fam   = B30CardExporter.TitleUseFurore(songId) ? _pRowFuroreFam : _pRowTitleFam;
            IntPtr tBase = BaseFont(_famTitleBase,  fam, FS_ROW_TITLE,  1 );
            IntPtr aBase = BaseFont(_famArtistBase, _pRowTitleFam, FS_ROW_ARTIST, 0 );
            IntPtr tFont = FitFont(gfx, title,  fam, 1, FS_ROW_TITLE,  (int)(270 * ROW_S), tBase);
            IntPtr aFont = FitFont(gfx, artist, _pRowTitleFam, 0, FS_ROW_ARTIST, (int)(300 * ROW_S), aBase);

            float[] ti = InkBounds(gfx, title,  tFont, fam, 1, FS_ROW_TITLE);
            float[] ai = string.IsNullOrEmpty(artist) ? null : InkBounds(gfx, artist, aFont, _pRowTitleFam, 0, FS_ROW_ARTIST);

            if (ti != null)
            {
                float inkT = ti[1] - ti[0];
                float inkA = ai != null ? ai[1] - ai[0] : 0f;
                float gap  = ai != null ? ROW_INK_GAP : 0f;
                float blockTop = cy - (inkT + gap + inkA) / 2f;

                RowTextL(gfx, title, tFont, Brush(diffColor), tx, blockTop - ti[0] + ti[2] / 2f);
                if (ai != null)
                    RowTextL(gfx, artist, aFont, _pBrArtist, ax, blockTop + inkT + gap - ai[0] + ai[2] / 2f);
            }
            else
            {

                RowTextL(gfx, title, tFont, Brush(diffColor), tx, cy - ROW_LINE_PITCH / 2f);
                if (!string.IsNullOrEmpty(artist))
                    RowTextL(gfx, artist, aFont, _pBrArtist, ax, cy + ROW_LINE_PITCH / 2f);
            }

            if (tFont != tBase) GdipDeleteFont(tFont);
            if (aFont != aBase) GdipDeleteFont(aFont);
        }

                static bool DetailOf(int i, out ResultDetail d, out string difficulty, out double constant, out double rating)
        {
            d = null; difficulty = ""; constant = 0; rating = double.NaN;

            int src = B30SrcOf(i);
            if (src < 0) return false;

            if (_activeTab == 0)
            {
                var list = Plugin.ChartRatings;
                if (list == null || src >= list.Count) return false;
                var c = list[src];
                difficulty = c.Difficulty ?? "";
                constant = c.Constant;
                d = c.Detail;
                rating = c.Rating;
            }
            else if (_activeTab == 1)
            {
                var list = Plugin.AllExactCharts;
                if (list == null || src >= list.Count) return false;
                var c = list[src];
                difficulty = c.Difficulty ?? "";
                constant = ConstantOf(c.SongId, c.Difficulty);
                d = c.Detail;
            }
            else
            {
                var list = Plugin.PlayHistory;
                if (list == null || src >= list.Count) return false;
                var c = list[src];
                difficulty = c.Difficulty ?? "";
                constant = ConstantOf(c.SongId, c.Difficulty);
                d = c.Detail;
            }

            if (double.IsNaN(rating) && d != null && constant > 0)
                rating = RatingEngine.SingleRating(constant, d.Score, d.Pace, d.Clear);

            return d != null;
        }

                static HistoryEntry HistEntryOf(int i)
        {
            if (_activeTab != 2) return null;
            int src = B30SrcOf(i);
            var list = Plugin.PlayHistory;
            if (src < 0 || list == null || src >= list.Count) return null;
            return list[src];
        }

        static string DetailBoardRel(string difficulty)
        {
            switch ((difficulty ?? "").ToLowerInvariant())
            {
                case "minimal": return "result/ResultMin.png";
                case "evolved": return "result/ResultEvo.png";
                case "ultimate": return "result/ResultUlt.png";
                case "forbidden": return "result/ResultFbd.png";
                default: return "";
            }
        }

        static string DetailClearArtRel(ResultDetail d, out bool plain)
        {
            plain = false;
            switch (d.Clear)
            {
                case 4:
                    return DetailIsPerfectDivePlus(d) ? "result/PerfectDivePlus.png" : "result/PerfectDive.png";
                case 3:
                    return "result/FullLInk.png";
                case 2:
                case 1:
                    plain = true;
                    return d.Pace < 0 ? "result/DiveFailed.png" : "result/DivePassed.png";
                default:
                    return "";
            }
        }

        static bool DetailIsPerfectDivePlus(ResultDetail d)
        {
            int all = d.SumAll();
            return all > 0 && d.Sum(ResultDetail.BShiny) == all;
        }

        static string DetailDiveText(int pace)
            => pace >= 0 ? "+" + pace.ToString(CultureInfo.InvariantCulture)
                         : pace.ToString(CultureInfo.InvariantCulture);

        static string DetailGradeName(ulong score)
        {
            if (score >= 99000000UL) return "EXPlus";
            if (score >= 98000000UL) return "EX";
            if (score >= 95000000UL) return "AA";
            if (score >= 90000000UL) return "A";
            if (score >= 80000000UL) return "B";
            if (score >= 70000000UL) return "C";
            return "D";
        }

        static IntPtr DetailGradeTex(string grade)
        {
            if (_dtGradeTex.TryGetValue(grade, out var cached)) return cached;

            IntPtr scaled = IntPtr.Zero;
            IntPtr src = Tex("gametexture/" + grade + ".png");
            if (src != IntPtr.Zero &&
                GdipGetImageWidth(src, out uint sw) == 0 &&
                GdipGetImageHeight(src, out uint sh) == 0 && sw > 0 && sh > 0)
                scaled = ScaleTo(src, 0, 0, (int)sw, (int)sh, DT_SLOT, DT_SLOT);

            _dtGradeTex[grade] = scaled;
            return scaled;
        }

        static IntPtr DtFont(int size)
        {
            if (_dtFonts.TryGetValue(size, out var f)) return f;

            f = IntPtr.Zero;
            if (_pRowNumFamily != IntPtr.Zero)
                GdipCreateFont(_pRowNumFamily, size, 1 , UnitPixel, out f);
            if (f == IntPtr.Zero && _pFontFamily != IntPtr.Zero)
                GdipCreateFont(_pFontFamily, size, 1 , UnitPixel, out f);
            _dtFonts[size] = f;
            return f;
        }

        static void DtTextC(IntPtr gfx, string text, int size, IntPtr brush, int cx, int cy, int maxW, bool shadow = false)
        {
            IntPtr baseFont = DtFont(size);
            if (baseFont == IntPtr.Zero || string.IsNullOrEmpty(text)) return;
            IntPtr f = FitFontSz(gfx, text, _pRowNumFamily, 1, size, maxW, baseFont, out _);
            if (shadow) RowTextC(gfx, text, f, Brush(DT_SHADOW_ARGB), cx + 1, cy + 1);
            RowTextC(gfx, text, f, brush, cx, cy);
            if (f != baseFont) GdipDeleteFont(f);
        }

        static void DtTextR(IntPtr gfx, string text, int size, IntPtr brush, int right, int cy, int maxW, bool shadow = false)
        {
            IntPtr baseFont = DtFont(size);
            if (baseFont == IntPtr.Zero || string.IsNullOrEmpty(text)) return;
            IntPtr f = FitFontSz(gfx, text, _pRowNumFamily, 1, size, maxW, baseFont, out _);
            if (shadow) RowTextR(gfx, text, f, Brush(DT_SHADOW_ARGB), right + 1, cy + 1);
            RowTextR(gfx, text, f, brush, right, cy);
            if (f != baseFont) GdipDeleteFont(f);
        }

        static string ScoreText(ulong score)
            => score.ToString("N0", CultureInfo.InvariantCulture).Replace(",", "'");

        internal static string DetailRateText(ResultDetail d)
        {
            int all = d.SumAll();
            if (all <= 0) return "-";
            return (100.0 * d.Sum(ResultDetail.BShiny) / all).ToString("F1", CultureInfo.InvariantCulture) + "%";
        }

        internal static string DetailExactificationText(ResultDetail d, double constant)
        {
            int all = d.SumAll();
            if (constant <= 0 || all <= 0) return "-";
            return (100.0 * d.Sum(ResultDetail.BShiny) * constant / all)
                .ToString("F0", CultureInfo.InvariantCulture);
        }

        internal static string DetailRatingText(double rating)
            => double.IsNaN(rating) || double.IsInfinity(rating)
                ? "-" : Plugin.RatingText(rating);

        internal static string DetailExpText(string difficulty, ResultDetail d)
        {
            int g = PlayerLevel.GainFor(difficulty, d.Score, d.Clear);
            return g <= 0 ? "-" : "+" + g.ToString(CultureInfo.InvariantCulture);
        }

        static int PartSum(ResultDetail d, int col, int bucket)
        {
            switch (col)
            {
                case 0: return d.Sum(bucket);
                case 1: return d.Tap[bucket];
                case 2: return d.Hold[bucket];
                case 3: return d.Sky[bucket];
                default: return d.Flick[bucket];
            }
        }

        internal static int DetailCell(ResultDetail d, int row, int col)
        {
            if (row == 0) return PartSum(d, col, ResultDetail.BShiny);
            if (row == 1) return PartSum(d, col, ResultDetail.BPerfectEarly)
                               + PartSum(d, col, ResultDetail.BPerfectLate);
            if (row == 2) return PartSum(d, col, ResultDetail.BNearEarly)
                               + PartSum(d, col, ResultDetail.BNearLate);
            if (row == 3) return PartSum(d, col, ResultDetail.BMiss);
            int s = 0;
            for (int r = 0; r < 4; r++) s += DetailCell(d, r, col);
            return s;
        }

        internal static int DetailFastLate(ResultDetail d, int row, bool early)
        {
            if (row == 0)
                return d.Sum(early ? ResultDetail.BPerfectEarly : ResultDetail.BPerfectLate);
            if (row == 1)
                return d.Sum(early ? ResultDetail.BNearEarly : ResultDetail.BNearLate);
            return early
                ? d.Sum(ResultDetail.BPerfectEarly) + d.Sum(ResultDetail.BNearEarly)
                : d.Sum(ResultDetail.BPerfectLate) + d.Sum(ResultDetail.BNearLate);
        }

                static void DrawDetail(IntPtr gfx, int i, int y)
        {
            bool has = DetailOf(i, out ResultDetail d, out string difficulty, out double constant, out double rating);
            int x = DETAIL_X;

            string rel = DetailBoardRel(difficulty);
            IntPtr board = rel.Length > 0 ? Tex(rel) : IntPtr.Zero;
            if (board != IntPtr.Zero)
            {
                Draw(gfx, board, x, y, DETAIL_W, DETAIL_H);
            }
            else
            {

                GdipFillRectangleI(gfx, _pBrTabOff, x, y, DETAIL_W, DETAIL_H);
                RowTextC(gfx, "缺少 result/Result*.png", _pFontCjkLabel, _pBrWhite,
                    x + DETAIL_W / 2, y + DETAIL_H / 2);
                return;
            }

            var he = HistEntryOf(i);
            if (he != null && he.ExamFail)
            {
                DrawExamFailDetail(gfx, he, x, y);
                return;
            }

            if (!has)
            {

                RowTextC(gfx, "没有判定详情", _pFontCjkLabel, _pBrWhite,
                    x + DETAIL_W / 2, y + DETAIL_H / 2 + 24);
                return;
            }

            string grade = DetailGradeName(d.Score);
            IntPtr art = DetailGradeTex(grade);
            if (art != IntPtr.Zero)
                Draw(gfx, art, x + DT_SLOT_X, y + DT_SLOT_Y, DT_SLOT, DT_SLOT);
            else
                DtTextC(gfx, grade == "EXPlus" ? "EX+" : grade, DT_FS_GRADE, Brush(DT_DARK_ARGB),
                    x + DT_SLOT_X + DT_SLOT / 2, y + DT_SLOT_Y + DT_SLOT / 2, DT_SLOT);

            DtTextC(gfx, ScoreText(d.Score), DT_FS_BIG, Brush(DT_DARK_ARGB),
                x + DT_SCORE_CX, y + DT_SCORE_CY, DT_SCORE_MAXW);
            DtTextC(gfx, d.Combo.ToString(CultureInfo.InvariantCulture), DT_FS_COMBO, _pBrWhite,
                x + DT_COMBO_CX, y + DT_COMBO_CY, DT_COMBO_MAXW, true);

            string clearRel = DetailClearArtRel(d, out bool clearPlain);
            IntPtr clearArt = clearRel.Length > 0 ? Tex(clearRel) : IntPtr.Zero;
            if (clearArt != IntPtr.Zero)
            {
                Draw(gfx, clearArt, x + DT_CLEAR_X, y + DT_CLEAR_Y, DT_CLEAR_W, DT_CLEAR_H);
                if (clearPlain)
                    DtTextC(gfx, DetailDiveText(d.Pace), DT_FS_WORD, _pBrWhite,
                        x + DT_CLEAR_CX, y + DT_CLEAR_CY, DT_CLEAR_MAXW, true);
            }
            else
            {
                DtTextC(gfx, HistClearWord(d.Clear), DT_FS_WORD, _pBrWhite,
                    x + DT_CLEAR_CX, y + DT_CLEAR_CY, DT_CLEAR_MAXW, true);
            }
            DtTextC(gfx, DetailRateText(d), DT_FS_NUM, _pBrWhite,
                x + DT_RATE_CX, y + DT_RATE_CY, DT_RATE_MAXW, true);

            DtTextC(gfx, DetailRatingText(rating), DT_FS_NUM, _pBrWhite,
                x + DT_PROT_CX, y + DT_PROT_CY, DT_PROT_MAXW, true);
            DtTextC(gfx, DetailExactificationText(d, constant), DT_FS_NUM, _pBrWhite,
                x + DT_EXACT_CX, y + DT_EXACT_CY, DT_EXACT_MAXW, true);
            DtTextC(gfx, DetailExpText(difficulty, d), DT_FS_NUM, _pBrWhite,
                x + DT_EXP_CX, y + DT_EXP_CY, DT_EXP_MAXW, true);

            for (int r = 0; r < DT_TABLE_ROWS.Length; r++)
            {
                bool darkRow = r == 4;
                IntPtr rowBr = darkRow ? Brush(DT_DARK_ARGB) : _pBrWhite;
                for (int c = 0; c < DT_TABLE_COLS.Length; c++)
                    DtTextC(gfx, DetailCell(d, r, c).ToString(CultureInfo.InvariantCulture),
                        DT_FS_NUM, rowBr, x + DT_TABLE_COLS[c], y + DT_TABLE_ROWS[r], 61, !darkRow);
            }

            DtTextR(gfx, HistTime(d.Seconds), DT_FS_TIME, Brush(DT_TIME_ARGB),
                x + DT_TIME_RIGHT, y + DT_TIME_CY, DT_TIME_MAXW);

            for (int r = 0; r < DT_FL_ROWS.Length; r++)
            {
                if (r == 2) continue;
                DtTextC(gfx, DetailFastLate(d, r, true).ToString(CultureInfo.InvariantCulture),
                    DT_FS_FL, _pBrWhite, x + DT_FL_FAST_X, y + DT_FL_ROWS[r], DT_FL_MAXW, true);
                DtTextC(gfx, DetailFastLate(d, r, false).ToString(CultureInfo.InvariantCulture),
                    DT_FS_FL, _pBrWhite, x + DT_FL_LATE_X, y + DT_FL_ROWS[r], DT_FL_MAXW, true);
            }
        }

                static void DrawExamFailDetail(IntPtr gfx, HistoryEntry he, int x, int y)
        {
            RowTextC(gfx, "课题里被打断 · 游戏没有记录这一首的判定", _pFontCjkLabel, _pBrWhite,
                x + DETAIL_W / 2, y + DETAIL_H / 2 - 14);

            string line;
            if (he.HasStats)
            {
                line = $"实时记录 · EXACT+ {he.Shiny} · EXACT {he.Exact} · NEAR {he.Near} · BREAK {he.Break}";
                if (he.Combo >= 0) line += $" · 最大连击 {he.Combo}";
            }
            else
            {
                line = "这一把的实时判定没接上 —— 没有数据";
            }

            IntPtr baseFont = _pFontCjkLabel;
            IntPtr f = baseFont;
            if (baseFont != IntPtr.Zero)
                f = FitFontSz(gfx, line, CjkFamSrc(), 0 , 20f, DETAIL_W - 48, baseFont, out _);
            RowTextC(gfx, line, f, he.HasStats ? _pBrWhite : _pBrArtist,
                x + DETAIL_W / 2, y + DETAIL_H / 2 + 22);
            if (f != baseFont && f != IntPtr.Zero) GdipDeleteFont(f);

            DtTextR(gfx, HistTime(he.Seconds), DT_FS_TIME, Brush(DT_TIME_ARGB),
                x + DT_TIME_RIGHT, y + DT_TIME_CY, DT_TIME_MAXW);
        }

        static IntPtr BaseFont(Dictionary<IntPtr, IntPtr> cache, IntPtr family, float size, int style)
        {
            if (family == IntPtr.Zero) return IntPtr.Zero;
            if (cache.TryGetValue(family, out var f)) return f;
            if (GdipCreateFont(family, size, style, UnitPixel, out f) != 0) f = IntPtr.Zero;
            cache[family] = f;
            return f;
        }

                static double PanelAnimP()
        {
            if (_panelAnimAt == 0) return 1;
            long ms = Environment.TickCount64 - _panelAnimAt;
            int dur = _panelAnimClosing ? PANEL_CLOSE_MS : PANEL_OPEN_MS;
            if (ms >= dur)
            {
                _panelAnimAt = 0;
                return _panelAnimClosing ? 0 : 1;
            }
            double u = ms / (double)dur;
            return _panelAnimClosing ? 1 - u : u;
        }

                static unsafe void PanelFxDib(double p)
        {
            if (_pBits == IntPtr.Zero || p >= 1) return;
            byte* bits = (byte*)_pBits;

            double e = 1 - Math.Pow(1 - p, 3);
            int vis = (int)Math.Round(Math.Max(2.0, e * _viewH));
            int top = _viewH - vis;
            if (top < 0) top = 0;
            for (int y = 0; y < top; y++) PanelFxClearRow(bits, y);

            int a = (int)(110 + 145 * p);
            PanelFxLine(bits, top, a);
            PanelFxLine(bits, top + 1, a * 3 / 5);
            for (int k = 2; k < 7; k++)
            {
                int ta = a * (7 - k) / 18;
                if (ta <= 0) continue;
                PanelFxLine(bits, top + k, ta);
            }

            if (p > 0.65)
            {
                double s = (p - 0.65) / 0.35;
                if (s > 1) s = 1;
                int sy = (int)Math.Round((1 - s) * (_viewH + 140)) - 70;
                PanelFxGlow(bits, sy, 120, top);
                for (int k = 1; k <= 14; k++)
                    PanelFxGlow(bits, sy + k, 90 * (14 - k) / 14, top);
            }
        }

        static unsafe void PanelFxClearRow(byte* bits, int y)
        {
            uint* row = (uint*)(bits + (long)y * _viewW * 4);
            for (int i = 0; i < _viewW; i++) row[i] = 0u;
        }

                static unsafe void PanelFxLine(byte* bits, int y, int alpha)
        {
            if (y < 0 || y >= _viewH || alpha <= 0) return;
            if (alpha > 255) alpha = 255;
            const int R = 0xB4, G = 0xF2, B = 0xFF;
            uint px = ((uint)alpha << 24)
                    | ((uint)(R * alpha / 255) << 16)
                    | ((uint)(G * alpha / 255) << 8)
                    |  (uint)(B * alpha / 255);
            uint* row = (uint*)(bits + (long)y * _viewW * 4);
            for (int i = 0; i < _viewW; i++) row[i] = px;
        }

                static unsafe void PanelFxGlow(byte* bits, int y, int alpha, int minY)
        {
            if (y < minY || y >= _viewH || alpha <= 0) return;
            if (alpha > 255) alpha = 255;
            const int R = 0xB4, G = 0xF2, B = 0xFF;
            uint ar = (uint)(R * alpha / 255), ag = (uint)(G * alpha / 255), ab = (uint)(B * alpha / 255);
            uint aa = (uint)alpha;
            uint* row = (uint*)(bits + (long)y * _viewW * 4);
            for (int i = 0; i < _viewW; i++)
            {
                uint q = row[i];
                uint qa = (q >> 24) + aa; if (qa > 255) qa = 255;
                uint qr = ((q >> 16) & 0xFF) + ar; if (qr > 255) qr = 255;
                uint qg = ((q >> 8) & 0xFF) + ag; if (qg > 255) qg = 255;
                uint qb = (q & 0xFF) + ab; if (qb > 255) qb = 255;
                row[i] = (qa << 24) | (qr << 16) | (qg << 8) | qb;
            }
        }

        static void CompositeToDib(IntPtr bmp, int w, int h)
        {
            if (bmp == IntPtr.Zero || _pBits == IntPtr.Zero || w <= 0 || h <= 0) return;
            var lockRect = new GPRECT { X = 0, Y = 0, Width = w, Height = h };
            if (GdipBitmapLockBits(bmp, ref lockRect, ImageLockModeRead, PixelFormat32bppARGB, out var bd) != 0) return;

            unsafe
            {
                for (int y = 0; y < h; y++)
                {
                    byte* src = (byte*)bd.Scan0 + (long)y * bd.Stride;
                    byte* dst = (byte*)_pBits + (long)y * _viewW * 4;
                    for (int x = 0; x < w; x++)
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
            }
            GdipBitmapUnlockBits(bmp, ref bd);
        }

                static unsafe void DibClipToShell(int x, int y)
        {
            if (_pBits == IntPtr.Zero || (x == 0 && y == 0)) return;
            byte* bits = (byte*)_pBits;

            int top = y > 0 ? y : 0;
            int bot = y < 0 ? _viewH + y : _viewH;
            int left = x > 0 ? x : 0;
            int right = x < 0 ? _viewW + x : _viewW;

            for (int yy = 0; yy < top; yy++) PanelFxClearRow(bits, yy);
            for (int yy = bot; yy < _viewH; yy++) PanelFxClearRow(bits, yy);
            for (int yy = top; yy < bot; yy++)
            {
                uint* row = (uint*)(bits + (long)yy * _viewW * 4);
                for (int i = 0; i < left; i++) row[i] = 0u;
                for (int i = right; i < _viewW; i++) row[i] = 0u;
            }
        }

        static void Commit()
        {
            if (_hWnd == IntPtr.Zero) return;
            var ptDst = new POINT { x = _screenX, y = _screenY };
            var sz = new SIZE { cx = _viewW, cy = _viewH };
            var ptSrc = new POINT { x = 0, y = 0 };

            byte sca = 255;
            if (_panelFxP < 1)
            {
                double v = _panelAnimClosing
                    ? 255 * Math.Pow(_panelFxP, 0.7)
                    : 90 + 165 * Math.Pow(_panelFxP, 0.7);
                sca = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(v)));
            }

            int op = Settings.PanelOpacity;
            if (op < 10) op = 10; else if (op > 100) op = 100;
            if (op < 100) sca = (byte)(sca * op / 100);
            var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = sca, AlphaFormat = 1 };
            UpdateLayeredWindow(_hWnd, IntPtr.Zero, ref ptDst, ref sz, _pDC, ref ptSrc, 0, ref blend, ULW_ALPHA);
        }

        static void SetupDib()
        {
            var screenDC = GetDC(IntPtr.Zero);
            _pDC = CreateCompatibleDC(screenDC);

            var bmi = new BITMAPINFO();
            bmi.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER));
            bmi.bmiHeader.biWidth = _viewW;
            bmi.bmiHeader.biHeight = -_viewH;
            bmi.bmiHeader.biPlanes = 1;
            bmi.bmiHeader.biBitCount = 32;
            bmi.bmiHeader.biCompression = 0;

            _pDib = CreateDIBSection(screenDC, ref bmi, 0, out _pBits, IntPtr.Zero, 0);
            _pDibOld = SelectObject(_pDC, _pDib);
            ReleaseDC(IntPtr.Zero, screenDC);
        }

        static IntPtr WndProcImpl(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {

            if (msg == WM_PANEL_SETACT)
            {
                RunSetActMsg(wParam.ToInt32());
                return IntPtr.Zero;
            }
            if (msg == WM_LBUTTONDOWN_P)
            {
                int lp = lParam.ToInt32();
                int px = (short)(lp & 0xFFFF);
                int py = (short)((lp >> 16) & 0xFFFF);

                int lx = LayoutX(px), ly = LayoutY(py);

                if (!_visible) return IntPtr.Zero;

                if (_visible && !_sheetOpen && OnInfoPage && _infoNameRW > 0 && HitTestInfoName(lx, ly))
                {
                    OpenNameDlg();
                    return IntPtr.Zero;
                }

                if (_visible && !_sheetOpen && OnB30Page && HitTestB30Search(lx, ly))
                {
                    if (_hNameDlg != IntPtr.Zero && _nameDlgMode == 0) EndNameDlg(true, true);
                    OpenNameDlg(true);
                    return IntPtr.Zero;
                }

                if (_hNameDlg != IntPtr.Zero) EndNameDlg(true, true);
                _clickX = lx;
                _clickY = ly;
                _clickPending = true;
                return IntPtr.Zero;
            }
            if (msg == WM_RBUTTONDOWN_P)
            {

                if (!_visible) return IntPtr.Zero;
                int lp = lParam.ToInt32();
                if (_hNameDlg != IntPtr.Zero) EndNameDlg(true, true);
                _rclickX = LayoutX((short)(lp & 0xFFFF));
                _rclickY = LayoutY((short)((lp >> 16) & 0xFFFF));
                _rclickPending = true;
                return IntPtr.Zero;
            }
            if (msg == WM_MOUSEWHEEL_P)
            {

                if (!_visible) return IntPtr.Zero;
                Interlocked.Add(ref _wheelAccum, (short)((wParam.ToInt64() >> 16) & 0xFFFF));
                return IntPtr.Zero;
            }
            if (msg == WM_DESTROY)
            {

                if (_hNameDlg != IntPtr.Zero) EndNameDlg(true, true);

                StopThumbWorker();
                DisposeTextures();
                foreach (KeyValuePair<float, IntPtr> pair in cjkConstFontDictionary.ToArray())
                {
                    if (pair.Value != IntPtr.Zero) GdipDeleteFont(pair.Value);
                    cjkConstFontDictionary.Remove(pair.Key);
                }
                if (_pFontCjkLabel != IntPtr.Zero) GdipDeleteFont(_pFontCjkLabel);
                if (_pFontCjkPage  != IntPtr.Zero) GdipDeleteFont(_pFontCjkPage);
                _pFontCjkLabel = _pFontCjkPage = IntPtr.Zero;

                if (_pFontInfoName  != IntPtr.Zero) GdipDeleteFont(_pFontInfoName);
                if (_pFontInfoTitle != IntPtr.Zero) GdipDeleteFont(_pFontInfoTitle);
                if (_pFontInfoRat   != IntPtr.Zero) GdipDeleteFont(_pFontInfoRat);
                if (_pFontInfoVal   != IntPtr.Zero) GdipDeleteFont(_pFontInfoVal);
                _pFontInfoName = _pFontInfoTitle = _pFontInfoRat = _pFontInfoVal = IntPtr.Zero;

                foreach (var kv in _famTitleBase)
                    if (kv.Value != IntPtr.Zero) GdipDeleteFont(kv.Value);
                foreach (var kv in _famArtistBase)
                    if (kv.Value != IntPtr.Zero) GdipDeleteFont(kv.Value);
                _famTitleBase.Clear();
                _famArtistBase.Clear();
                _pFontRowNum = _pFontTab = IntPtr.Zero;
                if (_pFontAchBadge != IntPtr.Zero) GdipDeleteFont(_pFontAchBadge);
                _pFontAchBadge = IntPtr.Zero;
                _pRowNumFamily = _pRowTitleFam = _pRowFuroreFam = IntPtr.Zero;
                _pRowFamSrc = IntPtr.Zero;
                foreach (var kv in _brCache)
                    if (kv.Value != IntPtr.Zero) GdipDeleteBrush(kv.Value);
                _brCache.Clear();
                _inkCache.Clear();

                if (_pCjkCollection != IntPtr.Zero) GdipDeletePrivateFontCollection(ref _pCjkCollection);
                _pCjkFamily = IntPtr.Zero;
                if (_pBrWhite   != IntPtr.Zero) GdipDeleteBrush(_pBrWhite);
                if (_pBrShade   != IntPtr.Zero) GdipDeleteBrush(_pBrShade);
                if (_pBrActiveText != IntPtr.Zero) GdipDeleteBrush(_pBrActiveText);
                if (_pBrArtist  != IntPtr.Zero) GdipDeleteBrush(_pBrArtist);
                if (_pBrTabOn   != IntPtr.Zero) GdipDeleteBrush(_pBrTabOn);
                if (_pBrTabOff  != IntPtr.Zero) GdipDeleteBrush(_pBrTabOff);
                if (_pBrTabHover != IntPtr.Zero) GdipDeleteBrush(_pBrTabHover);

                if (_bdDimBr != IntPtr.Zero) GdipDeleteBrush(_bdDimBr);
                _bdDimBr = IntPtr.Zero;
                _bdDimA = -1;

                if (_pToken     != IntPtr.Zero) GdiplusShutdown(_pToken);
                if (_pDibOld != IntPtr.Zero) SelectObject(_pDC, _pDibOld);
                if (_pDib != IntPtr.Zero) DeleteObject(_pDib);
                if (_pDC != IntPtr.Zero) DeleteDC(_pDC);
                PostQuitMessage(0);
                return IntPtr.Zero;
            }
            return DefWindowProcW(hWnd, msg, wParam, lParam);
        }

        const int REC_X = CK_X;
        const int REC_W = CK_W;
        const int REC_H = 96;
        const int REC_PITCH = 112;
        const int REC_Y0 = PAGE_TOP;
        const int REC_BAR_W = 4;

        const int RECB_X = DETB_X + DETB_W + 20;
        const int RECB_W = CONTENT_X + CONTENT_W - 24 - RECB_X;

                const int SHAKE_REFRESH = -4;

        static int _recIdx = -1;
        static int _recHover = -1;
        static bool _recHoverRefresh;

        static bool OnRecommendPage => NAV[_activeNav].Key == "recommend";

                static bool RecHas(int i) => Recommend.Slot(i) != null;

                static int HitTestRecCard(int x, int y)
        {
            if (x < REC_X || x >= REC_X + REC_W) return -1;
            if (y < REC_Y0) return -1;
            int i = (y - REC_Y0) / REC_PITCH;
            if (i < 0 || i >= Recommend.SlotCount) return -1;
            if (y - REC_Y0 - i * REC_PITCH >= REC_H) return -1;
            return RecHas(i) ? i : -1;
        }

        static bool HitTestRecRefresh(int x, int y)
            => x >= RECB_X && x < RECB_X + RECB_W && y >= DETB_Y && y < DETB_Y + DETB_H;

                static void RollRec()
        {
            Recommend.Roll();
            _recIdx = RecHas(0) ? 0 : -1;
            _recHover = -1;
        }

                static void ShakeRec(int what, string why)
        {
            _shakeTarget = what;
            _shakeAt = DateTime.Now;
            FreeRun.LastError = why.Length > 0 ? why : "现在跳不了";
            Plugin.Logger.LogWarning($"[推荐] 按不动（{what}）：{FreeRun.LastError}");
        }

                static void StartRec()
        {

            if (FreeRun.Active) { FreeRun.RequestAbort(); return; }

            var e = Recommend.Slot(_recIdx);
            if (e == null) { ShakeRec(SHAKE_GO, "先点左边一张卡选上"); return; }

            string why = FreeRun.RequestStart(e.SongId, e.Diff);
            if (why.Length > 0) { ShakeRec(SHAKE_GO, why); return; }
            SetVisible(false);
        }

        static void RenderRecommend(IntPtr gfx)
        {
            var brDim = Brush(unchecked((int)0xFF9AA3B8));
            var brVal = Brush(unchecked((int)0xFFEDF1F7));
            var brHdr = Brush(unchecked((int)0xFFB8DCF5));
            var brBad = Brush(unchecked((int)0xFFFF7B7B));
            var brKind = Brush(unchecked((int)0xFF8FB4FF));
            var brSel2 = Brush(unchecked((int)0xFF52627E));

            Recommend.EnsureRolled();

            if (_recIdx < 0 && RecHas(0)) _recIdx = 0;

            RowTextL(gfx, "曲目推荐", _pFontCjkPage, _pBrWhite, CONTENT_X + 32, CONTENT_Y + 36);
            RowTextR(gfx, "三条口径各一首 · 曲子不重复", _pFontCjkLabel, brHdr,
                CONTENT_X + CONTENT_W - 32, CONTENT_Y + 42);

            if (Recommend.Empty)
            {
                RowTextL(gfx, "还没有能推荐的谱面", _pFontCjkLabel, brHdr, REC_X, REC_Y0 + 24);

                RowTextL(gfx, Recommend.EmptyReason switch
                    {
                        Recommend.EmptyKind.Maxed => "你手上的谱都推到顶了 —— 等解锁新谱再来",
                        Recommend.EmptyKind.BelowB30 => "剩下的谱推满也够不着 B30 —— 往难了打",
                        _ => "先去选歌界面打两首（定数表也要读出来才有得挑）",
                    },
                    _pFontCjkLabel, brDim, REC_X, REC_Y0 + 56);
            }
            else
            {
                for (int i = 0; i < Recommend.SlotCount; i++)
                    DrawRecCard(gfx, i, REC_Y0 + i * REC_PITCH, brVal, brKind, brSel2);
            }

            RenderRecDetail(gfx, brDim, brVal, brHdr);

            bool running = FreeRun.Active;
            string err = FreeRun.LastError ?? "";
            string stat = FreeRun.Status ?? "";
            string line;
            IntPtr brLine;
            if (running && stat.Length > 0) { line = stat; brLine = brHdr; }
            else if (err.Length > 0) { line = err; brLine = brBad; }
            else { line = "点一下选中，再点一下跳过去 · 换一批 = 重新抽三首"; brLine = brDim; }
            RowTextL(gfx, ClipTitleLine(line, REC_W), _pFontCjkLabel, brLine,
                REC_X, REC_Y0 + Recommend.SlotCount * REC_PITCH + 24);

            bool litGo = _hoverGoBtn == 0;
            string goSt = litGo ? "hover" : "normal";
            int gx = DETB_X + ShakeOffset(SHAKE_GO);
            IntPtr plate = TaskPlate("go", goSt);
            IntPtr plateF = TaskPlate("go", goSt, THEME_FIN);
            IntPtr plateI = TaskPlate("go", goSt, THEME_INS);
            if (plate != IntPtr.Zero) DrawNavPlate(gfx, plateI, plateF, plate, gx, DETB_Y, DETB_W, DETB_H);
            else GdipFillRectangleI(gfx, litGo ? _pBrTabHover : _pBrTabOff, gx, DETB_Y, DETB_W, DETB_H);

            RowTextC(gfx, running ? "取消" : "开始", _pFontCjkPage, ActiveLabelBrush(),
                gx + DETB_W / 2, DETB_Y + DETB_H / 2 + PAGE_TEXT_DY);

            bool litRf = _recHoverRefresh && !running;
            string rfSt = litRf ? "hover" : "normal";
            int rx = RECB_X + ShakeOffset(SHAKE_REFRESH);
            IntPtr rp = TaskPlate("cat", rfSt);
            IntPtr rpF = TaskPlate("cat", rfSt, THEME_FIN);
            IntPtr rpI = TaskPlate("cat", rfSt, THEME_INS);
            if (rp != IntPtr.Zero) DrawNavPlate(gfx, rpI, rpF, rp, rx, DETB_Y, RECB_W, DETB_H);
            else GdipFillRectangleI(gfx, litRf ? _pBrTabHover : _pBrTabOff, rx, DETB_Y, RECB_W, DETB_H);

            if (running) GdipFillRectangleI(gfx, BrAchDim(), rx, DETB_Y, RECB_W, DETB_H);

            RowTextC(gfx, "换一批", _pFontCjkLabel, running ? brDim : ActiveLabelBrush(),
                rx + RECB_W / 2, DETB_Y + DETB_H / 2 + LABEL_TEXT_DY);
        }

                static void DrawRecCard(IntPtr gfx, int i, int y, IntPtr brVal, IntPtr brKind, IntPtr brSel2)
        {
            var e = Recommend.Slot(i);
            if (e == null) return;

            bool sel = i == _recIdx;
            bool hov = i == _recHover;
            string state = sel ? "active" : (hov ? "hover" : "normal");

            IntPtr own = Tex($"rec/card_{state}.png");
            if (own == IntPtr.Zero && state != "normal") own = Tex("rec/card_normal.png");
            if (own != IntPtr.Zero) Draw(gfx, own, REC_X, y, REC_W, REC_H);
            else GdipFillRectangleI(gfx,
                sel ? _pBrTabOn : (hov ? _pBrTabHover : _pBrTabOff), REC_X, y, REC_W, REC_H);

            int dc = B30CardExporter.GetB10DiffColor(e.Diff);
            GdipFillRectangleI(gfx, Brush(dc), REC_X, y, sel ? REC_BAR_W + 2 : REC_BAR_W, REC_H);

            IntPtr br1 = sel ? ActiveLabelBrush() : _pBrWhite;
            IntPtr br2 = sel ? brSel2 : brVal;

            string name = SongInfoCache.GetName(e.SongId.ToString(CultureInfo.InvariantCulture));
            RowTextL(gfx, ClipTitleLine(name, REC_W - 130), _pFontCjkLabel, br1, REC_X + 20, y + 28);
            float constSize = ConstTable.TextSizeScale();
            if (!cjkConstFontDictionary.TryGetValue(constSize, out var _pFontConst))
            {
                cjkConstFontDictionary[constSize] = _pFontConst = RegisterCjkFont(CjkFamSrc(), constSize);
            }
            RowTextR(gfx, $"{TaskConfig.DiffShort(e.Diff)} {ConstTable.Text(e.Const)}", _pFontConst, Brush(dc),
                REC_X + REC_W - 16, y + 28);

            RowTextL(gfx, Recommend.KindName(e.Kind), _pFontCjkLabel,
                sel ? brSel2 : brKind, REC_X + 20, y + 68);
            RowTextL(gfx, ClipTitleLine(e.ShortWhy, REC_W - 120), _pFontCjkLabel, br2,
                REC_X + 96, y + 68);
        }

                static void RenderRecDetail(IntPtr gfx, IntPtr brDim, IntPtr brVal, IntPtr brHdr)
        {
            int x = DET_X;
            int y = PAGE_TOP + 4;

            var e = Recommend.Slot(_recIdx);
            if (e == null)
            {
                RowTextL(gfx, "左边点一张卡，这儿就显示它凭什么被推上来", _pFontCjkLabel, brDim, x, y + 26);
                return;
            }

            string name = SongInfoCache.GetName(e.SongId.ToString(CultureInfo.InvariantCulture));
            RowTextL(gfx, ClipTitleLine(name, DET_W), _pFontCjkPage, _pBrWhite, x, y); y += 42;

            string artist = SongInfoCache.GetArtist(e.SongId.ToString(CultureInfo.InvariantCulture));
            if (artist.Length > 0)
            { RowTextL(gfx, ClipTitleLine(artist, DET_W), _pFontCjkLabel, brDim, x, y); y += 34; }

            RowTextL(gfx, $"{e.Diff} · 定数 {ConstTable.Text(e.Const)} · {Recommend.KindName(e.Kind)}",
                _pFontCjkLabel, brHdr, x, y); y += 40;

            RowTextL(gfx, "你的成绩", _pFontCjkLabel, brHdr, x, y); y += 28;
            if (!e.Played)
            {
                RowTextL(gfx, "未挑战", _pFontCjkLabel, brVal, x, y); y += 26;
            }
            else
            {
                RowTextL(gfx, $"{e.Score:N0} · 准度 {Pct2(e.Acc)}", _pFontCjkLabel, brVal, x, y); y += 26;

                string clear = e.IsPM ? " · PD" : (e.IsFC ? " · FC" : (e.Passed ? "" : " · 未通过"));
                RowTextL(gfx, $"dive {e.Pace} · 评级 {Plugin.RatingText(e.Rating)}{clear}",
                    _pFontCjkLabel, brVal, x, y); y += 34;
            }

            y += 8;
            RowTextL(gfx, "空间", _pFontCjkLabel, brHdr, x, y); y += 28;
            RowTextL(gfx, $"天花板 {Num2(e.Ceiling)}（定数 {ConstTable.Text(e.Const)} 折算 {Num2(RatingEngine.CurveConstant(e.Const))} + 2.75）",
                _pFontCjkLabel, brDim, x, y); y += 26;
            RowTextL(gfx, e.Played
                    ? $"还差 {Num2(e.Headroom)} 摸到顶"
                    : "这一张你还没打过，整档都是空间",
                _pFontCjkLabel, brVal, x, y); y += 40;

            RowTextL(gfx, "为什么推它", _pFontCjkLabel, brHdr, x, y); y += 28;
            var why = e.WhyLines;
            for (int i = 0; i < why.Length && i < 3; i++)
            {
                if (string.IsNullOrEmpty(why[i])) continue;
                RowTextL(gfx, ClipTitleLine(why[i], DET_W), _pFontCjkLabel, brVal, x, y);
                y += 26;
            }
        }

        static string Pct2(double v) => (v * 100).ToString("0.##", CultureInfo.InvariantCulture) + "%";
        static string Num2(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);
    }

}
