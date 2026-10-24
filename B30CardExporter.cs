using System.Globalization;
using System.Runtime.InteropServices;
using BepInEx;

namespace InFalsusRating;

public static class B30CardExporter
{

    const float TEXT_OFFSET_Y = 14f;

    const int CARD_W = 220;
    const int CARD_H = 330;
    const int COLS   = 6;
    const int ROWS   = 5;
    const int GAP    = 30;
    const int OUTER  = 30;

    const int JACKET_X = 10;
    const int JACKET_Y = 10;
    const int JACKET_W = 200;
    const int JACKET_H = 200;

    const int INFO_W = 740;
    const int CARDS_ORIGIN_X = INFO_W + OUTER;

    const int BIG_W = CARDS_ORIGIN_X + COLS * CARD_W + (COLS - 1) * GAP + OUTER;
    const int BIG_H = OUTER * 2 + ROWS * CARD_H + (ROWS - 1) * GAP;

    const int B10_Y      = 545;
    const int B10_COUNT  = 10;
    const int B10_GAP    = 5;
    const int B10_AREA_H = 1055;
    const int B10_ROW_H  = (B10_AREA_H - (B10_COUNT - 1) * B10_GAP) / B10_COUNT;

    const float EXACT_SCALE  = 0.8f;
    const int B10_CARD_W     = (int)(CARDS_ORIGIN_X * EXACT_SCALE);
    const int B10_CARD_H     = (int)(B10_ROW_H * EXACT_SCALE);
    const int B10_CARD_X     = (CARDS_ORIGIN_X - B10_CARD_W) / 2;
    const int B10_CARD_OFFY  = (B10_ROW_H - B10_CARD_H) / 2;

    const int NJ_RANK_COL_W = 70;
    const int NJ_RANK_GAP   = 20;
    const int NJ_BLOCK_W    = NJ_RANK_COL_W + NJ_RANK_GAP + B10_CARD_W;
    const int NJ_BLOCK_X    = (CARDS_ORIGIN_X - NJ_BLOCK_W) / 2;
    const int NJ_CARD_X     = NJ_BLOCK_X + NJ_RANK_COL_W + NJ_RANK_GAP;

    static string Root        => Path.Combine(Paths.ConfigPath, "InFalsusRating");
    static string CardsDir    => Path.Combine(Root, "b30_cards");
    static string JacketDir   => Path.Combine(Root, "jacket");

    static string[] BigBgCandidates => new[]
    {
        Path.Combine(JacketDir, "background_big.png"),
        Path.Combine(CardsDir, "background_big.png"),
    };

    static readonly (string SubDir, bool IsJacket)[] LayerOrder = new[]
    {
        ("background", false),
        (null,         true),
        ("difficulty", false),
        ("score",      false),
        ("fcpm",       false),
        ("decor",      false),
    };

    struct TextSlot
    {
        public float X, Y, Size;
        public int Align, FontId, Color;
    }

    static readonly TextSlot SlotRank   = new TextSlot { X =  20f,   Y =  6f,   Size = 22f, Align = 1, FontId = 0, Color = unchecked((int)0xFFFFFFFF) };
    static readonly TextSlot SlotDiff   = new TextSlot { X = 180f,   Y = 185f,   Size = 16f, Align = 1, FontId = 0, Color = unchecked((int)0xFFFFFFFF) };
    static readonly TextSlot SlotTitle  = new TextSlot { X = 110f,   Y = 216f,   Size = 22f, Align = 1, FontId = 1, Color = unchecked((int)0xFFFFFFFF) };
    static readonly TextSlot SlotArtist = new TextSlot { X = 110f,   Y = 234f,   Size = 13f, Align = 1, FontId = 2, Color = unchecked((int)0xFFDFDFDF) };
    static readonly TextSlot SlotScore  = new TextSlot { X =  136f,   Y = 265f,   Size = 22f, Align = 1, FontId = 0, Color = unchecked((int)0xFFFFFFFF) };
    static readonly TextSlot SlotDive   = new TextSlot { X =  68f,   Y = 302f, Size = 24f, Align = 1, FontId = 0, Color = unchecked((int)0xFFFFFFFF) };

    static readonly TextSlot SlotExact  = new TextSlot { X = 171f, Y = 301f, Size = 20f, Align = 2, FontId = 0, Color = unchecked((int)0xFFFFFFFF) };

    const float RANK_SMALL_SIZE = 16f;

    const int TITLE_MAX_CHARS  = 16;
    const int ARTIST_MAX_CHARS = 26;

    static readonly TextSlot SlotB10Rank  = new TextSlot { X =  12f, Y = 28f, Size = 30f, Align = 0, FontId = 2, Color = unchecked((int)0xFFFFFFFF) };
    static readonly TextSlot SlotB10Title = new TextSlot { X =  72f, Y = 28f, Size = 26f, Align = 0, FontId = 1, Color = unchecked((int)0xFFFFFFFF) };
    static readonly TextSlot SlotB10Count = new TextSlot { X = 400f, Y = 28f, Size = 30f, Align = 1, FontId = 0, Color = unchecked((int)0xFFFFFFFF) };
    static readonly TextSlot SlotB10Rate  = new TextSlot { X = B10_CARD_W - 80f, Y = 28, Size = 30f, Align = 1, FontId = 0, Color = unchecked((int)0xFFFFFFFF) };

    static readonly TextSlot SlotB30RowTitle  = new TextSlot { X = 72f, Y = 18f, Size = 35f, Align = 0, FontId = 1, Color = unchecked((int)0xFFFFFFFF) };
    static readonly TextSlot SlotB30RowArtist = new TextSlot { X = 76f, Y = 43f, Size = 14f, Align = 0, FontId = 2, Color = unchecked((int)0xFFDFDFDF) };

    static readonly TextSlot SlotNJRank = new TextSlot { X = NJ_BLOCK_X + NJ_RANK_COL_W / 2f, Y = 36f, Size = 30f, Align = 1, FontId = 0, Color = unchecked((int)0xFFFFFFFF) };

    static readonly Dictionary<string, int> B10DiffColors = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        { "Minimal",   unchecked((int)0xFF17A9BD) },
        { "Evolved",   unchecked((int)0xFFC4E0FF) },
        { "Ultimate",  unchecked((int)0xFFE8C4FF) },
        { "Forbidden", unchecked((int)0xFFFFCCCC) },
    };

    public static int GetB10DiffColor(string difficulty)
        => B10DiffColors.TryGetValue(difficulty, out var c) ? c : unchecked((int)0xFFFFFFFF);

    static readonly int C_BG_TOP    = unchecked((int)0xFF0F1420);
    static readonly int C_BG_BOTTOM = unchecked((int)0xFF1A2238);

    const int PC_W = 668;
    const float PC_SCALE = PC_W / 453f;

    const int PC_H = (int)(152 * PC_SCALE);
    const int PC_R = (int)(14 * PC_SCALE);

    const int PC_X = (CARDS_ORIGIN_X - PC_W) / 2;

    const int PC_Y = 79;

    const int NJ_PC_DY = -36;
    const int PC_PAD = (int)(20 * PC_SCALE);
    const int PC_AVA_S = (int)(111 * PC_SCALE);
    const int PC_AVA_X = PC_X + PC_PAD;
    const int PC_AVA_Y = PC_Y + (PC_H - PC_AVA_S) / 2;
    const int PC_AVA_IMG = (int)(100 * PC_SCALE);
    const int PC_AVA_IMG_X = PC_AVA_X + (PC_AVA_S - PC_AVA_IMG) / 2;
    const int PC_AVA_IMG_Y = PC_AVA_Y + (PC_AVA_S - PC_AVA_IMG) / 2;
    const int PC_AVA_RING = (int)(3 * PC_SCALE);
    const int PC_RIGHT_X = PC_AVA_X + PC_AVA_S + (int)(24 * PC_SCALE);
    const int PC_RIGHT_W = PC_X + PC_W - PC_PAD - PC_RIGHT_X;
    const int PC_RIGHT_DY = (int)(6 * PC_SCALE);
    const int PC_NAME_CY = PC_Y + (int)(60 * PC_SCALE) + PC_RIGHT_DY;
    const float PC_INK_INSET = 0.2f;
    const int PC_NAME_SZ = (int)(60 * PC_SCALE);
    const int PC_NAME_SHADOW_DX = (int)(3 * PC_SCALE),
              PC_NAME_SHADOW_DY = (int)(3 * PC_SCALE);
    const int PC_NAME_SHADOW_ARGB = unchecked((int)0x26000000);
    const int PC_BAR_X = PC_RIGHT_X;
    const int PC_BAR_W = (int)(225 * PC_SCALE),
              PC_BAR_H = (int)(22 * PC_SCALE);
    const int PC_BAR_CY = PC_Y + (int)(106 * PC_SCALE) + PC_RIGHT_DY;
    const int PC_BAR_CX = PC_BAR_X + PC_BAR_W / 2;
    const int PC_BAR_PAD = (int)(12 * PC_SCALE);
    const int PC_TITLE_SZ = (int)(18 * PC_SCALE);

    const int PLATE_W = (int)(474 * PC_SCALE),
              PLATE_H = (int)(79 * PC_SCALE);

    const int PLATE_BLEED = 40;
    const int PLATE_X = -PLATE_BLEED;
    const int PLATE_Y = PC_Y + PC_H + (int)(22 * PC_SCALE);

    const int PLATE_ART_DX = -(int)(284 * PC_SCALE),
              PLATE_ART_DY = -(int)(7 * PC_SCALE);
    const int PLATE_RAT_DECO_DY = (int)(5 * PC_SCALE);
    const int PLATE_RAT_NUM_DY = (int)(5 * PC_SCALE);

    const float PLATE_RAT_X = Win32Overlay.OVERLAY_W * Win32Overlay.MAIN_X * PC_SCALE + PLATE_ART_DX;
    const float PLATE_RAT_Y = Win32Overlay.LAYOUT_REF_H * Win32Overlay.MAIN_Y * PC_SCALE + PLATE_ART_DY;
    const float PLATE_RAT_W = Win32Overlay.OVERLAY_W * Win32Overlay.MAIN_W * PC_SCALE;
    const float PLATE_RAT_H = Win32Overlay.LAYOUT_REF_H * Win32Overlay.MAIN_H * PC_SCALE;
    const float PLATE_EXT_X = Win32Overlay.OVERLAY_W * Win32Overlay.EXACT_X * PC_SCALE + PLATE_ART_DX;
    const float PLATE_EXT_Y = Win32Overlay.LAYOUT_REF_H * Win32Overlay.EXACT_Y * PC_SCALE + PLATE_ART_DY;
    const float PLATE_EXT_W = Win32Overlay.OVERLAY_W * Win32Overlay.EXACT_W * PC_SCALE;
    const float PLATE_EXT_H = Win32Overlay.LAYOUT_REF_H * Win32Overlay.EXACT_H * PC_SCALE;

    const float PC_RAT_SZ = Win32Overlay.LAYOUT_REF_H * Win32Overlay.MAIN_FONT_RATIO * PC_SCALE;
    const float PC_VAL_SZ = Win32Overlay.LAYOUT_REF_H * Win32Overlay.EXACT_FONT_RATIO * PC_SCALE;
    const int PLATE_R = PLATE_H / 2;

    const int PC_DECO_W = (int)(Win32Overlay.OVERLAY_W * PC_SCALE);
    const int PC_DECO_H = (int)(Win32Overlay.OVERLAY_H * PC_SCALE);

    const int PC_COL_DIM  = unchecked((int)0xFF9AA3B8);
    const int PC_COL_GOLD = unchecked((int)0xFFF0C24B);
    const int PC_COL_INK  = unchecked((int)0xFF23262E);
    const int PC_COL_VAL  = unchecked((int)0xFFEDF1F7);
    const int PC_COL_OFF  = unchecked((int)0x2EFFFFFF);

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

    static IntPtr _fontCollection = IntPtr.Zero;
    static IntPtr _furoreFamily   = IntPtr.Zero;
    static IntPtr _segoeFamily    = IntPtr.Zero;

    static IntPtr _fontRank      = IntPtr.Zero;
    static IntPtr _fontRankSmall = IntPtr.Zero;
    private static Dictionary<float, IntPtr> _fontJacketConstDictionary = new();
    static IntPtr _fontScore  = IntPtr.Zero;
    static IntPtr _fontDive   = IntPtr.Zero;
    static IntPtr _fontExact  = IntPtr.Zero;
    static IntPtr _fontTitle  = IntPtr.Zero;
    static IntPtr _fontArtist = IntPtr.Zero;

    static IntPtr _fontTitleFurore    = IntPtr.Zero;
    static IntPtr _fontB10TitleFurore = IntPtr.Zero;

    static IntPtr _cjFontCollection = IntPtr.Zero;
    static IntPtr _cjFamily         = IntPtr.Zero;
    static IntPtr _fontPcName  = IntPtr.Zero;
    static IntPtr _fontPcTitle = IntPtr.Zero;
    static IntPtr _fontPcRat   = IntPtr.Zero;
    static IntPtr _fontPcVal   = IntPtr.Zero;

    static IntPtr _brPcShadow = IntPtr.Zero;
    static IntPtr _brPcDim    = IntPtr.Zero;
    static IntPtr _brPcGold   = IntPtr.Zero;
    static IntPtr _brPcInk    = IntPtr.Zero;
    static IntPtr _brPcVal    = IntPtr.Zero;
    static IntPtr _brPcOff    = IntPtr.Zero;
    
    static Dictionary<float, IntPtr> _fontNoJacketConstDictionary = new();
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

    [DllImport("gdiplus.dll")] static extern int GdipDrawImageRectRectI(IntPtr graphics, IntPtr image,
        int dstx, int dsty, int dstwidth, int dstheight,
        int srcx, int srcy, int srcwidth, int srcheight,
        int srcUnit, IntPtr imageAttributes, IntPtr callback, IntPtr callbackData);
    [DllImport("gdiplus.dll")] static extern int GdipSetInterpolationMode(IntPtr graphics, int mode);
    [DllImport("gdiplus.dll")] static extern int GdipSetSmoothingMode(IntPtr graphics, int mode);

    [DllImport("gdiplus.dll")] static extern int GdipCreatePath(int brushMode, out IntPtr path);
    [DllImport("gdiplus.dll")] static extern int GdipAddPathArc(IntPtr path, float x, float y, float width, float height, float startAngle, float sweepAngle);
    [DllImport("gdiplus.dll")] static extern int GdipAddPathRectangle(IntPtr path, float x, float y, float width, float height);
    [DllImport("gdiplus.dll")] static extern int GdipClosePathFigure(IntPtr path);
    [DllImport("gdiplus.dll")] static extern int GdipSetClipPath(IntPtr graphics, IntPtr path, int combineMode);
    [DllImport("gdiplus.dll")] static extern int GdipFillPath(IntPtr graphics, IntPtr brush, IntPtr path);
    [DllImport("gdiplus.dll")] static extern int GdipDeletePath(IntPtr path);

    [DllImport("gdiplus.dll")] static extern int GdipCreateLineBrushFromRectWithAngle(ref GPRECTF rect,
        int color1, int color2, float angle, int isAngleScalable, int wrapMode, out IntPtr brush);

    [DllImport("gdiplus.dll")] static extern int GdipGetImageEncodersSize(out int numEncoders, out int size);
    [DllImport("gdiplus.dll")] static extern int GdipGetImageEncoders(int numEncoders, int size, IntPtr encoders);
    [DllImport("gdiplus.dll", CharSet = CharSet.Unicode)] static extern int GdipSaveImageToFile(IntPtr image, string filename, ref Guid clsid, IntPtr encoderParams);

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

    static bool InitFonts()
    {

        string fontPath = Plugin.AssetPath("font2.ttf");
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

        if (GdipCreateFontFamilyFromName("Segoe UI", IntPtr.Zero, out _segoeFamily) != 0
            || _segoeFamily == IntPtr.Zero)
            GdipCreateFontFamilyFromName("Arial", IntPtr.Zero, out _segoeFamily);

        if (_furoreFamily == IntPtr.Zero || _segoeFamily == IntPtr.Zero) return false;

        float constSize = ConstTable.TextSizeScale();

        GdipCreateFont(_furoreFamily, SlotRank.Size,   FontStyleBold,    UnitPixel, out _fontRank);
        GdipCreateFont(_furoreFamily, RANK_SMALL_SIZE, FontStyleBold,    UnitPixel, out _fontRankSmall);
        _fontJacketConstDictionary[constSize] = RegisterJacketConstFont(constSize);
        GdipCreateFont(_furoreFamily, SlotScore.Size,  FontStyleBold,    UnitPixel, out _fontScore);
        GdipCreateFont(_furoreFamily, SlotDive.Size,   FontStyleBold,    UnitPixel, out _fontDive);
        GdipCreateFont(_furoreFamily, SlotExact.Size,  FontStyleBold,    UnitPixel, out _fontExact);
        GdipCreateFont(_segoeFamily,  SlotTitle.Size,  FontStyleBold,    UnitPixel, out _fontTitle);
        GdipCreateFont(_segoeFamily,  SlotArtist.Size, FontStyleRegular, UnitPixel, out _fontArtist);

        GdipCreateFont(_furoreFamily, SlotTitle.Size,    FontStyleBold, UnitPixel, out _fontTitleFurore);
        GdipCreateFont(_furoreFamily, SlotB10Title.Size, FontStyleBold, UnitPixel, out _fontB10TitleFurore);
        
        _fontNoJacketConstDictionary[constSize] = RegisterNoJacketConstFont(constSize);
        GdipCreateFont(_furoreFamily, SlotB10Rank.Size,  FontStyleBold, UnitPixel, out _fontB10Rank);
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

        GdipCreateStringFormat(StringFormatFlagsNoWrap, 0, out _fmtNameNowrap);
        GdipSetStringFormatAlign(_fmtNameNowrap, StringAlignmentNear);
        GdipSetStringFormatLineAlign(_fmtNameNowrap, StringAlignmentLineCenter);

        InitCardFonts();

        return true;
    }

    private static IntPtr RegisterNoJacketConstFont(float scale)
    {
        GdipCreateFont(_furoreFamily, scale * SlotB10Rank.Size, FontStyleBold, UnitPixel, out var tmp);
        return tmp;
    }
    
    private static IntPtr RegisterJacketConstFont(float scale)
    {
        GdipCreateFont(_furoreFamily, scale * SlotDiff.Size, FontStyleBold, UnitPixel, out var tmp);
        return tmp;
    }

        static void InitCardFonts()
    {
        string fontPath = Plugin.AssetPath("font3.ttf");
        if (_cjFamily == IntPtr.Zero && File.Exists(fontPath))
        {
            if (_cjFontCollection == IntPtr.Zero) GdipNewPrivateFontCollection(out _cjFontCollection);
            if (_cjFontCollection != IntPtr.Zero &&
                GdipPrivateAddFontFile(_cjFontCollection, fontPath) != 0)
                Plugin.Logger.LogWarning($"[B30Card] 中文字体加载失败: {fontPath}");

            if (_cjFontCollection != IntPtr.Zero)
            {

                if (GdipCreateFontFamilyFromName("Alimama ShuHeiTi", _cjFontCollection, out _cjFamily) != 0)
                    GdipCreateFontFamilyFromName("阿里妈妈数黑体", _cjFontCollection, out _cjFamily);
            }
        }

        IntPtr cjk = _cjFamily != IntPtr.Zero ? _cjFamily : _segoeFamily;
        if (cjk == IntPtr.Zero) cjk = _furoreFamily;

        _fontPcName = MakeCardFont(_furoreFamily, PC_NAME_SZ, FontStyleBold);
        _fontPcRat  = MakeCardFont(_furoreFamily, PC_RAT_SZ,  FontStyleBold);
        _fontPcVal  = MakeCardFont(_furoreFamily, PC_VAL_SZ,  FontStyleBold);

        if (cjk != IntPtr.Zero)
            GdipCreateFont(cjk, PC_TITLE_SZ, FontStyleRegular, UnitPixel, out _fontPcTitle);

        GdipCreateSolidFill(PC_NAME_SHADOW_ARGB, out _brPcShadow);
        GdipCreateSolidFill(PC_COL_DIM,  out _brPcDim);
        GdipCreateSolidFill(PC_COL_GOLD, out _brPcGold);
        GdipCreateSolidFill(PC_COL_INK,  out _brPcInk);
        GdipCreateSolidFill(PC_COL_VAL,  out _brPcVal);
        GdipCreateSolidFill(PC_COL_OFF,  out _brPcOff);
    }

        static IntPtr MakeCardFont(IntPtr family, float size, int style)
    {
        if (family == IntPtr.Zero) return IntPtr.Zero;
        if (GdipCreateFont(family, size, style, UnitPixel, out IntPtr f) != 0 || f == IntPtr.Zero)
            GdipCreateFont(family, size, FontStyleRegular, UnitPixel, out f);
        return f;
    }

    static void FreeCardFonts()
    {
        if (_fontPcName  != IntPtr.Zero) { GdipDeleteFont(_fontPcName);  _fontPcName  = IntPtr.Zero; }
        if (_fontPcTitle != IntPtr.Zero) { GdipDeleteFont(_fontPcTitle); _fontPcTitle = IntPtr.Zero; }
        if (_fontPcRat   != IntPtr.Zero) { GdipDeleteFont(_fontPcRat);   _fontPcRat   = IntPtr.Zero; }
        if (_fontPcVal   != IntPtr.Zero) { GdipDeleteFont(_fontPcVal);   _fontPcVal   = IntPtr.Zero; }

        if (_brPcShadow != IntPtr.Zero) { GdipDeleteBrush(_brPcShadow); _brPcShadow = IntPtr.Zero; }
        if (_brPcDim    != IntPtr.Zero) { GdipDeleteBrush(_brPcDim);    _brPcDim    = IntPtr.Zero; }
        if (_brPcGold   != IntPtr.Zero) { GdipDeleteBrush(_brPcGold);   _brPcGold   = IntPtr.Zero; }
        if (_brPcInk    != IntPtr.Zero) { GdipDeleteBrush(_brPcInk);    _brPcInk    = IntPtr.Zero; }
        if (_brPcVal    != IntPtr.Zero) { GdipDeleteBrush(_brPcVal);    _brPcVal    = IntPtr.Zero; }
        if (_brPcOff    != IntPtr.Zero) { GdipDeleteBrush(_brPcOff);    _brPcOff    = IntPtr.Zero; }

        if (_cjFontCollection != IntPtr.Zero)
        {
            GdipDeletePrivateFontCollection(ref _cjFontCollection);
            _cjFontCollection = IntPtr.Zero;
        }
        _cjFamily = IntPtr.Zero;
    }

    static void FreeFonts()
    {
        KeyValuePair<float, IntPtr>[] pairs = _fontNoJacketConstDictionary.ToArray();
        foreach (KeyValuePair<float, IntPtr> pair in pairs)
        {
            if (pair.Value != IntPtr.Zero) GdipDeleteFont(pair.Value);
            _fontNoJacketConstDictionary.Remove(pair.Key);
        }
        if (_fontRank      != IntPtr.Zero) GdipDeleteFont(_fontRank);
        if (_fontRankSmall != IntPtr.Zero) GdipDeleteFont(_fontRankSmall);
        pairs = _fontJacketConstDictionary.ToArray();
        foreach (KeyValuePair<float, IntPtr> pair in pairs)
        {
            if (pair.Value != IntPtr.Zero) GdipDeleteFont(pair.Value);
            _fontJacketConstDictionary.Remove(pair.Key);
        }
        if (_fontScore  != IntPtr.Zero) GdipDeleteFont(_fontScore);
        if (_fontDive   != IntPtr.Zero) GdipDeleteFont(_fontDive);
        if (_fontExact  != IntPtr.Zero) GdipDeleteFont(_fontExact);
        if (_fontTitle  != IntPtr.Zero) GdipDeleteFont(_fontTitle);
        if (_fontArtist != IntPtr.Zero) GdipDeleteFont(_fontArtist);
        if (_fontTitleFurore    != IntPtr.Zero) GdipDeleteFont(_fontTitleFurore);
        if (_fontB10TitleFurore != IntPtr.Zero) GdipDeleteFont(_fontB10TitleFurore);
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

        FreeCardFonts();

        if (_fontCollection != IntPtr.Zero) GdipDeletePrivateFontCollection(ref _fontCollection);
    }

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

    public static int ScoreTierIndex(ulong score)
    {
        if (score < 90_000_000) return 0;
        if (score < 95_000_000) return 1;
        if (score < 98_000_000) return 2;
        if (score < 99_000_000) return 3;
        return 4;
    }

    public static string ScoreTier(ulong score) => ScoreTierIndex(score).ToString();

    static string FindScoreLayerFile(string songId, string difficulty, ulong score)
    {
        string dir = Path.Combine(CardsDir, "score");
        if (!Directory.Exists(dir)) return null;

        var exts = new[] { ".png", ".jpg", ".jpeg" };

        string tier = ScoreTier(score);
        foreach (var e in exts)
        {
            string p = Path.Combine(dir, tier + e);
            if (File.Exists(p)) return p;
        }

        foreach (var e in exts)
        {
            string p = Path.Combine(dir, $"{songId}_{difficulty}" + e);
            if (File.Exists(p)) return p;
        }

        return null;
    }

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

    public static string FindExactLayerFile(string difficulty)
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

    static string DifficultyAbbr(string d) => d switch
    {
        "Minimal"   => "MIN",
        "Evolved"   => "EVO",
        "Ultimate"  => "ULT",
        "Forbidden" => "FBD",
        _           => d.ToUpperInvariant(),
    };

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

            DrawPlayerCard(bigGfx, playerName, rating, totalExacts);

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

            const int NJ_ROWS_Y = 523;
            const int BOTTOM = 330;

            IntPtr bg = LoadImageCached(FindNoJacketBg());
            int bigW = CARDS_ORIGIN_X;
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

            DrawPlayerCard(gfx, playerName, rating, totalExacts, NJ_PC_DY);

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

    static void DrawB30Row(IntPtr g, ChartRating c, int rank, int rowY)
    {
        IntPtr layer = LoadImageCached(FindExactLayerFile(c.Difficulty));
        if (layer != IntPtr.Zero)
            GdipDrawImageRectI(g, layer, NJ_CARD_X, rowY + B10_CARD_OFFY, B10_CARD_W, B10_CARD_H);

        int diffColor = GetB10DiffColor(c.Difficulty);
        string title  = SongInfoCache.GetName(c.SongId);
        string artist = SongInfoCache.GetArtist(c.SongId);

        DrawSlot(g, _fontB10Rank, SlotNJRank, $"#{rank}", 0, rowY);

        float constSize = ConstTable.TextSizeScale();
        if (!_fontNoJacketConstDictionary.TryGetValue(constSize, out var fontDiff))
        {
            _fontNoJacketConstDictionary[constSize] = fontDiff = RegisterJacketConstFont(constSize);
        }
        DrawSlot(g, fontDiff,  SlotB10Rank,  ConstTable.Text(c.Constant), NJ_CARD_X, rowY + B10_CARD_OFFY, diffColor);
        DrawSlot(g, _fontB10Count, SlotB10Count, Plugin.RatingText(c.Rating),    NJ_CARD_X, rowY + B10_CARD_OFFY);
        DrawSlot(g, _fontB10Rate,  SlotB10Rate,  c.ExactRate.ToString("F0"), NJ_CARD_X, rowY + B10_CARD_OFFY);

        IntPtr titleFont = TitleUseFurore(c.SongId)
            ? FitFontToWidth(g, _furoreFamily, FontStyleBold, SlotB30RowTitle.Size, title, 270, _fontB30TitleFurore)
            : FitFontToWidth(g, _segoeFamily,  FontStyleBold, SlotB30RowTitle.Size, title, 270, _fontB30Title);

        var measure = new GPRECTF { X = 0, Y = 0, Width = 10000f, Height = 10000f };
        GdipMeasureString(g, title, -1, titleFont, ref measure, IntPtr.Zero, out var titleBox, out _, out _);
        float titleH = titleBox.Height;

        float artistH = 0f;
        if (!string.IsNullOrEmpty(artist))
        {
            GdipMeasureString(g, artist, -1, _fontB30Artist, ref measure, IntPtr.Zero, out var artistBox, out _, out _);
            artistH = artistBox.Height;
        }

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

    public static bool TitleUseFurore(string songId) => songId == "54";

    static IntPtr FitFontToWidth(IntPtr g, IntPtr family, int style, float baseSize, string text, int maxWidth, IntPtr baseFont)
    {
        var layout = new GPRECTF { X = 0, Y = 0, Width = 10000f, Height = 10000f };

        GdipMeasureString(g, text, -1, baseFont, ref layout, IntPtr.Zero, out var box, out _, out _);
        if (box.Width <= maxWidth) return baseFont;

        float size = baseSize * maxWidth / box.Width;

        GdipCreateFont(family, size, style, UnitPixel, out IntPtr font);
        return font;
    }

    static IntPtr GetFitFont(IntPtr family, int style, float baseSize, string text, int maxChars, IntPtr baseFont)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars) return baseFont;

        float size = baseSize * maxChars / text.Length;

        GdipCreateFont(family, size, style, UnitPixel, out IntPtr font);
        return font;
    }

    static void DrawCard(IntPtr g, ChartRating c, int rank, int cardX, int cardY)
    {

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

                GdipDrawImageRectI(g, bmp,
                    cardX + JACKET_X, cardY + JACKET_Y,
                    JACKET_W, JACKET_H);
            }
            else
            {

                GdipDrawImageRectI(g, bmp, cardX, cardY, CARD_W, CARD_H);
            }
        }

        string artist    = SongInfoCache.GetArtist(c.SongId);
        string diffText  = $"{DifficultyAbbr(c.Difficulty)} {ConstTable.Text(c.Constant)}";
        Plugin.Logger.LogInfo($"[B30Card] {diffText}");
        string scoreText = c.Score.ToString("N0").Replace(",", "'");
        string diveText  = c.PaceValue >= 0 ? $"+{c.PaceValue}" : c.PaceValue.ToString();

        string potentialText = Plugin.RatingText(c.Rating);
        string titleText = SongInfoCache.GetName(c.SongId);

        IntPtr rankFont = rank >= 10 ? _fontRankSmall : _fontRank;
        IntPtr titleFont = TitleUseFurore(c.SongId)
            ? GetFitFont(_furoreFamily, FontStyleBold, SlotTitle.Size, titleText, TITLE_MAX_CHARS, _fontTitleFurore)
            : GetFitFont(_segoeFamily,  FontStyleBold, SlotTitle.Size, titleText, TITLE_MAX_CHARS, _fontTitle);
        IntPtr artistFont = GetFitFont(_segoeFamily, FontStyleRegular, SlotArtist.Size, artist,    ARTIST_MAX_CHARS, _fontArtist);
        float constSize = ConstTable.TextSizeScale();
        if (!_fontJacketConstDictionary.TryGetValue(constSize, out var fontDiff))
        {
            _fontJacketConstDictionary[constSize] = fontDiff = RegisterJacketConstFont(constSize);
        }
        
        DrawSlot(g, rankFont, SlotRank, $"#{rank}", cardX, cardY);
        DrawSlot(g, fontDiff,   SlotDiff,   diffText,   cardX, cardY);
        DrawSlot(g, titleFont,  SlotTitle,  titleText,  cardX, cardY);
        DrawSlot(g, artistFont, SlotArtist, artist,     cardX, cardY);
        DrawSlot(g, _fontScore,  SlotScore,  scoreText,  cardX, cardY);
        DrawSlot(g, _fontDive,   SlotDive,   diveText,   cardX, cardY);
        DrawSlot(g, _fontExact,  SlotExact,  potentialText,  cardX, cardY);

        if (titleFont  != _fontTitle && titleFont != _fontTitleFurore) GdipDeleteFont(titleFont);
        if (artistFont != _fontArtist) GdipDeleteFont(artistFont);
    }

    static void DrawSlot(IntPtr g, IntPtr font, TextSlot slot, string text, int cardX, int cardY, int? colorOverride = null)
    {
        if (string.IsNullOrEmpty(text) || font == IntPtr.Zero) return;

        float halfW = CARD_W;
        float h     = slot.Size * 2.5f;

        float rectX = slot.Align switch
        {
            0 => slot.X,
            1 => slot.X - halfW,
            _ => slot.X - halfW * 2f,
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

        int color = colorOverride ?? slot.Color;
        IntPtr brush;
        GdipCreateSolidFill(color, out brush);

        GdipDrawString(g, text, -1, font, ref rect, fmt, brush);
        GdipDeleteBrush(brush);
    }

    static void DrawPlayerCard(IntPtr g, string playerName, double rating, int totalExacts, int dy = 0)
    {

        DrawRatingPlate(g, rating, totalExacts, dy);

        string nm = string.IsNullOrEmpty(playerName) ? "Player" : playerName;
        CardTitle(out bool titleOn, out string titleName, out int titleGrade);
        DrawIdentityCard(g, nm, titleOn, titleName, titleGrade, dy);
    }

    static void CardTitle(out bool on, out string name, out int grade)
    {
        on = false; name = ""; grade = 0;
        try
        {
            var d = Achievements.EquippedDef;
            if (d == null) return;
            name = !string.IsNullOrEmpty(d.Name) ? d.Name : d.Id;
            grade = Achievements.TitleDisplayGrade(d);
            on = true;
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[B30Card] 称号读取失败（按没戴画）: {ex.Message}");
            on = false; name = ""; grade = 0;
        }
    }

    static void DrawRatingPlate(IntPtr g, double rating, int totalExacts, int dy)
    {

        int py = PLATE_Y + dy;

        var rbg = GuiTex("playerinfo/ratingbg.png");
        if (rbg != IntPtr.Zero)
        {
            GdipDrawImageRectI(g, rbg, PLATE_X, py, PLATE_W, PLATE_H);
        }
        else
        {
            FillRoundRect(g, _brPcOff, PLATE_X, py, PLATE_W, PLATE_H, PLATE_R);

            RowTextC(g, "PROTENTIAL", _fontPcTitle, _brPcDim,
                (int)(PLATE_X + PLATE_RAT_X + PLATE_RAT_W / 2f),
                (int)(py + PLATE_RAT_Y + PLATE_RAT_H + 5 * PC_SCALE));
            RowTextC(g, "EXACTIFICATION", _fontPcTitle, _brPcDim,
                (int)(PLATE_X + PLATE_EXT_X + PLATE_EXT_W / 2f),
                (int)(py + PLATE_EXT_Y - 5 * PC_SCALE));
        }

        string ratText = Plugin.RatingText(rating);
        string extText = totalExacts.ToString("N0", CultureInfo.InvariantCulture);

        RatingGradient(rating, out int ratTop, out int ratBot);

        PlateText(g, ratText, _fontPcRat, ratTop, ratBot, _brWhite,
            PLATE_X + PLATE_RAT_X, py + PLATE_RAT_Y + PLATE_RAT_NUM_DY,
            PLATE_RAT_W, PLATE_RAT_H);
        PlateText(g, extText, _fontPcVal, Win32Overlay.GRADIENT_TOP, Win32Overlay.GRADIENT_BOTTOM, _brPcVal,
            PLATE_X + PLATE_EXT_X, py + PLATE_EXT_Y, PLATE_EXT_W, PLATE_EXT_H);

        var decoRat = ResolvePlateDeco(Win32Overlay.DECO_TIERS, rating);
        if (decoRat != IntPtr.Zero)
            GdipDrawImageRectI(g, decoRat, PLATE_X + PLATE_ART_DX,
                py + PLATE_ART_DY + PLATE_RAT_DECO_DY,
                PC_DECO_W, PC_DECO_H);
        var decoExt = ResolvePlateDeco(Win32Overlay.EXT_DECO_TIERS, totalExacts);
        if (decoExt != IntPtr.Zero)
            GdipDrawImageRectI(g, decoExt, PLATE_X + PLATE_ART_DX,
                py + PLATE_ART_DY, PC_DECO_W, PC_DECO_H);
    }

    static void DrawIdentityCard(IntPtr g, string nm, bool titleOn, string titleName, int titleGrade, int dy)
    {

        int cy   = PC_Y + dy;
        int ay   = PC_AVA_Y + dy;
        int iy   = PC_AVA_IMG_Y + dy;
        int ncy  = PC_NAME_CY + dy;
        int bcy  = PC_BAR_CY + dy;

        IntPtr skin = BuildCardArt();
        try
        {
            IntPtr art = skin != IntPtr.Zero ? skin : GuiTex("info/card.png");
            if (art != IntPtr.Zero) DrawRoundClip(g, art, PC_X, cy, PC_W, PC_H, PC_R);
            else FillRoundRect(g, _brPcOff, PC_X, cy, PC_W, PC_H, PC_R);
        }
        finally { if (skin != IntPtr.Zero) GdipDisposeImage(skin); }

        IntPtr ava = BuildAvatarArt();
        try
        {
            var avaBg = GuiTex("playerinfo/avatar_bg.png");
            if (avaBg != IntPtr.Zero)
            {
                if (ava == IntPtr.Zero)
                {

                    GdipDrawImageRectI(g, avaBg, PC_AVA_X, ay, PC_AVA_S, PC_AVA_S);
                }
                else
                {

                    var ring = RingPath(PC_AVA_X, ay, PC_AVA_S, PC_AVA_S, PC_AVA_RING);
                    if (ring != IntPtr.Zero)
                    {
                        GdipSetClipPath(g, ring, 0 );
                        GdipDrawImageRectI(g, avaBg, PC_AVA_X, ay, PC_AVA_S, PC_AVA_S);
                        GdipResetClip(g);
                        GdipDeletePath(ring);
                    }
                }
            }
            if (ava != IntPtr.Zero)
            {
                GdipDrawImageRectI(g, ava, PC_AVA_IMG_X, iy, PC_AVA_IMG, PC_AVA_IMG);

                var frame = GuiTex("playerinfo/avatar_frame.png");
                if (frame != IntPtr.Zero)
                    GdipDrawImageRectI(g, frame, PC_AVA_X, ay, PC_AVA_S, PC_AVA_S);
            }
        }
        finally { if (ava != IntPtr.Zero) GdipDisposeImage(ava); }

        float nsz;
        IntPtr nf = FitFontSz(g, nm, _furoreFamily, FontStyleBold, PC_NAME_SZ,
            PC_RIGHT_W, _fontPcName, out nsz);
        if (nf != IntPtr.Zero)
        {
            int nInset = (int)Math.Round(nsz * PC_INK_INSET);
            int nx = PC_RIGHT_X - nInset;

            int sx = nx + PC_NAME_SHADOW_DX;
            float sy = ncy + PC_NAME_SHADOW_DY;
            RowTextL(g, nm, nf, _brPcShadow, sx, sy);
            RowTextL(g, nm, nf, _brPcShadow, sx - 1, sy);
            RowTextL(g, nm, nf, _brPcShadow, sx + 1, sy);
            RowTextL(g, nm, nf, _brPcShadow, sx, sy - 1f);
            RowTextL(g, nm, nf, _brPcShadow, sx, sy + 1f);
            RowTextL(g, nm, nf, _brWhite, nx, ncy);
            if (nf != _fontPcName) GdipDeleteFont(nf);
        }

        int by = bcy - PC_BAR_H / 2;
        if (titleOn)
        {
            var bar = TitleBarTex(titleGrade);
            if (bar != IntPtr.Zero) GdipDrawImageRectI(g, bar, PC_BAR_X, by, PC_BAR_W, PC_BAR_H);
            else FillRoundRect(g, _brPcOff, PC_BAR_X, by, PC_BAR_W, PC_BAR_H, PC_BAR_H / 2);

            RowTextC(g, ClipTitleLine(titleName, (PC_BAR_W - PC_BAR_PAD * 2) * 20 / PC_TITLE_SZ),
                _fontPcTitle, bar != IntPtr.Zero ? _brPcInk : _brPcGold, PC_BAR_CX, bcy);
        }
        else
        {

            RowTextC(g, "称号：没戴", _fontPcTitle, _brPcDim, PC_BAR_CX, bcy);
        }
    }

    static void DrawRoundClip(IntPtr g, IntPtr img, int x, int y, int w, int h, int r)
    {
        var clip = RoundRectPath(x, y, w, h, r);
        if (clip == IntPtr.Zero) { GdipDrawImageRectI(g, img, x, y, w, h); return; }
        GdipSetClipPath(g, clip, 0 );
        GdipDrawImageRectI(g, img, x, y, w, h);
        GdipResetClip(g);
        GdipDeletePath(clip);
    }

    static string GuiDir => Plugin.AssetPath("GUI");

    static IntPtr GuiTex(string rel) => LoadImageCached(Path.Combine(GuiDir, rel));

    static IntPtr DecoImage(string file)
        => string.IsNullOrEmpty(file) ? IntPtr.Zero
           : LoadImageCached(Path.Combine(Plugin.AssetPath("deco"), file));

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

        static IntPtr TitleBarTex(int grade)
    {
        if (grade <= 0) return IntPtr.Zero;
        var t = GuiTex("playerinfo/archt" + grade + ".png");
        return t != IntPtr.Zero ? t : GuiTex("playerinfo/archt4.png");
    }

        static readonly string[] PC_SKIN_EXTS = { ".png", ".jpg", ".jpeg", ".bmp" };

        static string SkinFile(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (name.IndexOf('/') >= 0 || name.IndexOf('\\') >= 0 || name.IndexOf(':') >= 0) return null;
        if (name.Contains("..")) return null;

        string dir = Plugin.AssetPath("jacket");
        foreach (var ext in PC_SKIN_EXTS)
        {
            string f = Path.Combine(dir, name + ext);
            if (File.Exists(f)) return f;
        }
        return null;
    }

    static bool ImageSize(IntPtr img, out int w, out int h)
    {
        w = 0; h = 0;
        if (GdipGetImageWidth(img, out uint uw) != 0) return false;
        if (GdipGetImageHeight(img, out uint uh) != 0) return false;
        w = (int)uw; h = (int)uh;
        return w > 0 && h > 0;
    }

        static IntPtr BuildAvatarArt()
    {
        if (string.IsNullOrEmpty(PlayerSkin.Avatar)) return IntPtr.Zero;
        string path = SkinFile(PlayerSkin.Avatar);
        if (path == null) return IntPtr.Zero;
        IntPtr src = LoadImageCached(path);
        if (src == IntPtr.Zero || !ImageSize(src, out int sw, out int sh)) return IntPtr.Zero;
        return BuildCover(src, sw, sh, 0.5, 0.5, 1.0, PC_AVA_IMG, PC_AVA_IMG);
    }

        static IntPtr BuildCardArt()
    {
        if (string.IsNullOrEmpty(PlayerSkin.Card)) return IntPtr.Zero;
        string path = SkinFile(PlayerSkin.Card);
        if (path == null) return IntPtr.Zero;
        IntPtr src = LoadImageCached(path);
        if (src == IntPtr.Zero || !ImageSize(src, out int sw, out int sh)) return IntPtr.Zero;
        return BuildCardCover(src, sw, sh,
            PlayerSkin.CardCx, PlayerSkin.CardCy, PlayerSkin.CardZoom,
            PC_W, PC_H, PlayerSkin.CardBlur);
    }

    static void PlateText(IntPtr g, string text, IntPtr font, int topRgb, int bottomRgb,
        IntPtr fallback, float x, float y, float w, float h)
    {
        if (font == IntPtr.Zero || string.IsNullOrEmpty(text)) return;
        var r = new GPRECTF { X = x, Y = y, Width = w, Height = h };
        var brush = CreateGradient(ref r, topRgb, bottomRgb);
        if (brush == IntPtr.Zero) brush = fallback;
        if (brush == IntPtr.Zero) return;
        GdipDrawString(g, text, -1, font, ref r, _fmtMid, brush);
        if (brush != fallback) GdipDeleteBrush(brush);
    }

        static IntPtr CreateGradient(ref GPRECTF rect, int startRgb, int endRgb)
    {
        int status = GdipCreateLineBrushFromRectWithAngle(ref rect,
            unchecked((int)0xFF000000) | startRgb,
            unchecked((int)0xFF000000) | endRgb,
            (float)Win32Overlay.GRADIENT_ANGLE_DEG, 0, WrapModeTile, out var brush);
        if (status != 0)
        {
            Plugin.Logger.LogWarning($"[B30Card] 渐变笔刷创建失败: status={status}");
            return IntPtr.Zero;
        }
        return brush;
    }

        static void RatingGradient(double rating, out int topRgb, out int bottomRgb)
    {
        if (double.IsNaN(rating) || rating < 10.0)
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
        if (i < 0) i = 0;
        if (i > 4) i = 4;
        topRgb = Win32Overlay.RATING_COLOR_STOPS[i];
        bottomRgb = Win32Overlay.RATING_COLOR_STOPS[i + 1];
    }

    static IntPtr FitFontSz(IntPtr g, string text, IntPtr family, int style, float baseSize,
        int maxW, IntPtr baseFont, out float usedSize)
    {
        usedSize = baseSize;
        if (baseFont == IntPtr.Zero || string.IsNullOrEmpty(text)) return baseFont;

        var layout = new GPRECTF { X = 0, Y = 0, Width = 10000f, Height = 10000f };
        if (GdipMeasureString(g, text, -1, baseFont, ref layout, IntPtr.Zero, out var box, out _, out _) != 0)
            return baseFont;
        if (box.Width <= maxW || box.Width <= 0f) return baseFont;

        usedSize = baseSize * maxW / box.Width;
        if (GdipCreateFont(family, usedSize, style, UnitPixel, out var f) != 0) { usedSize = baseSize; return baseFont; }
        return f;
    }

    static void RowTextL(IntPtr g, string text, IntPtr font, IntPtr brush, int x, float cy)
    {
        if (font == IntPtr.Zero || brush == IntPtr.Zero || string.IsNullOrEmpty(text)) return;
        var r = new GPRECTF { X = x, Y = cy - 100f, Width = 1000f, Height = 200f };
        GdipDrawString(g, text, -1, font, ref r, _fmtLeft, brush);
    }

    static void RowTextC(IntPtr g, string text, IntPtr font, IntPtr brush, int cx, int cy)
    {
        if (font == IntPtr.Zero || brush == IntPtr.Zero || string.IsNullOrEmpty(text)) return;
        var r = new GPRECTF { X = cx - 500f, Y = cy - 100f, Width = 1000f, Height = 200f };
        GdipDrawString(g, text, -1, font, ref r, _fmtMid, brush);
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

        static void FillPathAA(IntPtr g, IntPtr brush, IntPtr path)
    {
        if (path == IntPtr.Zero) return;
        if (brush != IntPtr.Zero)
        {
            GdipSetSmoothingMode(g, 4 );
            GdipFillPath(g, brush, path);
            GdipSetSmoothingMode(g, 3 );
        }
        GdipDeletePath(path);
    }

        static void FillRoundRect(IntPtr g, IntPtr brush, int x, int y, int w, int h, int r)
    {
        if (brush == IntPtr.Zero) return;
        var path = RoundRectPath(x, y, w, h, r);
        if (path == IntPtr.Zero) { GdipFillRectangleI(g, brush, x, y, w, h); return; }
        FillPathAA(g, brush, path);
    }

        static IntPtr ScaleTo(IntPtr src, int sx, int sy, int sw, int sh, int dw, int dh)
    {
        if (src == IntPtr.Zero || sw <= 0 || sh <= 0 || dw <= 0 || dh <= 0) return IntPtr.Zero;
        if (GdipCreateBitmapFromScan0(dw, dh, 0, PixelFormat32bppARGB, IntPtr.Zero, out var dst) != 0
            || dst == IntPtr.Zero) return IntPtr.Zero;
        if (GdipGetImageGraphicsContext(dst, out var dstG) != 0 || dstG == IntPtr.Zero)
        { GdipDisposeImage(dst); return IntPtr.Zero; }

        GdipSetInterpolationMode(dstG, dw < sw ? 6 : 7);
        GdipDrawImageRectRectI(dstG, src, 0, 0, dw, dh, sx, sy, sw, sh,
            UnitPixel, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        GdipDeleteGraphics(dstG);
        return dst;
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
}
