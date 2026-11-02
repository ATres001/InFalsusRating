using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using BepInEx;

namespace InFalsusRating;

public static class PlayerSkin
{
    public static string JsonPath => Path.Combine(Settings.ConfigRoot, "player.json");

    public static string Avatar = "";

    public static string Card = "";

    public static double CardCx = 0.5;
    public static double CardCy = 0.5;
    public static double CardZoom = 1.0;

    public static double CardBlur = 8;

    public const double BlurMin = 1.0, BlurMax = 16.0;

    public static string Char = "";

    public const string RandomKey = "*";

    public static string Bg = "";

    public static double BgBright = 0.45;

    public static int Version = 0;

    public static void Bump() => Version++;

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
            Plugin.Logger.LogWarning($"[玩家编辑] player.json 读不了（按默认值走）: {ex.Message}");
        }
    }

    static void ParseInto(string text)
    {
        using var doc = JsonDocument.Parse(text);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return;
        var root = doc.RootElement;

        Avatar = Clean(Nm(root, "avatar"));
        Card = Clean(Nm(root, "card"));
        Char = Clean(Nm(root, "char"));
        Bg = Clean(Nm(root, "bg"));
        CardCx = Dbl(root, "cardCx", CardCx, 0.0, 1.0);
        CardCy = Dbl(root, "cardCy", CardCy, 0.0, 1.0);
        CardZoom = Dbl(root, "cardZoom", CardZoom, 1.0, 8.0);
        CardBlur = Dbl(root, "cardBlur", CardBlur, BlurMin, BlurMax);
        BgBright = Dbl(root, "bgBright", BgBright, 0.0, 1.0);
    }

    public static bool Save()
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");

            sb.AppendLine(
                "  \"_字段\": \"F6 面板「玩家编辑」抽屉写出来的。名字是 jacket 或 jacket/scene 里的图片文件名（不含扩展名），空串 = 用默认。cardCx/cardCy 是裁剪中心（0~1）、cardZoom 是放大倍数（1~8）、cardBlur 是名片背景的模糊档（1~16，越大越糊，1 = 不糊）。bg 空串 = 背景每次随机抽；char 是 GUI/playerinfo/char 里的角色立绘，空串 = 不显示、\\\"*\\\" = 每次进页面随机抽。\",");
            sb.AppendLine("  \"v\": 1,");
            sb.AppendLine($"  \"avatar\": {Q(Avatar)},");
            sb.AppendLine($"  \"card\": {Q(Card)},");
            sb.AppendLine($"  \"cardCx\": {D(CardCx)},");
            sb.AppendLine($"  \"cardCy\": {D(CardCy)},");
            sb.AppendLine($"  \"cardZoom\": {D(CardZoom)},");
            sb.AppendLine($"  \"cardBlur\": {D(CardBlur)},");

            sb.AppendLine($"  \"char\": {Q(Char)},");
            sb.AppendLine($"  \"bg\": {Q(Bg)},");
            sb.AppendLine($"  \"bgBright\": {D(BgBright)}");
            sb.AppendLine("}");

            string text = sb.ToString();
            lock (_saveLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(JsonPath));
                File.WriteAllText(JsonPath, text, new UTF8Encoding(false));
            }

            return true;
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[玩家编辑] player.json 写不进去: {ex.Message}");
            return false;
        }
    }

    static string Nm(JsonElement o, string k)
        => o.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : "";

    static double Dbl(JsonElement o, string k, double dflt, double min, double max)
    {
        if (!o.TryGetProperty(k, out var v) || v.ValueKind != JsonValueKind.Number) return dflt;
        if (!v.TryGetDouble(out double d) || double.IsNaN(d) || double.IsInfinity(d)) return dflt;
        return d < min ? min : (d > max ? max : d);
    }

    static string Clean(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        if (name.IndexOf('/') >= 0 || name.IndexOf('\\') >= 0 || name.IndexOf(':') >= 0) return "";
        if (name.Contains("..")) return "";
        return name;
    }

    static string Q(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    static string D(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);
}