using Terraria;

namespace Terragent.Act;

/// <summary>
/// The three units that act on the player, held together so a behaviour takes one thing.
/// </summary>
// Movement is the body and its keys, Hand is what is held and the cursor, Inventory is
// the slots. Nothing is done here except putting every key down at once.
internal sealed class Character
{
    /// <summary>The fifty slots: what is carried, what may be spent, and moving items.</summary>
    internal Inventory Inventory { get; }

    /// <summary>What is in the hand and where it points: the held item, its reach, the cursor.</summary>
    internal Hand Hand { get; }

    /// <summary>The body: where it is, how it moves, and the keys that move it.</summary>
    internal Movement Movement { get; }

    // Handed in by the AgentPlayer that owns it, so a second agent on the same client
    // drives a second character rather than fighting the first for the controls.
    private readonly Player _player;

    // Built here rather than in initialisers, because the hand chooses from what the
    // inventory holds and an initialiser cannot see a sibling.
    public Character(Player player)
    {
        _player = player;
        Inventory = new Inventory(player);
        Hand = new Hand(player, Inventory);
        Movement = new Movement(player);
    }

    /// <summary>The raw Terraria player, for what the units do not yet wrap.</summary>
    // Each site that takes this wants something a unit should offer instead.
    internal Player Player => _player;

    /// <summary>
    /// Take the controls away from the person watching, before deciding anything.
    /// </summary>
    // Every key the agent might press, cleared in the same unit that sets them, so the
    // list of keys the agent drives exists once.
    public void Silence()
    {
        _player.controlLeft = false;
        _player.controlRight = false;
        _player.controlUp = false;
        _player.controlDown = false;
        _player.controlJump = false;
        _player.controlUseItem = false;
        _player.controlUseTile = false;
        _player.controlThrow = false;
        _player.controlHook = false;
        _player.controlMount = false;
    }
}
