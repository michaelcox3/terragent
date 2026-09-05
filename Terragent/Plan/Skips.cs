using System.Collections.Generic;

namespace Terragent.Plan;

/// <summary>
/// Goals the executor could not act on, and when to let it try again.
/// </summary>
internal sealed class Skips
{
    /// <summary>Seconds before a set-aside goal is tried again.</summary>
    // Flat, not doubling. Backing off makes sense when retrying is expensive or the
    // thing waited on recovers by itself, and neither is true here: the world changes
    // while the agent does something else, so the next attempt is a different attempt.
    private const double Wait = 3.0;

    private readonly Dictionary<string, (int Count, double Until, string Why)> _skipped = [];

    public void Skip(string key, double now, string why)
    {
        int count = _skipped.TryGetValue(key, out (int Count, double Until, string Why) prior)
            ? prior.Count + 1
            : 1;
        _skipped[key] = (count, now + Wait, why);
    }

    /// <summary>
    /// Why a goal was set aside, and for how much longer.
    /// </summary>
    public string Reason(string key, double now) =>
        _skipped.TryGetValue(key, out (int Count, double Until, string Why) entry)
            ? $"{key}: {entry.Why} (retry in {System.Math.Max(0, entry.Until - now):0}s)"
            : key;

    /// <summary>Keys still cooling off, for the planner to pass over.</summary>
    public HashSet<string> Current(double now)
    {
        HashSet<string> active = [];
        foreach ((string key, (int _, double until, string _)) in _skipped)
        {
            if (now < until)
            {
                active.Add(key);
            }
        }

        return active;
    }

    /// <summary>Forget a goal entirely. Called when it becomes satisfied.</summary>
    public void Clear(string key) => _skipped.Remove(key);

    public int Tries(string key) =>
        _skipped.TryGetValue(key, out (int Count, double Until, string Why) entry)
            ? entry.Count : 0;
}
