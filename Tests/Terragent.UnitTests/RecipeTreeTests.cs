using System;
using System.Collections.Generic;
using System.Linq;
using Terragent.World;

namespace Terragent.UnitTests;

/// <summary>What an iron pickaxe comes down to.</summary>
// Terraria.s own numbers, written out rather than read. A pickaxe is twelve iron bars and
// three wood at an anvil, a bar is three ore at a furnace, an anvil is five bars at a work
// bench, a bench is ten wood, and a furnace is twenty stone, four wood and three torches,
// also at a bench. The recipe group that lets lead stand in for iron is here too, because
// it is the difference between a run finishing and one digging for ore its world has none
// of.
public class RecipeTreeTests
{
    private const int IronPickaxe = 1;
    private const int IronBar = 2;
    private const int LeadBar = 3;
    private const int IronOre = 4;
    private const int LeadOre = 5;
    private const int Wood = 6;
    private const int WorkBench = 7;
    private const int IronAnvil = 8;
    private const int Furnace = 9;
    private const int Stone = 10;
    private const int WoodPlatform = 11;
    private const int Torch = 12;
    private const int Gel = 13;
    private const int LeadPickaxe = 14;
    private const int LeadAnvil = 15;

    private const int Anvils = 100;
    private const int Furnaces = 101;
    private const int WorkBenches = 102;

    /// <summary>What the run digs, chops or kills for rather than crafting.</summary>
    private static readonly HashSet<int> Found = [IronOre, LeadOre, Wood, Stone, Gel];

    private static readonly List<CraftingRecipe> Book =
    [
        Craft(IronPickaxe, [(IronBar, 12, [LeadBar]), (Wood, 3, [])], [(IronAnvil, [LeadAnvil], Anvils)]),

        // The same tool in the other metal, which is what an objective names beside the
        // first: either one ends the errand, and a world grows one of them.
        Craft(LeadPickaxe, [(LeadBar, 12, []), (Wood, 3, [])], [(IronAnvil, [LeadAnvil], Anvils)]),
        Craft(IronBar, [(IronOre, 3, [])], [(Furnace, [], Furnaces)], yields: 1),
        Craft(LeadBar, [(LeadOre, 3, [])], [(Furnace, [], Furnaces)], yields: 1),
        Craft(IronAnvil, [(IronBar, 5, [LeadBar])], [(WorkBench, [], WorkBenches)]),

        // The same tile in the other metal. One tile is anvils and two items place it, so
        // a lead world can stand one up out of what it has.
        Craft(LeadAnvil, [(LeadBar, 5, [IronBar])], [(WorkBench, [], WorkBenches)]),
        Craft(Furnace, [(Stone, 20, []), (Wood, 4, []), (Torch, 3, [])],
            [(WorkBench, [], WorkBenches)]),
        Craft(WorkBench, [(Wood, 10, [])], []),

        // Three at a time, which is why wanting three is one craft and one gel.
        Craft(Torch, [(Wood, 1, []), (Gel, 1, [])], [], yields: 3),

        // The trap this exists to avoid: wood's only recipe turns two platforms back into
        // one wood, and platforms are made of wood. Following it makes wood craftable.
        Craft(Wood, [(WoodPlatform, 2, [])], []),
        Craft(WoodPlatform, [(Wood, 1, [])], [], yields: 2),
    ];

    /// <summary>One row of the book: what it makes, from what, at what.</summary>
    private static CraftingRecipe Craft(int makes,
        (int ItemID, int Count, IReadOnlyList<int> Instead)[] items,
        (int ItemID, IReadOnlyList<int> Instead, int TileID)[] stations,
        int yields = 1) =>
        new(makes, yields, items, stations);

    private static RecipeNode Pickaxe() => new RecipeTree(Book, Found.Contains).Of(IronPickaxe, 6);

    [Fact]
    public void ThePickaxeIsCraftedAtAnAnvil()
    {
        RecipeNode tree = Pickaxe();

        Assert.Equal(IronPickaxe, tree.ItemID);
        Assert.False(tree.Raw);
        Assert.Equal([(IronAnvil, Anvils)], [.. Stations(tree)]);
    }

    [Fact]
    public void ItWantsTwelveBarsAndThreeWood()
    {
        RecipeNode tree = Pickaxe();

        Assert.Equal(12, Wants(tree, IronBar));
        Assert.Equal(3, Wants(tree, Wood));
        Assert.Equal(2, tree.Needs.Count);
    }

    [Fact]
    public void LeadBarsWouldDoInsteadOfIron()
    {
        Assert.Equal([LeadBar], [.. Instead(Pickaxe(), IronBar)]);
        Assert.Empty(Instead(Pickaxe(), Wood));
    }

    [Fact]
    public void OreAndWoodAreTheLeaves()
    {
        RecipeNode tree = Pickaxe();

        Assert.True(Under(tree, Wood).Raw);
        Assert.False(Under(tree, IronBar).Raw);
        Assert.True(Under(Under(tree, IronBar), IronOre).Raw);
    }

    [Fact]
    public void TheAnvilCarriesItsOwnTreeDownToWood()
    {
        // The station is a thing to obtain, not a condition: an anvil is five bars at a
        // bench, and the bench is ten wood, so wanting a pickaxe wants wood twice over.
        RecipeNode anvil = Under(Pickaxe(), IronAnvil);

        Assert.Equal([(WorkBench, WorkBenches)], [.. Stations(anvil)]);
        Assert.Equal(5, Wants(anvil, IronBar));
        Assert.True(Under(Under(anvil, WorkBench), Wood).Raw);
    }

    [Fact]
    public void WoodIsNeverCraftedFromPlatformsMadeOfWood()
    {
        // Two recipes produce each other. Following either makes both craftable and the
        // agent never goes and chops anything.
        Assert.True(Under(Pickaxe(), Wood).Raw);
        Assert.Null(new RecipeTree(Book, Found.Contains).Chosen(Wood));
    }

    [Fact]
    public void ADepthOfNoneIsALeafWhateverMakesIt()
    {
        Assert.True(new RecipeTree(Book, Found.Contains).Of(IronPickaxe, 0).Raw);
    }

    /// <summary>The named option of each needed item, so a count can be asserted.</summary>
    // The walk answers with alternatives now. These tests are about the arithmetic, so
    // they read the option the recipe names and OreOrItsStandIn covers the rest.
    private static Dictionary<int, int> Totals(IReadOnlyList<NeededItem> missing)
    {
        Dictionary<int, int> totals = [];
        foreach (NeededItem want in missing)
        {
            totals[want.ItemID] = want.Count;
        }

        return totals;
    }

    /// <summary>What an objective naming both pickaxes comes down to.</summary>
    // The shape Obtain hands the walk: the first item as the root, the rest of the same
    // want as stand-ins beside it. Written out so a change to the alternatives shows up as
    // a diff rather than as a count nobody can place.
    [Fact]
    public void EitherPickaxeIsOneErrand()
    {
        RecipeTree book = new(Book, Found.Contains);
        IReadOnlyList<NeededItem> missing = RecipeTree.Missing(
            [(book.Of(IronPickaxe, 6), 1, new[] { book.Of(LeadPickaxe, 6) })],
            _ => 0,
            _ => false);

        List<string> lines = [];
        foreach (NeededItem want in missing)
        {
            List<string> options = [];
            foreach ((int itemID, int count) in want.Options)
            {
                options.Add($"{count}x {Names[itemID]}");
            }

            lines.Add(string.Join(" or ", options));
        }

        Assert.Equal(
            """
            18x Wood
            20x Stone
            1x Gel
            51x Iron Ore or 51x Lead Ore
            """,
            string.Join("\n", lines));
    }

    /// <summary>Ore in hand of either metal ends the errand for both.</summary>
    // Options are two ways of filling one need, so filling either fills it. Reported per
    // branch, a run holding all the lead it could want was still told it needed forty five
    // iron ore, and went looking for iron in a world that has none.
    [Fact]
    public void OreOfEitherMetalSettlesTheNeed()
    {
        RecipeTree book = new(Book, Found.Contains);
        (RecipeNode, int, IReadOnlyList<RecipeNode>)[] pickaxe =
            [(book.Of(IronPickaxe, 6), 1, new[] { book.Of(LeadPickaxe, 6) })];

        // Nothing carried: either metal would do, and both are named.
        NeededItem ore = RecipeTree.Missing(pickaxe, _ => 0, _ => false)
            .First(want => want.ItemID == IronOre);
        Assert.Equal([(IronOre, 51), (LeadOre, 51)], [.. ore.Options]);

        // All the lead in hand, and the ore is not wanted at all any more.
        IReadOnlyList<NeededItem> full = RecipeTree.Missing(
            pickaxe, item => item == LeadOre ? 51 : 0, _ => false);

        Assert.DoesNotContain(full, want => want.ItemID == IronOre);
        Assert.DoesNotContain(full, want => want.ItemID == LeadOre);
    }

    /// <summary>One bench is charged once for everything made at it.</summary>
    // Six pieces of gold armour are one objective and one anvil, not six. Walked one at a
    // time the bench under each is a fresh ten wood, so the run is told to gather sixty for
    // a bench it will only ever build one of, and the trip does not finish until it has
    // them. Walked together the stations already counted are shared and the bench is paid
    // for once.
    [Fact]
    public void OneBenchIsChargedOnceForEverythingMadeAtIt()
    {
        RecipeTree book = new(Book, Found.Contains);
        RecipeNode iron = book.Of(IronBar, 6);
        RecipeNode lead = book.Of(LeadBar, 6);

        // Ten wood for the bench and five for the furnace stood on it, either way round.
        // Walked apart each pays the whole fifteen, so added up the run is told to gather
        // thirty for one furnace.
        Assert.Equal(15, Totals(RecipeTree.Missing(iron, 3, _ => 0, _ => false))[Wood]);
        Assert.Equal(15, Totals(RecipeTree.Missing(lead, 3, _ => 0, _ => false))[Wood]);

        Dictionary<int, int> together = Totals(RecipeTree.Missing(
            [(iron, 3, []), (lead, 3, [])], _ => 0, _ => false));

        Assert.Equal(15, together[Wood]);

        // The ore is not shared, because ore is spent and a station is not. Three bars of
        // each is nine of each however they are walked.
        Assert.Equal(9, together[IronOre]);
        Assert.Equal(9, together[LeadOre]);
    }

    /// <summary>A recipe group comes back as one need with two ways to fill it.</summary>
    // Not two entries. The pickaxe wants twelve bars of iron or of lead, so the ore under
    // them is one thing to go and get, and a world grows one metal or the other. Flattened
    // to a map the "or" had nowhere to live and the stand-in was thrown away, which left
    // the run mining lead only because the progression happened to name a lead pickaxe as
    // a second objective.
    [Fact]
    public void OreComesBackWithItsStandIn()
    {
        IReadOnlyList<NeededItem> missing =
            RecipeTree.Missing(Pickaxe(), 1, _ => 0, _ => false);

        NeededItem ore = missing.First(want => want.ItemID == IronOre);

        Assert.Equal([(IronOre, 51), (LeadOre, 51)], [.. ore.Options]);
    }

    /// <summary>The whole answer for one item, written out so it can be read.</summary>
    // The other tests each check one thing. This one is here to be looked at: the input is
    // the table above, the output is below, and a change to the walk shows up as a diff of
    // the shape rather than as a failed assertion about a count.
    [Fact]
    public void TheWholeTreeForAnIronPickaxe()
    {
        Assert.Equal(
            """
            Iron Pickaxe
              at Anvils: Iron Anvil
                at Work Benches: Work Bench
                  10x Wood (raw)
                5x Iron Bar (or Lead Bar)
                  at Furnaces: Furnace
                    at Work Benches: Work Bench
                      10x Wood (raw)
                    20x Stone (raw)
                    4x Wood (raw)
                    3x Torch
                      1x Wood (raw)
                      1x Gel (raw)
                  3x Iron Ore (raw)
              12x Iron Bar (or Lead Bar)
                at Furnaces: Furnace
                  at Work Benches: Work Bench
                    10x Wood (raw)
                  20x Stone (raw)
                  4x Wood (raw)
                  3x Torch
                    1x Wood (raw)
                    1x Gel (raw)
                3x Iron Ore (raw)
              3x Wood (raw)
            """,
            Draw(Pickaxe(), 0).TrimEnd());
    }

    /// <summary>What one iron pickaxe costs, all the way down.</summary>
    // The whole reason the walk builds a tree rather than answering a step at a time. The
    // pickaxe wants twelve bars and the anvil it is made at wants five more, so the ore is
    // one number and one trip. Split across two answers the agent walks to the vein twice.
    [Fact]
    public void OneIronPickaxeCostsFiftyOneOre()
    {
        Dictionary<int, int> raw = Totals(RecipeTree.Missing(Pickaxe(), 1, _ => 0, _ => false));

        // Twelve bars for the pickaxe and five for the anvil, three ore each.
        Assert.Equal(51, raw[IronOre]);

        // Ten for the bench, four for the furnace, one for the torches, three for the
        // pickaxe itself. The bench is counted once, though three things are made at it.
        Assert.Equal(18, raw[Wood]);

        Assert.Equal(20, raw[Stone]);

        // Three torches come out of one craft, which takes one gel and not three.
        Assert.Equal(1, raw[Gel]);
    }

    /// <summary>Carrying some of it takes the rest off, once.</summary>
    // Five bars in hand serve the pickaxe or the anvil and not both. Spent on the anvil,
    // which the walk reaches first, they leave the pickaxe wanting all twelve, so the ore
    // falls from fifty one to thirty six rather than to twenty one.
    [Fact]
    public void CarriedBarsAreSpentOnce()
    {
        Dictionary<int, int> left =
            Totals(RecipeTree.Missing(Pickaxe(), 1, item => item == IronBar ? 5 : 0,
                _ => false));

        Assert.Equal(36, left[IronOre]);
    }

    /// <summary>An anvil in the bag prunes the anvil and everything under it.</summary>
    [Fact]
    public void OwningTheAnvilRemovesItsWholeBranch()
    {
        Dictionary<int, int> left =
            Totals(RecipeTree.Missing(Pickaxe(), 1, item => item == IronAnvil ? 1 : 0,
                _ => false));

        // Thirty six for the pickaxe, and none of the anvil's fifteen.
        Assert.Equal(36, left[IronOre]);

        // The bench survives. It stood under the anvil and under the furnace, and the
        // bars still need the furnace, so the ten wood is still owed.
        Assert.Equal(18, left[Wood]);
    }

    /// <summary>Having the thing already leaves nothing to do.</summary>
    [Fact]
    public void CarryingThePickaxeLeavesNothing() =>
        Assert.Empty(
            RecipeTree.Missing(Pickaxe(), 1, item => item == IronPickaxe ? 1 : 0, _ => false));

    /// <summary>Crafting happens bottom up, deepest thing first.</summary>
    // With everything raw in hand, the first thing to make is the bench, because the
    // furnace is made at one and the bars are smelted in that. Asking the game for the
    // pickaxe here gets a no with nothing to do about it.
    /// <summary>Every station the whole tree stands at, however deep it sits.</summary>
    // The anvil is worked at a bench and the pickaxe at the anvil, so a pickaxe run stands
    // at both. Reading only the top node gives the anvil alone, and the bench it put down
    // to make that anvil is then left in the world for ever.
    [Fact]
    public void StationsAreCollectedFromTheWholeTree()
    {
        IReadOnlyDictionary<int, IReadOnlyList<int>> stations = RecipeTree.Stations(Pickaxe());

        Assert.Equal([WorkBench], stations[WorkBenches]);
        Assert.Equal([Furnace], stations[Furnaces]);

        // Two items place one tile, and both are kept: a lead world stands its anvil up
        // out of the lead it has rather than owing five iron bars it can never get.
        Assert.Equal([IronAnvil, LeadAnvil], stations[Anvils]);
    }

    [Fact]
    public void TheBenchIsMadeFirst()
    {
        int Held(int item) => item switch
        {
            Wood => 100,
            Stone => 100,
            IronOre => 100,
            Gel => 10,
            _ => 0,
        };

        Assert.Equal(WorkBench, RecipeTree.Craftable(Pickaxe(), 1, Held, _ => false)!.Value.Node.ItemID);
    }

    /// <summary>Nothing is makeable while a part is still in the ground.</summary>
    [Fact]
    public void NothingIsMadeWithoutTheMaterials() =>
        Assert.Null(RecipeTree.Craftable(Pickaxe(), 1, _ => 0, _ => false));

    /// <summary>With the bench and the furnace up, the bars come next.</summary>
    [Fact]
    public void BarsComeAfterTheStations()
    {
        int Held(int item) => item switch
        {
            Wood => 100,
            IronOre => 100,
            WorkBench => 1,
            Furnace => 1,
            _ => 0,
        };

        Assert.Equal(IronBar, RecipeTree.Craftable(Pickaxe(), 1, Held, _ => false)!.Value.Node.ItemID);
    }

    /// <summary>Holding exactly enough is holding enough.</summary>
    // The walk spends the bag as it goes, and asking a second time whether a part is short
    // once it has been spent reads what it took as what is missing. One gel, needed once,
    // came back as one gel short, and the torch it was for read as unmakeable while nothing
    // at all was reported missing. The run then had no jobs and stood still.
    [Fact]
    public void ExactlyEnoughIsEnough()
    {
        int Held(int item) => item switch
        {
            Wood => 1,
            Gel => 1,
            _ => 0,
        };

        RecipeNode tree = new RecipeTree(Book, Found.Contains).Of(Torch, 6);

        Assert.Empty(RecipeTree.Missing(tree, 1, Held, _ => false));
        Assert.Equal(Torch, RecipeTree.Craftable(tree, 1, Held, _ => false)!.Value.Node.ItemID);
    }

    private static readonly Dictionary<int, string> Names = new()
    {
        [IronPickaxe] = "Iron Pickaxe", [IronBar] = "Iron Bar", [LeadBar] = "Lead Bar",
        [IronOre] = "Iron Ore", [LeadOre] = "Lead Ore", [Wood] = "Wood",
        [WorkBench] = "Work Bench", [IronAnvil] = "Iron Anvil", [Furnace] = "Furnace",
        [Stone] = "Stone", [WoodPlatform] = "Wood Platform",
        [Torch] = "Torch", [Gel] = "Gel", [LeadPickaxe] = "Lead Pickaxe",
        [LeadAnvil] = "Lead Anvil",
        [Anvils] = "Anvils", [Furnaces] = "Furnaces", [WorkBenches] = "Work Benches",
    };

    private static string Draw(RecipeNode node, int depth, string before = "", string after = "")
    {
        System.Text.StringBuilder lines = new();
        lines.Append(new string(' ', depth * 2))
            .Append(before)
            .Append(Names[node.ItemID])
            .Append(after)
            .Append(node.Raw ? " (raw)" : string.Empty)
            .Append('\n');

        foreach ((RecipeNode station, IReadOnlyList<RecipeNode> _, int tileID) in node.Stations)
        {
            lines.Append(Draw(station, depth + 1, $"at {Names[tileID]}: "));
        }

        foreach ((RecipeNode need, int count, IReadOnlyList<RecipeNode> instead) in node.Needs)
        {
            List<string> others = [];
            foreach (RecipeNode other in instead)
            {
                others.Add(Names[other.ItemID]);
            }

            lines.Append(Draw(need, depth + 1, $"{count}x ",
                others.Count == 0 ? string.Empty : $" (or {string.Join(", ", others)})"));
        }

        return lines.ToString();
    }

    private static IEnumerable<(int ItemID, int TileID)> Stations(RecipeNode node)
    {
        foreach ((RecipeNode station, IReadOnlyList<RecipeNode> _, int tileID) in node.Stations)
        {
            yield return (station.ItemID, tileID);
        }
    }

    /// <summary>How many of an item a node calls for.</summary>
    private static int Wants(RecipeNode node, int itemID)
    {
        foreach ((RecipeNode need, int count, IReadOnlyList<RecipeNode> _) in node.Needs)
        {
            if (need.ItemID == itemID)
            {
                return count;
            }
        }

        return 0;
    }

    private static IEnumerable<int> Instead(RecipeNode node, int itemID)
    {
        foreach ((RecipeNode need, int _, IReadOnlyList<RecipeNode> instead) in node.Needs)
        {
            if (need.ItemID != itemID)
            {
                continue;
            }

            foreach (RecipeNode other in instead)
            {
                yield return other.ItemID;
            }
        }
    }

    /// <summary>A child of this node, station or material, by item.</summary>
    private static RecipeNode Under(RecipeNode node, int itemID)
    {
        foreach ((RecipeNode station, IReadOnlyList<RecipeNode> _, int _) in node.Stations)
        {
            if (station.ItemID == itemID)
            {
                return station;
            }
        }

        foreach ((RecipeNode need, int _, IReadOnlyList<RecipeNode> _) in node.Needs)
        {
            if (need.ItemID == itemID)
            {
                return need;
            }
        }

        Assert.Fail($"{itemID} is not under {node.ItemID}");
        return null!;
    }
}
