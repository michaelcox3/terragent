#if TESTING
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace Terragent;

/// <summary>One question about the torch behaviour, and the room to ask it in.</summary>
/// <param name="Torches">How many the character carries.</param>
/// <param name="Flooded">Whether the floor of the room is under water.</param>
/// <param name="Expect">Whether a torch should end up on the wall.</param>
internal sealed record Lamp(string Name, int Torches, bool Flooded, bool Expect,
    string Because);

internal static class Lamps
{
    public static readonly IReadOnlyList<Lamp> All =
    [
        new("light a dark room", 5, false, true,
            "the room is unlit and there are torches to spare, so one goes up"),

        new("keep the last torch", 1, false, false,
            "the last one is a lamp: held for the light, never spent"),

        new("do not drown a torch", 5, true, false,
            "Terraria refuses a wet placement and says nothing, so the agent has to "
            + "know without being told"),
    ];
}

/// <summary>
/// Whether the agent lights the place it is standing in.
/// </summary>
// The headless harness cannot ask this: it has no light and no walls, and the whole
// question is what the game does with them. So it is asked in a real world, in a sealed
// room deep enough that no sunlight reaches it.
internal sealed class LightTests
{
    private const int Width = 13;
    private const int Height = 7;

    /// <summary>Seconds a case gets before it has failed.</summary>
    private const double Limit = 8.0;

    /// <summary>Rows below the surface. Far enough down that the sky cannot reach.</summary>
    private const int Deep = 40;

    private readonly Pen _pen = new();
    private readonly List<(int Slot, int ItemID, int Stack)> _stashed = [];

    private int _index = -1;
    private int _passed;
    private int _failed;
    private double _started;
    private Point _floor;
    private bool _held;

    public bool Running { get; private set; }

    /// <summary>Built on the first tick, when a belief and a character exist.</summary>
    // Its own rather than the agent's, so a run starts with a clean slate. While this
    // was static the site it had committed to outlived not just the scenario but the
    // whole arena.
    private Lamplighting? _lamplighting;

    public string Status { get; private set; } = "";

    public string Only { get; set; } = "";

    public int Number => _index + 1;

    public int Total => Lamps.All.Count;

    public int Passed => _passed;

    public int Failed => _failed;

    public string Current => _index >= 0 && _index < Lamps.All.Count
        ? Lamps.All[_index].Name
        : "";

    public void Start()
    {
        Running = true;
        _index = -1;
        _passed = 0;
        _failed = 0;
        Next();
    }

    public void Stop(string why)
    {
        Running = false;
        _lamplighting = null;
        Restore();
        _pen.Clear();
        Log.Sample("lights", why, ("passed", _passed), ("failed", _failed));
    }

    public void Update(Belief belief, Character character, AgentState state,
        double now)
    {
        Player player = character.Player;
        if (!Running || _index >= Lamps.All.Count)
        {
            return;
        }

        Lamp lamp = Lamps.All[_index];

        // The behaviour under test, called exactly as the tick calls it.
        _lamplighting ??= new Lamplighting(belief, character);
        bool acted = _lamplighting.Light(state);
        _held |= player.inventory[player.selectedItem].type == ItemID.Torch;

        bool lit = Lit();
        if (lit || now - _started > Limit)
        {
            Score(lamp, lit, acted);
            Next();
        }
    }

    /// <summary>Whether a torch has actually appeared in the room.</summary>
    // Asked of the world rather than of the agent. Counting the times it pressed use
    // would pass a run that placed nothing, which is the failure this is here to catch.
    private bool Lit()
    {
        for (int x = _floor.X - Width; x <= _floor.X + Width; x++)
        {
            for (int y = _floor.Y - Height; y <= _floor.Y + 1; y++)
            {
                if (Main.tile[x, y].HasTile
                    && Main.tile[x, y].TileType == TileID.Torches)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void Score(Lamp lamp, bool lit, bool acted)
    {
        bool won = lit == lamp.Expect;

        // Refusing to place is only right for the right reason. With one torch left the
        // agent should still be holding it up, and over water it should be doing
        // neither, so "nothing happened" has to be told apart from "nothing was
        // possible".
        if (won && !lamp.Expect && lamp.Torches <= 1 && !_held)
        {
            won = false;
        }

        if (won)
        {
            _passed++;
        }
        else
        {
            _failed++;
        }

        Log.Sample("lamp", lamp.Name,
            ("ok", won),
            ("lit", lit),
            ("expected", lamp.Expect),
            ("held", _held),
            ("acting", acted),
            ("torches", lamp.Torches),
            ("flooded", lamp.Flooded),
            ("because", lamp.Because));
    }

    private void Next()
    {
        Restore();
        _pen.Clear();
        _index++;

        while (_index < Lamps.All.Count && Only.Length > 0
               && !Lamps.All[_index].Name.Contains(Only,
                   System.StringComparison.OrdinalIgnoreCase))
        {
            _index++;
        }

        if (_index >= Lamps.All.Count)
        {
            Stop("finished");
            return;
        }

        Lamp lamp = Lamps.All[_index];
        Player player = Main.LocalPlayer;

        int left = (int)(player.Center.X / 16f) - Width / 2;
        int top = (int)Main.worldSurface + Deep;
        Build(left, top, lamp.Flooded);

        _floor = new Point(left + Width / 2, top + Height - 2);
        Pen.Place(player, _floor);

        Arm(player, lamp.Torches);
        _held = false;
        _started = Main.GameUpdateCount / 60.0;
        Status = lamp.Name;
    }

    /// <summary>
    /// A sealed room with walls behind it, deep enough to be dark.
    /// </summary>
    // Walls matter to the question rather than to the scenery: a torch hangs on one, so
    // a cave with no solid tile beside the character is still somewhere a torch can go,
    // and that is most of what underground looks like.
    private void Build(int left, int top, bool flooded)
    {
        for (int row = 0; row < Height; row++)
        {
            for (int column = 0; column < Width; column++)
            {
                int x = left + column;
                int y = top + row;
                bool box = row == 0 || row == Height - 1
                    || column == 0 || column == Width - 1;
                _pen.Put(x, y, box ? TileID.Stone : -1);

                // A struct over a pointer: the local writes through to the world.
                Tile behind = Main.tile[x, y];
                behind.WallType = WallID.Stone;
            }
        }

        if (!flooded)
        {
            return;
        }

        // Two rows of water over the floor, which is where a torch would otherwise go.
        for (int column = 1; column < Width - 1; column++)
        {
            for (int row = Height - 3; row < Height - 1; row++)
            {
                Tile wet = Main.tile[left + column, top + row];
                wet.LiquidType = LiquidID.Water;
                wet.LiquidAmount = 255;
            }
        }
    }

    private void Arm(Player player, int torches)
    {
        for (int slot = 0; slot < player.inventory.Length; slot++)
        {
            if (player.inventory[slot].type != ItemID.Torch)
            {
                continue;
            }

            _stashed.Add((slot, ItemID.Torch, player.inventory[slot].stack));
            player.inventory[slot].TurnToAir();
        }

        for (int slot = 0; slot < 50; slot++)
        {
            if (!player.inventory[slot].IsAir)
            {
                continue;
            }

            player.inventory[slot].SetDefaults(ItemID.Torch);
            player.inventory[slot].stack = torches;
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
}
#endif
