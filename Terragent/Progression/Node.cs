using System.Collections.Generic;
using Terraria;

namespace Terragent.Progression;

/// <summary>What kind of goal a node represents.</summary>
internal enum NodeKind
{
    /// <summary>Possess a quantity of something.</summary>
    Item,

    /// <summary>A boss flag is set. Monotone: once true, true forever.</summary>
    Boss,

    /// <summary>
    /// A state of the world or character that is neither (maximum health, a placed
    /// station, a biome reached), which gates progression as hard as a boss does.
    /// </summary>
    // First class, or the graph quietly acquires edges nobody can explain.
    Milestone,
}

/// <summary>When a goal can be worked on at all.</summary>
internal enum TimeOfDay
{
    Any,
    Day,
    Night,
}

/// <summary>One goal in the progression graph.</summary>
internal sealed record Node
{
    public required string Key { get; init; }
    public required NodeKind Kind { get; init; }
    public required string Label { get; init; }

    /// <summary>Keys of nodes that must be satisfied first.</summary>
    public IReadOnlyList<string> Requires { get; init; } = [];

    /// <summary>
    /// For <see cref="NodeKind.Item"/>: the item id, or a recipe group id when the
    /// world decides which of two materials it has.
    /// </summary>
    public int ItemID { get; init; }

    public int Count { get; init; } = 1;

    /// <summary>
    /// Items any one of which satisfies this node, when the world decides which it has.
    /// </summary>
    // A world generates silver or tungsten, gold or platinum, never both, and only iron
    // and lead have a recipe group. Naming one of a pair stalls half of all worlds.
    public int[] Any { get; init; } = [];

    /// <summary>Whether this node takes whichever of several items the world has.</summary>
    public bool Interchangeable => Any.Length > 0;

    /// <summary>Upkeep entries that come into force once this node is done.</summary>
    // The when lives here and the how many in upkeep.json. Named rather than an item
    // id, so several entries can describe the same supply at different points of a run.
    public string[] Keeps { get; init; } = [];

    /// <summary>Baseline cost when it is not derived from an encounter.</summary>
    public float Effort { get; init; } = 1f;

    /// <summary>When this can be attempted.</summary>
    // Outside its window a node is deferred, not blocked: it keeps gating its
    // dependants while the agent does something else. Blocking cascades into
    // "nothing actionable" and ends the run.
    public TimeOfDay When { get; init; } = TimeOfDay.Any;

    /// <summary>Key of the encounter this is obtained from, for random drops.</summary>
    // How often it drops is the game's to say, so nothing here restates it.
    public int CreatureID { get; init; }

    public string Note { get; init; } = "";

    public bool IsStochastic => Kind == NodeKind.Item && CreatureID != 0;

    public bool AvailableNow(bool isDaytime) => When switch
    {
        TimeOfDay.Any => true,
        TimeOfDay.Day => isDaytime,
        _ => !isDaytime,
    };
}
