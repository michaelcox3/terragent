namespace Terragent.Pathfinding;

/// <summary>
/// What each kind of move costs this character, in ticks.
/// </summary>
/// <param name="WalkCost">One tile of level ground, and the unit the rest are quoted in.</param>
/// <param name="MineCost">One tile broken with the held pickaxe.</param>
/// <param name="PlaceCost">One block put down, jump included.</param>
/// <param name="InWaterCost">
/// A step taken while already under water, as a multiple of the same step dry.
/// </param>
// Dear, and never infinite. Refused outright it is not a price at all but a wall, and a
// wall has no inside and no outside: a pool is wider than a footing, so a body that ends up
// in one has every move it could make refused, and a run watched doing this stood in a pond
// reporting nothing reachable until it was killed. Priced instead, the way out is simply
// the cheapest route and the search finds it without being told to.
/// <param name="IntoWaterCost">
/// A step from dry ground into water. Infinity where nothing carried lights under water,
/// which the search reads as a refusal rather than as a price.
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
    float InWaterCost, float IntoWaterCost, float LavaCost, float FogCost)
{
    /// <summary>What liquid costs when the agent can still see in it.</summary>
    private const float Lit = 1.5f;

    /// <summary>What one tile of water costs with nothing that lights it.</summary>
    // Dear enough that no way round is longer: one wet tile against ten thousand dry ones,
    // where the widest world is four thousand across. So it is never worth a step in, and
    // still worth every step out.
    private const float Blind = 10000f;

    /// <summary>Ticks to place one block, plus the jump that has to precede it.</summary>
    // A constant, unlike walk and mine: nothing the agent can carry makes placing faster.
    private const float PlaceTicks = 30f;

    /// <summary>How many swings a pickaxe of this power takes to break an ordinary tile.</summary>
    // Terraria removes a hundred points of tile per hit at power a hundred and a share
    // at less; the ceiling is the count. Here so the harness prices a mine by the same
    // rule.
    public static int SwingsPerTile(int pickPower) =>
        pickPower <= 0 ? int.MaxValue : (int)System.Math.Ceiling(100.0 / pickPower);

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
        // Two prices, because getting in and getting out are not the same act. Already
        // under, every move has to stay possible or a body in a pool has nothing it can do
        // and stands there until it is killed, which is a run that happened. Dear, so the
        // way out is the shortest one.
        lightsWet ? Lit : Blind,

        // Getting in is refused, and a price cannot refuse anything. The search returns the
        // cheapest route it can find and has no idea what too expensive means, so ten
        // thousand only ranks water against a dry alternative in the same search: where
        // every route to a goal is wet, the cheapest is still wet and the number cancels
        // out. A run walked into a pool that way, drowned the torch that was its only light,
        // and could not see to leave. Infinite here, the search drops the move instead.
        lightsWet ? Lit : float.PositiveInfinity,
        // Lava glows, so the terrain survives it. What ought to make it dear is damage,
        // and the agent does not take any yet.
        Lit,
        // Unseen ground is only worth opening if the agent can light what it opens;
        // blind, it digs in, nothing reveals and it stands there. The torch reserve
        // should prevent that; this catches the run where something spent it anyway.
        lightsDark ? 1f : float.PositiveInfinity);
}
