using Terraria.ModLoader;
using Terragent.World;

namespace Terragent;

/// <summary>An agent that plays Terraria, built from classical AI.</summary>
// The namespace has to match the folder and the assembly name, all three Terragent:
// tModLoader's VerifyMod refuses to load a mod whose top level namespace differs from
// its folder, and the message says only that they do not match.
public sealed class TerragentMod : Mod
{
    /// <summary>The key that hands the character over, and takes it back.</summary>
    // Static because a keybind is registered once for the mod rather than per character,
    // and tModLoader hands it back only from Load. Nulled in Unload, or a reload leaves a
    // binding pointing at the mod that has gone.
    public static ModKeybind? ToggleDriving { get; private set; }

    /// <summary>The key that stops the world hurting the character.</summary>
    // For watching the agent play rather than watching it die. A run finished under this
    // says the plan was valid, not that the agent survived it, and any timing taken with
    // it on has to say so.
    public static ModKeybind? ToggleInvulnerable { get; private set; }

    public override void Load()
    {
        ToggleDriving = KeybindLoader.RegisterKeybind(this, "ToggleDriving", "K");
        ToggleInvulnerable = KeybindLoader.RegisterKeybind(this, "ToggleInvulnerable", "J");

        // Read once, here, so a typo in the file throws with the name in it at load rather
        // than becoming a zero somebody chases through a run.
        Mining.Load(this);
    }

    public override void Unload()
    {
        ToggleDriving = null;
        ToggleInvulnerable = null;
    }
}
