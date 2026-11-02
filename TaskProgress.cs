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

public static class TaskProgress
{
    public class Rec
    {
        public bool Cleared;
        public string Grade = "";
        public long Avg;
        public string At = "";

        public int Seen;
    }

    static Dictionary<string, Rec> _recs = new Dictionary<string, Rec>();

    static Dictionary<string, string> _picks = new Dictionary<string, string>();

    public static string FilePath => Path.Combine(Settings.ConfigRoot, "tasks_progress.json");

    public static string PicksFile => Path.Combine(Settings.ConfigRoot, "tasks_picks.json");

    static bool _loaded = false;

    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        ExternalSaveManager.RegisterFile(PicksFile, LoadPicks);
        ExternalSaveManager.RegisterFile(FilePath, LoadRecords);
        LoadPicks();
        LoadRecords();
    }

    private static void LoadRecords()
    {
        var recs = new Dictionary<string, Rec>();
        try
        {
            string path = FilePath;
            if (!System.IO.File.Exists(path)) return;

            using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path), TaskConfig.JSON_OPTS);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return;

            foreach (var p in doc.RootElement.EnumerateObject())
            {
                if (p.Value.ValueKind != JsonValueKind.Object) continue;
                var v = p.Value;
                var r = new Rec
                {
                    Cleared = v.TryGetProperty("cleared", out var c) && c.ValueKind == JsonValueKind.True,
                    Grade = Out(v, "grade"),
                    Avg = v.TryGetProperty("avg", out var a) && a.ValueKind == JsonValueKind.Number &&
                          a.TryGetInt64(out long av)
                        ? av
                        : 0,
                    At = Out(v, "at"),
                    Seen = v.TryGetProperty("seen", out var sn) && sn.ValueKind == JsonValueKind.Number &&
                           sn.TryGetInt32(out int sv)
                        ? sv
                        : 0,
                };
                recs[p.Name] = r;
            }

            Plugin.Logger.LogInfo($"[Exam] 课题进度载入：{recs.Count} 条记录");
            MigrateFinalKeys(recs);
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[Exam] tasks_progress.json 读取失败: {ex.Message}");
        }
        finally
        {
            _recs = recs;
        }
    }

    static string Out(JsonElement o, string k)
        => o.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : "";

    static void MigrateFinalKeys(Dictionary<string, Rec> recs)
    {
        var t = TaskConfig.Tiers;
        var m = TaskConfig.Themes;

        void One(Challenge c)
        {
            if (c == null || !c.Final) return;
            if (!recs.TryGetValue(c.Key, out var old)) return;

            string d = Pick(c.Key);
            if (string.IsNullOrEmpty(d)) d = "Forbidden";
            string target = TaskConfig.ProgKey(c, d);
            recs.Remove(c.Key);

            if (recs.TryGetValue(target, out var cur))
            {
                cur.Cleared |= old.Cleared;
                if (old.Seen > cur.Seen) cur.Seen = old.Seen;
                if (string.IsNullOrEmpty(cur.Grade) && !string.IsNullOrEmpty(old.Grade))
                {
                    cur.Grade = old.Grade;
                    cur.Avg = old.Avg;
                    cur.At = old.At;
                }
            }
            else recs[target] = old;

            Plugin.Logger.LogWarning($"[Exam] ⚠ 老档迁移：「{c.Name}」的旧记录（裸键 {c.Key}，当年不分难度）"
                                     + $"已搬到「{target}」轨 —— 依据是选档表里那档 {d}（没选过 = 默认 Forbidden）。"
                                     + "当年实际通的可能是别的档；不对就手改 tasks_progress.json。原文件留了一份 .presplit.bak 备份。");

            try
            {
                string bak = FilePath + ".presplit.bak";
                if (!System.IO.File.Exists(bak)) System.IO.File.Copy(FilePath, bak);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[Exam] 老档备份失败（不影响迁移）: {ex.Message}");
            }
        }

        if (t != null)
            foreach (var c in t)
                One(c);
        if (m != null)
            foreach (var c in m)
                One(c);
    }

    public static string Pick(string key)
        => key != null && _picks.TryGetValue(key, out var d) ? d : "";

    public static void SetPick(string key, string diff)
    {
        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(diff)) return;
        if (Pick(key) == diff) return;

        var next = new Dictionary<string, string>(_picks);
        next[key] = diff;
        _picks = next;
        SavePicks();
        Plugin.Logger.LogInfo($"[Exam] 主题选档 {key} → {diff}");
    }

    static void LoadPicks()
    {
        var picks = new Dictionary<string, string>();
        try
        {
            string path = PicksFile;
            if (!System.IO.File.Exists(path)) return;

            using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path), TaskConfig.JSON_OPTS);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return;

            foreach (var p in doc.RootElement.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.String)
                    picks[p.Name] = p.Value.GetString();
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[Exam] tasks_picks.json 读取失败: {ex.Message}");
        }
        finally
        {
            _picks = picks;
        }
    }

    static void SavePicks()
    {
        try
        {
            Directory.CreateDirectory(Settings.ConfigRoot);
            var snap = _picks;
            var sb = new StringBuilder();
            sb.AppendLine("{");
            int i = 0;
            foreach (var kv in snap)
            {
                sb.Append($"  \"{Esc(kv.Key)}\": \"{Esc(kv.Value)}\"");
                sb.AppendLine(++i < snap.Count ? "," : "");
            }

            sb.AppendLine("}");
            System.IO.File.WriteAllText(PicksFile, sb.ToString());
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[Exam] tasks_picks.json 写入失败: {ex.Message}");
        }
    }

    public static void Reload()
    {
        LoadPicks();

        if (!File.Exists(FilePath))
        {
            Plugin.Logger.LogWarning("[Exam] 没有 tasks_progress.json，进度保持不变");
            return;
        }

        LoadRecords();
    }

    public static IEnumerable<Challenge> All()
    {
        var t = TaskConfig.Tiers;
        var m = TaskConfig.Themes;
        if (t != null)
            foreach (var c in t)
                yield return c;
        if (m != null)
            foreach (var c in m)
                yield return c;
    }

    public static IEnumerable<string> ProgKeysOf(Challenge c)
    {
        if (c == null) yield break;
        var choices = c.DiffChoices();
        if (choices.Count == 0)
        {
            yield return c.Key;
            yield break;
        }

        foreach (var d in choices) yield return TaskConfig.ProgKey(c, d);
    }

    public static Rec Get(string key)
        => key != null && _recs.TryGetValue(key, out var r) ? r : null;

    public static bool Cleared(string key)
    {
        var r = Get(key);
        return r != null && r.Cleared;
    }

    public static bool ClearedAny(Challenge c)
    {
        if (c == null) return false;
        string k = c.Key;
        if (string.IsNullOrEmpty(k)) return false;

        var recs = _recs;
        foreach (var kv in recs)
        {
            if (kv.Value == null || !kv.Value.Cleared) continue;
            string key = kv.Key;
            if (key == null) continue;

            if (key.Length == k.Length)
            {
                if (string.Equals(key, k, StringComparison.Ordinal)) return true;
            }

            else if (key.Length > k.Length && key[k.Length] == '@'
                                           && key.StartsWith(k, StringComparison.Ordinal)) return true;
        }

        return false;
    }

    public static int Seen(string key)
    {
        var r = Get(key);
        return r != null ? r.Seen : 0;
    }

    public static void NoteSeen(string key, int n)
    {
        if (string.IsNullOrEmpty(key) || n <= 0) return;
        var old = Get(key);
        if (old != null && old.Seen >= n) return;

        int was = old != null ? old.Seen : 0;
        var next = new Dictionary<string, Rec>(_recs);
        next[key] = new Rec
        {
            Cleared = old != null && old.Cleared,
            Grade = old != null ? old.Grade : "",
            Avg = old != null ? old.Avg : 0,
            At = old != null ? old.At : "",
            Seen = n,
        };
        _recs = next;
        Save();
        Plugin.Logger.LogInfo($"[Exam] 进度 {key}：揭开到第 {n} 首"
                              + (was > 0 ? $"（原来是 {was}）" : "（第一次进谱）"));
    }

    public static int GradeRank(string g)
    {
        switch ((g ?? "").Trim().ToUpperInvariant())
        {
            case "S+": return 5;
            case "S": return 4;
            case "A": return 3;
            case "B": return 2;
            case "C": return 1;
            default: return 0;
        }
    }

    public static string GradeWord(string g)
    {
        switch ((g ?? "").Trim().ToUpperInvariant())
        {
            case "S+": return "PD";
            case "S": return "FL";
            case "A": return "EX+";
            case "B": return "EX";
            case "C": return "C";
            default: return g ?? "";
        }
    }

    public static void Record(string progKey, string name, SettlementData d)
    {
        if (string.IsNullOrEmpty(progKey) || d == null) return;

        var old = Get(progKey);
        long avg = (long)Math.Round(d.AverageScore);
        int newGrade = GradeRank(d.Rank);

        var rec = new Rec
        {
            Cleared = d.Certified || (old != null && old.Cleared),
            Grade = old != null && GradeRank(old.Grade) > newGrade ? old.Grade : d.Rank,
            Avg = old != null && old.Avg > avg ? old.Avg : avg,
            At = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),

            Seen = old != null ? old.Seen : 0,
        };

        var next = new Dictionary<string, Rec>(_recs);
        next[progKey] = rec;
        _recs = next;

        Plugin.Logger.LogInfo($"[Exam] 进度 {progKey}（{name}）：{(rec.Cleared ? "已通过" : "未通过")}"
                              + $" · 最好档位 {rec.Grade} · 最好均分 {rec.Avg:N0}");

        Save();
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Settings.ConfigRoot);
            var snapshot = _recs;
            var sb = new StringBuilder();
            sb.AppendLine("{");
            int i = 0;
            foreach (var kv in snapshot)
            {
                var r = kv.Value;

                string seen = r.Seen > 0 ? $", \"seen\": {r.Seen}" : "";
                sb.Append($"  \"{Esc(kv.Key)}\": {{ \"cleared\": {(r.Cleared ? "true" : "false")}, "
                          + $"\"grade\": \"{Esc(r.Grade)}\", \"avg\": {r.Avg}, \"at\": \"{Esc(r.At)}\"{seen} }}");
                sb.AppendLine(++i < snapshot.Count ? "," : "");
            }

            sb.AppendLine("}");
            System.IO.File.WriteAllText(FilePath, sb.ToString());
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[Exam] tasks_progress.json 写入失败: {ex.Message}");
        }

        Achievements.CheckAll("课题进度");
    }

    static string Esc(string s)
        => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");
}