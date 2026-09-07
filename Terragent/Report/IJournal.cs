namespace Terragent.Report;

/// <summary>Somewhere to write down what the agent decided, and why.</summary>
// Handed to whatever does the deciding rather than reached for. A static logger is a
// hidden input on every method that touches it, and it makes a headless test of anything
// that logs impossible to write without a file on disk.
//
// Two ways to write, because most of this happens sixty times a second. Note is for things
// that happen once, and Change is for the rest.
internal interface IJournal
{
    /// <summary>Write a line, every time.</summary>
    void Note(string what, string detail);

    /// <summary>Write a line only when it differs from the last one under this heading.</summary>
    // What a run needs read back is when something changed, not that it was still true on
    // the next frame. Without this, one tick of standing still is one line and a minute of
    // it is three and a half thousand.
    void Change(string what, string detail);
}
