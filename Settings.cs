using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using BepInEx;

namespace InFalsusRating;

public static class Settings
{
    public static string JsonPath => Path.Combine(ConfigRoot, "settings.json");

    public static volatile bool OverlayHidden;

    public static volatile bool HideOnMouseAway = true;

    public static volatile bool HideOnLeaveGame = true;

    public static volatile bool HasOverlayScale;
    public static volatile float OverlayScale;

    public static volatile bool HasTitleScale;
    public static volatile float TitleScale;

    public static volatile bool ParticlesOn = true;

    public static volatile bool BottomLayerOn = true;

    public static volatile float PanelScale = 1f;

    public static volatile int PanelOpacity = 100;

    public static volatile bool RememberTab;

    public static volatile int LastTab;

    public static string ConfigRoot => Path.Combine(BepInEx.Paths.ConfigPath, "InFalsusRating");

    public static volatile string CustomExportDir = "";

    public static string ExportRoot
    {
        get
        {
            string d = CustomExportDir;
            if (!string.IsNullOrEmpty(d))
            {
                try
                {
                    if (Path.IsPathRooted(d)) return d;
                }
                catch
                {
                }
            }

            return ConfigRoot;
        }
    }

    public static volatile bool ToastOn = true;

    public static volatile int ToastHoldMs = 2600;

    static readonly object _saveLock = new object();

    public static void Init()
    {
        ExternalSaveManager.RegisterFile(JsonPath, Load);
    }

    public static void Load()
    {
        try
        {
            if (File.Exists(JsonPath))
            {
                string text;
                lock (_saveLock)
                {
                    text = File.ReadAllText(JsonPath);
                }

                ParseInto(text);
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[设置] settings.json 读不了（按默认值走）: {ex.Message}");
        }

        Push();
    }

    static void Push()
    {
        Plugin.OverlayManuallyHidden = OverlayHidden;
        Win32Overlay.PlayerPanel.ParticlesOn = ParticlesOn;
    }

    static void ParseInto(string text)
    {
        using var doc = JsonDocument.Parse(text);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return;
        var root = doc.RootElement;

        if (Obj(root, "overlay", out var ov))
        {
            OverlayHidden = Bool(ov, "hidden", OverlayHidden);
            HideOnMouseAway = Bool(ov, "hideMouse", HideOnMouseAway);
            HideOnLeaveGame = Bool(ov, "hideLeave", HideOnLeaveGame);
            if (Num(ov, "scale", out float sc))
            {
                HasOverlayScale = true;
                OverlayScale = sc < 0f ? 0f : sc;
            }

            if (Num(ov, "titleScale", out float ts))
            {
                HasTitleScale = true;
                TitleScale = ts;
            }
        }

        if (Obj(root, "panel", out var pn))
        {
            ParticlesOn = Bool(pn, "particles", ParticlesOn);
            BottomLayerOn = Bool(pn, "bottomLayer", BottomLayerOn);

            if (Num(pn, "scale", out float ps)) PanelScale = ps <= 0f ? 1f : ps;
            if (Num(pn, "opacity", out float po)) PanelOpacity = (int)Math.Round(po);
            RememberTab = Bool(pn, "rememberTab", RememberTab);
            if (Num(pn, "lastTab", out float lt)) LastTab = (int)Math.Round(lt);
        }

        if (Obj(root, "files", out var fl))
        {
            if (fl.TryGetProperty("exportDir", out var ed) && ed.ValueKind == JsonValueKind.String)
                CustomExportDir = ed.GetString() ?? "";
        }

        if (Obj(root, "play", out var pl))
        {
            ToastOn = Bool(pl, "toast", ToastOn);
            if (Num(pl, "toastHold", out float th)) ToastHoldMs = (int)Math.Round(th);
        }

        if (Obj(root, "hotkeys", out var hk))
        {
            foreach (var p in hk.EnumerateObject())
            {
                if (p.Value.ValueKind != JsonValueKind.String) continue;
                Hotkeys.LoadOverride(p.Name, p.Value.GetString());
            }
        }
    }

    public static void Save()
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");

            sb.AppendLine(
                "  \"_字段\": \"F6 面板「设置」页写出来的。手改也行，但改完要重启游戏。scale/titleScale 没写过就跟着 overlay.json 走。panel.scale = 面板大小（1 = 1280x720）、panel.opacity = 不透明度百分比、files.exportDir = 导出位置（空 = 默认夹子）、play.toastHold = 弹条停留毫秒、hotkeys = 改过的全局快捷键（只写改过的，键名见 Hotkeys.cs 的动作表，值写成 Ctrl+F1 / F9 这种，Esc 和鼠标键不能绑定）。\",");
            sb.AppendLine("  \"v\": 1,");
            sb.AppendLine("  \"overlay\": {");
            sb.AppendLine($"    \"hidden\": {B(OverlayHidden)},");

            sb.AppendLine($"    \"hideMouse\": {B(HideOnMouseAway)},");
            sb.AppendLine($"    \"hideLeave\": {B(HideOnLeaveGame)},");

            if (HasOverlayScale) sb.AppendLine($"    \"scale\": {F(OverlayScale)},");
            if (HasTitleScale) sb.AppendLine($"    \"titleScale\": {F(TitleScale)},");
            Trim(sb);
            sb.AppendLine("  },");
            sb.AppendLine("  \"panel\": {");
            sb.AppendLine($"    \"particles\": {B(ParticlesOn)},");
            sb.AppendLine($"    \"bottomLayer\": {B(BottomLayerOn)},");
            sb.AppendLine($"    \"scale\": {F(PanelScale)},");
            sb.AppendLine($"    \"opacity\": {PanelOpacity},");
            sb.AppendLine($"    \"rememberTab\": {B(RememberTab)},");
            sb.AppendLine($"    \"lastTab\": {LastTab}");
            sb.AppendLine("  },");
            sb.AppendLine("  \"files\": {");

            sb.AppendLine($"    \"exportDir\": {Esc(CustomExportDir)}");
            sb.AppendLine("  },");
            sb.AppendLine("  \"play\": {");
            sb.AppendLine($"    \"toast\": {B(ToastOn)},");
            sb.AppendLine($"    \"toastHold\": {ToastHoldMs}");
            sb.AppendLine("  },");

            sb.AppendLine("  \"hotkeys\": {");
            var hkLines = new List<string>();
            for (int i = 0; i < Hotkeys.Count; i++)
            {
                string t = Hotkeys.OverrideText(i);
                if (t != null) hkLines.Add($"    \"{Hotkeys.Defs[i].Id}\": {Esc(t)}");
            }

            for (int i = 0; i < hkLines.Count; i++)
                sb.AppendLine(hkLines[i] + (i < hkLines.Count - 1 ? "," : ""));
            sb.AppendLine("  }");
            sb.AppendLine("}");

            string text = sb.ToString();
            lock (_saveLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(JsonPath));
                File.WriteAllText(JsonPath, text);
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[设置] settings.json 写不进去: {ex.Message}");
        }
    }

    static void Trim(StringBuilder sb)
    {
        int i = sb.Length - 1;
        while (i > 0 && (sb[i] == '\n' || sb[i] == '\r')) i--;
        if (sb[i] == ',') sb[i] = ' ';
    }

    static string B(bool v) => v ? "true" : "false";
    static string F(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    static string Esc(string s)
    {
        if (string.IsNullOrEmpty(s)) return "\"\"";
        var sb = new StringBuilder(s.Length + 8);
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }

        return sb.Append('"').ToString();
    }

    static bool Obj(JsonElement o, string k, out JsonElement v)
        => o.TryGetProperty(k, out v) && v.ValueKind == JsonValueKind.Object;

    static bool Bool(JsonElement o, string k, bool dflt)
        => o.TryGetProperty(k, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False)
            ? v.GetBoolean()
            : dflt;

    static bool BoolOpt(JsonElement o, string k, out bool val)
    {
        val = false;
        if (!o.TryGetProperty(k, out var v)) return false;
        if (v.ValueKind != JsonValueKind.True && v.ValueKind != JsonValueKind.False) return false;
        val = v.GetBoolean();
        return true;
    }

    static bool Num(JsonElement o, string k, out float val)
    {
        val = 0f;
        return o.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetSingle(out val);
    }
}