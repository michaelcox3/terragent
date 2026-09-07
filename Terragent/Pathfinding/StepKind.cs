namespace Terragent.Pathfinding;

/// <summary>
/// How the character gets from one footing to the next.
/// </summary>
internal enum StepKind
{
    Walk,
    Jump,
    Fall,

    /// <summary>
    /// Jump and put a block under your own feet, to stand one row higher.
    /// </summary>
    Place,
}
