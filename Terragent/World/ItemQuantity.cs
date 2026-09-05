namespace Terragent.World;

/// <summary>Some of an item: what, and how many.</summary>
// One shape for the pair, rather than the tuples (Item, Needed) and (Item, Count) that
// named the same thing twice. A tuple says nothing about itself at a call site.
internal readonly record struct ItemQuantity(int ItemID, int Count);
