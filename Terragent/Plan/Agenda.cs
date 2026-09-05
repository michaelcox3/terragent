using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terragent.Progression;

namespace Terragent.Plan;

/// <summary>
/// What the run is going for, what it must keep in stock, and what it has finished.
/// </summary>
// A ModPlayer's job is to be where the game calls in. Deciding what the run does next
// is not that, and none of this touches a control.
//
// It owns the three things that outlive a tick and that only these methods read: what
// has ever been reached, what is on backoff, and which standing supply is being
// restocked.
internal sealed class Agenda(ProgressionGraph progression, Belief belief,
    Character character, Stations stations)
{
    private readonly Skips _skips = new();

    /// <summary>Nodes this run has ever met, which is what "done" means.</summary>
    // Reached once is reached. Satisfaction reports what is carried this second, which
    // answers "can I craft that" and not "has this run got past a hundred wood":
    // spending the wood would otherwise un-finish the goal it was spent on.
    private readonly HashSet<string> _reached = [];

    private HashSet<string> _skipped = [];

    /// <summary>Where the run is going, or null when nothing is actionable.</summary>
    public ObjectiveChoice? Objective { get; private set; }

    /// <summary>
    /// The standing supplies that have run short, empty when all are met.
    /// </summary>
    // Not named Upkeep, which is the type. Named for the state it reports: something is
    // short, and until it is not the run carries a restock alongside its objective.
    public IReadOnlyList<Upkeep> Shortfalls { get; private set; } = [];

    /// <summary>
    /// Work out where the run stands: what is done, what is short, and what is next.
    /// </summary>
    // Once a second, from PostUpdate. Nothing here presses a button.
    public void Review(AgentState state, double now)
    {
        _reached.UnionWith(Satisfaction.Of(progression, character));
        HashSet<string> done = _reached;
        state.Finished.Clear();
        state.Finished.AddRange(done);

        // A node satisfied is a node worth trying again: the reason it was skipped may
        // have been the thing that just got done.
        foreach (string key in done)
        {
            _skips.Clear(key);
        }

        _skipped = _skips.Current(now);
        Shortfalls = Restocking(done);
        Objective = progression.NextObjective(done, _skipped, Main.dayTime);
        Say(state, done, now);

        state.Blocked.Clear();
        foreach (string key in _skipped)
        {
            state.Blocked.Add(_skips.Reason(key, now));
        }

        Publish(state, done);
    }

    /// <summary>
    /// Which standing supply to restock, holding the one already started.
    /// </summary>
    // A standing requirement that has run short outranks progress, because the progress
    // is what spent it. Unlike a graph node it can become unmet again, which is why it
    // lives beside the graph rather than in it. It does not replace the objective: it is
    // a job on the same board, and the objective goes on saying what it is for.
    private List<Upkeep> Restocking(HashSet<string> done)
    {
        // What the finished nodes have brought into force. The progression says when a
        // thing starts mattering (a bow is the moment arrows do); upkeep.json says how
        // many to hold and, where a supply is described twice, which description wins.
        HashSet<string> unlocked = [];
        foreach (string key in done)
        {
            foreach (string name in progression[key].Keeps)
            {
                unlocked.Add(name);
            }
        }

        // Once restocking has started it runs to the ceiling, not back to the trigger.
        // Short is recomputed from the trigger every tick, so one slime's worth of gel
        // ends the job ten short. Two numbers only mean anything if the exit uses the
        // other one.
        //
        // Each supply on its own, or one that cannot be had (glowsticks before the first
        // pot) holds the slot while the torches behind it run out.
        IReadOnlyList<Upkeep> standing = Upkeeps.For(unlocked);
        List<Upkeep> shortfalls = [];
        foreach (Upkeep running in Shortfalls)
        {
            if (standing.Any(entry => entry.ItemID == running.ItemID && entry.Ceiling == running.Ceiling)
                && character.Inventory.Carrying(running.ItemID) < running.Ceiling)
            {
                shortfalls.Add(running);
            }
        }

        foreach (Upkeep fresh in Upkeeps.Short(character.Inventory, unlocked))
        {
            if (!shortfalls.Any(running => running.ItemID == fresh.ItemID))
            {
                shortfalls.Add(fresh);
            }
        }

        return shortfalls;
    }

    /// <summary>Put what the run is doing into words for the panel.</summary>
    private void Say(AgentState state, HashSet<string> done, double now)
    {
        if (Objective is not null)
        {
            state.Objective = Objective.Node.Label;
            state.Reason = Objective.Reason;
            return;
        }

        if (progression.GoalReached(done))
        {
            state.Objective = "progression complete";
            state.Reason = "";
            return;
        }

        if (_skipped.Count > 0)
        {
            // Everything doable is on backoff. Say that, rather than naming the
            // night-gated goal: "waiting for night" while daytime work is merely stuck
            // reads as the agent choosing a job it cannot start.
            state.Objective = "retrying shortly";
            state.Reason = _skips.Reason(_skipped.First(), now);
            return;
        }

        IReadOnlyList<Node> waiting =
            progression.DeferredByTime(done, _skipped, Main.dayTime);
        state.Objective = waiting.Count > 0
            ? $"waiting for {(Main.dayTime ? "night" : "day")}"
            : "nothing actionable";
        state.Reason = waiting.Count > 0
            ? $"{waiting[0].Label} needs it"
            : "no goal is available";
    }

    /// <summary>
    /// Set the objective aside for a while, because it could not be acted on.
    /// </summary>
    // Skipped, not failed. The node stays unmet and keeps gating what depends on it;
    // the agent just spends the time on something it can actually do.
    public void SetAside(AgentState state, double now)
    {
        if (Objective is not { } stuck)
        {
            return;
        }

        Log.Sample("skip", stuck.Node.Key,
            ("why", state.Behaviour), ("tries", _skips.Tries(stuck.Node.Key) + 1));
        _skips.Skip(stuck.Node.Key, now, state.Behaviour);
        Objective = null;
    }

    /// <summary>
    /// What the panel and the journal see: the whole spine, and where the run is.
    /// </summary>
    // Called on every path out of the review, not just the objective branch: restocking
    // returns early, and the panel would go on showing a goal already finished.
    private void Publish(AgentState state, HashSet<string> done)
    {
        state.Progression.Clear();
        Dictionary<string, NodeStatus> costs = progression.ExpectedCosts(done);
        foreach (string key in progression.TopologicalOrder())
        {
            string status = done.Contains(key) ? "done"
                : Objective?.Node.Key == key ? "now"
                : _skipped.Contains(key) ? "skipped"
                : costs[key].Available ? "ready"
                : "locked";
            state.Progression.Add((progression[key].Label, status));
        }

        Log.Event("have", string.Join(",", state.Finished),
            ("benchAt", stations.Nearest(TileID.WorkBenches, belief, character.Movement.At)
                ?.ToString() ?? "none"),
            ("carrying", character.Inventory.Carrying(ItemID.WorkBench)),
            ("wood", character.Inventory.Carrying(ItemID.Wood)),
            ("treesKnown", belief.Sites.Where(TileID.Trees).Count));

        Log.Event("objective", state.Objective,
            ("why", state.Reason),
            ("done", done.Count),
            ("skipped", (IEnumerable<string>)_skipped));
    }
}
