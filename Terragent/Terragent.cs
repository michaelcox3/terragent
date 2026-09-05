using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework.Input;
using Terraria.ID;
using Terraria.ModLoader;
using Terragent.Progression;

namespace Terragent;

/// <summary>
/// An autonomous Terraria player built from classical AI: search, planning and
/// behaviour trees. No LLM is involved in any decision.
/// </summary>
public sealed class AgentMod : Mod
{
    /// <summary>
    /// The pre-Hardmode spine. Built once, at load, out of the file that describes it:
    /// it is our own plan, not game data, and it does not change while the game runs.
    /// </summary>
    internal static Progression.ProgressionGraph Progression { get; private set; } = null!;

    /// <summary>
    /// Whether the agent ignores damage and knockback.
    /// </summary>
    internal static bool Invulnerable { get; set; } = true;

    internal static bool ShowPanel { get; set; } = true;
    internal static bool ShowOverlay { get; set; } = true;

    internal static ModKeybind? ToggleDriving { get; private set; }
    internal static ModKeybind? TogglePanel { get; private set; }
    internal static ModKeybind? ToggleOverlay { get; private set; }
    internal static ModKeybind? ToggleInvulnerable { get; private set; }

    /// <summary>Runs the movement scenarios in a sky arena. See <see cref="Arena"/>.</summary>
#if TESTING
    internal static ModKeybind? RunTests { get; private set; }
#endif

#if TESTING
    internal static PathingTests Pathing { get; } = new();

    internal static CombatTests Combat { get; } = new();

    /// <summary>Whether the agent lights the place it is standing in.</summary>
    internal static LightTests Lights { get; } = new();
#endif

    /// <summary>The short version, for the panel. Bumped in build.txt per patch.</summary>
    internal static string BuildStamp => $"v{ModContent.GetInstance<AgentMod>().Version}";

    /// <summary>
    /// When this assembly was built, for the journal only.
    /// </summary>
    internal static string BuildDetail { get; } =
        typeof(AgentMod).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "unknown";

    public override void Load()
    {
        // The agent drives the player you are looking at, so taking the
        // controls back has to be one keypress and always available.
        ToggleDriving = KeybindLoader.RegisterKeybind(this, "Toggle agent driving", Keys.OemSemicolon);
        TogglePanel = KeybindLoader.RegisterKeybind(this, "Toggle agent panel", Keys.OemQuotes);
        ToggleOverlay = KeybindLoader.RegisterKeybind(this, "Toggle path overlay", Keys.OemPeriod);
        ToggleInvulnerable = KeybindLoader.RegisterKeybind(this, "Toggle invulnerable", Keys.OemComma);
#if TESTING
        RunTests = KeybindLoader.RegisterKeybind(this, "Run pathing tests", Keys.OemQuestion);
#endif

        // Before anything asks for either: every name in them is resolved here, so a
        // typo is a load failure that says which name, rather than a zero that turns
        // into a goal the run cannot reach.
        Progression = Terragent.Progression.ProgressionGraph.Load(this);
        Upkeeps.Load(this);
        Tiles.Load(this);
        foreach (int kind in Tiles.Watched.Concat(StationKinds.Watched))
        {
            TileIndex.Watch(kind);
        }

        // Beside tModLoader's own logs, so there is one place to look.
        Log.Open(System.IO.Path.Combine(Logging.LogDir, "agent"));
        // What the index was told to keep. Registering the wrong ids is silent, and the
        // agent simply never finds a tree, so the count and the names are said once at
        // load where a zero or a wrong name is obvious.
        Log.Sample("boot", "loaded", ("build", BuildStamp), ("built", BuildDetail),
            ("log", Log.Path),
            ("watching", string.Join(" ", Tiles.Watched.Concat(StationKinds.Watched)
                .Select(TileID.Search.GetName))));
        Logger.Info($"agent log: {Log.Path}");
    }

    public override void Unload()
    {
        Log.Close();
        ToggleDriving = null;
        TogglePanel = null;
        ToggleOverlay = null;
        ToggleInvulnerable = null;
#if TESTING
        RunTests = null;
#endif
    }
}
