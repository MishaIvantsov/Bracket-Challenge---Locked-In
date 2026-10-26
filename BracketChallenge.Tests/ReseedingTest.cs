using Xunit;
using BracketChallenge.Scoring;

public class ReseedingTests
{
    [Fact]
    public void AllFavoritesWin()
    {
        var games = Reseeding.Divisional([2, 3, 4]);
        Assert.Equal((1, 4), games[0]);
        Assert.Equal((2, 3), games[1]);
    }

    [Fact]
    public void AllUnderdogsWin()
    {
        var games = Reseeding.Divisional([7, 6, 5]);
        Assert.Equal((1, 7), games[0]);
        Assert.Equal((5, 6), games[1]);
    }

    [Fact]
    public void MixedResults_OneSeedFacesLowestRemaining()
    {
        var games = Reseeding.Divisional([2, 6, 5]);
        Assert.Equal((1, 6), games[0]);
        Assert.Equal((2, 5), games[1]);
    }

    [Fact]
    public void InputOrderDoesNotMatter()
    {
        Assert.Equal(Reseeding.Divisional([2, 6, 5]), Reseeding.Divisional([5, 2, 6]));
    }

    [Fact]
    public void TwoWinnersFromSameGame_Throws()
    {
        Assert.Throws<ArgumentException>(() => Reseeding.Divisional([2, 7, 4]));
    }

    [Fact]
    public void WrongNumberOfWinners_Throws()
    {
        Assert.Throws<ArgumentException>(() => Reseeding.Divisional([2, 3]));
    }

    [Fact]
    public void ConferenceChampionship_BestSeedHosts()
    {
        Assert.Equal((1, 3), Reseeding.ConferenceChampionship([3, 1]));
    }

    [Fact]
    public void ConferenceChampionship_SameTeamTwice_Throws()
    {
        Assert.Throws<ArgumentException>(() => Reseeding.ConferenceChampionship([2, 2]));
    }
}