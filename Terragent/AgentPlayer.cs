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

#if TESTING
    /// <summary>The scenario arena, while one is being walked.</summary>
    private Tests.Arena? _arena;
#endif

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
    internal bool Invulnerable { get; private set; } = true;

    /// <summary>Turn damage off, or let it back in.</summary>
    // Through here rather than by assignment, so the keybind and the panel switch cannot
    // drift apart, and for the same shape as Drive.
    internal void Protect(bool safe) => Invulnerable = safe;

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

        ISites sites = new Sites(terrain);
        IProgression progression = Progression.Read(Mod, recipes, terrain, bag, hand, body,
            sites, new Drops(terrain), new Creatures(terrain), clock, journal);

        _agent = new Agent(progression, new Foreman(body, pilot, clock, journal),
            new Lamplighter(terrain, bag, hand, sites, body, pilot, journal), terrain, journal);
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

#if TESTING
        // The scenario arena, which steers the body itself and leaves the agent switched
        // off. Here because this is where a key held counts for this frame.
        if ((_arena ?? Runner.Arena) is { Running: true } arena)
        {
            Loose();
            arena.Tick();
            return;
        }
#endif

        if (_agent is { Driving: true })
        {
            Loose();
        }

        _agent?.Tick();
    }

    /// <summary>Let go of every key the person at the keyboard might be holding.</summary>
    // The keyboard is read into these before this hook runs, so anything a person is
    // holding would be added to whatever the agent presses: they steer together and
    // neither gets where it was going. While something else is driving, it drives.
    private void Loose()
    {
        Player.controlLeft = false;
        Player.controlRight = false;
        Player.controlJump = false;
        Player.controlDown = false;
        Player.controlUp = false;
        Player.controlUseItem = false;
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
            Protect(!Invulnerable);
            Main.NewText(
                Invulnerable ? "[Agent] nothing can hurt you" : "[Agent] mortal again",
                Invulnerable ? Color.LightGreen : Color.LightGray);
        }

#if TESTING
        if (TerragentMod.RunScenarios?.JustPressed == true)
        {
            Walk(agent);
        }

        if (TerragentMod.CopyGround?.JustPressed == true)
        {
            Copy(agent);
        }
#endif

        if (TerragentMod.ToggleDriving?.JustPressed != true)
        {
            return;
        }

        agent.Drive(!agent.Driving);
        Mod.Logger.Info(agent.Driving ? "[driving] taking the controls"
            : "[driving] you have them back");
        Main.NewText(
            agent.Driving ? "[Agent] taking the controls" : "[Agent] you have them back",
            agent.Driving ? Color.LightGreen : Color.LightGray);
    }

#if TESTING
    /// <summary>Start walking the scenarios, or stop part way through.</summary>
    // Driving goes off for the duration. The arena steers the body itself, and a foreman
    // choosing its own work at the same time is a second thing pressing the keys.
    private void Walk(IAgent agent)
    {
        if (_arena is { Running: true } walking)
        {
            walking.Stop("stopped");
            Main.NewText("[Agent] scenarios stopped", Color.LightGray);
            return;
        }

        agent.Drive(false);
        Protect(true);
        _arena = new Tests.Arena(agent.Foreman.Pilot, agent.Terrain, new Journal(Mod));
        _arena.Start(string.Empty);
        Main.NewText("[Agent] walking the scenarios", Color.LightGreen);
    }
#endif

#if TESTING
    /// <summary>Write the ground around the agent out as a scenario grid.</summary>
    // The route as well as the two ends, so what the search was looking at when it chose
    // is in the picture and not just where it started and where it was going.
    private void Copy(IAgent agent)
    {
        string path = Tests.Snapshot.Write(
            agent.Terrain,
            new Controls.Inventory(Player).PickPower,
            World.Hitbox.Footing(Player.position, Player.height),
            agent.Foreman.Destination?.Site,
            agent.Foreman.Pilot.Route);

        Mod.Logger.Info($"[ground] copied to {path}");
        Main.NewText($"[Agent] ground copied to {path}", Color.LightGreen);
    }
#endif

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
