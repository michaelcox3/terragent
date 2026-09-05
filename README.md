# Terragent

An agent that plays Terraria using classical AI: A\* search over what the body can stand on, 
a goal graph for progression, and a follower that only ever presses the keys a player would.

It is a **tModLoader client mod** in C#, targeting Terraria **1.4.4.9**. Load a world,
switch it on, and watch it play your character.

The mod's panel has two switches:

- **Driving** hands the character to the agent. Turn it off to take it back.
- **Invulnerable** stops the character taking damage.

## Building and running

Copy `.env.example` to `.env` and set `TMODLOADER_INSTALL_PATH` to your own tModLoader
install directory. Both the build and the launch script read it from there; with no
`.env`, they fall back to Steam's default install location.

```
dotnet build Terragent
```

builds the mod and writes the package into tModLoader's `Mods` directory. It fails
while tModLoader is running, because the game holds the package open.

Building it once is also what puts it in tModLoader's in-game Develop Mods menu, which
is where Build, Reload and Publish live. Since tModLoader v2025.01 the project does not
have to sit inside `ModSources` and does not want a link in there: a link is a second
path to the same project, and the menu then lists it twice.

To run unattended, `Terragent/Tests/launch.ps1 "<flag>"` writes the flag, starts the
game and clicks past the no-audio panel; the game plays and exits. The flag picks what
runs: `run` for every pathing scenario, one scenario's name for just that one, `combat`
for the fights, or `drive <seconds>` for a timed free play driven by the progression
graph. Adding `fresh` anywhere in the flag makes a never-played character and world,
which is the only run that says anything about a start. Each run writes a JSON-lines
journal under `tModLoader-Logs/agent/` with every search, route, decision and stall.

## Tests

```
Terragent/Tests/launch.ps1 "run"      # every pathing scenario
Terragent/Tests/launch.ps1 "combat"   # the fights
```

There is no headless harness. Every scenario builds real tiles in a real world and is
walked by the real follower, scored on arrival, on which kinds of move it made, and on
whether it ever stood still. A scenario states yes or no on each of walk, jump, mine and
build; "expect anything" is not available.

## Contributing

Pull requests are welcome! No third-party bot or protocol libraries.
Reproduce a behaviour with a scenario before fixing it. The arena and the harness share one list.

## Licence

[MIT](LICENSE). This project is not affiliated with, endorsed by, or associated with
Re-Logic. Terraria is a trademark of Re-Logic. No game assets, code or binaries are
redistributed.
