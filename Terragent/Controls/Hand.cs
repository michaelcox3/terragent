using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terragent.World;

namespace Terragent.Controls;

/// <summary>The cursor and the use button.</summary>
// Mining, placing, chopping and swinging are one thing to Terraria, use the held item at
// the cursor, so they are one verb here. The reach arithmetic is the game's own, copied
// rather than approximated: a radius says yes to a stone two rows below the rim of a pit
// that the game refuses, and the route then ends on the rim with the swing going nowhere.
internal sealed class Hand(Player player) : IHand
{
    // Given, not fetched. Reading Main.LocalPlayer here would pin the mod to one agent.
    private readonly Player _player = player;

    /// <summary>The tile the agent asked to point at, for Use to check against.</summary>
    private Point? _aimed;

    /// <summary>The item actually in the hand.</summary>
    private Item Holding => _player.inventory[_player.selectedItem];

    public int Held => Holding.IsAir ? ItemID.None : Holding.type;

    public bool Blocked { get; private set; }

    public bool InReach(int x, int y) => Within(_player.position, 0f, x, y);

    // blockRange is what the game adds for placing, and it is why placing further than
    // mining is not an approximation.
    public bool InPlaceReach(int x, int y) => Within(_player.position, _player.blockRange, x, y);

    public bool CanUseFrom(Point footing, int x, int y) => Standing(footing, 0f, x, y);

    public bool CanPlaceFrom(Point footing, int x, int y) =>
        Standing(footing, _player.blockRange, x, y);

    /// <summary>How far the held weapon can hurt something, from the body's middle.</summary>
    // Width, not the sprite's diagonal: the diagonal is how far the corner reaches on the
    // way past, not how far the weapon reaches at the target, and stopping at its edge
    // misses most swings. A thing that is fired wants to be closer than it can shoot,
    // since standing off is the point of carrying one.
    private float StrikePixels(int itemID)
    {
        if (itemID == ItemID.None)
        {
            return 0f;
        }

        Item weapon = ContentSamples.ItemsByType[itemID];
        if (weapon.damage <= 0)
        {
            return 0f;
        }

        return weapon.CountsAsClass(DamageClass.Ranged)
            || weapon.CountsAsClass(DamageClass.Magic)
            ? 12f * 16f
            : (_player.width / 2f) + (weapon.width * weapon.scale);
    }

    public bool CanHitFrom(Point footing, Vector2 target, int itemID,
        float stretch = 1f)
    {
        float reach = StrikePixels(itemID) * stretch;
        return Vector2.DistanceSquared(Hitbox.Middle(footing), target) <= reach * reach
            && Collision.CanHitLine(Hitbox.Stand(footing), _player.width, _player.height,
                target, 1, 1);
    }

    public void AimAt(Vector2 where)
    {
        _aimed = new Point((int)(where.X / 16f), (int)(where.Y / 16f));
        PutCursor(where);
    }

    public void Aim(int x, int y)
    {
        _aimed = new Point(x, y);
        PutCursor(new Vector2((x * 16f) + 8f, (y * 16f) + 8f));
    }

    public void Use()
    {
        // Do not fight delayUseItem. Player.Update sets it whenever the cursor is over
        // interface, and clears it only on a frame where controlUseItem is false.
        if (_player.delayUseItem)
        {
            Blocked = true;
            return;
        }

        // Only swing where the cursor actually is. Aim converts with the camera as it
        // stands during this hook and the game recomputes its target later in the frame,
        // so on the tick a fall begins the two disagree and the swing breaks a tile the
        // route never drew. A tile of slack covers rounding; more than that means the
        // camera moved, and waiting a frame is cheaper than breaking the wrong tile.
        if (_aimed is { } wanted
            && (Math.Abs(Player.tileTargetX - wanted.X) > 1
                || Math.Abs(Player.tileTargetY - wanted.Y) > 1))
        {
            Blocked = true;
            return;
        }

        Blocked = false;

        // A weapon without autoReuse fires once for as long as the button is held:
        // Terraria wants releaseUseItem true in between, as it wants releaseJump between
        // jumps. Letting go while the animation runs costs no rate, because the next use
        // could not have started until it ended.
        if (!Holding.autoReuse && _player.itemAnimation > 0)
        {
            return;
        }

        _player.controlUseItem = true;
    }

    /// <summary>Put the cursor on a place in the world.</summary>
    // The cursor is in the same space as screenPosition, with no zoom or UI scaling
    // anywhere. Putting the offset through GameViewMatrix.ZoomMatrix is the trap: it is
    // the identity at zoom one and scales about the screen centre at any other, fifteen
    // pixels out at 1.33, which is a whole tile when the target sits near a boundary.
    private static void PutCursor(Vector2 world)
    {
        Vector2 screen = world - Main.screenPosition;
        Main.mouseX = (int)Math.Round(screen.X);
        Main.mouseY = (int)Math.Round(screen.Y);
    }

    /// <summary>Extra tiles of reach the held item carries.</summary>
    private float Boost => Holding.IsAir ? 0f : Holding.tileBoost;

    /// <summary>The same box, asked of a footing the body has not walked to yet.</summary>
    // Both extremes of where the body can sit in its column pair, not the middle: the box
    // is a float comparison, so a tile can be in range from one edge and not the other,
    // and answering for the middle promises a swing the body cannot always make.
    //
    // A tile of margin on top of that, because the box answers for a character standing
    // squarely and still, and the real one is a pixel or two off or sliding.
    private bool Standing(Point footing, float extra, int x, int y)
    {
        float top = (footing.Y * 16f) - _player.height;
        float left = footing.X * 16f;
        float right = left + (Hitbox.Width * 16f) - _player.width;

        return Within(new Vector2(left, top), extra - 1f, x, y)
            && Within(new Vector2(right, top), extra - 1f, x, y);
    }

    /// <summary>The game's own reach test, with however much extra the caller allows.</summary>
    // A box, not a radius, and measured from the body edges rather than its middle. The
    // one and two subtracted are the game's, and dropping them puts the bottom row of the
    // box a tile past where a swing lands.
    private bool Within(Vector2 position, float extra, int x, int y)
    {
        float reachX = Player.tileRangeX + Boost + extra;
        float reachY = Player.tileRangeY + Boost + extra;

        return (position.X / 16f) - reachX <= x
            && ((position.X + _player.width) / 16f) + reachX - 1f >= x
            && (position.Y / 16f) - reachY <= y
            && ((position.Y + _player.height) / 16f) + reachY - 2f >= y;
    }
}
