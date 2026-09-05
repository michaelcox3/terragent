using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;

namespace Terragent.Act;

/// <summary>
/// What is in the hand and where it points: the held item, what it reaches, the cursor,
/// and using it.
/// </summary>
// One of the three units of Act: the selected slot and the cursor. Mining, placing,
// swinging and throwing are all one thing to Terraria, use the held item at the cursor,
// so they are one verb here. Binding the player and the held item once is what keeps
// Reach and Weapons pure geometry underneath.
internal sealed class Hand(Player player, Inventory inventory)
{
    // Given, not fetched. Reading Main.LocalPlayer here would pin the mod to one agent.
    private readonly Player _player = player;

    private readonly Inventory _inventory = inventory;

    /// <summary>The item actually in the hand.</summary>
    private Item Held => _player.inventory[_player.selectedItem];

    /// <summary>
    /// Whether the tool a tile job would use reaches the tile from this footing, by the
    /// game's own box rather than a radius.
    /// </summary>
    // The box is six tiles up and two down, so a radius understates it: three says yes
    // to a stone two rows below a pit's rim that the game refuses, and the route then
    // ends on the rim with the swing blocked.
    public bool ToolReachesFrom(int tileID, Point footing, int tileX, int tileY)
    {
        Item? tool = Tiles.NeedsAxe(tileID) ? BestAxe : BestPickaxe;
        return tool is not null && Reach.CanUseFrom(_player, tool, footing, tileX, tileY);
    }

    /// <summary>Whether any of these could be swung at from that footing.</summary>
    public bool Usable(Point footing, IReadOnlyList<Point> targets) =>
        Reach.Usable(_player, Held, footing, targets);

    /// <summary>Whether the held item could be used on this tile from here.</summary>
    public bool InReach(int tileX, int tileY) =>
        Reach.CanUse(_player, Held, tileX, tileY);

    /// <summary>Whether the held item could be <i>placed</i> here. A wider box.</summary>
    public bool InPlaceReach(int tileX, int tileY) =>
        Reach.CanPlace(_player, Held, tileX, tileY);

    /// <summary>The mining reach box, for drawing.</summary>
    public Rectangle ReachBox =>
        Reach.Box(_player, Held);

    /// <summary>How close a route must get for the held tool to reach, in tiles.</summary>
    public int ReachTiles =>
        Reach.Radius(_player, Held);

    /// <summary>How far outside reach a tile is. Zero when inside.</summary>
    public int Overshoot(int tileX, int tileY) =>
        Reach.Overshoot(_player, Held, tileX, tileY);

    /// <summary>Whether the game itself says the aimed tile is in range of what is held.</summary>
    // Asked plainly. ReachAgrees below answers a different question, whether our
    // predicate matches the game's, and returns true when both say no.
    public bool GameSaysInRange =>
        !Held.IsAir && _player.IsTargetTileInItemRange(Held);

    /// <summary>
    /// Whether our reach predicate agrees with the game's, for the tile the game is
    /// currently aimed at.
    /// </summary>
    public bool ReachAgrees =>
        Held.IsAir
        || _player.IsTargetTileInItemRange(Held)
           == Reach.CanUse(_player, Held, Player.tileTargetX, Player.tileTargetY);

    /// <summary>How far the held weapon can hurt something, in pixels.</summary>
    // Pixels off the sprite, which is what decides whether a swing lands. Not
    // WeaponReachTiles, which is the tile-targeting box and decides how near a route
    // counts as arrived.
    public float StrikePixels =>
        Weapons.StrikePixels(_player, Held);

    /// <summary>Whether a swing started now would plausibly land on this.</summary>
    public bool Strikes(NPC target) =>
        Weapons.Strike(_player, Held, target);

    /// <summary>Whether what is held is used from a distance rather than swung.</summary>
    public bool Throws =>
        Weapons.Throws(Held);

    /// <summary>What an item is worth as a weapon, or zero when it is not one.</summary>
    // Ammo is excluded: a Wooden Arrow does five damage and a Wooden Bow four, so
    // picking by damage alone holds the arrows and fires nothing.
    internal int Damage(Item item) =>
        item.damage > 0 && item.pick == 0 && item.axe == 0 && item.hammer == 0
        && item.ammo == AmmoID.None
        && item.useStyle != ItemUseStyleID.None
        && (item.useAmmo == AmmoID.None || Carries(item.useAmmo))
            ? item.damage
            : 0;

    /// <summary>Whether any ammo of this kind is carried.</summary>
    private bool Carries(int ammo)
    {
        foreach (Item item in _player.inventory)
        {
            if (item.ammo == ammo && item.stack > 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The best pickaxe carried, or nothing when none is.</summary>
    public Item? BestPickaxe => BestBy(item => item.pick);

    /// <summary>The best axe carried, or nothing when none is.</summary>
    public Item? BestAxe => BestBy(item => item.axe);

    /// <summary>Ticks per swing of the best pickaxe carried.</summary>
    // The pickaxe's, not the held item's: the search prices a mine whatever is in the
    // hand when it runs, and a torch in hand priced every swing at a torch.
    public int PickaxeUseTime => BestPickaxe is { useTime: > 0 } pick ? pick.useTime : 15;

    /// <summary>The best weapon carried, by the same rule HoldWeapon uses.</summary>
    public Item? BestWeapon => BestBy(Damage);

    /// <summary>
    /// How far the best pickaxe can reach, in tiles.
    /// </summary>
    // Asked of the tool the job would use, not the one in hand, or walking to a tree
    // with a torch out measures the torch. Every pickaxe past copper carries tileBoost,
    // so this is not constant per tool either.
    public int PickaxeReachTiles => ReachTilesOf(BestPickaxe);

    /// <summary>How far the best axe can reach, in tiles.</summary>
    public int AxeReachTiles => ReachTilesOf(BestAxe);

    /// <summary>
    /// How far a route must close on a fight, in tiles.
    /// </summary>
    // Not how far the weapon can hurt, which is Weapons.StrikePixels in pixels off the
    // sprite. This is the tile-targeting box, and it reads tileBoost, which no weapon
    // carries: a bow and a shortsword come out the same here while their strike ranges
    // are twelve tiles and two. It decides only how near a route counts as arrived.
    public int WeaponReachTiles => ReachTilesOf(BestWeapon);

    private int ReachTilesOf(Item? tool) =>
        tool is null ? 1 : Reach.Radius(_player, tool);

    /// <summary>Whichever carried item scores highest, or nothing if none scores.</summary>
    private Item? BestBy(System.Func<Item, int> power) => _inventory.Best(power);

    /// <summary>The tile the agent asked to aim at, for the panel to check against.</summary>
    public Point16? Intended { get; private set; }

    /// <summary>Where the game says the cursor actually landed.</summary>
    public static Point16 Actual => new((short)Player.tileTargetX, (short)Player.tileTargetY);

    /// <summary>
    /// Point at a tile, by moving the mouse there.
    /// </summary>
    public void Aim(int tileX, int tileY)
    {
        // Through the game's own view transform, because the screen is not the
        // world at one to one.
        Intended = new Point16((short)tileX, (short)tileY);
        PutCursor(new Vector2((tileX * 16f) + 8f, (tileY * 16f) + 8f));
    }

    /// <summary>Point at a place in the world rather than at a tile.</summary>
    // A creature is not on the tile grid, and rounding it onto one costs up to eight
    // pixels of aim: the difference between a swing landing and missing at the edge of
    // reach, and which way a projectile goes.
    public void AimAt(Vector2 world)
    {
        Intended = new Point16((short)(world.X / 16f), (short)(world.Y / 16f));
        PutCursor(world);
    }

    /// <summary>Put the cursor on a world position.</summary>
    // Static because it writes Main.mouseX and Main.mouseY, the game's one cursor.
    private static void PutCursor(Vector2 world)
    {
        // The cursor is in the same space as screenPosition, with no zoom or UI scaling
        // anywhere (measured at 2560x1440, zoom 1.3333, UI scale 1.4667). Putting the
        // offset through GameViewMatrix.ZoomMatrix is the trap: it is the identity at
        // zoom one and scales about the screen centre at any other, fifteen pixels out
        // at 1.33, which is a whole tile when the target sits near a boundary.
        Vector2 screen = world - Main.screenPosition;
        Main.mouseX = (int)System.Math.Round(screen.X);
        Main.mouseY = (int)System.Math.Round(screen.Y);
    }

    /// <summary>Hold an item of this type, fetching it out of the bag if need be.</summary>
    public bool HoldItem(int itemID) => HoldBy(item => item.type == itemID ? 1 : 0);

    /// <summary>Hold whatever hits hardest. False if carrying nothing that does.</summary>
    public bool HoldWeapon() => HoldBy(Damage);

    /// <summary>Hold the best pickaxe on the hotbar. False if carrying none.</summary>
    public bool HoldPickaxe() => HoldBy(item => item.pick);

    /// <summary>Hold the best axe on the hotbar. False if carrying none.</summary>
    public bool HoldAxe() => HoldBy(item => item.axe);

    /// <summary>
    /// Hold an ordinary building block, for pillaring.
    /// </summary>
    public bool HoldBlock() => HoldBy(item => _inventory.Spendable(item) ? item.stack : 0);

    /// <summary>Select whichever carried item scores highest, fetching it onto the hotbar first.</summary>
    private bool HoldBy(System.Func<Item, int> power)
    {
        int best = _inventory.BestSlot(power);
        if (best < 0)
        {
            return false;
        }

        _player.selectedItem = _inventory.BringToHotbar(best);
        return true;
    }

    /// <summary>
    /// Press use. One tick's worth; the caller holds it across ticks.
    /// </summary>
    public void Use()
    {
        // Do not fight delayUseItem. Player.Update sets it whenever the cursor
        // is over interface (line 24408), and clears it only on a frame where
        // controlUseItem is false (23967).
        if (_player.delayUseItem)
        {
            Blocked = true;
            return;
        }

        // Only swing where the cursor actually is. Aim converts with the camera as it
        // stands during this hook and the game recomputes the target later in the frame,
        // so on the tick a fall begins the two disagree and the swing breaks a block the
        // route never drew. A tile of slack covers rounding; more than that means the
        // camera moved, and waiting a frame is cheaper than breaking the wrong tile.
        if (Intended is { } wanted
            && (System.Math.Abs(Actual.X - wanted.X) > 1
                || System.Math.Abs(Actual.Y - wanted.Y) > 1))
        {
            Blocked = true;
            return;
        }

        Blocked = false;

        // A weapon without autoReuse fires once for as long as the button is held:
        // Terraria wants releaseUseItem true in between, as it wants releaseJump between
        // jumps. Letting go while the animation runs costs no rate, because the next use
        // could not have started until it ended.
        if (!Held.autoReuse && _player.itemAnimation > 0)
        {
            return;
        }

        _player.controlUseItem = true;
    }

    /// <summary>True when the last <see cref="Use"/> was withheld by the game's
    /// interface delay rather than issued.</summary>
    public bool Blocked { get; private set; }

    /// <summary>The item currently held, for diagnostics.</summary>
    public string HeldName =>
        Held.IsAir
            ? "nothing"
            : Held.Name;
}
