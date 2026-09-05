using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.ModLoader;
using Terraria.UI;

namespace Terragent.Display;

/// <summary>The agent's reasoning, on screen, over the game.</summary>
public sealed class AgentPanel : ModSystem
{
    private const int LineHeight = 20;
    private const int Width = 340;
    private const int Pad = 12;
    private const int TitleBar = 22;

    /// <summary>The scale every report row is drawn at, and so the one wrapping measures at.</summary>
    private const float TextScale = 0.85f;

    private static readonly Color Dim = new(150, 150, 160);
    private static readonly Color Live = new(255, 255, 255);
    private static readonly Color Good = new(120, 230, 140);
    private static readonly Color Bad = new(240, 140, 120);

    /// <summary>Progression rows visible at once. The rest scrolls.</summary>
    private const int ProgressionRows = 8;

    private static Vector2 _position;
    private static bool _placed;
    private static Rectangle _spine;
    private static int _scroll;
    private static bool _dragging;
    private static bool _wasDown;
    private static bool _hasScrolled;
    private static Vector2 _grabOffset;

    public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
    {
        // Above the inventory so the panel is never hidden by it, below the mouse text
        // so tooltips still win.
        int index = layers.FindIndex(layer => layer.Name == "Vanilla: Mouse Text");
        if (index == -1)
        {
            return;
        }

        layers.Insert(index, new LegacyGameInterfaceLayer(
            "Terragent: Panel", Draw, InterfaceScaleType.None));
    }

    /// <summary>
    /// The real cursor, <b>not</b> <c>Main.mouseX</c>.
    /// </summary>
    private static Point Cursor => new(PlayerInput.MouseX, PlayerInput.MouseY);

    private static bool Draw()
    {
        if (!AgentMod.ShowPanel || Main.gameMenu)
        {
            _dragging = false;

            // Keep tracking the button while hidden. A press held from the menu, which
            // is how a world is entered, would otherwise read as a fresh click on the
            // first frame in world and flip whichever switch is under the cursor.
            _wasDown = Main.mouseLeft;
            return true;
        }

        AgentState state = AgentPlayer.Local.Doing;

        // Under the hotbar rather than on top of it. The panel draws at InterfaceScale
        // None, in raw pixels, while the hotbar draws at the user's UI scale, so how
        // far down it reaches is only knowable once there is a frame to ask.
        if (!_placed)
        {
            _placed = true;
            _position = new Vector2(12f, 112f * Main.UIScale + 8f);
        }

        // A different window entirely while testing. The progression, the
        // upkeep and the spine are all about a run that is not happening:
        // during a scenario the only questions are which one, where the
        // character is, and whether it is getting there.
#if TESTING
        bool testing = AgentMod.Pathing.Running;
        List<(string Text, Color Colour)> lines = testing ? Scoreboard() : Report(state);
#else
        const bool testing = false;
        List<(string Text, Color Colour)> lines = Report(state);
#endif

        // Wrapped before the height is measured, because a job or a behaviour that runs
        // past the edge becomes two rows and the window has to own both.
        lines = Wrapped(lines);

        int spineRows = testing ? 0 : System.Math.Min(state.Progression.Count, ProgressionRows);

        // Drawn above the report rather than inside it: the driving line, the two
        // switches and the gap under them. The arena shows none of it.
        int header = testing ? 0 : 4;
        int height = (lines.Count + spineRows + 1) * LineHeight
                     + Pad * 2 + TitleBar + (header * LineHeight) + 8;
        Rectangle window = new((int)_position.X, (int)_position.Y, Width, height);
        Rectangle titleBar = new(window.X, window.Y, Width, TitleBar);

        bool down = Main.mouseLeft;
        bool clicked = down && !_wasDown;
        Point cursor = Cursor;

        Drag(titleBar, down, cursor);
        window.X = (int)_position.X;
        window.Y = (int)_position.Y;
        titleBar.Location = window.Location;

        // Claim the mouse only while the button is actually down, never on hover.
        //
        // mouseInterface makes Player.Update set delayUseItem, and Character.Use
        // refuses to swing while that is set, so claiming the mouse for a cursor merely
        // resting over the panel stops the agent dead. Reading the panel is not
        // interacting with it, and a press still claims, which is all the toggles and
        // the title bar ever needed.
        int status = window.Y + TitleBar + Pad / 2;
        int toggles = testing ? status : status + LineHeight;
        bool overToggles = !testing && cursor.X >= window.X && cursor.X <= window.Right
            && cursor.Y >= toggles && cursor.Y <= toggles + (LineHeight * 2);
        if (_dragging || (down && (titleBar.Contains(cursor) || overToggles)))
        {
            Main.LocalPlayer.mouseInterface = true;
        }

        SpriteBatch batch = Main.spriteBatch;
        batch.Draw(TextureAssets.MagicPixel.Value, window, Color.Black * 0.6f);
        batch.Draw(TextureAssets.MagicPixel.Value, titleBar,
            (_dragging ? new Color(70, 90, 120) : new Color(45, 55, 75)) * 0.95f);
        Utils.DrawBorderString(batch,
            testing ? "Terragent (testing)" : "Terragent",
            new Vector2(window.X + Pad, window.Y + 3f), Dim, 0.8f);

        // Which build is actually running. See AgentMod.BuildStamp.
        const float Scale = 0.7f;
        float stamp = FontAssets.MouseText.Value.MeasureString(AgentMod.BuildStamp).X
                      * Scale;
        Utils.DrawBorderString(batch, AgentMod.BuildStamp,
            new Vector2(window.Right - Pad - stamp, window.Y + 4f), Dim, Scale);

        // Whether the agent has the controls, then the two switches, where a hand goes
        // without reading.
        int y = status;
        if (!testing)
        {
            Utils.DrawBorderString(batch,
                state.Driving ? "Agent Driving" : "Agent Idle",
                new Vector2(window.X + Pad, y), state.Driving ? Good : Dim, TextScale);
            y += LineHeight;

            AgentPlayer.Local.Doing.Driving = Toggle(batch, window, ref y, cursor, clicked,
                "Driving", AgentPlayer.Local.Doing.Driving);
            AgentMod.Invulnerable = Toggle(batch, window, ref y, cursor, clicked,
                "Invulnerable", AgentMod.Invulnerable);
            y += LineHeight;
        }

        foreach ((string text, Color colour) in lines)
        {
            if (text.Length > 0)
            {
                Utils.DrawBorderString(batch, text, new Vector2(window.X + Pad, y),
                    colour, TextScale);
            }

            y += LineHeight;
        }

        y += 4;
        if (!testing)
        {
            DrawProgression(batch, window, ref y, state);
        }

        _wasDown = down;
        return true;
    }

    private static void Drag(Rectangle titleBar, bool down, Point cursor)
    {
        if (!_dragging && down && titleBar.Contains(cursor))
        {
            _dragging = true;
            _grabOffset = cursor.ToVector2() - _position;
        }
        else if (!down)
        {
            _dragging = false;
        }

        if (!_dragging)
        {
            return;
        }

        _position = cursor.ToVector2() - _grabOffset;

        // Keep a grabbable strip on screen: a window dragged fully off is a window you
        // have to edit a config file to get back.
        _position.X = MathHelper.Clamp(_position.X, -Width + 80f, Main.screenWidth - 80f);
        _position.Y = MathHelper.Clamp(_position.Y, 0f, Main.screenHeight - TitleBar);
    }

    /// <summary>
    /// The progression, scrollable, with the current goal kept in view.
    /// </summary>
    private static void DrawProgression(SpriteBatch batch, Rectangle window, ref int y,
        AgentState state)
    {
        if (state.Progression.Count == 0)
        {
            return;
        }

        int rows = System.Math.Min(state.Progression.Count, ProgressionRows);
        Rectangle area = new(window.X, y, Width, rows * LineHeight);
        int maxScroll = System.Math.Max(0, state.Progression.Count - rows);

        _spine = area;

        // Until somebody scrolls, follow the current goal. After that, leave
        // it where they put it: a view that snaps back while you are reading
        // it is worse than one that is occasionally stale.
        if (!_hasScrolled)
        {
            int current = state.Progression.FindIndex(entry => entry.Status == "now");
            if (current >= 0)
            {
                _scroll = System.Math.Clamp(current - rows / 2, 0, maxScroll);
            }
        }

        Utils.DrawBorderString(batch,
            $"Progression  {_scroll + 1}-{_scroll + rows} of {state.Progression.Count}"
            + (maxScroll > 0 ? "   (Wheel)" : ""),
            new Vector2(window.X + Pad, y), Dim, 0.75f);
        y += LineHeight;

        for (int row = 0; row < rows; row++)
        {
            (string label, string status) = state.Progression[_scroll + row];
            (string glyph, Color colour) = status switch
            {
                "done" => ("x", Good),
                "now" => (">", Live),
                "skipped" => ("!", Bad),
                "ready" => ("-", Dim),
                _ => (" ", new Color(90, 90, 100)),
            };
            Utils.DrawBorderString(batch, $" [{glyph}] {label}",
                new Vector2(window.X + Pad, y), colour, status == "now" ? 0.9f : 0.8f);
            y += LineHeight;
        }
    }

    /// <summary>
    /// Scrolling the progression, taken before the hotbar can see the same wheel.
    /// </summary>
    // Drawing is far too late to consume input: by then the selected item has already
    // changed, so reading the progression swapped the weapon out from under the agent.
    // This runs before the players update, which is the last moment the delta is still
    // anybody's to claim.
    public override void PreUpdatePlayers()
    {
        if (!AgentMod.ShowPanel || Main.gameMenu || _spine.Width == 0
            || !_spine.Contains(Cursor))
        {
            return;
        }

        int wheel = PlayerInput.ScrollWheelDeltaForUI / 120;
        if (wheel == 0)
        {
            return;
        }

        int maxScroll = System.Math.Max(0, AgentPlayer.Local.Doing.Progression.Count - ProgressionRows);
        _scroll = System.Math.Clamp(_scroll - wheel, 0, maxScroll);
        _hasScrolled = true;

        PlayerInput.ScrollWheelDelta = 0;
        PlayerInput.ScrollWheelDeltaForUI = 0;
    }

    /// <summary>A clickable checkbox row. Returns the value after any click.</summary>
    /// <summary>Splits rows too wide for the window, hanging the remainder under itself.</summary>
    private static List<(string Text, Color Colour)> Wrapped(
        List<(string Text, Color Colour)> lines)
    {
        List<(string Text, Color Colour)> wrapped = new(lines.Count);
        foreach ((string text, Color colour) in lines)
        {
            string body = text.TrimStart();
            string indent = text[..(text.Length - body.Length)];
            if (body.Length == 0 || Fits(text))
            {
                wrapped.Add((text, colour));
                continue;
            }

            // The continuation sits two spaces deeper than the row it belongs to, so a
            // wrapped job cannot be read as a second job.
            string prefix = indent;
            string line = "";
            foreach (string word in body.Split(' '))
            {
                string wider = line.Length == 0 ? word : $"{line} {word}";
                if (Fits(prefix + wider))
                {
                    line = wider;
                    continue;
                }

                // A single word wider than the window still goes out on its own row:
                // breaking mid-word costs more than the overhang.
                if (line.Length > 0)
                {
                    wrapped.Add((prefix + line, colour));
                    prefix = indent + "  ";
                }

                line = word;
            }

            wrapped.Add((prefix + line, colour));
        }

        return wrapped;
    }

    private static bool Fits(string text) =>
        FontAssets.MouseText.Value.MeasureString(text).X * TextScale <= Width - (Pad * 2);

    private static bool Toggle(SpriteBatch batch, Rectangle window, ref int y,
        Point cursor, bool clicked, string label, bool value)
    {
        Rectangle row = new(window.X + Pad, y, Width - Pad * 2, LineHeight);
        bool hovered = row.Contains(cursor);
        if (hovered && clicked)
        {
            value = !value;
        }

        Utils.DrawBorderString(batch, $"[{(value ? "x" : " ")}] {label}",
            new Vector2(row.X, row.Y), value ? Good : Dim, hovered ? 0.95f : 0.85f);
        y += LineHeight;
        return value;
    }

#if TESTING
    /// <summary>
    /// What a scenario run looks like: which one, where, and how it is going.
    /// </summary>
    private static List<(string, Color)> Scoreboard()
    {
        PathingTests arena = AgentMod.Pathing;
        Point at = AgentPlayer.Local.Acting.Movement.Footing;

        List<(string, Color)> lines =
        [
            ($"Scenario {arena.Number}/{arena.Total}", Dim),
            ($"  {arena.Current}", Live),
            ("", Dim),
            ($"  {AgentPlayer.Local.Doing.Behaviour}", Live),
            ($"  At ({at.X}, {at.Y})  Goal ({arena.Goal.X}, {arena.Goal.Y})", Dim),
            ($"  {arena.Remaining:0.0}s Left", arena.Remaining < 3 ? Bad : Dim),
            ("", Dim),
            ($"Passed {arena.Passed}   Failed {arena.Failed}",
                arena.Failed > 0 ? Bad : Good),
        ];

        foreach ((string name, bool walked) in arena.Recent)
        {
            lines.Add(($"  {(walked ? "OK  " : "FAIL")} {name}", walked ? Good : Bad));
        }

        return lines;
    }
#endif

    private static List<(string, Color)> Report(AgentState state)
    {
        List<(string, Color)> lines =
        [
            ("Objective", Dim),
            ($"  {Sentence(state.Objective)}", Live),
        ];

        if (state.Reason.Length > 0)
        {
            lines.Add(($"  {Sentence(state.Reason)}", Dim));
        }

        IReadOnlyList<Executor.Job> jobs = AgentPlayer.Local.Jobs;
        if (jobs.Count > 0)
        {
            lines.Add(("", Dim));
            lines.Add(("Jobs", Dim));
            for (int n = 0; n < jobs.Count; n++)
            {
                bool active = n == AgentPlayer.Local.WorkingJob;
                lines.Add(($"  {(active ? ">" : "-")} {JobLine.Doing(jobs[n])}",
                    active ? Live : Dim));
            }
        }

        lines.Add(("", Dim));
        lines.Add(("Doing", Dim));
        lines.Add(($"  {Sentence(state.Behaviour.Length > 0 ? state.Behaviour : "Nothing")}",
            Live));

        if (state.Waypoints.Count > 0)
        {
            lines.Add(($"  {state.Waypoints.Count} Waypoints, {state.Digs.Count} Digs", Dim));
        }

        if (state.Blocked.Count > 0)
        {
            // One per line with its reason. "blocked: furnace" is the half of
            // the report that does not help; "furnace: no route to (2105,
            // 284)" is the half that does, and it went only to the log.
            lines.Add(("", Dim));
            lines.Add(("Stuck", Bad));
            foreach (string blocked in Tail(state.Blocked, 3))
            {
                lines.Add(($"  {Sentence(blocked)}", Bad));
            }
        }

        // Not a readout: the two answers have diverged and every placement from here is
        // aimed at a tile the game will refuse.
        if (state.Driving && !state.ReachAgrees)
        {
            lines.Add(("", Dim));
            lines.Add(("Reach disagrees with the game", Bad));
        }

        return lines;
    }

    /// <summary>First letter up. The agent writes its own lines in lower case.</summary>
    private static string Sentence(string line) =>
        line.Length == 0 ? line : char.ToUpperInvariant(line[0]) + line[1..];

    private static List<string> Tail(List<string> items, int count) =>
        items.GetRange(System.Math.Max(0, items.Count - count),
            System.Math.Min(count, items.Count));
}
