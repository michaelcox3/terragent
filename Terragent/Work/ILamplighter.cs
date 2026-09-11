namespace Terragent.Work;

/// <summary>Seeing, which is upkeep rather than work.</summary>
// Not a job. A job is chosen against every other job and held until it is finished, and
// being able to see is neither: it is what the tick in the middle of a dig is spent on,
// and the dig carries on afterwards. Kept off IJob so that nothing tries to route to it,
// price it, or leave it in the pool.
internal interface ILamplighter
{
    /// <summary>Put a lamp in hand while it is dark, whatever else is going on.</summary>
    // Never takes the tick, which is what makes it safe to do first: whatever works next
    // holds its own tool and that is what the frame ends with. A tick nothing else wants
    // the hand for is a tick spent carrying a light, which is free.
    void Raise();

    /// <summary>Whether putting one up is what this tick is for.</summary>
    bool Needed { get; }

    /// <summary>One tick of putting one up, which is a tick nothing else gets.</summary>
    void Tick();
}
