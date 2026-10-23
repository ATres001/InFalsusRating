
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

[BepInPlugin("com.atres.infalsusrating", "In Falsus Rating", "0.9.0")]
public class Plugin : BasePlugin
{
    internal static ManualLogSource Logger;
    public static double PlayerRating = 0;
    public static int TotalExacts = 0;
    public static string PlayerName = "Player";
    public static List<ChartRating> ChartRatings = new List<ChartRating>();
    public static List<ExactChart> TopExactCharts = new List<ExactChart>();

    public static List<ExactChart> AllExactCharts = new List<ExactChart>();

    public static List<HistoryEntry> PlayHistory = new List<HistoryEntry>();
    public static volatile bool FirstRatingReady = false;
    public static volatile bool OverlayManuallyHidden = false;

    public static double DisplayRating => PlayerRating;
    public static int DisplayExt => TotalExacts;

    public static volatile bool HasRatingDelta = false;
    public static double RatingDeltaValue = 0;
    public static DateTime RatingDeltaShownAt = DateTime.MinValue;

        public static double RatingFloor(double v)
    {
        if (double.IsNaN(v) || double.IsInfinity(v)) return 0;
        return Math.Floor(v * 100.0 + 1e-7) / 100.0;
    }

        public static string RatingText(double v)
        => RatingFloor(v).ToString("0.00", CultureInfo.InvariantCulture);

        public static bool SetRatingDelta(double oldRating, double newRating)
    {
        double delta = RatingFloor(newRating) - RatingFloor(oldRating);
        if (Math.Abs(delta) < 0.005) return false;
        RatingDeltaValue = delta;
        RatingDeltaShownAt = DateTime.Now;
        HasRatingDelta = true;
        return true;
    }

    public static void ClearRatingDelta()
    {
        HasRatingDelta = false;
    }

    public override void Load()
    {
        Logger = base.Log;
        Logger.LogInfo("In Falsus Rating Mod 加载中...");
        ReleaseCleanup();
        EnsureResourceFiles();

        CleanLegacyLayout();
        ConstTable.Load();
        SongInfoCache.LoadCatalog();
        TaskConfig.Load();
        TaskProgress.Load();

        PlayCounts.Load();

        PlayerLevel.Load();

        PlayTimes.Load();

        TaskMarks.Load();

        ExamFails.Load();
        LoadPlayerName();

        Settings.Load();

        PlayerSkin.Load();
        Win32Overlay.PlayerPanel.Start();

        Win32Overlay.PlayerPanel.BottomLayer.Start();
        Win32Overlay.HpBar.Start();
        StartScenePatchLoop();
        StartHotkeyListener();
        StartDelayedOverlay();

        var harmony = new Harmony("com.atres.infalsusrating");
        harmony.PatchAll(typeof(Patch_NH_VNA));
        harmony.PatchAll(typeof(Patch_UIManagerUpdate));
        harmony.PatchAll(typeof(Patch_GameSceneExit));

        if (Patch_GameSceneExit.TargetMethod() == null)
            Logger.LogError("[Exam] 挂不上 GameScene._Sk —— 玩家自己退出/重开**不会**被判失败");
        else
            Logger.LogInfo("[Exam] 已挂 GameScene._Sk（玩家自己退出/重开 → 这一场判失败）");

        Logger.LogInfo("[Plugin] Harmony patch 已挂载");
    }

        public static string AssetDir => Path.Combine(Paths.ConfigPath, "InFalsusRating", "assets");

        public static string AssetPath(string rel)
    {
        string root = Path.Combine(Paths.ConfigPath, "InFalsusRating");
        string alt = Path.Combine(root, "assets", rel);
        if (File.Exists(alt) || Directory.Exists(alt)) return alt;
        return Path.Combine(root, rel);
    }

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

            EnsureFile("font2.ttf", AssetDir, sourceDirs);
            EnsureFile("bg.png",    Path.Combine(AssetDir, "GUI", "overlay"), sourceDirs);
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

                    Directory.CreateDirectory(destDir);
                    File.Copy(src, destPath);
                    Logger.LogInfo($"[Res] 已复制 {fileName}: {src} → {destPath}");
                    return;
                }
            }
            catch { }
        }

        Logger.LogInfo($"[Res] 未找到 {fileName}，请手动放到: {destPath}");
    }

        static void CleanLegacyLayout()
    {
        try
        {
            string root = Path.Combine(Paths.ConfigPath, "InFalsusRating");
            string assets = Path.Combine(root, "assets");

            CleanLegacyDir(root, "GUI", Path.Combine(assets, "GUI"));
            CleanLegacyDir(root, "deco", Path.Combine(assets, "deco"));
            CleanLegacyFile(root, "font2.ttf", Path.Combine(assets, "font2.ttf"));
            CleanLegacyFile(root, "font3.ttf", Path.Combine(assets, "font3.ttf"));
            CleanLegacyFile(root, "bg.png", Path.Combine(assets, "GUI", "overlay", "bg.png"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"[Res] 清理老布局: {ex.Message}");
        }
    }

    static void CleanLegacyDir(string root, string name, string newPath)
    {
        try
        {
            string old = Path.Combine(root, name);
            if (!Directory.Exists(old) || !Directory.Exists(newPath)) return;
            Directory.Delete(old, true);
            Logger.LogInfo($"[Res] 已清理老布局目录: {old}（素材已在新位置）");
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"[Res] 清理老布局 {name}: {ex.Message}");
        }
    }

    static void CleanLegacyFile(string root, string name, string newPath)
    {
        try
        {
            string old = Path.Combine(root, name);
            if (!File.Exists(old) || !File.Exists(newPath)) return;
            File.Delete(old);
            Logger.LogInfo($"[Res] 已清理老布局文件: {old}（素材已在新位置）");
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"[Res] 清理老布局 {name}: {ex.Message}");
        }
    }

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

        public static bool SavePlayerName(string name)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0) return false;
        try
        {
            string dir = Path.Combine(Paths.ConfigPath, "InFalsusRating");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "name.txt"), name);
            PlayerName = name;
            Logger.LogInfo($"[Name] 玩家名字已改为: {PlayerName}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"[Name] 写入 name.txt 失败: {ex.Message}");
            return false;
        }
    }

    static void ReleaseCleanup()
    {
        try
        {
            string dir = Path.Combine(Paths.ConfigPath, "InFalsusRating");
            Directory.CreateDirectory(dir);
            string done = Path.Combine(dir, "release_cleanup.done");
            if (File.Exists(done)) return;

            string[] names =
            {
                "debug.key", "debug_override.json",
                "achievements_extra.json",
                "ins_setlist.json", "ins_scores.json",
            };
            foreach (string n in names)
            {
                string f = Path.Combine(dir, n);
                if (!File.Exists(f)) continue;
                File.Delete(f);
                Logger.LogInfo($"[Release] 已清除调试期遗留文件：{n}");
            }
            File.WriteAllText(done, "pre1.0\n");
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"[Release] 清理调试遗留失败: {ex.Message}");
        }
    }

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

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vKey);

    const int VK_CONTROL = 0x11;
    const int VK_SHIFT = 0x10;
    const int VK_MENU = 0x12;

    static void StartHotkeyListener()
    {
        var t = new Thread(() =>
        {

            var keyDown = new bool[256];
            var edge = new bool[256];

            while (true)
            {
                try
                {

                    bool skipArmed = GameProbe.MvAnyKeySkip;
                    bool fresh = false;
                    for (int vk = 0x01; vk <= 0xFE; vk++)
                    {
                        bool d;
                        try { d = (GetAsyncKeyState(vk) & 0x8000) != 0; }
                        catch { d = false; }
                        bool e = d && !keyDown[vk];
                        edge[vk] = e;
                        if (e) fresh = true;
                        keyDown[vk] = d;
                    }

                    bool ctrl = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
                    bool shift = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
                    bool alt = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;

                    bool canSkip = skipArmed && GameProbe.MvState != GameProbe.MvStateOff;

                    if (!Hotkeys.Capturing)
                    {
                        var binds = Hotkeys.Snapshot();
                        for (int i = 0; i < binds.Length; i++)
                        {
                            var b = binds[i];
                            if (b.Vk < 0 || b.Vk > 0xFE || !edge[b.Vk]) continue;
                            bool c = (b.Mods & Hotkeys.ModCtrl) != 0;
                            bool s = (b.Mods & Hotkeys.ModShift) != 0;
                            bool a = (b.Mods & Hotkeys.ModAlt) != 0;
                            if (c != ctrl || s != shift || a != alt) continue;

                            HotkeyFire(i);
                        }
                    }

                    if (canSkip && fresh && !Hotkeys.Capturing)
                    {
                        Logger.LogInfo("[Hotkey] 任意键 → 跳过开谱前的过场");

                        GameProbe.MvAnyKeySkip = false;
                        GameProbe.MvCmd = 2;
                    }
                }
                catch { }
                Thread.Sleep(50);
            }
        });
        t.IsBackground = true;
        t.Start();
    }

    static void HotkeyFire(int i)
    {
        string t = Hotkeys.Text(i);
        switch (Hotkeys.Defs[i].Id)
        {
            case "panel":

                if (Win32Overlay.SettlementPanel.IsShowing)
                    Win32Overlay.SettlementPanel.Hide();
                else if (TaskRunner.Busy)
                    Win32Overlay.SettlementPanel.ShowLive();
                else
                    Win32Overlay.PlayerPanel.Toggle();
                break;

            case "banner":
                OverlayManuallyHidden = !OverlayManuallyHidden;

                Settings.OverlayHidden = OverlayManuallyHidden;
                Settings.Save();
                Logger.LogInfo($"[Hotkey] {t} → overlay {(OverlayManuallyHidden ? "隐藏" : "显示")}（已记进 settings.json）");
                break;

            case "export_b30":
                Logger.LogInfo($"[Hotkey] {t} → 导出 B30 图片");
                ExportB30Image();
                break;
        }
    }

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

                Win32Overlay.Start(() => RatingText(DisplayRating));
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

    public static void ExportB30Image()
    {
        try
        {

            string dir = Settings.ExportRoot;
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "b30.png");

            if (ChartRatings == null || ChartRatings.Count == 0)
            {
                Logger.LogWarning("[B30Img] 暂无成绩，跳过导出");
                return;
            }

            if (B30CardExporter.HasJacketFolder())
            {
                bool ok = B30CardExporter.Export(path, PlayerName, DisplayRating, DisplayExt, ChartRatings, TopExactCharts);
                Logger.LogInfo($"[B30Img] 曲绘版导出 {(ok ? "成功" : "失败")} → {path}");
            }
            else
            {
                Logger.LogInfo("[B30Img] 无 jacket 文件夹，跳过曲绘版");
            }

            string noJacketPath = Path.Combine(dir, "b30_nojacket.png");
            bool ok2 = B30CardExporter.ExportNoJacket(noJacketPath, PlayerName, DisplayRating, DisplayExt, ChartRatings);
            Logger.LogInfo($"[B30Img] 无曲绘版导出 {(ok2 ? "成功" : "失败")} → {noJacketPath}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[B30Img] 导出异常: {ex.Message}");
        }
    }

    public static void ExportFromPanel(bool withJacket)
    {
        try
        {
            string dir = Settings.ExportRoot;
            Directory.CreateDirectory(dir);

            if (ChartRatings == null || ChartRatings.Count == 0)
            {
                Logger.LogWarning("[Panel] 暂无成绩，跳过导出");
                return;
            }

            if (withJacket)
            {
                if (!B30CardExporter.HasJacketFolder())
                {
                    Logger.LogWarning("[Panel] 没有 jacket 文件夹，跳过曲绘版");
                    return;
                }
                string path = Path.Combine(dir, "b30.png");
                bool ok = B30CardExporter.Export(path, PlayerName, DisplayRating, DisplayExt, ChartRatings, TopExactCharts);
                Logger.LogInfo($"[Panel] 曲绘版导出 {(ok ? "成功" : "失败")} → {path}");
                return;
            }

            RatingEngine.Redump();
            string noJacketPath = Path.Combine(dir, "b30_nojacket.png");
            bool ok2 = B30CardExporter.ExportNoJacket(noJacketPath, PlayerName, DisplayRating, DisplayExt, ChartRatings);
            Logger.LogInfo($"[Panel] 普通导出 {(ok2 ? "成功" : "失败")} → {noJacketPath}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[Panel] 导出异常: {ex.Message}");
        }
    }
}
