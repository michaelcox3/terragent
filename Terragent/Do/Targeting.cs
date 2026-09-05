using Microsoft.Xna.Framework;
using Terraria;

namespace Terragent.Do;

/// <summary>
/// Which creature the agent is fighting, and what it takes to change its mind.
/// </summary>
// One place decides this, for the reflex and for a progression goal alike, so the two
// cannot drift into different answers to one question.
internal sealed class Targeting
{
    /// <summary>
    /// The aggro box: how near something has to come to become a target, in tiles.
    /// </summary>
    // Close enough that it is already a fight rather than a detour. A radius rather
    // than a square, so the nearest intruder and the nearest enemy are one creature.
    private const int Radius = 5;

    /// <summary>How far a fight can be dragged before it counts as gone.</summary>
    // Until it is dead or off screen. NPC.sWidth is what Terraria despawns against and
    // is fixed whatever the monitor is; half of it, because this is a distance from
    // the middle.
    private static int Leash => NPC.sWidth / 32;

    /// <summary>How many nearby creatures count as the spawn cap being full.</summary>
    // Written down because Terraria will not say: the cap is a local in NPC.SpawnNPC,
    // reachable only as a ref parameter on EditSpawnRate. Five is vanilla's default
    // for one player at the surface. Under it, killing passers-by brings the wanted
    // creature no closer.
    private const int Cap = 5;

    private int _target = -1;

    /// <summary>The item the target was taken for, or zero when it walked into the box.</summary>
    private int _because;

    /// <summary>What is being fought, or null when nothing is.</summary>
    public int Target => _target;

    /// <summary>
    /// Pick something to fight, or keep what is already being fought.
    /// </summary>
    /// <param name="wanted">
    /// An item a progression goal is after, or zero when the agent is doing something
    /// else. A creature that drops it outranks one merely inside the aggro radius: the
    /// radius is there so a fight cannot be walked past, not so a stray zombie can pull
    /// the agent off the slime it came for.
    /// </param>
    public NPC? Choose(Belief belief, Vector2 from, int wantedItemID)
    {
        NPC? target = null;
        NPC? closestDropper = null;
        NPC? closestEnemy = null;
        float bestDropper = float.MaxValue;
        float bestEnemy = float.MaxValue;
        int crowd = 0;

        foreach (NPC npc in belief.VisibleEnemies())
        {
            if (npc.whoAmI == _target)
            {
                target = npc;
            }

            // A creature is an entity, visible however dark its ground, and a route to
            // one across unrevealed rock prices mining through every unknown cell:
            // the whole search budget, once a tick. Fog is the test, not distance; a
            // slime at the screen's edge on walked ground is a fine target.
            Point where = new((int)(npc.Center.X / 16f), (int)(npc.Center.Y / 16f));
            if (!belief.IsKnown(where.X, where.Y))
            {
                continue;
            }

            float distance = Vector2.DistanceSquared(npc.Center, from);

            if (wantedItemID != 0 && Drops.Yields(npc.netID, wantedItemID) && distance < bestDropper)
            {
                bestDropper = distance;
                closestDropper = npc;
            }

            crowd++;
            if (distance < bestEnemy)
            {
                bestEnemy = distance;
                closestEnemy = npc;
            }
        }

        // No switching while the fight still stands: gone from VisibleEnemies means
        // dead or left, and anything else (hopped away, something nearer) is
        // abandoning a fight halfway and taking the hits twice. A fight taken for a
        // drop ends when the run no longer wants it, or the target outlives the job;
        // one left beyond the leash is gone whether or not it is still perceivable.
        if (target is not null && (_because == 0 || _because == wantedItemID)
            && Vector2.DistanceSquared(target.Center, from)
               <= Leash * 16f * (Leash * 16f))
        {
            return target;
        }

        bool intruding = closestEnemy is not null
                         && bestEnemy <= Radius * 16f * (Radius * 16f);

        NPC? picked;
        int because;
        if (closestDropper is not null)
        {
            // What the run needs outranks what walked up to it.
            picked = closestDropper;
            because = wantedItemID;
        }
        else if (intruding)
        {
            // Nothing to do with the job, so it survives the job ending: this is
            // hunted until it is dead or left behind.
            picked = closestEnemy;
            because = 0;
        }
        else if (wantedItemID != 0 && crowd >= Cap)
        {
            // Clearing counts as hunting only while the cap is full: Terraria will not
            // spawn more with the nearby limit reached, so a useless creature is then
            // the reason the useful one never arrives. Below the cap it is fighting
            // for nothing.
            picked = closestEnemy;
            because = wantedItemID;
        }
        else
        {
            picked = null;
            because = 0;
        }

        _target = picked?.whoAmI ?? -1;
        _because = because;
        return picked;
    }
}
