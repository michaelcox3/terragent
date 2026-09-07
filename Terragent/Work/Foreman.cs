using Microsoft.Xna.Framework;
using Terragent.Controls;
using Terragent.Pathfinding;
using Terragent.Report;
using Terragent.World;

using Terragent.Work.Jobs;
using Terragent.Work.Jobs.Targets;

using System.Collections.Generic;
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
        set
        {
            // By instance, which works because the progression keeps one list and hands
            // back the same one until the graph moves on. A fresh list every tick would
            // drop the job every tick.
            if (ReferenceEquals(value, _objectives))
            {
                return;
            }

            _objectives = value;
            Drop();
        }
    }

    public IPilot Pilot => _pilot;

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

        // If the current job is spent, announce it and drop it.
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

    /// <summary>Take whichever job the search can actually reach soonest.</summary>
    // One search over every candidate rather than a straight line between them. The ore
    // behind a wall is nearer than the ore down an open shaft and further away in the only
    // sense that matters, and overruling that is what a search is for.
    //
    // It also means an unreachable candidate is never chosen. The search simply does not
    // settle on it, so there is nothing to fail at afterwards.
    private void Choose()
    {
        _looked = clock.Now;
        Point from = _body.Footing;

        // One pool out of all of them. Which objective a job came from stops mattering
        // the moment it is offered: a crystal underfoot beats ore ten tiles away whatever
        // either of them is for.
        List<IJob> offered = [];
        foreach (IObjective objective in _objectives)
        {
            offered.AddRange(objective.Jobs());
        }

        List<(IJob Job, Offer Offer)> candidates = [];
        List<Destination> destinations = [];
        foreach (IJob job in offered)
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
                continue;
            }

            candidates.Add((job, offer));
            destinations.Add(offer.Destination);
        }

        // Nothing any of them offered can be walked to this instant. Nothing is set aside
        // for it: a slime in mid jump is unreachable for one frame and every bit as worth
        // fighting when it lands.
        RouteMatch? reached = _pilot.FindRoute(destinations);
        if (reached is null)
        {
            // Said out loud, because this and an empty offer read the same in a log: both
            // are a run that stops choosing. One is a hole in the progression and the other
            // is a body in a pit, and they want opposite fixes.
            journal.Change("idle", $"nothing reachable from "
                + $"({from.X}, {from.Y}): {candidates.Count} sites offered of "
                + $"{offered.Count} jobs");
            return;
        }

        // By its place in the list, which is what the search reached. Matching the tile
        // back instead cannot tell two jobs offering the same one apart.
        (IJob taken, Offer won) = candidates[reached.Index];
        Job = taken;
        Target = won.Target;
        Destination = won.Destination;
        _pilot.Follow(won.Destination, reached.Route);

        journal.Note("chose",
            $"{taken.Label} at ({won.Destination.Site.X}, {won.Destination.Site.Y}), "
            + $"from {offered.Count} on offer");
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
