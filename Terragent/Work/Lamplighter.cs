using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria.ID;
using Terragent.Controls;
using Terragent.Pathfinding;
using Terragent.Report;
using Terragent.World;

namespace Terragent.Work;

/// <summary>Carrying a torch to see by, and putting one up when there is one to spare.</summary>
// Darkness is not cosmetic. Everything the agent knows is a tile reading gated on the
// player's own map, and a cell only reaches that map once it has appeared lit, so in the
// dark the agent cannot find out what it is standing next to. One old run swung seventy
// nine times at a cell that stayed unknown throughout and then wrote the tile off as too
// hard for its pickaxe.
internal sealed class Lamplighter(
    ITerrain terrain,
    IInventory bag,
    IHand hand,
    ISites sites,
    IBody body,
    IPilot pilot,
    IJournal journal) : ILamplighter
{
    private readonly ITerrain _terrain = terrain;
    private readonly IInventory _bag = bag;
    private readonly IHand _hand = hand;
    private readonly ISites _sites = sites;
    private readonly IBody _body = body;
    private readonly IPilot _pilot = pilot;

    /// <summary>How far apart to keep them, in tiles.</summary>
    // A rule about where the tiles are, so it holds from the moment the swing lands rather
    // than several frames later when the light has spread, and it needs nothing remembered
    // between ticks.
    private const int Apart = 5;

    /// <summary>How far out to look for somewhere to put one, in tiles.</summary>
    // The same as the spacing, so that a torch just put down is one the spacing can see.
    // Wider, and a torch laid at the edge of the sweep sits outside the ring that decides
    // whether another is wanted, and the two rules answer each other for ever.
    private const int Range = Apart;

    /// <summary>Dark, with nothing in hand to see by: below this the map stops filling in.</summary>
    /// <summary>How many cells out from the body the ground has to be on the map.</summary>
    // Two. One is what the body needs to stand somewhere; two is what it needs to work
    // there, since breaking the tile under the feet means knowing what is under that tile.
    private const int Rings = 2;

    private const float Dim = 0.12f;

    /// <summary>Dark, with a lamp already in hand.</summary>
    // Two bars, not one, because the lamp lights the cell being asked about. A run watched
    // doing this read 0.11 with a pickaxe in hand and 0.51 a tick later with the torch out,
    // so one bar makes the same spot dark and bright on alternate ticks: torch, pickaxe,
    // torch, pickaxe, sixty times a second, and the swing cancelled every time. Which bar
    // to use is read off the hand, so none of it has to be remembered.
    private const float Lit = 0.8f;

    // Carrying something that would help, as well as needing it. Blind with an empty bag
    // is not a tick worth taking: holding up nothing reveals nothing, and a body that kept
    // taking the tick for it would stand in the dark for ever instead of digging out of it.
    //
    // Said either way, because an empty bag and a light that works look identical in a log
    // that only records success. A run stood swinging at a cell it could not see for twenty
    // seconds and the whole of what it said about light was nothing.
    public bool Blind
    {
        get
        {
            if (!Hidden)
            {
                return false;
            }

            int lamp = Lamp();
            journal.Change("raising", lamp <= 0
                ? $"nothing to hold up at ({Here.X}, {Here.Y}): torches "
                    + $"{_bag.Carrying(Lights.Dark)}, wet lights {_bag.Carrying(Lights.Wet)}, "
                    + $"submerged {_body.Submerged}"
                : $"{Names.Item(lamp)} at ({Here.X}, {Here.Y}): "
                    + $"light here {_terrain.Brightness(Here.X, Here.Y):0.00}, "
                    + $"was holding {Names.Item(_hand.Held)}");

            return lamp > 0;
        }
    }

    public void Raise() => _bag.Hold(Lamp());

    /// <summary>What to hold up where the body is, or zero when nothing carried will do.</summary>
    // Water is the whole of the difference. A torch goes out the moment the body goes
    // under, which is exactly when seeing matters most: the map stops filling in, every
    // cell reads unknown, and the run swings at holes it has already dug. A glowstick
    // lights either.
    //
    // The same two lists the search prices routes off, so what the plan assumed is carried
    // and what the hand reaches for cannot disagree. Dry, a torch comes first, because a
    // glowstick is the scarcer thing and the run keeps one for the water.
    //
    // Carrying, not spendable, which is the whole of what the reserve buys. The last one is
    // never put down: held, it is still a lamp, and a lit cell is one the map takes. It
    // costs nothing to carry, since whatever works next takes the hand back.
    private int Lamp()
    {
        foreach (int itemID in _body.Submerged ? Lights.Wet : Lights.Dark)
        {
            if (_bag.Carrying(itemID) > 0)
            {
                return itemID;
            }
        }

        return ItemID.None;
    }

    /// <summary>Whether anything the body is up against has yet to reach the map.</summary>
    // What raising a torch is for, asked as itself. A cell reaches the map by having
    // appeared lit and stays on it for good, so this goes quiet the moment the light has
    // done its work and cannot come back on for the same ground twice.
    //
    // Darkness was the old test and it is not the same question. Underground it is dark
    // almost everywhere, including every corridor the run has already lit and mapped, so a
    // torch was in hand nearly all the time and every job spent its first tick taking the
    // hand back.
    //
    // One ring, not a radius. Rock two cells deep is unlit whatever is carried, since
    // nothing shines through rock, so a wider question is one that can never be answered
    // yes and asks for a torch for ever.
    // Two rings and not one. Breaking the tile under the feet means knowing what is under
    // that tile, so the ground has to be read a cell further out than the body stands. Asked
    // one ring, a body at the face of a tunnel it had just cut saw everything touching it,
    // never counted as blind, never held the torch up, and could not legally break anything:
    // a whole run went by without one line saying it had raised a light.
    private bool Hidden
    {
        get
        {
            Rectangle body = Hitbox.Fills(_body.Footing);
            for (int x = body.Left - Rings; x <= body.Right + Rings - 1; x++)
            {
                for (int y = body.Top - Rings; y <= body.Bottom + Rings - 1; y++)
                {
                    if (!_terrain.IsKnown(x, y))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    /// <summary>Whether putting one up is what this tick is for.</summary>
    // Dry ground only. A torch will not light under water and Terraria refuses to place one
    // there without a word, and a glowstick is thrown rather than stood up, so there is
    // nothing to put anywhere. Holding one is the whole of what can be done down there, and
    // it is enough, since a lit cell is a revealed cell.
    public bool Needed =>
        Dark && !_body.Submerged && Somewhere() is not null;

    public void Tick()
    {
        _bag.Hold(ItemID.Torch);

        if (Somewhere() is not { } site)
        {
            return;
        }

        // Through Note rather than Change, and with what the hand did. A refused placement
        // and one that has not landed yet read the same, and the same site chosen three
        // times over is a torch that never went down: a deduplicated line cannot show that
        // at all, since the text is identical every time.
        journal.Note("lighting", $"putting one at ({site.X}, {site.Y}): "
            + $"light here {_terrain.Brightness(Here.X, Here.Y):0.00}, "
            + $"spendable {_bag.Spendable(ItemID.Torch)}, "
            + $"holding {Names.Item(_hand.Held)}, withheld {_hand.Blocked}, "
            + $"still swinging {_hand.Busy}, "
            + $"cell {_terrain.KindAt(site.X, site.Y)} "
            + $"type {_terrain.TypeAt(site.X, site.Y)}, "
            + $"game aims at ({_hand.Targeted.X}, {_hand.Targeted.Y})");
        _hand.Aim(site.X, site.Y);
        _hand.Use();
    }

    /// <summary>The cell the body fills, which is the one that has to be lit.</summary>
    // Not the floor underneath it, which is a solid tile and answers for the inside of a
    // rock.
    private Point Here => new(_body.Footing.X, _body.Footing.Y - 1);

    /// <summary>Whether it is too dark here to see by.</summary>
    // Which bar to read against is decided by what is in the hand, and a glowstick lights
    // the cell being asked about exactly as a torch does.
    private bool Dark =>
        _terrain.Brightness(Here.X, Here.Y)
            < (System.Array.IndexOf(Lights.Dark, _hand.Held) >= 0 ? Lit : Dim);

    /// <summary>The nearest cell that will take a torch, or null when none will.</summary>
    // Nearest, and never by how dark it looks. Ranking by darkness moves the answer the
    // moment the torch is in hand, and Terraria refuses a swing aimed anywhere but where
    // the cursor was on the previous frame, so a target that moves is a torch that never
    // lands. Nothing read here changes while it is being read.
    private Point? Somewhere()
    {
        if (_bag.Spendable(ItemID.Torch) <= 0
            || _sites.Nearest(_body.Footing, [TileID.Torches], Apart) is not null)
        {
            return null;
        }

        Point? best = null;
        int nearest = int.MaxValue;
        Point here = Here;

        for (int across = -Range; across <= Range; across++)
        {
            for (int down = -Range; down <= Range; down++)
            {
                Point site = new(here.X + across, here.Y + down);

                // Water puts a torch out, and Terraria refuses the placement rather than
                // wasting it, silently, which reads from here as a swing that has not
                // landed yet: the same puddle, for ever.
                // A torch where a block is going stops the block going: the cell reads as
                // taken, Terraria refuses the placement without a word, and the follower
                // presses it for ever. A run lit its own pillar and then stood at the foot
                // of it until it died. Where the route means to dig, a torch is only
                // destroyed, which is a wasted torch rather than a wedged run.
                if (!_terrain.Buildable(site.X, site.Y) || _terrain.Wet(site.X, site.Y)
                    || _pilot.CellsToFill.Contains(site)
                    || _pilot.CellsToBreak.Contains(site))
                {
                    continue;
                }

                int away = (across * across) + (down * down);
                if (away >= nearest || !Anchored(site)
                    || !_hand.InPlaceReach(site.X, site.Y))
                {
                    continue;
                }

                nearest = away;
                best = site;
            }
        }

        return best;
    }

    /// <summary>Whether a torch put here would have anything to hold on to.</summary>
    // A torch stands on a floor or clings to a wall beside it, and does not hang from a
    // ceiling. Counting the cell above as an anchor is what had a run aiming at the same
    // spot in a shaft over and over: Terraria refuses a placement with no anchor and says
    // nothing about it, which cannot be told from one that has not landed yet.
    private bool Anchored(Point site) =>
        _terrain.Holds(site.X, site.Y + 1, trustFog: false)
        || _terrain.Holds(site.X - 1, site.Y, trustFog: false)
        || _terrain.Holds(site.X + 1, site.Y, trustFog: false);
}
