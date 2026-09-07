using System.Collections.Generic;
using Terraria.ModLoader;

namespace Terragent.Report;

/// <summary>The journal, written into tModLoader's own log.</summary>
// The game's log rather than a file of ours, because that is where a crash lands and
// reading two files interleaved by hand is how a cause gets missed.
internal sealed class Journal(Mod mod) : IJournal
{
    private readonly Mod _mod = mod;

    /// <summary>The last line written under each heading.</summary>
    private readonly Dictionary<string, string> _said = [];

    public void Note(string what, string detail) => _mod.Logger.Info($"[{what}] {detail}");

    public void Change(string what, string detail)
    {
        if (_said.TryGetValue(what, out string? said) && said == detail)
        {
            return;
        }

        _said[what] = detail;
        Note(what, detail);
    }
}
