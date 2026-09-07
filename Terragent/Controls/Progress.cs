namespace Terragent.Controls;

/// <summary>How a pilot is getting on with where it was sent.</summary>
internal enum Progress
{
    /// <summary>Nowhere to be, so nothing is pressed.</summary>
    Idle,

    /// <summary>On the way.</summary>
    Going,

    /// <summary>Near enough to the destination to count as there.</summary>
    Arrived,

    /// <summary>No route to it from where the body stands.</summary>
    Unreachable,
}
