using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace BracketChallenge.Data;

public class PlayoffSyncService(
    IServiceScopeFactory scopes,
    IHttpClientFactory httpFactory,
    IConfiguration config,
    ILogger<PlayoffSyncService> log)
{
    static readonly SemaphoreSlim Gate = new(1, 1);

    // ESPN postseason week numbers. Week 4 is the Pro Bowl and is ignored.
    static readonly Dictionary<int, Round> Rounds = new()
    {
        [1] = Round.WildCard,
        [2] = Round.Divisional,
        [3] = Round.Conference,
        [5] = Round.SuperBowl
    };

    record Competitor(int EspnId, bool Winner);
    record Game(int Week, bool Completed, DateTime StartUtc, List<Competitor> Teams);
    record StandingTeam(int EspnId, string Name, Conference Conference, int Seed);

    /// Call from pages. Uses its own database scope, so a page closing can't break it.
    /// Does nothing if another sync is running or one ran recently.
    public async Task EnsureFreshAsync()
    {
        if (!config.GetValue<bool>("Sync:Enabled", true)) return;
        if (!await Gate.WaitAsync(0)) return;
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var minInterval = TimeSpan.FromMinutes(config.GetValue<int>("Sync:MinMinutes", 10));
            var last = await GetAsync(db, "LastSyncUtc");
            if (DateTime.TryParse(last, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t)
                && DateTime.UtcNow - t < minInterval) return;

            try { await SyncAsync(db); }
            catch (Exception ex) { log.LogWarning(ex, "Playoff sync failed"); }

            await SetAsync(db, "LastSyncUtc", DateTime.UtcNow.ToString("O"));
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Playoff sync could not run");
        }
        finally { Gate.Release(); }
    }

    async Task SyncAsync(ApplicationDbContext db)
    {
        var start = DateTime.Parse(config["Sync:PlayoffStart"] ?? "2027-01-05", CultureInfo.InvariantCulture);
        var season = config["Sync:Season"] ?? "2026";
        log.LogInformation("Sync starting: season {Season}, playoffs from {Start:d}", season, start);

        if (DateTime.UtcNow < start.AddDays(-10))   // too early, don't bother ESPN
        {
            log.LogInformation("Sync skipped: too early for this playoff window.");
            return;
        }

        var games = await FetchGamesAsync(season);
        log.LogInformation("Sync fetched {Count} playoff games ({Wc} wild card).",
            games.Count, games.Count(g => g.Week == 1));

        if (await GetAsync(db, "FieldLoaded") != "1")
            if (!await TryLoadFieldAsync(db, games, season)) return;

        await RecordResultsAsync(db, games);
    }

    // ---------- scoreboard ----------
    async Task<List<Game>> FetchGamesAsync(string season)
    {
        var http = httpFactory.CreateClient("espn");
        var games = new List<Game>();

        foreach (var week in Rounds.Keys)
        {
            var url = $"site/v2/sports/football/nfl/scoreboard?seasontype=3&week={week}&dates={season}";
            int before = games.Count;
            int skipped = 0;
            try
            {
                using var doc = JsonDocument.Parse(await http.GetStringAsync(url));
                if (!doc.RootElement.TryGetProperty("events", out var events))
                {
                    log.LogInformation("Week {Week}: response had no 'events'.", week);
                    continue;
                }

                foreach (var e in events.EnumerateArray())
                {
                    // Skip games from another season (ESPN can return the upcoming schedule).
                    if (e.TryGetProperty("season", out var s) && s.TryGetProperty("year", out var y)
                        && y.GetInt32().ToString() != season)
                    {
                        skipped++;
                        continue;
                    }

                    bool completed = e.GetProperty("status").GetProperty("type").GetProperty("completed").GetBoolean();
                    var startUtc = DateTime.Parse(e.GetProperty("date").GetString()!, CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

                    var teams = e.GetProperty("competitions")[0].GetProperty("competitors").EnumerateArray()
                        .Select(c => new Competitor(
                            int.Parse(c.GetProperty("team").GetProperty("id").GetString()!),
                            c.TryGetProperty("winner", out var w) && w.ValueKind == JsonValueKind.True))
                        .ToList();

                    // Skip "TBD" placeholder games (ESPN uses negative team ids for them).
                    if (teams.Any(t => t.EspnId <= 0))
                    {
                        skipped++;
                        continue;
                    }

                    games.Add(new Game(week, completed, startUtc, teams));
                }
            }
            catch (Exception ex)
            {
                // A round that isn't published yet can fail. Keep the rounds we did get.
                log.LogInformation("Scoreboard for week {Week} unavailable: {Message}", week, ex.Message);
            }
            log.LogInformation("Week {Week}: {Count} games ({Skipped} skipped as wrong season or TBD).",
                week, games.Count - before, skipped);
        }
        return games;
    }

    // ---------- playoff field ----------
    async Task<bool> TryLoadFieldAsync(ApplicationDbContext db, List<Game> games, string season)
    {
        var wc = games.Where(g => g.Week == 1).ToList();
        if (wc.Count != 6)                                   // field not announced yet
        {
            log.LogInformation("Field not loaded: found {Count} wild-card games (need 6).", wc.Count);
            return false;
        }

        var http = httpFactory.CreateClient("espn");
        using var doc = JsonDocument.Parse(await http.GetStringAsync($"v2/sports/football/nfl/standings?season={season}"));

        var standings = new List<StandingTeam>();
        Walk(doc.RootElement, null, standings);

        bool unique = standings.GroupBy(s => (s.Conference, s.Seed)).All(g => g.Count() == 1);
        if (standings.Count != 14 || !unique)
        {
            log.LogWarning("Standings gave {Count} seeded teams (expected 14, unique). Field not loaded.", standings.Count);
            return false;
        }

        // Cross-check against the scoreboard before writing anything.
        var byId = standings.ToDictionary(s => s.EspnId);
        var wcIds = wc.SelectMany(g => g.Teams.Select(t => t.EspnId)).ToHashSet();
        var expected = standings.Where(s => s.Seed >= 2).Select(s => s.EspnId).ToHashSet();

        var notInStandings = wcIds.Where(id => !byId.ContainsKey(id)).ToList();
        var badGames = wc.Where(g => g.Teams.Count != 2
                || g.Teams.Any(t => !byId.ContainsKey(t.EspnId))
                || g.Teams.Sum(t => byId[t.EspnId].Seed) != 9).ToList();

        if (!wcIds.SetEquals(expected) || notInStandings.Count > 0 || badGames.Count > 0)
        {
            log.LogWarning("Standings seeds: {Seeds}", string.Join(", ",
                standings.OrderBy(s => s.Conference).ThenBy(s => s.Seed)
                         .Select(s => $"{s.Conference}{s.Seed}={s.EspnId}")));
            log.LogWarning("Wild-card games: {Games}", string.Join(" | ", wc.Select(g =>
                string.Join(" v ", g.Teams.Select(t => byId.TryGetValue(t.EspnId, out var s)
                    ? $"{t.EspnId}(seed {s.Seed})" : $"{t.EspnId}(not in standings)")))));
            log.LogWarning("In scoreboard but not seeds 2-7: {A}. In seeds 2-7 but not on scoreboard: {B}.",
                string.Join(",", wcIds.Except(expected)), string.Join(",", expected.Except(wcIds)));
            log.LogWarning("Standings and wild-card games disagree. Field not loaded.");
            return false;
        }

        var teams = await db.Teams.ToListAsync();
        foreach (var s in standings)
        {
            var row = teams.Single(t => t.Conference == s.Conference && t.Seed == s.Seed);
            row.Name = s.Name;
            row.EspnId = s.EspnId;
        }
        await db.SaveChangesAsync();

        await SetAsync(db, "LockUtc", wc.Min(g => g.StartUtc).ToString("O"));
        await SetAsync(db, "FieldLoaded", "1");
        log.LogInformation("Playoff field loaded.");
        return true;
    }

    // Finds every object that has "team" and "stats", tracking which conference we are inside.
    static void Walk(JsonElement el, Conference? conf, List<StandingTeam> into)
    {
        if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var x in el.EnumerateArray()) Walk(x, conf, into);
            return;
        }
        if (el.ValueKind != JsonValueKind.Object) return;

        conf = ConferenceOf(el) ?? conf;

        if (el.TryGetProperty("team", out var team) && el.TryGetProperty("stats", out var stats))
        {
            if (conf is null || stats.ValueKind != JsonValueKind.Array) return;

            var seedStat = stats.EnumerateArray().FirstOrDefault(s =>
                s.TryGetProperty("name", out var n) && n.GetString() == "playoffSeed");

            if (seedStat.ValueKind == JsonValueKind.Object)
            {
                int seed = (int)seedStat.GetProperty("value").GetDouble();
                if (seed is >= 1 and <= 7)
                    into.Add(new StandingTeam(
                        int.Parse(team.GetProperty("id").GetString()!),
                        team.GetProperty("displayName").GetString()!,
                        conf.Value, seed));
            }
            return;
        }

        foreach (var p in el.EnumerateObject()) Walk(p.Value, conf, into);
    }

    static Conference? ConferenceOf(JsonElement el)
    {
        string? Str(string p) =>
            el.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        var abbr = Str("abbreviation");
        var name = Str("name");
        if (abbr == "AFC" || name?.Contains("American Football Conference") == true) return Conference.AFC;
        if (abbr == "NFC" || name?.Contains("National Football Conference") == true) return Conference.NFC;
        return null;
    }

    // ---------- results ----------
    async Task RecordResultsAsync(ApplicationDbContext db, List<Game> games)
    {
        var teams = await db.Teams.Where(t => t.EspnId != null).ToDictionaryAsync(t => t.EspnId!.Value);
        var existing = (await db.RoundResults.Select(r => new { r.Round, r.TeamId }).ToListAsync())
            .Select(r => (r.Round, r.TeamId)).ToHashSet();

        int added = 0;
        foreach (var g in games.Where(g => g.Completed))
        {
            var winners = g.Teams.Where(t => t.Winner).ToList();
            if (winners.Count != 1 || !teams.TryGetValue(winners[0].EspnId, out var team))
            {
                log.LogWarning("Skipping a final game with no clear winner in the field (week {Week}).", g.Week);
                continue;
            }

            if (existing.Add((Rounds[g.Week], team.Id)))
            {
                db.RoundResults.Add(new RoundResult { Round = Rounds[g.Week], TeamId = team.Id });
                added++;
            }
        }

        if (added > 0)
        {
            await db.SaveChangesAsync();
            log.LogInformation("Recorded {Added} new results.", added);
        }
    }

    // ---------- settings ----------
    static async Task<string?> GetAsync(ApplicationDbContext db, string key) =>
        (await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key))?.Value;

    static async Task SetAsync(ApplicationDbContext db, string key, string value)
    {
        var row = await db.AppSettings.FindAsync(key);
        if (row is null) db.AppSettings.Add(new AppSetting { Key = key, Value = value });
        else row.Value = value;
        await db.SaveChangesAsync();
    }
}