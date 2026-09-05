using System;
using System.Text.Json;
using Terraria.ModLoader;
using Terragent;
using System.Collections.Generic;
using System.Linq;

namespace Terragent.Progression;

/// <summary>What a node costs and whether it can be started.</summary>
internal sealed record NodeStatus
{
    public required string Key { get; init; }
    public bool Satisfied { get; init; }

    /// <summary>Every prerequisite met, so work could begin now.</summary>
    public bool Available { get; init; }

    /// <summary>This node's own cost plus everything unmet it depends on.</summary>
    public float TotalCost { get; init; }

    /// <summary>Expected attempts, above 1 only when a random drop is involved.</summary>
    public float Attempts { get; init; } = 1f;

    /// <summary>Longest chain of prerequisites behind it, for tie-breaking.</summary>
    public int Depth { get; init; }
}

/// <summary>The node to work on, and why.</summary>
internal sealed record ObjectiveChoice(Node Node, NodeStatus Status, string Reason);

/// <summary>
/// The progression graph: what to do next, and what that will cost.
/// </summary>
internal sealed class ProgressionGraph
{
    /// <summary>What the run is for: the target is the Wall of Flesh, not Moon Lord.</summary>
    // Not in the file: the one thing about the progression that is a project decision
    // rather than a number to tune.
    public const string Target = "wall_of_flesh";

    private readonly Dictionary<string, Node> _nodes;

    /// <summary>Build the graph from the file that describes it.</summary>
    public static ProgressionGraph Load(Mod mod) =>
        new(Described(Data.Read(mod, "Data/progression.json")));

    /// <summary>The nodes the file describes, with every name resolved.</summary>
    private static Node[] Described(JsonElement root)
    {
        List<Node> nodes = [];
        foreach (JsonElement entry in root.GetProperty("nodes").EnumerateArray())
        {
            nodes.Add(new Node
            {
                Key = Data.Text(entry, "key"),
                Kind = Data.Choice(entry, "kind", NodeKind.Item),
                Label = Data.Text(entry, "label"),
                Requires = Data.Strings(entry, "requires"),
                ItemID = entry.TryGetProperty("item", out JsonElement item)
                    ? Data.Item(item.GetString() ?? "")
                    : 0,
                Any = Data.Items(entry, "any"),
                Keeps = Data.Strings(entry, "keeps"),
                Count = Data.Number(entry, "count", 1),
                Effort = Data.Decimal(entry, "effort", 1f),
                When = Data.Choice(entry, "when", TimeOfDay.Any),
                CreatureID = entry.TryGetProperty("source", out JsonElement source)
                    ? Data.Creature(source.GetString() ?? "")
                    : 0,
                Note = Data.Text(entry, "note"),
            });
        }

        return [.. nodes];
    }

    public ProgressionGraph(IEnumerable<Node> nodes)
    {
        _nodes = nodes.ToDictionary(node => node.Key);
        Goal = Target;

        foreach (Node node in _nodes.Values)
        {
            foreach (string requirement in node.Requires)
            {
                if (!_nodes.ContainsKey(requirement))
                {
                    throw new ArgumentException(
                        $"{node.Key} requires {requirement}, which is not a node");
                }
            }
        }

        if (!_nodes.ContainsKey(Target))
        {
            throw new ArgumentException($"target {Target} is not a node");
        }

        // A cycle makes costing non-terminating, and a graph edited by hand acquires
        // one eventually. Better a load-time exception than a hang mid-run.
        _ = TopologicalOrder();
    }

    public string Goal { get; }

    public IReadOnlyCollection<Node> Nodes => _nodes.Values;

    public Node this[string key] => _nodes[key];

    public IReadOnlyList<string> TopologicalOrder()
    {
        Dictionary<string, int> pending = _nodes.Values.ToDictionary(
            node => node.Key, node => node.Requires.Count);
        Queue<string> ready = new(pending.Where(pair => pair.Value == 0)
            .Select(pair => pair.Key).OrderBy(key => key, StringComparer.Ordinal));
        List<string> order = [];

        while (ready.Count > 0)
        {
            string key = ready.Dequeue();
            order.Add(key);
            foreach (Node node in _nodes.Values)
            {
                if (!node.Requires.Contains(key))
                {
                    continue;
                }

                if (--pending[node.Key] == 0)
                {
                    ready.Enqueue(node.Key);
                }
            }
        }

        if (order.Count != _nodes.Count)
        {
            throw new InvalidOperationException(
                "the progression graph has a cycle: "
                + string.Join(", ", _nodes.Keys.Except(order).OrderBy(k => k, StringComparer.Ordinal)));
        }

        return order;
    }

    /// <summary>Every unmet node behind this one, including itself.</summary>
    private HashSet<string> AncestorSet(string key, ISet<string> satisfied)
    {
        HashSet<string> seen = [];
        Stack<string> stack = new();
        stack.Push(key);
        while (stack.Count > 0)
        {
            string current = stack.Pop();
            if (satisfied.Contains(current) || !seen.Add(current))
            {
                continue;
            }

            foreach (string requirement in _nodes[current].Requires)
            {
                stack.Push(requirement);
            }
        }

        return seen;
    }

    /// <summary>What one node costs on its own, expected attempts included.</summary>
    private (float Cost, float Attempts) OwnCost(Node node)
    {
        if (node.CreatureID == 0)
        {
            return (node.Effort, 1f);
        }

        // Drops per kill is the game's answer. Effort is ours: what one fight costs is
        // a judgement about the run, and nothing in the drop table knows it.
        float each = Drops.PerKill(node.CreatureID, node.ItemID);
        if (each <= 0f)
        {
            return (node.Effort, 1f);
        }

        float attempts = node.Count / each;
        return (node.Effort * attempts, attempts);
    }

    private int DepthOf(string key, Dictionary<string, int> memo)
    {
        if (memo.TryGetValue(key, out int known))
        {
            return known;
        }

        int depth = 0;
        foreach (string requirement in _nodes[key].Requires)
        {
            depth = Math.Max(depth, DepthOf(requirement, memo) + 1);
        }

        memo[key] = depth;
        return depth;
    }

    public Dictionary<string, NodeStatus> ExpectedCosts(ISet<string> satisfied)
    {
        Dictionary<string, int> depths = [];
        Dictionary<string, NodeStatus> statuses = [];

        foreach (Node node in _nodes.Values)
        {
            bool done = satisfied.Contains(node.Key);
            float total = 0f;
            foreach (string ancestor in AncestorSet(node.Key, satisfied))
            {
                total += OwnCost(_nodes[ancestor]).Cost;
            }

            statuses[node.Key] = new NodeStatus
            {
                Key = node.Key,
                Satisfied = done,
                Available = !done && node.Requires.All(satisfied.Contains),
                TotalCost = done ? 0f : total,
                Attempts = OwnCost(node).Attempts,
                Depth = DepthOf(node.Key, depths),
            };
        }

        return statuses;
    }

    public bool GoalReached(ISet<string> satisfied) => satisfied.Contains(Goal);

    /// <summary>
    /// The cheapest node that can be worked on now, or null when there is none.
    /// </summary>
    /// <param name="skip">
    /// Keys the executor reported it cannot currently act on. Skipping is not
    /// satisfying: the node stays unmet and still gates its dependants, the agent just
    /// spends its time on something it can do.
    /// </param>
    /// <param name="isDaytime">
    /// The world clock. A node outside its window is deferred like a skipped one: six
    /// Lenses cannot be worked on at noon, and handing it back anyway hunts a creature
    /// the game never spawns.
    /// </param>
    public ObjectiveChoice? NextObjective(ISet<string> satisfied, ISet<string>? skip = null,
        bool isDaytime = true)
    {
        if (GoalReached(satisfied))
        {
            return null;
        }

        skip ??= new HashSet<string>();
        NodeStatus? best = null;
        foreach (NodeStatus status in ExpectedCosts(satisfied).Values)
        {
            if (!status.Available || skip.Contains(status.Key)
                || !_nodes[status.Key].AvailableNow(isDaytime))
            {
                continue;
            }

            if (best is null || Prefer(status, best))
            {
                best = status;
            }
        }

        if (best is null)
        {
            return null;
        }

        // Only says something when there is something to say: every goal picked is the
        // cheapest available, and printing that under each one tells the reader nothing.
        string reason = best.Attempts > 1.5f
            ? $"about {best.Attempts:0} attempts for a random drop"
            : "";
        return new ObjectiveChoice(_nodes[best.Key], best, reason);
    }

    /// <summary>
    /// Cheapest first; ties broken by depth so the agent advances the spine rather
    /// than collecting sideways, then by key so a run is reproducible.
    /// </summary>
    private static bool Prefer(NodeStatus candidate, NodeStatus best)
    {
        if (candidate.TotalCost != best.TotalCost)
        {
            return candidate.TotalCost < best.TotalCost;
        }

        if (candidate.Depth != best.Depth)
        {
            return candidate.Depth > best.Depth;
        }

        return string.CompareOrdinal(candidate.Key, best.Key) < 0;
    }

    /// <summary>
    /// Nodes that are unlocked and affordable, but not at this hour.
    /// </summary>
    public IReadOnlyList<Node> DeferredByTime(ISet<string> satisfied,
        ISet<string>? skip = null, bool isDaytime = true)
    {
        skip ??= new HashSet<string>();
        return ExpectedCosts(satisfied).Values
            .Where(status => status.Available && !skip.Contains(status.Key)
                && !_nodes[status.Key].AvailableNow(isDaytime))
            .Select(status => _nodes[status.Key])
            .ToList();
    }
}
