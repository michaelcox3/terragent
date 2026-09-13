namespace Terragent.Work;

/// <summary>Seeing, which is upkeep rather than work.</summary>
// Not a job. A job is chosen against every other job and held until it is finished, and
// being able to see is neither: it is what the tick in the middle of a dig is spent on,
// and the dig carries on afterwards. Kept off IJob so that nothing tries to route to it,
// price it, or leave it in the pool.
internal interface ILamplighter
{
    /// <summary>Whether the body cannot see, and carries something that would fix it.</summary>
    // Asked before the hand is given to anything else. Raising used to go first and keep
    // nothing, since whatever worked next held its own tool and the light was put away in
    // the same frame, so the one situation it exists for, digging into the dark, was the
    // one situation it could not help with.
    bool Blind { get; }

    /// <summary>Put a lamp in hand, and spend the tick doing it.</summary>
    // It takes the tick, because holding a light and swinging a pickaxe are the same hand.
    // Cheap even so: the map keeps whatever it is shown, so this costs one tick for each
    // patch of dark opened rather than one a tick.
    void Raise();

    /// <summary>Whether putting one up is what this tick is for.</summary>
    bool Needed { get; }

    /// <summary>One tick of putting one up, which is a tick nothing else gets.</summary>
    void Tick();
}
