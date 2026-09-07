using System.Collections.Generic;

using Terragent.Work.Objectives;

namespace Terragent.Work;

/// <summary>One objective in the graph, with the nodes it waits on.</summary>
// The nodes and their edges are the graph; there is no collection class above them. The
// only question anyone asks of the run is whether everything a node waits on has been
// reached, and that is a walk of one node's own list.
//
// The edges point back at what came before rather than forward at what follows, because
// that is the direction the question is asked in. Successors would have to be searched
// every time to answer it.
//
// Nodes rather than keys. A requirement naming a key that nothing defines was silently a
// node that could never become ready, and no log said so; holding the node itself makes
// that impossible to write down rather than something to check for.
//
// Edges only. What the node is called and what it takes to be true both belong to the
// objective, which is where a reader looks for them; all this adds is where it sits.
internal sealed class DagNode(IObjective objective)
{
    private readonly List<DagNode> _dependsOn = [];

    /// <summary>What it takes to be true, and the work that would do it.</summary>
    public IObjective Objective => objective;

    /// <summary>What has to be reached before this can be worked on.</summary>
    public IReadOnlyList<DagNode> DependsOn => _dependsOn;

    /// <summary>Wire an edge, which only the loader does and only once.</summary>
    // A list that is filled and then never touched again. Nodes can name each other in
    // any order in the file, so they are all made before any of them is wired, and there
    // is no way to hold a finished node while its edges are still arriving.
    internal void Needs(DagNode other) => _dependsOn.Add(other);

    public override string ToString() => objective.Key;
}
