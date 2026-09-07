using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ModLoader;
using Terraria.UI;
using Terragent.Controls;
using Terragent.Work;
using Terragent.Work.Jobs;
using Terragent.Work.Objectives;
using Terragent.World;

namespace Terragent.Display;

/// <summary>What the agent is doing, in the corner of the screen.</summary>
// Reads the interfaces and keeps nothing but the two boxes it can be clicked on. Every
// line here is an answer something already gives: the agent says whether it is driving,
// the foreman says what it chose and what it chose between, the pilot says how the
// walking is going.
public sealed class Panel : ModSystem, IPanel
{
    private const int WidthPixels = 340;
    private const int LinePixels = 18;
    private const int PadPixels = 8;
    private const int BoxPixels = 14;

    /// <summary>Where the window sits, which the user can move.</summary>
    // Below the hotbar, not over it. The top left corner is where Terraria draws the ten
    // slots and the coin and ammo columns, and a panel there covers the thing you most
    // want to see while watching the agent play.
    private static Point _corner = new(20, 260);

    /// <summary>Where it is being dragged from, or null when it is not.</summary>
    private static Point? _grabbed;

    /// <summary>The strip along the top that can be dragged by.</summary>
    private static Rectangle _bar;

    /// <summary>Where the two switches were last drawn, so a click can find them.</summary>
    // Drawing is far too late to take input: by the time a layer runs, the click has
    // already gone through to the world. So the boxes are remembered here and read by the
    // update hook below, a frame behind, and nobody minds.
    private static Rectangle _driving;
    private static Rectangle _invulnerable;

    /// <summary>How many unfinished objectives to list.</summary>
    private const int Upcoming = 5;

    public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
    {
        int index = layers.FindIndex(layer => layer.Name == "Vanilla: Mouse Text");
        if (index < 0)
        {
            return;
        }

        layers.Insert(index, new LegacyGameInterfaceLayer(
            "Terragent: Panel", Layer, InterfaceScaleType.None));
    }

    /// <summary>Take a click on a switch, or a drag on the bar, before the world sees it.</summary>
    // Here rather than while drawing, because by the time a layer runs the click has
    // already gone through to the world and swung a pickaxe.
    public override void PreUpdatePlayers()
    {
        if (Main.gameMenu || Main.LocalPlayer is not { active: true } player)
        {
            return;
        }

        if (!Main.mouseLeft)
        {
            _grabbed = null;
            return;
        }

        Point at = new(Main.mouseX, Main.mouseY);

        // Held from a previous frame, so this is a drag and nothing else happens.
        if (_grabbed is { } hold)
        {
            _corner = new Point(at.X - hold.X, at.Y - hold.Y);
            player.mouseInterface = true;
            return;
        }

        if (!Main.mouseLeftRelease)
        {
            return;
        }

        AgentPlayer driver = player.GetModPlayer<AgentPlayer>();

        if (_driving.Contains(at) && driver.Agent is { } agent)
        {
            agent.Driving = !agent.Driving;
            player.mouseInterface = true;
        }
        else if (_invulnerable.Contains(at))
        {
            driver.Invulnerable = !driver.Invulnerable;
            player.mouseInterface = true;
        }
        else if (_bar.Contains(at))
        {
            _grabbed = new Point(at.X - _corner.X, at.Y - _corner.Y);
            player.mouseInterface = true;
        }
    }

    private bool Layer()
    {
        if (!Main.gameMenu && Main.LocalPlayer is { active: true } player
            && player.GetModPlayer<AgentPlayer>() is { Agent: { } agent } driver)
        {
            Show(agent, driver);
        }

        return true;
    }

    void IPanel.Draw(IAgent agent) =>
        Show(agent, Main.LocalPlayer.GetModPlayer<AgentPlayer>());

    private static void Show(IAgent agent, AgentPlayer driver)
    {
        List<string> lines = Lines(agent);
        Rectangle window = new(_corner.X, _corner.Y, WidthPixels,
            ((lines.Count + 2) * LinePixels) + (PadPixels * 5));

        SpriteBatch batch = Main.spriteBatch;
        batch.Draw(TextureAssets.MagicPixel.Value, window, Color.Black * 0.6f);

        _bar = new Rectangle(window.X, window.Y, window.Width, LinePixels + PadPixels);
        batch.Draw(TextureAssets.MagicPixel.Value, _bar, Color.White * 0.12f);
        Utils.DrawBorderString(batch, "Terragent",
            new Vector2(window.X + PadPixels, window.Y + (PadPixels / 2)), Color.White);

        int y = window.Y + LinePixels + (PadPixels * 2);
        _driving = Switch(batch, window.X + PadPixels, y, agent.Driving, "Driving");
        _invulnerable = Switch(batch, window.X + (WidthPixels / 2), y, driver.Invulnerable,
            "Invulnerable");
        y += LinePixels + PadPixels;

        foreach (string line in lines)
        {
            // An empty entry is a gap between sections rather than a line to draw.
            if (line.Length == 0)
            {
                y += PadPixels;
                continue;
            }

            Utils.DrawBorderString(batch, line, new Vector2(window.X + PadPixels, y), Color.White);
            y += LinePixels;
        }
    }

    /// <summary>One switch, drawn as a box and a word, and where it landed.</summary>
    private static Rectangle Switch(SpriteBatch batch, int x, int y, bool on, string label)
    {
        Rectangle box = new(x, y + 2, BoxPixels, BoxPixels);
        batch.Draw(TextureAssets.MagicPixel.Value, box, on ? Color.LightGreen : Color.Gray);
        Utils.DrawBorderString(batch, label, new Vector2(x + BoxPixels + 6, y), Color.White);
        return box;
    }

    /// <summary>The state of the run, one line at a time.</summary>
    private static List<string> Lines(IAgent agent)
    {
        IForeman foreman = agent.Foreman;
        List<string> lines =
        [
            foreman.Objective is { } objective
                ? $"Objective: {objective.Label}"
                : "Objective: nothing left it can do",
            $"Reached: {agent.Reached.Count}",
            string.Empty,
        ];

        IPilot pilot = foreman.Pilot;
        lines.Add(foreman.Job is { } doing ? $"Doing: {doing.Label}" : "Doing: nothing");
        lines.Add(foreman.Destination is { } site
            ? $"Site: ({site.Site.X}, {site.Site.Y}) within {site.Within}"
            : "Site: nowhere");
        lines.Add(pilot.Route is { } route
            ? $"Pilot: {Said(pilot.Progress)}, step {pilot.Walked + 1} of {route.Steps.Count}"
            : $"Pilot: {Said(pilot.Progress)}");
        lines.Add(string.Empty);
        lines.Add("Items Needed");
        foreach (KeyValuePair<int, int> want in foreman.Objective?.Missing()
            ?? new Dictionary<int, int>())
        {
            lines.Add($"    {Names.Item(want.Key)} x{want.Value}");
        }

        lines.Add(string.Empty);
        lines.Add("Jobs");

        // What the objective is offering, with the one in hand marked. A list of one
        // means every other job has no site, which is usually the answer to why it is
        // doing something odd.
        //
        // Matched by name rather than by instance: the objective builds fresh jobs every
        // time it is asked, so nothing in this list is the object the foreman holds.
        foreach (IJob job in foreman.Objective?.Jobs() ?? [])
        {
            lines.Add(job.Label == foreman.Job?.Label
                ? $"  > {job.Label}"
                : $"    {job.Label}");
        }

        lines.Add(string.Empty);
        lines.Add($"Run ({agent.Reached.Count} of {Steps(agent)} done)");
        Ahead(lines, agent);

        return lines;
    }

    /// <summary>How many of the run's nodes are objectives rather than standing supplies.</summary>
    private static int Steps(IAgent agent)
    {
        int steps = 0;
        foreach (Node node in agent.Progression.Nodes)
        {
            steps += node.Standing ? 0 : 1;
        }

        return steps;
    }

    /// <summary>The next few things the run has not reached, current one first.</summary>
    // A window rather than the whole graph. Thirty lines of mostly finished work is not a
    // panel, and what is wanted is where it is and what comes next.
    private static void Ahead(List<string> lines, IAgent agent)
    {
        HashSet<string> reached = [.. agent.Reached];
        int shown = 0;
        foreach (Node node in agent.Progression.Nodes)
        {
            if (node.Standing || reached.Contains(node.Key))
            {
                continue;
            }

            lines.Add(ReferenceEquals(node.Objective, agent.Foreman.Objective)
                ? $"  > {node.Objective.Label}"
                : $"    {node.Objective.Label}");

            if (++shown == Upcoming)
            {
                return;
            }
        }
    }

    /// <summary>What the pilot is doing, in a word.</summary>
    private static string Said(Progress progress) => progress switch
    {
        Progress.Idle => "Idle",
        Progress.Going => "Going",
        Progress.Arrived => "Arrived",
        _ => "No route",
    };
}
