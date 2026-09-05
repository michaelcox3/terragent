using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;
using Terragent.Progression;

namespace Terragent;

/// <summary>
/// Drives the local character by writing the same control flags a keyboard would.
/// </summary>
public sealed class AgentPlayer : ModPlayer
{
    private int _sampleCountdown;
    private int _bestPick;

    /// <summary>Build the agent, once this ModPlayer has a character to drive.</summary>
    // Not the constructor: tModLoader sets Player after making a ModPlayer, and reaching
    // for Main.LocalPlayer instead is what makes a mod one-agent only.
    public override void Initialize()
    {
        if (_executor is not null)
        {
            return;
        }

        _character = new Character(Player);

        // Made here rather than in a field initialiser, for the same reason Character is.
        _belief = new Belief(Player);

        // One Pilot, shared: two route planners would be two answers about where the
        // character is going. Executor and Building know each other; that loop is
        // wired here rather than hidden.
        _exploring = new Exploring(_pilot, _belief, _character);
        _lamplighting = new Lamplighting(_belief, _character);
        _fighting = new Fighting(_pilot, _exploring, _belief, _character);
        _building = new Building(_pilot, _belief, _character,
            _stations, _workshop);
        _executor = new Executor(_pilot, _exploring, _fighting, _building,
            _belief, _character, new Requirements(_belief, _character, _stations));
        _building.Runs(_executor);
        _agenda = new Agenda(AgentMod.Progression, _belief, _character, _stations);
    }

    /// <summary>
    /// What this character has seen, what it is doing, and what it has built.
    /// </summary>
    // Per AgentPlayer, not static: tModLoader makes a ModPlayer per player, and this is
    // what lets a second agent exist. Assigned in Initialize, which runs before any hook.
    private Belief _belief = null!;
    private readonly AgentState _state = new();
    private readonly Stations _stations = new();
    private readonly Workshop _workshop = new();

    /// <summary>The agent driving this client's character.</summary>
    // For the panel and the overlay, which are ModSystems with no player of their own.
    // A lookup rather than a global, so on a server with several agents it answers
    // about the local one.
    internal static AgentPlayer Local => Main.LocalPlayer.GetModPlayer<AgentPlayer>();

    /// <summary>What this agent has seen, for anything that only draws it.</summary>
    internal Belief Seen => _belief;

    /// <summary>The character this agent drives, for anything that only reports on it.</summary>
    internal Character Acting => _character;

    /// <summary>What this agent is doing, for anything that only draws it.</summary>
    internal AgentState Doing => _state;

    /// <summary>The jobs on the executor's board, and which is being worked.</summary>
    // Jobs, not sentences: the panel decides how a job reads.
    internal IReadOnlyList<Executor.Job> Jobs => _executor.Board;

    internal int WorkingJob => _executor.Chosen;

    private readonly Pilot _pilot = new();

    // The null! fields are assigned in Initialize, which tModLoader calls before any
    // hook; nothing runs in the window before it.
    private Exploring _exploring = null!;

    private Fighting _fighting = null!;

    private Building _building = null!;

    private Executor _executor = null!;
    private Lamplighting _lamplighting = null!;

    private Agenda _agenda = null!;
    private Character _character = null!;

    private bool _measured;

    public override void PostUpdate()
    {
        if (Player.whoAmI != Main.myPlayer)
        {
            return;
        }

        // Once the character is loaded: these come off the player, not a constant.
        if (!_measured)
        {
            _measured = true;
            Log.Sample("movement", "as the game has it",
                ("screenTilesAcross", Terraria.NPC.sWidth / 16),
                ("screenTilesDown", Terraria.NPC.sHeight / 16),
                ("jumpHeightFrames", Terraria.Player.jumpHeight),
                ("jumpSpeed", Terraria.Player.jumpSpeed),
                ("gravity", Terraria.Player.defaultGravity),
                ("maxRunSpeed", Player.maxRunSpeed),
                ("runAcceleration", Player.runAcceleration),
                ("jumpHeightTiles", _character.Movement.JumpHeight),
                ("jumpSpeedBoost", Player.jumpSpeedBoost),
                ("jumpBoost", Player.jumpBoost),
                ("gravity", Player.gravity),
                ("airFrames", _character.Movement.AirFrames),
                ("leapStandingTiles", _character.Movement.LeapPixels(0f) / 16f),
                ("leapFullRunTiles",
                    _character.Movement.LeapPixels(Player.maxRunSpeed) / 16f));
        }

        // Last word: whatever got past the hooks is undone before the next frame, so
        // invulnerable means the life total does not move at all.
        if (AgentMod.Invulnerable)
        {
            Player.statLife = Player.statLifeMax2;
            Player.breath = Player.breathMax;
        }

        // Coverage samples ~90,000 cells; once a second is plenty for a readout and
        // invisible next to the game's own frame.
        if (--_sampleCountdown > 0)
        {
            return;
        }

        _sampleCountdown = 60;

        // A better pickaxe opens walls the old one could not, so tiles written off as
        // immovable must be reconsidered.
        int pick = _character.Inventory.PickPower;
        if (pick > _bestPick)
        {
            _bestPick = pick;
            _pilot.ReconsiderWalls();
        }

        // Reviewing presses no button, so it belongs in this sampled tick, not in Drive.
        _agenda.Review(_state, Main.GameUpdateCount / 60.0);
        _state.Coverage = _belief.Coverage();

        // Aiming writes the mouse and the game derives the target from it, so
        // these two should agree.
        if (_character.Hand.Intended is { } wanted)
        {
            Point16 got = Hand.Actual;
            _state.Aim = wanted.X == got.X && wanted.Y == got.Y
                ? $"({got.X}, {got.Y})"
                : $"wanted ({wanted.X}, {wanted.Y}) but hit ({got.X}, {got.Y})";
        }
    }

    /// <summary>
    /// Read back what the game thinks, rather than what we asked for.
    /// </summary>
    private void ReadBack(AgentState state)
    {
        Item held = Player.inventory[Player.selectedItem];
        state.Holding = held.IsAir ? "nothing" : held.Name;
        state.UseDown = Player.controlUseItem;
        state.Aimed = new Point(Terraria.Player.tileTargetX, Terraria.Player.tileTargetY);

        // Only for things that act on tiles: a pickaxe, an axe, a hammer, or a
        // block being placed.
        bool tool = held.pick > 0 || held.axe > 0 || held.hammer > 0
                    || held.createTile != -1;
        state.ReachBox = state.Driving && tool ? _character.Hand.ReachBox : null;
        state.ReachAgrees = _character.Hand.ReachAgrees;
        state.SwingTicks = Player.itemAnimation;
    }

    public override void ProcessTriggers(Terraria.GameInput.TriggersSet triggers)
    {
        if (Player.whoAmI != Main.myPlayer)
        {
            return;
        }

        if (AgentMod.ToggleDriving?.JustPressed == true)
        {
            AgentState state = _state;
            state.Driving = !state.Driving;
            if (!state.Driving)
            {
                state.ClearPlan();
                state.Behaviour = "";
                state.ReachBox = null;
            }

            Main.NewText(
                state.Driving ? "[Agent] taking the controls" : "[Agent] you have them back",
                state.Driving ? Color.LightGreen : Color.LightGray);
            Log.Sample("boot", state.Driving ? "driving" : "released",
                ("build", AgentMod.BuildStamp),
                ("invulnerable", AgentMod.Invulnerable));
        }

        if (AgentMod.TogglePanel?.JustPressed == true)
        {
            AgentMod.ShowPanel = !AgentMod.ShowPanel;
        }

        if (AgentMod.ToggleOverlay?.JustPressed == true)
        {
            AgentMod.ShowOverlay = !AgentMod.ShowOverlay;
        }

#if TESTING
        if (AgentMod.RunTests?.JustPressed == true)
        {
            if (AgentMod.Pathing.Running)
            {
                AgentMod.Pathing.Stop("stopped");
                Main.NewText("[Agent] arena stopped", Color.Orange);
            }
            else
            {
                _state.Driving = false;
                AgentMod.Pathing.Start();
                Main.NewText(
                    "[Agent] running movement scenarios in the sky; results in the journal",
                    Color.LightGreen);
            }
        }
#endif

        if (AgentMod.ToggleInvulnerable?.JustPressed == true)
        {
            AgentMod.Invulnerable = !AgentMod.Invulnerable;
            Main.NewText(
                AgentMod.Invulnerable
                    ? "[Agent] invulnerable: results are plan validity, not play"
                    : "[Agent] mortal",
                AgentMod.Invulnerable ? Color.LightGreen : Color.Orange);
        }
    }

    /// <summary>
    /// Refuse all damage while <see cref="AgentMod.Invulnerable"/> is set.
    /// </summary>
    public override bool ImmuneTo(PlayerDeathReason damageSource, int cooldownCounter,
        bool dodgeable) => AgentMod.Invulnerable;

    public override void PostUpdateEquips()
    {
        if (!AgentMod.Invulnerable)
        {
            return;
        }

        // Real game mechanics rather than hacks: these are the fields a Cobalt Shield,
        // an Obsidian Skull and a Lucky Horseshoe set.
        Player.noKnockback = true;
        Player.lavaImmune = true;
        Player.fireWalk = true;
        Player.noFallDmg = true;

        // Breath is not a hit, so ImmuneTo does not cover it: it runs down underwater
        // and bleeds life through the regen.
        Player.breath = Player.breathMax;
    }

    /// <summary>
    /// Cancel damage over time, which never goes near <see cref="ImmuneTo"/>.
    /// </summary>
    // Drowning, lava, poison and the rest drain through lifeRegen. Refusing hits says
    // nothing about any of them.
    public override void UpdateBadLifeRegen()
    {
        if (AgentMod.Invulnerable && Player.lifeRegen < 0)
        {
            Player.lifeRegen = 0;
        }
    }

    public override void SetControls()
    {
        // tModLoader catches whatever escapes a ModPlayer hook and carries on, so an
        // unlogged throw leaves the journal looking like healthy progress. Rethrown
        // afterwards: swallowing it is upstream's call, not this mod's.
        try
        {
            Drive();
        }
        catch (System.Exception crash)
        {
            Log.Event("crash", crash.Message,
                ("type", crash.GetType().Name),
                ("where", crash.StackTrace?.Split('\n')[0].Trim() ?? ""));
            throw;
        }
    }

    private void Drive()
    {
        // A ModPlayer exists for every player in the game; only drive the one this
        // client owns.
        AgentState state = _state;
        if (Player.whoAmI != Main.myPlayer)
        {
            return;
        }

#if TESTING
        if (AgentMod.Combat.Running)
        {
            _character.Silence();
            state.ClearPlan();
            AgentMod.Combat.Update(_belief, _character, state,
                Main.GameUpdateCount / 60.0);
            state.Behaviour = AgentMod.Combat.Status;
            ReadBack(state);
            return;
        }

        if (AgentMod.Lights.Running)
        {
            _character.Silence();
            state.ClearPlan();
            AgentMod.Lights.Update(_belief, _character, state,
                Main.GameUpdateCount / 60.0);
            state.Behaviour = AgentMod.Lights.Status;
            ReadBack(state);
            return;
        }

        // The tests drive the same follower through the same controls.
        if (AgentMod.Pathing.Running)
        {
            _character.Silence();
            state.ClearPlan();
            AgentMod.Pathing.Update(_belief, _character, state,
                Main.GameUpdateCount / 60.0);
            state.Behaviour = AgentMod.Pathing.Status;
            ReadBack(state);
            Swings.Audit(_belief, _character, state);
            return;
        }
#endif

        if (!state.Driving)
        {
            return;
        }

        // Take the controls away from the person watching, before deciding anything.
        _character.Silence();
        state.ClearPlan();

        // Restated every tick by whatever is running, so a reservation cannot outlive
        // the job that wanted it.
        _character.Inventory.FreeAll();
        double now = Main.GameUpdateCount / 60.0;

        // Before anything asks where something is; a slice a tick keeps the index
        // under a third of a second stale.
        _belief.Sweep();

        // First, because the belief only fills in where the map is lit: an agent in the
        // dark cannot tell rock from air however close it stands.
        if (_lamplighting.Light(state))
        {
            state.Behaviour = "putting a torch down";
            ReadBack(state);
            return;
        }

        // Reclaim while still in reach. Reads last tick's answer about what is wanted,
        // because the objective decides that and runs after this.
        if (_building.Reclaim(state, now))
        {
            ReadBack(state);
            return;
        }

        // Hostiles in arm's reach come before the objective, or the agent arrives
        // somewhere with a fight already in progress behind it.
        if (_fighting.Nearby(state, now))
        {
            ReadBack(state);
            Swings.Audit(_belief, _character, state);
            return;
        }

        // The graph chose; do that. Nothing here decides what to work on.
        if (_agenda.Objective is null)
        {
            // The standing requirement goes through the same executor as a goal, not a
            // parallel path that would drift. No backoff: nothing changes while it
            // waits, so there is nothing to retry later.
            if (_agenda.Shortfalls.Count > 0)
            {
                Upkeep alone = _agenda.Shortfalls[0];
                _executor.Obtain(new ItemQuantity(alone.ItemID, alone.Ceiling), state, now);
                ReadBack(state);
                Swings.Audit(_belief, _character, state);
                Diagnose.Watch(_belief, _character.Movement.Footing,
                    state.Goal, state.Behaviour, now);
                return;
            }

            state.Behaviour = "";
            return;
        }

        // Restocking rides along with the objective instead of stopping it, so the
        // panel lists both and the agent digs whichever it can reach.
        _executor.Act(_agenda.Objective.Node, state, now, _agenda.Shortfalls);

        ReadBack(state);
        Swings.Audit(_belief, _character, state);
        Diagnose.Watch(_belief, _character.Movement.Footing, state.Goal,
            state.Behaviour, now);

        Log.Event("act", state.Behaviour,
            ("held", state.Holding),
            ("slot", Player.selectedItem),
            ("use", Player.controlUseItem),
            // From Player.ItemCheck: a swing starts only when controlUseItem and
            // releaseUseItem are both set and itemAnimation is zero; mining runs only
            // when toolTime is zero while itemAnimation is above it.
            ("release", Player.releaseUseItem),
            ("anim", Player.itemAnimation),
            ("tool", Player.toolTime),
            ("aimWanted", _character.Hand.Intended?.ToString() ?? ""),
            ("aimActual", Hand.Actual.ToString()),
            // The game's own verdict, not whether ours matches it: ReachAgrees is also
            // true when both say no.
            ("inRange", _character.Hand.GameSaysInRange),
            ("agrees", state.ReachAgrees),
            ("anim2", Player.itemAnimationMax),
            ("useTime", Player.HeldItem.useTime),
            ("axe", Player.HeldItem.axe),
            ("delayUse", Player.delayUseItem),
            ("mouseUI", Player.mouseInterface),
            ("pos", _character.Movement.Footing.ToString()));

        if (state.Stuck)
        {
            // The board as it stood, because the reason names one job and the question
            // is why that one.
            Log.Sample("board", state.Behaviour,
                ("chosen", WorkingJob),
                ("jobs", string.Join(" | ", Jobs.Select(job =>
                    job.Where is { } where
                        ? $"{JobLine.Doing(job)} at ({where.X},{where.Y})"
                        : JobLine.Doing(job)))));
            _agenda.SetAside(state, Main.GameUpdateCount / 60.0);
        }
    }

}
