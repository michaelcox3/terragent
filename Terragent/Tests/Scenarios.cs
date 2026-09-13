using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;


namespace Terragent.Tests;

/// <summary>The kinds of move a route can be made of.</summary>
[System.Flags]
internal enum Move
{
    None = 0,

    /// <summary>Walking, stepping up, and falling off things. Costs nothing.</summary>
    Walk = 1,

    /// <summary>Leaving the ground on purpose to reach a ledge.</summary>
    Jump = 2,

    /// <summary>Breaking a block to get through it.</summary>
    Mine = 4,

    /// <summary>Putting a block down to stand on it. Pillaring.</summary>
    Build = 8,
}

/// <summary>
/// One pathfinding problem: a picture of some ground, and what the right answer does.
/// </summary>
/// <param name="Walk">Whether walking, stepping up and falling are allowed.</param>
/// <param name="Jump">Whether leaving the ground is allowed.</param>
/// <param name="Mine">Whether breaking blocks is part of the right answer.</param>
/// <param name="Build">Whether placing blocks is part of the right answer.</param>
internal sealed record Case(string Name, bool Walk, bool Jump, bool Mine, bool Build,
    string Why, string[] Rows)
{
    /// <summary>The four answers as a set, for comparing against a route.</summary>
    public Move Does =>
        (Walk ? Move.Walk : Move.None)
        | (Jump ? Move.Jump : Move.None)
        | (Mine ? Move.Mine : Move.None)
        | (Build ? Move.Build : Move.None);

    /// <summary>True when no route is the correct answer.</summary>
    public bool Unreachable { get; init; }

    /// <summary>
    /// Seconds the arena gives this scenario before calling it failed.
    /// </summary>
    public int Seconds { get; init; } = 10;

    /// <summary>Whether the arena walls the grid in. On unless a scenario says not.</summary>
    // The surround is Ebonstone the agent cannot mine, and it brings a floor with it,
    // which is a thing to land on and pillar off. A scenario about crossing a gap does
    // not want one underneath it. Only the arena builds this; the headless harness has
    // nothing outside a grid either way.
    public bool Border { get; init; } = true;

    /// <summary>Steps the route may not exceed, when it matters.</summary>
    public int MaxSteps { get; init; } = int.MaxValue;

    /// <summary>Which marker is the goal. '@' asks for a route to where we already are.</summary>
    public char Goal { get; init; } = 'G';

    /// <summary>
    /// Blocks the character carries. Zero forbids pillaring.
    /// </summary>
    public int Blocks { get; init; } = 200;
}

/// <summary>
/// Small pathfinding problems with known answers, as written in scenarios.json.
/// </summary>
// One list read by both levels. The arena stamps these as real tiles and the headless
// harness parses the same pictures, and a case that lived in two places could pass at one
// level and not exist at the other.
internal static class Scenarios
{
    /// <summary>Where the cases are packed, inside the mod and beside the harness.</summary>
    public const string File = "Tests/scenarios.json";

    private static IReadOnlyList<Case>? _read;

    /// <summary>Every case. Available once <see cref="Load"/> has been handed the text.</summary>
    // A load once table rather than state: what is in the file cannot change while the
    // game is running. Loud when it is missing, because a silent empty list turns every
    // scenario run into a run that passed without doing anything.
    public static IReadOnlyList<Case> All =>
        _read ?? throw new InvalidOperationException(
            $"the scenarios have not been loaded: hand Load the text of {File}");

    public static void Load(string json) => _read = Read(json);

    /// <summary>The cases a file of them describes.</summary>
    // Text in and cases out, so whoever has the bytes may get them from the mod package or
    // from a file on disk without this having an opinion about which.
    public static IReadOnlyList<Case> Read(string json)
    {
        List<Case> cases = [];
        foreach (JsonElement entry in JsonDocument.Parse(json).RootElement.EnumerateArray())
        {
            string[] rows = [.. entry.GetProperty("rows").EnumerateArray()
                .Select(row => row.GetString() ?? string.Empty)];

            cases.Add(new Case(
                entry.GetProperty("name").GetString() ?? string.Empty,
                entry.GetProperty("walk").GetBoolean(),
                entry.GetProperty("jump").GetBoolean(),
                entry.GetProperty("mine").GetBoolean(),
                entry.GetProperty("build").GetBoolean(),
                entry.GetProperty("why").GetString() ?? string.Empty,
                rows)
            {
                Unreachable = Flag(entry, "unreachable", false),
                Border = Flag(entry, "border", true),
                Seconds = Number(entry, "seconds", 10),
                MaxSteps = Number(entry, "maxSteps", int.MaxValue),
                Blocks = Number(entry, "blocks", 200),
                Goal = Letter(entry, "goal", 'G'),
            });
        }

        return cases;
    }

    private static bool Flag(JsonElement entry, string name, bool missing) =>
        entry.TryGetProperty(name, out JsonElement found) ? found.GetBoolean() : missing;

    private static int Number(JsonElement entry, string name, int missing) =>
        entry.TryGetProperty(name, out JsonElement found) ? found.GetInt32() : missing;

    private static char Letter(JsonElement entry, string name, char missing) =>
        entry.TryGetProperty(name, out JsonElement found)
            ? (found.GetString() ?? string.Empty).FirstOrDefault(missing)
            : missing;
}
