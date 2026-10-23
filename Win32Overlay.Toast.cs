using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using BepInEx;

namespace InFalsusRating;

public static partial class Win32Overlay
{

    public static class Toast
    {
        const int SW_HIDE = 0;
        const int NoWrap = 0x00001000;

        const int TOAST_W = 460;
        const int TOAST_H = 96;
                const float TOAST_Y_RATIO = 0.5f;

        const int FADE_IN_MS  = 200;
        const int HOLD_MS     = 2600;
        const int FADE_OUT_MS = 300;
        const int GAP_MS      = 250;
        const int DRIFT_PX    = 16;

        const int MAX_QUEUE = 6;

        static readonly int C_BG     = unchecked((int)0xE6141720);
        static readonly int C_BORDER = unchecked((int)0xFF3A3F52);
        static readonly int C_GOLD   = unchecked((int)0xFFF0C24B);
        static readonly int C_NAME   = unchecked((int)0xFFFFFFFF);
        static readonly int C_DESC   = unchecked((int)0xFFB9C2D4);

        sealed class Item
        {
            public string Name = "";
            public string Desc = "";
                        public string Tag = "成就解锁";
                        public int Accent = 0;
        }

        enum Phase { Idle, In, Hold, Out, Gap }

        static Thread _thread = null;
        static readonly object _lock = new object();
        static readonly Queue<Item> _queue = new Queue<Item>();

        static Item _cur = null;
        static Phase _phase = Phase.Idle;
        static long _phaseAt = 0;

        static bool _lbtnWas = false;
                static float _outFrom = 1f;

        static IntPtr _hWnd = IntPtr.Zero;
        static IntPtr _dc = IntPtr.Zero, _dib = IntPtr.Zero, _bits = IntPtr.Zero, _dibOld = IntPtr.Zero;
        static IntPtr _token = IntPtr.Zero;
        static int _screenX = 0, _screenY = 0;
        static bool _windowShown = false;
        static IntPtr _game = IntPtr.Zero;

        static WndProcDelegate _tsWndProc;

        static IntPtr _cjkColl = IntPtr.Zero, _fam = IntPtr.Zero;
        static IntPtr _fTag, _fName, _fDesc, _fFoot;
        static IntPtr _fmtNear;
        static readonly Dictionary<int, IntPtr> _brushes = new Dictionary<int, IntPtr>();

                public static void Queue(string name, string desc) => Queue(name, desc, null, 0);

                public static void Queue(string name, string desc, string tag) => Queue(name, desc, tag, 0);

                public static void Queue(string name, string desc, string tag, int accent)
        {
            if (string.IsNullOrEmpty(name)) return;

            if (!Settings.ToastOn) return;
            lock (_lock)
            {
                if (_queue.Count >= MAX_QUEUE)
                {
                    var drop = _queue.Dequeue();
                    Plugin.Logger.LogWarning($"[弹条] 队列满了（>{MAX_QUEUE}），丢掉最早的一条「{drop.Name}」");
                }
                _queue.Enqueue(new Item
                {
                    Name = name,
                    Desc = desc ?? "",
                    Tag = string.IsNullOrEmpty(tag) ? "成就解锁" : tag,
                    Accent = accent,
                });

                EnsureStarted();
            }
        }

        static void EnsureStarted()
        {
            if (_thread != null) return;
            _thread = new Thread(() =>
            {
                try { RunToast(); }
                catch (Exception ex) { Plugin.Logger.LogError($"[弹条] {ex}"); }
            });
            _thread.IsBackground = true;
#pragma warning disable CA1416
            _thread.SetApartmentState(ApartmentState.STA);
#pragma warning restore CA1416
            _thread.Start();
        }

        static void RunToast()
        {
            var input = new GdiplusStartupInput { GdiplusVersion = 1 };
            if (GdiplusStartup(out _token, ref input, IntPtr.Zero) != 0)
            {
                Plugin.Logger.LogError("[弹条] GdiplusStartup 失败");
                return;
            }

            SetupFonts();

            _tsWndProc = WndProcImpl;
            var wc = new WNDCLASS
            {
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_tsWndProc),
                lpszClassName = "InFalsusRatingToast"
            };
            RegisterClassW(ref wc);

            uint exStyle = WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            _hWnd = CreateWindowExW(exStyle, "InFalsusRatingToast", "Toast", WS_POPUP,
                0, 0, TOAST_W, TOAST_H, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (_hWnd == IntPtr.Zero) { Plugin.Logger.LogError("[弹条] CreateWindowEx 失败"); return; }

            _game = FindGameWindow();
            if (_game != IntPtr.Zero) SetWindowLongPtrW(_hWnd, GWLP_HWNDPARENT, _game);

            Plugin.Logger.LogInfo("[弹条] 就绪（有解锁就往屏幕中间飘一条）");

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
            long now = Environment.TickCount64;

            bool justStarted = false;
            if (_phase == Phase.Idle)
            {
                Item next = null;
                lock (_lock) { if (_queue.Count > 0) next = _queue.Dequeue(); }
                if (next != null) { _cur = next; _phase = Phase.In; _phaseAt = now; justStarted = true; }
            }

            bool lbtn = (GetAsyncKeyState(0x01) & 0x8000) != 0;
            bool clickEdge = lbtn && !_lbtnWas;
            _lbtnWas = lbtn;
            if (clickEdge && !justStarted && _cur != null
                && (_phase == Phase.In || _phase == Phase.Hold))
            {
                float at = _phase == Phase.In ? (now - _phaseAt) / (float)FADE_IN_MS : 1f;
                if (at < 0f) at = 0f;
                if (at > 1f) at = 1f;
                _outFrom = at;
                _phase = Phase.Out;
                _phaseAt = now;
            }

            float alpha = 0f;
            int dy = 0;
            switch (_phase)
            {
                case Phase.In:
                {
                    float t = (now - _phaseAt) / (float)FADE_IN_MS;
                    if (t >= 1f) { alpha = 1f; _phase = Phase.Hold; _phaseAt = now; }
                    else { alpha = t; dy = (int)((1f - t) * DRIFT_PX); }
                    break;
                }
                case Phase.Hold:
                    alpha = 1f;

                    int hold = Settings.ToastHoldMs;
                    if (hold < 600) hold = HOLD_MS;
                    if (now - _phaseAt >= hold) { _outFrom = 1f; _phase = Phase.Out; _phaseAt = now; }
                    break;
                case Phase.Out:
                {
                    float t = (now - _phaseAt) / (float)FADE_OUT_MS;
                    if (t >= 1f) { alpha = 0f; _cur = null; _phase = Phase.Gap; _phaseAt = now; }
                    else alpha = _outFrom * (1f - t);
                    break;
                }
                case Phase.Gap:
                    if (now - _phaseAt >= GAP_MS) _phase = Phase.Idle;
                    break;
            }

            bool visible = _cur != null && alpha > 0.004f;
            if (_hWnd != IntPtr.Zero)
            {
                if (visible && !_windowShown) { ShowWindow(_hWnd, SW_SHOWNOACTIVATE); _windowShown = true; }
                else if (!visible && _windowShown) { ShowWindow(_hWnd, SW_HIDE); _windowShown = false; }
            }
            if (!visible) return;

            TrackGameWindow();
            _screenY += dy;

            EnsureDib(TOAST_W, TOAST_H);
            Render(TOAST_W, TOAST_H);
            Commit(alpha);
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
            _screenX = pt.x + Math.Max(0, (cw - TOAST_W) / 2);
            _screenY = pt.y + Math.Max(0, (int)((ch - TOAST_H) * TOAST_Y_RATIO));
        }

        static void SetupFonts()
        {

            string cjkPath = Plugin.AssetPath("font3.ttf");
            if (File.Exists(cjkPath) && GdipNewPrivateFontCollection(out _cjkColl) == 0)
            {
                if (GdipPrivateAddFontFile(_cjkColl, cjkPath) != 0)
                    Plugin.Logger.LogWarning($"[弹条] 中文字体加载失败: {cjkPath}");
                if (GdipCreateFontFamilyFromName("Alimama ShuHeiTi", _cjkColl, out _fam) != 0)
                    GdipCreateFontFamilyFromName("阿里妈妈数黑体", _cjkColl, out _fam);
            }
            if (_fam == IntPtr.Zero &&
                GdipCreateFontFamilyFromName("Microsoft YaHei", IntPtr.Zero, out _fam) != 0)
                GdipCreateFontFamilyFromName("Segoe UI", IntPtr.Zero, out _fam);

            IntPtr fam = _fam;
            GdipCreateFont(fam, 15f, 0 , UnitPixel, out _fTag);
            GdipCreateFont(fam, 24f, FontStyleBold, UnitPixel, out _fName);
            GdipCreateFont(fam, 16f, 0 , UnitPixel, out _fDesc);
            GdipCreateFont(fam, 14f, 0 , UnitPixel, out _fFoot);

            GdipCreateStringFormat(NoWrap, 0, out _fmtNear);
            GdipSetStringFormatAlign(_fmtNear, StringAlignmentNear);
            GdipSetStringFormatLineAlign(_fmtNear, StringAlignmentLineCenter);
        }

        static IntPtr Br(int argb)
        {
            if (_brushes.TryGetValue(argb, out var b) && b != IntPtr.Zero) return b;
            IntPtr nb = IntPtr.Zero;
            if (GdipCreateSolidFill(argb, out nb) != 0) nb = IntPtr.Zero;
            _brushes[argb] = nb;
            return nb;
        }

        static readonly string _guiDir = Plugin.AssetPath("GUI");
        static readonly Dictionary<string, IntPtr> _tex = new Dictionary<string, IntPtr>();

        static IntPtr Tex(string rel)
        {
            if (_tex.TryGetValue(rel, out var cached)) return cached;

            IntPtr bmp = IntPtr.Zero;
            string full = Path.Combine(_guiDir, rel);
            if (File.Exists(full) && GdipCreateBitmapFromFile(full, out bmp) != 0) bmp = IntPtr.Zero;

            _tex[rel] = bmp;
            return bmp;
        }

        static void EnsureDib(int w, int h)
        {
            if (_dib != IntPtr.Zero) return;

            if (_dc == IntPtr.Zero)
            {
                var sdc = GetDC(IntPtr.Zero);
                _dc = CreateCompatibleDC(sdc);
                ReleaseDC(IntPtr.Zero, sdc);
            }

            var bmi = new BITMAPINFO();
            bmi.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER));
            bmi.bmiHeader.biWidth = w;
            bmi.bmiHeader.biHeight = -h;
            bmi.bmiHeader.biPlanes = 1;
            bmi.bmiHeader.biBitCount = 32;
            bmi.bmiHeader.biCompression = 0;

            var sdc2 = GetDC(IntPtr.Zero);
            _dib = CreateDIBSection(sdc2, ref bmi, 0, out _bits, IntPtr.Zero, 0);
            ReleaseDC(IntPtr.Zero, sdc2);

            _dibOld = SelectObject(_dc, _dib);
        }

        static void Render(int w, int h)
        {
            IntPtr work = IntPtr.Zero;
            try
            {
                if (GdipCreateBitmapFromScan0(w, h, 0, PixelFormat32bppARGB, IntPtr.Zero, out work) != 0
                    || work == IntPtr.Zero) return;
                if (GdipGetImageGraphicsContext(work, out var gfx) != 0) return;

                GdipSetTextRenderingHint(gfx, TextRenderingHintAntiAlias);
                GdipGraphicsClear(gfx, 0);

                DrawToast(gfx, w, h);

                GdipDeleteGraphics(gfx);

                var all = new GPRECT { X = 0, Y = 0, Width = w, Height = h };
                if (GdipBitmapLockBits(work, ref all, ImageLockModeRead, PixelFormat32bppARGB, out var bd) == 0)
                {
                    unsafe
                    {
                        byte* src = (byte*)bd.Scan0;
                        byte* dst = (byte*)_bits;
                        int total = w * h;
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
            }
            finally
            {
                if (work != IntPtr.Zero) GdipDisposeImage(work);
            }
        }

        static void DrawToast(IntPtr gfx, int w, int h)
        {
            var it = _cur;
            if (it == null) return;

            IntPtr plate = Tex("toast.png");
            if (plate != IntPtr.Zero)
            {
                GdipDrawImageRectI(gfx, plate, 0, 0, w, h);
            }
            else
            {
                IntPtr brBg = Br(C_BG);
                if (brBg == IntPtr.Zero) return;
                GdipFillRectangleI(gfx, brBg, 0, 0, w, h);
            }

            IntPtr brB = Br(C_BORDER);
            if (brB != IntPtr.Zero)
            {
                GdipFillRectangleI(gfx, brB, 0, 0, w, 1);
                GdipFillRectangleI(gfx, brB, 0, h - 1, w, 1);
                GdipFillRectangleI(gfx, brB, 0, 1, 1, h - 2);
                GdipFillRectangleI(gfx, brB, w - 1, 1, 1, h - 2);
            }

            int accent = it.Accent != 0 ? it.Accent : C_GOLD;

            IntPtr brG = Br(accent);
            if (brG != IntPtr.Zero) GdipFillRectangleI(gfx, brG, 18, 18, 5, h - 36);

            int tx = 40, tw = w - tx - 18;
            string tag = string.IsNullOrEmpty(it.Tag) ? "成就解锁" : it.Tag;
            bool hasDesc = !string.IsNullOrEmpty(it.Desc);
            if (hasDesc)
            {
                Txt(gfx, tag, _fTag, tx, 12, tw, 20, accent);
                Txt(gfx, Fit(gfx, it.Name, _fName, tw), _fName, tx, 32, tw, 30, C_NAME);
                Txt(gfx, Fit(gfx, it.Desc, _fDesc, tw), _fDesc, tx, 64, tw, 22, C_DESC);
            }
            else
            {

                Txt(gfx, tag, _fTag, tx, 28, tw, 20, accent);
                Txt(gfx, Fit(gfx, it.Name, _fName, tw), _fName, tx, 48, tw, 30, C_NAME);
            }
        }

        static void Txt(IntPtr gfx, string text, IntPtr font, int x, int y, int w, int h, int color)
        {
            if (string.IsNullOrEmpty(text) || font == IntPtr.Zero) return;
            var r = new GPRECTF { X = x, Y = y, Width = w, Height = h };
            GdipDrawString(gfx, text, -1, font, ref r, _fmtNear, Br(color));
        }

        static string Fit(IntPtr gfx, string s, IntPtr font, float width)
        {
            if (string.IsNullOrEmpty(s)) return "";
            try
            {
                var r = new GPRECTF { X = 0, Y = 0, Width = width, Height = TOAST_H };
                if (GdipMeasureString(gfx, s, -1, font, ref r, _fmtNear, out _, out int fitted, out _) != 0)
                    return s;
                if (fitted <= 0 || fitted >= s.Length) return s;
                return s.Substring(0, Math.Max(1, fitted - 1)) + "…";
            }
            catch { return s; }
        }

        static void Commit(float alpha)
        {
            if (_hWnd == IntPtr.Zero) return;

            var ptDst = new POINT { x = _screenX, y = _screenY };
            var sz = new SIZE { cx = TOAST_W, cy = TOAST_H };
            var ptSrc = new POINT { x = 0, y = 0 };
            var blend = new BLENDFUNCTION
            {
                BlendOp = 0,
                BlendFlags = 0,
                SourceConstantAlpha = (byte)Math.Clamp((int)(alpha * 255f), 0, 255),
                AlphaFormat = 1
            };
            UpdateLayeredWindow(_hWnd, IntPtr.Zero, ref ptDst, ref sz, _dc, ref ptSrc, 0, ref blend, ULW_ALPHA);
        }

        static IntPtr WndProcImpl(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_DESTROY)
            {
                foreach (var b in _brushes.Values)
                    if (b != IntPtr.Zero) GdipDeleteBrush(b);
                _brushes.Clear();

                IntPtr[] fonts = { _fTag, _fName, _fDesc, _fFoot };
                foreach (var f in fonts)
                    if (f != IntPtr.Zero) GdipDeleteFont(f);
                _fTag = _fName = _fDesc = _fFoot = IntPtr.Zero;

                if (_cjkColl != IntPtr.Zero) GdipDeletePrivateFontCollection(ref _cjkColl);
                _fam = IntPtr.Zero;
                if (_dibOld != IntPtr.Zero && _dc != IntPtr.Zero) SelectObject(_dc, _dibOld);
                if (_dib != IntPtr.Zero) DeleteObject(_dib);
                if (_dc != IntPtr.Zero) DeleteDC(_dc);
                PostQuitMessage(0);
                return IntPtr.Zero;
            }
            return DefWindowProcW(hWnd, msg, wParam, lParam);
        }
    }
}
