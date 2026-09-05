#if TESTING
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;

namespace Terragent;

/// <summary>One fight: a weapon, something to use it on, and how long it may take.</summary>
/// <param name="Weapon">The only weapon the character will be carrying.</param>
/// <param name="Ammo">What it fires, or zero for something swung.</param>
/// <param name="Enemy">What to spawn.</param>
/// <param name="Wants">An item that enemy drops, which is what sends the agent at it.</param>
internal sealed record Bout(string Name, int Weapon, int Ammo, int Enemy, int Wants,
    int Seconds = 30);

/// <summary>
/// Can it actually kill the thing.
/// </summary>
// The pathing tests answer whether the agent can get somewhere. Nothing answered
// whether it can fight, and fighting was broken in a way no amount of walking would
// show: it closed to a tool's reach and swung a weapon with a different one.
internal static class Bouts
{
    public static readonly IReadOnlyList<Bout> All =
    [
        new("shortsword kills a slime", ItemID.CopperShortsword, 0,
            NPCID.BlueSlime, ItemID.Gel),
        new("wooden bow kills a slime", ItemID.WoodenBow, ItemID.WoodenArrow,
            NPCID.BlueSlime, ItemID.Gel),
    ];
}

/// <summary>Runs the fights and says what happened.</summary>
internal sealed class CombatTests
{
    private const int Width = 40;
    private const int Height = 10;
    private const int Margin = 2;

    private readonly Pen _pen = new();
    private readonly Pilot _pilot = new();

    private Executor? _executor;
    private readonly List<(int Slot, int ItemID, int Stack)> _stashed = [];

    private int _index = -1;
    private int _passed;
    private int _failed;
    private int _ticks;
    private int _npc = -1;
    private bool _wasMortal;
    private string _last = "";

    // What the report needs when a fight fails, gathered as it goes: the answer is
    // almost never "it lost", it is "it never got close" or "it never swung".
    private float _closest = float.MaxValue;
    private bool _held;
    private bool _swung;
    private int _hurt;

    // Swings against connections. "It misses a lot" is not something the report could
    // say, and a fix for it is not something anyone could check.
    private int _swings;
    private int _hits;
    private int _wasAnimating;
    private int _wasLife;

    /// <summary>Build the graph the pen fights with, once, out of what it is given.</summary>
    // Not in the constructor. CombatTests is made when AgentMod's statics initialise,
    // during Mod.Load, and there is no player to take a belief off yet, so asking for
    // one there is a crash at load rather than a fight that does not run.
    private Executor Ready(Belief belief, Character character)
    {
        if (_executor is not null)
        {
            return _executor;
        }

        Exploring exploring = new(_pilot, belief, character);
        Building building = new(_pilot, belief, character,
            new Stations(), new Workshop());
        _executor = new Executor(_pilot, exploring,
            new Fighting(_pilot, exploring, belief, character), building, belief, character,
            new Requirements(belief, character, new Stations()));

        // The executor and the building each need the other; nothing crafts in a fight
        // pen, but leaving it unwired would be a null waiting for the day one does.
        building.Runs(_executor);
        return _executor;
    }

    public bool Running { get; private set; }

    public string Status { get; private set; } = "";

    public string Only { get; set; } = "";

    public int Number => _index + 1;

    public string Current => _index >= 0 && _index < Bouts.All.Count
        ? Bouts.All[_index].Name
        : "";

    public void Start()
    {
        _wasMortal = !AgentMod.Invulnerable;
        AgentMod.Invulnerable = true;
        Running = true;
        _index = -1;
        _passed = 0;
        _failed = 0;
        Log.Sample("combat", "starting", ("bouts", Bouts.All.Count));
        Next();
    }

    public void Stop(string why)
    {
        Despawn();
        Sweep();
        _pen.Clear();
        Restore();
        if (_wasMortal)
        {
            AgentMod.Invulnerable = false;
        }

        Running = false;
        Status = $"{_passed}/{_passed + _failed} fights won";
        Log.Sample("combat", why, ("passed", _passed), ("failed", _failed));
    }

    public void Update(Belief belief, Character character, AgentState state,
        double now)
    {
        Player player = character.Player;
        if (!Running)
        {
            return;
        }

        Bout bout = Bouts.All[_index];
        NPC? enemy = _npc >= 0 && Main.npc[_npc].active && Main.npc[_npc].type == bout.Enemy
            ? Main.npc[_npc]
            : null;

        if (enemy is null || enemy.life <= 0)
        {
            Score(true, $"killed it in {_ticks / 60.0:0.0}s");
            return;
        }

        _closest = System.Math.Min(_closest,
            Vector2.Distance(enemy.Center, player.Center));
        _hurt = enemy.lifeMax - enemy.life;

        // A swing is the animation starting; a hit is the target losing life. Counted
        // here rather than inside the agent, because the agent must not be told whether
        // it connected: that would be the harness feeding it the answer.
        if (player.itemAnimation > 0 && _wasAnimating == 0)
        {
            _swings++;
        }

        if (enemy.life < _wasLife)
        {
            _hits++;
        }

        _wasAnimating = player.itemAnimation;
        _wasLife = enemy.life;

        if (++_ticks > bout.Seconds * 60)
        {
            Score(false, Why(bout, enemy));
            return;
        }

        // The real executor, asked for something the enemy drops. Anything less tests a
        // copy of the thing that is failing.
        Ready(belief, character).Obtain(new ItemQuantity(bout.Wants, 999), state, now);

        Item held = player.inventory[player.selectedItem];
        _held |= held.type == bout.Weapon;
        _swung |= player.controlUseItem;

        if (state.Behaviour != _last)
        {
            _last = state.Behaviour;
            Log.Sample("bout", state.Behaviour,
                ("bout", bout.Name),
                ("t", $"{_ticks / 60.0:0.00}"),
                ("held", held.Name),
                ("gap", $"{Vector2.Distance(enemy.Center, player.Center) / 16f:0.0} tiles"),
                ("reach", $"{Weapons.StrikePixels(player, held) / 16f:0.0} tiles"),
                ("thrown", Weapons.Throws(held)),
                ("shoot", held.shoot),
                ("noMelee", held.noMelee),
                ("life", $"{enemy.life}/{enemy.lifeMax}"),

                // Aim is a world position put through the camera, so any error in that
                // conversion scales with the window. Logged as three numbers rather
                // than one so the offset can be read rather than guessed at.
                ("screen", $"{Main.screenWidth}x{Main.screenHeight}"),
                ("zoom", $"{Main.GameViewMatrix.Zoom.X:0.00}"),
                ("enemyAt", $"({(int)(enemy.Center.X / 16f)}, {(int)(enemy.Center.Y / 16f)})"),
                ("aimWanted", character.Hand.Intended?.ToString() ?? "none"),
                ("aimActual", Hand.Actual.ToString()));
        }

        Status = $"{bout.Name}: {state.Behaviour}";
    }

    /// <summary>Why a fight was lost, in the terms that tell you where to look.</summary>
    private string Why(Bout bout, NPC enemy)
    {
        if (!_held)
        {
            return $"never held the {Lang.GetItemNameValue(bout.Weapon)}";
        }

        if (!_swung)
        {
            return $"held the weapon and never used it, closest {_closest / 16f:0.0} tiles";
        }

        if (_hurt == 0)
        {
            return $"swung and never connected, closest {_closest / 16f:0.0} tiles "
                   + $"against {Weapons.StrikePixels(Main.LocalPlayer, Main.LocalPlayer.inventory[Main.LocalPlayer.selectedItem]) / 16f:0.0} of reach";
        }

        return $"took it to {enemy.life}/{enemy.lifeMax} in {bout.Seconds}s, "
               + $"closest {_closest / 16f:0.0} tiles";
    }

    private void Score(bool won, string why)
    {
        if (won)
        {
            _passed++;
        }
        else
        {
            _failed++;
        }

        Log.Sample("bout", Bouts.All[_index].Name,
            ("passed", won), ("why", why), ("ticks", _ticks),
            ("swings", _swings), ("hits", _hits),
            ("landed", _swings == 0 ? "n/a" : $"{_hits * 100 / _swings}%"));
        Next();
    }

    private void Next()
    {
        Despawn();
        Sweep();
        _pen.Clear();
        Restore();

        while (true)
        {
            _index++;
            if (_index >= Bouts.All.Count)
            {
                Stop("finished");
                return;
            }

            if (Only.Length == 0
                || Bouts.All[_index].Name.Contains(Only,
                    System.StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
        }

        Bout bout = Bouts.All[_index];
        _ticks = 0;
        _closest = float.MaxValue;
        _held = false;
        _swung = false;
        _hurt = 0;
        _swings = 0;
        _hits = 0;
        _wasAnimating = 0;
        _wasLife = int.MaxValue;
        _last = "";

        int left = (Main.maxTilesX / 2) - (Width / 2);
        int top = Pen.Top(Height, Margin);
        Build(left, top);

        Player player = Main.LocalPlayer;
        Pen.Place(player, new Point(left + 3, top + Height - 1));
        Arm(player, bout);

        // Far enough that closing the distance is part of the test, and on the floor so
        // it is not falling when the fight starts.
        _npc = NPC.NewNPC(new EntitySource_DebugCommand("agent combat test"),
            (left + Width - 5) * 16, (top + Height - 1) * 16, bout.Enemy);
        if (_npc >= 0 && _npc < Main.npc.Length)
        {
            Main.npc[_npc].SetDefaults(bout.Enemy);
            Main.npc[_npc].position = new Vector2((left + Width - 5) * 16f,
                (top + Height - 1) * 16f - Main.npc[_npc].height);
        }

        Status = bout.Name;
    }

    /// <summary>
    /// A sealed box with a wall across the middle of it.
    /// </summary>
    // The box keeps the fight in. The wall in the middle is the test: three tiles tall,
    // which is one hop, so a melee weapon has to path over it rather than stand on the
    // near side. It also breaks the line a bow needs, so the search has to find a clear
    // shot instead of firing into stone at the right distance.
    private void Build(int left, int top)
    {
        for (int row = 0; row < Height; row++)
        {
            for (int column = 0; column < Width; column++)
            {
                bool box = row == 0 || row == Height - 1
                    || column == 0 || column == Width - 1;
                bool bar = column == Width / 2 && row >= Height - 4 && row < Height - 1;
                _pen.Put(left + column, top + row,
                    box || bar ? TileID.Ebonstone : -1);
            }
        }
    }

    /// <summary>Carry the weapon under test, and nothing else that could be swung.</summary>
    // Otherwise the test is about whichever weapon does the most damage, which is not
    // the one whose name is on it.
    private void Arm(Player player, Bout bout)
    {
        for (int slot = 0; slot < player.inventory.Length; slot++)
        {
            Item item = player.inventory[slot];
            if (item.IsAir || (item.damage <= 0 && item.ammo == AmmoID.None)
                || item.pick > 0 || item.axe > 0 || item.hammer > 0)
            {
                continue;
            }

            _stashed.Add((slot, item.type, item.stack));
            item.TurnToAir();
        }

        Give(player, bout.Weapon, 1);
        if (bout.Ammo != 0)
        {
            Give(player, bout.Ammo, 200);
        }
    }

    private void Give(Player player, int itemID, int stack)
    {
        for (int slot = 0; slot < 50; slot++)
        {
            if (!player.inventory[slot].IsAir)
            {
                continue;
            }

            player.inventory[slot].SetDefaults(itemID);
            player.inventory[slot].stack = stack;
            _stashed.Add((slot, ItemID.None, 0));
            return;
        }
    }

    private void Restore()
    {
        Player player = Main.LocalPlayer;
        foreach ((int slot, int type, int stack) in _stashed)
        {
            if (type == ItemID.None)
            {
                player.inventory[slot].TurnToAir();
            }
            else
            {
                player.inventory[slot].SetDefaults(type);
                player.inventory[slot].stack = stack;
            }
        }

        _stashed.Clear();
    }

    /// <summary>Sweep up anything the last fight dropped.</summary>
    // Bout two opened with the agent walking away from a fresh slime to pick up the
    // gel from bout one, which is correct behaviour and a useless test.
    private static void Sweep()
    {
        foreach (Item item in Main.item)
        {
            if (item.active)
            {
                item.active = false;
            }
        }
    }

    private void Despawn()
    {
        if (_npc >= 0 && _npc < Main.npc.Length && Main.npc[_npc].active)
        {
            Main.npc[_npc].active = false;
        }

        _npc = -1;
    }
}
#endif
