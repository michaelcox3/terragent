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
internal sealed class Progression(IReadOnlyList<Node> nodes) : IProgression
{
    private readonly IReadOnlyList<Node> _nodes = nodes;

    /// <summary>Keys reached, which is the only thing here that outlives a tick.</summary>
    private readonly HashSet<string> _reached = [];

    public IReadOnlyList<Node> Nodes => _nodes;

    public IReadOnlyCollection<string> Reached => _reached;

    public IObjective? Next()
    {
        // Supplies first. A restock that waited its turn behind the objective would wait
        // for ever, since the objective is what spends the torches.
        //
        // Later replaces earlier for the same item, so the node that makes sixty torches
        // affordable retires the one that asked for five. Order in the file is the rule,
        // which is why no entry carries a tier.
        Dictionary<int, Supply> keeping = [];
        foreach (Node node in _nodes)
        {
            if (node.Standing && node.Objective is Supply keep && Ready(node))
            {
                keeping[keep.ItemID] = keep;
            }
        }

        foreach (Supply keep in keeping.Values)
        {
            if (!keep.Met)
            {
                return keep;
            }
        }

        foreach (Node node in _nodes)
        {
            if (!node.Standing && !_reached.Contains(node.Key) && Ready(node))
            {
                return node.Objective;
            }
        }

        return null;
    }

    public void Record()
    {
        foreach (Node node in _nodes)
        {
            // Supplies are never recorded. Going short again is the whole point of one,
            // and latching it would mean the run stocked torches once and then went dark.
            //
            // The one place a kind is asked about rather than the interface: reviewing is
            // what keeps the low trigger and the high fill apart, and no other objective
            // has anything to review.
            if (node.Standing)
            {
                if (node.Objective is Supply keep && Ready(node))
                {
                    keep.Review();
                }

                continue;
            }

            // Every unreached node, not only the one being worked. A pickaxe comes out of
            // a chest, a boss flag gets set by something else, and wood is a side effect
            // of clearing a route: none of those are noticed by asking about one node.
            if (!_reached.Contains(node.Key) && node.Objective.Met)
            {
                _reached.Add(node.Key);
            }
        }
    }

    public void Restore(IEnumerable<string> reached) => _reached.UnionWith(reached);

    /// <summary>Whether everything this node waits on has been reached.</summary>
    private bool Ready(Node node)
    {
        foreach (string key in node.Requires)
        {
            if (!_reached.Contains(key))
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
        List<Node> nodes = [];

        foreach (JsonElement entry in root.GetProperty("objectives").EnumerateArray())
        {
            nodes.Add(new Node(
                Data.Text(entry, "key"),
                Wants(entry, recipes, terrain, bag, hand, body, sites, drops, creatures,
                    clock, journal),
                Data.Strings(entry, "requires"),
                Data.Text(entry, "kind") == "Supply"));
        }

        return new Progression(nodes);
    }

    /// <summary>The objective a node describes.</summary>
    private static IObjective Wants(JsonElement entry, IRecipeTree recipes, ITerrain terrain,
        IInventory bag, IHand hand, IBody body, ISites sites, IDrops drops,
        ICreatures creatures, IClock clock, IJournal journal)
    {
        string label = Data.Text(entry, "label");

        return Data.Text(entry, "kind") switch
        {
            "Obtain" => new Obtain(label, recipes, terrain, bag, hand, body, sites, drops,
                creatures, clock, journal, Data.Items(entry, "items"),
                Data.Number(entry, "count", 1)),
            "Defeat" => new Defeat(label, creatures, bag, hand, journal,
                Data.Creatures(entry, "creatures"), Downed(Data.Text(entry, "downed"))),
            "Supply" => Keeping(entry, label, recipes, terrain, bag, hand, body, sites,
                drops, creatures, clock, journal),
            _ => new Gap(label),
        };
    }

    /// <summary>A supply, and the getting of the item that fills it.</summary>
    private static IObjective Keeping(JsonElement entry, string label, IRecipeTree recipes,
        ITerrain terrain, IInventory bag, IHand hand, IBody body, ISites sites, IDrops drops,
        ICreatures creatures, IClock clock, IJournal journal)
    {
        int item = Data.Item(Data.Text(entry, "item"));
        int ceiling = Data.Number(entry, "ceiling", 1);

        return new Supply(label, bag,
            new Obtain(label, recipes, terrain, bag, hand, body, sites, drops, creatures,
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
