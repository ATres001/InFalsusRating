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

public class SettlementRow
{
    public string Title = "";
    public string Difficulty = "";
        public double Const = -1;
        public double Score = -1;
        public int Dive = 9999;
        public int Clear = -1;
        public bool Failed;

        public long Miss = -1, Near = -1, Perfect = -1, Shiny = -1;

        public int Combo = -1;

        public bool LiveStats;

        public ResultDetail Detail;
}