using Microsoft.Xna.Framework;
using Terragent.Plan;
using Terragent.World;

namespace Terragent.UnitTests;

/// <summary>
/// The rule that ends the pendulum: a route is redrawn when the selected candidate
/// moves, and the selection changes only when that candidate cannot be reached.
/// </summary>
public class CandidateTests
{
    private static Candidate Ore(int x, int y, int within = 5) =>
        new(ItemID: 11, Origin.Tile, new Point(x, y), within);

    [Fact]
    public void StandingStillIsNotAMove()
    {
        Candidate was = Ore(2097, 313);
        Assert.False(was.Moved(Ore(2097, 313)));
    }

    [Fact]
    public void DriftInsideTheRadiusIsNotAMove()
    {
        Candidate was = Ore(2097, 313, within: 5);
        Assert.False(was.Moved(Ore(2100, 316, within: 5)));
    }

    [Fact]
    public void DriftPastTheRadiusIsAMove()
    {
        Candidate was = Ore(2097, 313, within: 5);
        Assert.True(was.Moved(Ore(2103, 309, within: 5)));
    }

    [Fact]
    public void ADropMustBeTouchedSoAnyDriftIsAMove()
    {
        Candidate was = new(ItemID: 11, Origin.Drop, new Point(2086, 316), Within: 0);
        Assert.True(was.Moved(was with { Site = new Point(2087, 316) }));
        Assert.False(was.Moved(was));
    }

    [Fact]
    public void TheSameJobFromTheSameOriginIsTheSameCandidate()
    {
        Candidate was = Ore(2097, 313);
        Assert.True(was.Same(Ore(2103, 309)));
    }

    [Fact]
    public void AnotherItemOrAnotherOriginIsAnotherCandidate()
    {
        Candidate was = Ore(2097, 313);
        Assert.False(was.Same(was with { ItemID = 12 }));
        Assert.False(was.Same(was with { From = Origin.Drop }));
    }

    [Fact]
    public void ACandidateSittingWhereAnotherSatIsStillAnotherJob()
    {
        Candidate selected = Ore(2097, 313);
        Assert.False(selected.Same(new Candidate(ItemID: 12, Origin.Tile, new Point(2097, 313), 5)));
    }

    [Fact]
    public void DriftIsMeasuredAsACircleRatherThanABox()
    {
        // Three across and three down is four and a quarter, not three: a box would call
        // this still and leave the route drawn to where the work is not.
        Assert.True(Candidate.Beyond(new Point(0, 0), new Point(3, 3), radius: 4));
        Assert.False(Candidate.Beyond(new Point(0, 0), new Point(3, 3), radius: 5));
    }

    [Fact]
    public void ARadiusOfZeroMakesEveryDriftAMove()
    {
        Assert.True(Candidate.Beyond(new Point(5, 5), new Point(5, 6), radius: 0));
        Assert.False(Candidate.Beyond(new Point(5, 5), new Point(5, 5), radius: 0));
    }
}
