using System.Collections.Generic;
using Terraria;
using Terraria.ID;

namespace Terragent.Progression;

/// <summary>
/// Which goals the agent's actual situation already meets.
/// </summary>
internal static class Satisfaction
{
    public static HashSet<string> Of(ProgressionGraph graph, Character character)
    {
        HashSet<string> done = [];
        foreach (Node node in graph.Nodes)
        {
            if (IsMet(node, character))
            {
                done.Add(node.Key);
            }
        }

        return done;
    }

    private static bool IsMet(Node node, Character character) => node.Kind switch
    {
        NodeKind.Item => Carried(character, node) >= node.Count,
        NodeKind.Boss => BossDown(node.Key),
        _ => MilestoneMet(node.Key, character),
    };


    /// <summary>How many of what this node accepts the character is carrying.</summary>
    private static int Carried(Character character, Node node) =>
        node.Interchangeable
            ? character.Inventory.Carrying(node.Any)
            : character.Inventory.Carrying(node.ItemID);

    private static bool BossDown(string key) => key switch
    {
        "eoc" => NPC.downedBoss1,
        "evil_boss" => NPC.downedBoss2,
        "skeletron_boss" => NPC.downedBoss3,
        "wall_of_flesh" => Main.hardMode,
        _ => false,
    };

    private static bool MilestoneMet(string key, Character character) => key switch
    {
        // The agent starts with these; there is nothing to check.
        "starting_tools" => true,

        // Stations are not milestones: they are carried tools, put down where a recipe
        // needs one and picked up afterwards.

        "hp_200" => character.Player.statLifeMax >= 200,
        _ => false,
    };
}
