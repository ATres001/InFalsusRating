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

public static class Achievements
{
        public sealed class Def
    {
        public string Id = "";
        public string Name = "";
        public string Desc = "";
                public string Type = "";
                public string Diff = ConstTable.AnyDiff;
                public string Need = "";
                public double Value = 0;
                public string Clear = "";
                public string Song = "";
                public string Task = "";
                public bool Hidden = false;
                public string Cat = "";
                public string MinGrade = "";
                public int FromLevel = 1;
                public List<Rung> Grades;
                public bool Disabled = false;
    }

        public sealed class Rung
    {
        public string Diff = "";
        public string Need = "";
        public string Clear = "";
        public string Song = "";
        public string Task = "";
        public string MinGrade = "";
                public string Desc = "";
                public double? Value;
    }

        public sealed class State
    {
                public string At = "";
                public int Grade = 1;
                public bool Unseen = false;
    }

        public struct Progress
    {
        public bool Ok;
        public long Now, Need;
        public string Note;
    }

    public static string DefFile => Path.Combine(TaskConfig.Dir, "achievements.json");
    public static string StateFile => Path.Combine(TaskConfig.Dir, "achievements_progress.json");
        public static string DefExtraFile => Path.Combine(TaskConfig.Dir, "achievements_extra.json");

    static List<Def> _defs = new List<Def>();
    static Dictionary<string, State> _states = new Dictionary<string, State>();

        static volatile int _statesRev;

        const string TitleKey = "$title";

        const string TitleGradeKey = "$titleGrade";

        static string _equipped = "";

        static int _titleGradeSel;
        static HashSet<string> _baseIds = new HashSet<string>(StringComparer.Ordinal);
    static HashSet<string> _extraIds = new HashSet<string>(StringComparer.Ordinal);
        static HashSet<string> _genIds = new HashSet<string>(StringComparer.Ordinal);
    static bool _loaded;
        static bool _statesBad;
    static readonly object _lock = new object();
    static readonly HashSet<string> _warned = new HashSet<string>();

    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        LoadStates();
        LoadDefs();
    }

        public static void Reload()
    {
        _loaded = true;
        lock (_lock) _warned.Clear();
        LoadStates();
        LoadDefs();
    }

    static void LoadDefs()
    {
        var list = new List<Def>();
        var baseIds = new HashSet<string>(StringComparer.Ordinal);
        var extraIds = new HashSet<string>(StringComparer.Ordinal);
        var genIds = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            string path = DefFile;
            string text;
            if (System.IO.File.Exists(path))
            {
                text = System.IO.File.ReadAllText(path);
            }
            else
            {
                text = SEED;
                try
                {
                    Directory.CreateDirectory(TaskConfig.Dir);
                    System.IO.File.WriteAllText(path, SEED);
                    Plugin.Logger.LogInfo($"[Achieve] 生成默认成就定义：{path}");
                }
                catch (Exception ex)
                {

                    Plugin.Logger.LogWarning($"[Achieve] 默认定义写不出去（{ex.Message}），本次用内存里那份");
                }
            }

            ParseInto(list, baseIds, text, "achievements.json");

            BuildSongDefs(list, genIds);

            BuildTaskDefs(list, genIds);

            try
            {
                string epath = DefExtraFile;
                string etext = null;
                if (System.IO.File.Exists(epath))
                {
                    etext = System.IO.File.ReadAllText(epath);
                }
                else if (EXTRA_SEED.Length > 0)
                {

                    etext = EXTRA_SEED;
                    try
                    {
                        Directory.CreateDirectory(TaskConfig.Dir);
                        System.IO.File.WriteAllText(epath, EXTRA_SEED);
                        Plugin.Logger.LogInfo($"[Achieve] 生成默认成就批次（extra）：{epath}");
                    }
                    catch (Exception ex)
                    {
                        Plugin.Logger.LogWarning($"[Achieve] extra 批次写不出去（{ex.Message}），本次用内存里那份");
                    }
                }

                if (etext != null)
                {
                    var exList = new List<Def>();
                    ParseInto(exList, extraIds, etext, "achievements_extra.json");
                    int over = 0;
                    foreach (var d in exList)
                    {

                        int at = list.FindIndex(x => string.Equals(x.Id, d.Id, StringComparison.Ordinal));
                        if (at >= 0)
                        {
                            list[at] = d;
                            over++;
                            Plugin.Logger.LogWarning($"[Achieve] 「{d.Id}」的定义被 achievements_extra.json 覆盖（编辑器里改过）");
                        }
                        else list.Add(d);
                    }
                    if (exList.Count > 0)
                        Plugin.Logger.LogInfo($"[Achieve] extra 成就 {exList.Count} 条（其中覆盖基础定义 {over} 条）");
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[Achieve] achievements_extra.json 读取失败: {ex.Message}（按空处理，不影响 achievements.json）");
            }

            int off = list.RemoveAll(x => x.Disabled);
            if (off > 0)
                Plugin.Logger.LogInfo($"[Achieve] 停用（disabled）{off} 条，已从定义表里移除");

            int unl = 0; var st0 = _states;
            foreach (var d in list) if (st0.ContainsKey(d.Id)) unl++;
            Plugin.Logger.LogInfo($"[Achieve] 成就定义 {list.Count} 条，已解锁 {unl} 个");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[Achieve] achievements.json 读取失败: {ex.Message}");
        }
        finally
        {
            lock (_lock)
            {
                _defs = list;
                _baseIds = baseIds;
                _extraIds = extraIds;
                _genIds = genIds;
            }
        }
    }

        static void BuildSongDefs(List<Def> list, HashSet<string> genIds)
    {
        const string SONG_CAT = "曲目";
        const string FBD_DIFF = "Forbidden";

        List<int> ids = SongInfoCache.SelectableIds();
        if (ids == null || ids.Count == 0)
        {

            Plugin.Logger.LogWarning("[Achieve] 曲单是空的 —— 这次没有生成曲目成就（catalog 载入后重读即可）");
            return;
        }

        int made = 0, clash = 0;
        foreach (int sid in ids)
        {
            string song = sid.ToString(CultureInfo.InvariantCulture);
            string id = "song_" + song;
            if (list.Exists(x => string.Equals(x.Id, id, StringComparison.Ordinal)))
            {
                clash++;
                continue;
            }

            string name = SongInfoCache.GetName(song);
            if (string.IsNullOrEmpty(name) || name == song) name = "曲目 " + song;

            var d = new Def
            {
                Id   = id,
                Name = name,

                Desc = "任意难度达成 FullLink",
                Type = "clear",
                Diff = ConstTable.AnyDiff,
                Clear = "fullink",
                Song = song,
                Cat  = SONG_CAT,
            };

            d.Grades = new List<Rung>
            {
                new Rung { Diff = FBD_DIFF,
                           Desc = "Forbidden 难度达成 FullLink" },
                new Rung { Clear = "perfectdive",
                           Desc = "任意难度达成 PerfectDive" },
                new Rung { Diff = FBD_DIFF, Clear = "perfectdive",
                           Desc = "Forbidden 难度达成 PerfectDive" },
            };
            list.Add(d);
            genIds.Add(id);
            made++;
        }

        Plugin.Logger.LogInfo($"[Achieve] 曲目成就生成 {made} 条（曲单 {ids.Count} 首"
            + (clash > 0 ? $"，{clash} 条与已有定义同 id 已跳过" : "") + "）");
    }

        static void BuildTaskDefs(List<Def> list, HashSet<string> genIds)
    {
        const string TASK_CAT = "课题";
        const string FBD_DIFF = "Forbidden";

        var all = new List<Challenge>();
        if (TaskConfig.Tiers != null) all.AddRange(TaskConfig.Tiers);
        if (TaskConfig.Themes != null) all.AddRange(TaskConfig.Themes);
        if (all.Count == 0)
        {

            Plugin.Logger.LogWarning("[Achieve] 课题表是空的 —— 这次没有生成课题成就（tasks 载入后重读即可）");
            return;
        }

        int made = 0, clash = 0, covered = 0;
        foreach (var c in all)
        {
            if (c == null || string.IsNullOrEmpty(c.Key)) continue;

            if (list.Exists(x => x.Type == "task" &&
                    (string.Equals(x.Task, c.Key, StringComparison.OrdinalIgnoreCase)
                     || (c.Final && string.Equals(x.Task, "final", StringComparison.OrdinalIgnoreCase)))))
            {
                covered++;
                continue;
            }

            string id = "task_" + c.Key;
            if (list.Exists(x => string.Equals(x.Id, id, StringComparison.Ordinal)))
            {
                clash++;
                continue;
            }

            string name = string.IsNullOrEmpty(c.Name) ? c.Key : c.Name;

            bool hasFbd = !c.Switchable || c.DiffChoices().Contains(FBD_DIFF);
            string fbdTask = (c.Switchable && hasFbd) ? TaskConfig.ProgKey(c, FBD_DIFF) : c.Key;

            bool pool = !c.Switchable && c.Songs.Count == 0;

            var d = new Def
            {
                Id   = id,
                Name = name,

                Desc = "通关课题「" + name + "」",
                Type = "task",
                Task = c.Key,
                Cat  = TASK_CAT,
            };

            d.Grades = new List<Rung>
            {
                new Rung { Task = fbdTask,
                           Desc = (pool || !hasFbd) ? "通关该课题（该课题没有单独的 Forbidden 版）"
                                                    : "在 Forbidden 难度通关" },
                new Rung { MinGrade = "S",
                           Desc = "任意难度达成 FullLink（整套全部全连）" },
                new Rung { Task = fbdTask, MinGrade = "S+",
                           Desc = (pool || !hasFbd) ? "达成 PerfectDive（整套全部全完美）"
                                                    : "在 Forbidden 难度达成 PerfectDive" },
            };
            list.Add(d);
            genIds.Add(id);
            made++;
        }

        Plugin.Logger.LogInfo($"[Achieve] 课题成就生成 {made} 条（段位 {TaskConfig.Tiers?.Count ?? 0} + "
            + $"主题 {TaskConfig.Themes?.Count ?? 0}"
            + (covered > 0 ? $"，{covered} 条已有手写成就（不重复生成）" : "")
            + (clash > 0 ? $"，{clash} 条与已有定义同 id 已跳过" : "") + "）");
    }

        static void ParseInto(List<Def> list, HashSet<string> ids, string text, string label)
    {
        using var doc = JsonDocument.Parse(text, TaskConfig.JSON_OPTS);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return;
        if (!doc.RootElement.TryGetProperty("list", out var arr) || arr.ValueKind != JsonValueKind.Array) return;

        foreach (var e in arr.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object) continue;
            var d = ParseDef(e);
            if (d.Id.Length == 0) continue;

            if (!ids.Add(d.Id))
            {
                Plugin.Logger.LogWarning($"[Achieve] {label} 里有重复 id \"{d.Id}\"，后一条已忽略");
                continue;
            }
            list.Add(d);
        }
    }

        static Def ParseDef(JsonElement e)
    {
        var d = new Def
        {
            Id     = Str(e, "id").Trim(),
            Name   = Str(e, "name"),
            Desc   = Str(e, "desc"),
            Type   = Str(e, "type").Trim().ToLowerInvariant(),
            Diff   = Str(e, "diff").Trim(),
            Need   = Str(e, "need").Trim().ToLowerInvariant(),
            Clear  = Str(e, "clear").Trim().ToLowerInvariant(),
            Song   = Str(e, "song").Trim(),
            Task   = Str(e, "task").Trim(),
            Cat    = Str(e, "cat").Trim(),
            MinGrade = Str(e, "minGrade").Trim(),
            Hidden = e.TryGetProperty("hidden", out var h) && h.ValueKind == JsonValueKind.True,
            Disabled = e.TryGetProperty("disabled", out var dz) && dz.ValueKind == JsonValueKind.True,
        };
        if (e.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Number) d.Value = v.GetDouble();
        if (e.TryGetProperty("fromLevel", out var fl) && fl.ValueKind == JsonValueKind.Number)
            d.FromLevel = (int)Math.Round(fl.GetDouble());
        if (d.Diff.Length == 0) d.Diff = ConstTable.AnyDiff;
        if (d.Name.Length == 0) d.Name = d.Id;
        if (d.Cat.Length == 0) d.Cat = DefaultCatFor(d.Type);
        ParseGrades(e, d);

        int mg = MaxGrade(d);
        if (d.FromLevel < 1) d.FromLevel = 1;
        else if (d.FromLevel > mg) d.FromLevel = mg;
        return d;
    }

        static void ParseGrades(JsonElement e, Def d)
    {
        if (!e.TryGetProperty("grades", out var arr) || arr.ValueKind != JsonValueKind.Array) return;
        foreach (var ge in arr.EnumerateArray())
        {
            if (ge.ValueKind != JsonValueKind.Object) continue;
            if (d.Grades == null) d.Grades = new List<Rung>();
            if (d.Grades.Count >= 4)
            {
                Plugin.Logger.LogWarning($"[Achieve] 「{d.Id}」的 grades 超过 4 条（最多 1-4 公众级 + 第 5 隐藏级），多出来的已忽略");
                break;
            }
            var r = new Rung
            {
                Diff     = Str(ge, "diff").Trim(),
                Need     = Str(ge, "need").Trim().ToLowerInvariant(),
                Clear    = Str(ge, "clear").Trim().ToLowerInvariant(),
                Song     = Str(ge, "song").Trim(),
                Task     = Str(ge, "task").Trim(),
                MinGrade = Str(ge, "minGrade").Trim(),
                Desc     = Str(ge, "desc"),
            };
            if (ge.TryGetProperty("value", out var gv) && gv.ValueKind == JsonValueKind.Number) r.Value = gv.GetDouble();
            if (ge.TryGetProperty("type", out _))
                Plugin.Logger.LogWarning($"[Achieve] 「{d.Id}」的某一级写了 type —— 等级不换判据种类，已忽略（要换请另开一条成就）");
            d.Grades.Add(r);
        }
    }

        public static string DefaultCatFor(string type)
    {
        switch ((type ?? "").Trim().ToLowerInvariant())
        {
            case "clear": case "all": case "count": return "收集";
            case "rating": case "exact": case "score": return "评级";
            case "task": return "课题";
            default: return "其他";
        }
    }

    static void LoadStates()
    {
        var m = new Dictionary<string, State>();
        string equipped = "";
        int titleGrade = 0;
        bool backupFailed = false;
        string path = StateFile;
        try
        {
            if (System.IO.File.Exists(path))
            {
                using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path), TaskConfig.JSON_OPTS);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var p in doc.RootElement.EnumerateObject())
                    {

                        if (p.Name == TitleKey)
                        {
                            equipped = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : "";
                            continue;
                        }

                        if (p.Name == TitleGradeKey)
                        {
                            titleGrade = p.Value.ValueKind == JsonValueKind.Number
                                         && p.Value.TryGetInt32(out var tg) && tg >= 1 && tg <= 5 ? tg : 0;
                            continue;
                        }
                        if (p.Value.ValueKind != JsonValueKind.Object) continue;
                        m[p.Name] = new State
                        {
                            At = Str(p.Value, "at"),

                            Grade = p.Value.TryGetProperty("grade", out var g) && g.ValueKind == JsonValueKind.Number
                                    && g.TryGetInt32(out var gi) && gi >= 2 ? gi : 1,
                            Unseen = p.Value.TryGetProperty("unseen", out var u) && u.ValueKind == JsonValueKind.True,
                        };
                    }
                }
            }
        }
        catch (Exception ex)
        {

            Plugin.Logger.LogError($"[Achieve] achievements_progress.json 读取失败: {ex.Message}");
            try
            {
                System.IO.File.Copy(path, path + ".bad", true);
                Plugin.Logger.LogError($"[Achieve] 坏档已备份到 {path}.bad —— 本次按空档继续，下次解锁会重写原文件");
            }
            catch (Exception bex)
            {
                backupFailed = true;
                Plugin.Logger.LogError($"[Achieve] 坏档连备份都失败（{bex.Message}）—— 本次会话**不写** achievements_progress.json，免得把还能救的数据盖掉");
            }
        }
        finally
        {
            lock (_lock)
            {
                _states = m;
                _statesRev++;
                _equipped = equipped;
                _titleGradeSel = titleGrade;
                _statesBad = backupFailed;
            }
        }
    }

        static string Str(JsonElement o, string k)
        => o.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : "";

    static string Esc(string s)
        => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");

    static readonly List<ChartRating> _noCharts = new List<ChartRating>();

        static List<ChartRating> Snap => Plugin.ChartRatings ?? _noCharts;

        sealed class Ev { public Def D; public int G; public bool IsNew; public string At; }

        public static void CheckAll(string reason)
    {
        if (!_loaded) Load();

        List<Ev> fresh = null;
        string now = null;
        lock (_lock)
        {
            var defs = _defs;
            if (defs == null || defs.Count == 0) return;
            foreach (var d in defs)
            {
                bool has = _states.TryGetValue(d.Id, out var st);
                int cur = has ? (st.Grade < 1 ? 1 : st.Grade) : 0;
                int from = FromLevel(d);
                if (has && cur < from) cur = from;
                int maxG = MaxGrade(d);
                if (cur >= maxG) continue;
                int best = cur;

                for (int g = Math.Max(cur + 1, from); g <= maxG; g++)
                    if (Eval(LevelDef(d, g)).Ok) best = g;
                if (best <= cur) continue;
                if (now == null) now = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                (fresh ?? (fresh = new List<Ev>())).Add(new Ev { D = d, G = best, IsNew = !has, At = has ? st.At : null });
            }
            if (fresh == null) return;

            var next = new Dictionary<string, State>(_states);
            foreach (var e in fresh)
                next[e.D.Id] = new State
                {

                    At = e.IsNew || string.IsNullOrEmpty(e.At) ? now : e.At,
                    Grade = e.G,
                    Unseen = true,
                };
            _states = next;
            _statesRev++;
        }

        Save();
        int nNew = 0, nUp = 0;
        var namesNew = new List<string>();
        var namesUp = new List<string>();
        foreach (var e in fresh)
        {
            if (e.IsNew) { nNew++; namesNew.Add(e.D.Name); }
            else { nUp++; namesUp.Add(e.D.Name + "→" + GradeRoman(e.G)); }
        }
        string line = $"[Achieve] ({reason}) 新解锁 {nNew} 个";
        if (nNew > 0) line += "：" + string.Join("、", namesNew);
        if (nUp > 0) line += $"；升级 {nUp} 个：" + string.Join("、", namesUp);
        Plugin.Logger.LogInfo(line);

        foreach (var e in fresh) Notify(e.D, e.G, e.IsNew);
    }

        public static void Notify(Def d, int grade, bool isNew)
    {
        if (grade < 1) grade = 1;
        Plugin.Logger.LogInfo(isNew
            ? $"[Achieve] ★ 解锁「{d.Name}」（{d.Id}）"
            : $"[Achieve] ★ 「{d.Name}」升到 {GradeRoman(grade)}（{d.Id}）");

        string desc;
        if (isNew) desc = (d.Desc ?? "").Length > 0 || !HasLadder(d) ? d.Desc : DescribeRung(d, 1);
        else desc = DescribeRung(d, grade);

        int accent = HasLadder(d) ? GradeArgb(grade) : 0;
        Win32Overlay.Toast.Queue(d.Name, desc,
            isNew ? "成就解锁" : "成就升级 " + GradeRoman(grade), accent);
    }

        static Progress Eval(Def d)
    {

        if (d.Type != "task" && (d.MinGrade ?? "").Length > 0)
            WarnOnce(d, $"minGrade 只对 task 有效（这条是 {d.Type}），已忽略");
        switch (d.Type)
        {
            case "clear":  return EvalClear(d);
            case "all":    return EvalCharts(d, -1);
            case "count":  return EvalCharts(d, (int)d.Value);
            case "rating": return EvalRating(d);
            case "exact":  return EvalExact(d);
            case "score":  return EvalScore(d);
            case "task":   return EvalTask(d);
            default:
                WarnOnce(d, $"认不出的 type「{d.Type}」—— 这条永远不会解锁");
                return default;
        }
    }

    static Progress EvalClear(Def d)
    {
        int need = Level(d.Clear.Length > 0 ? d.Clear : d.Need);
        if (need < 0) { WarnOnce(d, $"通关等级「{(d.Clear.Length > 0 ? d.Clear : d.Need)}」认不出"); return default; }

        int best = 0;
        foreach (var c in Snap)
        {
            if (c == null || !DiffOk(d.Diff, c.Difficulty)) continue;
            if (d.Song.Length > 0 && !string.Equals(d.Song, c.SongId, StringComparison.Ordinal)) continue;
            int lv = LevelOf(c);
            if (lv > best) best = lv;
        }

        return new Progress { Ok = best >= need, Now = best, Need = need };
    }

        static Progress EvalCharts(Def d, int want)
    {
        int need = Level(d.Need.Length > 0 ? d.Need : d.Clear);
        if (need < 0) { WarnOnce(d, $"通关等级「{d.Need}」认不出"); return default; }

        var idx = Index();
        int got = 0;
        var rows = ConstTable.RowsOf(d.Diff);
        foreach (var key in rows)
            if (idx.TryGetValue(key, out var c) && LevelOf(c) >= need) got++;

        if (rows.Count == 0) return new Progress { Ok = false, Now = 0, Need = want > 0 ? want : 1 };

        long target = want > 0 ? want : rows.Count;
        return new Progress { Ok = got >= target, Now = got, Need = target };
    }

    static Progress EvalRating(Def d)
    {

        double r = Plugin.DisplayRating;
        return new Progress
        {
            Ok = r >= d.Value,
            Now = (long)Math.Round(r * 1000),
            Need = (long)Math.Round(d.Value * 1000),
            Note = $"{r:F3} / {d.Value:F2}",
        };
    }

    static Progress EvalExact(Def d)
    {
        int v = Plugin.TotalExacts;
        return new Progress { Ok = v >= d.Value, Now = v, Need = (long)Math.Round(d.Value) };
    }

    static Progress EvalScore(Def d)
    {
        ulong best = 0;
        foreach (var c in Snap)
            if (c != null && DiffOk(d.Diff, c.Difficulty) && c.Score > best) best = c.Score;
        long need = (long)Math.Max(0, d.Value);
        return new Progress { Ok = (long)best >= need, Now = (long)best, Need = need };
    }

    static Progress EvalTask(Def d)
    {
        string k = (d.Task ?? "").Trim();

        int need = 0;
        string mg = (d.MinGrade ?? "").Trim();
        if (mg.Length > 0)
        {
            need = TaskProgress.GradeRank(mg);
            if (need == 0)
            {
                WarnOnce(d, $"档位「{mg}」认不出（认 C / B / A / S / S+）—— 这条永远不会解锁");
                return default;
            }
        }

        bool ok;
        switch (k.ToLowerInvariant())
        {
            case "all_tiers":  ok = AllTiersCleared(need);  break;
            case "all_themes": ok = AllThemesCleared(need); break;
            case "final":

                if (need > 0) WarnOnce(d, "final 不支持 minGrade（终局课题没有档位），已忽略");
                ok = FinalCleared();
                break;
            default:
                if (k.Length == 0) { WarnOnce(d, "task 没写"); return default; }
                ok = TaskOk(k, need);
                break;
        }
        return new Progress { Ok = ok, Now = ok ? 1 : 0, Need = 1 };
    }

        static bool TaskOk(string key, int need)
    {
        if (TaskProgress.Get(key) != null) return RecOk(key, need);
        if (key.IndexOf('@') >= 0) return false;

        var c = TaskConfig.FindAny(key);
        if (c == null) return false;
        foreach (var diff in c.DiffChoices())
            if (RecOk(TaskConfig.ProgKey(c, diff), need)) return true;
        return false;
    }

        static bool RecOk(string key, int need)
    {
        var rec = TaskProgress.Get(key);
        return rec != null && rec.Cleared
               && (need == 0 || TaskProgress.GradeRank(rec.Grade) >= need);
    }

        static bool AllTiersCleared(int need)
    {
        var t = TaskConfig.Tiers;
        if (t == null || t.Count == 0) return false;
        foreach (var c in t)
            if (c != null && !RecOk(TaskConfig.ProgKey(c), need)) return false;
        return true;
    }

        static bool AllThemesCleared(int need)
    {
        var m = TaskConfig.Themes;
        if (m == null || m.Count == 0) return false;
        foreach (var c in m)
            if (c != null && !RecOk(TaskConfig.ProgKey(c), need)) return false;
        return true;
    }

        static bool FinalCleared()
    {
        var t = TaskConfig.Tiers;
        if (t != null) foreach (var c in t) if (c != null && c.Final) return TaskConfig.AnyCleared(c);
        var m = TaskConfig.Themes;
        if (m != null) foreach (var c in m) if (c != null && c.Final) return TaskConfig.AnyCleared(c);
        return false;
    }

        static Dictionary<string, ChartRating> Index()
    {
        var m = new Dictionary<string, ChartRating>(StringComparer.Ordinal);
        foreach (var c in Snap)
            if (c != null) m[ConstTable.GetKey(c.SongId, c.Difficulty)] = c;
        return m;
    }

        static int LevelOf(ChartRating c)
        => c.IsPM ? 4 : c.IsFC ? 3 : (c.PaceValue >= 0 ? 2 : 1);

        static int Level(string s)
    {
        switch ((s ?? "").Trim())
        {
            case "cleared": case "clear": case "pass": case "divecleared": return 2;
            case "fullink": case "fc": return 3;
            case "perfectdive": case "pd": return 4;
            default: return -1;
        }
    }

        static bool DiffOk(string want, string have)
        => string.IsNullOrEmpty(want) || want == ConstTable.AnyDiff
           || string.Equals(want, have, StringComparison.OrdinalIgnoreCase);

        static void WarnOnce(Def d, string msg)
    {
        lock (_lock)
        {
            if (!_warned.Add(d.Id)) return;
        }
        Plugin.Logger.LogWarning($"[Achieve] 成就「{d.Id}」：{msg}");
    }

        static readonly object _saveLock = new object();

    static void Save()
    {
        bool bad;
        lock (_lock) bad = _statesBad;
        if (bad)
        {

            Plugin.Logger.LogError("[Achieve] 上次读档失败且备份没成功 —— 这次不写 achievements_progress.json（防止把还能救的数据盖掉）。把文件修好后重启游戏可解除");
            return;
        }
        try
        {
            Directory.CreateDirectory(TaskConfig.Dir);
            Dictionary<string, State> snap;
            string eq;
            int sel;
            lock (_lock) { snap = _states; eq = _equipped; sel = _titleGradeSel; }

            var sb = new StringBuilder();
            sb.AppendLine("{");

            var head = new List<string>(2);
            if (eq.Length > 0) head.Add($"  \"{TitleKey}\": \"{Esc(eq)}\"");
            if (sel >= 1) head.Add($"  \"{TitleGradeKey}\": {sel}");
            for (int h = 0; h < head.Count; h++)
                sb.AppendLine(head[h] + (h < head.Count - 1 || snap.Count > 0 ? "," : ""));
            int i = 0;
            foreach (var kv in snap)
            {

                string un = kv.Value.Unseen ? ", \"unseen\": true" : "";
                string gr = kv.Value.Grade >= 2 ? $", \"grade\": {kv.Value.Grade}" : "";
                sb.Append($"  \"{Esc(kv.Key)}\": {{ \"at\": \"{Esc(kv.Value.At)}\"{un}{gr} }}");
                sb.AppendLine(++i < snap.Count ? "," : "");
            }
            sb.AppendLine("}");
            lock (_saveLock) System.IO.File.WriteAllText(StateFile, sb.ToString());
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[Achieve] achievements_progress.json 写入失败: {ex.Message}");
        }
    }

        static List<Def> LoadDisabledExtraDefs()
    {
        var keep = new List<Def>();
        try
        {
            string path = DefExtraFile;
            if (!System.IO.File.Exists(path)) return keep;
            using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path), TaskConfig.JSON_OPTS);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return keep;
            if (!doc.RootElement.TryGetProperty("list", out var arr) || arr.ValueKind != JsonValueKind.Array) return keep;
            foreach (var e in arr.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                var d = ParseDef(e);
                if (d.Id.Length > 0 && d.Disabled) keep.Add(d);
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[Achieve] 读旧 extra 里的停用条目失败（这次保存可能把它们丢掉）: {ex.Message}");
        }
        return keep;
    }

        public static string SaveExtra(List<Def> rows)
    {
        if (rows == null) return "列表是空的";
        try
        {

            var all = new List<Def>(rows);
            foreach (var d in LoadDisabledExtraDefs())
                if (!all.Exists(x => x != null && string.Equals((x.Id ?? "").Trim(), d.Id, StringComparison.Ordinal)))
                    all.Add(d);

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"_说明\": \"成就定义覆盖 / 追加文件。同 id 覆盖 achievements.json 里那一条（启动日志里会记一笔）；新 id 就是追加一条。手改也行，改完重开游戏生效。\",");
            sb.AppendLine("  \"_字段\": \"同 achievements.json：id/name/desc/type 必写；cat 是面板上的分类（收集 / 评级 / 课题 / 其他，也可以自己编一个）；minGrade = task 的档位下限（C / B / A / S / S+）；grades 是等级阶梯（第 1 条 = 第 2 级，第 1 级 = 基础判据本身，空字段 = 继承基础，最多 4 条 = 第 5 级为隐藏级）；disabled = 停用这一条（同 id 压掉基础那份，面板里一条都不出现）\",");
            sb.AppendLine("  \"list\": [");
            for (int i = 0; i < all.Count; i++)
            {
                var d = all[i];
                string id = (d?.Id ?? "").Trim();
                if (id.Length == 0) return "有一条的 id 是空的";
                if (!seen.Add(id)) return $"id「{id}」在列表里重复了";
                string type = (d.Type ?? "").Trim().ToLowerInvariant();
                if (type.Length == 0) return $"「{id}」没写 type（判据）";

                sb.Append("    ").Append(DefJson(d, id, type));
                sb.AppendLine(i < all.Count - 1 ? "," : "");
            }
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            Directory.CreateDirectory(TaskConfig.Dir);
            System.IO.File.WriteAllText(DefExtraFile, sb.ToString());
            Plugin.Logger.LogInfo($"[Achieve] achievements_extra.json 已保存 {all.Count} 条（其中停用 {all.Count - rows.Count} 条原样留住）→ {DefExtraFile}");
            return null;
        }
        catch (Exception ex)
        {
            return "写入失败：" + ex.Message;
        }
    }

        static string DefJson(Def d, string id, string type)
    {
        var sb = new StringBuilder();
        sb.Append("{ \"id\": \"").Append(Esc(id)).Append('"');
        if ((d.Name ?? "").Length > 0) sb.Append(", \"name\": \"").Append(Esc(d.Name)).Append('"');
        if ((d.Desc ?? "").Length > 0) sb.Append(", \"desc\": \"").Append(Esc(d.Desc)).Append('"');
        sb.Append(", \"type\": \"").Append(Esc(type)).Append('"');
        string diff = (d.Diff ?? "").Trim();
        if (diff.Length > 0 && diff != ConstTable.AnyDiff) sb.Append(", \"diff\": \"").Append(Esc(diff)).Append('"');
        if ((d.Need ?? "").Length > 0) sb.Append(", \"need\": \"").Append(Esc(d.Need)).Append('"');
        if ((d.Clear ?? "").Length > 0) sb.Append(", \"clear\": \"").Append(Esc(d.Clear)).Append('"');
        if ((d.Song ?? "").Length > 0) sb.Append(", \"song\": \"").Append(Esc(d.Song)).Append('"');
        if ((d.Task ?? "").Length > 0) sb.Append(", \"task\": \"").Append(Esc(d.Task)).Append('"');
        string mg = (d.MinGrade ?? "").Trim();
        if (mg.Length > 0) sb.Append(", \"minGrade\": \"").Append(Esc(mg)).Append('"');
        if (d.Value != 0) sb.Append(", \"value\": ").Append(d.Value.ToString("G9", CultureInfo.InvariantCulture));
        if (d.Hidden) sb.Append(", \"hidden\": true");
        if (d.Disabled) sb.Append(", \"disabled\": true");
        string cat = (d.Cat ?? "").Trim();
        if (cat.Length > 0) sb.Append(", \"cat\": \"").Append(Esc(cat)).Append('"');

        int from = d.FromLevel;
        int mgMax = 1 + (d.Grades?.Count ?? 0);
        if (from < 1) from = 1;
        if (from > mgMax) from = mgMax;
        if (from > 1) sb.Append(", \"fromLevel\": ").Append(from);
        if (d.Grades != null && d.Grades.Count > 0)
        {
            sb.Append(", \"grades\": [");
            for (int i = 0; i < d.Grades.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(RungJson(d.Grades[i]));
            }
            sb.Append(" ]");
        }
        sb.Append(" }");
        return sb.ToString();
    }

        static string RungJson(Rung r)
    {
        r ??= new Rung();
        var sb = new StringBuilder("{ ");
        bool first = true;
        void S(string key, string val)
        {
            if (string.IsNullOrEmpty(val)) return;
            if (!first) sb.Append(", ");
            first = false;
            sb.Append('"').Append(key).Append("\": \"").Append(Esc(val)).Append('"');
        }
        S("desc", r.Desc);
        S("diff", r.Diff);
        S("need", r.Need);
        S("clear", r.Clear);
        S("song", r.Song);
        S("task", r.Task);
        if (r.Value.HasValue)
        {
            if (!first) sb.Append(", ");
            first = false;
            sb.Append("\"value\": ").Append(r.Value.Value.ToString("G9", CultureInfo.InvariantCulture));
        }
        S("minGrade", r.MinGrade);
        sb.Append(" }");
        return sb.ToString();
    }

        public static List<Def> All => _defs;

        public static int MaxGrade(Def d)
        => d?.Grades == null ? 1 : Math.Min(5, 1 + d.Grades.Count);

        public static bool HasLadder(Def d) => d?.Grades != null && d.Grades.Count > 0;

        public static int FromLevel(Def d)
    {
        int m = MaxGrade(d);
        int f = d?.FromLevel ?? 1;
        if (f < 1) f = 1;
        return f > m ? m : f;
    }

        public static int GradeOf(Def d)
    {
        if (d == null || !_states.TryGetValue(d.Id, out var s)) return 0;
        int g = s.Grade < 1 ? 1 : s.Grade;

        int f = FromLevel(d);
        if (g < f) g = f;
        int m = MaxGrade(d);
        return g > m ? m : g;
    }

        public static int PublicGrade(Def d)
    {
        int m = MaxGrade(d);
        return m >= 5 && GradeOf(d) < 5 ? 4 : m;
    }

        public static Def LevelDef(Def b, int lv)
    {
        if (b == null) return null;
        var gs = b.Grades;
        if (gs == null || lv <= 1 || lv - 2 >= gs.Count) return b;
        var r = gs[lv - 2];
        if (r == null) return b;
        var d = new Def
        {
            Id = b.Id, Name = b.Name, Desc = b.Desc, Type = b.Type,
            Diff = b.Diff, Need = b.Need, Clear = b.Clear, Song = b.Song,
            Task = b.Task, Hidden = b.Hidden, Cat = b.Cat,
            MinGrade = b.MinGrade, Value = b.Value, FromLevel = b.FromLevel,
        };

        if (r.Diff.Length > 0) d.Diff = r.Diff;
        if (r.Need.Length > 0) d.Need = r.Need;
        if (r.Clear.Length > 0) d.Clear = r.Clear;
        if (r.Song.Length > 0) d.Song = r.Song;
        if (r.Task.Length > 0) d.Task = r.Task;
        if (r.MinGrade.Length > 0) d.MinGrade = r.MinGrade;
        if (r.Value.HasValue) d.Value = r.Value.Value;
        return d;
    }

        public static string DescribeRung(Def d, int lv)
    {
        if (d == null) return "";
        if (lv <= 1) return Describe(d);
        var gs = d.Grades;
        if (gs != null && lv - 2 < gs.Count)
        {
            var r = gs[lv - 2];
            if (r != null && r.Desc.Length > 0) return r.Desc;
        }
        return Describe(LevelDef(d, lv));
    }

        public static Progress ProgressNext(Def d)
    {
        if (d == null) return default;
        int cur = GradeOf(d);

        if (cur <= 0)
        {
            int first = FromLevel(d);
            return first <= 1 ? Eval(d) : Eval(LevelDef(d, first));
        }
        int next = cur + 1;
        if (next > PublicGrade(d)) return new Progress { Ok = true, Now = 1, Need = 1, Note = "" };
        return Eval(LevelDef(d, next));
    }

        public static string GradeRoman(int g)
    {
        switch (g)
        {
            case 1: return "I";
            case 2: return "II";
            case 3: return "III";
            case 4: return "IV";
            case 5: return "V";
            default: return "";
        }
    }

        public static int GradeArgb(int g)
    {
        switch (g)
        {
            case 1: return unchecked((int)0xFFC8CDD6);
            case 2: return unchecked((int)0xFFA3D6FF);
            case 3: return unchecked((int)0xFFFFC24B);
            case 4: return unchecked((int)0xFFC9B4FF);
            case 5: return unchecked((int)0xFFFF6B8B);
            default: return unchecked((int)0xFFF0C24B);
        }
    }

        public static bool IsFromBase(string id) => id != null && _baseIds.Contains(id);

        public static bool IsFromExtra(string id) => id != null && _extraIds.Contains(id);

        public static bool IsGenerated(string id) => id != null && _genIds.Contains(id);

    public static State GetState(string id)
        => id != null && _states.TryGetValue(id, out var s) ? s : null;

    public static bool IsUnlocked(string id) => id != null && _states.ContainsKey(id);

        public static int UnlockedCount
    {
        get
        {
            int n = 0;
            var ds = _defs; var st = _states;
            foreach (var d in ds) if (st.ContainsKey(d.Id)) n++;
            return n;
        }
    }

        public static bool IsVisible(Def d)
        => d != null && (!d.Hidden || IsUnlocked(d.Id));

        public static int CountedTotal
    {
        get
        {
            int n = 0;
            var ds = _defs; var st = _states;
            foreach (var d in ds)
                if (d != null && (!d.Hidden || st.ContainsKey(d.Id))) n++;
            return n;
        }
    }

        public static int StatesRevision => _statesRev;

        public static int UnseenCount
    {
        get
        {
            int n = 0;
            var st = _states;
            foreach (var kv in st) if (kv.Value.Unseen) n++;
            return n;
        }
    }

        public static bool MarkSeen(string id)
    {
        if (id == null) return false;
        lock (_lock)
        {
            if (!_states.TryGetValue(id, out var s) || !s.Unseen) return false;
            var next = new Dictionary<string, State>(_states);

            next[id] = new State { At = s.At, Grade = s.Grade, Unseen = false };
            _states = next;
            _statesRev++;
        }
        Save();
        return true;
    }

        public static string EquippedId => _equipped;

        public static Def EquippedDef
    {
        get
        {
            string id = _equipped;
            if (string.IsNullOrEmpty(id)) return null;
            var ds = _defs;
            foreach (var d in ds) if (d != null && d.Id == id) return d;
            return null;
        }
    }

        public static bool IsEquipped(string id)
        => !string.IsNullOrEmpty(id) && id == _equipped;

        public static bool SetEquipped(string id)
    {
        string want = (id ?? "").Trim();
        if (want.Length > 0 && !IsUnlocked(want)) return false;

        lock (_lock)
        {
            if (_equipped == want) return true;
            _equipped = want;
        }
        Save();
        Plugin.Logger.LogInfo(want.Length == 0
            ? "[Achieve] 称号已卸下"
            : $"[Achieve] 称号已装备：{want}");
        return true;
    }

        public static int TitleGradeChoice => _titleGradeSel;

        public static int TitleDisplayGrade(Def d)
    {
        int got = GradeOf(d);
        if (got <= 0) return 0;
        int sel = _titleGradeSel;

        return sel >= 1 && sel < got && sel >= FromLevel(d) ? sel : got;
    }

        public static bool SetTitleGrade(int grade)
    {
        int want = grade < 1 ? 0 : (grade > 5 ? 5 : grade);
        if (want >= 1)
        {
            var eq = EquippedDef;
            int got = GradeOf(eq);

            if (got <= 0 || want > got || want < FromLevel(eq)) return false;
        }

        lock (_lock)
        {
            if (_titleGradeSel == want) return true;
            _titleGradeSel = want;
        }
        Save();
        Plugin.Logger.LogInfo(want <= 0
            ? "[Achieve] 称号显示等级：自动（跟着拿到的最高级走）"
            : $"[Achieve] 称号显示等级：{GradeRoman(want)}（{want} 级）");
        return true;
    }

        public static Progress ProgressOf(Def d)
    {
        if (d == null) return default;
        if (IsUnlocked(d.Id)) return new Progress { Ok = true, Now = 1, Need = 1, Note = "" };
        return Eval(d);
    }

        public static string Describe(Def d)
    {
        string diff = (d.Diff == ConstTable.AnyDiff || d.Diff.Length == 0) ? "" : DiffShort(d.Diff) + " ";
        switch (d.Type)
        {
            case "clear":
            {

                string lv = LevelName(d.Clear.Length > 0 ? d.Clear : d.Need);
                return (d.Song.Length > 0
                           ? $"「{SongName(d.Song)}」的谱面拿到 {lv}"
                           : $"任意谱面拿到 {lv}")
                     + (diff.Length > 0 ? $"（限 {diff.Trim()}）" : "");
            }
            case "all":   return $"{diff}全部谱面都拿到 {LevelName(d.Need)}";
            case "count": return $"{diff}拿到 {LevelName(d.Need)} 的谱面累计 {d.Value:0} 张";
            case "rating": return $"评级达到 {d.Value:F2}";
            case "exact": return $"EXACT 值达到 {d.Value:0}";
            case "score": return $"单曲分数达到 {d.Value:0}";
            case "task":
            {

                string mg = (d.MinGrade ?? "").Trim();
                switch ((d.Task ?? "").Trim().ToLowerInvariant())
                {
                    case "all_tiers":  return mg.Length == 0 ? "全部段位通过" : $"全部段位通过（每段档位 ≥ {TaskProgress.GradeWord(mg)}）";
                    case "all_themes": return mg.Length == 0 ? "全部主题通过" : $"全部主题通过（每主题档位 ≥ {TaskProgress.GradeWord(mg)}）";
                    case "final":      return "通过终局课题";
                    default:           return DescribeTaskKey((d.Task ?? "").Trim(), mg);
                }
            }
            default: return $"（认不出的判据：{d.Type}）";
        }
    }

        static string DescribeTaskKey(string key, string mg)
    {
        string baseKey = key, diff = "";
        int at = key.IndexOf('@');
        if (at > 0) { baseKey = key.Substring(0, at); diff = key.Substring(at + 1); }

        var c = TaskConfig.FindAny(baseKey);
        string name = c != null && !string.IsNullOrEmpty(c.Name) ? c.Name : baseKey;
        string s = $"课题「{name}」通过";
        if (diff.Length > 0) s += $"（{DiffShort(diff)}）";
        else if (c != null && c.Switchable) s += "（任意难度）";
        if (mg.Length > 0) s += $"（档位 ≥ {TaskProgress.GradeWord(mg)}）";
        return s;
    }

        static string LevelName(string lv)
    {
        switch (Level(lv))
        {
            case 2: return "通关（Clear）";
            case 3: return "全连（FL）";
            case 4: return "全完美（PD）";
            default: return string.IsNullOrEmpty(lv) ? "通关（Clear）" : $"「{lv}?」";
        }
    }

        static string SongName(string songId)
    {
        string n = SongInfoCache.GetName(songId);
        return string.IsNullOrEmpty(n) ? songId : n;
    }

        static string DiffShort(string diff)
    {
        string s = TaskConfig.DiffShort(diff);
        return string.IsNullOrEmpty(s) ? diff : s;
    }

        const string SEED = @"{
  ""_说明"": ""成就定义。改完重开游戏生效。这个文件插件只读，不会回写。"",
  ""_字段"": ""id 唯一英文标识（存档按它存，改 id = 丢掉这条的解锁记录） / name 显示名 / desc 描述 / type 判据 / hidden true = 解锁前整条不显示、也不计入总数（解锁才现身）"",
  ""_判据"": ""clear 某张谱面达到某等级 / all 某难度的全部谱面都达到 / count N 张达到 / rating 评级到 / exact EXACT 到 / score 单曲分数到 / task 课题通过"",
  ""_等级"": ""cleared（也认 clear/pass） / fullink（也认 fc） / perfectdive（也认 pd）"",
  ""_难度"": ""* 或留空 = 不限；其余照抄难度名 Minimal / Evolved / Ultimate / Forbidden"",
  ""_课题"": ""task 写课题 Key（rank7 / theme1 ...），或者 all_tiers（段位全通） / all_themes（主题全通） / final（终局课题）"",
  ""_分级"": ""grades = 等级阶梯：第 1 条 = 第 2 级（第 1 级 = 基础判据本身），每条只写要覆盖的字段（desc/diff/need/clear/song/task/value/minGrade），空 = 继承基础，最多 4 条；第 5 级永远是隐藏级（拿到才现身）。minGrade = task 的档位下限（C / B / A / S / S+）"",

  ""list"": [
    { ""id"": ""first_pd"",     ""name"": ""初临神域"",   ""desc"": ""拿到第一个 PerfectDive"",              ""type"": ""clear"",  ""diff"": ""*"",         ""clear"": ""perfectdive"" },
    { ""id"": ""first_fbd_pd"", ""name"": ""禁忌之巅"",   ""desc"": ""在 Forbidden 谱面上拿到 PerfectDive"",  ""type"": ""clear"",  ""diff"": ""Forbidden"", ""clear"": ""perfectdive"" },
    { ""id"": ""fbd_all"",      ""name"": ""制霸·禁忌"", ""desc"": ""Forbidden 难度的全部谱面通关"",         ""type"": ""all"",    ""diff"": ""Forbidden"", ""need"": ""cleared"" },
    { ""id"": ""pd_10"",        ""name"": ""完美十回"",   ""desc"": ""十张谱面 PerfectDive"",               ""type"": ""count"",  ""diff"": ""*"",         ""need"": ""perfectdive"", ""value"": 10 },
    { ""id"": ""fc_50"",        ""name"": ""半百全连"",   ""desc"": ""五十张谱面 FullLink 及以上"",            ""type"": ""count"",  ""diff"": ""*"",         ""need"": ""fullink"",     ""value"": 50 },
    { ""id"": ""rating_12"",    ""name"": ""十二星"",     ""desc"": ""评级达到 12.00"",                   ""type"": ""rating"", ""value"": 12.0 },
    { ""id"": ""rating_13"",    ""name"": ""十三星"",     ""desc"": ""评级达到 13.00"",                   ""type"": ""rating"", ""value"": 13.0 },
    { ""id"": ""score_1m"",     ""name"": ""百万分"",     ""desc"": ""单曲分数达到 1,000,000"",              ""type"": ""score"",  ""value"": 1000000 },
    { ""id"": ""exact_100"",    ""name"": ""精确一百"",   ""desc"": ""EXACT 值达到 100"",                  ""type"": ""exact"",  ""value"": 100 },
    { ""id"": ""tier_all"",     ""name"": ""十段制霸"",   ""desc"": ""全部段位通过"",                       ""type"": ""task"",   ""task"": ""all_tiers"" },
    { ""id"": ""the_final"",    ""name"": ""神罚"",       ""desc"": ""通过终局课题"",                       ""type"": ""task"",   ""task"": ""final"", ""hidden"": true }
  ]
}";

        const string EXTRA_SEED = "";
}