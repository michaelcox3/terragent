using System;
using System.Collections.Generic;
using System.Text.Json;
using Terraria;
using Terraria.ModLoader;
using Terragent.Controls;
using Terragent.Report;
using Terragent.World;

using Terragent.Work.Objectives;

namespace Terragent.Work;

/// <summary>The graph of a run, and what this character has got through of it.</summary>
// A list in the order they should be attempted, filtered by what each one requires. Order
// is the tiebreak and the requirements are the rule, so a graph that says nothing about
// prerequisites still runs top to bottom.
internal sealed class Progression(IReadOnlyList<DagNode> nodes) : IProgression
{
    private readonly IReadOnlyList<DagNode> _nodes = nodes;

    /// <summary>Keys reached, which is the only thing here that outlives a tick.</summary>
    private readonly HashSet<string> _reached = [];

    /// <summary>How many keys were reached when the live list was last worked out.</summary>
    // A version rather than a comparison of the sets, which is sound because reached only
    // ever grows. Restore adds to it and Record adds to it, and nothing takes anything out.
    private int _known = -1;

    /// <summary>The live objectives, kept rather than rebuilt every tick.</summary>
    // Not memory of anything. This is a function of the nodes and what has been reached,
    // memoised: the foreman drops the job it is holding whenever it is handed a different
    // list, and a fresh one every tick would reset it sixty times a second.
    private IReadOnlyList<IObjective> _working = [];

    public IReadOnlyList<DagNode> Objectives => _nodes;

    public IReadOnlyCollection<string> Reached => _reached;

    /// <summary>Every objective being worked right now, supplies among them.</summary>
    // A method rather than a property because it works the front out when the graph has
    // moved on. Cheap on every other tick, and the panel is read before the run is even
    // driving, so it cannot wait for the tick to have asked first.
    public IReadOnlyList<IObjective> Active()
    {
        Refresh();
        return _working;
    }

    /// <summary>Work out what the graph allows, when what has been reached has moved on.</summary>
    // Everything unreached whose requirements are behind it. Requirements are a partial
    // order, so "the next node" is not a thing the graph can answer, and picking one by
    // file position is what made a run blind to every crystal, pot and vein belonging to a
    // node further down.
    //
    // Supplies sit in here like anything else rather than cutting in front. One that is
    // stocked offers no jobs at all, so its being listed costs nothing, and one that is
    // short puts its jobs in the same pool as the rest and wins them on distance. Cutting
    // in was from when a single objective was handed down and a restock could be queued
    // behind the thing that spends it; with the whole front live there is no queue to be
    // stuck at the back of.
    private void Refresh()
    {
        if (_known == _reached.Count)
        {
            return;
        }

        List<IObjective> working = [];
        foreach (DagNode node in _nodes)
        {
            if (node.Objective is not Supply && !_reached.Contains(node.Objective.Key) && Ready(node))
            {
                working.Add(node.Objective);
            }
        }

        // After the objectives, so the label names what the run is actually for rather
        // than whichever upkeep happens to head the file.
        //
        // Later replaces earlier for the same item, so the node that makes sixty torches
        // affordable retires the one that asked for five. Order in the file is the rule,
        // which is why no entry carries a tier.
        Dictionary<int, Supply> keeping = [];
        foreach (DagNode node in _nodes)
        {
            if (node.Objective is Supply keep && Ready(node))
            {
                keeping[keep.ItemID] = keep;
            }
        }

        working.AddRange(keeping.Values);

        _known = _reached.Count;
        _working = working;
    }

    public void Record()
    {
        foreach (DagNode node in _nodes)
        {
            // Supplies are never recorded. Going short again is the whole point of one,
            // and latching it would mean the run stocked torches once and then went dark.
            //
            // The one place a kind is asked about rather than the interface: reviewing is
            // what keeps the low trigger and the high fill apart, and no other objective
            // has anything to review.
            if (node.Objective is Supply keep)
            {
                if (Ready(node))
                {
                    keep.Review();
                }

                continue;
            }

            // Every unreached node, not only the one being worked. A pickaxe comes out of
            // a chest, a boss flag gets set by something else, and wood is a side effect
            // of clearing a route: none of those are noticed by asking about one node.
            if (!_reached.Contains(node.Objective.Key) && node.Objective.Met)
            {
                _reached.Add(node.Objective.Key);
            }
        }
    }

    public void Restore(IEnumerable<string> reached) => _reached.UnionWith(reached);

    /// <summary>Whether everything this node waits on has been reached.</summary>
    private bool Ready(DagNode node)
    {
        foreach (DagNode before in node.DependsOn)
        {
            if (!_reached.Contains(before.Objective.Key))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Build the run out of the file that describes it.</summary>
    // A node names what it wants and this makes the objective that answers it. The kinds
    // that have no objective yet come back Fixed, so the graph still hangs together and
    // the run stops where the capability does.
    public static Progression Read(Mod mod, IRecipeTree recipes, ITerrain terrain,
        IInventory bag, IHand hand, IBody body, ISites sites, IDrops drops,
        ICreatures creatures, IClock clock, IJournal journal)
    {
        JsonElement root = Data.Read(mod, "Data/progression.json");
        List<DagNode> nodes = [];
        Dictionary<string, DagNode> byKey = [];

        // Every node before any edge, since the file may name a key before it defines it
        // and nothing here should depend on the order things are written in.
        foreach (JsonElement entry in root.GetProperty("objectives").EnumerateArray())
        {
            DagNode node = new(Wants(entry, recipes, terrain, bag, hand, body, sites, drops,
                creatures, clock, journal));

            if (!byKey.TryAdd(node.Objective.Key, node))
            {
                throw new InvalidOperationException(
                    $"the progression names '{node.Objective.Key}' twice");
            }

            nodes.Add(node);
        }

        int at = 0;
        foreach (JsonElement entry in root.GetProperty("objectives").EnumerateArray())
        {
            foreach (string key in Data.Strings(entry, "requires"))
            {
                // Loudly, because the old shape swallowed it: a requirement naming nothing
                // was never in reached, so the node waiting on it was quietly dead for the
                // whole run and the log read as if the file were fine.
                if (!byKey.TryGetValue(key, out DagNode? before))
                {
                    throw new InvalidOperationException(
                        $"'{nodes[at].Objective.Key}' requires '{key}', which no objective defines");
                }

                nodes[at].Needs(before);
            }

            at++;
        }

        Acyclic(nodes);
        return new Progression(nodes);
    }

    /// <summary>Refuse a graph that waits on itself.</summary>
    // A cycle is invisible at run time: every node in it waits on another that is never
    // reached, so all of them are quietly skipped for ever and the run just stops short
    // with nothing to say. Cheap to find once at load, impossible to notice later.
    private static void Acyclic(IReadOnlyList<DagNode> nodes)
    {
        HashSet<DagNode> settled = [];
        HashSet<DagNode> walking = [];

        foreach (DagNode node in nodes)
        {
            Walk(node, settled, walking);
        }
    }

    private static void Walk(DagNode node, HashSet<DagNode> settled, HashSet<DagNode> walking)
    {
        if (settled.Contains(node))
        {
            return;
        }

        if (!walking.Add(node))
        {
            throw new InvalidOperationException(
                $"the progression waits on itself at '{node.Objective.Key}'");
        }

        foreach (DagNode before in node.DependsOn)
        {
            Walk(before, settled, walking);
        }

        walking.Remove(node);
        settled.Add(node);
    }

    /// <summary>The objective a node describes.</summary>
    private static IObjective Wants(JsonElement entry, IRecipeTree recipes, ITerrain terrain,
        IInventory bag, IHand hand, IBody body, ISites sites, IDrops drops,
        ICreatures creatures, IClock clock, IJournal journal)
    {
        string key = Data.Text(entry, "key");
        string label = Data.Text(entry, "label");

        return Data.Text(entry, "kind") switch
        {
            "Obtain" => Getting(key, label, recipes, terrain, bag, hand, body, sites, drops,
                creatures, clock, journal, Data.Items(entry, "items"),
                Data.Number(entry, "count", 1)),
            "Defeat" => new Defeat(key, label, creatures, bag, hand, journal,
                Data.Creatures(entry, "creatures"), Downed(Data.Text(entry, "downed"))),
            "Supply" => Keeping(entry, key, label, recipes, terrain, bag, hand, body, sites,
                drops, creatures, clock, journal),
            _ => throw new InvalidOperationException(
                $"'{key}' is of kind '{Data.Text(entry, "kind")}', which nothing builds"),
        };
    }

    /// <summary>Wanting a number of something, told which stations its recipes stand at.</summary>
    // Worked out here and once, at load. The stations are a fact about what the run makes,
    // so they do not change while it plays, and a sweep of the ground for one of them costs
    // what the list is long.
    private static Obtain Getting(string key, string label, IRecipeTree recipes,
        ITerrain terrain,
        IInventory bag, IHand hand, IBody body, ISites sites, IDrops drops,
        ICreatures creatures, IClock clock, IJournal journal, IReadOnlyList<int> items,
        int count)
    {
        Dictionary<int, int> stations = [];
        foreach (int item in items)
        {
            foreach (KeyValuePair<int, int> station in
                RecipeTree.Stations(recipes.Of(item, Obtain.Deep)))
            {
                stations[station.Key] = station.Value;
            }
        }

        return new Obtain(key, label, recipes, terrain, bag, hand, body, sites, drops,
            creatures, clock, journal, items, stations, count);
    }

    /// <summary>A supply, and the getting of the item that fills it.</summary>
    private static IObjective Keeping(JsonElement entry, string key, string label,
        IRecipeTree recipes,
        ITerrain terrain, IInventory bag, IHand hand, IBody body, ISites sites, IDrops drops,
        ICreatures creatures, IClock clock, IJournal journal)
    {
        int item = Data.Item(Data.Text(entry, "item"));
        int ceiling = Data.Number(entry, "ceiling", 1);

        return new Supply(key, label, bag,
            Getting(key, label, recipes, terrain, bag, hand, body, sites, drops, creatures,
                clock, journal, [item], ceiling),
            item, Data.Number(entry, "restockAt", 0), ceiling,
            Data.Number(entry, "reserve", 0));
    }

    /// <summary>The game's own flag for a boss having been killed.</summary>
    // Named in the file rather than derived from the key, since the flags are a handful of
    // statics with no pattern to them and hardmode is not called downedBoss4.
    private static Func<bool> Downed(string flag) => flag switch
    {
        "boss1" => () => NPC.downedBoss1,
        "boss2" => () => NPC.downedBoss2,
        "boss3" => () => NPC.downedBoss3,
        "hardmode" => () => Main.hardMode,
        _ => () => false,
    };
}
