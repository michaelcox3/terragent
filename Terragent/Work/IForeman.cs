using Terragent.Controls;
using Terragent.Pathfinding;

using Terragent.Work.Jobs;

using System.Collections.Generic;
using Terragent.Work.Objectives;

namespace Terragent.Work;

/// <summary>Picks one job, sends the body to it, and sees it through.</summary>
// The only thing that touches both an objective and the pilot. An objective knows what
// would help and a job knows how to do it; neither knows where the body is going, and the
// pilot does not know why. This is the seam, and it is the whole of the agent's loop.
//
// What it exists for is holding on. Nearest is a fresh answer every tick, and an agent
// that asks it every tick walks between two ore veins for ever. So the pair chosen here
// is kept, and only three things let go of it:
//
//   the job says it is done,
//   its site has moved further than the arrival radius allows,
//   the pilot says there is no route from here.
//
// Nothing else. Not a better job appearing, not a route getting long, not time passing.
// Every oscillation this project has had was a fourth reason to let go.
internal interface IForeman
{
    /// <summary>What the run is working towards, empty to stand idle.</summary>
    // Several, because the progression is a graph and everything whose requirements are
    // behind it is workable at once. Their jobs go into one pool and the search takes
    // whichever it reaches most cheaply, so nothing here has to decide between them.
    //
    // Setting it leaves the job in hand alone. A front is a set, so it changes because one
    // of several came true and the rest are still being worked, and a job whose reason has
    // gone lets go of itself within a tick or two anyway.
    IReadOnlyList<IObjective> Objectives { get; set; }

    /// <summary>Everything it last had to choose from, one job per kind of work.</summary>
    // Pooled and deduplicated here and once. A reader wants the same list the choice was
    // made out of, and building a second one costs a recipe walk per objective per frame
    // and can disagree with this one about what is on offer.
    IReadOnlyList<IJob> Offered { get; }

    /// <summary>The job being seen through, or null when none is chosen.</summary>
    IJob? Job { get; }

    /// <summary>The site it was chosen at, which is where the pilot was sent.</summary>
    // Held beside the job rather than asked for again. These two were picked together on
    // one tick from one position, and the body works the tile it walked to because this
    // is the copy that gets used.
    Destination? Destination { get; }

    /// <summary>The pilot it is sending, so the panel can say how the travelling goes.</summary>
    IPilot Pilot { get; }

    /// <summary>One tick: choose if there is nothing in hand, then travel or work.</summary>
    // Travel or work, never both. Arrival is the pilot's answer, and a job that swung
    // while still walking would swing at whatever it passed.
    void Tick();
}
