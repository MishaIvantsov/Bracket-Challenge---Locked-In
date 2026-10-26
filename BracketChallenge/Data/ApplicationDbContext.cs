using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BracketChallenge.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<RoundResult> RoundResults => Set<RoundResult>();
    public DbSet<Bracket> Brackets => Set<Bracket>();
    public DbSet<BracketPick> BracketPicks => Set<BracketPick>();
    public DbSet<League> Leagues => Set<League>();
    public DbSet<LeagueMember> LeagueMembers => Set<LeagueMember>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<Bracket>().HasIndex(x => x.UserId).IsUnique();      // one bracket per user
        b.Entity<League>().HasIndex(x => x.JoinCode).IsUnique();
        b.Entity<LeagueMember>().HasKey(x => new { x.LeagueId, x.UserId });
        b.Entity<BracketPick>().HasIndex(x => new { x.BracketId, x.Round, x.TeamId }).IsUnique();
        b.Entity<RoundResult>().HasIndex(x => new { x.Round, x.TeamId }).IsUnique();
        b.Entity<AppSetting>().HasKey(x => x.Key);

        // Placeholder teams for testing. Replace with the real field in January.
        var teams = new List<Team>();
        int id = 1;
        foreach (var conf in new[] { Conference.AFC, Conference.NFC })
            for (int seed = 1; seed <= 7; seed++)
                teams.Add(new Team { Id = id++, Name = $"{conf} Seed {seed}", Conference = conf, Seed = seed });
        b.Entity<Team>().HasData(teams);
    }
}