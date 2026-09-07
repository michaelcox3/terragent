using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;

namespace Terragent.World;

/// <summary>What is alive nearby, as far as the player can see.</summary>
// The third of the three files allowed to read the game's world state, and the only one
// that names Main.npc. Everything above it gets a Creature, which is an index, a place, a
// life total and whether it bites.
//
// Gated on the map like the tiles and the drops are. Main.npc holds everything alive in
// the world, and an agent that read it whole would set off after a slime it has never seen
// on the other side of a mountain.
internal sealed class Creatures(ITerrain terrain) : ICreatures
{
    private readonly ITerrain _terrain = terrain;

    /// <summary>How far out to look, in tiles.</summary>
    private const int Far = 120;

    public Creature? Nearest(Point from)
    {
        Vector2 me = Hitbox.Middle(from);
        Creature? best = null;
        float nearest = float.MaxValue;

        for (int n = 0; n < Main.maxNPCs; n++)
        {
            if (Read(n) is not { Hostile: true } creature || Beyond(from, creature.Middle))
            {
                continue;
            }

            float span = Vector2.DistanceSquared(me, creature.Middle);
            if (span < nearest)
            {
                nearest = span;
                best = creature;
            }
        }

        return best;
    }

    public Creature? Nearest(Point from, IReadOnlyList<int> types)
    {
        Vector2 me = Hitbox.Middle(from);
        Creature? best = null;
        float nearest = float.MaxValue;

        for (int n = 0; n < Main.maxNPCs; n++)
        {
            if (Read(n) is not { } creature || !Names(types, creature.Type)
                || Beyond(from, creature.Middle))
            {
                continue;
            }

            float span = Vector2.DistanceSquared(me, creature.Middle);
            if (span < nearest)
            {
                nearest = span;
                best = creature;
            }
        }

        return best;
    }

    // Null the moment it dies, which is what a fight asks every tick. Terraria reuses the
    // slot straight away, so this can answer about something else entirely; the index is
    // the only identity the game offers and a fight that held a position would be worse.
    public Creature? At(int index) =>
        index >= 0 && index < Main.maxNPCs ? Read(index) : null;

    /// <summary>One slot, or null when it holds nothing worth knowing about.</summary>
    private Creature? Read(int index)
    {
        NPC npc = Main.npc[index];
        if (!npc.active || npc.life <= 0)
        {
            return null;
        }

        Point at = new((int)(npc.Center.X / 16f), (int)(npc.Center.Y / 16f));
        if (!_terrain.IsKnown(at.X, at.Y))
        {
            return null;
        }

        // Hostile by what it does rather than by what it is. A town NPC is friendly, and a
        // bunny does no damage, so neither is something to swing at.
        return new Creature(index, npc.type, npc.Center, npc.life,
            !npc.friendly && npc.damage > 0);
    }

    private static bool Names(IReadOnlyList<int> types, int type)
    {
        for (int n = 0; n < types.Count; n++)
        {
            if (types[n] == type)
            {
                return true;
            }
        }

        return false;
    }

    private static bool Beyond(Point from, Vector2 middle) =>
        System.Math.Abs((middle.X / 16f) - from.X) > Far
        || System.Math.Abs((middle.Y / 16f) - from.Y) > Far;
}
