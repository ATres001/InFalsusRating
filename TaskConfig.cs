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

public static class TaskConfig
{
        public static int FailDive = -15;

        public static HpConfig Hp = new HpConfig();

        public static int SongsPerRun = 4;

        public static int OpenTiers = 6;

        public static List<Challenge> Tiers = new List<Challenge>();

        public static List<Challenge> Themes = new List<Challenge>();

        public static List<Challenge> Chapters = new List<Challenge>();

        public static List<Challenge> PlainThemes = new List<Challenge>();

    public static string Dir => Path.Combine(Paths.ConfigPath, "InFalsusRating");
    public static string ConfigFile => Path.Combine(Dir, "tasks.json");
    public static string ResultFile => Path.Combine(Dir, "task_result.json");

        public static string ExtraFile => Path.Combine(Dir, "tasks_themes.json");

        static List<Challenge> _themesFile = new List<Challenge>();

        static List<Challenge> _themesExtra = new List<Challenge>();

        static List<Challenge> _tiersFile = new List<Challenge>();

        static List<Challenge> _ranksExtra = new List<Challenge>();

        public static readonly string[] DiffOrder = { "Minimal", "Evolved", "Ultimate", "Forbidden" };

        public const string CatChapter = "chapter";

        public const string TagRank = "rank";

        public const string TagPlain = "";

        public class CatDef
    {
                public string Key = "";
                public string Label = "";
    }

        public static List<CatDef> Cats = new List<CatDef>();

        public static string CatsFile => Path.Combine(Dir, "tasks_cats.json");

    static List<CatDef> _catsFile = new List<CatDef>();
        static Dictionary<string, string> _catOf = new Dictionary<string, string>();

        static Dictionary<string, List<string>> _orderOf = new Dictionary<string, List<string>>();

        static Dictionary<string, List<Challenge>> _byCat = new Dictionary<string, List<Challenge>>();

        static readonly List<Challenge> NoChallenges = new List<Challenge>();

        public static string TagOf(Challenge c)
    {
        if (c == null) return TagPlain;
        if (_catOf.TryGetValue(c.Key, out var t)) return t ?? TagPlain;
        if (c.Cat.Length > 0) return c.Cat;
        return c.Kind == "rank" ? TagRank : TagPlain;
    }

        public static List<Challenge> Group(string tag)
    {
        if (tag == null) tag = TagPlain;
        if (tag == "theme") tag = TagPlain;
        return _byCat.TryGetValue(tag, out var l) ? l : NoChallenges;
    }

        public static void SetCat(string key, string tag)
    {
        if (string.IsNullOrEmpty(key)) return;
        tag = SanitizeArt(tag ?? "");

        var old = FindAny(key);
        if (old != null && old.Cat.Length > 0 && old.Cat != tag)
            Plugin.Logger.LogWarning($"[Exam] {key}「{old.Name}」原本写着 \"cat\": \"{old.Cat}\"，"
                + $"现在被面板改到「{tag}」—— tasks.json 里那行没动（插件不回写它），以面板这份为准");

        var next = new Dictionary<string, string>(_catOf);
        next[key] = tag;
        _catOf = next;

        if (tag.Length > 0 && !HasCat(tag))
        {
            var nc = new List<CatDef>(_catsFile) { new CatDef { Key = tag, Label = tag } };
            _catsFile = nc;
        }

        if (_orderOf.Count > 0)
        {
            var no = new Dictionary<string, List<string>>();
            foreach (var kv in _orderOf)
            {
                var l = new List<string>(kv.Value);
                l.Remove(key);
                no[kv.Key] = l;
            }
            _orderOf = no;
        }

        SaveCats();
        RebuildAll();
        Plugin.Logger.LogWarning($"[Debug] {key}「{(old != null ? old.Name : "?")}」归到分类「{tag}」→ {CatsFile}");
    }

        public static bool AddCat(string label)
    {
        string key = SanitizeArt((label ?? "").Trim());
        if (key.Length == 0) return false;
        if (HasCat(key)) return false;

        var next = new List<CatDef>(_catsFile) { new CatDef { Key = key, Label = (label ?? "").Trim() } };
        _catsFile = next;
        SaveCats();
        RebuildAll();
        Plugin.Logger.LogWarning($"[Debug] 新分类：{key}「{label}」→ {CatsFile}");
        return true;
    }

        public static bool RenameCat(string key, string label)
    {
        if (string.IsNullOrEmpty(key)) return false;
        label = (label ?? "").Trim();
        if (label.Length == 0) return false;

        var next = new List<CatDef>(_catsFile);
        int at = next.FindIndex(c => c != null && c.Key == key);
        if (at >= 0) next[at] = new CatDef { Key = key, Label = label };
        else next.Add(new CatDef { Key = key, Label = label });
        _catsFile = next;
        SaveCats();
        RebuildAll();
        return true;
    }

        public static bool RemoveCat(string key)
    {
        if (string.IsNullOrEmpty(key) || key == TagRank) return false;
        int at = _catsFile.FindIndex(c => c != null && c.Key == key);
        if (at < 0) return false;

        var next = new List<CatDef>(_catsFile);
        next.RemoveAt(at);
        _catsFile = next;
        SaveCats();
        RebuildAll();
        return true;
    }

        public static bool MoveCat(string key, int dir)
    {
        var keys = new List<string>();
        foreach (var c in Cats) if (c != null) keys.Add(c.Key);

        int i = keys.IndexOf(key);
        if (i < 0) return false;
        int j = i + dir;
        if (j < 0 || j >= keys.Count) return false;

        keys.RemoveAt(i);
        keys.Insert(j, key);

        var next = new List<CatDef>();
        foreach (var k in keys)
            foreach (var c in Cats)
                if (c != null && c.Key == k) { next.Add(new CatDef { Key = c.Key, Label = c.Label }); break; }

        _catsFile = next;
        SaveCats();
        RebuildAll();
        return true;
    }

        public static bool MoveInCat(string key, int dir)
    {
        var c = FindAny(key);
        if (c == null) return false;
        string tag = TagOf(c);
        var list = Group(tag);
        if (list.Count < 2) return false;

        int i = -1;
        for (int k = 0; k < list.Count; k++) if (list[k].Key == key) { i = k; break; }
        if (i < 0) return false;
        int j = i + dir;
        if (j < 0 || j >= list.Count) return false;

        var order = new List<string>();
        foreach (var x in list) order.Add(x.Key);
        order.RemoveAt(i);
        order.Insert(j, key);

        var next = new Dictionary<string, List<string>>(_orderOf);
        next[tag] = order;
        _orderOf = next;

        SaveCats();
        RebuildAll();
        return true;
    }

        public static Challenge FindAny(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        var t = Tiers;
        if (t != null) foreach (var c in t) if (c.Key == key) return c;
        var m = Themes;
        if (m != null) foreach (var c in m) if (c.Key == key) return c;
        return null;
    }

    static bool HasCat(string key)
    {
        foreach (var c in Cats) if (c != null && c.Key == key) return true;
        return false;
    }

        public static void SaveCats()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var cats = _catsFile;
            var catOf = _catOf;
            var order = _orderOf;

            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  // 课题分类（课题页顶部那排按钮）、每个课题归哪一类、每一类里课题的顺序。");
            sb.AppendLine("  // 插件整份重写这个文件时，**注释留不住**。");
            sb.AppendLine("  // 单开一个文件是为了不冲掉 tasks.json 里手写的注释（同 tasks_themes.json 的理由）。");
            sb.AppendLine("  // ⚠ cats 里的 key 是**标识符**（会被拼进课题的 cat），只能小写字母数字 _ -；");
            sb.AppendLine("  //   label 才是按钮上显示的字，随便写中文。");
            sb.AppendLine("  // ⚠ cat / order 只覆盖面板改过的那些；没提到的按课题自己的 cat / kind，顺序按 n。");
            sb.AppendLine("  //   \"rank\" 和 \"\" 是两个内置分类（段位 / 主题挑战），删不掉。");

            sb.AppendLine("  \"cats\": [");
            for (int i = 0; i < cats.Count; i++)
            {
                var c = cats[i];
                if (c == null) continue;
                sb.Append("    { \"key\": \"").Append(Esc(c.Key)).Append("\", \"label\": \"")
                  .Append(Esc(c.Label)).Append("\" }").AppendLine(i == cats.Count - 1 ? "" : ",");
            }
            sb.AppendLine("  ],");

            sb.AppendLine("  \"cat\": {");
            int n = 0, total = catOf.Count;
            foreach (var kv in catOf)
            {
                n++;
                sb.Append($"    \"{Esc(kv.Key)}\": \"{Esc(kv.Value)}\"")
                  .AppendLine(n == total ? "" : ",");
            }
            sb.AppendLine("  },");

            sb.AppendLine("  \"order\": {");
            n = 0; total = order.Count;
            foreach (var kv in order)
            {
                n++;
                sb.Append($"    \"{Esc(kv.Key)}\": [");
                for (int i = 0; i < kv.Value.Count; i++)
                    sb.Append(i > 0 ? ", " : "").Append('"').Append(Esc(kv.Value[i])).Append('"');
                sb.Append(']').AppendLine(n == total ? "" : ",");
            }
            sb.AppendLine("  }");
            sb.AppendLine("}");

            File.WriteAllText(CatsFile, sb.ToString());
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[Exam] tasks_cats.json 写入失败: {ex.Message}");
        }
    }

        static void LoadCats()
    {
        var cats = new List<CatDef>();
        var catOf = new Dictionary<string, string>();
        var order = new Dictionary<string, List<string>>();
        try
        {
            if (File.Exists(CatsFile))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(CatsFile), JSON_OPTS);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("cats", out var cs) && cs.ValueKind == JsonValueKind.Array)
                        foreach (var el in cs.EnumerateArray())
                        {
                            if (el.ValueKind != JsonValueKind.Object) continue;
                            if (!el.TryGetProperty("key", out var kEl)) continue;
                            var d = new CatDef
                            {
                                Key = SanitizeArt((kEl.GetString() ?? "").Trim()),
                                Label = el.TryGetProperty("label", out var lEl) && lEl.ValueKind == JsonValueKind.String
                                    ? (lEl.GetString() ?? "").Trim() : "",
                            };
                            cats.Add(d);
                        }

                    if (root.TryGetProperty("cat", out var co) && co.ValueKind == JsonValueKind.Object)
                        foreach (var p in co.EnumerateObject())
                            if (p.Value.ValueKind == JsonValueKind.String)
                                catOf[p.Name] = SanitizeArt((p.Value.GetString() ?? "").Trim());

                    if (root.TryGetProperty("order", out var od) && od.ValueKind == JsonValueKind.Object)
                        foreach (var p in od.EnumerateObject())
                        {
                            if (p.Value.ValueKind != JsonValueKind.Array) continue;
                            var l = new List<string>();
                            foreach (var el in p.Value.EnumerateArray())
                                if (el.ValueKind == JsonValueKind.String)
                                {
                                    var s = (el.GetString() ?? "").Trim();
                                    if (s.Length > 0) l.Add(s);
                                }
                            order[p.Name] = l;
                        }
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[Exam] tasks_cats.json 读取失败: {ex.Message}（分类按默认，不影响 tasks.json）");
            cats.Clear(); catOf.Clear(); order.Clear();
        }

        _catsFile = cats;
        _catOf = catOf;
        _orderOf = order;
        if (cats.Count > 0 || catOf.Count > 0)
            Plugin.Logger.LogInfo($"[Exam] tasks_cats.json：{cats.Count} 个自定义分类、"
                + $"{catOf.Count} 个归属覆盖、{order.Count} 份顺序表");
    }

    static bool _loaded = false;
    static bool _needSeed = false;

    internal static readonly JsonDocumentOptions JSON_OPTS = new JsonDocumentOptions
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        var tiers = new List<Challenge>();
        var themes = new List<Challenge>();

        LoadExtras();

        LoadCats();

        try
        {
            if (!File.Exists(ConfigFile))
            {
                _needSeed = true;
                Plugin.Logger.LogInfo($"[Exam] 没有 {ConfigFile}，等定数表就绪后写一份默认题库");
                return;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(ConfigFile), JSON_OPTS);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                Plugin.Logger.LogError("[Exam] tasks.json 的根不是对象，课题按空处理");
                return;
            }

            if (root.TryGetProperty("failDive", out var fd) &&
                fd.ValueKind == JsonValueKind.Number && fd.TryGetInt32(out int dive))
                FailDive = dive;

            if (root.TryGetProperty("songsPerRun", out var sp) && sp.ValueKind == JsonValueKind.Number &&
                sp.TryGetInt32(out int spr) && spr > 0)
                SongsPerRun = spr;

            Hp = ParseHp(root) ?? new HpConfig();

            if (root.TryGetProperty("rank", out var rk) && rk.ValueKind == JsonValueKind.Object)
            {
                if (rk.TryGetProperty("openTiers", out var ot) && ot.ValueKind == JsonValueKind.Number &&
                    ot.TryGetInt32(out int opn) && opn >= 0)
                    OpenTiers = opn;
                if (rk.TryGetProperty("tiers", out var ts)) ParseChallenges(ts, "rank", tiers);
            }

            if (root.TryGetProperty("themes", out var th)) ParseChallenges(th, "theme", themes);

            _tiersFile = tiers;
            _themesFile = themes;

            if (tiers.Count == 0 && themes.Count == 0)
            {
                _needSeed = true;
                Plugin.Logger.LogWarning("[Exam] tasks.json 里没有段位也没有主题，按默认题库补一份");
            }

            Plugin.Logger.LogInfo($"[Exam] 课题载入：段位 {tiers.Count} 段（开放到 {OpenTiers} 段）、"
                + $"主题 {themes.Count} 个（另有面板加的 {_themesExtra.Count} 个主题 / {_ranksExtra.Count} 段），"
                + $"每个课题默认 {SongsPerRun} 首，dive 报警线 {FailDive}（只记日志）");
            Plugin.Logger.LogInfo("[Exam] 段位评级闸："
                + string.Join(" / ", tiers.ConvertAll(t => $"{t.Name} {FmtRating(t.MinRating)}"))
                + "（0.0 = 不看评级）");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[Exam] tasks.json 读取失败: {ex.Message}");
        }
        finally
        {

            RebuildAll();
        }
    }

        static void LoadExtras()
    {
        var themes = new List<Challenge>();
        var ranks = new List<Challenge>();
        try
        {
            if (File.Exists(ExtraFile))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(ExtraFile), JSON_OPTS);
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Array) ParseChallenges(root, "theme", themes);
                else if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("themes", out var th)) ParseChallenges(th, "theme", themes);
                    if (root.TryGetProperty("ranks", out var rk)) ParseChallenges(rk, "rank", ranks);
                }
                Plugin.Logger.LogInfo($"[Exam] tasks_themes.json（面板加的 / 改过的）：主题 {themes.Count} 个、段位 {ranks.Count} 段");
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[Exam] tasks_themes.json 读取失败: {ex.Message}（按空处理，不影响 tasks.json）");
            themes.Clear();
            ranks.Clear();
        }
        _themesExtra = themes;
        _ranksExtra = ranks;
    }

        static List<Challenge> MergeThemes(List<Challenge> fromFile, List<Challenge> extra, string what)
    {
        var res = new List<Challenge>();
        if (fromFile != null) res.AddRange(fromFile);

        if (extra != null)
            foreach (var c in extra)
            {
                int at = res.FindIndex(x => x.Key == c.Key);
                if (at >= 0)
                {
                    Plugin.Logger.LogWarning($"[Exam] {what} {c.Key}「{res[at].Name}」被面板改过，"
                        + $"以 tasks_themes.json 的「{c.Name}」为准");
                    res[at] = c;
                }
                else res.Add(c);
            }

        res.Sort((a, b) => a.N.CompareTo(b.N));
        return res;
    }

        public static int NextThemeN()
    {
        int max = 0;
        foreach (var c in _themesFile) if (c.N > max) max = c.N;
        foreach (var c in _themesExtra) if (c.N > max) max = c.N;
        return max + 1;
    }

        public static void AddTheme(Challenge c)
    {
        if (c == null) return;

        if (c.Songs.Count == 0 && !c.IsPool)
        {
            Plugin.Logger.LogWarning($"[Exam] 拒绝加空主题「{c.Name}」({c.Key})：一首曲子都没有，池子也没写");
            return;
        }

        var next = new List<Challenge>(_themesExtra);
        next.Add(c);
        _themesExtra = next;
        SaveExtraThemes();
        RebuildAll();

        Plugin.Logger.LogWarning($"[Debug] 新主题：{c.Key}「{c.Name}」{Describe(c)} → {ExtraFile}");
    }

        public static void ReplaceChallenge(Challenge c)
    {
        if (c == null || c.Key.Length == 0) return;
        if (c.Songs.Count == 0 && !c.IsPool)
        {
            Plugin.Logger.LogWarning($"[Exam] 拒绝改「{c.Name}」({c.Key})：一首曲子都没有，池子也没写");
            return;
        }

        bool rank = c.Kind == "rank";
        bool fromExtra = IsFromExtra(c.Key);
        var src = rank ? _ranksExtra : _themesExtra;
        var next = new List<Challenge>(src);
        int at = next.FindIndex(x => x.Key == c.Key);
        bool existed = at >= 0;
        if (existed) next[at] = c;
        else next.Add(c);
        if (rank) _ranksExtra = next; else _themesExtra = next;
        SaveExtraThemes();
        RebuildAll();

        Plugin.Logger.LogWarning($"[Debug] {(existed ? "改" : "写")}{(rank ? "段位" : "主题")}："
            + $"{c.Key}「{c.Name}」{Describe(c)}"
            + (fromExtra ? "" : " · ⚠ 原件在 tasks.json 里，现在被这份覆盖")
            + $" → {ExtraFile}");
    }

        public static string Describe(Challenge c)
    {
        if (c == null) return "";
        return c.IsPool
            ? $"{c.Count} 首随机池（{PoolDiffText(c)} {c.PoolMin}-{c.PoolMax}）"
            : $"{c.Songs.Count} 首（默认 {(c.Songs.Count > 0 ? c.Songs[0].Diff : "?")}）";
    }

        public static bool IsFromExtra(string key)
    {
        if (key == null || key.Length == 0) return false;
        foreach (var c in _themesExtra) if (c.Key == key) return true;
        foreach (var c in _ranksExtra) if (c.Key == key) return true;
        return false;
    }

    static void SaveExtraThemes()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var themes = _themesExtra;
            var ranks = _ranksExtra;
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  // 自己加的主题、以及**改过的段位/主题**都写在这儿。");
            sb.AppendLine("  // 单开一个文件是为了**不冲掉 tasks.json 里手写的注释** —— 插件整份重写 json");
            sb.AppendLine("  // 只会输出代码里那套模板，手加的行全没（同 tasks_picks.json 的理由）。");
            sb.AppendLine("  // 格式和 tasks.json 的 themes / rank.tiers 一模一样，手改也行 —— **两边都会被读**。");
            sb.AppendLine("  // ⚠ 同 key 的以这份为准（原件还在 tasks.json 里躺着，只是被盖掉）；");
            sb.AppendLine("  //   n 是存档键，两个文件合起来算（插件只会往后排），别手改成撞号的。");
            sb.AppendLine("  \"themes\": [");
            for (int i = 0; i < themes.Count; i++) WriteChallenge(sb, themes[i], "    ", i == themes.Count - 1);
            sb.AppendLine("  ],");
            sb.AppendLine("  // 段位：**只放面板改过的那些**（改一段就整段抄过来，没改的不在这儿）");
            sb.AppendLine("  \"ranks\": [");
            for (int i = 0; i < ranks.Count; i++) WriteChallenge(sb, ranks[i], "    ", i == ranks.Count - 1);
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            File.WriteAllText(ExtraFile, sb.ToString());
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[Exam] tasks_themes.json 写入失败: {ex.Message}");
        }
    }

        static void RebuildAll()
    {

        Tiers  = MergeThemes(_tiersFile,  _ranksExtra,  "段位");
        Themes = MergeThemes(_themesFile, _themesExtra, "主题");

        var by = new Dictionary<string, List<Challenge>>();
        BucketInto(by, Tiers);
        BucketInto(by, Themes);

        foreach (var kv in by) SortInPlace(kv.Value, kv.Key);

        Cats = FixCats(_catsFile, by);

        _byCat = by;
        Chapters = Group(CatChapter);
        PlainThemes = Group(TagPlain);
    }

    static void BucketInto(Dictionary<string, List<Challenge>> by, List<Challenge> src)
    {
        if (src == null) return;
        foreach (var c in src)
        {
            if (c == null) continue;
            string tag = TagOf(c);
            if (!by.TryGetValue(tag, out var l)) { l = new List<Challenge>(); by[tag] = l; }
            if (!l.Contains(c)) l.Add(c);
        }
    }

        static void SortInPlace(List<Challenge> list, string tag)
    {
        if (list.Count < 2) return;
        if (!_orderOf.TryGetValue(tag, out var keys) || keys == null || keys.Count == 0) return;

        var pos = new Dictionary<string, int>();
        for (int i = 0; i < keys.Count; i++) pos[keys[i]] = i;

        list.Sort((a, b) =>
        {
            bool ha = pos.TryGetValue(a.Key, out int pa);
            bool hb = pos.TryGetValue(b.Key, out int pb);
            if (ha && hb) return pa.CompareTo(pb);
            if (ha) return -1;
            if (hb) return 1;
            int k = a.N.CompareTo(b.N);
            return k != 0 ? k : string.CompareOrdinal(a.Key, b.Key);
        });
    }

        static List<CatDef> FixCats(List<CatDef> fromFile, Dictionary<string, List<Challenge>> by)
    {
        var res = new List<CatDef>();
        var seen = new HashSet<string>();

        void Add(string key, string label)
        {
            if (key == null) return;
            if (!seen.Add(key)) return;
            res.Add(new CatDef { Key = key, Label = label.Length > 0 ? label : DefaultCatLabel(key) });
        }

        var src = fromFile ?? new List<CatDef>();

        if (!src.Exists(c => c != null && c.Key == TagRank))  Add(TagRank, "");
        if (!src.Exists(c => c != null && c.Key == TagPlain)) Add(TagPlain, "");

        foreach (var c in src) if (c != null) Add(c.Key, c.Label ?? "");
        foreach (var kv in by) Add(kv.Key, "");

        return res;
    }

        static string LabelIn(List<CatDef> fromFile, string key)
    {
        if (fromFile != null)
            foreach (var c in fromFile)
                if (c != null && c.Key == key && !string.IsNullOrEmpty(c.Label)) return c.Label;
        return "";
    }

        static string DefaultCatLabel(string key)
    {
        if (key == TagRank) return "段位";
        if (key == TagPlain) return "主题挑战";
        if (key == CatChapter) return "章节";
        return key;
    }

        public static void Reload()
    {
        _loaded = false;
        _needSeed = false;
        Load();
        Plugin.Logger.LogInfo("[Exam] tasks.json 已重新载入");
    }

        public static string SanitizeArt(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(24);
        foreach (char ch in s)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-') sb.Append(char.ToLowerInvariant(ch));
            if (sb.Length >= 24) break;
        }
        return sb.ToString();
    }

        static void ParseChallenges(JsonElement arr, string kind, List<Challenge> into)
    {
        if (arr.ValueKind != JsonValueKind.Array)
        {
            Plugin.Logger.LogWarning($"[Exam] {kind} 不是数组，跳过");
            return;
        }

        foreach (var el in arr.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object) continue;

            var c = new Challenge { Kind = kind, N = into.Count + 1, Count = SongsPerRun };

            if (el.TryGetProperty("n", out var nEl) && nEl.ValueKind == JsonValueKind.Number &&
                nEl.TryGetInt32(out int n) && n > 0)
                c.N = n;
            c.Key = kind + c.N;

            if (el.TryGetProperty("name", out var nmEl) && nmEl.ValueKind == JsonValueKind.String)
                c.Name = (nmEl.GetString() ?? "").Trim();
            if (c.Name.Length == 0) c.Name = kind == "theme" ? $"主题 {c.N}" : $"{c.N} 段";

            if (el.TryGetProperty("note", out var ntEl) && ntEl.ValueKind == JsonValueKind.String)
                c.Note = (ntEl.GetString() ?? "").Trim();

            if (el.TryGetProperty("art", out var artEl) && artEl.ValueKind == JsonValueKind.String)
                c.Art = SanitizeArt((artEl.GetString() ?? "").Trim());

            if (el.TryGetProperty("cat", out var catEl) && catEl.ValueKind == JsonValueKind.String)
                c.Cat = SanitizeArt((catEl.GetString() ?? "").Trim());

            if (el.TryGetProperty("final", out var finEl) && finEl.ValueKind == JsonValueKind.True)
                c.Final = true;

            if (el.TryGetProperty("mv", out var mvEl))
            {
                if (mvEl.ValueKind == JsonValueKind.True) c.Mv = true;
                else if (mvEl.ValueKind == JsonValueKind.False) c.Mv = false;
            }

            if (el.TryGetProperty("bga", out var bgEl))
            {
                if (bgEl.ValueKind == JsonValueKind.True) c.Bga = true;
                else if (bgEl.ValueKind == JsonValueKind.False) c.Bga = false;
            }

            if (el.TryGetProperty("needSongs", out var nsEl))
            {
                if (nsEl.ValueKind == JsonValueKind.True) c.NeedSongs = true;
                else if (nsEl.ValueKind == JsonValueKind.False) c.NeedSongs = false;
            }

            if (el.TryGetProperty("count", out var ctEl) && ctEl.ValueKind == JsonValueKind.Number &&
                ctEl.TryGetInt32(out int ct) && ct > 0)
                c.Count = ct;

            if (el.TryGetProperty("rating", out var rtEl) && rtEl.ValueKind == JsonValueKind.Number)
                c.MinRating = rtEl.GetDouble();
            if (el.TryGetProperty("needAvg", out var naEl) && naEl.ValueKind == JsonValueKind.Number)
                c.MinAvg = (long)naEl.GetDouble();
            if (el.TryGetProperty("needGrade", out var ngEl) && ngEl.ValueKind == JsonValueKind.String)
                c.MinGrade = (ngEl.GetString() ?? "").Trim().ToUpperInvariant();

            c.Hp = ParseHp(el);

            if (el.TryGetProperty("songs", out var ss)) ParseSongs(ss, c.Songs);

            if (c.Songs.Count == 0 &&
                el.TryGetProperty("diff", out var dfEl) && dfEl.ValueKind == JsonValueKind.String)
            {
                string d = (dfEl.GetString() ?? "").Trim();
                if (d.Length > 0)
                {
                    c.PoolDiff = d;
                    if (el.TryGetProperty("min", out var mnEl) && mnEl.ValueKind == JsonValueKind.Number &&
                        mnEl.TryGetInt32(out int mn)) c.PoolMin = mn;
                    if (el.TryGetProperty("max", out var mxEl) && mxEl.ValueKind == JsonValueKind.Number &&
                        mxEl.TryGetInt32(out int mx)) c.PoolMax = mx;
                    if (c.PoolMin < 0) c.PoolMin = 0;
                    if (c.PoolMax < c.PoolMin) c.PoolMax = c.PoolMin;
                }
            }

            if (into.Exists(x => x.Key == c.Key))
                Plugin.Logger.LogWarning($"[Exam] {kind} 里有两个 n={c.N} 的课题 —— 进度会串在一起，建议改掉");

            into.Add(c);
        }

        into.Sort((a, b) => a.N.CompareTo(b.N));
    }

        public static HpConfig ParseHp(JsonElement parent)
    {
        if (parent.ValueKind != JsonValueKind.Object) return null;
        if (!parent.TryGetProperty("hp", out var el) || el.ValueKind != JsonValueKind.Object) return null;

        var h = new HpConfig();
        if (el.TryGetProperty("max", out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int i)) h.Max = i;
        if (el.TryGetProperty("min", out v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out i)) h.Min = i;
        if (el.TryGetProperty("pass", out v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out i)) h.Pass = i;
        if (el.TryGetProperty("perSong", out v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out i)) h.PerSong = i;
        if (el.TryGetProperty("regen", out v))
        {
            if (v.ValueKind == JsonValueKind.True) h.Regen = true;
            else if (v.ValueKind == JsonValueKind.False) h.Regen = false;
        }
        if (el.TryGetProperty("fatal", out v))
        {
            if (v.ValueKind == JsonValueKind.True) h.Fatal = true;
            else if (v.ValueKind == JsonValueKind.False) h.Fatal = false;
        }

        if (el.TryGetProperty("nearLoss", out v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out i)) h.NearLoss = i;
        if (el.TryGetProperty("breakLoss", out v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out i)) h.BreakLoss = i;
        if (el.TryGetProperty("regenNeed", out v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out i)) h.RegenNeed = i;
        return h;
    }

        static void ParseSongs(JsonElement arr, List<TaskSong> into)
    {
        if (arr.ValueKind != JsonValueKind.Array) return;

        foreach (var s in arr.EnumerateArray())
        {
            if (s.ValueKind != JsonValueKind.Object) continue;
            if (!s.TryGetProperty("id", out var idEl)) { Plugin.Logger.LogWarning("[Exam] 有一条曲目没有 id，跳过"); continue; }

            int id;
            if (idEl.ValueKind == JsonValueKind.Number) { if (!idEl.TryGetInt32(out id)) continue; }
            else if (idEl.ValueKind == JsonValueKind.String) { if (!int.TryParse(idEl.GetString(), out id)) continue; }
            else { Plugin.Logger.LogWarning("[Exam] 有一条曲目的 id 既不是数字也不是字符串，跳过"); continue; }

            string diff = "Forbidden";
            if (s.TryGetProperty("diff", out var dEl) && dEl.ValueKind == JsonValueKind.String)
            {
                string ds = dEl.GetString();
                if (!string.IsNullOrWhiteSpace(ds)) diff = ds.Trim();
            }
            into.Add(new TaskSong { Id = id, Diff = diff });
        }
    }

        public static string WhyNotRunnable(Challenge c)
    {
        if (c == null) return "没有选中课题";
        if (c.Count <= 0) return "这个课题的 count 是 0，没法打";
        if (c.IsPool)
        {
            int n = PoolSize(c);
            if (n < c.Count)
            {

                int raw = PoolSizeRaw(c);
                if (raw > n && SongUnlock.Known)
                    return $"{PoolDiffText(c)} 定数 {c.PoolMin}-{c.PoolMax} 共 {raw} 张谱面，"
                        + $"其中 {raw - n} 张你还没在游戏里解锁，能抽的只剩 {n} 张 —— 不够抽 {c.Count} 首"
                        + $"（当前已解锁 {SongUnlock.UnlockedCharts} 张，先去多解锁一些歌）";
                return $"{PoolDiffText(c)} 定数 {c.PoolMin}-{c.PoolMax} 只找到 {n} 张谱面，不够抽 {c.Count} 首";
            }
            return "";
        }
        if (c.Songs.Count < c.Count) return $"曲单只有 {c.Songs.Count} 首，不够打 {c.Count} 首";

        if (c.Switchable)
        {
            string d = DiffFor(c);
            if (d.Length == 0) return "这个主题挑不出可打的难度";
            foreach (var s in c.Songs)
                if (!ConstTable.HasChart(s.Id, d)) return $"曲目 {s.Id} 没有 {d} 难度";
        }
        return "";
    }

        public static HpRules HpOf(Challenge c)
    {
        var r = new HpRules();

        Apply(r, Hp);
        Apply(r, c != null ? c.Hp : null);
        return Clamp(r);
    }

        static HpRules Clamp(HpRules r)
    {

        if (r.Min >= r.Max) r.Min = r.Max - 1;

        if (r.RegenNeed <= 0) r.RegenNeed = 1;
        if (r.NearLoss < 0) r.NearLoss = 0;
        if (r.BreakLoss < 0) r.BreakLoss = 0;
        return r;
    }

    static void Apply(HpRules r, HpConfig h)
    {
        if (h == null) return;
        if (h.Max.HasValue) r.Max = h.Max.Value;
        if (h.Min.HasValue) r.Min = h.Min.Value;
        if (h.Pass.HasValue) r.Pass = h.Pass.Value;
        if (h.PerSong.HasValue) r.PerSong = h.PerSong.Value;
        if (h.Regen.HasValue) r.Regen = h.Regen.Value;
        if (h.Fatal.HasValue) r.Fatal = h.Fatal.Value;
        if (h.NearLoss.HasValue) r.NearLoss = h.NearLoss.Value;
        if (h.BreakLoss.HasValue) r.BreakLoss = h.BreakLoss.Value;
        if (h.RegenNeed.HasValue) r.RegenNeed = h.RegenNeed.Value;
    }

        public static bool SetHp(string key, HpConfig hp)
    {
        var c = FindAny(key);
        if (c == null) return false;

        if (hp == null && !IsFromExtra(key))
        {
            Plugin.Logger.LogWarning($"[Debug] 改血量：{c.Key}「{c.Name}」→ 本来就没覆盖，无事可做");
            return true;
        }

        var copy = Clone(c);
        copy.Hp = hp;

        bool rank = copy.Kind == "rank";
        var next = new List<Challenge>(rank ? _ranksExtra : _themesExtra);
        int at = next.FindIndex(x => x.Key == copy.Key);
        if (at >= 0) next[at] = copy;
        else next.Add(copy);
        if (rank) _ranksExtra = next; else _themesExtra = next;
        SaveExtraThemes();
        RebuildAll();

        Plugin.Logger.LogWarning($"[Debug] 改血量：{copy.Key}「{copy.Name}」→ "
            + (hp == null ? "清掉覆盖（跟全局）" : HpJson(hp))
            + $" → {ExtraFile}（同键覆盖版，tasks.json 不动"
            + (IsFromExtra(key) ? "" : "；这个课题是 tasks.json 里的，下次合并会报一条「被面板改过」的 warning，正常")
            + "）");
        return true;
    }

        public static Challenge Clone(Challenge c)
    {
        if (c == null) return null;
        var n = new Challenge
        {
            Key = c.Key, Kind = c.Kind, N = c.N,
            Name = c.Name, Note = c.Note, Art = c.Art, Cat = c.Cat, Final = c.Final,
            Mv = c.Mv,
            Bga = c.Bga,
            PoolDiff = c.PoolDiff, PoolMin = c.PoolMin, PoolMax = c.PoolMax,
            Count = c.Count,
            MinRating = c.MinRating, NeedSongs = c.NeedSongs,
            MinAvg = c.MinAvg, MinGrade = c.MinGrade,
            Hp = c.Hp,
        };

        foreach (var s in c.Songs) n.Songs.Add(new TaskSong { Id = s.Id, Diff = s.Diff });
        return n;
    }

        public static int PoolSize(Challenge c)
        => c != null && c.IsPool ? ConstTable.InRange(c.PoolDiff, c.PoolMin, c.PoolMax).Count : 0;

        public static int PoolSizeRaw(Challenge c)
        => c != null && c.IsPool ? ConstTable.InRange(c.PoolDiff, c.PoolMin, c.PoolMax, true).Count : 0;

        public static string PoolDiffText(Challenge c)
        => c == null ? "" : (c.PoolDiff == ConstTable.AnyDiff ? "全难度" : c.PoolDiff);

        public static string DiffShort(string diff)
    {
        if (string.IsNullOrEmpty(diff)) return "";
        if (diff == ConstTable.AnyDiff) return "ALL";
        switch (diff.ToLowerInvariant())
        {
            case "minimal":   return "MIN";
            case "evolved":   return "EVO";
            case "ultimate":  return "ULT";
            case "forbidden": return "FBD";
        }
        return diff;
    }

        public static string DiffKey(string diff)
    {
        switch (DiffShort(diff))
        {
            case "MIN": return "min";
            case "EVO": return "evo";
            case "ULT": return "ult";
            case "FBD": return "fbd";
        }
        return "";
    }

        public static List<string> DiffChoicesOf(int songId)
    {
        var res = new List<string>();
        foreach (var d in DiffOrder)
            if (ConstTable.HasChart(songId, d)) res.Add(d);
        return res;
    }

        public static List<TaskSong> SetlistFor(Challenge c)
    {
        var res = new List<TaskSong>();
        if (c == null) return res;

        if (!c.IsPool)
        {
            string pick = DiffFor(c);
            bool overrideDiff = c.Switchable && pick.Length > 0;
            for (int i = 0; i < c.Songs.Count && res.Count < c.Count; i++)
            {
                var s = c.Songs[i];
                res.Add(overrideDiff ? new TaskSong { Id = s.Id, Diff = pick } : s);
            }
            return res;
        }
        return ConstTable.Draw(c.PoolDiff, c.PoolMin, c.PoolMax, c.Count);
    }

        public static string DiffFor(Challenge c)
    {
        if (c == null || !c.Switchable) return "";

        var choices = c.DiffChoices();
        string p = TaskProgress.Pick(c.Key);

        if (p.Length > 0 && choices.Contains(p)) return p;

        if (choices.Count == 0) return c.Songs.Count > 0 ? c.Songs[0].Diff : "";

        string cfg = c.Songs.Count > 0 ? c.Songs[0].Diff : "";
        return choices.Contains(cfg) ? cfg : choices[choices.Count - 1];
    }

        public static string ProgKey(Challenge c) => ProgKey(c, DiffFor(c));

        public static string ProgKey(Challenge c, string diff)
    {
        if (c == null) return "";
        return string.IsNullOrEmpty(diff) ? c.Key : c.Key + "@" + diff;
    }

        public static bool Unlocked(Challenge c)
    {
        if (c == null) return false;
        if (c.MinRating > 0 && Plugin.DisplayRating < c.MinRating) return false;

        if (LockedInSet(c) > 0) return false;

        if (c.Final)
        {

            var peers = Group(TagOf(c));
            foreach (var o in peers)
                if (o.Key != c.Key && !AnyCleared(o)) return false;
        }

        if (c.Kind != "rank") return true;
        if (c.N <= OpenTiers) return true;
        for (int i = Math.Max(OpenTiers + 1, 2); i <= c.N; i++)
            if (!TaskProgress.Cleared("rank" + (i - 1))) return false;
        return true;
    }

        public static bool AnyCleared(Challenge c) => TaskProgress.ClearedAny(c);

        public static int LockedInSet(Challenge c)
    {
        if (c == null || !c.NeedSongs || c.IsPool || c.Songs.Count == 0) return 0;
        if (!SongUnlock.Known) return 0;

        string pick = DiffFor(c);
        bool overrideDiff = c.Switchable && pick.Length > 0;
        int n = 0;
        for (int i = 0; i < c.Songs.Count && i < c.Count; i++)
        {
            var s = c.Songs[i];
            if (!SongUnlock.Unlocked(s.Id, overrideDiff ? pick : s.Diff)) n++;
        }
        return n;
    }

        public static bool RatingOk(Challenge c)
        => c != null && (c.MinRating <= 0 || Plugin.DisplayRating >= c.MinRating);

        public static string UnlockHint(Challenge c)
    {
        if (c == null || Unlocked(c)) return "";
        if (!RatingOk(c)) return $"总评级到 {FmtRating(c.MinRating)} 解锁（现在是 {FmtRating(Plugin.DisplayRating)}）";

        int locked = LockedInSet(c);
        if (locked > 0) return $"曲单里还有 {locked} 张谱面没解锁";

        if (c.Final)
        {
            var peers = Group(TagOf(c));
            foreach (var o in peers)
                if (o.Key != c.Key && !AnyCleared(o)) return $"先把「{o.Name}」打过";
            return $"先把{c.Name}以外的课题全部通过";
        }

        var list = Group(TagOf(c));
        for (int i = 0; i < list.Count; i++)
            if (list[i].Key == c.Key && i > 0) return $"先把「{list[i - 1].Name}」打过";

        return c.Kind == "rank" ? "先把上一段打过" : "（前一个课题还没过）";
    }

        public static string FmtRating(double r)
    {
        if (double.IsNaN(r) || double.IsInfinity(r)) return "0";
        double v = Math.Floor(r * 10.0 + 1e-7) / 10.0;
        return v.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
    }

        static void AddPool(List<Challenge> into, string kind, int n, string name, string note,
                        double needRating, string diff, int min, int max)
    {
        into.Add(new Challenge
        {
            Key = kind + n, Kind = kind, N = n, Name = name, Note = note,
            PoolDiff = diff, PoolMin = min, PoolMax = max,
            Count = SongsPerRun, MinRating = needRating,
        });
    }

        static Challenge AddFixed(List<Challenge> into, string kind, int n, string name, string note,
                              double needRating, params int[] ids)
    {
        var c = new Challenge
        {
            Key = kind + n, Kind = kind, N = n, Name = name, Note = note,
            Count = SongsPerRun, MinRating = needRating,
        };
        foreach (int id in ids) c.Songs.Add(new TaskSong { Id = id, Diff = "Forbidden" });
        into.Add(c);
        return c;
    }

        public static void EnsureSeeded()
    {
        if (!_needSeed) return;
        _needSeed = false;

        var tiers = new List<Challenge>();
        var themes = new List<Challenge>();

        try
        {

            AddPool(tiers, "rank",  1, "一段", "",  0, ConstTable.AnyDiff,  4,  6);
            AddPool(tiers, "rank",  2, "二段", "",  2, ConstTable.AnyDiff,  5,  7);
            AddPool(tiers, "rank",  3, "三段", "",  5, ConstTable.AnyDiff,  6,  8);
            AddPool(tiers, "rank",  4, "四段", "",  7, ConstTable.AnyDiff,  7,  9);
            AddPool(tiers, "rank",  5, "五段", "", 10, ConstTable.AnyDiff,  8, 10);
            AddPool(tiers, "rank",  6, "六段", "", 12, ConstTable.AnyDiff,  9, 11);

            AddFixed(tiers, "rank",  7, "七段", "", 0, 15, 22, 33, 19);
            AddFixed(tiers, "rank",  8, "八段", "", 0, 43, 68, 29, 45);
            AddFixed(tiers, "rank",  9, "九段", "", 0, 46, 49, 53, 10);
            AddFixed(tiers, "rank", 10, "十段", "", 0, 48, 6, 61, 9);

            AddFixed(themes, "theme", 1, "ARCAEA", "Arcaea 那几首里定数最高的四首",
                     0, 5, 12, 28, 59).Art = "arc";

            Plugin.Logger.LogInfo($"[Exam] 已写默认题库：段位 {tiers.Count} 段（1~6 段随机池 / 7~10 段定曲）、"
                + $"主题 {themes.Count} 个");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[Exam] 播种默认题库失败: {ex.Message}");
        }

        _tiersFile = tiers;
        _themesFile = themes;

        RebuildAll();
        Save();
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  // dive 掉到它就**在日志里记一笔**。2026-09-28 起它不管判死了 ——");
            sb.AppendLine("  // 那个交给下面的 \"hp\"（掉到 min 才结束）。这条现在纯粹是日志里的一根标尺。");
            sb.AppendLine($"  \"failDive\": {FailDive},");
            sb.AppendLine($"  \"songsPerRun\": {SongsPerRun},");
            sb.AppendLine();
            sb.AppendLine("  // ---- 血量（HP）----");
            sb.AppendLine("  // 考试现在按**我们自己的血量**判通过，不再看游戏内的 dive 值。");
            sb.AppendLine("  // 血量的增减 = dive 的逐帧变化量，等量加减（dive 掉 2 → 血掉 2），整场共用一条血。");
            sb.AppendLine("  //   max      上限，也是每首开场时的血量");
            sb.AppendLine("  //   min      下限（血量的活动范围就是 min ~ max，血条也按这一段画）");
            sb.AppendLine("  //   pass     通过线：全部打完血量**大于**它才算过");
            sb.AppendLine("  //   regen    true = 打歌途中 dive 回升会回血");
            sb.AppendLine("  //            false = 整个挑战不回血（只有 perSong 那一份）");
            sb.AppendLine("  //   perSong  每打完一首补多少血（0 = 不补）");
            sb.AppendLine("  //   fatal    true（默认）= **掉到 min 整场考试立即结束**（未认定，直接弹结算）");
            sb.AppendLine("  //            false = 掉到 min **不判死**，血卡在下限那儿接着打");
            sb.AppendLine("  //");
            sb.AppendLine("  // 三种难度就是这几个键的组合，没有单独的 mode 字段：");
            sb.AppendLine("  //   普通  = { }                                  ← 默认，会回血，掉到底就结束");
            sb.AppendLine("  //   简单  = { \"min\": 0, \"fatal\": false }         ← 不掉负血，中途永不结束");
            sb.AppendLine("  //   困难  = { \"regen\": false, \"perSong\": 0 }     ← 不回血（perSong 写个数就是每关补）");
            sb.AppendLine("  // 这里写的是**全局默认**，单个课题可以再写一份同名的覆盖它，比如");
            sb.AppendLine("  //   { \"n\": 7, \"hp\": { \"pass\": 30 } }   ← 只挪通过线，别的照旧跟全局");
            sb.AppendLine("  \"hp\": " + HpJson(Hp) + ",");
            sb.AppendLine();
            sb.AppendLine("  // ---- 段位 ----");
            sb.AppendLine("  //   n       段号（也是存档里的键，别乱改）");
            sb.AppendLine("  //   name    显示名，随便改");
            sb.AppendLine("  //   池子     \"diff\":\"Ultimate\", \"min\":10, \"max\":12 = 从该难度、定数 10~12 的谱面里随机抽");
            sb.AppendLine("  //            \"diff\":\"*\" = **不限难度**，只按定数抽（定数本来就是跨难度可比的，");
            sb.AppendLine("  //            一~六段用的就是这个，池子大得多，不会老是抽到同几张）");
            sb.AppendLine("  //            抽出来的 4 首会按定数**从易到难**排，第一首最简单");
            sb.AppendLine("  //   定曲     \"songs\":[{\"id\":2,\"diff\":\"Forbidden\"}] = 就打这几首（顺序照你写的，不重排）");
            sb.AppendLine("  //            （一首歌通常四个难度都有谱面，diff 写错不报错，只是开不了，注意对）");
            sb.AppendLine("  //   count    可选，打几首，默认跟上面的 songsPerRun 一样");
            sb.AppendLine("  //   rating   可选，**总评级**到它才解锁这一段（不写 = 不看评级）。一段 0 = 白送");
            sb.AppendLine("  //            一~六段用这个锁；七~十段没有 rating，卡的是「上一段通过」");
            sb.AppendLine("  //   needAvg  可选，均分要**大于**它才算过（不写 = 不看均分）");
            sb.AppendLine("  //            分数满分是 100,000,000（全 EXACT），实测强成绩在 96,000,000 ~ 99,000,000");
            sb.AppendLine("  //   needGrade 可选，档位至少要到它才算过：C / B / A / S / S+");
            sb.AppendLine("  //            C = 通过 / B = 均分 > 96,500,000 / A = 均分 > 98,000,000");
            sb.AppendLine("  //            S = 全部 FullLink / S+ = 全部 PerfectDive（档位按整套算，取最高的那档）");
            sb.AppendLine(OpenTiers > 0
                ? $"  // 解锁两道闸，**都要满足**：① 该段的 \"rating\"（总评级）；"
                  + $"② 前 {OpenTiers} 段直接开，再往后要**逐段通过**。"
                : "  // 解锁两道闸，**都要满足**：① 该段的 \"rating\"（总评级）；"
                  + "② **通过即解锁下一段**（openTiers = 0，开局只有一段开着）。");
            sb.AppendLine("  // 通过 = 这一个课题的歌**全打下来**，不看均分也不看档位。");
            sb.AppendLine("  \"rank\": {");
            sb.AppendLine($"    \"openTiers\": {OpenTiers},");
            sb.AppendLine("    \"tiers\": [");

            for (int i = 0; i < _tiersFile.Count; i++) WriteChallenge(sb, _tiersFile[i], "      ", i == _tiersFile.Count - 1);
            sb.AppendLine("    ]");
            sb.AppendLine("  },");
            sb.AppendLine();
            sb.AppendLine("  // ---- 主题挑战 ----");
            sb.AppendLine("  // 跟段位是同一套东西，多了个 note 当说明，**不做解锁**（全都开着）。");
            sb.AppendLine("  \"themes\": [");

            for (int i = 0; i < _themesFile.Count; i++) WriteChallenge(sb, _themesFile[i], "    ", i == _themesFile.Count - 1);
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            File.WriteAllText(ConfigFile, sb.ToString());
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[Exam] tasks.json 写入失败: {ex.Message}");
        }
    }

        public static string HpJson(HpConfig h)
    {
        if (h == null) return "{}";
        var sb = new StringBuilder("{");
        bool first = true;
        void Add(string k, string v)
        {
            if (!first) sb.Append(", ");
            first = false;
            sb.Append('"').Append(k).Append("\": ").Append(v);
        }
        if (h.Max.HasValue) Add("max", h.Max.Value.ToString());
        if (h.Min.HasValue) Add("min", h.Min.Value.ToString());
        if (h.Pass.HasValue) Add("pass", h.Pass.Value.ToString());
        if (h.PerSong.HasValue) Add("perSong", h.PerSong.Value.ToString());
        if (h.Regen.HasValue) Add("regen", h.Regen.Value ? "true" : "false");
        if (h.Fatal.HasValue) Add("fatal", h.Fatal.Value ? "true" : "false");
        if (h.NearLoss.HasValue) Add("nearLoss", h.NearLoss.Value.ToString());
        if (h.BreakLoss.HasValue) Add("breakLoss", h.BreakLoss.Value.ToString());
        if (h.RegenNeed.HasValue) Add("regenNeed", h.RegenNeed.Value.ToString());
        return sb.Append('}').ToString();
    }

        static void WriteChallenge(StringBuilder sb, Challenge c, string indent, bool last)
    {
        sb.Append(indent).Append("{ \"n\": ").Append(c.N);
        if (c.Name.Length > 0) sb.Append(", \"name\": \"").Append(Esc(c.Name)).Append('"');
        if (c.Note.Length > 0) sb.Append(", \"note\": \"").Append(Esc(c.Note)).Append('"');

        if (c.IsPool)
        {
            sb.Append(", \"diff\": \"").Append(Esc(c.PoolDiff)).Append('"');
            sb.Append(", \"min\": ").Append(c.PoolMin).Append(", \"max\": ").Append(c.PoolMax);
        }
        else
        {
            sb.Append(", \"songs\": [");
            for (int i = 0; i < c.Songs.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append("{ \"id\": ").Append(c.Songs[i].Id)
                  .Append(", \"diff\": \"").Append(Esc(c.Songs[i].Diff)).Append("\" }");
            }
            sb.Append(']');
        }

        if (c.Count != SongsPerRun) sb.Append(", \"count\": ").Append(c.Count);

        if (c.Art.Length > 0) sb.Append(", \"art\": \"").Append(Esc(c.Art)).Append('"');

        if (c.Cat.Length > 0) sb.Append(", \"cat\": \"").Append(Esc(c.Cat)).Append('"');
        if (c.Final) sb.Append(", \"final\": true");

        sb.Append(", \"needSongs\": ").Append(c.NeedSongs ? "true" : "false");

        sb.Append(", \"mv\": ").Append(c.Mv ? "true" : "false");

        sb.Append(", \"bga\": ").Append(c.Bga ? "true" : "false");

        if (c.MinRating > 0) sb.Append(", \"rating\": ").Append(c.MinRating.ToString("0.###"));
        if (c.MinAvg >= 0) sb.Append(", \"needAvg\": ").Append(c.MinAvg);
        if (c.MinGrade.Length > 0) sb.Append(", \"needGrade\": \"").Append(Esc(c.MinGrade)).Append('"');

        if (c.Hp != null) sb.Append(", \"hp\": ").Append(HpJson(c.Hp));

        sb.Append(" }").AppendLine(last ? "" : ",");
    }

    public static void SaveResult(SettlementData d)
    {
        if (d == null) return;
        try
        {
            Directory.CreateDirectory(Dir);
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"at\": \"{DateTime.Now:yyyy-MM-dd HH:mm}\",");
            sb.AppendLine($"  \"task\": \"{Esc(d.Title)}\",");
            sb.AppendLine($"  \"rank\": \"{d.Rank}\",");
            sb.AppendLine($"  \"certified\": {(d.Certified ? "true" : "false")},");
            sb.AppendLine($"  \"average\": {(long)Math.Round(d.AverageScore)},");
            sb.AppendLine($"  \"played\": {d.PlayedSongs},");
            sb.AppendLine($"  \"total\": {d.TotalSongs},");
            sb.AppendLine($"  \"fullLink\": {d.FullLinkCount},");
            sb.AppendLine($"  \"perfectDive\": {d.PerfectDiveCount},");
            sb.AppendLine($"  \"verdict\": \"{Esc(d.Verdict)}\"");
            sb.AppendLine("}");
            File.WriteAllText(ResultFile, sb.ToString());
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[Exam] task_result.json 写入失败: {ex.Message}");
        }
    }

        public static string LastResultLine()
    {
        if (Environment.TickCount - _lastLineAt < 1000) return _lastLine;
        _lastLineAt = Environment.TickCount;
        _lastLine = ReadResultLine();
        return _lastLine;
    }

    static int _lastLineAt = 0;
    static string _lastLine = null;

    static string ReadResultLine()
    {
        try
        {
            if (!File.Exists(ResultFile)) return "";
            using var doc = JsonDocument.Parse(File.ReadAllText(ResultFile), JSON_OPTS);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return "";

            string at = Str(r, "at"), rank = Str(r, "rank"), verdict = Str(r, "verdict");
            string task = Str(r, "task");
            bool certified = r.TryGetProperty("certified", out var c) && c.ValueKind == JsonValueKind.True;
            long avg = Num(r, "average");
            int played = (int)Num(r, "played"), total = (int)Num(r, "total");
            int fl = (int)Num(r, "fullLink"), pd = (int)Num(r, "perfectDive");

            string who = task.Length > 0 ? task + " " : "";
            string head = certified
                ? $"上次 {at} · {who}通过 {rank}（均分 {avg:N0} · FullLink {fl}/{total} · PerfectDive {pd}/{total}）"
                : $"上次 {at} · {who}没过（打了 {played}/{total} 首）";
            return string.IsNullOrEmpty(verdict) ? head : head + " · " + verdict;
        }
        catch { return ""; }
    }

    static string Str(JsonElement o, string k)
        => o.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : "";

    static long Num(JsonElement o, string k)
        => o.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long n) ? n : 0;

    static string Esc(string s)
        => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");
}