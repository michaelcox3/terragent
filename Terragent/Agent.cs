using System.Collections.Generic;
using Terragent.Report;
using Terragent.Work;
using Terragent.Work.Objectives;
using Terragent.World;

namespace Terragent;

/// <summary>The agent.</summary>
// Three lines of tick and nothing else, which is the point: what the run does next is
// decided by the progression, and how it gets done is the foreman's. This holds the two
// of them in the order they run and the switch that says whether to run them at all.
internal sealed class Agent(IProgression progression, IForeman foreman, ITerrain terrain,
    IJournal journal) : IAgent
{
    private readonly IProgression _progression = progression;
    private readonly IForeman _foreman = foreman;
    private readonly ITerrain _terrain = terrain;

    public bool Driving { get; set; }

    public IForeman Foreman => _foreman;

    public IProgression Progression => _progression;

    public ITerrain Terrain => _terrain;

    public IReadOnlyCollection<string> Reached => _progression.Reached;

    public void Tick()
    {
        if (!Driving)
        {
            return;
        }

        // Record first. An objective can come true from a drop or from work done for
        // something else, and asking what to do next before looking would spend the tick
        // on something already finished.
        _progression.Record();

        // Handed down every tick rather than when it changes. The foreman ignores the
        // objective it already has, so the only tick this does anything on is the one
        // after something completes.
        _foreman.Objectives = _progression.Active();
        journal.Change("objectives", Said(_foreman.Objectives, _progression.Reached.Count));

        _foreman.Tick();
    }

    public void Restore(IEnumerable<string> reached) => _progression.Restore(reached);

    /// <summary>Every live objective on one line, for the log.</summary>
    // All of them by name rather than the first and a count. Written through Change, so a
    // run that has been on the same three for ten minutes says so once.
    private static string Said(IReadOnlyList<IObjective> working, int reached)
    {
        if (working.Count == 0)
        {
            return "nothing left it can do";
        }

        List<string> names = [];
        foreach (IObjective objective in working)
        {
            names.Add(objective.Label);
        }

        return $"{string.Join(", ", names)}; {reached} reached";
    }
}
