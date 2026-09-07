using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terragent.Controls;
using Terragent.Work.Jobs;

namespace Terragent.Work.Objectives;

/// <summary>Something the run keeps topped up rather than finishes.</summary>
// Torches, glowsticks, arrows. An objective is reached once and stays reached; a supply
// goes short again every time it is spent, which is why the progression records the one
// and never the other.
//
// Two numbers, not one. Restocking the moment the count dips is the thrash the old mod
// had: gather a torch, place it, gather a torch. Going short at the low number and filling
// to the high one is the reorder point every stock system uses, and it is the difference
// between one trip and forty.
internal sealed class Supply : IObjective
{
    private readonly string _key;
    private readonly string _label;
    private readonly IInventory _bag;

    /// <summary>What it does when it is short, which is the same as wanting the thing.</summary>
    // A supply is not a different kind of work, it is a different reason for it. Restocking
    // torches is getting torches, tree and all, so the breaking down belongs to the same
    // thing that breaks down every other item and is only borrowed here.
    private readonly IObjective _filling;
    private readonly int _itemID;
    private readonly int _restockAt;
    private readonly int _ceiling;

    /// <summary>Whether a restock is under way, which is the whole of its memory.</summary>
    private bool _short;

    /// <param name="filling">Getting a ceiling's worth of it, which is ordinary work.</param>
    /// <param name="restockAt">Falling below this starts a restock.</param>
    /// <param name="ceiling">A restock runs until this many are carried.</param>
    /// <param name="reserve">How many are held back from being spent on anything else.</param>
    // The reserve is declared here and once. It is a standing floor rather than a claim on
    // a tick, so the bag is told at the moment the supply comes into being and never again.
    public Supply(string key, string label, IInventory bag, IObjective filling, int itemID,
        int restockAt, int ceiling, int reserve)
    {
        _filling = filling;
        _key = key;
        _label = label;
        _bag = bag;
        _itemID = itemID;
        _restockAt = restockAt;
        _ceiling = ceiling;
        bag.Reserve(itemID, reserve);
    }

    /// <summary>What is being kept in stock.</summary>
    // Named so the progression can tell two supplies for the same thing apart and keep
    // only the later one, which is how torches climb from five to sixty over a run.
    public int ItemID => _itemID;

    public string Key => _key;

    public string Label => _label;

    public bool Met => !_short;

    /// <summary>Look at the bag and decide whether a restock is on.</summary>
    // A command, called by the progression before it asks what to do next. Deciding this
    // inside Met would make a question that changes the answer to the next question.
    public void Review() =>
        _short = _bag.Carrying(_itemID) < (_short ? _ceiling : _restockAt);

    /// <summary>What restocking still needs, once it has started.</summary>
    public IReadOnlyDictionary<int, int> Missing() =>
        _short ? _filling.Missing() : new Dictionary<int, int>();

    /// <summary>The work of restocking, and nothing at all while it is stocked.</summary>
    public IReadOnlyList<IJob> Jobs() => _short ? _filling.Jobs() : [];
}
