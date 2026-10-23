using ifapp.Game.Data;

namespace InFalsusRating;

public sealed class ResultDetail
{

    public const int BNone = 0;
    public const int BMiss = 1;
    public const int BNearEarly = 2;
    public const int BNearLate = 3;
    public const int BPerfectEarly = 4;
    public const int BPerfectLate = 5;
    public const int BShiny = 6;
    public const int BucketCount = 7;

        public ulong Score;

        public int Clear;

        public int Lamp;

        public int Pace;

        public int Combo;

        public ulong Seconds;

    public readonly int[] Tap = new int[BucketCount];
    public readonly int[] Hold = new int[BucketCount];
    public readonly int[] Sky = new int[BucketCount];
    public readonly int[] Flick = new int[BucketCount];

        public static ResultDetail From(in GameResultV4 r, ulong seconds)
    {
        var d = new ResultDetail
        {
            Score = r.PlayerScore,
            Clear = (int)r.CalculatedResultClear,
            Lamp = (int)r.Lamp,
            Pace = r.CalculatedPaceValue,
            Combo = r.MaxCombo,
            Seconds = seconds,
        };
        Fill(d.Tap, r.TapCounts);
        Fill(d.Hold, r.HoldCounts);
        Fill(d.Sky, r.SkyAreaCounts);
        Fill(d.Flick, r.FlickCounts);
        return d;
    }

    static void Fill(int[] dst, in JudgementTypeCount j)
    {
        dst[BNone] = j.None;
        dst[BMiss] = j.Miss;
        dst[BNearEarly] = j.NearEarly;
        dst[BNearLate] = j.NearLate;
        dst[BPerfectEarly] = j.PerfectEarly;
        dst[BPerfectLate] = j.PerfectLate;
        dst[BShiny] = j.Shiny;
    }

        public int Sum(int bucket) => Tap[bucket] + Hold[bucket] + Sky[bucket] + Flick[bucket];

        public int SumAll()
    {
        int s = 0;
        for (int b = 0; b < BucketCount; b++) s += Sum(b);
        return s;
    }
}
