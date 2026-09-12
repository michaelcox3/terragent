using Terragent.Pathfinding;
using Terragent.Work.Jobs.Targets;

namespace Terragent.Work.Jobs;

/// <summary>What a job offers: something to work on, and somewhere to stand to do it.</summary>
/// <param name="Target">The thing the work is about.</param>
/// <param name="Destination">Where the body goes, and what counts as being there.</param>
// The two together because they are decided together, from one position on one tick. Asked
// for separately, the second answer can be about a different thing from the first, and the
// body then works something it never walked to.
//
// Where the work is, and never how to get there. How far a search may attempt at a time is
// the pilot's, and a job that named its own legs was a job with an opinion about the
// journey: three of them had one and the fourth had forgotten to.
internal sealed record Offer(ITarget Target, Destination Destination);
