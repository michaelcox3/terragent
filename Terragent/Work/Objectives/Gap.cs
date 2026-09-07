using System.Collections.Generic;
using Microsoft.Xna.Framework;

using Terragent.Work.Jobs;

namespace Terragent.Work.Objectives;

/// <summary>A node the agent cannot do anything about yet.</summary>
// Never met, so the run walks up to it and stops there. Visible, rather than skipping a
// fight it cannot have and reporting a Wall of Flesh it never saw.
//
// Every one of these is a hole in the agent. The three bosses are all of them today, and
// when killing something is an objective this class has no callers left and goes.
internal sealed class Gap(string label) : IObjective
{
    public string Label => label;

    public bool Met => false;

    public IReadOnlyDictionary<int, int> Missing() => new Dictionary<int, int>();

    public IReadOnlyList<IJob> Jobs() => [];
}
