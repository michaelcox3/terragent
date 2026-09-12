using Microsoft.Xna.Framework;
using Terragent.Controls;
using Terragent.Pathfinding;
using Terragent.Report;
using Terragent.World;

using Terragent.Work.Jobs;
using Terragent.Work.Jobs.Targets;

using System.Collections.Generic;
using System.Linq;
using Terragent.Work.Objectives;

namespace Terragent.Work;

/// <summary>The foreman.</summary>
// Holds one job and one site and refuses to reconsider. Everything here is about not
// choosing again: choosing is cheap and correct, and doing it every tick is what walks a
// body back and forth between two equally good ore veins until the sun comes up.
internal sealed class Foreman(IBody body, IPilot pilot, IClock clock, IJournal journal)
    : IForeman
{
    private readonly IBody _body = body;
    private readonly IPilot _pilot = pilot;

    private IReadOnlyList<IObjective> _objectives = [];

    /// <summary>How long to wait before looking for work again after finding none.</summary>
    // Not a judgement about any job. A search that reaches nothing costs most of a frame,
    // and running it again on the next one is how the game ends up at six frames a second.
    // A quarter of a second later everything is offered again, including whatever was in
    // mid air the first time.
    private const double RestSeconds = 0.25;

    /// <summary>How far a site may move before the route to it is worth drawing again.</summary>
    // Two, because a slime shuffles a tile constantly and redrawing on every twitch is the
    // search running all day. Further than that and the route points at where it was.
    private const int DriftTiles = 2;

    /// <summary>When it last went looking for something to do.</summary>
    private double _looked;

    public IReadOnlyList<IObjective> Objectives
    {
        get => _objectives;
        // Set, and the job in hand is left alone. It used to be dropped, from when one
        // objective was handed down and a new one meant the old work was for something the
        // run had stopped caring about. A front is a set: it changes because one of several
        // came true, and the rest are still being worked. Dropping there threw away a craft
        // on the tick its own output completed an objective, which is the tick it was about
        // to take its work bench back.
        //
        // Nothing is lost by keeping it. A job whose reason has gone is done or unworkable
        // within a tick or two and lets go of itself.
        set => _objectives = value;
    }

    public IPilot Pilot => _pilot;

    public IReadOnlyList<IJob> Offered { get; private set; } = [];

    public IJob? Job { get; private set; }

    /// <summary>The thing it is working on, kept with the job that offered it.</summary>
    public ITarget? Target { get; private set; }

    public Destination? Destination { get; private set; }

    public void Tick()
    {
        if (_objectives.Count == 0)
        {
            Drop();
            return;
        }

        if (Spent())
        {
            Said();
            Drop();
        }

        if (Job is null && clock.Now - _looked >= RestSeconds)
        {
            Choose();
        }

        IJob? job = Job;
        if (job is null || Destination is not { } site)
        {
            return;
        }

        // Through Change, so it is one line per state and silent while nothing moves. A
        // run that wedges then says what it was holding when it stopped, which the log
        // could not do before.
        journal.Change("holding", $"{job.Label} at ({site.Site.X}, {site.Site.Y}), "
            + $"{_pilot.Progress.ToString().ToLowerInvariant()}, "
            + $"body at ({_body.Footing.X}, {_body.Footing.Y})");

        // Travel or work, never both. Arrival is the pilot's answer to a question the
        // foreman cannot answer itself, since being near the tile is not the same as being
        // able to reach it.
        if (_pilot.Progress is Progress.Arrived)
        {
            // Arriving is the whole of what a travel job does, and arriving is not
            // finishing: it is done when the ore comes into view or the slime does. So it
            // asked where to go next and keeps the job, rather than being dropped and
            // made to win the choosing again. Dropped, a hunt and an explore would take
            // turns beating each other and the body would walk between them for ever.
            if (Travelling(job))
            {
                Onward(job);
                return;
            }

            job.Work(Target!);
            return;
        }

        _pilot.Tick();
    }

    /// <summary>Whether the job in hand is over, one way or the other.</summary>
    // The whole list. Not a better job appearing, not a route getting long, not time
    // passing: every oscillation this project has had was a fourth entry here.
    private bool Spent() =>
        Job is null
        || Job.Done
        || _pilot.Progress is Progress.Unreachable
        || (Target is { } target && Destination is { } site
            && (!Job.Workable(target) || Wandered(target, site)));

    /// <summary>Whether the work has moved far enough that the route is pointing wrongly.</summary>
    // A target with nowhere to be cannot have moved. A recipe worked where the body
    // stands has no place, and neither does a creature that has died.
    private bool Wandered(ITarget target, Destination site) =>
        target.Tile is { } now && Pathfinding.Destination.Beyond(site.Site, now, DriftTiles);

    /// <summary>Take whichever job the search can reach most cheaply.</summary>
    // One search over every candidate rather than a straight line between them. The ore
    // behind a wall is nearer than the ore down an open shaft and further away in the only
    // sense that matters, and overruling that is what a search is for.
    //
    // A candidate no route reaches can still be chosen. The search reaches none of them,
    // hands back the way to the footing that got nearest, and names whichever destination
    // that footing is closest to, which is a straight line again.
    private void Choose()
    {
        _looked = clock.Now;
        Point from = _body.Footing;

        (List<IJob> working, List<IJob> looking) = Pool();
        Needing();

        // Filled by each round, so what was on offer is the union of the rounds that ran
        // and not of the ones that would have.
        List<IJob> offering = [];
        List<string> nowhere = [];

        // Looking is not even asked where it would go while work is reachable. Asking costs
        // a sweep of the frontier, sixteen thousand cells, thrown away whenever work wins.
        int pooled = working.Count;
        Pick? pick = Chosen(from, working, offering, nowhere);
        if (pick is null)
        {
            pooled += looking.Count;
            pick = Chosen(from, looking, offering, nowhere);
        }

        if (pick is not null)
        {
            Assign(pick);
            journal.Note("chose", $"{pick.Job.Label} at "
                + $"({pick.Destination.Site.X}, {pick.Destination.Site.Y}), "
                + $"from {offering.Count} on offer");
        }

        Offering(pooled, offering, nowhere);

        if (pick is null && pooled > 0)
        {
            // Said out loud, because this and an empty offer read the same in a log: both
            // are a run that stops choosing. One is a hole in the progression and the
            // other is a body in a pit, and they want opposite fixes.
            journal.Change("idle", $"nothing reachable from ({from.X}, {from.Y})");
        }
    }

    /// <summary>Every live objective's jobs, deduplicated, split into work and looking.</summary>
    // Which objective a job came from stops mattering the moment it is offered: a crystal
    // underfoot beats ore ten tiles away whatever either of them is for. One of each,
    // however many objectives asked, and what counts as the same is each job's own to say.
    private (List<IJob> Working, List<IJob> Looking) Pool()
    {
        List<IJob> working = [];
        List<IJob> looking = [];
        HashSet<IJob> already = [];
        foreach (IObjective objective in _objectives)
        {
            foreach (IJob job in objective.Jobs())
            {
                if (!already.Add(job))
                {
                    continue;
                }

                (Travelling(job) ? looking : working).Add(job);
            }
        }

        return (working, looking);
    }

    /// <summary>Put a pick in hand and send the body to it.</summary>
    // The one place these four are set together, so a reader of either caller can see the
    // body being committed rather than finding it inside a condition. Onward passes the job
    // it is already holding, which is why this sets the job rather than leaving it alone.
    private void Assign(Pick pick)
    {
        Job = pick.Job;
        Target = pick.Target;
        Destination = pick.Destination;
        _pilot.Follow(pick.Destination, pick.Route);
    }

    /// <summary>Send a travel job on to wherever it wants next, keeping it in hand.</summary>
    // No choosing, so nothing competes for the body while it is on its way. The job is let
    // go only when it is done or when it runs out of anywhere to go.
    private void Onward(IJob job)
    {
        Point from = _body.Footing;
        Offer? offer = job.Nearest(from);
        if (offer is null)
        {
            journal.Note("dropped", $"{job.Label}: nowhere further to look from "
                + $"({from.X}, {from.Y})");
            Drop();
            return;
        }

        RouteMatch? reached = _pilot.FindRoute([offer.Destination]);
        if (reached is null)
        {
            journal.Note("dropped", $"{job.Label}: no way on from ({from.X}, {from.Y})");
            Drop();
            return;
        }

        Assign(new Pick(job, offer.Target, offer.Destination, reached.Route));

        journal.Note("onward", $"{job.Label} to "
            + $"({offer.Destination.Site.X}, {offer.Destination.Site.Y})");
    }

    /// <summary>What the run is still short of, alternatives and all.</summary>
    // The same list the panel draws, written down. A reader can see a line on the panel
    // and not tell whether an alternative was never worked out or was worked out and
    // dropped on the way to being drawn, and those want opposite fixes.
    private void Needing()
    {
        List<string> short_ = [];
        foreach (IObjective objective in _objectives)
        {
            foreach (NeededItem want in objective.NeededItems())
            {
                List<string> options = [];
                foreach ((int itemID, int owed) in want.Options)
                {
                    options.Add($"{owed}x {Names.Item(itemID)}");
                }

                short_.Add(string.Join(" or ", options));
            }
        }

        journal.Change("needing", short_.Count == 0
            ? "nothing"
            : string.Join("; ", short_));
    }

    /// <summary>Whether a job's whole work is getting somewhere rather than doing something.</summary>
    // The two that go looking. Named here rather than flagged on every job, because this
    // is the only place the difference means anything and six jobs would otherwise carry a
    // field to say they are ordinary.
    private static bool Travelling(IJob job) => job is Explore or Hunt;

    /// <summary>
    /// Whichever of these the search reaches most cheaply, or null when it reaches none.
    /// </summary>
    // A question, so a caller can ask without the body being committed by the asking. What
    // it writes down is which jobs had somewhere to work and which had nowhere, since only
    // this end knows and the caller wants to say so either way.
    private Pick? Chosen(Point from, IReadOnlyList<IJob> jobs,
        List<IJob> offering, List<string> nowhere)
    {
        List<(IJob Job, Offer Offer)> candidates = [];
        List<Destination> destinations = [];
        foreach (IJob job in jobs)
        {
            // Finished work is not work. A hunt whose creature is already in view is done
            // before it starts, and choosing it means dropping it on the same tick and
            // paying for a search to do so. Asked before Nearest, which sweeps the ground.
            if (job.Done)
            {
                continue;
            }

            Offer? offer = job.Nearest(from);
            if (offer is null)
            {
                nowhere.Add(job.Label);
                continue;
            }

            offering.Add(job);
            candidates.Add((job, offer));
            destinations.Add(offer.Destination);
        }

        // Nothing these offered can be walked to this instant. Nothing is set aside for
        // it: a slime in mid jump is unreachable for one frame and every bit as worth
        // fighting when it lands.
        RouteMatch? reached = _pilot.FindRoute(destinations);
        if (reached is null)
        {
            // Said out loud. A multi-goal search that settles on nothing is silent
            // otherwise, since only the follower's own search writes a route line, and a
            // job that offered a site it cannot reach reads exactly like one that offered
            // no site at all.
            journal.Change("unreached", $"no way from ({from.X}, {from.Y}) to any of "
                + $"{destinations.Count}: "
                + string.Join(" ", destinations
                    .Select(site => $"({site.Site.X}, {site.Site.Y})")));
            return null;
        }

        // By its place in the list, which is how the search names what it settled on.
        // Matching the tile back instead cannot tell two jobs offering the same one apart.
        (IJob taken, Offer won) = candidates[reached.Index];
        return new Pick(taken, won.Target, won.Destination, reached.Route);
    }

    /// <summary>What was on offer and what of it had nowhere to work.</summary>
    // Through Change, so it is one line per distinct set. The count alone said seven jobs
    // were offered and two had sites, and left no way to tell which two.
    private void Offering(int pooled, List<IJob> offering, List<string> nowhere)
    {
        Offered = new List<IJob>(offering);

        journal.Change("offering", offering.Count == 0
            ? $"nothing with anywhere to work, of {pooled} jobs"
            : string.Join("; ", offering.Select(job => job.Label))
                + (nowhere.Count > 0
                    ? $"; nowhere to work for {string.Join("; ", nowhere)}"
                    : string.Empty));
    }

    /// <summary>Why the job in hand is being let go, while it can still be asked.</summary>
    private void Said()
    {
        IJob? job = Job;
        if (job is null)
        {
            return;
        }

        journal.Note("dropped", $"{job.Label}: " + (job.Done ? "done"
            : _pilot.Progress is Progress.Unreachable ? "no route from here"
            : Target is { } moved && Destination is { } where && Wandered(moved, where)
                ? "it moved, going again"
            : "the site stopped being work"));
    }

    private void Drop()
    {
        Job = null;
        Target = null;
        Destination = null;
        _pilot.Halt();
    }
}
