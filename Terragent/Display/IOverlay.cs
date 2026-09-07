namespace Terragent.Display;

/// <summary>Marks drawn on the world itself: the route, and where it is going.</summary>
// World pixels rather than screen pixels, so everything it draws is offset by the camera
// and it has to be drawn under the interface rather than over it. That difference is the
// reason this is not the panel.
//
// Read only, like the panel, and pulled fresh every frame for the same reason.
internal interface IOverlay
{
    /// <summary>Draw this agent's plan onto the world.</summary>
    void Draw(IAgent agent);
}
