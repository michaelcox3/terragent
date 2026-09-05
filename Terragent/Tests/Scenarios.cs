#if TESTING
using System.Collections.Generic;

namespace Terragent;

/// <summary>The kinds of move a route can be made of.</summary>
[System.Flags]
internal enum Move
{
    None = 0,

    /// <summary>Walking, stepping up, and falling off things. Costs nothing.</summary>
    Walk = 1,

    /// <summary>Leaving the ground on purpose to reach a ledge.</summary>
    Jump = 2,

    /// <summary>Breaking a block to get through it.</summary>
    Mine = 4,

    /// <summary>Putting a block down to stand on it. Pillaring.</summary>
    Build = 8,
}

/// <summary>
/// One pathfinding problem: a picture of some ground, and what the right answer does.
/// </summary>
/// <param name="Walk">Whether walking, stepping up and falling are allowed.</param>
/// <param name="Jump">Whether leaving the ground is allowed.</param>
/// <param name="Mine">Whether breaking blocks is part of the right answer.</param>
/// <param name="Build">Whether placing blocks is part of the right answer.</param>
internal sealed record Case(string Name, bool Walk, bool Jump, bool Mine, bool Build,
    string Why, string[] Rows)
{
    /// <summary>The four answers as a set, for comparing against a route.</summary>
    public Move Does =>
        (Walk ? Move.Walk : Move.None)
        | (Jump ? Move.Jump : Move.None)
        | (Mine ? Move.Mine : Move.None)
        | (Build ? Move.Build : Move.None);

    /// <summary>True when no route is the correct answer.</summary>
    public bool Unreachable { get; init; }

    /// <summary>
    /// Seconds the arena gives this scenario before calling it failed.
    /// </summary>
    public int Seconds { get; init; } = 10;

    /// <summary>Whether the arena walls the grid in. On unless a scenario says not.</summary>
    // The surround is Ebonstone the agent cannot mine, and it brings a floor with it,
    // which is a thing to land on and pillar off. A scenario about crossing a gap does
    // not want one underneath it. Only the arena builds this; the headless harness has
    // nothing outside a grid either way.
    public bool Border { get; init; } = true;

    /// <summary>Steps the route may not exceed, when it matters.</summary>
    public int MaxSteps { get; init; } = int.MaxValue;

    /// <summary>Which marker is the goal. '@' asks for a route to where we already are.</summary>
    public char Goal { get; init; } = 'G';

    /// <summary>
    /// Blocks the character carries. Zero forbids pillaring.
    /// </summary>
    public int Blocks { get; init; } = 200;
}

/// <summary>
/// Small pathfinding problems with known answers.
/// </summary>
internal static class Scenarios
{
    public static IReadOnlyList<Case> All { get; } = new Case[]
    {
        new("flat ground",
            Walk: true, Jump: false, Mine: false, Build: false, "the simplest possible route",
        [
            "............",
            "............",
            "............",
            "@..........G",
            "############",
        ]),

        new("one step up",
            Walk: true, Jump: false, Mine: false, Build: false,
            "the game steps up a single tile for free, so it is a walk and not a jump. "
            + "This grid used to put the goal a row *below* the start, which tested "
            + "stepping down under the name of stepping up",
        [
            "..............",
            "..............",
            "..............",
            "..........G...",
            "@.........####",
            "##############",
        ]),

        new("one step down",
            Walk: true, Jump: false, Mine: false, Build: false, "walking off a one-tile ledge",
        [
            "............",
            "............",
            "............",
            "@...........",
            "#####.......",
            "#####......G",
            "############",
        ]),

        new("goal is where we stand",
            Walk: false, Jump: false, Mine: false, Build: false,
            "an empty route is arrival, not failure, and Pilot read the two as one",
        [
            "............",
            "............",
            "............",
            "@...........",
            "############",
        ]) { MaxSteps = 0, Goal = '@' },

        new("two-row tunnel is too short",
            Walk: true, Jump: false, Mine: true, Build: false,
            "42 pixels of body will not fit in 32 pixels of gap. The way through is "
            + "over the wall and down the far side. No blocks are named because no "
            + "particular block has to go: the wall can be crossed anywhere along it, "
            + "and pinning cells here would assert which route was chosen rather than "
            + "that the route is sound. Where the shape is the point, the geometry "
            + "forces it. See the three cases below",
        [
            "############",
            "#..........#",
            "#..........#",
            "#..........#",
            "#@...#######",
            "######.....#",
            "######....G#",
            "############",
        ]),

        new("three-row tunnel fits",
            Walk: true, Jump: false, Mine: false, Build: false, "48 pixels of gap is enough",
        [
            "############",
            "############",
            "#..........#",
            "#..........#",
            "#@........G#",
            "############",
        ]),

        new("one-wide slot is not standable",
            Walk: true, Jump: false, Mine: true, Build: false,
            "20 pixels of body cannot sit in a 16 pixel column, so both columns of the "
            + "wall come out, over all three rows the body occupies. Six blocks. "
            + "Asserting only that it dug would have passed a route that cut one "
            + "column and left a slot the character cannot enter",
        [
            "XXXXXXXXXXXX",
            "XXXXXXXXXXXX",
            "X....dd....X",
            "X....dd....X",
            "X@...dd...GX",
            "XXXXXXXXXXXX",
        ]),

        // Body size. Three rows tall, two columns wide, always.
        // Jumps: rise, cross, land. From the hill loop, run 20:02.
        new("jump to a ledge",
            Walk: true, Jump: true, Mine: false, Build: false,
            "two tiles up is past what a free step-up covers, so this one really does "
            + "jump. The grid it replaces had the goal a row below the start and never "
            + "jumped at all",
        [
            "..............",
            "..............",
            "..............",
            "..........G...",
            "..........####",
            "@.........####",
            "##############",
        ]),

        new("a four-tile ledge is jumped, not pillared",
            Walk: true, Jump: true, Mine: false, Build: false,
            "four tiles used to be out of reach and this scenario used to spend blocks "
            + "on it, because the jump was hard-coded at three. Measured from the "
            + "game (jumpSpeed held for jumpHeight frames, then coasting against "
            + "gravity) it is closer to six, so a four-tile ledge is a hop. Placing "
            + "a block to reach something you can jump to is time and stone spent on "
            + "nothing",
        [
            "............",
            "............",
            "..........G.",
            "..........##",
            "..........##",
            "..........##",
            "@.........##",
            "############",
        ]),

        new("pillar up to a plateau",
            Walk: true, Jump: true, Mine: false, Build: true,
            "eight rows, which is past what a jump reaches even measured generously, "
            + "and a face of rock too tall to stair up cheaply. Blocks are the answer "
            + "here and only here. The four-tile ledge above is a hop, and this "
            + "scenario had to grow when the jump height was corrected, because at "
            + "four rows it had stopped testing pillaring at all",
        [
            "..............",
            "..............",
            "..............",
            ".............G",
            "......HHHHHHHH",
            "......HHHHHHHH",
            "......HHHHHHHH",
            "......HHHHHHHH",
            "......HHHHHHHH",
            "......HHHHHHHH",
            "......HHHHHHHH",
            "@.....HHHHHHHH",
            "##############",
        ]) { Seconds = 20 },

        new("drop into a two-wide shaft",
            Walk: true, Jump: false, Mine: false, Build: false, "the body fits, so it may fall",
        [
            "@...........",
            "#..#########",
            "#..#########",
            "#..#########",
            "#.G#########",
            "############",
        ]),

        new("a one-wide hole is widened, not refused",
            Walk: true, Jump: false, Mine: true, Build: false,
            "sixteen pixels for a twenty pixel body, so it cannot be dropped into "
            + "however deep it goes, but the column beside a crack is ordinary rock, "
            + "and four blocks turn a crack into a shaft. Refusing was right and "
            + "stopping there was not: it walked away from a goal four swings beneath "
            + "it. Ebonstone on the far side settles which way it widens, so the "
            + "scenario names one answer and gets it for a reason. There used to be a "
            + "parity rule for that, which the follower had to reproduce exactly and "
            + "did not; a footing names both its columns, so the plan simply says",
        [
            "@...........",
            "#####d.H####",
            "#####d.H####",
            "#####d.H####",
            "#####dGH####",
            "############",
        ]),

        new("walking beats digging",
            Walk: true, Jump: false, Mine: false, Build: false,
            "the whole reason mining is an edge and not a rule",
        [
            "............",
            "............",
            "........#...",
            "@..#...##..G",
            "############",
            "############",
        ]),

        new("dig down to a buried goal",
            Walk: true, Jump: false, Mine: true, Build: false,
            "nothing else will reach it. Which column the shaft goes down is the "
            + "search's business; that it is two columns wide is asserted where the "
            + "geometry forces one answer",
        [
            "............",
            "............",
            "@...........",
            "############",
            "############",
            "#..........#",
            "#..........#",
            "#.........G#",
            "############",
        ]),

        new("a tree is gone round, not through",
            Walk: true, Jump: false, Mine: true, Build: false,
            "run 17:34: twenty-two seconds swinging at a tile the game refuses. The "
            + "pocket is ordinary rock on every side and the floor is protected, so "
            + "the only descent there is cannot be made, which is the point, since "
            + "the refusal is silent in game and looks exactly like slow mining. So "
            + "the route leaves by the side instead, and the invariant that no route "
            + "may plan to break what the pickaxe cannot is what actually holds it, "
            + "for every case here rather than only this one. "
            + "It expected None before, on a grid whose goal sat under a ceiling: "
            + "refused for being unstandable, with the tree never tested at all",
        [
            "############",
            "##..########",
            "##..########",
            "##@.########",
            "##XX########",
            "#..........#",
            "#.........G#",
            "############",
        ]),

        new("too hard for this pickaxe",
            Walk: false, Jump: false, Mine: false, Build: false,
            "Ebonstone wants 65 and copper is 35. Progression expressed as physics: "
            + "the corruption is shut until the agent has been to a boss and back, and "
            + "the search should see a wall rather than carry that rule separately",
        [
            "@...........",
            "HHHHHHHHHHHH",
            "H..........H",
            "H..........H",
            "H....G.....H",
            "HHHHHHHHHHHH",
        ]) { Unreachable = true },

        new("go round the hard rock",
            Walk: true, Jump: false, Mine: false, Build: false,
            "a wall is only a wall if there is no way past it",
        [
            "@...........",
            "#HHHHHHHH...",
            "#HHHHHHHH...",
            "#HHHHHHHH...",
            "#...........",
            "#...........",
            "#..........#",
            "#G.........#",
            "############",
        ]),

        new("a shaft is two columns wide",
            Walk: true, Jump: false, Mine: true, Build: false,
            "the excavation, not merely that one happened. Cutting one column of a "
            + "two column shaft is still mining, and leaves a hole the body cannot "
            + "enter, which is the bug this shape came from",
        [
            "XXXXXXXXXXXX",
            "XX..XXXXXXXX",
            "XX..XXXXXXXX",
            "XX@.XXXXXXXX",
            "XXddXXXXXXXX",
            "X..........X",
            "X..........X",
            "X.........GX",
            "XXXXXXXXXXXX",
        ]),

        new("a sideways tunnel is three rows tall",
            Walk: true, Jump: false, Mine: true, Build: false,
            "the body is three rows, so a tunnel cut two rows high is one the "
            + "character cannot walk down. The planner costed exactly that for weeks",
        [
            "############",
            "############",
            "#..d.......#",
            "#..d.......#",
            "#.@d......G#",
            "############",
        ]),

        new("do not walk into fog",
            Walk: false, Jump: false, Mine: true, Build: false,
            "unknown is not empty; the agent may tunnel through it but not stroll",
        [
            "???????????",
            "???????????",
            "???????????",
            "@?????????G",
            "###########",
        ]),

        new("known ground beats a foggy shortcut",
            Walk: true, Jump: true, Mine: false, Build: false,
            "the long way round is on the map; the short way is a guess. Fog costs "
            + "1.5x precisely so that a seen route wins when there is one",
        [
            "##############",
            "#............#",
            "#............#",
            "#............#",
            "#..########..#",
            "#@.????????.G#",
            "##############",
        ]),

        new("go round the pond",
            Walk: true, Jump: true, Mine: false, Build: false,
            "water halves running speed and the agent has no swimming to speak of, so "
            + "a pool is somewhere it wallows rather than crosses, which is how a run "
            + "got spent in one. Eight wet steps along the bottom against one wet step "
            + "and a dry ledge: the ledge should win by a distance",
        [
            "..............",
            "..............",
            "..............",
            "#..#######...#",
            "#@wwwwwwwwG..#",
            "##############",
        ]),

        new("wade when there is no dry way",
            Walk: true, Jump: false, Mine: false, Build: false,
            "priced, not forbidden. A cost that cannot be paid is a wall, and the way "
            + "through is sometimes simply wet",
        [
            "##############",
            "#............#",
            "#............#",
            "#@wwwwwwwwwwG#",
            "##############",
        ]),

        new("sealed behind unbreakable rock",
            Walk: false, Jump: false, Mine: false, Build: false,
            "no route is the correct answer, and saying so beats inventing one",
        [
            "@...........",
            "#XXXXXXXXXXX",
            "#X.........X",
            "#X.........X",
            "#X....G....X",
            "#XXXXXXXXXXX",
            "############",
        ]) { Unreachable = true },

        new("walk up a mountain",
            Walk: true, Jump: true, Mine: false, Build: false,
            "this should work as the agent walks up the mountain",
        [
            "G...........",
            "#\\..........",
            "##..........",
            "##..........",
            "##..........",
            "##\\.......@.",
            "############",
        ]),

        new("climb out with blocks in hand",
            Walk: true, Jump: true, Mine: true, Build: true,
            "up five and across nine, carrying stone. This used to be called excavate "
            + "stair and expected one, which was the right answer only while pillaring "
            + "could not cut its own ceiling: a staircase is five blocks a row and a "
            + "pillar is two, so the cheap climb is a mix, and it is what a player "
            + "does. See the buried goal below for the empty-handed version",
        [
            "G..#########",
            "############",
            "############",
            "#########...",
            "#########...",
            "#########.@.",
            "############",
        ]) { Seconds = 25 },

        new("a natural staircase up",
            Walk: true, Jump: false, Mine: false, Build: false,
            "one-tile terraces. The game steps up each of these for free, so a hill "
            + "made of them is a walk from bottom to top",
        [
            "................",
            "................",
            "................",
            "..............G.",
            "..............##",
            "............####",
            "..........######",
            "........########",
            "@.....##########",
            "################",
        ]),

        new("a natural staircase down",
            Walk: true, Jump: false, Mine: false, Build: false,
            "the same hill from the top. Falling a tile at a time costs nothing and "
            + "should not be mistaken for needing a jump",
        [
            "................",
            "................",
            "................",
            "@...............",
            "####............",
            "######..........",
            "########........",
            "##########....G.",
            "################",
        ]),

        new("a two-tile ledge needs a jump",
            Walk: true, Jump: true, Mine: false, Build: false,
            "one tile is a free step and two is not, which is the whole difference "
            + "between the terraces above and this",
        [
            "................",
            "................",
            "................",
            "................",
            "................",
            ".........G......",
            ".........#######",
            "@........#######",
            "################",
        ]),

        new("jump to platforms",
            Walk: true, Jump: true, Mine: false, Build: false,
            "the agent must jump to reach the platforms above",
        [
            "...............",
            "...............",
            "...............",
            "..............G",
            "===============",
            "...............",
            "...............",
            "...............",
            "@..............",
            "###############",
        ]),

        new("fall into a pit",
            Walk: true, Jump: false, Mine: false, Build: false,
            "four rows is well inside what the planner will fall through, and gravity "
            + "does the work",
        [
            "................",
            "................",
            "................",
            "@......#########",
            "#......#########",
            "#......#########",
            "#......#########",
            "#.....G#########",
            "################",
        ]),

        new("cut through a thin wall",
            Walk: true, Jump: false, Mine: true, Build: false,
            "one column thick, three rows of body: three blocks. Going over the top "
            + "costs more, which is the point of pricing mining by the tile",
        [
            "################",
            "################",
            "#.....d........#",
            "#.....d........#",
            "#@....d.......G#",
            "################",
        ]),

        new("must mine one block",
            Walk: true, Jump: false, Mine: true, Build: false,
            "the planner must mine one block to proceed, as the gap is not sufficient for passage",
        [
            "################",
            "#.......#......#",
            "#..............#",
            "#..............#",
            "#@....#.......G#",
            "################",
        ]),


        new("sink a shaft to a deep goal",
            Walk: true, Jump: false, Mine: true, Build: false,
            "straight down through solid rock, two columns wide the whole way, "
            + "because a one-wide shaft is a hole the body cannot enter",
        [
            "............",
            "............",
            "@...........",
            "############",
            "############",
            "#########..#",
            "#..........#",
            "#.........G#",
            "############",
        ]) { Seconds = 20 },

        new("drop down a long chimney",
            Walk: true, Jump: false, Mine: false, Build: false,
            "seven rows is inside the fall limit, and a two-wide chimney is one the "
            + "body fits down",
        [
            "@...........",
            "#..#########",
            "#..#########",
            "#..#########",
            "#..#########",
            "#..#########",
            "#.G#########",
            "############",
        ]),

        new("stair up to a buried goal",
            Walk: true, Jump: true, Mine: true, Build: false,
            "a goal above, in rock that can be cut, and nothing to build with. Cutting "
            + "upward is the expensive way and the agent has to have it: with stone in "
            + "the bag it pillars instead, correctly, because a placement costs forty "
            + "ticks and a row of staircase costs five blocks at two hundred. Empty "
            + "handed is the only way to ask the question this case is named after",
        [
            "#...########",
            "#G..########",
            "############",
            "############",
            "#########..#",
            "#########..#",
            "#########@.#",
            "############",
        ]) { Seconds = 25, Blocks = 0 },

        // Ground that is not made of whole blocks.
        // 1.4 smooths its terrain, so this is what a real surface is: the
        // agent read every half block and slope as a whole one, which walls
        // the body out of the row above a hillside.
        new("walk along half blocks",
            Walk: true, Jump: false, Mine: false, Build: false,
            "a floor of half blocks is a floor. Reading them as walls makes the row "
            + "above one unusable, so the body has nowhere to be and the flattest "
            + "ground in the world becomes impassable",
        [
            "................",
            "................",
            "................",
            "@..............G",
            "________________",
            "################",
        ]),

        new("up a smoothed hillside",
            Walk: true, Jump: false, Mine: false, Build: false,
            "a hillside as worldgen actually leaves it. One slope per change of height "
            + "and flat ground between. A row of them side by side would be a "
            + "sawtooth, which is not a shape any world contains",
        [
            "................",
            "................",
            "................",
            "..............G.",
            "............./##",
            "@....../########",
            "################",
        ]),

        new("shaped ground sitting on rock",
            Walk: true, Jump: false, Mine: false, Build: false,
            "half blocks laid over solid stone, which is what a floor looks like almost "
            + "everywhere. Both rows hold the character up and only the upper one is "
            + "where it can actually stand. A plan that picks the lower one leaves "
            + "the step nowhere to go, and the character waits out the stall guard "
            + "against every shaped tile it meets",
        [
            "................",
            "................",
            "................",
            "@..___________G.",
            "################",
        ]),

        new("down a smoothed hillside",
            Walk: true, Jump: false, Mine: false, Build: false,
            "the same hill descending, which is drawn with the other slope because that "
            + "is the way it faces. A picture of a hill with its ramps back to front is "
            + "worse than no picture in a suite whose whole subject is reading shapes",
        [
            "..................",
            "..................",
            "..................",
            ".@................",
            "#####\\............",
            "###########\\.....G",
            "##################",
        ]),

        new("a long rolling surface",
            Walk: true, Jump: false, Mine: false, Build: false,
            "up and down twice over twenty-four columns, with a single slope at each "
            + "change of height and half blocks along the tops. Long enough that a "
            + "freeze anywhere on it shows up, and the arena fails a scenario that "
            + "stands still on a move with nothing to dig",
        [
            "........................",
            "........................",
            "........................",
            "........................",
            "@..../____\\..../____\\..G",
            "########################",
            "########################",
        ]),

        new("the surface it got stuck on",
            Walk: true, Jump: false, Mine: false, Build: false,
            "copied off a live run, from the terrain dump taken where the agent stood "
            + "for six seconds: forest floor with tree bases in it, a half block at the "
            + "far end and a one-tile gap beside it. The gap is narrower than the body "
            + "and the half block is a floor, so this is a walk from end to end. The "
            + "run's actual fault was elsewhere (it was searching for wood still "
            + "falling through the air) but ground the agent has really been stuck on "
            + "is worth keeping whatever stopped it there",
        [
            ".................",
            ".................",
            ".................",
            "....@...........G",
            "##############_._",
        ]),

        new("dig down through a half block",
            Walk: true, Jump: false, Mine: true, Build: false,
            "shaped rock is still rock. It reads as a platform because the body fits "
            + "in the empty part, and a descent that would not break it is a descent "
            + "sealed by every smoothed floor in the world",
        [
            ".............",
            ".............",
            "@............",
            "_____________",
            "#...........#",
            "#...........#",
            "#.........G.#",
            "#############",
        ]) { Seconds = 20 },

        new("skirt a deep pool",
            Walk: true, Jump: true, Mine: false, Build: false,
            "four times the cost per tile means a dry way round wins whenever there "
            + "is one, even a longer one",
        [
            "..............",
            "..............",
            "..............",
            "#..#######...#",
            "#@wwwwwwwwG..#",
            "##############",
        ]),

        new("walk round a patch of fog",
            Walk: true, Jump: true, Mine: false, Build: false,
            "unknown ground is not walkable, and it is not a wall either. Known "
            + "ground beside it is simply cheaper",
        [
            "..............",
            "#............#",
            "#............#",
            "#...####.....#",
            "#@..????....G#",
            "##############",
        ]),

        new("tunnel into the unknown",
            Walk: true, Jump: false, Mine: true, Build: false,
            "when the only way on is fog, the agent digs rather than strolls, which "
            + "is the asymmetry that lets it explore at all",
        [
            "HHHHHHHHHHHHHH",
            "HHHHHHHHHHHHHH",
            "H...????????.H",
            "H...????????.H",
            "H@..????????GH",
            "HHHHHHHHHHHHHH",
        ]) { Seconds = 20 },

        new("walk round the ebonstone",
            Walk: true, Jump: true, Mine: false, Build: false,
            "sixty-five against thirty-five. A copper pick makes corruption a wall, "
            + "and a wall with a way round it is only a detour",
        [
            "..............",
            "#............#",
            "#....HH......#",
            "#....HH......#",
            "#@...HH.....G#",
            "##############",
        ]),

        new("find the soft seam",
            Walk: true, Jump: false, Mine: true, Build: false,
            "one pair of ordinary columns in a floor of ebonstone. Everything else "
            + "underfoot is beyond a copper pick, so the whole scenario is whether the "
            + "search walks to the two tiles it can actually break instead of standing "
            + "on the ones it cannot",
        [
            "##############",
            "#............#",
            "#............#",
            "#@...........#",
            "#HHHddHHHHHHH#",
            "#............#",
            "#............#",
            "#...........G#",
            "##############",
        ]) { Seconds = 25 },

        new("a ledge at exactly the jump's height",
            Walk: true, Jump: true, Mine: false, Build: false,
            "six tiles, which is what a character clears with no equipment and no "
            + "buffs: jumpSpeed held for jumpHeight frames, then coasting against "
            + "gravity, floored. Nothing should be spent getting up here",
        [
            "................",
            "................",
            "................",
            "................",
            "................",
            "..........G.....",
            "..........######",
            "..........######",
            "..........######",
            "..........######",
            "..........######",
            "@.........######",
            "################",
        ]),

        new("a ledge one tile past the jump",
            Walk: true, Jump: true, Mine: false, Build: true,
            "seven, which is one more than a jump reaches, and a face of ebonstone so "
            + "there is no staircase to cut. Blocks are the only way up. This and the "
            + "six-tile ledge above pin the boundary from both sides: get the height "
            + "wrong in either direction and exactly one of them fails",
        [
            "................",
            "................",
            "................",
            "................",
            "..........G.....",
            "..........HHHHHH",
            "..........HHHHHH",
            "..........HHHHHH",
            "..........HHHHHH",
            "..........HHHHHH",
            "..........HHHHHH",
            "@.........HHHHHH",
            "################",
        ]) { Seconds = 20 },

        new("a one-tile lip on the way",
            Walk: true, Jump: false, Mine: false, Build: false,
            "a bump in the floor is not an obstacle, and treating it as one is how a "
            + "follower ends up jumping at every pebble",
        [
            "################",
            "#..............#",
            "#..............#",
            "#..............#",
            "#@...#....#...G#",
            "################",
        ]),

        new("get to the surface",
            Walk: true, Jump: true, Mine: true, Build: true,
            "the player must build a path to reach the surface from the starting point",
        [
            "#......G.......#",
            "################",
            "################",
            "################",
            "################",
            "################",
            "################",
            "#..............#",
            "#..............#",
            "#......@.......#",
            "################",
        ]) { Seconds = 25 },

        new("do a small jump",
            Walk: true, Jump: true, Mine: false, Build: false,
            "the player must perform a jump to reach the goal",
        [
            "........#",        
            "........#",
            "........#",
            "@......G#",
            "##....###",
            ".........",
        ]) { Seconds = 25, Border = false },


        new("build horizontally to do a jump",
            Walk: true, Jump: true, Mine: false, Build: true,
            "the player must build horizontally to create a path for a jump",
        [
            ".....................#",        
            ".....................#",
            ".....................#",
            ".@..................G#",
            "###................###",
        ]) { Seconds = 25, Border = false },

        // Drawn off a run that hung here for eleven seconds: it planned a leap of
        // four, landed on the half block one column along, walked back off it and
        // planned the same leap again. Walking over the two is the answer, and the
        // point of the case is that the jump is never offered.
        // A work bench is not solid: the game lets the character walk straight through
        // one, and up onto it, and nothing may break it: Belief.SupportsStation
        // refuses, because the run needs the bench more than it needs the tile. So the
        // only right answer here is to go through it.
        new("walk through a work bench",
            Walk: true, Jump: false, Mine: false, Build: false,
            "the player must pass a crafting station without breaking or climbing it",
        [
            "............",
            "............",
            "............",
            ".@..B......G",
            "############",
        ]) { Seconds = 25, Border = false },

        new("step over what a jump would land on",
            Walk: true, Jump: true, Mine: false, Build: false,
            "the player must walk over the half block rather than leap past it",
        [
            "..........",
            "..........",
            "..........",
            ".@._XG....",
            "##########",
        ]) { Seconds = 25, Border = false },

        new("a six-tile jump onto a ledge one column over",
            Walk: true, Jump: true, Mine: false, Build: false,
            "copied from a fresh world where the run spent ninety seconds here: the body "
            + "stands on a one-wide pillar with its right half over air, and the search "
            + "asks for a jump of the full six tiles that also steps one column left onto "
            + "a ledge against a wall. The follower pressed left and jump, drifted off the "
            + "pillar, was replanned back onto it, and never left the ground",
        [
            "###########..............",
            "###########..............",
            "###########G.............",
            "############.........###.",
            "###########..........####",
            "##########...........####",
            "##########............###",
            "##########............###",
            "##########..@.........###",
            "##########..#.........###",
            "#########...#.......#####",
            "##########..#.......#####",
            "##########..#........####",
            "########....#........####",
            "########....#.........###",
            "########....##........###",
        ])
        { Seconds = 20 },

        new("a six-tile jump onto a ledge, walking in from the right",
            Walk: true, Jump: true, Mine: false, Build: false,
            "the same ledge, reached the way the run reached it: walking left along the "
            + "top and stopping on the takeoff column with leftward momentum, then a jump "
            + "whose heading is also left. The follower pressed left with the jump from "
            + "the first frame, slid a column past the takeoff before it launched, jumped "
            + "straight up into the underside of the ledge and fell, every half second "
            + "for as long as the run lasted",
        [
            "###########..............",
            "###########..............",
            "###########G.............",
            "############.........###.",
            "###########..........####",
            "##########...........####",
            "##########............###",
            "##########............###",
            "##########....@.......###",
            "##########..#####.....###",
            "#########...#.......#####",
            "##########..#.......#####",
            "##########..#........####",
            "########....#........####",
            "########....#.........###",
            "########....##........###",
        ])
        { Seconds = 20 },
        new("a four-tile jump under an overhanging ledge",
            Walk: true, Jump: true, Mine: false, Build: false,
            "copied from a fresh world where the run spent five minutes here. The ledge "
            + "overhangs the column left of the pillar the body stands on, so a jump "
            + "that drifts left from the first frame puts its head under the ledge, one "
            + "row up, and falls. Rising in the takeoff column and stepping over at the "
            + "top is the only way up, and it is what the search's arc assumed",
        [
            "###########........######",
            "###########G.........####",
            "############.........####",
            "##########...........####",
            "##########..........#####",
            "#########...@......######",
            "#########...#..../#######",
            "##########..#._##########",
            "##########\\.#############",
            "#########################",
            "#########################",
            "#########################",
            "#########################",
        ])
        { Seconds = 20 },
        new("step down a slope to the left",
            Walk: true, Jump: false, Mine: false, Build: false,
            "copied from a fresh world where the run skipped wood eleven times in a "
            + "row: a wood drop and a tree lay to the left, past a slope that runs down "
            + "one row, and the search found no route to either from three tiles away",
        [
            ".........................",
            ".........................",
            ".........................",
            ".........................",
            ".........................",
            "............@......_####\\",
            "#_...G.../###############",
            "#########################",
            "#########################",
            "#########################",
            "#########################",
            "#########################",
            "#########################",
        ])
        { Seconds = 20 },
        new("pillar out of a pool from a slope",
            Walk: true, Jump: true, Mine: false, Build: true,
            "copied from a fresh world: standing on a slope at the bottom of a pool, sunk "
            + "half a tile into it, the body overlaps the cell beside its feet, so a bridge "
            + "block there is refused by the game; and a jump in water does not rise the "
            + "twenty-four pixels a pillar under the feet needs, so the pillar is never "
            + "placed. The rim is out of jumping reach from the floor, so the way up is "
            + "to pillar through the water and jump the last of it",
        [
            "............",
            "............",
            ".........G..",
            "HH......HHHH",
            "HH......HHHH",
            "HH......HHHH",
            "HH......HHHH",
            "HH......HHHH",
            "HH......HHHH",
            "HHwwwwwwHHHH",
            "HHwww@wwHHHH",
            "HHHHH\\wHHHHH",
            "HHHHHHHHHHHH",
        ])
        { Seconds = 30 },
        new("six up and five across is not a jump",
            Walk: false, Jump: false, Mine: false, Build: false,
            "copied from a fresh world: from the top of a one-wide column the search "
            + "planned a jump six rows up and five columns across to a ledge, which no "
            + "standing start can make. The body rose, came down on the floor, and the "
            + "same route was drawn from there sixty times. Height and reach trade off, "
            + "and with no blocks and unbreakable rock there is no way up",
        [
            "HHHHHHHHHHHHHHHH",
            "H..............H",
            "H............G.H",
            "H............HHH",
            "H..............H",
            "H..............H",
            "H..............H",
            "H..............H",
            "H.......@......H",
            "H.......H......H",
            "H.......H......H",
            "H.......H......H",
            "H.......H......H",
            "HHHHHHHHHHHHHHHH",
        ])
        { Unreachable = true, Blocks = 0 },
        new("six rows from water is not a jump",
            Walk: false, Jump: false, Mine: false, Build: false,
            "copied from a fresh world, the run that made the first iron pickaxe: standing "
            + "in a pool, the search planned a six-row jump to the ledge beside it, the wet "
            + "body could not make it, the edge was refused, the footing wobbled a column "
            + "and the same jump was planned again for as long as the run lasted. A wet "
            + "body's jump is a hop of two rows at most, and with no blocks and no dry "
            + "ground clear of the ledge there is no way up",
        [
            "HHHHHHHHHHHHHHHH",
            "H..............H",
            "H..............H",
            "H..............H",
            "H..........G...H",
            "H..........HHHHH",
            "H..............H",
            "H..............H",
            "H..............H",
            "H..............H",
            "H.wwwwwwww@w...H",
            "HHHHHHHHHHHHHHHH",
        ])
        { Unreachable = true, Blocks = 0 },
    };
}
#endif
