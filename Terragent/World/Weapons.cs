using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace Terragent.World;

/// <summary>How far a weapon reaches, and how it wants to be used.</summary>
internal static class Weapons
{
    /// <summary>
    /// How close a thrown or fired weapon wants to be, in pixels.
    /// </summary>
    // Not the projectile's range, which is far further: how close a shot at something
    // moving is worth taking. Standing off is the point of carrying one.
    private const float ThrownRange = 12f * 16f;

    /// <summary>Whether the weapon is used from a distance rather than swung.</summary>
    // Asked of the damage class, not item.shoot: 1.4.4 shortswords stab with a
    // projectile, so the starting sword would read as something to fire from twenty
    // tiles away. Melee is melee however it is implemented.
    public static bool Throws(Item item) =>
        item.CountsAsClass(DamageClass.Ranged) || item.CountsAsClass(DamageClass.Magic);

    /// <summary>How far this weapon can hurt something, from the body's middle.</summary>
    // Pixels, and the name says so: Held.WeaponReachTiles answers a different question
    // in a different unit.
    public static float StrikePixels(Player player, Item item)
    {
        if (item.IsAir || item.damage <= 0)
        {
            return 0f;
        }

        if (Throws(item))
        {
            return ThrownRange;
        }

        // Width, not the sprite's diagonal: the diagonal is how far the corner reaches
        // on the way past, not how far the weapon reaches at the target, and stopping
        // at its edge misses most swings.
        return (player.width / 2f) + (item.width * item.scale);
    }

    /// <summary>
    /// Roughly where a swing lands: in front, at the body's own height.
    /// </summary>
    // Range says nothing about height, and a slime spends half a fight above the arc.
    // On a weapon without autoReuse a wasted animation is the whole of the fire rate.
    public static Rectangle Arc(Player player, Item item, Vector2 target)
    {
        int reach = (int)StrikePixels(player, item);
        int x = target.X > player.Center.X
            ? (int)player.Center.X
            : (int)player.Center.X - reach;
        return new Rectangle(x, (int)player.position.Y - 8, reach, player.height + 16);
    }

    /// <summary>Whether a swing started now would plausibly land on this.</summary>
    // Where the target will be, not where it is: a swing connects partway through its
    // animation, and a slime crosses most of a tile in that time.
    public static bool Strike(Player player, Item item, NPC target)
    {
        float lead = System.Math.Min(item.useAnimation, 20) / 2f;
        Rectangle hitbox = target.Hitbox;
        hitbox.Offset((int)(target.velocity.X * lead), (int)(target.velocity.Y * lead));
        return Arc(player, item, hitbox.Center.ToVector2()).Intersects(hitbox);
    }

    /// <summary>
    /// How far the target may drift before the agent goes after it again.
    /// </summary>
    // Without it a slime's hop crosses the line and the agent spends the fight
    // alternating between closing and swinging one tile apart.
    public const float Hysteresis = 1.4f;
}
