using System.Collections.Generic;
using Terragent.Work;
using Terragent.World;

namespace Terragent;

/// <summary>The whole agent, as the game sees it.</summary>
// One object above everything else, so that the ModPlayer hook is three lines and none of
// them is a decision. Terraria calls in on its own schedule and expects to be handed
// control back immediately; deciding what a run does next is not that, and none of this
// belongs in a class the game constructs.
//
// It owns the progression, the foreman and the pilot, and it is the only thing that knows
// they exist in that order.
internal interface IAgent
{
    /// <summary>Whether the agent is playing. False leaves the keys alone.</summary>
    // Off by default. A mod that starts playing the moment it loads cannot be watched, and
    // the first thing anyone wants is to stand still and read the panel.
    bool Driving { get; set; }

    /// <summary>What this character has finished, for saving with it.</summary>
    // Passed straight out to the player file. Reached once is reached, and a run that
    // re-derived it from what is carried would chop its hundred wood again every time the
    // game was reopened.
    IReadOnlyCollection<string> Reached { get; }

    /// <summary>What it is working on, and through it the pilot and the job.</summary>
    // Read only, and only the panel reads it. Everything the display needs is already an
    // answer some interface gives, so exposing the chain beats keeping a second copy of
    // the run in a bag of fields for the panel to read.
    IForeman Foreman { get; }

    /// <summary>The run, so the panel can show where it has got to.</summary>
    IProgression Progression { get; }

    /// <summary>The ground, so the overlay can tell a booked tile from a broken one.</summary>
    // A route books its digging when it is planned, and the plan is never rewritten as it
    // is walked. Drawing it needs to know what is still standing, or a tile that has
    // already gone keeps its mark until the whole step is behind the body.
    ITerrain Terrain { get; }

    /// <summary>One tick of playing.</summary>
    // Record what is met, take the objective that follows from it, and let the foreman
    // spend the tick. Everything the agent does happens under this call.
    void Tick();

    /// <summary>Take back what this character had finished when it was last put down.</summary>
    void Restore(IEnumerable<string> reached);
}
