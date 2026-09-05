using Microsoft.Xna.Framework;

namespace Terragent.UnitTests;

/// <summary>
/// What the follower does with a step, asked without a game.
/// </summary>
// The search half of this has always been testable; the half that turns a step into
// keys was not, because it lived among Player fields. It cost a level jump that never
// pressed jump: planned correctly, walked off the ledge, and passed every test there
// was, because every test stopped at the plan.
public class SteeringTests
{
    private static readonly Point From = new(10, 20);

    // The case that was broken: across a gap, same row, standing still. Nothing about
    // it is upward, and the old guard only ever asked about height.
    [Fact]
    public void LevelJumpPressesJump()
    {
        Step level = new(new Point(15, 20), StepKind.Jump);
        Assert.True(Steering.Wants(level, From, From).Jump);
    }

    [Fact]
    public void LevelJumpHeadsTowardItsTarget()
    {
        Step level = new(new Point(15, 20), StepKind.Jump);
        Assert.True(Steering.Wants(level, From, From).Direction > 0);
    }

    [Fact]
    public void JumpUpwardPressesJump()
    {
        Step upward = new(new Point(13, 16), StepKind.Jump);
        Assert.True(Steering.Wants(upward, From, From).Jump);
    }

    [Fact]
    public void JumpDoesNotPressOnceAboveItsTarget()
    {
        Step above = new(new Point(13, 24), StepKind.Jump);
        Assert.False(Steering.Wants(above, From, From).Jump);
    }

    // A one-column jump rises first. It steps across once the head, three rows above
    // the footing, is level with the row the feet will land in.
    [Fact]
    public void SidestepRisesStraightFromTheGround()
    {
        Step ledge = new(new Point(11, 15), StepKind.Jump);
        Assert.Equal(0, Steering.Wants(ledge, From, From).Direction);
    }

    [Fact]
    public void SidestepStillRisesWithItsHeadBelowTheLanding()
    {
        Step ledge = new(new Point(11, 15), StepKind.Jump);
        Assert.Equal(0, Steering.Wants(ledge, From, new Point(10, 18)).Direction);
    }

    [Fact]
    public void SidestepStepsAcrossOnceItsHeadIsLevelWithTheLanding()
    {
        Step ledge = new(new Point(11, 15), StepKind.Jump);
        Assert.Equal(1, Steering.Wants(ledge, From, new Point(10, 17)).Direction);
    }

    [Fact]
    public void WalkNeverPressesJump() =>
        Assert.False(Steering.Wants(new Step(new Point(15, 20), StepKind.Walk), From, From).Jump);

    // A jump that has come down within a column is the route's business, not the
    // steering's: it advances, so the step never reaches Wants again.
    [Fact]
    public void LandedJumpIsConsumedByTheRoute()
    {
        Step level = new(new Point(15, 20), StepKind.Jump);
        Assert.True(Progress.Landed(level, new Point(15, 20)));
    }

    [Fact]
    public void JumpShortByFiveIsNotLanded()
    {
        Step level = new(new Point(15, 20), StepKind.Jump);
        Assert.False(Progress.Landed(level, From));
    }
}
