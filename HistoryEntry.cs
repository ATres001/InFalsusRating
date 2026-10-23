using System;

namespace InFalsusRating;

public class HistoryEntry
{
    public string SongId;
    public string Difficulty;
        public ulong Score;
        public int Clear;
        public ulong Seconds;

        public ResultDetail Detail;

        public bool TaskPlay;

        public bool ExamFail;

        public bool HasStats;

        public long Break = -1, Near = -1, Exact = -1, Shiny = -1;

        public int Combo = -1;
}
