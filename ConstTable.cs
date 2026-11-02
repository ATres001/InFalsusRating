using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using BepInEx;

namespace InFalsusRating;

public static class ConstTable
{
    public const string AnyDiff = "*";

    private static Configuration _constTableConfig = null;

    private static Configuration ConstTableConfig
    {
        get => _constTableConfig ??= new();
        set => _constTableConfig = value;
    }

    private static Dictionary<string, double> _table = new Dictionary<string, double>();

    private static readonly Dictionary<string, double> _defaultsByDifficulty =
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            { "Minimal", 0 }, { "Evolved", 0 },
            { "Ultimate", 0 }, { "Forbidden", 0 }
        };

    public static string GetKey(string songId, string difficulty) => $"{songId}|{difficulty}";

    public static string Text(double c)
    {
        return c.ToString(ConstTableConfig.Format, CultureInfo.InvariantCulture);
    }

    public static float TextSizeScale() => ConstTableConfig.ConstFontSizeScale;

    public static double Get(string songId, string difficulty)
    {
        if (_table.TryGetValue(GetKey(songId, difficulty), out double exact)) return exact;
        if (_defaultsByDifficulty.TryGetValue(difficulty, out double def)) return def;
        return -1;
    }

    public static bool HasChart(int songId, string difficulty)
        => !string.IsNullOrEmpty(difficulty) && _table.ContainsKey(GetKey(songId.ToString(), difficulty));

    public static int Count => _table.Count;

    public static List<string> RowsOf(string difficulty)
    {
        var list = new List<string>();
        bool all = string.IsNullOrEmpty(difficulty) || difficulty == AnyDiff;
        foreach (var kv in _table)
        {
            if (all)
            {
                list.Add(kv.Key);
                continue;
            }

            int bar = kv.Key.LastIndexOf('|');
            if (bar < 0) continue;
            if (string.Equals(kv.Key.Substring(bar + 1), difficulty, StringComparison.OrdinalIgnoreCase))
                list.Add(kv.Key);
        }

        return list;
    }

    public static List<double> AllConstants()
    {
        var list = new List<double>(_table.Count);
        foreach (var kv in _table)
            if (kv.Value > 0)
                list.Add(kv.Value);
        return list;
    }

    public static List<KeyValuePair<string, double>> TopByDifficulty(string difficulty, int n)
    {
        var list = new List<KeyValuePair<string, double>>();
        foreach (var kv in _table)
        {
            int bar = kv.Key.LastIndexOf('|');
            if (bar < 0) continue;
            if (!string.Equals(kv.Key.Substring(bar + 1), difficulty, StringComparison.OrdinalIgnoreCase)) continue;
            list.Add(kv);
        }

        list.Sort((a, b) =>
        {
            int c = b.Value.CompareTo(a.Value);
            return c != 0 ? c : string.CompareOrdinal(a.Key, b.Key);
        });
        if (list.Count > n) list.RemoveRange(n, list.Count - n);
        return list;
    }

    public static List<KeyValuePair<string, double>> ByConstant(string difficulty, double constant, int n)
    {
        if (constant < 0) return TopByDifficulty(difficulty, n);

        var list = new List<KeyValuePair<string, double>>();
        foreach (var kv in _table)
        {
            int bar = kv.Key.LastIndexOf('|');
            if (bar < 0) continue;
            if (!string.Equals(kv.Key.Substring(bar + 1), difficulty, StringComparison.OrdinalIgnoreCase)) continue;
            if (kv.Value != constant) continue;
            list.Add(kv);
        }

        list.Sort((a, b) => CompareById(a.Key, b.Key));
        if (list.Count > n) list.RemoveRange(n, list.Count - n);
        return list;
    }

    static int CompareById(string a, string b)
    {
        int ia = IdOf(a), ib = IdOf(b);
        return ia != ib ? ia.CompareTo(ib) : string.CompareOrdinal(a, b);
    }

    public static List<KeyValuePair<string, double>> InRange(string difficulty, int min, int max)
        => InRange(difficulty, min, max, false);

    public static List<KeyValuePair<string, double>> InRange(string difficulty, int min, int max, bool ignoreUnlock)
    {
        bool any = difficulty == AnyDiff;
        var list = new List<KeyValuePair<string, double>>();
        foreach (var kv in _table)
        {
            int bar = kv.Key.LastIndexOf('|');
            if (bar < 0) continue;
            if (!any && !string.Equals(kv.Key.Substring(bar + 1), difficulty, StringComparison.OrdinalIgnoreCase))
                continue;
            if (kv.Value < min || kv.Value > max) continue;

            int id = IdOf(kv.Key);
            if (!SongInfoCache.Selectable(id)) continue;

            if (!ignoreUnlock && !SongUnlock.Unlocked(id, kv.Key.Substring(bar + 1))) continue;

            list.Add(kv);
        }

        list.Sort((a, b) => CompareById(a.Key, b.Key));
        return list;
    }

    public static List<TaskSong> Draw(string difficulty, int min, int max, int n)
    {
        var pool = InRange(difficulty, min, max);
        var res = new List<TaskSong>();
        var rnd = new Random();

        for (int i = 0; i < pool.Count && res.Count < n; i++)
        {
            int j = rnd.Next(i, pool.Count);
            var t = pool[i];
            pool[i] = pool[j];
            pool[j] = t;

            int bar = pool[i].Key.LastIndexOf('|');
            int id = IdOf(pool[i].Key);
            if (id == int.MaxValue || bar < 0) continue;
            res.Add(new TaskSong { Id = id, Diff = pool[i].Key.Substring(bar + 1) });
        }

        res.Sort((a, b) => ConstOf(a).CompareTo(ConstOf(b)));
        return res;
    }

    public static double ConstOf(TaskSong s)
        => s == null ? -1 : Get(s.Id.ToString(), s.Diff);

    static int IdOf(string key)
    {
        int bar = key.IndexOf('|');
        if (bar <= 0) return int.MaxValue;
        return int.TryParse(key.Substring(0, bar), out int id) ? id : int.MaxValue;
    }

    public static void Load()
    {
        try
        {
            string dir = Settings.ConfigRoot;
            string file = Path.Combine(dir, "const.json");
            Directory.CreateDirectory(dir);

            if (!File.Exists(file))
            {
                File.WriteAllText(file, "{\n  \"_说明\": \"键 = 歌曲id|难度，值 = 定数（可写小数）\"\n}\n");
                return;
            }

            string json = File.ReadAllText(file);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return;

            int count = 0;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Name.StartsWith("_"))
                {
                    if (!prop.Name.Equals("_config")) continue;
                    ConstTableConfig = prop.Value.Deserialize<Configuration>();
                }

                if (prop.Value.ValueKind == JsonValueKind.Number &&
                    prop.Value.TryGetDouble(out double v))
                {
                    _table[prop.Name] = v;
                    count++;
                }
            }

            Plugin.Logger.LogInfo($"[Const] 载入 {count} 条精确定数");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[Const] 读取失败: {ex.Message}");
        }
    }

    public static void SaveIfIncomplete(Dictionary<string, int> full)
    {
        if (full == null || full.Count == 0)
        {
            Plugin.Logger.LogWarning("[Const] 游戏数据为空，跳过生成");
            return;
        }

        if (_table.Count >= 10)
        {
            Plugin.Logger.LogInfo($"[Const] 已有 {_table.Count} 条定数，跳过自动生成");
            return;
        }

        try
        {
            string dir = Settings.ConfigRoot;
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "const.json");

            var ordered = full
                .OrderBy(kv =>
                {
                    var parts = kv.Key.Split('|');
                    return int.TryParse(parts[0], out var id) ? id : int.MaxValue;
                })
                .ThenBy(kv => kv.Key).ToArray();

            JsonObject configNode = JsonSerializer.SerializeToNode(ConstTableConfig)!.AsObject();
            using (var fileWriter = new FileStream(file, FileMode.Create, FileAccess.Write))
            {
                JsonWriterOptions jsonWriterOptions = new JsonWriterOptions()
                {
                    Indented = true
                };
                JsonSerializerOptions jsonSerializerOptions = new JsonSerializerOptions()
                {
                    WriteIndented = true
                };
                using var jsonWriter = new Utf8JsonWriter(fileWriter, jsonWriterOptions);
                jsonWriter.WriteStartObject();
                jsonWriter.WritePropertyName("_config");
                configNode.WriteTo(jsonWriter, jsonSerializerOptions);
                foreach (var pair in ordered)
                {
                    jsonWriter.WritePropertyName(pair.Key);
                    jsonWriter.WriteRawValue(Text(pair.Value));
                }

                jsonWriter.WriteEndObject();
            }

            _table = ordered.ToDictionary(kv => kv.Key, kv => (double)kv.Value);

            Plugin.Logger.LogInfo($"[Const] 已生成 {full.Count} 条定数: {file}");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[Const] 自动生成失败: {ex.Message}");
        }
    }

    public class Configuration : IJsonOnDeserialized
    {
        public string Format { get; set; } = "0.###";
        public float ConstFontSizeScale { get; set; } = 1;

        public void OnDeserialized()
        {
            Configuration configuration = new Configuration();
            if (!CheckFormat(Format))
            {
                Plugin.Logger.LogWarning($"[Const] 警告：{Format}不是一个合法的double型格式样板，将改用默认值");
                Format = configuration.Format;
            }
            else
            {
                Plugin.Logger.LogInfo($"[Const] 输出的定数格式将采用{Format}作为格式 ");
            }

            if (ConstFontSizeScale <= 0f) ConstFontSizeScale = 1f;
        }

        private static bool CheckFormat(string newFormat)
        {
            if (string.IsNullOrWhiteSpace(newFormat)) return false;
            const double test = 114.514;
            string str;
            try
            {
                str = test.ToString(newFormat, CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
                return false;
            }

            if (!double.TryParse(str, out _))
            {
                return false;
            }

            return true;
        }
    }
}