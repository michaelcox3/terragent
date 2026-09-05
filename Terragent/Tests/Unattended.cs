#if TESTING
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.Creative;
using Terraria.ID;
using Terraria.IO;
using Terraria.ModLoader;

namespace Terragent;

/// <summary>
/// Launches into a world, runs the arena, writes a report and quits.
/// </summary>
public sealed class Unattended : ModSystem
{
    /// <summary>Dropped next to the journals to ask for a run. Deleted when read.</summary>
    public static string FlagPath =>
        Path.Combine(Logging.LogDir, "agent", "run-tests.flag");

    /// <summary>Seconds to let the world settle before building anything in it.</summary>
    private const double Settle = 4.0;

    /// <summary>Seconds of no frames at all before the process is killed.</summary>
    private const double Frozen = 30.0;

    /// <summary>
    /// Wall-clock seconds a single scenario may hold the suite before it is abandoned.
    /// </summary>
    private const double Stall = 150.0;

    /// <summary>Seconds to let a polite shutdown finish before forcing one.</summary>
    private const double Shutdown = 5.0;

    /// <summary>Seconds to wait for the world to load before giving up on it.</summary>
    private const double LoadLimit = 180.0;

    private enum Phase
    {
        Idle,
        Entering,
        Settling,
        Running,
        Driving,
        Leaving,
    }

    /// <summary>Seconds of free play a "drive" run gets before it is stopped.</summary>
    // Long enough to fell several trees, which is the shortest loop that touches
    // choosing a job, walking to it, using a tool and watching the world change under
    // the index. The suites never chop anything, so nothing else exercises it.
    private const double DriveFor = 90.0;

    /// <summary>When free play stops, on the wall clock.</summary>
    private static DateTime _driveUntil;

    private static Phase _phase = Phase.Idle;
    private static double _since;
    private static bool _playing;

    /// <summary>
    /// Frames drawn since the flag was read, which is the only clock that runs here.
    /// </summary>
    private static long _frames;
    private static string _only = "";
    private static int _seen = -1;
    private static double _moved;

    /// <summary>Whether the run asked for a never-played character and world.</summary>
    private static bool _fresh;

    /// <summary>The name shared by a fresh character and world, so a report can pair them.</summary>
    private static string _freshName = "";

    /// <summary>World generation in flight, or null before it starts.</summary>
    private static Task? _generating;

    public override void PostSetupContent()
    {
        if (!File.Exists(FlagPath))
        {
            return;
        }

        // Whatever is written in the flag picks a subset of scenarios by name. Read
        // before deleting, because the file is the only place it is said.
        try
        {
            _only = File.ReadAllText(FlagPath).Trim();
            if (_only.Equals("run", StringComparison.OrdinalIgnoreCase))
            {
                _only = "";
            }

            // "fresh" anywhere in the flag asks for a character and a world that have
            // never been played, which is the only run that says anything about a
            // start rather than about the saves that happen to be first in the list.
            Match fresh = Regex.Match(_only, @"\bfresh\b", RegexOptions.IgnoreCase);
            if (fresh.Success)
            {
                _fresh = true;
                _only = Regex.Replace(_only, @"\s*\bfresh\b\s*", " ", RegexOptions.IgnoreCase).Trim();
            }
        }
        catch (IOException)
        {
            _only = "";
        }

        try
        {
            File.Delete(FlagPath);
        }
        catch (IOException)
        {
            // If it cannot be deleted it cannot be trusted not to fire again, and a
            // run that repeats itself unasked is worse than one that does not happen.
            return;
        }

        _phase = Phase.Entering;
        Log.Sample("unattended", "asked", ("flag", FlagPath));
    }

    /// <summary>Wall-clock ticks of the last sign of life from the game.</summary>
    private static long _alive = DateTime.UtcNow.Ticks;

    public override void Load()
    {
        Main.OnPostDraw += Tick;

        // A watchdog on its own thread, because the ones on the main thread
        // cannot fire when the main thread is the thing that has stopped.
        Thread guard = new(() =>
        {
            while (true)
            {
                Thread.Sleep(5000);
                if ((_phase != Phase.Running && _phase != Phase.Driving)
                    || new TimeSpan(DateTime.UtcNow.Ticks - _alive).TotalSeconds < Frozen)
                {
                    continue;
                }

                Log.Sample("unattended", "the game stopped responding",
                    ("case", AgentMod.Pathing.Current),
                    ("number", AgentMod.Pathing.Number),
                    ("doing", AgentPlayer.Local.Doing.Behaviour));
                Log.Close();
                Environment.Exit(5);
            }
        })
        { IsBackground = true, Name = "agent arena watchdog" };
        guard.Start();
    }

    public override void Unload() => Main.OnPostDraw -= Tick;

    /// <summary>
    /// Runs every frame, menu or world, which is the whole reason it is this hook.
    /// </summary>
    private static void Tick(GameTime gameTime)
    {
        if (_phase == Phase.Idle)
        {
            return;
        }

        double now = ++_frames / 60.0;
        _alive = DateTime.UtcNow.Ticks;

        // A single-player game stops updating the world the moment its window is not
        // the foreground one, and goes on drawing, so a run whose window was clicked
        // away from sat still for minutes writing heartbeats and nothing else. Told it
        // has focus every frame, it keeps updating whoever is in front.
        if (_phase is Phase.Running or Phase.Driving)
        {
            Main.hasFocus = true;
        }

        switch (_phase)
        {
            case Phase.Entering when Main.gameMenu:
                Enter(now);
                return;

            case Phase.Entering:
                // The world is up. Give it a moment: it finishes loading
                // around the player, and a scenario built into a half-loaded
                // world measures the loading rather than the agent.
                _phase = Phase.Settling;
                _since = now;
                return;

            case Phase.Settling when now - _since < Settle:
                return;

            case Phase.Settling:
                _phase = Phase.Running;

                // A resolution in the flag, because things that scale with the window
                // only misbehave at a window you are not testing at.
                Match size = Regex.Match(_only, @"(\d{3,5})x(\d{3,5})");
                if (size.Success)
                {
                    _only = _only.Remove(size.Index, size.Length).Trim();
                    Main.SetResolution(int.Parse(size.Groups[1].Value),
                        int.Parse(size.Groups[2].Value));
                    Log.Sample("unattended", "resolution set",
                        ("screen", $"{Main.screenWidth}x{Main.screenHeight}"));
                }

                // The flag names a suite, or a case within one. "combat" runs the
                // fights; anything else is a pathing scenario, which is what the flag
                // has always meant.
                if (_only.StartsWith("combat", StringComparison.OrdinalIgnoreCase))
                {
                    AgentMod.Combat.Only = _only.Length > "combat".Length
                        ? _only["combat".Length..].Trim()
                        : "";
                    AgentMod.Combat.Start();
                    return;
                }

                // Free play rather than a suite: turn driving on and let the
                // progression graph choose for itself. This is the only mode that
                // exercises the agent working an objective in a real world (gathering,
                // crafting, the index going stale under it); every scenario above
                // builds the situation it scores and then scores only the walk.
                if (_only.StartsWith("drive", StringComparison.OrdinalIgnoreCase))
                {
                    string howLong = _only["drive".Length..].Trim();

                    // The wall clock, which is what the journal stamps. Drawn frames
                    // run faster and game updates slower once the window loses focus,
                    // so counting frames makes "drive 240" run to four hundred.
                    double seconds = double.TryParse(howLong, out double asked) ? asked : DriveFor;
                    _driveUntil = DateTime.UtcNow.AddSeconds(seconds);
                    AgentPlayer.Local.Doing.Driving = true;
                    Log.Sample("unattended", "driving", ("seconds", seconds));
                    _phase = Phase.Driving;
                    return;
                }

                if (_only.StartsWith("lights", StringComparison.OrdinalIgnoreCase))
                {
                    AgentMod.Lights.Only = _only.Length > "lights".Length
                        ? _only["lights".Length..].Trim()
                        : "";
                    AgentMod.Lights.Start();
                    return;
                }

                AgentMod.Pathing.Only = _only;
                AgentMod.Pathing.Start();
                return;

            case Phase.Running when AgentMod.Pathing.Running || AgentMod.Combat.Running
                                    || AgentMod.Lights.Running:
                // Once a second, from the draw loop. If this stops the game
                // itself has stopped; if it continues while the scenario does
                // not, the player update has.
                if (_frames % 60 == 0)
                {
                    Log.Sample("heartbeat", Scenario,
                        ("n", Reached),
                        ("doing", AgentPlayer.Local.Doing.Behaviour),
                        ("dead", Main.LocalPlayer.dead));
                }

                Watchdog(now);
                return;

            case Phase.Driving when DateTime.UtcNow < _driveUntil:
                if (_frames % 60 == 0)
                {
                    Log.Sample("heartbeat", "driving",
                        ("doing", AgentPlayer.Local.Doing.Behaviour),
                        ("dead", Main.LocalPlayer.dead));
                }

                return;

            case Phase.Driving:
                AgentPlayer.Local.Doing.Driving = false;
                goto case Phase.Running;

            case Phase.Running:
                Log.Sample("unattended", "done", ("report", Log.Path));
                Log.Close();
                _phase = Phase.Leaving;
                _since = now;
                Main.instance.Exit();
                return;

            case Phase.Leaving when now - _since > Shutdown:
                // Asking politely first and insisting afterwards. Exit() from
                // inside a draw callback returns without the process ending: it
                // stays up holding the file lock that stops the mod being
                // rebuilt, which breaks the loop this exists to close.
                Environment.Exit(0);
                return;
        }
    }

    /// <summary>Whichever suite is running, so the watchdog works for both.</summary>
    private static string Scenario =>
        AgentMod.Combat.Running ? AgentMod.Combat.Current
        : AgentMod.Lights.Running ? AgentMod.Lights.Current
        : AgentMod.Pathing.Current;

    /// <summary>How far the running suite has got. Standing still here is a hang.</summary>
    private static int Reached =>
        AgentMod.Combat.Running ? AgentMod.Combat.Number
        : AgentMod.Lights.Running ? AgentMod.Lights.Number
        : AgentMod.Pathing.Number;

    private static void Watchdog(double now)
    {
        if (Main.LocalPlayer.dead)
        {
            Log.Sample("unattended", "reviving the character",
                ("case", Scenario));
            Main.LocalPlayer.respawnTimer = 0;
            Main.LocalPlayer.Spawn(PlayerSpawnContext.ReviveFromDeath);
            return;
        }

        if (Reached != _seen)
        {
            _seen = Reached;
            _moved = now;
            return;
        }

        if (now - _moved > Stall)
        {
            Log.Sample("unattended", "the suite stopped advancing",
                ("case", Scenario),
                ("waited", now - _moved));
            Log.Close();
            Environment.Exit(4);
        }
    }

    /// <summary>Walk the menu into the first world of the first character.</summary>
    private static void Enter(double now)
    {
        // Loading is asynchronous, so this is called every frame until the menu goes
        // away. Asking twice would restart it.
        if (_playing)
        {
            if (now > LoadLimit)
            {
                Log.Sample("unattended", "world never loaded", ("waited", now));
                Environment.Exit(3);
            }

            return;
        }

        if (_fresh)
        {
            EnterFresh(now);
            return;
        }

        Main.LoadPlayers();
        Main.LoadWorlds();
        if (Main.PlayerList.Count == 0 || Main.WorldList.Count == 0)
        {
            Log.Sample("unattended", "nothing to load",
                ("players", Main.PlayerList.Count),
                ("worlds", Main.WorldList.Count));
            Environment.Exit(2);
            return;
        }

        // SetAsActive does what choosing them on the menu does, which is more than
        // assigning the fields: it loads the save behind each one.
        Main.PlayerList[0].SetAsActive();
        Main.WorldList[0].SetAsActive();

        Log.Sample("unattended", "entering",
            ("player", Main.ActivePlayerFileData.Name),
            ("world", Main.ActiveWorldFileData.Name));

        _playing = true;
        WorldGen.playWorld();
    }

    /// <summary>
    /// Makes a character and a world that have never been played and walks into them.
    /// </summary>
    private static void EnterFresh(double now)
    {
        if (_generating == null)
        {
            _freshName = "Fresh-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");

            // What the character creation screen hands a classic character: the
            // three copper tools, then whatever loaded mods add to a start.
            Player player = new() { name = _freshName, difficulty = 0 };
            player.statLife = player.statLifeMax = 100;
            player.statMana = player.statManaMax = 20;
            Item[] vanilla =
            {
                new Item(ItemID.CopperShortsword),
                new Item(ItemID.CopperPickaxe),
                new Item(ItemID.CopperAxe),
            };
            PlayerLoader.SetStartInventory(player,
                PlayerLoader.GetStartingItems(player, vanilla, mediumCoreDeath: false));

            // Spawning reads these back even for a classic character, and a character
            // made without them takes the game down on its first spawn.
            player.savedPerPlayerFieldsThatArentInThePlayerClass = new();
            CreativePowerManager.Instance.ResetDataForNewPlayer(player);
            PlayerFileData.CreateAndSave(player).SetAsActive();

            // A small classic world with a random seed and a random evil, which is
            // the cheapest one the game will generate and the one a first run sees.
            Main.maxTilesX = 4200;
            Main.maxTilesY = 1200;
            WorldGen.setWorldSize();
            WorldGen.WorldGenParam_Evil = -1;
            Main.GameMode = 0;
            Main.worldName = _freshName;
            Main.ActiveWorldFileData = WorldFile.CreateMetadata(_freshName, false, 0);
            Main.ActiveWorldFileData.SetSeedToRandom();
            Main.menuMode = 10;
            _generating = WorldGen.CreateNewWorld();
            Log.Sample("unattended", "generating",
                ("player", _freshName),
                ("world", _freshName),
                ("seed", Main.ActiveWorldFileData.Seed));
            return;
        }

        if (!_generating.IsCompleted)
        {
            if (now > LoadLimit)
            {
                Log.Sample("unattended", "world never generated", ("waited", now));
                Environment.Exit(3);
            }

            return;
        }

        if (_generating.IsFaulted)
        {
            Log.Sample("unattended", "world generation failed",
                ("error", _generating.Exception?.GetBaseException().Message));
            Environment.Exit(3);
            return;
        }

        // Generation saves the world and drops back to the menu rather than entering
        // it, so find the file it wrote and go in the way a saved world is gone into.
        Main.LoadWorlds();
        WorldFileData? made = Main.WorldList.FirstOrDefault(w => w.Name == _freshName);
        if (made == null)
        {
            Log.Sample("unattended", "generated world not found",
                ("world", _freshName),
                ("worlds", Main.WorldList.Count));
            Environment.Exit(3);
            return;
        }

        made.SetAsActive();

        // The character again, by name from disk. About one run in ten arrives here
        // with the active record's player gone (the name logs as null) and the map
        // loader takes the game down on it. SetAsActive on a freshly created record
        // evidently does not always leave it loaded the way the menu would.
        if (Main.ActivePlayerFileData?.Player is null)
        {
            Main.LoadPlayers();
            PlayerFileData? saved = Main.PlayerList.FirstOrDefault(p => p.Name == _freshName);
            if (saved is null)
            {
                Log.Sample("unattended", "fresh character not found",
                    ("player", _freshName),
                    ("players", Main.PlayerList.Count));
                Environment.Exit(3);
                return;
            }

            saved.SetAsActive();
        }

        Log.Sample("unattended", "entering",
            ("player", Main.ActivePlayerFileData!.Name),
            ("world", Main.ActiveWorldFileData.Name),
            ("fresh", true));
        _playing = true;
        _frames = 0;
        WorldGen.playWorld();
    }
}
#endif
