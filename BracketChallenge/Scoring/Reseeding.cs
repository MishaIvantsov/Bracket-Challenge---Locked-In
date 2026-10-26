namespace BracketChallenge.Scoring;

public static class Reseeding
{
    // Wild-card games in one conference: (home seed, away seed). The 1 seed has a bye.
    public static readonly (int Home, int Away)[] WildCardGames = [(2, 7), (3, 6), (4, 5)];

    /// Takes the 3 wild-card winners (seeds 2-7) and returns the 2 divisional games.
    /// The 1 seed plays the lowest remaining seed; the other two play each other.
    public static (int Home, int Away)[] Divisional(IReadOnlyCollection<int> wildCardWinners)
    {
        if (wildCardWinners.Count != 3)
            throw new ArgumentException("Need exactly 3 wild-card winners.");

        // Exactly one winner must come from each wild-card game.
        foreach (var (home, away) in WildCardGames)
        {
            int fromGame = wildCardWinners.Count(s => s == home || s == away);
            if (fromGame != 1)
                throw new ArgumentException($"Need exactly one winner from the {home} vs {away} game.");
        }

        var seeds = wildCardWinners.Append(1).OrderBy(s => s).ToList(); // 4 seeds, best first
        return [(seeds[0], seeds[3]), (seeds[1], seeds[2])];
    }

    /// Takes the 2 divisional winners and returns the conference championship game.
    public static (int Home, int Away) ConferenceChampionship(IReadOnlyCollection<int> divisionalWinners)
    {
        if (divisionalWinners.Count != 2)
            throw new ArgumentException("Need exactly 2 divisional winners.");

        var seeds = divisionalWinners.OrderBy(s => s).ToList();
        if (seeds[0] == seeds[1])
            throw new ArgumentException("Divisional winners must be different teams.");

        return (seeds[0], seeds[1]);
    }
}