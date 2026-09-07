namespace Terragent.Pathfinding;

/// <summary>
/// What a jump from a standstill can reach: how high, and how far across by the time
/// it comes down to each height.
/// </summary>
/// <param name="Height">How many rows the apex clears, understated by one.</param>
/// <param name="ReachByLanding">
/// How many columns across the body can be when it comes down to a landing this many
/// rows above the takeoff, index zero being level ground.
/// </param>
// Height and reach travel together because they trade off: a six row rise peaks about
// twenty four frames in, when a standing start has gained under two columns.
//
// Numbers only. The body works them out from its own physics, and the search reads them
// without knowing what a frame is.
internal readonly record struct Leap(int Height, int[] ReachByLanding)
{
    /// <summary>Columns across a jump can carry when it lands this many rows above the takeoff.</summary>
    public int Reach(int rowsUp) =>
        rowsUp < 0 || rowsUp >= ReachByLanding.Length ? 0 : ReachByLanding[rowsUp];
}
