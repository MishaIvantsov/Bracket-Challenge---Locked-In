namespace BracketChallenge.Scoring;

/// Teams (by id) that advance out of each round, plus the champion.
/// Used for both a user's picks and the real results.
public record Picks(
    IReadOnlySet<int> WildCard,     // 6 wild-card winners
    IReadOnlySet<int> Divisional,   // 4 divisional winners
    IReadOnlySet<int> Conference,   // 2 conference winners (the Super Bowl teams)
    int? Champion                   // Super Bowl winner
);                 

public record Score(int WildCard, int Divisional, int Conference, bool ChampionCorrect)
{
    public int ChampionBonus => ChampionCorrect ? 2 : 0;
    public int Total => WildCard + Divisional + Conference + ChampionBonus;
}

public static class ScoringService
{
    public static Score Calculate(Picks picks, Picks results) => new(
        picks.WildCard.Intersect(results.WildCard).Count(),
        picks.Divisional.Intersect(results.Divisional).Count(),
        picks.Conference.Intersect(results.Conference).Count(),
        results.Champion is not null && picks.Champion == results.Champion
    );
}

/// Sorts the best score first: total, then champion, conference, divisional, wild card.
public class LeaderboardComparer : IComparer<Score>
{
    public int Compare(Score? x, Score? y)
    {
        if (x is null || y is null) return 0;
        int c = y.Total.CompareTo(x.Total);
        if (c != 0) return c;
        c = y.ChampionCorrect.CompareTo(x.ChampionCorrect);
        if (c != 0) return c;
        c = y.Conference.CompareTo(x.Conference);
        if (c != 0) return c;
        c = y.Divisional.CompareTo(x.Divisional);
        if (c != 0) return c;
        return y.WildCard.CompareTo(x.WildCard);
    }
}