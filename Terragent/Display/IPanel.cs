namespace Terragent.Display;

/// <summary>The corner window: what the run is doing, in words.</summary>
// Screen pixels, drawn over everything, and pulled fresh every frame. It is handed the
// agent rather than fetching one, so nothing about where a run comes from is decided here
// and a second character on screen would need no new code.
//
// Read only, always. Everything it touches is a getter on an interface, which is what
// makes it impossible for a panel to change what the agent does.
internal interface IPanel
{
    /// <summary>Draw the window for this agent.</summary>
    void Draw(IAgent agent);
}
