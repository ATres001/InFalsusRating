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

public class SettlementData
{
        public string Title = "";

        public bool Final;

        public string Rank = "";
        public bool Certified;
        public string Verdict = "";

    public double AverageScore;

    public int TotalSongs;
    public int PlayedSongs;
    public int FullLinkCount;
    public int PerfectDiveCount;

    public List<SettlementRow> Rows = new List<SettlementRow>();

        public bool Live;

        public bool Ended;

        public string Phase = "";

        public string EndText = "";

        public int CurrentIndex;

        public int CurrentRow = -1;

        public int DoneSongs;

    public int Hp, HpMax, HpPass, HpMin, HpNotch;
        public bool HpNoLoss;

    public long LiveShiny, LiveExact, LiveNear, LiveBreak;
    public bool LiveJudgesOk;
        public int LiveCombo = -1;
        public int LiveComboMax;
}