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

public static class SongUnlock
{
        static volatile HashSet<int> _seen = new HashSet<int>();

        static volatile HashSet<string> _exists = new HashSet<string>();

        static volatile HashSet<string> _avail = new HashSet<string>();

        static volatile bool _trusted;

        static volatile string _why = "还没扫过（进一次选歌页就有了）";

    public static int SeenSongs => _seen.Count;
    public static int ChartCount => _exists.Count;
    public static int UnlockedCharts => _avail.Count;

        public static bool Known => _trusted && _avail.Count > 0;

        public static string Key(int songId, string diff)
        => songId + "|" + (diff ?? "").ToLowerInvariant();

        public static bool Unlocked(int songId, string diff)
    {
        var avail = _avail;
        if (!_trusted || avail.Count == 0) return true;
        if (!_seen.Contains(songId)) return true;
        string key = Key(songId, diff);
        if (!_exists.Contains(key)) return true;
        return avail.Contains(key);
    }

        public static int LockedCount(List<TaskSong> songs)
    {
        if (songs == null || !Known) return 0;
        int n = 0;
        foreach (var s in songs) if (!Unlocked(s.Id, s.Diff)) n++;
        return n;
    }

        public static string StateText()
    {
        if (!_trusted) return $"⚠ 不认这份数据，一律放行：{_why}";
        if (_avail.Count == 0) return $"⚠ 一张已解锁的谱面都没读到，一律放行：{_why}";
        return $"曲 {_seen.Count} / 谱 {_exists.Count}，其中已解锁 {_avail.Count}";
    }

        public static void Publish(HashSet<int> seen, HashSet<string> exists, HashSet<string> avail,
                               bool trusted, string why)
    {
        if (seen == null)   seen = new HashSet<int>();
        if (exists == null) exists = new HashSet<string>();
        if (avail == null)  avail = new HashSet<string>();

        var seenAll = new HashSet<int>(_seen);         seenAll.UnionWith(seen);
        var existsAll = new HashSet<string>(_exists);  existsAll.UnionWith(exists);

        var availAll = new HashSet<string>(_avail);    availAll.UnionWith(avail);

        _seen = seenAll;
        _exists = existsAll;

        _avail = trusted ? availAll : new HashSet<string>();
        _trusted = trusted;
        _why = why;
    }
}