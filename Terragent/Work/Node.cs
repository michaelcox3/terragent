using System.Collections.Generic;

using Terragent.Work.Objectives;

namespace Terragent.Work;

/// <summary>One objective in the graph, with the name it is saved under.</summary>
/// <param name="Key">What goes in the player file. Stable across versions, unlike a position.</param>
/// <param name="Objective">What it takes to be true, and the work that would do it.</param>
/// <param name="Requires">Keys that have to be reached before this can be worked on.</param>
/// <param name="Standing">
/// Whether it is a supply, which is never recorded and always checked first.
/// </param>
// A supply is a node like any other, requiring whatever made it affordable, rather than a
// list hanging off one. That is what gives it a key to be named by, a label to be shown
// and requirements to wait on, and it means the file has one shape rather than two.
//
// Standing is the whole difference: a supply goes short again and so is never recorded,
// and it is checked before the graph because the objective is what spends the torches.
//
// The key and the edges live out here rather than on the objective. An objective is a
// statement about the world and knows nothing about the run it belongs to; the same one
// could sit in two graphs under two names.
internal sealed record Node(string Key, IObjective Objective, IReadOnlyList<string> Requires,
    bool Standing);
