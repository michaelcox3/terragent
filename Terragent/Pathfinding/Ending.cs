namespace Terragent.Pathfinding;

/// <summary>Why a search stopped looking.</summary>
// Four events that all hand back a route and are not the same news at all. Arrived is an
// answer; exhausted is proof that there is no way from here with what the body is carrying;
// stalled is the search giving up because nothing has got nearer the goal in a long while;
// spent is being cut off mid thought, and the only one of the four where a bigger number
// would have changed the answer.
internal enum Ending
{
    Arrived,
    Exhausted,
    Stalled,
    Spent,
}
