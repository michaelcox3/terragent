using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Terragent.Report;

/// <summary>
/// Notices a decision flipping between two answers, and writes it down once.
/// </summary>
// Diagnosis only, on purpose: an agent that notices itself oscillating and does
// something else has papered over the decision that was wrong to begin with. Every
// oscillation here has been two answers to one question, re-derived from the body's
// position while the route moved the body across a boundary. This names both answers
// so the question can be found.
internal sealed class Oscillation(string what)
{
    /// <summary>Changes of answer, in a row, that make an oscillation: A B A B.</summary>
    private const int Flips = 4;

    /// <summary>Seconds the changes have to fall within.</summary>
    private const double Window = 10.0;

    /// <summary>Seconds between reports, so one episode is one line.</summary>
    private const double Quiet = 15.0;

    private readonly List<(string Value, double When, string Doing)> _changes = [];

    private string? _last;

    private double _reported = double.NegativeInfinity;

    /// <summary>Note this tick's answer; only a change from the last one counts.</summary>
    public void Observe(string value, string doing, Belief belief, Point at, double now)
    {
        if (value == _last)
        {
            return;
        }

        _last = value;

        // Two answers in one tick are two layers asking, not one layer changing its
        // mind: a fight asks for the target and then for the ground under it. The
        // later one stands for the tick.
        if (_changes.Count > 0 && _changes[^1].When == now)
        {
            _changes[^1] = (value, now, doing);
        }
        else
        {
            _changes.Add((value, now, doing));
        }

        while (_changes.Count > 0 && now - _changes[0].When > Window)
        {
            _changes.RemoveAt(0);
        }

        if (_changes.Count < Flips || now - _reported < Quiet)
        {
            return;
        }

        int first = _changes.Count - Flips;
        string a = _changes[first].Value;
        string b = _changes[first + 1].Value;
        if (a == b)
        {
            return;
        }

        for (int n = 0; n < Flips; n++)
        {
            if (_changes[first + n].Value != (n % 2 == 0 ? a : b))
            {
                return;
            }
        }

        _reported = now;
        Log.Sample("oscillation", $"{what} flips between {a} and {b}",
            ("changes", _changes.Count),
            ("seconds", $"{now - _changes[0].When:0.0}"),
            ("doingA", _changes[first].Doing),
            ("doingB", _changes[first + 1].Doing),
            ("at", $"({at.X},{at.Y})"),
            ("grid", Diagnose.Draw(belief, at, null)));
        _changes.Clear();
    }
}
