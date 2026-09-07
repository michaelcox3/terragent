using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ModLoader;
using Terragent.Controls;
using Terragent.Pathfinding;
using Terragent.World;

namespace Terragent.Display;

/// <summary>The route, drawn on the ground it is planned over.</summary>
// Reads the pilot and keeps nothing, like the panel. What it draws is the plan the search
// returned, not a redrawing of it: if the marks and the walking disagree, the disagreement
// is the bug, and a second derivation here would hide it.
public sealed class Overlay : ModSystem, IOverlay
{
    private const int TilePixels = 16;

    /// <summary>Where a step lands.</summary>
    private static readonly Color Footing = Color.SkyBlue * 0.7f;

    /// <summary>Where a step ends up, once there are no more.</summary>
    private static readonly Color Site = Color.Gold * 0.8f;

    /// <summary>A tile the route has booked to break.</summary>
    private static readonly Color Breaking = Color.OrangeRed * 0.7f;

    /// <summary>A tile the route has booked to put down.</summary>
    private static readonly Color Building = Color.LimeGreen * 0.7f;

    /// <summary>How thick a route line is drawn, in pixels.</summary>
    private const float Thick = 2f;

    // After the tiles and before the interface, which is what puts marks on the world
    // rather than on the screen. The batch is not open here, so it is opened and closed
    // rather than joined.
    public override void PostDrawTiles()
    {
        if (Main.gameMenu || Main.LocalPlayer is not { active: true } player)
        {
            return;
        }

        IAgent? agent = player.GetModPlayer<AgentPlayer>().Agent;
        if (agent is null)
        {
            return;
        }

        SpriteBatch batch = Main.spriteBatch;
        batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullCounterClockwise, null,
            Main.GameViewMatrix.TransformationMatrix);

        ((IOverlay)this).Draw(agent);

        batch.End();
    }

    // Explicitly implemented: tModLoader only autoloads public types, and a public method
    // cannot take an internal one.
    void IOverlay.Draw(IAgent agent)
    {
        if (agent.Foreman.Destination is { } site)
        {
            Box(site.Site, Site);
        }

        IPilot pilot = agent.Foreman.Pilot;
        Route? route = pilot.Route;
        if (route is null)
        {
            return;
        }

        // From the body, not from the start of the route. A path drawn from where the
        // plan began floats behind the character and reads as somebody else's route.
        Vector2 last = Main.LocalPlayer.Center;

        // From where it has got to, not from the start. What is drawn is what is still
        // going to happen.
        for (int n = pilot.Walked; n < route.Steps.Count; n++)
        {
            Step step = route.Steps[n];
            Vector2 middle = Hitbox.Middle(step.To);
            Line(last, middle, Footing);
            Dot(middle, Footing);
            last = middle;

            // Only what is still there. The route books its digging once and is never
            // rewritten, so a tile already broken keeps its booking: drawing that reads as
            // work outstanding on ground the body has already cleared.
            foreach (Point cell in step.Removes)
            {
                if (!agent.Terrain.Passable(cell.X, cell.Y))
                {
                    Box(cell, Breaking);
                }
            }

            if (step.Puts is { } put && agent.Terrain.Passable(put.X, put.Y))
            {
                Box(put, Building);
            }
        }
    }

    /// <summary>Join two places in the world, so a route reads as a path.</summary>
    // One texel of source, explicitly. MagicPixel is not a one pixel texture, and scaling
    // the whole of it by the segment's length draws a rectangle that many times too big:
    // slabs across the screen rather than a line.
    private static void Line(Vector2 from, Vector2 to, Color colour)
    {
        Vector2 start = from - Main.screenPosition;
        Vector2 along = to - from;
        float length = along.Length();
        if (length < 0.5f)
        {
            return;
        }

        Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value, start, new Rectangle(0, 0, 1, 1),
            colour, (float)System.Math.Atan2(along.Y, along.X), new Vector2(0f, 0.5f),
            new Vector2(length, Thick), SpriteEffects.None, 0f);
    }

    /// <summary>A dot where a step lands, so the corners of a path are readable.</summary>
    private static void Dot(Vector2 at, Color colour)
    {
        Vector2 middle = at - Main.screenPosition;
        Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value,
            new Rectangle((int)middle.X - 2, (int)middle.Y - 2, 5, 5), colour);
    }

    /// <summary>Outline one tile, in world pixels.</summary>
    // Offset by the camera, since everything here is a place in the world and the screen
    // is only where the camera happens to be pointing.
    private static void Box(Point tile, Color colour)
    {
        Texture2D pixel = TextureAssets.MagicPixel.Value;
        Vector2 corner = new Vector2(tile.X * TilePixels, tile.Y * TilePixels) - Main.screenPosition;
        Rectangle box = new((int)corner.X, (int)corner.Y, TilePixels, TilePixels);

        Main.spriteBatch.Draw(pixel, new Rectangle(box.X, box.Y, box.Width, 2), colour);
        Main.spriteBatch.Draw(pixel, new Rectangle(box.X, box.Bottom - 2, box.Width, 2), colour);
        Main.spriteBatch.Draw(pixel, new Rectangle(box.X, box.Y, 2, box.Height), colour);
        Main.spriteBatch.Draw(pixel, new Rectangle(box.Right - 2, box.Y, 2, box.Height), colour);
    }
}
