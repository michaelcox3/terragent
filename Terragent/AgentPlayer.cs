using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameInput;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terragent.Controls;
using Terragent.Pathfinding;
using Terragent.Report;
using Terragent.Work;
using Terragent.World;

namespace Terragent;

/// <summary>Where the game calls in, and the only place that builds the agent.</summary>
// A ModPlayer exists to be somewhere the game can reach. Deciding anything is not that,
// so this hook is a null check, a hand over to Tick, and the two save calls.
public sealed class AgentPlayer : ModPlayer
{
    private const string ReachedTag = "reached";

    private IAgent? _agent;

    /// <summary>The agent this character is running, or null before it is built.</summary>
    // For the panel, which cannot be handed anything: tModLoader constructs a ModSystem
    // itself, so it reaches the run through the player that owns it.
    internal IAgent? Agent => _agent;

    /// <summary>Whether the world is allowed to hurt this character.</summary>
    // Deliberate, and not the agent's business: it is a switch for watching a run without
    // a slime ending it. Anything measured with this on is a plan result and not a play
    // result.
    // On to begin with. A run that dies to the first slime says nothing about the plan,
    // and turning it off is a deliberate act rather than the default.
    internal bool Invulnerable { get; set; } = true;

    /// <summary>Build the agent, once this ModPlayer has a character to drive.</summary>
    // Not a field initialiser and not a constructor: tModLoader sets Player after making
    // a ModPlayer, and reaching for Main.LocalPlayer instead is what makes a mod work for
    // one character only.
    public override void Initialize()
    {
        if (_agent is not null)
        {
            return;
        }

        IJournal journal = new Journal(Mod);
        ITerrain terrain = new Terrain();
        IBody body = new Body(Player);
        IHand hand = new Hand(Player);
        IInventory bag = new Inventory(Player);

        IClock clock = new Clock();
        IPilot pilot = new Pilot(new Navigator(terrain), terrain, body, hand, bag, clock,
            journal);
        // The book is read once here rather than per objective: it is five thousand rows
        // and the same in every world.
        IRecipeTree recipes = new RecipeTree(Recipes.Book(), Recipes.Gathered);

        IProgression progression = Progression.Read(Mod, recipes, terrain, bag, hand, body,
            new Sites(terrain), new Drops(terrain), new Creatures(terrain), clock,
            journal);

        _agent = new Agent(progression, new Foreman(body, pilot, clock, journal), terrain,
            journal);
    }

    /// <summary>One tick of playing, at the point the game reads the controls.</summary>
    // SetControls rather than PostUpdate: this runs while Terraria is deciding what the
    // player pressed, so a key held here is a key held this frame. Written in PostUpdate
    // it would apply a frame late and fight the movement it was meant to cause.
    public override void SetControls()
    {
        // A ModPlayer exists for every player in the game. Only drive the one this client
        // owns, or the mod presses keys on somebody else.
        if (Player.whoAmI != Main.myPlayer)
        {
            return;
        }

        // The keyboard is read into these before this hook runs, so anything the person at
        // the keyboard is holding would be added to whatever the agent presses: they steer
        // together and neither gets where it was going. While it is driving, it drives.
        if (_agent is { Driving: true })
        {
            Player.controlLeft = false;
            Player.controlRight = false;
            Player.controlJump = false;
            Player.controlDown = false;
            Player.controlUp = false;
            Player.controlUseItem = false;
        }

        _agent?.Tick();
    }

    public override void ProcessTriggers(TriggersSet triggers)
    {
        IAgent? agent = _agent;
        if (Player.whoAmI != Main.myPlayer || agent is null)
        {
            return;
        }

        if (TerragentMod.ToggleInvulnerable?.JustPressed == true)
        {
            Invulnerable = !Invulnerable;
            Main.NewText(
                Invulnerable ? "[Agent] nothing can hurt you" : "[Agent] mortal again",
                Invulnerable ? Color.LightGreen : Color.LightGray);
        }

        if (TerragentMod.ToggleDriving?.JustPressed != true)
        {
            return;
        }

        agent.Driving = !agent.Driving;
        Mod.Logger.Info(agent.Driving ? "[driving] taking the controls"
            : "[driving] you have them back");
        Main.NewText(
            agent.Driving ? "[Agent] taking the controls" : "[Agent] you have them back",
            agent.Driving ? Color.LightGreen : Color.LightGray);
    }

    /// <summary>Refuse all damage while the switch is on.</summary>
    public override bool ImmuneTo(PlayerDeathReason damageSource, int cooldownCounter,
        bool dodgeable) => Invulnerable;

    /// <summary>And refuse the things that kill without dealing damage.</summary>
    // Drowning and lava burn through life regeneration rather than through a hit, so
    // ImmuneTo never sees them.
    public override void PostUpdate()
    {
        if (Invulnerable)
        {
            Player.breath = Player.breathMax;
            Player.lavaImmune = true;
        }
    }

    public override void SaveData(TagCompound tag)
    {
        IAgent? agent = _agent;
        if (agent is not null && agent.Reached.Count > 0)
        {
            tag[ReachedTag] = new List<string>(agent.Reached);
        }
    }

    public override void LoadData(TagCompound tag)
    {
        // Initialize runs before this on a load, but not on every path tModLoader takes to
        // get here, so the agent is made rather than assumed.
        Initialize();

        if (tag.TryGet(ReachedTag, out List<string> reached))
        {
            _agent?.Restore(reached);
        }
    }
}
