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
    const uint WS_EX_LAYERED = 0x00080000;
    const uint WS_EX_TRANSPARENT = 0x00000020;
    const uint WS_EX_TOOLWINDOW = 0x00000080;
    const uint WS_EX_NOACTIVATE = 0x08000000;
    const uint WS_POPUP = 0x80000000;

    const int WM_DESTROY = 0x0002;
    const int SW_SHOWNOACTIVATE = 4;

    const uint SWP_NOSIZE = 0x0001,
        SWP_NOMOVE = 0x0002,
        SWP_NOACTIVATE = 0x0010,
        SWP_NOOWNERZORDER = 0x0200;

    const int GWLP_HWNDPARENT = -8;
    const uint ULW_ALPHA = 0x00000002;
    const uint SRCCOPY = 0x00CC0020;

    const int PixelFormat32bppARGB = 0x0026200A;

    const int PixelFormat32bppPARGB = 0x000E200B;
    const int ImageLockModeRead = 1;
    const int UnitPixel = 2;
    const int FontStyleBold = 1;
    const int StringAlignmentNear = 0;
    const int StringAlignmentCenter = 1;
    const int StringAlignmentLineCenter = 1;
    const int TextRenderingHintAntiAlias = 4;
    const int LinearGradientModeVertical = 1;
    const int WrapModeTile = 0;

    internal const double GRADIENT_ANGLE_DEG = 30.0;

    const float NAME_FONT_RATIO = 0.7f;
    internal const float MAIN_FONT_RATIO = 0.72f;
    internal const float EXACT_FONT_RATIO = 0.46f;

    const float NAME_X = 0.05f;
    const float NAME_W = 0.4f;
    const float NAME_Y = 0.27f;
    const float NAME_H = 0.50f;

    internal const float MAIN_X = 0.43f;
    internal const float MAIN_W = 0.34f;
    internal const float MAIN_Y = 0.23f;
    internal const float MAIN_H = 0.6f;

    internal const float EXACT_X = 0.71f;
    internal const float EXACT_W = 0.28f;
    internal const float EXACT_Y = 0.35f;
    internal const float EXACT_H = 0.50f;

    const float DELTA_FONT_RATIO = 0.32f;
    const float DELTA_X = 0.43f;
    const float DELTA_W = 0.34f;
    const float DELTA_Y = 0.06f;
    const float DELTA_H = 0.25f;

    static readonly int COLOR_POSITIVE = unchecked((int)0xFF7FFF7F);
    static readonly int COLOR_NEGATIVE = unchecked((int)0xFFFF7F7F);

    internal static readonly int GRADIENT_TOP = 0x80AEFF;
    internal static readonly int GRADIENT_BOTTOM = 0xE0C9FF;

    internal static readonly int[] RATING_COLOR_STOPS =
    {
        0xFFBA7D, 0xFF8080, 0xFC7ED7, 0xB580FF, 0x7E8FFC, 0x80C6FF,
    };

    const int OVERLAY_OFFSET_Y = 10;
    const float ANIM_LERP = 0.15f;
    const int ANIM_FRAME_MS = 16;
    const float DIGIT_LERP = 0.10f;
    const int SHOW_CONFIRM_MS = 300;

    const float MOUSE_NEAR_DIST = 80f;
    const float MOUSE_FULL_FADE_DIST = 20f;
    const float FADE_OUT_SPEED = 0.35f;
    const float FADE_IN_SPEED = 0.15f;

    const double SWEEP_DURATION_MS = 500.0;
    const float SWEEP_PEAK = 0.85f;
    const float SWEEP_BAND_HALF = 0.15f;

    static readonly float SweepCos = (float)Math.Cos(GRADIENT_ANGLE_DEG * Math.PI / 180.0);
    static readonly float SweepSin = (float)Math.Sin(GRADIENT_ANGLE_DEG * Math.PI / 180.0);

    [StructLayout(LayoutKind.Sequential)]
    struct GdiplusStartupInput
    {
        public uint GdiplusVersion;
        public IntPtr DebugEventCallback;
        public int SuppressBackgroundThread;
        public int SuppressExternalCodecs;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct GPRECT
    {
        public int X, Y, Width, Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct GPRECTF
    {
        public float X, Y, Width, Height;
    }

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
    struct ColorMatrix
    {
        public float m00, m01, m02, m03, m04;
        public float m10, m11, m12, m13, m14;
        public float m20, m21, m22, m23, m24;
        public float m30, m31, m32, m33, m34;
        public float m40, m41, m42, m43, m44;
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
    struct POINT
    {
        public int x, y;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SIZE
    {
        public int cx, cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RECT
    {
        public int left, top, right, bottom;
    }

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

    [DllImport("gdiplus.dll", CharSet = CharSet.Unicode)]
    static extern int GdipSaveImageToFile(IntPtr image, string filename, ref Guid clsid, IntPtr encoderParams);

    [DllImport("gdiplus.dll")]
    static extern int GdipGetImageEncodersSize(out uint numEncoders, out uint size);

    [DllImport("gdiplus.dll")]
    static extern int GdipGetImageEncoders(uint numEncoders, uint size, IntPtr encoders);

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
    static extern int GdipCreateBitmapFromScan0(int width, int height, int stride,
        int pixelFormat, IntPtr scan0, out IntPtr bitmap);

    [DllImport("gdiplus.dll")]
    static extern int GdipGetImageGraphicsContext(IntPtr image, out IntPtr graphics);

    [DllImport("gdiplus.dll")]
    static extern int GdipDrawImageRectI(IntPtr graphics, IntPtr image, int x, int y, int width, int height);

    [DllImport("gdiplus.dll")]
    static extern int GdipDrawImageRectRectI(IntPtr graphics, IntPtr image,
        int dstx, int dsty, int dstwidth, int dstheight,
        int srcx, int srcy, int srcwidth, int srcheight,
        int srcUnit, IntPtr imageAttributes, IntPtr callback, IntPtr callbackData);

    [DllImport("gdiplus.dll")]
    static extern int GdipSetInterpolationMode(IntPtr graphics, int mode);

    [DllImport("gdiplus.dll")]
    static extern int GdipCreateImageAttributes(out IntPtr imageattr);

    [DllImport("gdiplus.dll")]
    static extern int GdipDisposeImageAttributes(IntPtr imageattr);

    [DllImport("gdiplus.dll")]
    static extern int GdipSetImageAttributesColorMatrix(IntPtr imageattr,
        int type, [MarshalAs(UnmanagedType.Bool)] bool enableFlag, ref ColorMatrix colorMatrix,
        IntPtr grayMatrix, int flags);

    [DllImport("gdiplus.dll")]
    static extern int GdipFillRectangleI(IntPtr graphics, IntPtr brush, int x, int y, int w, int h);

    [DllImport("gdiplus.dll")]
    static extern int GdipFillEllipseI(IntPtr graphics, IntPtr brush, int x, int y, int w, int h);

    [DllImport("gdiplus.dll")]
    static extern int GdipFillPolygonI(IntPtr graphics, IntPtr brush, [In] POINT[] points, int count, int fillMode);

    [DllImport("gdiplus.dll")]
    static extern int GdipCreatePath(int brushMode, out IntPtr path);

    [DllImport("gdiplus.dll")]
    static extern int GdipAddPathArc(IntPtr path, float x, float y, float width, float height,
        float startAngle, float sweepAngle);

    [DllImport("gdiplus.dll")]
    static extern int GdipAddPathLine(IntPtr path, float x1, float y1, float x2, float y2);

    [DllImport("gdiplus.dll")]
    static extern int GdipAddPathRectangle(IntPtr path, float x, float y, float width, float height);

    [DllImport("gdiplus.dll")]
    static extern int GdipClosePathFigure(IntPtr path);

    [DllImport("gdiplus.dll")]
    static extern int GdipFillPath(IntPtr graphics, IntPtr brush, IntPtr path);

    [DllImport("gdiplus.dll")]
    static extern int GdipDeletePath(IntPtr path);

    [DllImport("gdiplus.dll")]
    static extern int GdipSetSmoothingMode(IntPtr graphics, int mode);

    [DllImport("gdiplus.dll")]
    static extern int GdipCreateMatrix2(float m11, float m12, float m21, float m22,
        float dx, float dy, out IntPtr matrix);

    [DllImport("gdiplus.dll")]
    static extern int GdipSetWorldTransform(IntPtr graphics, IntPtr matrix);

    [DllImport("gdiplus.dll")]
    static extern int GdipDeleteMatrix(IntPtr matrix);

    [DllImport("gdiplus.dll")]
    static extern int GdipDeleteFont(IntPtr font);

    [DllImport("gdiplus.dll")]
    static extern int GdipDeleteFontFamily(IntPtr fontFamily);

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vKey);

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
    static extern int GdipSetSolidFillColor(IntPtr brush, int color);

    [DllImport("gdiplus.dll")]
    static extern int GdipCreateLineBrushFromRect(ref GPRECTF rect,
        int color1, int color2, int mode, int wrapMode, out IntPtr brush);

    [DllImport("gdiplus.dll")]
    static extern int GdipCreateLineBrushFromRectWithAngle(ref GPRECTF rect,
        int color1, int color2, float angle, int isAngleScalable, int wrapMode, out IntPtr brush);

    [DllImport("gdiplus.dll")]
    static extern int GdipDeleteBrush(IntPtr brush);

    [DllImport("gdiplus.dll")]
    static extern int GdipCreateStringFormat(int formatAttributes, int language, out IntPtr format);

    [DllImport("gdiplus.dll")]
    static extern int GdipDeleteStringFormat(IntPtr format);

    [DllImport("gdiplus.dll")]
    static extern int GdipSetStringFormatAlign(IntPtr format, int align);

    [DllImport("gdiplus.dll")]
    static extern int GdipSetStringFormatLineAlign(IntPtr format, int align);

    [DllImport("gdiplus.dll")]
    static extern int GdipSetTextRenderingHint(IntPtr graphics, int mode);

    [DllImport("gdiplus.dll")]
    static extern int GdipGraphicsClear(IntPtr graphics, int color);

    [DllImport("gdiplus.dll", CharSet = CharSet.Unicode)]
    static extern int GdipDrawString(IntPtr graphics, string text, int length,
        IntPtr font, ref GPRECTF layoutRect, IntPtr stringFormat, IntPtr brush);

    [DllImport("gdiplus.dll")]
    static extern int GdipSetClipRect(IntPtr graphics, float x, float y, float width, float height, int combineMode);

    [DllImport("gdiplus.dll")]
    static extern int GdipSetClipPath(IntPtr graphics, IntPtr path, int combineMode);

    [DllImport("gdiplus.dll")]
    static extern int GdipResetClip(IntPtr graphics);

    [DllImport("gdiplus.dll", CharSet = CharSet.Unicode)]
    static extern int GdipMeasureString(IntPtr graphics, string text, int length, IntPtr font,
        ref GPRECTF layoutRect, IntPtr stringFormat, out GPRECTF boundingBox, out int codepointsFitted,
        out int linesFilled);

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
    static extern IntPtr CallWindowProcW(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

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
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    static extern IntPtr WindowFromPoint(POINT pt);

    [DllImport("user32.dll")]
    static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern bool AdjustWindowRectEx(ref RECT lpRect, uint dwStyle, bool bMenu, uint dwExStyle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowTextW(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool SetWindowTextW(IntPtr hWnd, string text);

    [DllImport("user32.dll")]
    static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte bAlpha, uint dwFlags);

    [DllImport("imm32.dll")]
    static extern IntPtr ImmGetContext(IntPtr hWnd);

    [DllImport("imm32.dll", CharSet = CharSet.Unicode)]
    static extern int ImmGetCompositionStringW(IntPtr hIMC, uint dwIndex, IntPtr lpBuf, int dwBufLen);

    [DllImport("imm32.dll")]
    static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);

    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

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
    static extern IntPtr CreateFontW(int h, int w, int esc, int orient, int weight,
        uint italic, uint underline, uint strike, uint charset, uint outPrec, uint clipPrec,
        uint quality, uint pitch, string face);

    [DllImport("gdi32.dll")]
    static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest,
        IntPtr hdcSrc, int xSrc, int ySrc, uint dwRop);

    [DllImport("msimg32.dll")]
    static extern bool AlphaBlend(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest,
        IntPtr hdcSrc, int xSrc, int ySrc, int wSrc, int hSrc, BLENDFUNCTION ftn);

    static IntPtr _gdiplusToken = IntPtr.Zero;
    static IntPtr _bgBitmap = IntPtr.Zero;

    internal const int OVERLAY_W = 766;
    internal const int OVERLAY_H = 115;

    internal const int LAYOUT_REF_H = 90;

    const int TITLE_W = 702, TITLE_H = 34;

    const int TITLE_GAP = -23;
    const int TITLE_PAD_BOTTOM = 6;
    const int TITLE_INSET_X = 60;
    const float TITLE_FONT_PX = 20f;

    const float TITLE_SCALE = 0.8f;
    const float TITLE_SCALE_MIN = 0.4f, TITLE_SCALE_MAX = 1.0f;
    static float _titleScale = TITLE_SCALE;

    static int TitleW => (int)Math.Round(TITLE_W * _titleScale);
    static int TitleH => (int)Math.Round(TITLE_H * _titleScale);
    static int TitleInsetX => (int)Math.Round(TITLE_INSET_X * _titleScale);

    static int TitleStripH => TITLE_GAP + TitleH + TITLE_PAD_BOTTOM;

    static int _bgW = OVERLAY_W;
    static int _bgH = OVERLAY_H + TitleStripH;

    const int SCALE_REF_W = 1920, SCALE_REF_H = 1080;
    const float SCALE_MIN = 0.4f, SCALE_MAX = 4.0f;
    const float SCALE_EPS = 0.005f;
    const double SCALE_SETTLE_MS = 250.0;
    const int SLIDE_GAP = 40;

    static readonly string SCALE_PATH = Path.Combine(Settings.ConfigRoot, "overlay.json");
    static float _scale = 1f;
    static float _scaleOverride = 0f;
    static float _pendingScale = 0f;
    static DateTime _pendingScaleAt = DateTime.MinValue;

    static int _contentH = OVERLAY_H;
    static bool _needRedraw = false;

    static readonly Dictionary<int, IntPtr> _titlePlates = new Dictionary<int, IntPtr>();
    static string _titleName = "";
    static string _titleText = "";
    static int _titleGrade = 1;
    static string _renderedTitleKey = "\0";

    internal static readonly (double Min, string File)[] DECO_TIERS =
    {
        (12.0, "12.png"),
        (13.0, "13.png"),
        (14.0, "14.png"),
        (15.0, "15.png"),
        (15.3, "15_1.png"),
    };

    static readonly Dictionary<string, IntPtr> _decoBitmaps = new Dictionary<string, IntPtr>();
    static string DecoDir => Plugin.AssetPath("deco");
    static volatile bool _decoReloadRequested = false;
    static volatile bool _scaleReloadRequested = false;
    static string _renderedDecoTier = "\0";

    internal static readonly (double Min, string File)[] EXT_DECO_TIERS =
    {
        (10000.0, "10000.png"),
        (11000.0, "11000.png"),
        (12000.0, "12000.png"),
        (12500.0, "12500.png"),
        (13000.0, "13000.png"),
        (13333.0, "13333.png"),
    };

    static readonly Dictionary<string, IntPtr> _extDecoBitmaps = new Dictionary<string, IntPtr>();
    static string _renderedExtDecoTier = "\0";

    const double EXT_TRANS_DURATION_MS = 350.0;
    static bool _extTransActive = false;
    static DateTime _extTransStartAt = DateTime.MinValue;
    static string _extTransOldTier = null;
    static string _extTransNewTier = null;
    static float _extTransPhase = -1f;
    static float _renderedExtTransPhase = -1f;

    static IntPtr _memDC = IntPtr.Zero;
    static IntPtr _dibSection = IntPtr.Zero;
    static IntPtr _dibBits = IntPtr.Zero;
    static IntPtr _dibOldObj = IntPtr.Zero;

    static IntPtr _privateFontCollection = IntPtr.Zero;
    static IntPtr _fontFamily = IntPtr.Zero;
    static IntPtr _fontName = IntPtr.Zero;
    static IntPtr _fontMain = IntPtr.Zero;
    static IntPtr _fontExact = IntPtr.Zero;
    static IntPtr _fontDelta = IntPtr.Zero;
    static IntPtr _whiteBrush = IntPtr.Zero;
    static IntPtr _brushGreen = IntPtr.Zero;
    static IntPtr _brushRed = IntPtr.Zero;

    static IntPtr _titleFontCollection = IntPtr.Zero;
    static IntPtr _titleFontFamily = IntPtr.Zero;
    static IntPtr _fontTitle = IntPtr.Zero;
    static float _fontTitlePx = 0f;
    static IntPtr _brushTitleText = IntPtr.Zero;
    static IntPtr _brushTitlePlate = IntPtr.Zero;
    static readonly string TITLE_FONT_PATH = Plugin.AssetPath("font3.ttf");
    const int COLOR_TITLE_TEXT = unchecked((int)0xFFFFFFFF);
    const int COLOR_TITLE_PLATE = unchecked((int)0xB0101828);
    static int _renderedGradTop = -1;
    static int _renderedGradBottom = -1;

    static bool _sweepActive = false;
    static DateTime _sweepStartAt = DateTime.MinValue;
    static double _lastTargetRating = double.NaN;
    static double _targetRating = double.NaN;
    static float _sweepPhase = -1f;
    static float _renderedSweepPhase = -1f;
    static int _sweepOldTop = 0;
    static int _sweepOldBottom = 0;
    static int _sweepL, _sweepT, _sweepR, _sweepB;
    static float _sweepP, _sweepHalfW;
    static IntPtr _formatLeft = IntPtr.Zero;
    static IntPtr _formatCenter = IntPtr.Zero;

    static string _nameText = "Player";
    static string _mainText = "--.--";
    static string _exactText = "0";
    static string _deltaText = "";
    static bool _deltaIsPositive = true;
    static string _loadedFontName = null;
    static IntPtr _gameHwnd = IntPtr.Zero;

    static int _screenX = 0;
    static int _screenY = 0;
    static int _targetY = 0;
    static string _lastRenderedName = null;
    static string _lastRenderedMain = null;
    static string _lastRenderedExact = null;
    static string _lastRenderedDelta = null;
    static double _displayRating = 0;
    static double _displayExact = 0;
    static float _currentAlpha = 1.0f;
    static bool _stableShow = false;
    static bool _pendingShow = false;
    static DateTime _pendingShowAt = DateTime.MinValue;

    static bool _committed = false;
    static int _commitX = int.MinValue, _commitY = int.MinValue, _commitAlpha = -1;

    static int _ownPid = 0;
    static bool _lastInGame = true;
    static bool _inGameInit = false;

    static int _clipX0 = 0, _clipY0 = 0, _clipX1 = 0, _clipY1 = 0;
    static int _renderedClipX0 = 0, _renderedClipY0 = 0, _renderedClipX1 = 0, _renderedClipY1 = 0;

    static readonly string OverlayArtDir = Plugin.AssetPath(Path.Combine("GUI", "overlay"));
    static readonly string BG_PATH = Path.Combine(OverlayArtDir, "bg.png");
    static readonly string FONT_PATH = Plugin.AssetPath("font2.ttf");

    public static void Start(Func<string> mainProvider)
    {
        ExternalSaveManager.RegisterFile(SCALE_PATH, RequestScaleReload);
        var thread = new Thread(() =>
        {
            try
            {
                RunOverlay(mainProvider);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"[Overlay] {ex}");
            }
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

    static void LoadDecorations()
    {
        try
        {
            Directory.CreateDirectory(DecoDir);

            int loaded = 0;
            foreach (var tier in DECO_TIERS)
            {
                if (_decoBitmaps.ContainsKey(tier.File)) continue;

                string path = Path.Combine(DecoDir, tier.File);
                if (!File.Exists(path)) continue;

                int status = GdipCreateBitmapFromFile(path, out var bmp);
                if (status != 0 || bmp == IntPtr.Zero)
                {
                    Plugin.Logger.LogWarning($"[Deco] 加载失败 ({tier.File}): status={status}");
                    continue;
                }

                _decoBitmaps[tier.File] = bmp;
                loaded++;
                Plugin.Logger.LogInfo($"[Deco] 已加载 {tier.File} (>= {tier.Min:F1})");
            }

            Plugin.Logger.LogInfo(loaded > 0
                ? $"[Deco] 装饰层 {loaded}/{DECO_TIERS.Length} 档就绪: {DecoDir}"
                : $"[Deco] 无装饰素材（可选），目录: {DecoDir}");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[Deco] 加载装饰层出错: {ex.Message}");
        }
    }

    static void DisposeDecorations()
    {
        foreach (var kv in _decoBitmaps)
            if (kv.Value != IntPtr.Zero)
                GdipDisposeImage(kv.Value);
        _decoBitmaps.Clear();
    }

    static void LoadExtDecorations()
    {
        try
        {
            Directory.CreateDirectory(DecoDir);

            int loaded = 0;
            foreach (var tier in EXT_DECO_TIERS)
            {
                if (_extDecoBitmaps.ContainsKey(tier.File)) continue;

                string path = Path.Combine(DecoDir, tier.File);
                if (!File.Exists(path)) continue;

                int status = GdipCreateBitmapFromFile(path, out var bmp);
                if (status != 0 || bmp == IntPtr.Zero)
                {
                    Plugin.Logger.LogWarning($"[Deco] EXT 加载失败 ({tier.File}): status={status}");
                    continue;
                }

                _extDecoBitmaps[tier.File] = bmp;
                loaded++;
                Plugin.Logger.LogInfo($"[Deco] EXT 已加载 {tier.File} (>= {tier.Min:F0})");
            }

            Plugin.Logger.LogInfo(loaded > 0
                ? $"[Deco] EXT 装饰层 {loaded}/{EXT_DECO_TIERS.Length} 档就绪: {DecoDir}"
                : $"[Deco] 无 EXT 装饰素材（可选），目录: {DecoDir}");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[Deco] 加载 EXT 装饰层出错: {ex.Message}");
        }
    }

    static void DisposeExtDecorations()
    {
        foreach (var kv in _extDecoBitmaps)
            if (kv.Value != IntPtr.Zero)
                GdipDisposeImage(kv.Value);
        _extDecoBitmaps.Clear();
    }

    public static void RequestDecoReload()
    {
        _decoReloadRequested = true;
    }

    public static void RequestScaleReload()
    {
        _scaleReloadRequested = true;
    }

    public static float ActiveScale => _scale;

    public static float ActiveScaleOverride => _scaleOverride;

    public static float ActiveTitleScale => _titleScale;

    static string ResolveDecoTier(double rating)
    {
        string tier = null;
        foreach (var t in DECO_TIERS)
            if (rating >= t.Min - 1e-9 && _decoBitmaps.ContainsKey(t.File))
                tier = t.File;
        return tier;
    }

    static string ResolveExtDecoTier(double ext)
    {
        string tier = null;
        foreach (var t in EXT_DECO_TIERS)
            if (ext >= t.Min - 1e-9 && _extDecoBitmaps.ContainsKey(t.File))
                tier = t.File;
        return tier;
    }

    static void DrawImageAlpha(IntPtr gfx, IntPtr image, float alpha)
    {
        var cm = new ColorMatrix { m00 = 1f, m11 = 1f, m22 = 1f, m33 = alpha, m44 = 1f };
        if (GdipCreateImageAttributes(out var attr) != 0) return;
        GdipSetImageAttributesColorMatrix(attr, 0, true, ref cm, IntPtr.Zero, 0);
        GdipGetImageWidth(image, out uint iw);
        GdipGetImageHeight(image, out uint ih);
        GdipDrawImageRectRectI(gfx, image, 0, 0, OVERLAY_W, OVERLAY_H, 0, 0, (int)iw, (int)ih, 2, attr, IntPtr.Zero,
            IntPtr.Zero);
        GdipDisposeImageAttributes(attr);
    }

    static void DisposeBackgroundImage()
    {
        if (_bgBitmap != IntPtr.Zero)
        {
            GdipDisposeImage(_bgBitmap);
            _bgBitmap = IntPtr.Zero;
        }
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

        Plugin.Logger.LogInfo($"[Overlay] 背景图已加载: {w}x{h} → 拉伸铺满 {OVERLAY_W}x{OVERLAY_H}");
    }

    static void LoadTitlePlates()
    {
        for (int g = 1; g <= 5; g++)
        {
            if (_titlePlates.ContainsKey(g)) continue;
            string f = Path.Combine(OverlayArtDir, "arch_tier" + g + ".png");
            if (!File.Exists(f)) continue;
            if (GdipCreateBitmapFromFile(f, out var bmp) != 0 || bmp == IntPtr.Zero)
            {
                Plugin.Logger.LogWarning($"[Overlay] 称号底板加载失败: {f}");
                continue;
            }

            _titlePlates[g] = bmp;
        }

        if (_titlePlates.Count == 0)
            Plugin.Logger.LogInfo($"[Overlay] 没有称号底板（{Path.Combine(OverlayArtDir, "arch_tier1..5.png")}）—— 称号条走纯色兜底");
        else
            Plugin.Logger.LogInfo($"[Overlay] 称号底板已加载: {_titlePlates.Count} 档（arch_tier*.png）");
    }

    static void DisposeTitlePlates()
    {
        foreach (var kv in _titlePlates)
            if (kv.Value != IntPtr.Zero)
                GdipDisposeImage(kv.Value);
        _titlePlates.Clear();
    }

    static void LoadTitleFont()
    {
        try
        {
            if (!File.Exists(TITLE_FONT_PATH))
            {
                Plugin.Logger.LogInfo($"[Overlay] 中文字体不存在: {TITLE_FONT_PATH}（称号条用现有字体）");
                return;
            }

            if (GdipNewPrivateFontCollection(out _titleFontCollection) != 0)
            {
                _titleFontCollection = IntPtr.Zero;
                return;
            }

            if (GdipPrivateAddFontFile(_titleFontCollection, TITLE_FONT_PATH) != 0)
            {
                Plugin.Logger.LogWarning($"[Overlay] 称号字体加载失败: {TITLE_FONT_PATH}");
                GdipDeletePrivateFontCollection(ref _titleFontCollection);
                _titleFontCollection = IntPtr.Zero;
                return;
            }

            if (GdipCreateFontFamilyFromName("Alimama ShuHeiTi", _titleFontCollection, out _titleFontFamily) != 0)
                GdipCreateFontFamilyFromName("阿里妈妈数黑体", _titleFontCollection, out _titleFontFamily);

            if (_titleFontFamily != IntPtr.Zero) Plugin.Logger.LogInfo("[Overlay] 称号字体就绪: Alimama ShuHeiTi");
            else Plugin.Logger.LogWarning("[Overlay] 找不到字体族 Alimama ShuHeiTi，称号退回现有字体");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[Overlay] LoadTitleFont: {ex}");
        }
    }

    static void SyncTitle()
    {
        var d = Achievements.EquippedDef;
        if (d == null)
        {
            _titleName = "";
            _titleText = "";
            _titleGrade = 1;
            return;
        }

        _titleName = d.Name.Length > 0 ? d.Name : d.Id;
        int g = Achievements.TitleDisplayGrade(d);
        _titleGrade = g < 1 ? 1 : (g > 5 ? 5 : g);
        _titleText = TitleFit(_titleName, TitleW - TitleInsetX * 2);
    }

    static IntPtr TitlePlate(int grade)
    {
        for (int g = grade > 5 ? 5 : grade; g >= 1; g--)
            if (_titlePlates.TryGetValue(g, out var bmp) && bmp != IntPtr.Zero)
                return bmp;
        return IntPtr.Zero;
    }

    static string TitleFit(string s, int maxW)
    {
        if (string.IsNullOrEmpty(s)) return "";
        float fontPx = TITLE_FONT_PX * _titleScale;
        float w = 0f;
        for (int i = 0; i < s.Length; i++)
        {
            float cw = s[i] > 0x2E80 ? fontPx : fontPx * 0.5f;
            if (w + cw > maxW - fontPx) return s.Substring(0, i) + "…";
            w += cw;
        }

        return s;
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
        var hwnd = FindWindowW(null, "In Falsus");
        if (hwnd != IntPtr.Zero && IsWindow(hwnd))
            return hwnd;

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
                catch
                {
                }

                return true;
            }, IntPtr.Zero);

            if (best != IntPtr.Zero)
                return best;
        }
        catch
        {
        }

        try
        {
            var p = System.Diagnostics.Process.GetCurrentProcess();
            if (p.MainWindowHandle != IntPtr.Zero)
                return p.MainWindowHandle;
        }
        catch
        {
        }

        return IntPtr.Zero;
    }

    static void RatingGradient(double rating, out int topRgb, out int bottomRgb)
    {
        if (rating < 10.0)
        {
            topRgb = bottomRgb = 0xFFFFFF;
            return;
        }

        if (rating >= 15.0)
        {
            topRgb = 0x80C6FF;
            bottomRgb = 0xFC7EE7;
            return;
        }

        int i = (int)(rating - 10.0);
        topRgb = RATING_COLOR_STOPS[i];
        bottomRgb = RATING_COLOR_STOPS[i + 1];
    }

    static int LerpRgb(int a, int b, float t)
    {
        int ar = (a >> 16) & 0xFF, ag = (a >> 8) & 0xFF, ab = a & 0xFF;
        int br = (b >> 16) & 0xFF, bg = (b >> 8) & 0xFF, bb = b & 0xFF;
        int r = ar + (int)((br - ar) * t);
        int g = ag + (int)((bg - ag) * t);
        int bl = ab + (int)((bb - ab) * t);
        return (r << 16) | (g << 8) | bl;
    }

    static void EffectiveGradient(double rating, out int topRgb, out int bottomRgb)
    {
        RatingGradient(rating, out topRgb, out bottomRgb);
        if (_sweepActive)
        {
            topRgb = LerpRgb(_sweepOldTop, topRgb, _sweepPhase);
            bottomRgb = LerpRgb(_sweepOldBottom, bottomRgb, _sweepPhase);
        }
    }

    static IntPtr CreateGradient(ref GPRECTF rect, int startRgb, int endRgb)
    {
        int status = GdipCreateLineBrushFromRectWithAngle(ref rect,
            unchecked((int)0xFF000000) | startRgb,
            unchecked((int)0xFF000000) | endRgb,
            (float)GRADIENT_ANGLE_DEG, 0, WrapModeTile, out var brush);
        if (status != 0)
        {
            Plugin.Logger.LogWarning($"[Overlay] 渐变笔刷创建失败: status={status}");
            return IntPtr.Zero;
        }

        return brush;
    }

    static bool PrepareSweep(ref GPRECTF rect)
    {
        int l = Math.Max(0, (int)Math.Floor(rect.X));
        int t = Math.Max(0, (int)Math.Floor(rect.Y));
        int r = Math.Min(_bgW, (int)Math.Ceiling(rect.X + rect.Width));
        int b = Math.Min(_bgH, (int)Math.Ceiling(rect.Y + rect.Height));
        if (r <= l || b <= t) return false;

        float pMax = (r - l) * SweepCos + (b - t) * SweepSin;
        float halfW = pMax * SWEEP_BAND_HALF;
        if (halfW < 1f) return false;

        _sweepL = l;
        _sweepT = t;
        _sweepR = r;
        _sweepB = b;
        _sweepHalfW = halfW;

        _sweepP = -halfW + _sweepPhase * (pMax + 2f * halfW);
        return true;
    }

    static float SweepAmount(int x, int y)
    {
        if (y < _sweepT || y >= _sweepB || x < _sweepL || x >= _sweepR) return 0f;
        float d = (x - _sweepL) * SweepCos + (y - _sweepT) * SweepSin - _sweepP;
        if (d < -_sweepHalfW || d > _sweepHalfW) return 0f;
        float u = d / _sweepHalfW;
        float f = 1f - u * u;
        return SWEEP_PEAK * f * f;
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

        float nameSize = LAYOUT_REF_H * NAME_FONT_RATIO;
        float mainSize = LAYOUT_REF_H * MAIN_FONT_RATIO;
        float exactSize = LAYOUT_REF_H * EXACT_FONT_RATIO;
        float deltaSize = LAYOUT_REF_H * DELTA_FONT_RATIO;

        GdipCreateFont(_fontFamily, nameSize, FontStyleBold, UnitPixel, out _fontName);
        GdipCreateFont(_fontFamily, mainSize, FontStyleBold, UnitPixel, out _fontMain);
        GdipCreateFont(_fontFamily, exactSize, FontStyleBold, UnitPixel, out _fontExact);
        GdipCreateFont(_fontFamily, deltaSize, FontStyleBold, UnitPixel, out _fontDelta);

        EnsureTitleFont();

        GdipCreateSolidFill(unchecked((int)0xFFFFFFFF), out _whiteBrush);
        GdipCreateSolidFill(COLOR_POSITIVE, out _brushGreen);
        GdipCreateSolidFill(COLOR_NEGATIVE, out _brushRed);
        GdipCreateSolidFill(COLOR_TITLE_TEXT, out _brushTitleText);
        GdipCreateSolidFill(COLOR_TITLE_PLATE, out _brushTitlePlate);

        GdipCreateStringFormat(0, 0, out _formatLeft);
        GdipSetStringFormatAlign(_formatLeft, StringAlignmentNear);
        GdipSetStringFormatLineAlign(_formatLeft, StringAlignmentLineCenter);

        GdipCreateStringFormat(0, 0, out _formatCenter);
        GdipSetStringFormatAlign(_formatCenter, StringAlignmentCenter);
        GdipSetStringFormatLineAlign(_formatCenter, StringAlignmentLineCenter);

        Plugin.Logger.LogInfo(
            $"[Overlay] 字号 Name={nameSize:F1} Main={mainSize:F1} Exact={exactSize:F1} Delta={deltaSize:F1}");
    }

    static void EnsureTitleFont()
    {
        float want = TITLE_FONT_PX * _titleScale;
        if (_fontTitle != IntPtr.Zero && Math.Abs(want - _fontTitlePx) < 0.01f) return;

        if (_fontTitle != IntPtr.Zero)
        {
            GdipDeleteFont(_fontTitle);
            _fontTitle = IntPtr.Zero;
        }

        IntPtr fam = _titleFontFamily != IntPtr.Zero ? _titleFontFamily : _fontFamily;
        if (fam == IntPtr.Zero) return;

        GdipCreateFont(fam, want, 0, UnitPixel, out _fontTitle);
        _fontTitlePx = _fontTitle != IntPtr.Zero ? want : 0f;
    }

    static void DisposeDib()
    {
        if (_dibOldObj != IntPtr.Zero && _memDC != IntPtr.Zero)
        {
            SelectObject(_memDC, _dibOldObj);
            _dibOldObj = IntPtr.Zero;
        }

        if (_dibSection != IntPtr.Zero)
        {
            DeleteObject(_dibSection);
            _dibSection = IntPtr.Zero;
        }

        _dibBits = IntPtr.Zero;
        if (_memDC != IntPtr.Zero)
        {
            DeleteDC(_memDC);
            _memDC = IntPtr.Zero;
        }
    }

    static void SetupDib()
    {
        DisposeDib();

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
        Plugin.Logger.LogInfo($"[Overlay] DIB: {_bgW}x{_bgH}（设计 {OVERLAY_W}x{OVERLAY_H + TitleStripH} × {_scale:F3}）");
    }

    static float ResolveScale()
    {
        if (_scaleOverride > 0f) return _scaleOverride;

        if (_gameHwnd == IntPtr.Zero || !IsWindow(_gameHwnd)) return _scale;
        RECT rc;
        if (!GetClientRect(_gameHwnd, out rc)) return _scale;
        int cw = rc.right - rc.left, ch = rc.bottom - rc.top;
        if (cw <= 0 || ch <= 0) return _scale;

        float s = Math.Min(cw / (float)SCALE_REF_W, ch / (float)SCALE_REF_H);
        if (s < SCALE_MIN) s = SCALE_MIN;
        if (s > SCALE_MAX) s = SCALE_MAX;
        return s;
    }

    static bool RefreshScale(bool force = false)
    {
        float want = ResolveScale();
        if (Math.Abs(want - _scale) < SCALE_EPS)
        {
            _pendingScale = 0f;
        }
        else
        {
            bool take = force;
            if (!take)
            {
                if (Math.Abs(want - _pendingScale) > 0.0005f)
                {
                    _pendingScale = want;
                    _pendingScaleAt = DateTime.Now;
                }
                else if ((DateTime.Now - _pendingScaleAt).TotalMilliseconds >= SCALE_SETTLE_MS)
                {
                    take = true;
                }
            }

            if (take)
            {
                _pendingScale = 0f;
                _scale = want;
            }
        }

        int w = (int)Math.Round(OVERLAY_W * _scale);
        int h = (int)Math.Round((OVERLAY_H + TitleStripH) * _scale);
        if (w == _bgW && h == _bgH) return false;

        _bgW = w;
        _bgH = h;
        _contentH = (int)Math.Round(OVERLAY_H * _scale);
        Plugin.Logger.LogInfo($"[Overlay] 等比例缩放 → ×{_scale:F3}（窗口 {_bgW}x{_bgH}）；称号条 {TitleW}x{TitleH}");
        return true;
    }

    static void LoadOverlayJson()
    {
        _scaleOverride = 0f;
        _titleScale = TITLE_SCALE;
        bool lockScale = Settings.HasOverlayScale, lockTitle = Settings.HasTitleScale;
        if (lockScale)
        {
            float v = Settings.OverlayScale;
            _scaleOverride = v < SCALE_MIN ? (v > 0f ? SCALE_MIN : 0f) : (v > SCALE_MAX ? SCALE_MAX : v);

            Plugin.Logger.LogInfo(_scaleOverride > 0f
                ? $"[Overlay] 设置页指定缩放 ×{_scaleOverride:F3}（自动值被覆盖）"
                : "[Overlay] 设置页选了「自动」缩放（overlay.json 里的 scale 被它压过）");
        }

        if (lockTitle)
        {
            float v = Settings.TitleScale;
            _titleScale = v < TITLE_SCALE_MIN
                ? TITLE_SCALE_MIN
                : (v > TITLE_SCALE_MAX ? TITLE_SCALE_MAX : v);
            Plugin.Logger.LogInfo($"[Overlay] 设置页指定称号条缩放 ×{_titleScale:F3}（默认 {TITLE_SCALE:F2}）");
        }

        try
        {
            if (!File.Exists(SCALE_PATH)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(SCALE_PATH));
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return;

            if (doc.RootElement.TryGetProperty("scale", out var sc) &&
                sc.ValueKind == JsonValueKind.Number && sc.TryGetSingle(out float v) && v > 0f)
            {
                if (lockScale)
                {
                    Plugin.Logger.LogInfo("[Overlay] overlay.json 里的 scale 这次不算数（设置页定过）");
                }
                else
                {
                    _scaleOverride = v < SCALE_MIN ? SCALE_MIN : (v > SCALE_MAX ? SCALE_MAX : v);
                    Plugin.Logger.LogInfo($"[Overlay] overlay.json 指定缩放 ×{_scaleOverride:F3}（自动值被覆盖）");
                }
            }

            if (doc.RootElement.TryGetProperty("titleScale", out var ts) &&
                ts.ValueKind == JsonValueKind.Number && ts.TryGetSingle(out float tv) && tv > 0f)
            {
                if (lockTitle)
                {
                    Plugin.Logger.LogInfo("[Overlay] overlay.json 里的 titleScale 这次不算数（设置页定过）");
                }
                else
                {
                    _titleScale = tv < TITLE_SCALE_MIN
                        ? TITLE_SCALE_MIN
                        : (tv > TITLE_SCALE_MAX ? TITLE_SCALE_MAX : tv);
                    Plugin.Logger.LogInfo($"[Overlay] overlay.json 指定称号条缩放 ×{_titleScale:F3}（默认 {TITLE_SCALE:F2}）");
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[Overlay] overlay.json 读不了: {ex.Message}（按自动缩放）");
        }
    }

    static void RenderToDib()
    {
        IntPtr work = IntPtr.Zero;
        IntPtr gradMain = IntPtr.Zero;
        IntPtr gradExact = IntPtr.Zero;
        bool sweepOn = false;
        IntPtr mask = IntPtr.Zero;
        IntPtr maskGfx = IntPtr.Zero;
        IntPtr scaleMatrix = IntPtr.Zero;

        try
        {
            int status = GdipCreateBitmapFromScan0(_bgW, _bgH, 0, PixelFormat32bppARGB,
                IntPtr.Zero, out work);
            if (status != 0 || work == IntPtr.Zero)
            {
                Plugin.Logger.LogError($"[Overlay] 建画布失败: {status}");
                return;
            }

            if (GdipGetImageGraphicsContext(work, out var gfx) == 0)
            {
                GdipSetTextRenderingHint(gfx, TextRenderingHintAntiAlias);

                if (_scale != 1f)
                {
                    GdipSetInterpolationMode(gfx, 2);
                    if (GdipCreateMatrix2(_scale, 0f, 0f, _scale, 0f, 0f, out scaleMatrix) == 0)
                        GdipSetWorldTransform(gfx, scaleMatrix);
                    else
                        scaleMatrix = IntPtr.Zero;
                }

                if (_bgBitmap != IntPtr.Zero)
                    GdipDrawImageRectI(gfx, _bgBitmap, 0, 0, OVERLAY_W, OVERLAY_H);

                var rectName = new GPRECTF
                {
                    X = OVERLAY_W * NAME_X, Y = LAYOUT_REF_H * NAME_Y,
                    Width = OVERLAY_W * NAME_W, Height = LAYOUT_REF_H * NAME_H
                };
                GdipDrawString(gfx, _nameText, -1, _fontName, ref rectName, _formatLeft, _whiteBrush);

                var rectMain = new GPRECTF
                {
                    X = OVERLAY_W * MAIN_X, Y = LAYOUT_REF_H * MAIN_Y,
                    Width = OVERLAY_W * MAIN_W, Height = LAYOUT_REF_H * MAIN_H
                };
                EffectiveGradient(_targetRating, out int gradTop, out int gradBottom);
                gradMain = CreateGradient(ref rectMain, gradTop, gradBottom);
                var brushMain = gradMain != IntPtr.Zero ? gradMain : _whiteBrush;
                GdipDrawString(gfx, _mainText, -1, _fontMain, ref rectMain, _formatCenter, brushMain);

                if (_sweepPhase >= 0f)
                {
                    var sweepRect = new GPRECTF
                    {
                        X = rectMain.X * _scale, Y = rectMain.Y * _scale,
                        Width = rectMain.Width * _scale, Height = rectMain.Height * _scale
                    };
                    sweepOn = PrepareSweep(ref sweepRect);
                }

                if (sweepOn &&
                    GdipCreateBitmapFromScan0(_bgW, _bgH, 0, PixelFormat32bppARGB, IntPtr.Zero, out mask) == 0)
                {
                    if (GdipGetImageGraphicsContext(mask, out maskGfx) == 0)
                    {
                        GdipGraphicsClear(maskGfx, 0x00000000);
                        GdipSetTextRenderingHint(maskGfx, TextRenderingHintAntiAlias);

                        if (scaleMatrix != IntPtr.Zero) GdipSetWorldTransform(maskGfx, scaleMatrix);

                        GdipDrawString(maskGfx, _mainText, -1, _fontMain, ref rectMain, _formatCenter, _whiteBrush);
                    }
                    else
                    {
                        GdipDisposeImage(mask);
                        mask = IntPtr.Zero;
                        maskGfx = IntPtr.Zero;
                    }
                }

                var rectExact = new GPRECTF
                {
                    X = OVERLAY_W * EXACT_X, Y = LAYOUT_REF_H * EXACT_Y,
                    Width = OVERLAY_W * EXACT_W, Height = LAYOUT_REF_H * EXACT_H
                };
                gradExact = CreateGradient(ref rectExact, GRADIENT_TOP, GRADIENT_BOTTOM);
                var brushExact = gradExact != IntPtr.Zero ? gradExact : _whiteBrush;
                GdipDrawString(gfx, _exactText, -1, _fontExact, ref rectExact, _formatCenter, brushExact);

                if (!string.IsNullOrEmpty(_deltaText))
                {
                    var rectDelta = new GPRECTF
                    {
                        X = OVERLAY_W * DELTA_X, Y = LAYOUT_REF_H * DELTA_Y,
                        Width = OVERLAY_W * DELTA_W, Height = LAYOUT_REF_H * DELTA_H
                    };
                    var brushDelta = _deltaIsPositive ? _brushGreen : _brushRed;
                    GdipDrawString(gfx, _deltaText, -1, _fontDelta, ref rectDelta, _formatCenter, brushDelta);
                }

                string decoTier = ResolveDecoTier(_displayRating);
                if (decoTier != null && _decoBitmaps.TryGetValue(decoTier, out var deco) && deco != IntPtr.Zero)
                {
                    GdipDrawImageRectI(gfx, deco, 0, 0, OVERLAY_W, OVERLAY_H);
                }

                if (_extTransPhase >= 0f)
                {
                    float t = _extTransPhase;
                    if (_extTransOldTier != null && _extDecoBitmaps.TryGetValue(_extTransOldTier, out var oldDeco) &&
                        oldDeco != IntPtr.Zero)
                        GdipDrawImageRectI(gfx, oldDeco, 0, 0, OVERLAY_W, OVERLAY_H);
                    if (_extTransNewTier != null && _extDecoBitmaps.TryGetValue(_extTransNewTier, out var newDeco) &&
                        newDeco != IntPtr.Zero)
                        DrawImageAlpha(gfx, newDeco, t);
                }
                else
                {
                    string extDecoTier = ResolveExtDecoTier(_displayExact);
                    if (extDecoTier != null && _extDecoBitmaps.TryGetValue(extDecoTier, out var extDeco) &&
                        extDeco != IntPtr.Zero)
                    {
                        GdipDrawImageRectI(gfx, extDeco, 0, 0, OVERLAY_W, OVERLAY_H);
                    }
                }

                if (_titleName.Length > 0)
                {
                    int tw = TitleW, th = TitleH, tinset = TitleInsetX;
                    int tx = (OVERLAY_W - tw) / 2;
                    int ty = OVERLAY_H + TITLE_GAP;
                    var plate = TitlePlate(_titleGrade);
                    if (plate != IntPtr.Zero)
                    {
                        if (_titleScale != 1f) GdipSetInterpolationMode(gfx, 2);
                        GdipDrawImageRectI(gfx, plate, tx, ty, tw, th);
                    }
                    else GdipFillRectangleI(gfx, _brushTitlePlate, tx, ty, tw, th);

                    EnsureTitleFont();
                    var rectTitle = new GPRECTF
                    {
                        X = tx + tinset, Y = ty,
                        Width = tw - tinset * 2, Height = th
                    };
                    GdipDrawString(gfx, _titleText, -1, _fontTitle, ref rectTitle, _formatCenter, _brushTitleText);
                }

                if (maskGfx != IntPtr.Zero) GdipDeleteGraphics(maskGfx);
                GdipDeleteGraphics(gfx);
            }

            var lockRect = new GPRECT { X = 0, Y = 0, Width = _bgW, Height = _bgH };
            if (GdipBitmapLockBits(work, ref lockRect, ImageLockModeRead,
                    PixelFormat32bppARGB, out var bd) != 0)
            {
                Plugin.Logger.LogError("[Overlay] lock 失败");
                return;
            }

            BitmapData bdMask = default;
            bool maskLocked = false;
            if (sweepOn && mask != IntPtr.Zero)
            {
                if (GdipBitmapLockBits(mask, ref lockRect, ImageLockModeRead,
                        PixelFormat32bppARGB, out bdMask) != 0)
                {
                    GdipDisposeImage(mask);
                    mask = IntPtr.Zero;
                }
                else maskLocked = true;
            }

            if (_dibBits == IntPtr.Zero)
            {
                GdipBitmapUnlockBits(work, ref bd);
                if (maskLocked) GdipBitmapUnlockBits(mask, ref bdMask);
                return;
            }

            bool clip = _clipX0 > 0 || _clipY0 > 0 || _clipX1 < _bgW || _clipY1 < _bgH;

            _renderedClipX0 = _clipX0;
            _renderedClipY0 = _clipY0;
            _renderedClipX1 = _clipX1;
            _renderedClipY1 = _clipY1;

            unsafe
            {
                byte* src = (byte*)bd.Scan0;
                byte* dst = (byte*)_dibBits;
                byte* m = maskLocked ? (byte*)bdMask.Scan0 : null;
                int total = _bgW * _bgH;
                int px = 0, py = 0;
                for (int i = 0; i < total; i++)
                {
                    if (clip && (px < _clipX0 || px >= _clipX1 || py < _clipY0 || py >= _clipY1))
                    {
                        dst[0] = dst[1] = dst[2] = dst[3] = 0;
                    }
                    else
                    {
                        byte b = src[0], g = src[1], r = src[2], a = src[3];

                        if (sweepOn && m != null && a != 0 && m[3] != 0)
                        {
                            float k = SweepAmount(px, py);
                            if (k > 0f)
                            {
                                r = (byte)(r + (255 - r) * k);
                                g = (byte)(g + (255 - g) * k);
                                b = (byte)(b + (255 - b) * k);
                            }
                        }

                        if (a == 0)
                        {
                            dst[0] = dst[1] = dst[2] = dst[3] = 0;
                        }
                        else if (a == 255)
                        {
                            dst[0] = b;
                            dst[1] = g;
                            dst[2] = r;
                            dst[3] = 255;
                        }
                        else
                        {
                            dst[0] = (byte)(b * a / 255);
                            dst[1] = (byte)(g * a / 255);
                            dst[2] = (byte)(r * a / 255);
                            dst[3] = a;
                        }
                    }

                    src += 4;
                    dst += 4;
                    if (m != null) m += 4;
                    if (++px == _bgW)
                    {
                        px = 0;
                        py++;
                    }
                }
            }

            if (maskLocked) GdipBitmapUnlockBits(mask, ref bdMask);
            GdipBitmapUnlockBits(work, ref bd);
        }
        finally
        {
            if (gradMain != IntPtr.Zero) GdipDeleteBrush(gradMain);
            if (gradExact != IntPtr.Zero) GdipDeleteBrush(gradExact);
            if (scaleMatrix != IntPtr.Zero) GdipDeleteMatrix(scaleMatrix);
            if (mask != IntPtr.Zero) GdipDisposeImage(mask);
            if (work != IntPtr.Zero) GdipDisposeImage(work);
        }
    }

    static void UpdateMouseAlpha()
    {
        float targetAlpha = 1.0f;

        if (Settings.HideOnMouseAway)
            try
            {
                POINT mouse;
                if (GetCursorPos(out mouse))
                {
                    int left = _screenX;
                    int top = _screenY;
                    int right = _screenX + _bgW;

                    int bottom = _screenY + (_titleName.Length > 0 ? _bgH : _contentH);

                    int dx = 0, dy = 0;
                    if (mouse.x < left) dx = left - mouse.x;
                    else if (mouse.x > right) dx = mouse.x - right;

                    if (mouse.y < top) dy = top - mouse.y;
                    else if (mouse.y > bottom) dy = mouse.y - bottom;

                    double dist = Math.Sqrt(dx * dx + dy * dy);

                    float nearDist = MOUSE_NEAR_DIST * _scale;
                    float fullDist = MOUSE_FULL_FADE_DIST * _scale;

                    if (dist <= fullDist)
                        targetAlpha = 0f;
                    else if (dist <= nearDist)
                        targetAlpha = (float)((dist - fullDist) / (nearDist - fullDist));
                    else
                        targetAlpha = 1f;
                }
            }
            catch
            {
            }

        if (Settings.HideOnLeaveGame && !StillInGame()) targetAlpha = 0f;

        float speed = targetAlpha < _currentAlpha ? FADE_OUT_SPEED : FADE_IN_SPEED;
        _currentAlpha += (targetAlpha - _currentAlpha) * speed;

        if (Math.Abs(targetAlpha - _currentAlpha) < 0.005f)
            _currentAlpha = targetAlpha;

        if (_currentAlpha < 0f) _currentAlpha = 0f;
        if (_currentAlpha > 1f) _currentAlpha = 1f;
    }

    static bool StillInGame()
    {
        bool inGame = ComputeInGame();
        if (!_inGameInit || inGame != _lastInGame)
        {
            _inGameInit = true;
            _lastInGame = inGame;
            Plugin.Logger.LogInfo(inGame
                ? "[Overlay] 回到游戏窗口，overlay 恢复显示"
                : "[Overlay] 离开游戏窗口，overlay 淡出（透明度归 0）");
        }

        return inGame;
    }

    static bool ComputeInGame()
    {
        int me = OwnPid();
        if (me == 0) return true;

        IntPtr fg = IntPtr.Zero, under = IntPtr.Zero;
        try
        {
            fg = GetForegroundWindow();
        }
        catch
        {
        }

        try
        {
            POINT pt;
            if (GetCursorPos(out pt)) under = WindowFromPoint(pt);
        }
        catch
        {
        }

        if (fg == IntPtr.Zero && under == IntPtr.Zero) return true;
        if (OwnsWindow(fg, me)) return true;
        if (OwnsWindow(under, me)) return true;
        return false;
    }

    static bool OwnsWindow(IntPtr h, int me)
    {
        if (h == IntPtr.Zero) return false;
        try
        {
            GetWindowThreadProcessId(h, out uint pid);
            return pid == 0 || pid == (uint)me;
        }
        catch
        {
            return true;
        }
    }

    static int OwnPid()
    {
        if (_ownPid != 0) return _ownPid;
        try
        {
            _ownPid = System.Diagnostics.Process.GetCurrentProcess().Id;
        }
        catch
        {
        }

        return _ownPid;
    }

    static byte AlphaByte() => (byte)Math.Clamp((int)(_currentAlpha * 255f), 0, 255);

    static void CommitToWindow(IntPtr hWnd) => CommitToWindow(hWnd, AlphaByte());

    static void CommitToWindow(IntPtr hWnd, byte alphaByte)
    {
        var ptDst = new POINT { x = _screenX, y = _screenY };
        var sz = new SIZE { cx = _bgW, cy = _bgH };
        var ptSrc = new POINT { x = 0, y = 0 };

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

        _targetY = pt.y + (int)Math.Round(OVERLAY_OFFSET_Y * _scale);
    }

    static int HiddenY()
    {
        return _targetY - _bgH - (int)Math.Round(SLIDE_GAP * _scale);
    }

    static void UpdateClipRect()
    {
        int x0 = 0, y0 = 0, x1 = _bgW, y1 = _bgH;

        try
        {
            if (_gameHwnd != IntPtr.Zero && IsWindow(_gameHwnd))
            {
                var pt = new POINT { x = 0, y = 0 };
                RECT rc;
                if (ClientToScreen(_gameHwnd, ref pt) && GetClientRect(_gameHwnd, out rc) &&
                    rc.right > rc.left && rc.bottom > rc.top)
                {
                    x0 = Math.Max(0, pt.x - _screenX);
                    y0 = Math.Max(0, pt.y - _screenY);
                    x1 = Math.Min(_bgW, pt.x + (rc.right - rc.left) - _screenX);
                    y1 = Math.Min(_bgH, pt.y + (rc.bottom - rc.top) - _screenY);
                    if (x1 < x0) x1 = x0;
                    if (y1 < y0) y1 = y0;
                }
            }
        }
        catch
        {
            x0 = 0;
            y0 = 0;
            x1 = _bgW;
            y1 = _bgH;
        }

        _clipX0 = x0;
        _clipY0 = y0;
        _clipX1 = x1;
        _clipY1 = y1;
    }

    static void RunOverlay(Func<string> mainProvider)
    {
        StartupGdiplus();
        LoadBackgroundImage();
        LoadDecorations();
        LoadExtDecorations();
        LoadTitlePlates();
        LoadCustomFont();
        LoadTitleFont();
        SetupGdiplusResources();

        _gameHwnd = FindGameWindow();

        Plugin.Logger.LogInfo($"[Overlay] 游戏窗口 HWND: 0x{_gameHwnd.ToInt64():X}（本进程 PID {OwnPid()}）");

        LoadOverlayJson();
        RefreshScale(force: true);
        SetupDib();

        ComputeTargetPosition();
        bool initialShow = SceneTracker.ShouldShowOverlay();
        _screenY = initialShow ? _targetY : HiddenY();
        UpdateClipRect();
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
        if (hWnd == IntPtr.Zero)
        {
            Plugin.Logger.LogError("[Overlay] CreateWindowEx 失败");
            return;
        }

        if (_gameHwnd != IntPtr.Zero)
        {
            SetWindowLongPtrW(hWnd, GWLP_HWNDPARENT, _gameHwnd);
            Plugin.Logger.LogInfo("[Overlay] owner 已设置");
        }

        ShowWindow(hWnd, SW_SHOWNOACTIVATE);

        _displayRating = Plugin.DisplayRating;
        _targetRating = Plugin.DisplayRating;
        _displayExact = Plugin.DisplayExt;

        _nameText = Plugin.PlayerName;
        _mainText = Plugin.RatingText(_displayRating);
        _exactText = ((int)Math.Round(_displayExact)).ToString();
        _deltaText = "";
        _lastRenderedName = _nameText;
        _lastRenderedMain = _mainText;
        _lastRenderedExact = _exactText;
        _lastRenderedDelta = "";

        SyncTitle();
        RenderToDib();
        CommitToWindow(hWnd);

        _committed = true;
        _commitX = _screenX;
        _commitY = _screenY;
        _commitAlpha = AlphaByte();

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

                    if (RefreshScale())
                    {
                        SetupDib();
                        _needRedraw = true;
                    }

                    ComputeTargetPosition();

                    SyncTitle();
                    UpdateMouseAlpha();

                    bool panelOpen = PlayerPanel.Visible;
                    bool rawShouldShow = (SceneTracker.ShouldShowOverlay() || panelOpen)
                                         && !Plugin.OverlayManuallyHidden
                                         && (GameProbe.MvState == GameProbe.MvStateOff || panelOpen);

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

                    int destY = _stableShow ? _targetY : HiddenY();

                    if (_screenY != destY)
                    {
                        int diff = destY - _screenY;
                        if (Math.Abs(diff) <= 1)
                            _screenY = destY;
                        else
                            _screenY += (int)(diff * ANIM_LERP);
                    }

                    UpdateClipRect();

                    double targetRating = Plugin.DisplayRating;
                    int targetExact = Plugin.DisplayExt;
                    _targetRating = targetRating;

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

                    _nameText = Plugin.PlayerName;
                    _mainText = Plugin.RatingText(_displayRating);
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

                    if (_scaleReloadRequested)
                    {
                        _scaleReloadRequested = false;
                        LoadOverlayJson();
                        if (RefreshScale(force: true))
                        {
                            SetupDib();
                            _needRedraw = true;
                        }

                        _renderedTitleKey = "\0";
                    }

                    if (_decoReloadRequested)
                    {
                        _decoReloadRequested = false;

                        LoadOverlayJson();
                        if (RefreshScale(force: true))
                        {
                            SetupDib();
                            _needRedraw = true;
                        }

                        DisposeDecorations();
                        LoadDecorations();
                        _renderedDecoTier = "\0";
                        DisposeExtDecorations();
                        LoadExtDecorations();
                        _renderedExtDecoTier = "\0";

                        DisposeBackgroundImage();
                        LoadBackgroundImage();
                        DisposeTitlePlates();
                        LoadTitlePlates();
                        _renderedTitleKey = "\0";
                    }

                    string titleKey = _titleName.Length == 0
                        ? ""
                        : _titleGrade + "\u0001" + _titleText;

                    string decoTier = ResolveDecoTier(_displayRating);
                    string extDecoTier = ResolveExtDecoTier(_displayExact);

                    if (extDecoTier != _renderedExtDecoTier && _renderedExtDecoTier != "\0")
                    {
                        if (!_extTransActive)
                        {
                            _extTransActive = true;
                            _extTransStartAt = DateTime.Now;
                            _extTransOldTier = _renderedExtDecoTier;
                        }

                        _extTransNewTier = extDecoTier;
                    }

                    if (_extTransActive)
                    {
                        double tp = (DateTime.Now - _extTransStartAt).TotalMilliseconds / EXT_TRANS_DURATION_MS;
                        if (tp >= 1.0)
                        {
                            _extTransActive = false;
                            _extTransPhase = -1f;
                        }
                        else _extTransPhase = (float)(tp < 0.0 ? 0.0 : tp);
                    }
                    else _extTransPhase = -1f;

                    if (targetRating - _lastTargetRating >= 0.005 && !_sweepActive)
                    {
                        _sweepActive = true;
                        _sweepStartAt = DateTime.Now;

                        _sweepOldTop = _renderedGradTop;
                        _sweepOldBottom = _renderedGradBottom;
                    }

                    _lastTargetRating = targetRating;

                    if (_sweepActive)
                    {
                        double sp = (DateTime.Now - _sweepStartAt).TotalMilliseconds / SWEEP_DURATION_MS;
                        if (sp >= 1.0)
                        {
                            _sweepActive = false;
                            _sweepPhase = -1f;
                        }
                        else _sweepPhase = (float)(sp < 0.0 ? 0.0 : sp);
                    }
                    else _sweepPhase = -1f;

                    EffectiveGradient(_targetRating, out int gradTop, out int gradBottom);

                    bool clipChanged = _clipX0 != _renderedClipX0 || _clipY0 != _renderedClipY0 ||
                                       _clipX1 != _renderedClipX1 || _clipY1 != _renderedClipY1;

                    bool redrew = false;
                    if (_needRedraw ||
                        clipChanged ||
                        _nameText != _lastRenderedName ||
                        _mainText != _lastRenderedMain ||
                        _exactText != _lastRenderedExact ||
                        _deltaText != _lastRenderedDelta ||
                        decoTier != _renderedDecoTier ||
                        extDecoTier != _renderedExtDecoTier ||
                        gradTop != _renderedGradTop ||
                        gradBottom != _renderedGradBottom ||
                        _sweepPhase != _renderedSweepPhase ||
                        _extTransPhase != _renderedExtTransPhase ||
                        titleKey != _renderedTitleKey)
                    {
                        _needRedraw = false;
                        RenderToDib();
                        _lastRenderedName = _nameText;
                        _lastRenderedMain = _mainText;
                        _lastRenderedExact = _exactText;
                        _lastRenderedDelta = _deltaText;
                        _renderedDecoTier = decoTier;
                        _renderedExtDecoTier = extDecoTier;
                        _renderedGradTop = gradTop;
                        _renderedGradBottom = gradBottom;
                        _renderedSweepPhase = _sweepPhase;
                        _renderedExtTransPhase = _extTransPhase;
                        _renderedTitleKey = titleKey;
                        redrew = true;
                    }

                    byte alphaByte = AlphaByte();
                    if (redrew || !_committed
                               || _screenX != _commitX || _screenY != _commitY
                               || alphaByte != _commitAlpha)
                    {
                        CommitToWindow(hWnd, alphaByte);
                        _committed = true;
                        _commitX = _screenX;
                        _commitY = _screenY;
                        _commitAlpha = alphaByte;
                    }
                }
                catch
                {
                }

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
            DisposeDecorations();
            DisposeExtDecorations();
            DisposeTitlePlates();
            if (_privateFontCollection != IntPtr.Zero) GdipDeletePrivateFontCollection(ref _privateFontCollection);

            if (_fontTitle != IntPtr.Zero) GdipDeleteFont(_fontTitle);
            if (_brushTitleText != IntPtr.Zero) GdipDeleteBrush(_brushTitleText);
            if (_brushTitlePlate != IntPtr.Zero) GdipDeleteBrush(_brushTitlePlate);
            if (_titleFontCollection != IntPtr.Zero) GdipDeletePrivateFontCollection(ref _titleFontCollection);
            if (_gdiplusToken != IntPtr.Zero) GdiplusShutdown(_gdiplusToken);
            DisposeDib();
            PostQuitMessage(0);
            return IntPtr.Zero;
        }

        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }
}