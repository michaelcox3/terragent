using Microsoft.Xna.Framework;
using Terragent.Pathfinding;

using Terragent.Work.Jobs.Targets;

namespace Terragent.Work.Jobs;

/// <summary>One kind of work, and everywhere it could be done.</summary>
// The unit the agent actually does. Everything above is a reason to do something and
// everything below is a key press; this is where the two meet: go here, then do this
// until it is finished.
//
// A kind of work rather than one spot of it. Chopping wood is one job with a hundred
// trees in sight, not a hundred jobs, and building one object per visible tile every
// tick is how a frame gets dropped.
internal interface IJob
{
    /// <summary>What to call it on the panel.</summary>
    string Label { get; }

    /// <summary>Whether the work is finished.</summary>
    bool Done { get; }

    /// <summary>Whether the thing it was sent to work on is still worth working.</summary>
    // A felled tree leaves air where the trunk was, and without this the body stands in
    // the stump swinging at nothing until something else interrupts it.
    //
    // Asked only about the site in hand, never about what is nearest now. That is the
    // difference between noticing the work is gone and reconsidering every tick, and the
    // second one is what walks a body between two ore veins for ever.
    bool Workable(ITarget target);

    /// <summary>The closest work of this kind, or null when there is none to be had.</summary>
    // Straight line, not by route: pricing every candidate with a search is a search per
    // candidate, and the search itself already prefers the goal it can actually get to.
    //
    // Null means nothing to work from here, which is not the same as finished. The ore
    // ran out of sight, the item despawned, the trees are all behind a wall. Whatever
    // holds the job then picks another one, and the objective is still unmet.
    //
    // A destination rather than a tile: how close counts differs per job, since a tile is
    // reached at tool range and a dropped item only by being walked into.
    Offer? Nearest(Point from);

    /// <summary>One tick of working it, with the body already standing at the site.</summary>
    // Told which site rather than finding it again. Asked twice on two ticks, NearestSite
    // can answer with two different tiles, and the agent then works the one it did not
    // walk to. That is the two tile shuffle, and passing the site back is what makes it
    // impossible rather than unlikely.
    //
    // Getting there is not part of it. A job that walked as well as worked would decide
    // its own route, and then two places answer where the agent goes.
    void Work(ITarget target);
}
