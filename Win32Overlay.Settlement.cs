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

    public static class SettlementPanel
    {
        const int SW_HIDE = 0;
        const int AlignFar = 2;

        const int NoWrap = 0x00001000;

        const int PANEL_W  = 860;
        const int PAD      = 30;
        const int HEADER_H = 92;
        const int SUM_H    = 132;
        const int THEAD_H  = 34;
        const int ROW_H    = 32;
        const int FOOT_H   = 46;
        const int MAX_ROWS = 16;

        const int COL_NUM_X   = PAD,  COL_NUM_W   = 40;
        const int COL_TITLE_X = 78,   COL_TITLE_W = 330;
        const int COL_DIFF_X  = 416,  COL_DIFF_W  = 80;
        const int COL_SCORE_X = 504,  COL_SCORE_W = 130;

        const int COL_CLEAR_X = 642,  COL_CLEAR_W = 188;

        const int CLOSE_W = 26, CLOSE_H = 26;
        const int CLOSE_X = PANEL_W - PAD - CLOSE_W;
        const int CLOSE_Y = 23;

        const int VK_LBUTTON = 0x01;

        const float FADE_LERP = 0.18f;

        static readonly int C_BG     = unchecked((int)0xF2161920);
        static readonly int C_BORDER = unchecked((int)0xFF39415A);
        static readonly int C_LINE   = unchecked((int)0x3FFFFFFF);
        static readonly int C_TITLE  = unchecked((int)0xFFFFFFFF);
        static readonly int C_LABEL  = unchecked((int)0xFF9AA3B8);
        static readonly int C_VALUE  = unchecked((int)0xFFEDF1F7);
        static readonly int C_THEAD  = unchecked((int)0xFF7C869C);
        static readonly int C_ALT    = unchecked((int)0x0AFFFFFF);
        static readonly int C_OK     = unchecked((int)0xFF6BE39B);
        static readonly int C_BAD    = unchecked((int)0xFFFF7B7B);
                static readonly int C_ACCENT = unchecked((int)0xFF7FB2E8);
        static readonly int C_FOOT   = unchecked((int)0xFF6E788C);
                static readonly int C_HOVER  = unchecked((int)0x1CFFFFFF);
                static readonly int C_CURROW = unchecked((int)0x12FFFFFF);

        static Thread _thread = null;
        static readonly object _lock = new object();
        static SettlementData _pending = null;
        static SettlementData _data = null;

                public static volatile bool IsShowing = false;

        static IntPtr _hWnd = IntPtr.Zero;
        static IntPtr _dc = IntPtr.Zero, _dib = IntPtr.Zero, _bits = IntPtr.Zero, _dibOld = IntPtr.Zero;
        static IntPtr _token = IntPtr.Zero;
        static int _dibW = 0, _dibH = 0;
        static int _panelH = 500;
        static int _screenX = 0, _screenY = 0;
        static float _alpha = 0f;
        static bool _windowShown = false;
        static IntPtr _game = IntPtr.Zero;

                static bool _drawn = false;
        static int _drawnAlpha = -1, _drawnX = int.MinValue, _drawnY = int.MinValue, _drawnH = 0;
        static IntPtr _drawnGame = IntPtr.Zero;

        static readonly HashSet<int> _expanded = new HashSet<int>();
        static int _hoverRow = -1;
        static bool _hoverClose = false;
        static bool _lbtnWas = false;

        const int BTN_W = 136, BTN_H = 30, BTN_GAP = 10;
        const long ABORT_CONFIRM_MS = 3000;
        static bool _hoverAbort = false;
                static long _abortArmAt = 0;
                static volatile bool _swallowClick = false;
                static int _uiVer = 0;
        static int _drawnUi = -1;

        static readonly POINT[] _tri = new POINT[3];

        static WndProcDelegate _spWndProc;

        static IntPtr _cjkColl = IntPtr.Zero, _fam = IntPtr.Zero;
        static IntPtr _fTitle, _fRank, _fVerdict, _fLabel, _fValue, _fHead, _fRow, _fFoot;
        static IntPtr _fmtNear, _fmtCenter, _fmtFar;
        static readonly Dictionary<int, IntPtr> _brushes = new Dictionary<int, IntPtr>();

                public static void Show(SettlementData data)
        {
            if (data == null) return;
            lock (_lock) { _pending = data; }
            EnsureStarted();
            IsShowing = true;
            _swallowClick = true;
        }

                public static void PushLive(SettlementData data)
        {
            if (data == null || !data.Live) return;
            lock (_lock) { _pending = data; }
        }

                public static void ShowLive()
        {
            EnsureStarted();
            IsShowing = true;
            _swallowClick = true;
        }

        public static void Hide() { IsShowing = false; }

        static void EnsureStarted()
        {
            if (_thread != null) return;
            _thread = new Thread(() =>
            {
                try { RunPanel(); }
                catch (Exception ex) { Plugin.Logger.LogError($"[结算] {ex}"); }
            });
            _thread.IsBackground = true;
#pragma warning disable CA1416
            _thread.SetApartmentState(ApartmentState.STA);
#pragma warning restore CA1416
            _thread.Start();
        }

        static void RunPanel()
        {
            var input = new GdiplusStartupInput { GdiplusVersion = 1 };
            if (GdiplusStartup(out _token, ref input, IntPtr.Zero) != 0)
            {
                Plugin.Logger.LogError("[结算] GdiplusStartup 失败");
                return;
            }

            SetupFonts();

            _spWndProc = WndProcImpl;
            var wc = new WNDCLASS
            {
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_spWndProc),
                lpszClassName = "InFalsusRatingSettlement"
            };
            RegisterClassW(ref wc);

            uint exStyle = WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            _hWnd = CreateWindowExW(exStyle, "InFalsusRatingSettlement", "Settlement", WS_POPUP,
                0, 0, PANEL_W, _panelH, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (_hWnd == IntPtr.Zero) { Plugin.Logger.LogError("[结算] CreateWindowEx 失败"); return; }

            _game = FindGameWindow();
            if (_game != IntPtr.Zero) SetWindowLongPtrW(_hWnd, GWLP_HWNDPARENT, _game);

            Plugin.Logger.LogInfo("[结算] 结算面板就绪（数据推入即显示）");

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

            SettlementData d;
            lock (_lock) { d = _pending; _pending = null; }
            if (d != null)
            {

                bool same = d.Live && _data != null && _data.Live && SameLive(_data, d);
                if (!same)
                {

                    bool reset = _data == null || _data.Live != d.Live || _data.Title != d.Title;
                    _data = d;
                    if (reset)
                    {

                        _expanded.Clear();
                        int n = Math.Min(d.Rows.Count, MAX_ROWS);
                        for (int i = 0; i < n; i++)
                            if (d.Rows[i] != null && d.Rows[i].LiveStats) _expanded.Add(i);
                        _hoverRow = -1; _hoverClose = false;
                        _hoverAbort = false;
                        _abortArmAt = 0;
                    }
                    _uiVer++;
                    Layout();
                }
            }

            if (_swallowClick) { _swallowClick = false; _lbtnWas = Key(VK_LBUTTON); }

            float target = IsShowing ? 1f : 0f;
            _alpha += (target - _alpha) * FADE_LERP;
            if (Math.Abs(target - _alpha) < 0.01f) _alpha = target;

            bool visible = _alpha > 0.002f;
            if (_hWnd != IntPtr.Zero)
            {
                if (visible && !_windowShown) { ShowWindow(_hWnd, SW_SHOWNOACTIVATE); _windowShown = true; }
                else if (!visible && _windowShown) { ShowWindow(_hWnd, SW_HIDE); _windowShown = false; }
            }
            if (!visible || _data == null) return;

            TrackGameWindow();
            UpdateMouse();
            EnsureDib(PANEL_W, _panelH);

            int ab = (int)(_alpha * 255f + 0.5f);
            if (ab < 0) ab = 0; else if (ab > 255) ab = 255;

            if (!_drawn
                || ab != _drawnAlpha || _screenX != _drawnX || _screenY != _drawnY
                || _panelH != _drawnH || _game != _drawnGame || _uiVer != _drawnUi)
            {
                Render(PANEL_W, _panelH);
                Commit();
                _drawn = true; _drawnAlpha = ab;
                _drawnX = _screenX; _drawnY = _screenY;
                _drawnH = _panelH; _drawnGame = _game;
                _drawnUi = _uiVer;
            }
        }

        static bool Key(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

                static int HitRow(int cx, int cy)
        {
            if (_data == null) return -1;
            int top = HEADER_H + SUM_H + THEAD_H;
            if (cx < PAD || cx >= PANEL_W - PAD || cy < top) return -1;

            int n = Math.Min(_data.Rows.Count, MAX_ROWS);
            int ry = top;
            for (int i = 0; i < n; i++)
            {
                int rh = RowH(i);
                if (cy >= ry && cy < ry + rh) return i;
                ry += rh;
            }
            return -1;
        }

        static bool HitClose(int cx, int cy)
            => cx >= CLOSE_X && cx < CLOSE_X + CLOSE_W
            && cy >= CLOSE_Y && cy < CLOSE_Y + CLOSE_H;

        static int FootBtnY() => _panelH - FOOT_H + (FOOT_H - BTN_H) / 2;
        static bool LiveButtons => _data != null && _data.Live && !_data.Ended;

        static bool HitAbort(int cx, int cy)
            => LiveButtons
            && cx >= PAD && cx < PAD + BTN_W
            && cy >= FootBtnY() && cy < FootBtnY() + BTN_H;

        static void UpdateMouse()
        {

            if (!IsShowing || _alpha <= 0.5f) { ResetMouse(); return; }

            POINT mp;
            if (!GetCursorPos(out mp)) { ResetMouse(); return; }
            int cx = mp.x - _screenX;
            int cy = mp.y - _screenY;

            bool inWin = cx >= 0 && cx < PANEL_W && cy >= 0 && cy < _panelH;
            bool hc = inWin && HitClose(cx, cy);
            bool ha = inWin && !hc && HitAbort(cx, cy);
            int hr = (inWin && !hc && !ha) ? HitRow(cx, cy) : -1;

            if (hc != _hoverClose || hr != _hoverRow || ha != _hoverAbort)
            {
                _hoverClose = hc;
                _hoverRow = hr;
                _hoverAbort = ha;
                _uiVer++;
            }

            if (_abortArmAt > 0 && Environment.TickCount64 - _abortArmAt > ABORT_CONFIRM_MS)
            {
                _abortArmAt = 0;
                _uiVer++;
            }

            bool down = Key(VK_LBUTTON);
            bool click = down && !_lbtnWas;
            _lbtnWas = down;
            if (!click) return;

            if (hc)
            {
                Plugin.Logger.LogInfo("[结算] 点关闭按钮 → 收起");
                Hide();
                return;
            }
            if (ha)
            {
                long now = Environment.TickCount64;
                if (_abortArmAt > 0 && now - _abortArmAt <= ABORT_CONFIRM_MS)
                {
                    _abortArmAt = 0;
                    _uiVer++;
                    Plugin.Logger.LogWarning("[Exam] 课题面板点了「中止课题」（二次确认）→ 这一场按没过收场");
                    TaskRunner.RequestAbort();
                }
                else
                {
                    _abortArmAt = now;
                    _uiVer++;
                    Plugin.Logger.LogInfo("[Exam] 课题面板点了「中止课题」→ 再点一次确认");
                }
                return;
            }
            if (hr >= 0)
            {
                if (!_expanded.Add(hr)) _expanded.Remove(hr);

                _hoverRow = -1;
                _uiVer++;
                Layout();
                EnsureDib(PANEL_W, _panelH);
            }
        }

                static void ResetMouse()
        {
            if (_hoverRow != -1 || _hoverClose || _hoverAbort || _abortArmAt != 0)
            {
                _hoverRow = -1; _hoverClose = false;
                _hoverAbort = false;
                _abortArmAt = 0;
                _uiVer++;
            }
            _lbtnWas = Key(VK_LBUTTON);
        }

                const int ROW_DET = 22;

        const int DET_LINE     = 18;
        const int DET_TOP      = 6;
        const int DET_CELL_W   = 70;
        const int DET_LABEL_W  = 76;
                const int DET_H        = DET_TOP + DET_LINE * 6 + DET_TOP;
                const int DET_LIVE_H   = 48;
        const int DET_SUM_A    = 590;
        const int DET_SUM_B    = 710;
        const int DET_SUM_AW   = 116;
        const int DET_SUM_BW   = 120;
        const int DET_KV_LABEL = 54;

                static readonly int[] DET_COL_R = { 244, 324, 404, 484, 564 };
        static readonly string[] DET_HEAD = { "ALL", "TAP", "HOLD", "FIELD", "FLICK" };
        static readonly string[] DET_ROWS = { "EXACT+", "EXACT", "NEAR", "BREAK", "TOTAL" };

                static int DetailH(SettlementRow r)
        {
            if (r == null) return ROW_DET;
            if (r.Detail != null) return DET_H;
            bool hasAgg = r.Shiny >= 0 && r.Perfect >= 0 && r.Near >= 0 && r.Miss >= 0;
            return r.LiveStats && hasAgg ? DET_LIVE_H : ROW_DET;
        }

                static int RowH(int i)
        {
            if (_data == null || i < 0 || i >= _data.Rows.Count) return ROW_H;
            return ROW_H + (_expanded.Contains(i) ? DetailH(_data.Rows[i]) : 0);
        }

        static void Layout()
        {
            int n = _data != null ? _data.Rows.Count : 0;
            int shown = Math.Min(n, MAX_ROWS);
            int rows = 0;
            for (int i = 0; i < shown; i++) rows += RowH(i);
            int more = n > MAX_ROWS ? ROW_H : 0;
            _panelH = HEADER_H + SUM_H + THEAD_H + rows + more + FOOT_H;
        }

                static bool SameLive(SettlementData a, SettlementData b)
        {
            if (a.Live != b.Live || a.Ended != b.Ended || a.Final != b.Final) return false;
            if (a.Title != b.Title || a.Phase != b.Phase || a.EndText != b.EndText) return false;
            if (a.TotalSongs != b.TotalSongs || a.DoneSongs != b.DoneSongs) return false;
            if (a.CurrentIndex != b.CurrentIndex || a.CurrentRow != b.CurrentRow) return false;
            if (a.Hp != b.Hp || a.HpMax != b.HpMax || a.HpPass != b.HpPass) return false;
            if (a.HpMin != b.HpMin || a.HpNotch != b.HpNotch || a.HpNoLoss != b.HpNoLoss) return false;
            if (a.LiveShiny != b.LiveShiny || a.LiveExact != b.LiveExact
                || a.LiveNear != b.LiveNear || a.LiveBreak != b.LiveBreak) return false;
            if (a.LiveJudgesOk != b.LiveJudgesOk) return false;
            if (a.LiveCombo != b.LiveCombo || a.LiveComboMax != b.LiveComboMax) return false;

            if (a.Rows == null || b.Rows == null) return ReferenceEquals(a.Rows, b.Rows);
            if (a.Rows.Count != b.Rows.Count) return false;
            for (int i = 0; i < a.Rows.Count; i++)
            {
                var x = a.Rows[i];
                var y = b.Rows[i];
                if (ReferenceEquals(x, y)) continue;
                if (x == null || y == null) return false;
                if (x.Title != y.Title || x.Difficulty != y.Difficulty) return false;
                if (x.Const != y.Const || x.Score != y.Score || x.Dive != y.Dive) return false;
                if (x.Clear != y.Clear || x.Failed != y.Failed) return false;
                if (x.Miss != y.Miss || x.Near != y.Near) return false;
                if (x.Perfect != y.Perfect || x.Shiny != y.Shiny) return false;
                if (x.Combo != y.Combo || x.LiveStats != y.LiveStats) return false;

                if (!ReferenceEquals(x.Detail, y.Detail)) return false;
            }
            return true;
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

            _screenX = pt.x + Math.Max(0, (rc.right - rc.left - PANEL_W) / 2);
            _screenY = pt.y + Math.Max(0, (rc.bottom - rc.top - _panelH) / 2);
        }

        static void SetupFonts()
        {

            string cjkPath = Plugin.AssetPath("font3.ttf");
            if (File.Exists(cjkPath) && GdipNewPrivateFontCollection(out _cjkColl) == 0)
            {
                if (GdipPrivateAddFontFile(_cjkColl, cjkPath) != 0)
                    Plugin.Logger.LogWarning($"[结算] 中文字体加载失败: {cjkPath}");
                if (GdipCreateFontFamilyFromName("Alimama ShuHeiTi", _cjkColl, out _fam) != 0)
                    GdipCreateFontFamilyFromName("阿里妈妈数黑体", _cjkColl, out _fam);
            }

            if (_fam == IntPtr.Zero &&
                GdipCreateFontFamilyFromName("Microsoft YaHei", IntPtr.Zero, out _fam) != 0)
                GdipCreateFontFamilyFromName("Segoe UI", IntPtr.Zero, out _fam);

            IntPtr fam = _fam;
            GdipCreateFont(fam, 26f, FontStyleBold, UnitPixel, out _fTitle);
            GdipCreateFont(fam, 46f, FontStyleBold, UnitPixel, out _fRank);
            GdipCreateFont(fam, 24f, FontStyleBold, UnitPixel, out _fVerdict);
            GdipCreateFont(fam, 18f, 0 , UnitPixel, out _fLabel);
            GdipCreateFont(fam, 22f, FontStyleBold, UnitPixel, out _fValue);
            GdipCreateFont(fam, 17f, 0 , UnitPixel, out _fHead);
            GdipCreateFont(fam, 18f, 0 , UnitPixel, out _fRow);
            GdipCreateFont(fam, 16f, 0 , UnitPixel, out _fFoot);

            GdipCreateStringFormat(NoWrap, 0, out _fmtNear);
            GdipSetStringFormatAlign(_fmtNear, StringAlignmentNear);
            GdipSetStringFormatLineAlign(_fmtNear, StringAlignmentLineCenter);

            GdipCreateStringFormat(NoWrap, 0, out _fmtCenter);
            GdipSetStringFormatAlign(_fmtCenter, StringAlignmentCenter);
            GdipSetStringFormatLineAlign(_fmtCenter, StringAlignmentLineCenter);

            GdipCreateStringFormat(NoWrap, 0, out _fmtFar);
            GdipSetStringFormatAlign(_fmtFar, AlignFar);
            GdipSetStringFormatLineAlign(_fmtFar, StringAlignmentLineCenter);

            Plugin.Logger.LogInfo($"[结算] 字体就绪（中文族句柄 {(fam != IntPtr.Zero ? "有" : "无")}）");
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

                static IntPtr NavPlateTex(bool fin)
        {
            if (fin)
            {
                IntPtr f = Tex("nav/nav_fin_normal.png");
                if (f != IntPtr.Zero) return f;
            }
            return Tex("nav/nav_normal.png");
        }

        static void EnsureDib(int w, int h)
        {
            if (_dib != IntPtr.Zero && _dibW == w && _dibH == h) return;

            if (_dc == IntPtr.Zero)
            {
                var sdc = GetDC(IntPtr.Zero);
                _dc = CreateCompatibleDC(sdc);
                ReleaseDC(IntPtr.Zero, sdc);
            }

            if (_dibOld != IntPtr.Zero) { SelectObject(_dc, _dibOld); _dibOld = IntPtr.Zero; }
            if (_dib != IntPtr.Zero) { DeleteObject(_dib); _dib = IntPtr.Zero; }

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
            _dibW = w; _dibH = h;
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

                DrawPanel(gfx, w, h);

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

        static void DrawPanel(IntPtr gfx, int w, int h)
        {
            var d = _data;
            if (d == null) return;

            IntPtr brBg = Br(C_BG);
            if (brBg == IntPtr.Zero) return;

            GdipFillRectangleI(gfx, brBg, 0, 0, w, h);

            IntPtr brB = Br(C_BORDER);
            GdipFillRectangleI(gfx, brB, 0, 0, w, 1);
            GdipFillRectangleI(gfx, brB, 0, h - 1, w, 1);
            GdipFillRectangleI(gfx, brB, 0, 1, 1, h - 2);
            GdipFillRectangleI(gfx, brB, w - 1, 1, 1, h - 2);

            const int PLATE_W = 196, PLATE_H = 44;
            string name = string.IsNullOrEmpty(d.Title) ? "课题" : d.Title;

            string suffix = d.Live ? (d.Ended ? " · 已中止" : " · 进行中") : " · 结算";
            IntPtr plate = NavPlateTex(d.Final);
            if (plate != IntPtr.Zero)
            {
                GdipDrawImageRectI(gfx, plate, PAD, 14, PLATE_W, PLATE_H);
                Txt(gfx, Fit(gfx, name, _fTitle, PLATE_W - 24), _fTitle,
                    PAD, 14, PLATE_W, PLATE_H, _fmtCenter, C_TITLE);
                Txt(gfx, suffix, _fLabel, PAD + PLATE_W + 12, 14, w - PAD * 2 - PLATE_W - 12, PLATE_H, _fmtNear,
                    d.Live ? (d.Ended ? C_BAD : C_ACCENT) : C_LABEL);
            }
            else
            {
                Txt(gfx, name + suffix, _fTitle, PAD, 24, w - PAD * 2, 40, _fmtNear, C_TITLE);
            }
            GdipFillRectangleI(gfx, Br(C_LINE), PAD, 74, w - PAD * 2, 1);

            {
                bool hov = _hoverClose;
                GdipFillRectangleI(gfx, Br(hov ? unchecked((int)0x3DFFFFFF) : unchecked((int)0x14FFFFFF)),
                    CLOSE_X, CLOSE_Y, CLOSE_W, CLOSE_H);
                int bc = hov ? C_TITLE : C_BORDER;
                GdipFillRectangleI(gfx, Br(bc), CLOSE_X, CLOSE_Y, CLOSE_W, 1);
                GdipFillRectangleI(gfx, Br(bc), CLOSE_X, CLOSE_Y + CLOSE_H - 1, CLOSE_W, 1);
                GdipFillRectangleI(gfx, Br(bc), CLOSE_X, CLOSE_Y + 1, 1, CLOSE_H - 2);
                GdipFillRectangleI(gfx, Br(bc), CLOSE_X + CLOSE_W - 1, CLOSE_Y + 1, 1, CLOSE_H - 2);
                Txt(gfx, "x", _fHead, CLOSE_X, CLOSE_Y, CLOSE_W, CLOSE_H, _fmtCenter, bc);
            }

            int y0 = HEADER_H;
            if (d.Live)
            {

                DrawLiveSummary(gfx, d, y0);
            }
            else
            {
                Txt(gfx, "档位", _fLabel, PAD, y0 + 8, 100, 46, _fmtNear, C_LABEL);

                Txt(gfx, string.IsNullOrEmpty(d.Rank) ? "—" : TaskProgress.GradeWord(d.Rank), _fRank,
                    PAD + 100, y0 - 4, 140, 60, _fmtNear, RankColor(d.Rank));
                Txt(gfx, d.Certified ? "通过" : "没过", _fVerdict,
                    PAD + 250, y0 + 12, 200, 40, _fmtNear, d.Certified ? C_OK : C_BAD);

                if (!string.IsNullOrEmpty(d.Verdict))
                    Txt(gfx, d.Verdict, _fLabel, PAD + 440, y0 + 18, w - PAD - 440, 28, _fmtNear,
                        d.Certified ? C_LABEL : C_BAD);

                int y1 = y0 + 62;
                Txt(gfx, "平均分", _fLabel, PAD, y1, 100, 26, _fmtNear, C_LABEL);
                Txt(gfx, d.PlayedSongs > 0 ? FmtScore(d.AverageScore) : "—", _fValue,
                    PAD + 100, y1 - 2, 220, 30, _fmtNear, C_VALUE);

                bool countsFull = d.TotalSongs > 0 && d.PlayedSongs >= d.TotalSongs;
                int y2 = y0 + 94;
                Txt(gfx, "FullLink", _fLabel, PAD, y2, 100, 26, _fmtNear, C_LABEL);
                Txt(gfx, countsFull ? $"{d.FullLinkCount} / {d.TotalSongs}" : d.FullLinkCount.ToString(),
                    _fValue, PAD + 100, y2 - 2, 120, 30, _fmtNear,
                    d.TotalSongs > 0 && d.FullLinkCount >= d.TotalSongs ? C_ACCENT : C_VALUE);
                Txt(gfx, "PerfectDive", _fLabel, PAD + 330, y2, 130, 26, _fmtNear, C_LABEL);
                Txt(gfx, countsFull ? $"{d.PerfectDiveCount} / {d.TotalSongs}" : d.PerfectDiveCount.ToString(),
                    _fValue, PAD + 460, y2 - 2, 120, 30, _fmtNear,
                    d.TotalSongs > 0 && d.PerfectDiveCount >= d.TotalSongs ? C_ACCENT : C_VALUE);
            }

            GdipFillRectangleI(gfx, Br(C_LINE), PAD, HEADER_H + SUM_H - 4, w - PAD * 2, 1);
            int th = HEADER_H + SUM_H + 4;
            Txt(gfx, "#",    _fHead, COL_NUM_X,   th, COL_NUM_W,   ROW_H - 4, _fmtCenter, C_THEAD);
            Txt(gfx, "曲目", _fHead, COL_TITLE_X, th, COL_TITLE_W, ROW_H - 4, _fmtNear,   C_THEAD);
            Txt(gfx, "难度", _fHead, COL_DIFF_X,  th, COL_DIFF_W,  ROW_H - 4, _fmtCenter, C_THEAD);
            Txt(gfx, "分数", _fHead, COL_SCORE_X, th, COL_SCORE_W, ROW_H - 4, _fmtFar,    C_THEAD);

            Txt(gfx, "判定", _fHead, COL_CLEAR_X, th, COL_CLEAR_W - 18, ROW_H - 4, _fmtCenter, C_THEAD);

            int shown = Math.Min(d.Rows.Count, MAX_ROWS);
            int ry = HEADER_H + SUM_H + THEAD_H;
            for (int i = 0; i < shown; i++)
            {
                var row = d.Rows[i];
                int rh = RowH(i);
                if ((i & 1) == 1) GdipFillRectangleI(gfx, Br(C_ALT), 1, ry, w - 2, rh);

                bool hov = i == _hoverRow;
                if (hov)
                {
                    GdipFillRectangleI(gfx, Br(C_HOVER), 1, ry, w - 2, rh);
                    GdipFillRectangleI(gfx, Br(C_ACCENT), 1, ry, 2, rh);
                }

                bool cur = i == d.CurrentRow;
                if (cur && !hov)
                {
                    GdipFillRectangleI(gfx, Br(C_CURROW), 1, ry, w - 2, rh);
                    GdipFillRectangleI(gfx, Br(C_ACCENT), 1, ry, 2, rh);
                }

                bool recorded = row.Score >= 0;

                Txt(gfx, (i + 1).ToString(), _fRow, COL_NUM_X, ry, COL_NUM_W, ROW_H, _fmtCenter,
                    cur ? C_ACCENT : (row.Failed ? C_BAD : C_LABEL));
                Txt(gfx, Fit(gfx, row.Title, _fRow, COL_TITLE_W), _fRow,
                    COL_TITLE_X, ry, COL_TITLE_W, ROW_H, _fmtNear,
                    row.Failed ? C_BAD : C_VALUE);

                double rconst = row.Const;
                string dtxt = rconst >= 0
                    ? TaskConfig.DiffShort(row.Difficulty) + " " + ConstTable.Text(rconst)
                    : TaskConfig.DiffShort(row.Difficulty);
                Txt(gfx, dtxt, _fRow, COL_DIFF_X, ry, COL_DIFF_W, ROW_H, _fmtCenter, C_LABEL);
                Txt(gfx, recorded ? FmtScore(row.Score) : "—", _fRow,
                    COL_SCORE_X, ry, COL_SCORE_W, ROW_H, _fmtFar, recorded ? C_VALUE : C_LABEL);
                Txt(gfx, Fit(gfx, ClearText(row.Clear), _fRow, COL_CLEAR_W - 18), _fRow,
                    COL_CLEAR_X, ry, COL_CLEAR_W - 18, ROW_H, _fmtCenter, ClearColor(row.Clear));

                {
                    int ax = PANEL_W - PAD - 14;
                    int cy = ry + ROW_H / 2;
                    if (_expanded.Contains(i))
                    {
                        _tri[0] = new POINT { x = ax - 1, y = cy - 4 };
                        _tri[1] = new POINT { x = ax + 9, y = cy - 4 };
                        _tri[2] = new POINT { x = ax + 4, y = cy + 4 };
                    }
                    else
                    {
                        _tri[0] = new POINT { x = ax + 1, y = cy - 5 };
                        _tri[1] = new POINT { x = ax + 1, y = cy + 5 };
                        _tri[2] = new POINT { x = ax + 8, y = cy };
                    }
                    GdipFillPolygonI(gfx, Br(hov || _expanded.Contains(i) ? C_VALUE : C_THEAD), _tri, 3, 1);
                }

                if (_expanded.Contains(i)) DrawRowDetail(gfx, row, ry + ROW_H);
                ry += rh;
            }
            if (d.Rows.Count > MAX_ROWS)
                Txt(gfx, $"… 还有 {d.Rows.Count - MAX_ROWS} 首未显示", _fHead, COL_TITLE_X,
                    ry, COL_TITLE_W, ROW_H, _fmtNear, C_LABEL);

            int footY = h - FOOT_H;
            GdipFillRectangleI(gfx, Br(C_LINE), PAD, footY, w - PAD * 2, 1);
            if (d.Live)
            {

                if (d.Ended)
                {
                    Txt(gfx, "课题已经中止 —— 点右上角 x 关闭", _fFoot,
                        PAD, footY, w - PAD * 2, FOOT_H, _fmtNear, C_BAD);
                }
                else
                {
                    int bx = PAD;
                    int by = FootBtnY();
                    bool arm = _abortArmAt > 0;
                    DrawFootButton(gfx, bx, by, arm ? "再点一次确认" : "中止课题",
                        _hoverAbort, arm);
                    bx += BTN_W + BTN_GAP;
                    Txt(gfx, "点一行展开判定明细 · 点右上角 x 关闭 · 按 F6 也可以", _fFoot,
                        bx, footY, w - PAD - bx, FOOT_H, _fmtNear, C_FOOT);
                }
            }
            else
            {
                Txt(gfx, "点一行展开判定明细 · 点右上角 x 关闭 · 按 F6 也可以", _fFoot,
                    0, footY + 6, w, FOOT_H - 12, _fmtCenter, C_FOOT);
            }
        }

                static void DrawRowDetail(IntPtr gfx, SettlementRow r, int y)
        {
            if (r == null) return;

            bool hasAgg = r.Shiny >= 0 && r.Perfect >= 0 && r.Near >= 0 && r.Miss >= 0;

            if (r.Detail != null) { DrawDetailBlock(gfx, r, y); return; }

            int x = COL_TITLE_X;
            int wid = COL_CLEAR_X + COL_CLEAR_W - COL_TITLE_X;

            if (!hasAgg && _data != null && _data.Live
                && _data.CurrentRow >= 0 && _data.CurrentRow < _data.Rows.Count
                && ReferenceEquals(_data.Rows[_data.CurrentRow], r))
            {
                Txt(gfx, "这一首还在打 —— 判定四档 / 连击看上面的概要", _fHead,
                    x, y, wid, ROW_DET, _fmtNear, C_ACCENT);
                return;
            }

            string s;
            if (!hasAgg)
            {
                s = "判定明细 · 没有读到这一首的判定数据";
            }
            else
            {

                s = $"{(r.LiveStats ? "实时记录" : "判定明细")}"
                  + $" · EXACT+ {r.Shiny} · EXACT {r.Perfect} · NEAR {r.Near} · BREAK {r.Miss}"
                  + $" · 最大连击 {(r.Combo < 0 ? "—" : r.Combo.ToString(CultureInfo.InvariantCulture))}";
            }
            Txt(gfx, Fit(gfx, s, _fHead, wid), _fHead, x, y, wid, ROW_DET, _fmtNear, C_LABEL);

            if (r.LiveStats && hasAgg)
                Txt(gfx, "这一首没写进游戏记录 —— 只有插件实时数下来的四档总数，没有分部位 / 早晚",
                    _fHead, x, y + ROW_DET, wid, ROW_DET, _fmtNear, C_THEAD);
        }

                static void DrawDetailBlock(IntPtr gfx, SettlementRow r, int y)
        {
            var d = r.Detail;
            int top = y + DET_TOP;

            Txt(gfx, "判定", _fHead, COL_TITLE_X, top, DET_LABEL_W, DET_LINE, _fmtNear, C_THEAD);
            for (int c = 0; c < DET_COL_R.Length; c++)
                Txt(gfx, DET_HEAD[c], _fHead, DET_COL_R[c] - DET_CELL_W, top, DET_CELL_W, DET_LINE,
                    _fmtCenter, C_THEAD);

            for (int k = 0; k < DET_ROWS.Length; k++)
            {
                int yy = top + DET_LINE * (k + 1);
                Txt(gfx, DET_ROWS[k], _fHead, COL_TITLE_X, yy, DET_LABEL_W, DET_LINE, _fmtNear,
                    k == 4 ? C_THEAD : C_LABEL);

                int col = k == 0 ? C_ACCENT
                        : k == 3 ? (PlayerPanel.DetailCell(d, 3, 0) > 0 ? C_BAD : C_LABEL)
                        : C_VALUE;
                for (int c = 0; c < DET_COL_R.Length; c++)
                    Txt(gfx, PlayerPanel.DetailCell(d, k, c).ToString(CultureInfo.InvariantCulture),
                        _fHead, DET_COL_R[c] - DET_CELL_W, yy, DET_CELL_W, DET_LINE, _fmtCenter, col);
            }

            int spanW = DET_SUM_B + DET_SUM_BW - DET_SUM_A;
            Txt(gfx, "成绩", _fHead, DET_SUM_A, top, spanW, DET_LINE, _fmtNear, C_THEAD);

            double rating = r.Const > 0
                ? RatingEngine.SingleRating(r.Const, d.Score, d.Pace, d.Clear)
                : double.NaN;

            string time = PlayerPanel.HistTime(d.Seconds);
            if (time.Length >= 16) time = time.Substring(11, 5);

            DetKv(gfx, DET_SUM_A, DET_SUM_AW, "准度", PlayerPanel.DetailRateText(d),
                top + DET_LINE, C_ACCENT);
            DetKv(gfx, DET_SUM_B, DET_SUM_BW, "评级", PlayerPanel.DetailRatingText(rating),
                top + DET_LINE, C_VALUE);
            DetKv(gfx, DET_SUM_A, DET_SUM_AW, "EXT", PlayerPanel.DetailExactificationText(d, r.Const),
                top + DET_LINE * 2, C_VALUE);
            DetKv(gfx, DET_SUM_B, DET_SUM_BW, "连击", d.Combo.ToString(CultureInfo.InvariantCulture),
                top + DET_LINE * 2, C_VALUE);
            DetKv(gfx, DET_SUM_A, DET_SUM_AW, "EXP", PlayerPanel.DetailExpText(r.Difficulty, d),
                top + DET_LINE * 3, C_OK);
            DetKv(gfx, DET_SUM_B, DET_SUM_BW, "时间", time.Length > 0 ? time : "—",
                top + DET_LINE * 3, C_LABEL);

            int fast = PlayerPanel.DetailFastLate(d, 3, true);
            int late = PlayerPanel.DetailFastLate(d, 3, false);
            Txt(gfx, $"FAST {fast} · LATE {late}", _fHead, DET_SUM_A, top + DET_LINE * 4,
                spanW, DET_LINE, _fmtNear, C_VALUE);
        }

                static void DetKv(IntPtr gfx, int x, int w, string k, string v, int y, int vcolor)
        {
            Txt(gfx, k, _fHead, x, y, DET_KV_LABEL, DET_LINE, _fmtNear, C_LABEL);
            Txt(gfx, v, _fHead, x + DET_KV_LABEL, y, w - DET_KV_LABEL, DET_LINE, _fmtFar, vcolor);
        }

        static void DrawLiveSummary(IntPtr gfx, SettlementData d, int y0)
        {
            const int BAR_X = 550, BAR_W = 280;

            Txt(gfx, "进度", _fLabel, PAD, y0 + 2, 100, 28, _fmtNear, C_LABEL);
            string prog;
            if (d.TotalSongs <= 0) prog = "—";
            else if (d.CurrentIndex >= 0) prog = $"第 {Math.Min(d.CurrentIndex + 1, d.TotalSongs)} / {d.TotalSongs} 首";
            else prog = $"{d.DoneSongs} / {d.TotalSongs} 首";
            Txt(gfx, prog, _fValue, PAD + 100, y0, 240, 32, _fmtNear, C_VALUE);
            int total = Math.Max(1, d.TotalSongs);
            int done = Math.Max(0, Math.Min(d.DoneSongs, total));
            Bar(gfx, BAR_X, y0 + 11, BAR_W, 10, (double)done / total, C_ACCENT, false, -1);

            Txt(gfx, "阶段", _fLabel, PAD, y0 + 34, 100, 28, _fmtNear, C_LABEL);
            string phase = d.Ended ? d.EndText : d.Phase;
            Txt(gfx, Fit(gfx, phase, _fRow, PANEL_W - PAD * 2 - 100), _fRow,
                PAD + 100, y0 + 34, PANEL_W - PAD * 2 - 100, 28, _fmtNear,
                d.Ended ? C_BAD : C_VALUE);

            Txt(gfx, "血量", _fLabel, PAD, y0 + 66, 100, 28, _fmtNear, C_LABEL);
            string hps = d.HpNoLoss ? $"{d.Hp} / {d.HpMax}（本首不掉血）" : $"{d.Hp} / {d.HpMax}";
            Txt(gfx, hps, _fValue, PAD + 100, y0 + 64, 270, 32, _fmtNear,
                d.Hp <= d.HpPass ? C_BAD : C_OK);
            Txt(gfx, "通过线", _fLabel, 400, y0 + 66, 80, 28, _fmtNear, C_LABEL);
            Txt(gfx, "> " + d.HpPass.ToString(CultureInfo.InvariantCulture), _fValue,
                470, y0 + 64, 70, 32, _fmtNear, C_VALUE);

            int span = d.HpMax - d.HpMin;
            double hf = span > 0 ? (double)(d.Hp - d.HpMin) / span : 0.0;
            double passF = span > 0 ? (double)(d.HpPass - d.HpMin) / span : -1.0;
            Bar(gfx, BAR_X, y0 + 74, BAR_W, 12, hf, d.Hp <= d.HpPass ? C_BAD : C_OK, true, passF);

            Txt(gfx, "本首", _fLabel, PAD, y0 + 98, 100, 28, _fmtNear, C_LABEL);
            if (!d.LiveJudgesOk)
            {

                Txt(gfx, "判定还没接上", _fRow, PAD + 100, y0 + 98, 420, 28, _fmtNear, C_LABEL);
            }
            else
            {
                string j = $"EXACT+ {d.LiveShiny} · EXACT {d.LiveExact} · NEAR {d.LiveNear} · BREAK {d.LiveBreak}";
                Txt(gfx, Fit(gfx, j, _fRow, 420), _fRow, PAD + 100, y0 + 98, 420, 28, _fmtNear, C_VALUE);
            }
            string cb = d.LiveCombo < 0 ? "连击 —" : $"连击 {d.LiveCombo} / 最大 {d.LiveComboMax}";
            Txt(gfx, Fit(gfx, cb, _fRow, BAR_W), _fRow, BAR_X, y0 + 98, BAR_W, 28, _fmtFar, C_VALUE);
        }

                static void Bar(IntPtr gfx, int x, int y, int w, int h, double f, int color, bool outline, double passF)
        {
            if (f < 0) f = 0; else if (f > 1) f = 1;
            GdipFillRectangleI(gfx, Br(unchecked((int)0x26FFFFFF)), x, y, w, h);
            int fw = (int)(w * f + 0.5);
            if (fw > 0) GdipFillRectangleI(gfx, Br(color), x, y, fw, h);
            if (outline)
            {
                int bc = C_BORDER;
                GdipFillRectangleI(gfx, Br(bc), x, y, w, 1);
                GdipFillRectangleI(gfx, Br(bc), x, y + h - 1, w, 1);
                GdipFillRectangleI(gfx, Br(bc), x, y + 1, 1, h - 2);
                GdipFillRectangleI(gfx, Br(bc), x + w - 1, y + 1, 1, h - 2);
            }
            if (passF >= 0 && passF <= 1)
            {
                int px = x + (int)(w * passF + 0.5);
                if (px > x + w - 2) px = x + w - 2;
                GdipFillRectangleI(gfx, Br(C_TITLE), px, y - 2, 2, h + 4);
            }
        }

                static void DrawFootButton(IntPtr gfx, int x, int y, string text, bool hov, bool armed)
        {
            int bg = armed ? unchecked((int)0x40FF7B7B)
                   : hov  ? unchecked((int)0x3DFFFFFF)
                          : unchecked((int)0x14FFFFFF);
            int bc = armed ? C_BAD : (hov ? C_TITLE : C_BORDER);
            int tc = armed ? C_BAD : (hov ? C_TITLE : C_VALUE);
            GdipFillRectangleI(gfx, Br(bg), x, y, BTN_W, BTN_H);
            GdipFillRectangleI(gfx, Br(bc), x, y, BTN_W, 1);
            GdipFillRectangleI(gfx, Br(bc), x, y + BTN_H - 1, BTN_W, 1);
            GdipFillRectangleI(gfx, Br(bc), x, y + 1, 1, BTN_H - 2);
            GdipFillRectangleI(gfx, Br(bc), x + BTN_W - 1, y + 1, 1, BTN_H - 2);
            Txt(gfx, Fit(gfx, text, _fRow, BTN_W - 16), _fRow, x, y, BTN_W, BTN_H, _fmtCenter, tc);
        }

        static void Txt(IntPtr gfx, string text, IntPtr font, int x, int y, int w, int h, IntPtr fmt, int color)
        {
            if (string.IsNullOrEmpty(text) || font == IntPtr.Zero) return;
            var r = new GPRECTF { X = x, Y = y, Width = w, Height = h };
            GdipDrawString(gfx, text, -1, font, ref r, fmt, Br(color));
        }

        static string Fit(IntPtr gfx, string s, IntPtr font, float width)
        {
            if (string.IsNullOrEmpty(s)) return "";
            try
            {
                var r = new GPRECTF { X = 0, Y = 0, Width = width, Height = ROW_H };
                if (GdipMeasureString(gfx, s, -1, font, ref r, _fmtNear, out _, out int fitted, out _) != 0)
                    return s;
                if (fitted <= 0 || fitted >= s.Length) return s;
                return s.Substring(0, Math.Max(1, fitted - 1)) + "…";
            }
            catch { return s; }
        }

        static string FmtScore(double s) => ((long)Math.Round(s)).ToString("N0");

                static string ClearText(int clear)
        {
            switch (clear)
            {
                case 4:  return "PD";
                case 3:  return "FL";
                case 2:  return "Clear";
                case 1:  return "Failed";

                default: return "Incomplete";
            }
        }

        static int ClearColor(int clear)
        {
            if (clear >= 4) return C_ACCENT;
            if (clear >= 3) return C_OK;
            if (clear >= 2) return C_VALUE;
            if (clear < 0)  return C_LABEL;
            return C_BAD;
        }

        static int RankColor(string rank)
        {
            switch ((rank ?? "").Trim().ToUpperInvariant())
            {
                case "C":  return unchecked((int)0xFFB9C2D4);
                case "B":  return unchecked((int)0xFF7FBFFF);
                case "A":  return unchecked((int)0xFFB580FF);
                case "S":  return C_ACCENT;
                case "S+": return unchecked((int)0xFFFF7EE0);
                default:   return C_TITLE;
            }
        }

        static void Commit()
        {
            if (_hWnd == IntPtr.Zero) return;

            var ptDst = new POINT { x = _screenX, y = _screenY };
            var sz = new SIZE { cx = PANEL_W, cy = _panelH };
            var ptSrc = new POINT { x = 0, y = 0 };
            var blend = new BLENDFUNCTION
            {
                BlendOp = 0,
                BlendFlags = 0,
                SourceConstantAlpha = (byte)Math.Clamp((int)(_alpha * 255f), 0, 255),
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

                IntPtr[] fonts = { _fTitle, _fRank, _fVerdict, _fLabel, _fValue, _fHead, _fRow, _fFoot };
                foreach (var f in fonts)
                    if (f != IntPtr.Zero) GdipDeleteFont(f);
                _fTitle = _fRank = _fVerdict = _fLabel = _fValue = _fHead = _fRow = _fFoot = IntPtr.Zero;

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