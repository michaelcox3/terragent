namespace Terragent.Pathfinding;

/// <summary>What the body can do, as far as the search is concerned.</summary>
/// <param name="Costs">What each kind of move is worth to it, in ticks.</param>
/// <param name="PickPower">The strongest pickaxe carried, or zero to forbid digging.</param>
/// <param name="Leap">How high and how far one jump reaches.</param>
/// <param name="Blocks">Blocks that may be spent, or zero to forbid pillaring.</param>
// One argument because it is one question, what this body can do right now, and the four
// answers are read off the body and the bag together at one moment. Apart, a search could
// be priced for a pickaxe that had already been swapped out.
internal readonly record struct Ability(Costs Costs, int PickPower, Leap Leap, int Blocks);
