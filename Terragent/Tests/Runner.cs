#if TESTING
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent.Creative;
using Terraria.ID;
using Terraria.IO;
using Terraria.ModLoader;

namespace Terragent;

/// <summary>Makes a character and a world, drives for a while, and quits.</summary>
// The only way to test the agent is to play it, and nobody can sit and watch every run.
// This is the unattended version: a flag file asks for a run, the game comes up, this
// walks it through character creation and world generation, turns driving on, and exits
// when the time is up. Everything it learns is in the log.
//
// Compiled only in Debug, behind TESTING, so a published build cannot start a run.
public sealed class Runner : ModSystem
{
    /// <summary>Seconds of no frames at all before the process is killed.</summary>
    private const double Frozen = 30.0;

    /// <summary>Seconds to wait for a world to generate or load before giving up.</summary>
    private const double LoadLimit = 240.0;

    /// <summary>How long to drive when the flag does not say.</summary>
    private const double DriveFor = 120.0;

    private static Phase _phase = Phase.Idle;

    private static double _seconds;

    private static long _frames;

    private static long _alive = DateTime.UtcNow.Ticks;

    private static string _name = "";

    private static bool _playing;

    /// <summary>Whether this run walks the scenarios rather than playing the progression.</summary>
    private static bool _walking;

    /// <summary>What a scenario's name must contain to be walked, or empty for all of them.</summary>
    private static string _only = "";

    /// <summary>The arena in progress, for the hook that presses keys.</summary>
    // Ticked from SetControls and not from this file's own hook, which runs as the frame is
    // drawn: a key held then is held after the game has already read what was pressed.
    internal static Tests.Arena? Arena { get; set; }

    private static Task? _generating;

    private static DateTime _until;

    private static Mod? _mod;

    /// <summary>Dropped next to the logs to ask for a run. Deleted when read.</summary>
    public static string FlagPath => Path.Combine(Logging.LogDir, "terragent-run.flag");

    private enum Phase
    {
        Idle,
        Entering,
        Driving,
        Leaving,
    }

    public override void PostSetupContent()
    {
        if (!File.Exists(FlagPath))
        {
            return;
        }

        _mod = Mod;
        string flag = File.ReadAllText(FlagPath).Trim();
        File.Delete(FlagPath);

        // Walking the scenarios needs a world and a character, and any will do, so it takes
        // whichever are already on disk rather than making fresh ones. Making one has never
        // worked unattended on this machine: every log in the folder stops at the new world
        // being saved and the game goes no further.
        _walking = flag.StartsWith("arena", StringComparison.OrdinalIgnoreCase);
        _only = _walking ? flag[5..].Trim() : string.Empty;
        _seconds = double.TryParse(flag, out double asked) ? asked : DriveFor;
        _phase = Phase.Entering;
        Say("asked", $"driving for {_seconds} seconds");
    }

    public override void Load()
    {
        Main.OnPostDraw += Tick;

        // On its own thread, because a watchdog on the main thread cannot fire when the
        // main thread is the thing that has stopped.
        Thread guard = new(() =>
        {
            while (true)
            {
                Thread.Sleep(5000);
                if (_phase is Phase.Idle or Phase.Leaving
                    || new TimeSpan(DateTime.UtcNow.Ticks - _alive).TotalSeconds < Frozen)
                {
                    continue;
                }

                Say("frozen", "the game stopped drawing");
                Environment.Exit(5);
            }
        })
        { IsBackground = true, Name = "terragent runner watchdog" };

        guard.Start();
    }

    public override void Unload() => Main.OnPostDraw -= Tick;

    /// <summary>Every frame, menu or world, which is the whole reason it is this hook.</summary>
    private static void Tick(GameTime time)
    {
        if (_phase == Phase.Idle)
        {
            return;
        }

        _alive = DateTime.UtcNow.Ticks;
        double now = ++_frames / 60.0;

        switch (_phase)
        {
            case Phase.Entering when Main.gameMenu:
                Enter(now);
                return;

            case Phase.Entering when _walking:
                if (Driver?.Agent is not { } walker)
                {
                    return;
                }

                walker.Drive(false);
                Arena = new Tests.Arena(walker.Foreman.Pilot, walker.Terrain,
                    new Report.Journal(_mod!));
                Arena.Start(_only);
                _phase = Phase.Driving;
                Say("walking", $"as {Main.LocalPlayer.name} in {Main.worldName}");
                return;

            case Phase.Driving when Arena is { Running: true }:
                return;

            case Phase.Driving when Arena is not null:
                Say("done", "the scenarios are walked");
                _phase = Phase.Leaving;
                Main.instance.Exit();
                return;

            case Phase.Entering:
                AgentPlayer? driver = Driver;
                IAgent? agent = driver?.Agent;
                if (agent is null)
                {
                    return;
                }

                agent.Drive(true);
                _until = DateTime.UtcNow.AddSeconds(_seconds);
                _phase = Phase.Driving;
                Say("driving", $"as {Main.LocalPlayer.name} in {Main.worldName}");
                return;

            case Phase.Driving when DateTime.UtcNow < _until:
                if (_frames % 300 == 0)
                {
                    Say("alive", $"at ({(int)(Main.LocalPlayer.position.X / 16f)}, "
                        + $"{(int)(Main.LocalPlayer.position.Y / 16f)}), "
                        + $"{Main.LocalPlayer.statLife} life, "
                        + (Main.LocalPlayer.dead ? "dead" : "up"));
                }

                return;

            case Phase.Driving:
                Say("done", "time is up");
                _phase = Phase.Leaving;
                Main.instance.Exit();
                return;
        }
    }

    private static AgentPlayer? Driver =>
        Main.LocalPlayer is { active: true } player
            ? player.GetModPlayer<AgentPlayer>()
            : null;

    /// <summary>Make a character and a world that have never been played, and go in.</summary>
    private static void Enter(double now)
    {
        if (_playing)
        {
            if (now > LoadLimit)
            {
                Say("stuck", "the world never loaded");
                Environment.Exit(3);
            }

            return;
        }

        if (_walking)
        {
            Open();
            return;
        }

        if (_generating is null)
        {
            Make();
            return;
        }

        if (!_generating.IsCompleted)
        {
            if (now > LoadLimit)
            {
                Say("stuck", "the world never generated");
                Environment.Exit(3);
            }

            return;
        }

        if (_generating.IsFaulted)
        {
            Say("failed", _generating.Exception?.GetBaseException().Message ?? "generation");
            Environment.Exit(3);
            return;
        }

        // Generation saves the world and drops back to the menu rather than entering it,
        // so find the file it wrote and go in the way a saved world is gone into.
        Main.LoadWorlds();
        WorldFileData? made = Main.WorldList.FirstOrDefault(world => world.Name == _name);
        if (made is null)
        {
            Say("failed", $"the generated world {_name} is not on disk");
            Environment.Exit(3);
            return;
        }

        made.SetAsActive();

        // The character again, by name from disk. A freshly created record does not
        // always stay loaded the way one chosen from the menu does, and the map loader
        // takes the game down when it is not.
        if (Main.ActivePlayerFileData?.Player is null)
        {
            Main.LoadPlayers();
            PlayerFileData? them =
                Main.PlayerList.FirstOrDefault(saved => saved.Name == _name);
            if (them is null)
            {
                Say("failed", $"the fresh character {_name} is not on disk");
                Environment.Exit(3);
                return;
            }

            them.SetAsActive();
        }

        Say("entering", $"{_name} into {_name}");
        _playing = true;
        _frames = 0;
        WorldGen.playWorld();
    }

    /// <summary>Go into whatever world and character are already saved.</summary>
    // The newest of each, because the arena builds its own ground in the sky and does not
    // care what is underneath it. What it needs is a loaded world, which making one has
    // never managed to hand over unattended.
    private static void Open()
    {
        Main.LoadWorlds();
        WorldFileData? world = Main.WorldList.FirstOrDefault();
        Main.LoadPlayers();
        PlayerFileData? them = Main.PlayerList.FirstOrDefault();
        if (world is null || them is null)
        {
            Say("failed", "no saved world and character to walk the scenarios in");
            Environment.Exit(3);
            return;
        }

        world.SetAsActive();
        them.SetAsActive();
        _name = them.Name;

        Say("entering", $"{them.Name} into {world.Name}");
        _playing = true;
        _frames = 0;
        WorldGen.playWorld();
    }

    /// <summary>A classic character with the three copper tools, and a small world.</summary>
    private static void Make()
    {
        _name = "Fresh-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");

        Player player = new() { name = _name, difficulty = 0 };
        player.statLife = player.statLifeMax = 100;
        player.statMana = player.statManaMax = 20;
        Item[] starting =
        [
            new Item(ItemID.CopperShortsword),
            new Item(ItemID.CopperPickaxe),
            new Item(ItemID.CopperAxe),
        ];

        PlayerLoader.SetStartInventory(player,
            PlayerLoader.GetStartingItems(player, starting, mediumCoreDeath: false));

        // Spawning reads these back even for a classic character, and one made without
        // them takes the game down on its first spawn.
        player.savedPerPlayerFieldsThatArentInThePlayerClass = new();
        CreativePowerManager.Instance.ResetDataForNewPlayer(player);
        PlayerFileData.CreateAndSave(player).SetAsActive();

        // The smallest classic world with a random seed and a random evil, which is the
        // cheapest the game will generate and what a first run sees.
        Main.maxTilesX = 4200;
        Main.maxTilesY = 1200;
        WorldGen.setWorldSize();
        WorldGen.WorldGenParam_Evil = -1;
        Main.GameMode = 0;
        Main.worldName = _name;
        Main.ActiveWorldFileData = WorldFile.CreateMetadata(_name, false, 0);
        Main.ActiveWorldFileData.SetSeedToRandom();
        Main.menuMode = 10;
        _generating = WorldGen.CreateNewWorld();

        Say("making", $"{_name}, seed {Main.ActiveWorldFileData.Seed}");
    }

    private static void Say(string what, string detail) =>
        _mod?.Logger.Info($"[run.{what}] {detail}");
}
#endif
