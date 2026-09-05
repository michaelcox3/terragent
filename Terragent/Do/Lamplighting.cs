using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace Terragent.Do;

/// <summary>Putting a torch down when it is too dark to see.</summary>
// Darkness is not cosmetic. The belief is gated on the map being revealed, and a cell
// only reveals once it has appeared lit, so in the dark the agent cannot confirm what
// it is standing next to. Light is how the agent sees at all.
internal sealed class Lamplighting(Belief belief, Character character)
{
    private readonly Belief _belief = belief;

    private readonly Character _character = character;

    // Below this the map stops filling in; judged by the belief, not by the screen.
    private const float Dark = 0.12f;

    // Far enough apart that one torch is not answered with another while its light is
    // still spreading over the next few frames.
    private const int Apart = 5;

    /// <summary>Frames to hold the last torch up before carrying on.</summary>
    // Light takes a few frames to spread, so raising it for one frame reveals nothing.
    private const int Peek = 20;

    /// <summary>Frames to keep trying a chosen site before choosing again.</summary>
    // An anchor can be mined away or a cell flood meanwhile, so a decision that cannot
    // be carried out has to expire.
    private const int Patience = 45;

    /// <summary>How far out to look for somewhere worth lighting, in tiles.</summary>
    // InPlaceReach is the real limit; this only bounds the search.
    private const int Range = 6;

    private Point _last = new(int.MinValue, int.MinValue);

    private Point _lastPeek = new(int.MinValue, int.MinValue);

    private int _peeking;

    /// <summary>The site chosen and not yet filled, and how long it has been held.</summary>
    // A torch in the hand lights the room, so the chosen cell stops reading dark the
    // moment the torch is picked up. The choice stands until it is carried out, or the
    // agent flourishes a torch and never puts it down.
    private Point? _placing;

    private int _committed;

    /// <summary>Put one down if this site needs it. True when that is this tick's job.</summary>
    public bool Light(AgentState state)
    {
        Player player = _character.Player;
        Point at = _character.Movement.Footing;
        Point here = new(at.X, at.Y - 1);
        bool dark = _belief.Brightness(here.X, here.Y) < Dark;

        // The cell the follower could not see counts as dark wherever the body stands:
        // lit in a doorway with the dig into the black, the body's own cell reads
        // bright. A new blind cell also lifts the peek throttle, which is keyed on
        // where the body was rather than on what it needs to see.
        if (state.Blind is { } blind && !_belief.IsKnown(blind.X, blind.Y))
        {
            dark = true;
            _lastPeek = new Point(int.MinValue, int.MinValue);
        }

        state.Blind = null;

        // Asked of the character rather than the tile: Belief.Wet is gated on the map
        // being revealed, and submerged in the dark it reads dry.
        if (player.wet || player.lavaWet || player.honeyWet)
        {
            // A torch will not light under water, and a glowstick is thrown rather
            // than placed, so holding one is all that can be done. It is enough,
            // because a lit cell is a revealed cell.
            return _character.Inventory.Carrying(ItemID.Glowstick) > 0
                   && Raise(here, dark, ItemID.Glowstick);
        }

        if (_character.Inventory.Carrying(ItemID.Torch) <= 0)
        {
            return false;
        }

        // The last torch is never put down: held, it is still a lamp, so with one left
        // the agent can still find out what it is standing in.
        if (_character.Inventory.Carrying(ItemID.Torch)
            <= Upkeeps.Reserved(ItemID.Torch))
        {
            return Raise(here, dark, ItemID.Torch);
        }

        // Carry out a decision already made, without asking about the light again.
        if (_placing is { } already && ++_committed < Patience
            && _belief.Buildable(already.X, already.Y)
            && !_belief.Wet(already.X, already.Y))
        {
            return Put(state, already);
        }

        // The darkest thing in reach, rather than the tile underfoot: in a lit doorway
        // with a black room to one side, the floor below is already lit.
        _placing = null;
        _committed = 0;
        if (Darkest(here) is not { } site)
        {
            // Nowhere to put one is not a reason to stay blind.
            return Raise(here, dark, ItemID.Torch);
        }

        _placing = site;

        return Put(state, site);
    }

    /// <summary>
    /// Hold one up for a moment to see by. True when that is this tick's job.
    /// </summary>
    // A lit cell is a revealed cell, so a held torch reveals without spending one.
    // Throttled on distance because each peek costs a moment of not mining.
    private bool Raise(Point here, bool dark, int lampID)
    {
        if (_peeking > 0)
        {
            _peeking--;
            _character.Hand.HoldItem(lampID);
            return true;
        }

        if (!dark
            || (System.Math.Abs(here.X - _lastPeek.X) < Apart
                && System.Math.Abs(here.Y - _lastPeek.Y) < Apart))
        {
            return false;
        }

        _lastPeek = here;
        _peeking = Peek;
        _character.Hand.HoldItem(lampID);
        return true;
    }

    /// <summary>Put one in a chosen cell. True when that is this tick's job.</summary>
    private bool Put(AgentState state, Point site)
    {
        if (!_character.Hand.HoldItem(ItemID.Torch))
        {
            _placing = null;
            return false;
        }

        Player player = _character.Player;

        // Wait the swing out rather than giving up on the light. Taking the tick is
        // what ends the animation: nothing presses use while this owns the frame.
        if (player.itemAnimation > 0)
        {
            return true;
        }

        _character.Hand.Aim(site.X, site.Y);
        _character.Hand.Use();
        state.Placing.Add(site);
        _last = site;
        return true;
    }

    /// <summary>
    /// The darkest reachable cell that will take a torch, or null when none will.
    /// </summary>
    private Point? Darkest(Point here)
    {
        Point? best = null;
        float darkest = Dark;

        for (int dx = -Range; dx <= Range; dx++)
        {
            for (int dy = -Range; dy <= Range; dy++)
            {
                Point site = new(here.X + dx, here.Y + dy);
                if (System.Math.Abs(site.X - _last.X) < Apart
                    && System.Math.Abs(site.Y - _last.Y) < Apart)
                {
                    continue;
                }

                // Terraria silently refuses a torch in water, which the follower reads
                // as a placement that has not landed yet.
                if (!_belief.Buildable(site.X, site.Y) || _belief.Wet(site.X, site.Y))
                {
                    continue;
                }

                float light = _belief.Brightness(site.X, site.Y);
                if (light >= darkest || !Anchored(site)
                    || !_character.Hand.InPlaceReach(site.X, site.Y))
                {
                    continue;
                }

                darkest = light;
                best = site;
            }
        }

        return best;
    }

    /// <summary>Whether a torch put here would have anything to hold on to.</summary>
    // Terraria silently refuses a placement with no anchor. A background wall is an
    // anchor in its own right, which is most of what a cave interior offers.
    private bool Anchored(Point site) =>
        _belief.Walled(site.X, site.Y)
        || _belief.Holds(site.X, site.Y + 1, trustFog: false)
        || _belief.Holds(site.X, site.Y - 1, trustFog: false)
        || _belief.Holds(site.X - 1, site.Y, trustFog: false)
        || _belief.Holds(site.X + 1, site.Y, trustFog: false);
}
