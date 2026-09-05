using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terragent.Progression;

namespace Terragent.Plan;

/// <summary>
/// Turns the node the graph chose into things the character does.
/// </summary>
// Belief, Character and Requirements are constructor state: there is one of each per
// character. What changes per call is passed: where the agent stands, the clock, and
// the state this tick writes.
internal sealed class Executor(Pilot pilot, Exploring exploring, Fighting fighting,
    Building building, Belief belief, Character character, Requirements requirements)
{


    private readonly Requirements _requirements = requirements;
    private readonly Collecting _collect = new(pilot, belief, character);

    /// <summary>Pick up a wanted item lying on the ground, returning true when that is the thing to do.</summary>
    // Building drops a station it could not place and fetches it back through here,
    // rather than holding Collecting itself.
    internal bool Collect(AgentState state, int itemID) =>
        _collect.Collect(state, itemID, _now);

    /// <summary>The jobs on the board, and which one is being worked.</summary>
    // Data, not panel text: the planner must not decide how a job reads.
    internal IReadOnlyList<Job> Board => _board;

    internal int Chosen { get; private set; } = -1;

    private List<Job> _board = [];

    /// <summary>How far down a recipe chain one goal may reach.</summary>
    // An Iron Pickaxe needs an anvil, bars, a furnace and torches: five deep. Three
    // left the furnace unable to ask for a torch.
    private const int ChainDepth = 6;



    private readonly Pilot _pilot = pilot;

    private readonly Exploring _exploring = exploring;

    private readonly Fighting _fighting = fighting;

    private readonly Building _building = building;

    private readonly Belief _belief = belief;

    private readonly Character _character = character;

    /// <summary>Forget walls a better pickaxe might now open.</summary>
    public void ReconsiderWalls() => _pilot.ReconsiderWalls();
    private double _now;





    /// <summary>The one place the run is working towards, until it is done or hopeless.</summary>
    // A commitment, not a dwell. Chosen afresh each tick, the choice follows the
    // footing, which alternates rows mid-fall and flips the winner every frame. Once
    // settled, only this candidate is offered until it is reached, leaves the board or
    // proves unreachable; a craft doable on the spot still pre-empts, since it is not
    // a walk.
    //
    // Its site moving does not end it. Sites come from the nearest deposit to the body,
    // so they drift as the body walks; that redraws the route to the same candidate
    // rather than choosing a different one, which is what the pendulum was.
    private Candidate? SelectedCandidate { get; set; }

    /// <summary>Sites the search could not reach, from where, and when.</summary>
    // Blocked per site, not per objective: a stuck objective goes on backoff and the
    // board empties. A site stays blocked while the body is near where the search
    // failed, and the objective is stuck only when every job is.
    private readonly Dictionary<Point, (Point At, double When)> _blocked = [];

    private const int BlockedRadius = 6;

    /// <summary>How near a needed drop has to be to take precedence over every other source of it.</summary>
    private const int DropFirstTiles = 10;

    private const double BlockedSeconds = 30.0;

    private IReadOnlyList<Upkeep> _upkeeps = [];

    /// <summary>
    /// A standing requirement to work on beside the objective, rather than instead.
    /// </summary>
    // Restocking does not pre-empt: the shortfall and the objective are jobs on one
    // board, and whichever is reachable is worked.
    public void Act(Node node,
        AgentState state, double now, IReadOnlyList<Upkeep>? upkeeps = null)
    {
        state.Stuck = false;
        _now = now;
        _upkeeps = upkeeps ?? [];

        // Cleared here and set again by Fight, so it names what this objective wants
        // rather than what an earlier one did. Nearby reads last tick's value on
        // purpose; a tick of staleness cannot pick the wrong species.
        _fighting.Seeking = 0;
        Point at = _character.Movement.At;

        switch (node.Kind)
        {
            case NodeKind.Item when !node.Interchangeable:
                Obtain(new ItemQuantity(node.ItemID, node.Count), state, at);
                return;

            case NodeKind.Item:
            {
                Obtain(node.Any, node.Count, state, at);
                return;
            }

            default:
                state.Behaviour = $"no executor for {node.Key} yet";
                state.Stuck = true;
                return;
        }
    }

    /// <summary>Get hold of something, by whatever route it takes.</summary>
    public void Obtain(ItemQuantity itemNeeded, AgentState state, double now)
    {
        state.Stuck = false;
        _now = now;
        _upkeeps = [];
        Obtain(itemNeeded, state, _character.Movement.At);
    }

    /// <summary>
    /// Get hold of any one of these, which is what an interchangeable objective wants.
    /// </summary>
    // Every alternative is walked. Terraria has no group joining an iron pickaxe to a
    // lead one, so nothing expands them later; flattening only one chain offered iron
    // ore in a world that had lead.
    private void Obtain(IReadOnlyList<int> any, int count, AgentState state, Point at)
    {
        foreach (int alternative in any)
        {
            _character.Inventory.Reserve(new ItemQuantity(alternative, count));
        }

        if (_character.Inventory.Carrying(any) >= count)
        {
            state.Behaviour = "have it";
            return;
        }

        List<ItemQuantity> roots = [];
        foreach (int alternative in any)
        {
            roots.Add(new ItemQuantity(alternative, count));
        }

        List<ItemQuantity> outstanding = Outstanding(roots, at);
        if (outstanding.Count > 0)
        {
            Work(outstanding, state, at);
            return;
        }

        // Nothing outstanding and still not held: whichever is nearest to being made.
        Acquire(new ItemQuantity(_requirements.Pick(any, at), count),
            new Source(Origin.Craft), state, at);
    }

    private void Obtain(ItemQuantity itemNeeded, AgentState state, Point at)
    {
        // Whatever this job needs is not building material until the job is done. The
        // materials under it are reserved by Leaves on the way down, or the agent
        // pillars out of the hole with the stone it dug for the furnace.
        _character.Inventory.Reserve(itemNeeded);

        if (_character.Inventory.Carrying(itemNeeded.ItemID) >= itemNeeded.Count)
        {
            state.Behaviour = "have it";
            return;
        }

        // What the job comes down to, or the job itself when it is already raw: going
        // to the board is what puts it on the panel.
        List<ItemQuantity> outstanding = Outstanding([itemNeeded], at);
        Work(outstanding.Count > 0 ? outstanding : [itemNeeded], state, at);
    }

    /// <summary>
    /// What a job comes down to, with every restock riding along beside it.
    /// </summary>
    private List<ItemQuantity> Outstanding(List<ItemQuantity> roots, Point at)
    {
        foreach (Upkeep keep in _upkeeps)
        {
            if (!roots.Any(root => root.ItemID == keep.ItemID))
            {
                roots.Add(new ItemQuantity(keep.ItemID, keep.Ceiling));
            }
        }

        List<ItemQuantity> leaves = [];
        _requirements.Leaves(roots, at, ChainDepth, leaves);
        return leaves;
    }

    /// <summary>
    /// Make one, once its materials are in hand: gather what is not, then craft.
    /// </summary>
    // The one cycle here: Make -> Supply -> Acquire -> Make, entered when a craft is
    // short of something itself crafted. It ends because Blocking returns only what
    // sits strictly below the item in the recipe tree, and Make refuses to recurse
    // when the first shortfall is the item being made. The flattening in Outstanding
    // is bounded separately, by ChainDepth.
    private void Make(ItemQuantity itemNeeded, AgentState state, Point at)
    {
        int itemID = itemNeeded.ItemID;
        int count = itemNeeded.Count;

        if (Recipes.RecipeFor(itemID) is not { } recipe)
        {
            state.Behaviour = $"nothing makes a {Lang.GetItemNameValue(itemID)}";
            state.Stuck = true;
            return;
        }

        int have = _character.Inventory.Carrying(itemID);
        int perCraft = System.Math.Max(1, recipe.createItem.stack);
        int crafts = ((count - have) + perCraft - 1) / perCraft;

        foreach (Item ingredient in recipe.requiredItem)
        {
            if (ingredient.IsAir)
            {
                continue;
            }

            foreach (int alt in Recipes.Accepts(recipe, ingredient))
            {
                _character.Inventory.Reserve(new ItemQuantity(alt, ingredient.stack * crafts));
            }
        }

        List<ItemQuantity> blocking =
            _requirements.Blocking(recipe, crafts, at);
        if (blocking.Count > 0 && blocking[0].ItemID != itemID)
        {
            Supply(blocking[0], state, at);

            if (state.Stuck && _character.Inventory.CanCraft(itemID))
            {
                state.Stuck = false;
                state.Behaviour = $"crafting {Lang.GetItemNameValue(itemID)} with what there is";
                _character.Inventory.CraftItem(itemID);
            }

            return;
        }

        if (_character.Inventory.CanCraft(itemID))
        {
            state.Behaviour = $"crafting {Lang.GetItemNameValue(itemID)}"
                        + (crafts > 1 ? $" ({have}/{count})" : string.Empty);
            _character.Inventory.CraftItem(itemID);
            return;
        }

        // Every ingredient is carried and the recipe is still unavailable, so what is
        // missing is a station: FindRecipes already accounts for what is in reach.
        _building.GoToStation(recipe, state, at, _now);
    }

    /// <summary>Decide what the run does next, say so, and do it.</summary>
    // Every leaf goes on the board so the panel says what the job consists of. This
    // speaks for the run; Supply asks the same question for one ingredient and does
    // not.
    private void Work(List<ItemQuantity> neededItems,
        AgentState state, Point at)
    {
        List<Job> jobs = Jobs(neededItems, at);

        for (int n = 0; n < jobs.Count; n++)
        {
            if (jobs[n].Where is { } where && Blocked(where, at))
            {
                jobs[n] = jobs[n] with { Standing = Standing.Blocked };
            }
        }

        // A selection of something no longer on the board, or blocked on it, is over.
        if (SelectedCandidate is { } selected && Index(jobs, selected) is not { } kept)
        {
            SelectedCandidate = null;
        }

        // A craft with its ingredients carried beats anything that needs walking to:
        // the bench comes out of the bag. Having the ingredients, not being able to
        // craft this instant; those differ by a bench, and placing it is the craft's
        // own job.
        bool walking = false;
        int ready = Ready(jobs, at);
        int chosen = ready >= 0 ? ready : Choose(jobs, state, at, out walking, commit: true);

        if (!walking)
        {
            Acquire(jobs[chosen].ItemNeeded, jobs[chosen].Source, state, at);
        }

        // The candidate is blocked, not the objective: going to look is a job. The next
        // tick has the rest of the board.
        if (state.Stuck && _pilot.Unreachable && jobs[chosen].Where is { } site)
        {
            _blocked[site] = (at, _now);
            jobs[chosen] = jobs[chosen] with { Standing = Standing.Blocked };
            SelectedCandidate = null;
            state.Stuck = false;
            state.Behaviour = $"{state.Behaviour}; blocking that site";
        }

        _board = jobs;
        Chosen = chosen;
    }

    /// <summary>Get one thing a craft is short of, without speaking for the run.</summary>
    // A detail inside a job already chosen: it neither writes the panel over that job
    // nor takes the commitment that keeps the job from being re-chosen every tick.
    private void Supply(ItemQuantity itemNeeded, AgentState state, Point at)
    {
        List<Job> ways = Jobs([itemNeeded], at);
        int pick = Choose(ways, state, at, out bool walking, commit: false);
        if (!walking)
        {
            Acquire(ways[pick].ItemNeeded, ways[pick].Source, state, at);
        }
    }

    /// <summary>Every way of doing everything outstanding, and where each would happen.</summary>
    // Nothing is chosen here and nothing moves.
    private List<Job> Jobs(List<ItemQuantity> neededItems, Point at)
    {
        List<Job> jobs = [];
        foreach (ItemQuantity neededItem in neededItems)
        {
            // Drops go on the board like any other source, so gel lying across the
            // clearing is seen and ranked rather than only what is within a few tiles.
            if (_collect.Fallen(neededItem.ItemID) is { } lying)
            {
                // Collecting is contact: Terraria picks an item up when the body
                // overlaps it, so there is no tool reach to borrow, and the box is what
                // arrival is judged against.
                Point tile = new(lying.Center.X / 16, lying.Center.Y / 16);
                jobs.Add(new Job(neededItem, new Source(Origin.Drop), tile, 0, Box: lying));
            }

            List<Source> ways = Sources.SourcesOf(neededItem.ItemID);
            if (ways.Count == 0 && jobs.Count == 0)
            {
                jobs.Add(new Job(neededItem, default, null, 0));
                continue;
            }

            foreach (Source way in ways)
            {
                jobs.Add(Weigh(neededItem, way, at));
            }
        }

        return jobs;
    }

    /// <summary>The first craft with everything it needs already carried.</summary>
    // Blocking counts a station as had when it is in the bag, so empty means Make can
    // put the bench down and craft.
    private int Ready(List<Job> jobs, Point at)
    {
        for (int n = 0; n < jobs.Count; n++)
        {
            if (jobs[n].Source.From != Origin.Craft
                || Recipes.RecipeFor(jobs[n].ItemNeeded.ItemID) is not { } recipe)
            {
                continue;
            }

            int have = _character.Inventory.Carrying(jobs[n].ItemNeeded.ItemID);
            int perCraft = System.Math.Max(1, recipe.createItem.stack);
            int crafts = ((jobs[n].ItemNeeded.Count - have) + perCraft - 1) / perCraft;
            if (_requirements.Blocking(recipe, crafts, at).Count == 0)
            {
                return n;
            }
        }

        return -1;
    }

    /// <summary>Which job to work, and the walking that decides it.</summary>
    // Reachability is the ranking and only the search knows it, so every site goes
    // into one search and whichever it settles on is the choice. Returns the index
    // into <paramref name="jobs"/>, and true when the walking has already taken this
    // tick.
    /// <param name="commit">
    /// Whether the candidate settled on becomes the run's commitment. A craft fetching
    /// one missing ingredient chooses without committing, since it is a detail inside a
    /// job already chosen.
    /// </param>
    private int Choose(List<Job> jobs, AgentState state, Point at, out bool walking,
        bool commit)
    {
        walking = false;
        int chosen = -1;

        // Reached is asked from the footing, which is what the search stands on. From
        // the body's middle column it disagreed with the search by one, so a block the
        // search had arrived at was never in reach and nothing swung.
        Point footing = _character.Movement.Footing;
        List<Candidate> candidates = [];
        List<int> spoken = [];

        // Creatures apart. A tile is certain and a slime is a chance, so a sighted
        // creature is walked to only when nothing else can be, and with the fight's
        // own budget: a hopping target is somewhere new every tick, and one on a ledge
        // overhead has no route within any budget.
        List<Candidate> hunts = [];
        List<int> huntSpoken = [];
        int budget = 20000;

        // The selection, while it is on the board.
        int held = commit && SelectedCandidate is { } selected && Index(jobs, selected) is { } kept
            ? kept
            : -1;

        // A needed drop within a short walk takes the selection outright: no swing and
        // no wait. Otherwise the run fells tree after tree with the wood of each lying
        // at its feet. Once a drop is what is selected nothing takes it back, which is
        // what stops the ten tile radius reading as a decision to remake on every pass
        // across it.
        if (commit && (held < 0 || jobs[held].Source.From != Origin.Drop)
            && NearestDrop(jobs, at) is { } fallen)
        {
            held = fallen;
            Commit(jobs[fallen], commit);
        }

        for (int n = 0; n < jobs.Count; n++)
        {
            // Only the selection is offered while there is one; the rest stay listed.
            if (held >= 0 && n != held)
            {
                continue;
            }

            if (Offered(jobs[n]) is not { } offer || jobs[n].Standing is Standing.Blocked)
            {
                continue;
            }

            // Doable standing here beats anything that needs walking to, whichever
            // kind: a pot underfoot beats a jellyfish across the water. Judged by the
            // job's own distance, not the held item's reach; a drop counts only when
            // touched, a stone block when a pickaxe could break it from here.
            if (Arrived(jobs[n], footing))
            {
                return n;
            }

            if (jobs[n].Source.From == Origin.Creature)
            {
                hunts.Add(offer);
                huntSpoken.Add(n);
                continue;
            }

            candidates.Add(offer);
            spoken.Add(n);
        }

        if (candidates.Count == 0 && hunts.Count > 0)
        {
            candidates = hunts;
            spoken = huntSpoken;
            budget = Fighting.Chase;
        }

        chosen = spoken.Count > 0 ? spoken[0] : -1;

        // Nothing to walk to and nothing to hand: go and look for whichever is
        // expected nearest, not the first listed, which may be a creature the bestiary
        // places nowhere.
        if (chosen < 0)
        {
            return held >= 0 ? held : Nearest(jobs, at);
        }

        // Head for all of them at once and let the ground decide. The arrival test
        // handed to the search is the one applied above, so where the route ends is
        // somewhere this calls reached next tick; a radius here and the game's box
        // there disagreed by a row.
        // The candidates carry their own reach, so the two lists the search wants are
        // read straight off them rather than gathered alongside.
        List<Job> offered = [];
        List<Point> sites = [];
        List<int> reaches = [];
        for (int n = 0; n < candidates.Count; n++)
        {
            offered.Add(jobs[spoken[n]]);
            sites.Add(candidates[n].Site);
            reaches.Add(candidates[n].Within);
        }

        _pilot.Approach(_belief, _character, state, sites, _now, reaches,
            arrived: node => offered.Any(job => Arrived(job, node)), maxNodes: budget);

        // Unreachable blocks the candidates offered, not the objective: going to look is
        // a job. With a selection standing that offer is one candidate, so one is blocked.
        if (_pilot.Unreachable)
        {
            foreach (Point site in sites)
            {
                _blocked[site] = (at, _now);
            }

            foreach (int n in spoken)
            {
                jobs[n] = jobs[n] with { Standing = Standing.Blocked };
            }

            SelectedCandidate = null;
            state.Stuck = false;
            return Nearest(jobs, at);
        }

        // A search that ends where the body stands has decided without moving it; the
        // job is worked now rather than re-asked next tick from the same place.
        walking = _pilot.Behaviour != "arrived";

        // As the search says, not the candidate nearest to where the route ended:
        // straight lines are what the search exists to overrule.
        if (_pilot.Settled is { } settled)
        {
            for (int n = 0; n < candidates.Count; n++)
            {
                if (candidates[n].Site == settled)
                {
                    Commit(jobs[spoken[n]], commit);
                    return spoken[n];
                }
            }
        }

        Commit(jobs[chosen], commit);
        return chosen;
    }

    /// <summary>This job as a place to work, or null when it has no site yet.</summary>
    private static Candidate? Offered(Job job) =>
        job.Where is { } where
            ? new Candidate(job.ItemNeeded.ItemID, job.Source.From, where, job.Within)
            : null;

    /// <summary>The nearest needed drop within a short walk, or null.</summary>
    private int? NearestDrop(List<Job> jobs, Point at)
    {
        int found = -1;
        int nearest = int.MaxValue;
        for (int n = 0; n < jobs.Count; n++)
        {
            if (jobs[n].Source.From != Origin.Drop || jobs[n].Standing is Standing.Blocked
                || jobs[n].Where is not { } lying)
            {
                continue;
            }

            int apart = System.Math.Abs(lying.X - at.X) + System.Math.Abs(lying.Y - at.Y);
            if (apart <= DropFirstTiles && apart < nearest)
            {
                nearest = apart;
                found = n;
            }
        }

        return found >= 0 ? found : null;
    }

    /// <summary>Make this job the selection, if choosing here is allowed to.</summary>
    private void Commit(Job job, bool commit)
    {
        if (commit && Offered(job) is { } offer)
        {
            SelectedCandidate = offer;
        }
    }

    /// <summary>Where the selection is on this board, or null when it is not.</summary>
    // A creature's site moves with it, so a kill is matched by what it is for; a tile
    // or a drop by its cell.
    private static int? Index(List<Job> jobs, Candidate selected)
    {
        for (int n = 0; n < jobs.Count; n++)
        {
            Job job = jobs[n];
            if (job.Standing is Standing.Blocked || job.Where is null)
            {
                continue;
            }

            // By the work, never by the place. Matching the site too ended the selection
            // every time the nearest deposit changed under the walking body.
            if (Offered(job) is { } offered && selected.Same(offered))
            {
                return n;
            }
        }

        return null;
    }

    /// <summary>Whether a site is still blocked from where the body stands.</summary>
    private bool Blocked(Point site, Point at)
    {
        if (!_blocked.TryGetValue(site, out (Point At, double When) failed))
        {
            return false;
        }

        // Standing somewhere else is the one thing that can change the answer.
        if (System.Math.Abs(at.X - failed.At.X) + System.Math.Abs(at.Y - failed.At.Y) > BlockedRadius
            || _now - failed.When > BlockedSeconds)
        {
            _blocked.Remove(site);
            return false;
        }

        return true;
    }

    /// <summary>Whether this job can be worked from this footing.</summary>
    // A tile by the game's reach box with the tool the job would use; a drop by
    // contact; a creature by the targeting box.
    private bool Arrived(Job job, Point footing) =>
        job.Where is { } where
        && (job.Box is { } box
            ? Body.Touches(footing, box)
            : job.Source.From == Origin.Tile
                ? _character.Hand.ToolReachesFrom(job.Source.TileID, footing, where.X, where.Y)
                : Navigator.Reached(footing, where, job.Within));

    /// <summary>
    /// The job to go looking for: something to dig before something to kill, and the
    /// nearest expected row within that; the first job when none names a row.
    /// </summary>
    // Digging first because digging is certain and a spawn is a chance. A job with no
    // site and no expectation is returned only when every job is like that, so the
    // failure reported is that job's own and says what is missing.
    private static int Nearest(List<Job> jobs, Point at)
    {
        int best = -1;
        (int Kind, int Distance) nearest = (int.MaxValue, int.MaxValue);
        for (int n = 0; n < jobs.Count; n++)
        {
            if (jobs[n].Expected is not { } row || jobs[n].Standing is Standing.Blocked)
            {
                continue;
            }

            (int, int) rank = (jobs[n].Source.From == Origin.Tile ? 0 : 1,
                System.Math.Abs(row - at.Y));
            if (rank.CompareTo(nearest) < 0)
            {
                nearest = rank;
                best = n;
            }
        }

        return best >= 0 ? best : 0;
    }

    /// <summary>One way of doing one outstanding thing, and where it would be done.</summary>
    /// <param name="Where">Somewhere to walk to, or null when there is nowhere known.</param>
    // Several of these describe one line of the plan: a pot and a jellyfish are two
    // ways of doing one thing.
    /// <param name="Within">
    /// How close counts as having got there, in tiles. Different per kind: a drop has
    /// to be touched, a tile only reached with the tool that will break it.
    /// </param>
    // One reach per job, not one for the whole search: Terraria collects a drop on
    // contact, and a shared reach left the agent standing a tile short of its wood.
    /// <param name="Standing">Whether the job can be started, and if not, why it is still listed.</param>
    /// <param name="Box">Where a drop lies in pixels, which is what touching it means.</param>
    internal readonly record struct Job(ItemQuantity ItemNeeded, Source Source, Point? Where,
        int Within, int? Expected = null, Standing Standing = Standing.Open, Rectangle? Box = null);

    /// <summary>Why a job is on the board but not being worked.</summary>
    internal enum Standing
    {
        Open,

        /// <summary>Its candidate is blocked: no route to that site from where the body stands.</summary>
        // The job is not what is blocked. A job holds one site at a time, so its only
        // candidate being blocked leaves it with nowhere to be worked this tick, and
        // this says so on the board. Recomputed from the blocked sites every tick, so
        // it is a reading of them and never a second copy to keep in step.
        Blocked,
    }


    /// <summary>Find out where this way of getting something would be done.</summary>
    // Every kind answers the same two questions (doable from here, and if not, where)
    // so that one comparison ranks a dig against a kill against a craft. Ranking by
    // kind sent the agent to a pot two hundred rows down while a jellyfish swam past.
    private Job Weigh(ItemQuantity itemNeeded, Source way,
        Point at)
    {
        if (way.From == Origin.Craft)
        {
            // A craft has no site: the bench comes out of the bag, or Building finds
            // one. Ready has already taken any craft that could be done, so one reaching
            // here is short of something and waits on the rest of the board.
            return new Job(itemNeeded, way, null, 0);
        }

        if (way.From == Origin.Creature)
        {
            // Getting to the ground under it is all this has to do; whether it can be
            // hit from there is Engage's question, asked with the weapon.
            Point? sighted = _fighting.Sighted(itemNeeded.ItemID);
            int? band = sighted is null
                && SpawnZones.Nearest(itemNeeded.ItemID, at.Y, Main.dayTime) is { } layer
                ? Layers.EntryRow(layer)
                : null;
            return new Job(itemNeeded, way, sighted, _character.Hand.WeaponReachTiles, band);
        }

        // The tool the job would use, not the one in hand: a tree is reached at axe
        // range whatever is carried while walking.
        int within = Tiles.NeedsAxe(way.TileID) ? _character.Hand.AxeReachTiles : _character.Hand.PickaxeReachTiles;
        if (_belief.NearestSite(at, way.TileID, _belief.SupportsStation, out int x, out int y))
        {
            return new Job(itemNeeded, way, new Point(x, y), within);
        }

        // Nothing seen, so the row generation favours is where to look. Trees are
        // surface only, and Explore says so itself when asked.
        int? row = Tiles.NeedsAxe(way.TileID) ? null : OreDepths.BestRow(way.TileID, at.Y);
        return new Job(itemNeeded, way, null, within, row);
    }



    /// <summary>
    /// Do whatever it takes to have one of these, by the one way the source names.
    /// </summary>
    // Three arms and nothing else deciding between them. Acquire rather than Get,
    // which in C# reads a property: Obtain is the job, this is one act inside it.
    private void Acquire(ItemQuantity itemNeeded, Source source, AgentState state,
        Point at)
    {
        int itemID = itemNeeded.ItemID;
        int count = itemNeeded.Count;

        // A drop is a job of its own, chosen beside the tile, the creature and the
        // craft, not a pre-emption with a distance cap: the cap hid gel across a
        // clearing, and without it the agent climbed out of its shaft for one ore.
        if (source.From == Origin.Drop)
        {
            if (!_collect.Collect(state, itemID, _now))
            {
                state.Behaviour = $"cannot reach the {Lang.GetItemNameValue(itemID)} on the floor";
                state.Stuck = true;
            }

            return;
        }

        if (source.From == Origin.Creature)
        {
            _fighting.Fight(state, itemID, at, _now);
            return;
        }

        if (source.From == Origin.Craft)
        {
            Make(itemNeeded, state, at);
            return;
        }

        if (source.TileID == 0)
        {
            state.Behaviour = $"do not know where {Lang.GetItemNameValue(itemID)} comes from";
            state.Stuck = true;
            return;
        }

        if (!_belief.NearestSite(at, source.TileID, _belief.SupportsStation, out int x, out int y))
        {
            // Not on the map is not the same as not in the world: only revealed ground
            // is known, and A* cannot route into fog by design, because a plan through
            // unseen ground replans every step.
            _exploring.Explore(state, source, at, _now);
            return;
        }

        if (Tiles.NeedsAxe(source.TileID))
        {
            int foundX = x;
            int foundY = y;
            bool trunk = Tiles.BaseOfTree(_belief, ref x, ref y);

            // Logged rather than reasoned about: two attempts at telling a trunk from
            // a root looked correct and were wrong in play, so this records evidence.
            Log.Event("tree", trunk ? $"trunk ({x},{y})" : "no trunk",
                ("found", $"({foundX},{foundY})"),
                ("chose", $"({x},{y})"),
                ("above", _belief.TypeAt(x, y - 1)),
                ("below", _belief.TypeAt(x, y + 1)),
                ("belowKind", _belief.KindAt(x, y + 1).ToString()),
                ("left", _belief.TypeAt(x - 1, y)),
                ("right", _belief.TypeAt(x + 1, y)),
                ("leftAbove", _belief.TypeAt(x - 1, y - 1)),
                ("rightAbove", _belief.TypeAt(x + 1, y - 1)),
                ("frame", _belief.FrameAt(x, y)),
                ("frameAbove", _belief.FrameAt(x, y - 1)),
                ("frameRight", _belief.FrameAt(x + 1, y)));

            if (!trunk)
            {
                state.Behaviour = "found a branch, not a trunk";
                state.Stuck = true;
                return;
            }
        }

        state.Digs.Add(new Point(x, y));

        // Hold the tool before walking, not on arrival. Reach depends on the held
        // item's tileBoost, so approaching with a sword plans the walk against the
        // wrong box and draws the wrong range on screen.
        if (!(Tiles.NeedsAxe(source.TileID) ? _character.Hand.HoldAxe() : _character.Hand.HoldPickaxe()))
        {
            state.Behaviour = Tiles.NeedsAxe(source.TileID)
                ? "no axe anywhere in the inventory"
                : "no pickaxe anywhere in the inventory";
            state.Stuck = true;
            return;
        }

        if (!_character.Hand.InReach(x, y))
        {
            _pilot.Approach(_belief, _character, state, new Point(x, y), _now);
            return;
        }

        _pilot.Forget();

        _character.Hand.Aim(x, y);
        _character.Hand.Use();
        state.Behaviour = _character.Hand.Blocked
            ? "holding the swing until the game aims where we do"
            : $"swinging {_character.Hand.HeldName} at ({x}, {y})";
    }



}
