# Terragent

An agent that plays Terraria using classical AI: A\* search over what the body can stand on, 
a goal graph for progression, and a follower that only ever presses the keys a player would.

It is a **tModLoader client mod** in C#, targeting Terraria **1.4.4.9**. Load a world,
switch it on, and watch it play your character.

The mod's panel has two switches:

- **Driving** hands the character to the agent. Turn it off to take it back.
- **Invulnerable** stops the character taking damage.

## How it works

Each tick the progression graph says what the run is still short of, and that objective
hands back a list of jobs: gather this, craft that, fight the thing in the way.

Every job that still has work names one place to go. A gather job picks the nearest ore it
has seen, a fight job the nearest creature, a craft job a station it can use. What nearest
means is each job's own business, and so is what counts as having arrived.

All of those places go into one A\* search together, not one search each. It runs over
footings, the two column by three row blocks of space the body can stand in, and its moves
are the ones a player has: walk, jump, fall, bridge a gap with a block, pillar up with one.
Every move is priced in ticks, so rock costs what it takes to mine.

The first destination the search reaches is therefore the cheapest to actually get to,
which is often not the closest in a straight line: ore behind a wall loses to ore down an
open shaft. That one result picks the job as well as the route. The pilot then walks it by
pressing the keys a player would, asking the destination each tick whether it is there
yet.

A place too far to plan in one go is handled while travelling rather than while choosing.
The search is capped, so when it runs out it hands back as much of the way as it did work
out, ending at the footing that got nearest. The body walks that stretch and the search
runs again from further along, which is how a long tunnel gets planned a piece at a time.

## Building and running

Copy `.env.example` to `.env` and set `TMODLOADER_INSTALL_PATH` to your own tModLoader
install directory. Both the build and the launch script read it from there; with no
`.env`, they fall back to Steam's default install location.

```
dotnet build Terragent
```

builds the mod and writes the package into tModLoader's `Mods` directory. It fails
while tModLoader is running, because the game holds the package open.

To run unattended, `Terragent/Tests/launch.ps1 "<flag>"` writes the flag, starts the
game and clicks past the no-audio panel; the game plays and exits. The flag is either a
number of seconds to play for, driven by the progression graph, or `arena` to walk the
pathfinding scenarios, optionally followed by text a scenario's name must contain. An
arena run enters whichever world and character are already saved, since it builds its own
ground in the sky and does not care what is underneath.

Everything the agent decides goes to the mod's own log under `tModLoader-Logs`: what it
chose and from how many offers, the whole route it planned as moves and coordinates,
every block mined and laid, and every step that stopped making ground.

## Tests

```
dotnet test Tests/Terragent.UnitTests   # the search alone, against written grids
Terragent/Tests/launch.ps1 "arena"      # the same cases, walked in a real world
```

Two levels over one list of scenarios, so the two cannot disagree about what a case is.
The headless tests run the search on its own and assert on exact plans. The arena stamps
the same cases as real tiles in the sky above a loaded world and hands them to the agent's
own pilot, scoring arrival, which kinds of move were made, and whether it ever stood
still. A scenario states yes or no on each of walk, jump, mine and build; "expect
anything" is not available.

## Contributing

Pull requests are welcome! No third-party bot or protocol libraries.
Reproduce a behaviour with a scenario before fixing it. The arena and the harness share one list.

## Licence

[MIT](LICENSE). This project is not affiliated with, endorsed by, or associated with
Re-Logic. Terraria is a trademark of Re-Logic. No game assets, code or binaries are
redistributed.
