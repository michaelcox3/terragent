using System.Linq;
using Microsoft.Xna.Framework;
using Terragent.World;
using Xunit;

namespace Terragent.UnitTests;

/// <summary>How unexplored ground is weighed against the walk to it.</summary>
// The two halves of what exploring decides with. What it picks out of a real cave needs a
// body and a site list to ask for, and there are no doubles for either, so the choosing
// itself is not covered here.
public class ProspectTests
{
    /// <summary>A square of ground, every tile of it the same.</summary>
    private static Grid Filled(char what) =>
        new(true, [.. Enumerable.Repeat(new string(what, 21), 21)]);

    /// <summary>Dark up to the middle column and seen from there rightward.</summary>
    private static Grid Half() =>
        new(true, [.. Enumerable.Repeat(new string('?', 11) + new string('.', 10), 21)]);

    /// <summary>Nothing to turn over where everything has been seen already.</summary>
    [Fact]
    public void GroundThatIsAllKnownIsWorthNothing() =>
        Assert.Equal(0, Prospect.Unseen(Filled('.'), new Point(10, 10)));

    /// <summary>An eleven by eleven of every other tile, all of it dark.</summary>
    [Fact]
    public void GroundThatIsAllDarkIsWorthEverySample() =>
        Assert.Equal(121, Prospect.Unseen(Filled('?'), new Point(10, 10)));

    /// <summary>On the line between the two, six of the eleven columns sampled are dark.</summary>
    // The number matters less than where it sits: between the two above, so a richer edge
    // of the dark outscores a poorer one instead of both reading the same.
    [Fact]
    public void GroundHalfInTheDarkIsWorthSomethingInBetween() =>
        Assert.Equal(66, Prospect.Unseen(Half(), new Point(10, 10)));

    /// <summary>Twenty tiles of open air costs twenty tiles of walking.</summary>
    [Fact]
    public void OpenGroundCostsWhatItIsLong() =>
        Assert.Equal(20f, Prospect.Reaching(Filled('.'), new Point(0, 10), new Point(20, 10)));

    /// <summary>The same distance through rock costs an order more.</summary>
    // Which is the whole job of the estimate: a rich seam behind forty tiles of stone must
    // not beat a cave mouth next door on the ratio.
    [Fact]
    public void RockCostsElevenTimesWhatWalkingDoes() =>
        Assert.Equal(240f, Prospect.Reaching(Filled('#'), new Point(0, 10), new Point(20, 10)));

    /// <summary>Standing on it already is a price of one, not a division by zero.</summary>
    [Fact]
    public void WhereTheBodyStandsCostsOne() =>
        Assert.Equal(1f, Prospect.Reaching(Filled('.'), new Point(10, 10), new Point(10, 10)));
}
