using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ModLoader;

namespace Terragent.Display;

/// <summary>Draws the agent's plan into the world, over the tiles.</summary>
public sealed class PathOverlay : ModSystem
{
    private const int Tile = 16;

    private static readonly Color RouteColour = new(80, 200, 255, 200);
    private static readonly Color DigColour = new(255, 170, 60, 200);
    private static readonly Color GoalColour = new(120, 255, 140, 220);
    private static readonly Color AimColour = new(255, 255, 255, 240);
    private static readonly Color ReachColour = new(120, 160, 255, 120);

    /// <summary>What the route means to break, as opposed to what it is breaking.</summary>
    private static readonly Color PlannedColour = new(255, 150, 60, 110);

    /// <summary>
    /// Blocks the running scenario demands, as opposed to blocks the agent intends.
    /// </summary>
    private static readonly Color RequiredColour = new(255, 105, 220, 160);

    /// <summary>Blocks the route means to place, as opposed to break.</summary>
    private static readonly Color BuildColour = new(110, 190, 255, 170);

    public override void PostDrawTiles()
    {
        AgentState state = AgentPlayer.Local.Doing;

        // Digs were missing from this test, and && binds tighter than ||, so
        // the condition read "no path and no goal", which is exactly the
        // case while harvesting.
        bool nothingToShow = state.Waypoints.Count == 0 && state.Digs.Count == 0
            && state.Planned.Count == 0 && state.Required.Count == 0
            && state.Placing.Count == 0
            && state.Goal is null && state.Aimed is null && state.ReachBox is null
            && state.Footprint is null;
        // The arena drives the same follower without setting Driving, which is
        // a separate mode rather than a run. Gating the overlay on Driving
        // hides the route exactly when it matters most, watching one fail.
#if TESTING
        bool active = state.Driving || AgentMod.Pathing.Running || AgentMod.Combat.Running;
#else
        bool active = state.Driving;
#endif
        if (!AgentMod.ShowOverlay || !active || nothingToShow)
        {
            return;
        }

        SpriteBatch batch = Main.spriteBatch;
        batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);

        // The reach box, from the game's own numbers. A target outside it
        // cannot be swung at no matter what the agent believes, and seeing the
        // two together settles "is it in range" without anyone reading
        // coordinates.
        if (state.ReachBox is { } reach)
        {
            Vector2 corner = new Vector2(reach.X * Tile, reach.Y * Tile) - Main.screenPosition;
            Rectangle box = new((int)corner.X, (int)corner.Y,
                reach.Width * Tile, reach.Height * Tile);
            Texture2D pixel = TextureAssets.MagicPixel.Value;
            batch.Draw(pixel, new Rectangle(box.X, box.Y, box.Width, 2), ReachColour);
            batch.Draw(pixel, new Rectangle(box.X, box.Bottom - 2, box.Width, 2), ReachColour);
            batch.Draw(pixel, new Rectangle(box.X, box.Y, 2, box.Height), ReachColour);
            batch.Draw(pixel, new Rectangle(box.Right - 2, box.Y, 2, box.Height), ReachColour);
        }

        // The intended excavation first, so the tile actually being swung at
        // draws over it rather than under it.
        foreach (Point planned in state.Planned)
        {
            Outline(batch, planned, PlannedColour);
        }

        foreach (Point building in state.Placing)
        {
            DrawTileBox(batch, building, BuildColour);
            Outline(batch, building, BuildColour);
        }

        foreach (Point required in state.Required)
        {
            DrawTileBox(batch, required, RequiredColour);
            Outline(batch, required, RequiredColour);
        }

        DrawRoute(batch, state.Waypoints);

        // Squares rather than line segments: a dig is a tile, and drawing it
        // as one makes an off-by-one visible instead of plausible.
        foreach (Point dig in state.Digs)
        {
            DrawTileBox(batch, dig, DigColour);
            Outline(batch, dig, DigColour);
        }

        // The whole footprint, so a 2x1 bench looks like a 2x1 bench and an
        // object whose origin is not a corner (a Sawmill's is its bottom
        // middle) is drawn where it would actually stand.
        if (state.Footprint is { } footprint)
        {
            for (int fx = 0; fx < footprint.Width; fx++)
            {
                for (int fy = 0; fy < footprint.Height; fy++)
                {
                    DrawTileBox(batch, new Point(footprint.X + fx, footprint.Y + fy),
                        GoalColour);
                }
            }
        }

        if (state.Goal is { } goal)
        {
            Outline(batch, goal, GoalColour);
        }

        // Where the game says the cursor actually is, which is not necessarily
        // where the agent asked to aim.
        if (state.Aimed is { } aimed)
        {
            Outline(batch, aimed, AimColour);
        }

        batch.End();
    }

    private static void DrawTileBox(SpriteBatch batch, Point tile, Color colour)
    {
        Vector2 corner = new Vector2(tile.X * Tile, tile.Y * Tile) - Main.screenPosition;
        batch.Draw(TextureAssets.MagicPixel.Value,
            new Rectangle((int)corner.X, (int)corner.Y, Tile, Tile), colour * 0.45f);
    }

    /// <summary>A hollow box, so an overlapping fill underneath stays readable.</summary>
    private static void Outline(SpriteBatch batch, Point tile, Color colour)
    {
        Vector2 corner = new Vector2(tile.X * Tile, tile.Y * Tile) - Main.screenPosition;
        Rectangle box = new((int)corner.X, (int)corner.Y, Tile, Tile);
        Texture2D pixel = TextureAssets.MagicPixel.Value;
        batch.Draw(pixel, new Rectangle(box.X, box.Y, box.Width, 2), colour);
        batch.Draw(pixel, new Rectangle(box.X, box.Bottom - 2, box.Width, 2), colour);
        batch.Draw(pixel, new Rectangle(box.X, box.Y, 2, box.Height), colour);
        batch.Draw(pixel, new Rectangle(box.Right - 2, box.Y, 2, box.Height), colour);
    }

    /// <summary>Where the body's middle sits when standing on this footing.</summary>
    private static Vector2 Middle(Point footing, float lift) =>
        new(Body.SeamX(footing), (footing.Y * 16f) - lift);

    /// <summary>
    /// The route, as one unbroken line through the middles of the body's positions.
    /// </summary>
    private static void DrawRoute(SpriteBatch batch, IReadOnlyList<Point> route)
    {
        const int MaxSegment = 16;
        const float Thickness = 2f;

        // From the floor tile's top to the middle of a body standing on it.
        float lift = Main.LocalPlayer.height / 2f;
        Texture2D pixel = TextureAssets.MagicPixel.Value;

        for (int i = 0; i < route.Count; i++)
        {
            Vector2 dot = Middle(route[i], lift) - Main.screenPosition;
            batch.Draw(pixel, new Rectangle((int)dot.X - 2, (int)dot.Y - 2, 5, 5),
                RouteColour);

            if (i == 0)
            {
                continue;
            }

            Point from = route[i - 1];
            Point to = route[i];

            // A gap this wide is not a step, it is the join between two routes. Drawing
            // it would claim the character is about to cross ground it never planned to.
            if (System.Math.Abs(to.X - from.X) > MaxSegment
                || System.Math.Abs(to.Y - from.Y) > MaxSegment)
            {
                continue;
            }

            Vector2 a = Middle(from, lift) - Main.screenPosition;
            Vector2 span = dot - a;
            float length = span.Length();
            if (length < 0.5f)
            {
                continue;
            }

            // One texel of source, explicitly. MagicPixel is not a one-pixel
            // texture, and scaling the whole of it by the segment's length
            // draws a rectangle that many times too big: slabs across the
            // screen rather than a line.
            batch.Draw(pixel, a, new Rectangle(0, 0, 1, 1), RouteColour,
                (float)System.Math.Atan2(span.Y, span.X), new Vector2(0f, 0.5f),
                new Vector2(length, Thickness), SpriteEffects.None, 0f);
        }
    }

}
