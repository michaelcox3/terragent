namespace Terragent.World;

/// <summary>
/// What each kind of move costs this character, in ticks.
/// </summary>
/// <param name="WalkCost">One tile of level ground, and the unit the rest are quoted in.</param>
/// <param name="MineCost">One tile broken with the held pickaxe.</param>
/// <param name="PlaceCost">One block put down, jump included.</param>
/// <param name="WaterCost">
/// A step that puts the head under water, as a multiple of the same step dry.
/// </param>
/// <param name="LavaCost">The same for lava, which is a different problem.</param>
/// <param name="FogCost">
/// What breaking into a cell nobody has seen costs. Infinity where the agent has no
/// light, because digging into the dark is how it goes blind.
/// </param>
// Together because they are one question, what this character's moves are worth,
// asked of one place at one time. Plain numbers, so the headless harness can price a
// scenario without a game. In World rather than Search: equipment sets these, and the
// search only takes them as input.
internal readonly record struct Costs(float WalkCost, float MineCost, float PlaceCost,
    float WaterCost, float LavaCost, float FogCost)
{
    /// <summary>How many swings a pickaxe of this power takes to break an ordinary tile.</summary>
    // Terraria removes a hundred points of tile per hit at power a hundred and a share
    // at less; the ceiling is the count. Here so the harness prices a mine by the same
    // rule.
    public static int SwingsPerTile(int pickPower) =>
        pickPower <= 0 ? int.MaxValue : (int)System.Math.Ceiling(100.0 / pickPower);

    /// <summary>What liquid costs when the agent can still see in it.</summary>
    private const float Lit = 1.5f;

    /// <summary>Ticks to place one block, plus the jump that has to precede it.</summary>
    // A constant, unlike walk and mine: nothing the agent can carry makes placing faster.
    private const float PlaceTicks = 30f;

    /// <summary>
    /// What every kind of move is worth to a character with these numbers, right now.
    /// </summary>
    /// <param name="runSpeed">The body's top speed, in pixels per tick.</param>
    /// <param name="pickPower">The strongest pickaxe carried, or zero for none.</param>
    /// <param name="pickUseTime">Ticks per swing of that pickaxe.</param>
    /// <param name="lightsWet">Whether something carried lights under water.</param>
    /// <param name="lightsDark">Whether something carried lights the dark.</param>
    // The one place the search's prices are set. Takes numbers from the body, the bag
    // and its lights rather than the units themselves, so the harness can price a
    // scenario too.
    public static Costs Priced(float runSpeed, int pickPower, int pickUseTime,
        bool lightsWet, bool lightsDark) => new(
        16f / System.Math.Max(1f, runSpeed),
        pickPower <= 0 ? float.PositiveInfinity : SwingsPerTile(pickPower) * (float)pickUseTime,
        PlaceTicks,
        // A torch will not light under water: the map stops revealing and the follower
        // swings at holes it has already dug. A glowstick lights wet. With nothing that
        // lights wet the move is refused outright; a dear price made the search five
        // times slower and still let the agent in.
        lightsWet ? Lit : float.PositiveInfinity,
        // Lava glows, so the belief survives it. What ought to make it dear is damage,
        // and the agent does not take any yet.
        Lit,
        // Unseen ground is only worth opening if the agent can light what it opens;
        // blind, it digs in, nothing reveals and it stands there. The torch reserve
        // should prevent that; this catches the run where something spent it anyway.
        lightsDark ? 1f : float.PositiveInfinity);
}
