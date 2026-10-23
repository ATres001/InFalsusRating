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

    public static class HpBar
    {

        const int BAR_W = 669, BAR_H = 43;
        const int TEX_H = 86;

        const int TOP_MARGIN = 0;

        const int FILL_X0 = 13;
        const int FILL_W = 643;

        static readonly string[] FILES = { "HP_Bar.png", "HP.png", "HP_Safe.png", "FL_Bar.png", "PD_Bar.png", "HP_Minus.png" };

        static readonly string _hpDir = Plugin.AssetPath(Path.Combine("GUI", "hp"));

        const int T_BAR = 0, T_FILL = 1, T_SAFE = 2, T_FL = 3, T_PD = 4, T_MINUS = 5;

        static Thread _thread = null;
        static IntPtr _hWnd = IntPtr.Zero;
        static IntPtr _dc = IntPtr.Zero, _dib = IntPtr.Zero, _bits = IntPtr.Zero, _dibOld = IntPtr.Zero;
        static IntPtr _token = IntPtr.Zero;
        static WndProcDelegate _wndProc;
        static IntPtr _game = IntPtr.Zero;
        static int _screenX, _screenY;
        static bool _windowShown = false;
        static readonly IntPtr[] _tex = new IntPtr[FILES.Length];
        static bool _texTried = false;
        static string _texWhy = "";
        static IntPtr _ia = IntPtr.Zero;

        const float HP_NUM_PX = 22f;
        const float HP_NUM_LIFT = 5f;
        const int HP_NUM_NOWRAP = 0x00001000;
        static IntPtr _famNum = IntPtr.Zero, _fontNum = IntPtr.Zero, _fmtNum = IntPtr.Zero;
        static IntPtr _brNum = IntPtr.Zero;
        static IntPtr _famColl = IntPtr.Zero;

        const int INK_DARK = unchecked((int)0xFF06222E);
        const int INK_HI   = unchecked((int)0xFFF2F6FF);
        const int INK_MID  = unchecked((int)0xFFFFC24A);
        const int INK_LO   = unchecked((int)0xFFFF6A5A);

        const int SLIDE_MS = 200;
        const int CHASE_DMG_MS = 80;
        const int CHASE_HEAL_MS = 320;
        const int DMG_MS = 200;
        const int HEAL_MS = 380;
        const float CHASE_EPS = 0.02f;

        static long _now;
        static float _vis, _visFrom, _visTo;
        static long _rampAt;
        static byte _fade = 0;
        static float _shownHp = float.MinValue;
        static long _chaseAt;
        static int _hpNow;
        static int _lastHp = int.MinValue;
        static long _dmgAt = -1, _healAt = -1;

                public static void Start()
        {
            if (_thread != null) return;
            var t = new Thread(() =>
            {
                try { Run(); }
                catch (Exception ex) { Plugin.Logger.LogError($"[血条] {ex}"); }
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
                Plugin.Logger.LogError("[血条] GdiplusStartup 失败");
                return;
            }
            LoadTextures();

            if (GdipCreateImageAttributes(out _ia) != 0) _ia = IntPtr.Zero;

            SetupFont();

            _wndProc = WndProcImpl;
            var wc = new WNDCLASS
            {
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                lpszClassName = "InFalsusRatingHpBar"
            };
            RegisterClassW(ref wc);

            uint exStyle = WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            _hWnd = CreateWindowExW(exStyle, "InFalsusRatingHpBar", "HpBar", WS_POPUP,
                0, 0, BAR_W, BAR_H, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (_hWnd == IntPtr.Zero) { Plugin.Logger.LogError("[血条] CreateWindowEx 失败"); return; }

            _game = FindGameWindow();
            if (_game != IntPtr.Zero) SetWindowLongPtrW(_hWnd, GWLP_HWNDPARENT, _game);

            Plugin.Logger.LogInfo($"[血条] 就绪（{BAR_W}×{BAR_H}，{_texWhy}）");

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

        static void LoadTextures()
        {
            if (_texTried) return;
            _texTried = true;

            var miss = new List<string>();
            for (int i = 0; i < FILES.Length; i++)
            {
                string full = Path.Combine(_hpDir, FILES[i]);
                IntPtr bmp = IntPtr.Zero;
                if (!File.Exists(full)) miss.Add(FILES[i]);
                else if (GdipCreateBitmapFromFile(full, out bmp) != 0 || bmp == IntPtr.Zero)
                { bmp = IntPtr.Zero; miss.Add(FILES[i] + "(读不出)"); }
                _tex[i] = bmp;
            }

            if (_tex[T_BAR] == IntPtr.Zero)
            {
                _texWhy = $"⚠ 缺少底槽 HP_Bar.png（{_hpDir}），血条不会显示";
                Plugin.Logger.LogError($"[血条] {_texWhy}");
                return;
            }
            _texWhy = miss.Count == 0
                ? $"{FILES.Length} 张素材都在"
                : $"⚠ 缺 {string.Join("、", miss)}（缺的部件这一局不画）";
            if (miss.Count > 0) Plugin.Logger.LogWarning($"[血条] {_texWhy}");
        }

        static void Tick()
        {
            long now = Environment.TickCount64;
            _now = now;

            bool on = ExamHp.Active && _hWnd != IntPtr.Zero && _tex[T_BAR] != IntPtr.Zero
                      && GameProbe.MvState == GameProbe.MvStateOff;

            float want = on ? 1f : 0f;
            if (want != _visTo)
            {
                _visFrom = Ramp(_visFrom, _visTo, _rampAt, now, SLIDE_MS);
                _rampAt = now;
                _visTo = want;
                if (want > 0f)
                {
                    _shownHp = float.MinValue;
                    _lastHp = int.MinValue;
                    _chaseAt = now;
                }
            }
            _vis = Ramp(_visFrom, _visTo, _rampAt, now, SLIDE_MS);
            _fade = _vis >= 1f ? (byte)255 : (byte)(255f * _vis);

            bool shown = _fade > 0;
            if (shown != _windowShown)
            {
                ShowWindow(_hWnd, shown ? SW_SHOWNOACTIVATE : 0 );
                _windowShown = shown;
                if (shown) Plugin.Logger.LogInfo("[血条] 显示");
                else Plugin.Logger.LogInfo($"[血条] 隐藏（{ExamHp.Status()}）");
            }
            if (!shown) return;

            int hp = ExamHp.Hp;
            _hpNow = hp;
            if (_lastHp != int.MinValue)
            {
                if (hp < _lastHp) _dmgAt = now;
                else if (hp > _lastHp) _healAt = now;
            }
            _lastHp = hp;

            if (_shownHp == float.MinValue) { _shownHp = hp; _chaseAt = now; }
            float dt = (now - _chaseAt) / 1000f;
            _chaseAt = now;
            if (dt < 0f) dt = 0f; else if (dt > 0.25f) dt = 0.25f;
            float tau = (hp < _shownHp ? CHASE_DMG_MS : CHASE_HEAL_MS) / 1000f;
            _shownHp += (hp - _shownHp) * (1f - MathF.Exp(-dt / tau));
            if (MathF.Abs(hp - _shownHp) < CHASE_EPS) _shownHp = hp;

            TrackGameWindow();

            _screenY -= (int)((1f - _vis) * (BAR_H + 8));

            EnsureDib(BAR_W, BAR_H);
            Render(BAR_W, BAR_H);
            Commit();
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

            _screenX = pt.x + ((rc.right - rc.left) - BAR_W) / 2;
            _screenY = pt.y + TOP_MARGIN;
        }

        static void EnsureDib(int w, int h)
        {
            if (_dib != IntPtr.Zero) return;

            var sdc = GetDC(IntPtr.Zero);
            _dc = CreateCompatibleDC(sdc);
            ReleaseDC(IntPtr.Zero, sdc);

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

                GdipGraphicsClear(gfx, 0);
                Draw(gfx);
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

                            if (_fade != 255) a = (byte)(a * _fade / 255);

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

        static void Draw(IntPtr gfx)
        {

            float dmg = Pulse(_dmgAt, _now, DMG_MS) * 0.34f;
            float glow = Swell(_healAt, _now, HEAL_MS) * 0.55f;

            Blit(gfx, _tex[T_BAR], 0, BAR_W, Gain(dmg, dmg * 0.5f));

            bool safe = ExamHp.Regen && _hpNow > 0
                     && _hpNow >= 5 && _hpNow >= ExamHp.Max * 0.2f;
            IntPtr fillTex = safe ? _tex[T_SAFE] : _tex[T_FILL];
            if (fillTex == IntPtr.Zero) fillTex = _tex[T_FILL];

            int k = 0, m = 0;
            if (_shownHp >= 0f)
            {
                float f = ExamHp.Max > 0f ? _shownHp / ExamHp.Max : 1f;
                if (f < 0f) f = 0f; else if (f > 1f) f = 1f;
                k = (int)(FILL_W * f);
            }
            else
            {
                float g = ExamHp.Min < 0f ? -_shownHp / -ExamHp.Min : 0f;
                if (g < 0f) g = 0f; else if (g > 1f) g = 1f;
                m = (int)(FILL_W * g);
            }

            if (k > 0 && fillTex != IntPtr.Zero)
                Blit(gfx, fillTex, FILL_X0, k, Gain(dmg + glow, dmg * 0.5f + glow * 0.25f));
            else if (m > 0)
            {

                IntPtr mt = _tex[T_MINUS] != IntPtr.Zero ? _tex[T_MINUS] : fillTex;
                if (mt != IntPtr.Zero)
                    Blit(gfx, mt, FILL_X0, m, Gain(dmg + glow, dmg * 0.5f + glow * 0.25f));
            }

            int notch = ExamHp.Notch;
            IntPtr lit = notch == ExamHp.NOTCH_PD ? _tex[T_PD]
                       : notch == ExamHp.NOTCH_FL ? _tex[T_FL]
                       : IntPtr.Zero;
            if (lit != IntPtr.Zero) Blit(gfx, lit, 0, BAR_W, Gain(dmg, dmg * 0.5f));

            DrawHpText(gfx, k, safe);
        }

                static void SetupFont()
        {

            string path = Plugin.AssetPath("font2.ttf");
            if (File.Exists(path)
                && GdipNewPrivateFontCollection(out _famColl) == 0
                && GdipPrivateAddFontFile(_famColl, path) == 0)
            {
                GdipGetFontCollectionFamilyCount(_famColl, out int fc);
                if (fc > 0)
                {
                    var fams = new IntPtr[fc];
                    GdipGetFontCollectionFamilyList(_famColl, fc, fams, out int got);
                    if (got > 0) _famNum = fams[0];
                }
            }
            if (_famNum == IntPtr.Zero)
            {
                if (GdipCreateFontFamilyFromName("Microsoft YaHei", IntPtr.Zero, out _famNum) != 0)
                    GdipCreateFontFamilyFromName("Segoe UI", IntPtr.Zero, out _famNum);
                if (_famNum != IntPtr.Zero)
                    Plugin.Logger.LogWarning($"[血条] 读不到 font2.ttf（{path}），数字退回系统字体");
            }
            if (_famNum == IntPtr.Zero) { Plugin.Logger.LogWarning("[血条] 找不到字体，剩余血量那个数字不画"); return; }

            if (GdipCreateFont(_famNum, HP_NUM_PX, FontStyleBold, UnitPixel, out _fontNum) != 0 || _fontNum == IntPtr.Zero)
                GdipCreateFont(_famNum, HP_NUM_PX, 0 , UnitPixel, out _fontNum);

            GdipCreateStringFormat(HP_NUM_NOWRAP, 0, out _fmtNum);
            GdipSetStringFormatAlign(_fmtNum, StringAlignmentCenter);
            GdipSetStringFormatLineAlign(_fmtNum, StringAlignmentLineCenter);

            GdipCreateSolidFill(INK_HI, out _brNum);
        }

                static void DrawHpText(IntPtr gfx, int fillK, bool safe)
        {
            if (_fontNum == IntPtr.Zero || _fmtNum == IntPtr.Zero || _brNum == IntPtr.Zero) return;
            if (_shownHp == float.MinValue) return;
            string s = ((int)MathF.Round(_shownHp)).ToString();

            const int anchor = FILL_X0 + FILL_W / 2;

            bool onCyan = safe && (anchor - FILL_X0) < fillK;
            GdipSetSolidFillColor(_brNum, onCyan ? INK_DARK : InkFor(_shownHp));

            GdipSetTextRenderingHint(gfx, TextRenderingHintAntiAlias);

            var r = new GPRECTF { X = anchor - BAR_W / 2f, Y = -HP_NUM_LIFT, Width = BAR_W, Height = BAR_H };
            GdipDrawString(gfx, s, -1, _fontNum, ref r, _fmtNum, _brNum);
        }

                static int InkFor(float hp)
        {
            float r = ExamHp.Max > 0 ? hp / ExamHp.Max : 0f;
            if (r >= 0.5f) return INK_HI;
            if (r >= 0.2f) return Mix(INK_MID, INK_HI, (r - 0.2f) / 0.3f);
            if (r <= 0f) return INK_LO;
            return Mix(INK_LO, INK_MID, r / 0.2f);
        }

                static int Mix(int a, int b, float t)
        {
            int r = (int)(((a >> 16) & 255) + (((b >> 16) & 255) - ((a >> 16) & 255)) * t);
            int g = (int)(((a >> 8) & 255) + (((b >> 8) & 255) - ((a >> 8) & 255)) * t);
            int bl = (int)((a & 255) + ((b & 255) - (a & 255)) * t);
            return unchecked((int)0xFF000000) | (r << 16) | (g << 8) | bl;
        }

                static void Blit(IntPtr gfx, IntPtr img, int x, int w, IntPtr ia)
            => GdipDrawImageRectRectI(gfx, img, x, 0, w, BAR_H, x, 0, w, TEX_H,
                                      UnitPixel, ia, IntPtr.Zero, IntPtr.Zero);

                static IntPtr Gain(float scale, float offset)
        {
            if (_ia == IntPtr.Zero || (scale <= 0f && offset <= 0f)) return IntPtr.Zero;
            var m = new ColorMatrix
            {
                m00 = 1f + scale, m11 = 1f + scale, m22 = 1f + scale, m33 = 1f,

                m40 = offset, m41 = offset, m42 = offset, m44 = 1f
            };
            GdipSetImageAttributesColorMatrix(_ia, 1 , true, ref m, IntPtr.Zero, 0);
            return _ia;
        }

                static float Ramp(float from, float to, long at, long now, int ms)
        {
            if (from == to) return to;
            float t = (now - at) / (float)ms;
            if (t <= 0f) return from;
            if (t >= 1f) return to;
            float u = 1f - t;
            return from + (to - from) * (1f - u * u * u);
        }

                static float Pulse(long at, long now, int ms)
        {
            if (at < 0) return 0f;
            float t = (now - at) / (float)ms;
            return t < 0f || t > 1f ? 0f : 1f - t;
        }

                static float Swell(long at, long now, int ms)
        {
            if (at < 0) return 0f;
            float t = (now - at) / (float)ms;
            return t < 0f || t > 1f ? 0f : MathF.Sin(MathF.PI * t);
        }

        static void Commit()
        {
            if (_hWnd == IntPtr.Zero) return;
            var ptDst = new POINT { x = _screenX, y = _screenY };
            var sz = new SIZE { cx = BAR_W, cy = BAR_H };
            var ptSrc = new POINT { x = 0, y = 0 };
            var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            UpdateLayeredWindow(_hWnd, IntPtr.Zero, ref ptDst, ref sz, _dc, ref ptSrc, 0, ref blend, ULW_ALPHA);
        }

        static IntPtr WndProcImpl(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_DESTROY)
            {
                foreach (var t in _tex) if (t != IntPtr.Zero) GdipDisposeImage(t);
                for (int i = 0; i < _tex.Length; i++) _tex[i] = IntPtr.Zero;
                if (_ia != IntPtr.Zero) { GdipDisposeImageAttributes(_ia); _ia = IntPtr.Zero; }
                if (_brNum != IntPtr.Zero) { GdipDeleteBrush(_brNum); _brNum = IntPtr.Zero; }
                if (_fmtNum != IntPtr.Zero) { GdipDeleteStringFormat(_fmtNum); _fmtNum = IntPtr.Zero; }
                if (_fontNum != IntPtr.Zero) { GdipDeleteFont(_fontNum); _fontNum = IntPtr.Zero; }

                if (_famColl != IntPtr.Zero) GdipDeletePrivateFontCollection(ref _famColl);

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
