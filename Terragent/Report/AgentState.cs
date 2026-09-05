using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Terragent.Report;

/// <summary>What the agent is doing, for anything that wants to show it.</summary>
internal sealed class AgentState
{
    /// <summary>The progression node being worked on, such as "Wooden Bow".</summary>
    public string Objective { get; set; } = "idle";

    /// <summary>Why that node, from the planner's own costing.</summary>
    public string Reason { get; set; } = "";

    /// <summary>
    /// What it is doing right now: "chopping", or "walking to (2103, 245)".
    /// <para>
    /// Deliberately separate from <see cref="Objective"/>. Conflating "the goal is a
    /// Wooden Bow" with "I am currently walking east" made the old dashboard unable
    /// to show a long goal being served by a short action, which is most of the time.
    /// </para>
    /// </summary>
    public string Behaviour { get; set; } = "";

    /// <summary>Whether the thing being attempted has run out of ways to proceed.</summary>
    // Beside Behaviour, and for the same reason: what the agent is doing and whether it
    // can go on are one report, written by whichever part holds the tick and read by
    // the panel and the backoff.
    public bool Stuck { get; set; }

    /// <summary>Goals finished this run, most recent last.</summary>
    public List<string> Finished { get; } = new();

    /// <summary>
    /// The whole spine, in dependency order, with a status for each.
    /// </summary>
    public List<(string Label, string Status)> Progression { get; } = new();

    /// <summary>Goals the executor reported it cannot currently act on.</summary>
    public List<string> Blocked { get; } = new();

    /// <summary>The planned route as points, in <b>world tile</b> order, for drawing.</summary>
    // Waypoints rather than Route, which is the type that holds the steps themselves.
    // This is a route flattened for the overlay: no kinds, no breaks, no index.
    public List<Point> Waypoints { get; } = new();

    /// <summary>Tiles the current plan intends to break.</summary>
    public List<Point> Digs { get; } = new();

    /// <summary>
    /// Every tile the current route intends to break, before it breaks any of them.
    /// </summary>
    public List<Point> Planned { get; } = new();

    /// <summary>Cells the current route intends to place a block in.</summary>
    public List<Point> Placing { get; } = new();

    /// <summary>
    /// Blocks the running scenario requires to be broken, from its 'd' marks.
    /// </summary>
    public List<Point> Required { get; } = new();

    /// <summary>Where the plan is heading, if anywhere.</summary>
    public Point? Goal { get; set; }

    /// <summary>
    /// The tile the <i>game</i> says the cursor is on, drawn so intent and effect can
    /// be compared without reading numbers.
    /// </summary>
    public Point? Aimed { get; set; }

    /// <summary>The tiles the game says the player can reach right now.</summary>
    public Rectangle? ReachBox { get; set; }

    /// <summary>The cells a pending placement would occupy, not just its origin.</summary>
    public Rectangle? Footprint { get; set; }

    /// <summary>Whether the agent holds the controls.</summary>
    public bool Driving { get; set; }

    /// <summary>Fraction of the world on the player's map, sampled. See the panel.</summary>
    public float Coverage { get; set; }

    /// <summary>
    /// What the objective is actually short of, and which one is being worked.
    /// </summary>
    // The objective says where the run is going; a job says what it is doing about it
    // this minute. A recipe wanting obsidian and hellstone is short of both, and both
    // are worth seeing even though one body can only fetch one of them.
    //
    // Not cleared with the rest of the plan: anything that takes the tick before the
    // executor runs, a slime in arm's reach or a station being taken back, would
    // otherwise blank the panel for a tick.

    /// <summary>Where the agent aimed, and where the game says it landed.</summary>
    public string Aim { get; set; } = "";

    /// <summary>
    /// The raw facts behind whatever the agent is doing: what it holds, whether the
    /// use button is down, whether the game is animating a swing.
    /// </summary>
    public string Holding { get; set; } = "";
    public bool UseDown { get; set; }

    /// <summary>
    /// Whether a swing is actually in progress, as opposed to the key being held.
    /// </summary>
    public bool Swinging => SwingTicks > 0;
    public int SwingTicks { get; set; }

    /// <summary>False when our reach predicate disagrees with the game's own.</summary>
    public bool ReachAgrees { get; set; } = true;

    /// <summary>A cell the follower could not see, for the lamp to light next tick.</summary>
    // Survives ClearPlan on purpose: it is set at the end of one tick and read at the
    // start of the next, and the lamp is what clears it.
    public Point? Blind { get; set; }


    public void ClearPlan()
    {
        Waypoints.Clear();
        Digs.Clear();
        Planned.Clear();
        Placing.Clear();
        Required.Clear();
        Goal = null;
        Aimed = null;
        Footprint = null;
    }
}
