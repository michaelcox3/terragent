using System.Collections.Generic;
using Terragent.Report;
using Terragent.Work;
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
        _foreman.Objective = _progression.Next();
        journal.Change("objective", _foreman.Objective is { } objective
            ? $"{objective.Label}, {_progression.Reached.Count} reached"
            : "nothing left it can do");

        _foreman.Tick();
    }

    public void Restore(IEnumerable<string> reached) => _progression.Restore(reached);
}
