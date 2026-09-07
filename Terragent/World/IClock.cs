
namespace Terragent.World;

/// <summary>The time, asked once so that everything reads the same tick.</summary>
// One clock, handed to whoever needs it. Two sources of time is how a stall timer and
// the thing it guards come to disagree about how long something has been going on, and a
// method that takes the time as a parameter is a second source: the caller decides.
internal interface IClock
{
    /// <summary>Seconds since the run began.</summary>
    // Not the wall clock and not Terraria's calendar. Something that only counts forward
    // while the game is running, since every use of it is a duration.
    double Now { get; }

    /// <summary>Whether the sun is up.</summary>
    // The graph gates on it, since half of what a run has to kill only comes out at
    // night, and jobs above ground are cheaper by daylight.
    bool Day { get; }
}
