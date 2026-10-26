namespace BracketChallenge.Data;

public enum Conference { AFC, NFC }

// WildCard/Divisional/Conference = teams that ADVANCE out of that round.
// SuperBowl = the champion.
public enum Round { WildCard = 1, Divisional = 2, Conference = 3, SuperBowl = 4 }

public class Team
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public Conference Conference { get; set; }
    public int Seed { get; set; }
}

// The real results: one row per team that advanced out of a round.
public class RoundResult
{
    public int Id { get; set; }
    public Round Round { get; set; }
    public int TeamId { get; set; }
    public Team? Team { get; set; }
}

public class Bracket
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public ApplicationUser? User { get; set; }
    public DateTime SubmittedAtUtc { get; set; }
    public List<BracketPick> Picks { get; set; } = [];
}

public class BracketPick
{
    public int Id { get; set; }
    public int BracketId { get; set; }
    public Bracket? Bracket { get; set; }
    public Round Round { get; set; }
    public int TeamId { get; set; }
    public Team? Team { get; set; }
}

public class League
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string JoinCode { get; set; } = "";
    public List<LeagueMember> Members { get; set; } = [];
}

public class LeagueMember
{
    public int LeagueId { get; set; }
    public League? League { get; set; }
    public string UserId { get; set; } = "";
    public ApplicationUser? User { get; set; }
}