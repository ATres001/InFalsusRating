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

public class Challenge
{
        public string Key = "";
        public string Kind = "rank";
        public int N = 1;
    public string Name = "";
        public string Note = "";

        public string Art = "";

        public string Cat = "";

        public bool Final = false;

        public bool Mv = true;

        public bool Bga = false;

        public List<TaskSong> Songs = new List<TaskSong>();
        public string PoolDiff = "";
        public int PoolMin = -1, PoolMax = -1;

        public int Count = 4;

        public double MinRating = 0;

        public bool NeedSongs = true;

        public long MinAvg = -1;
        public string MinGrade = "";

        public HpConfig Hp;

        public bool IsPool => Songs.Count == 0 && PoolDiff.Length > 0;

        public bool Switchable => Kind == "theme" && Songs.Count > 0;

        List<string> _diffChoices;

        public List<string> DiffChoices()
    {
        if (_diffChoices != null) return _diffChoices;

        var res = new List<string>();
        if (Switchable)
        {
            if (ConstTable.Count == 0)
            {

                res.AddRange(TaskConfig.DiffOrder);
            }
            else
            {
                foreach (var d in TaskConfig.DiffOrder)
                {
                    bool all = true;
                    foreach (var s in Songs)
                        if (!ConstTable.HasChart(s.Id, d)) { all = false; break; }
                    if (all) res.Add(d);
                }
            }
        }

        _diffChoices = res;
        return res;
    }
}