using Xunit;
using BracketChallenge.Scoring;

public class ScoringServiceTests
{
    static Picks Make(int[] wc, int[] div, int[] conf, int? champ) =>
        new(new HashSet<int>(wc), new HashSet<int>(div), new HashSet<int>(conf), champ);

    static readonly Picks Actual = Make([1, 2, 3, 4, 5, 6], [1, 2, 3, 4], [1, 2], 1);

    [Fact]
    public void PerfectBracket_Scores14()
    {
        var score = ScoringService.Calculate(Actual, Actual);
        Assert.Equal(14, score.Total);
    }

    [Fact]
    public void EmptyBracket_Scores0()
    {
        var score = ScoringService.Calculate(Make([], [], [], null), Actual);
        Assert.Equal(0, score.Total);
    }

    [Fact]
    public void SuperBowlTeamsAndChampionCorrect_Scores4()
    {
        // Seahawks (10) vs Patriots (20), Seahawks win.
        var results = Make([], [], [10, 20], 10);
        var picks = Make([], [], [10, 20], 10);
        Assert.Equal(4, ScoringService.Calculate(picks, results).Total);
    }

    [Fact]
    public void WrongChampion_GetsNoBonus()
    {
        var results = Make([], [], [10, 20], 10);
        var picks = Make([], [], [10, 20], 20);
        Assert.Equal(2, ScoringService.Calculate(picks, results).Total);
    }

    [Fact]
    public void Tiebreaker_LaterRoundsBeatEarlierRounds()
    {
        // A: conference + champion = 4 points. B: 4 wild-card points = 4 points.
        var a = ScoringService.Calculate(Make([], [], [1, 2], 1), Actual);
        var b = ScoringService.Calculate(Make([1, 2, 3, 4], [], [], null), Actual);
        Assert.Equal(a.Total, b.Total);

        var sorted = new[] { b, a }.OrderBy(s => s, new LeaderboardComparer()).ToList();
        Assert.Same(a, sorted[0]);
    }
}