using Terragent.Pathfinding;
using Terragent.Work.Jobs;
using Terragent.Work.Jobs.Targets;

namespace Terragent.Work;

/// <summary>A job the search reached, and everything needed to put the body on it.</summary>
// All four decided by one search from one footing on one tick. Handed over together so the
// choosing can be a question: whoever asks gets an answer and nothing has happened yet,
// which is what lets the assigning be one visible step instead of a side effect.
internal sealed record Pick(IJob Job, ITarget Target, Destination Destination, Route Route);
