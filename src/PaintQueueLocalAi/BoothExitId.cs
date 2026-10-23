namespace PaintQueueLocalAi;

/// <summary>Spray-booth exit that feeds the buffer bank.</summary>
public enum BoothExitId
{
    /// <summary>Booth exit A. The only exit allowed to release a body directly on the fast lane.</summary>
    A = 0,

    /// <summary>Booth exit B. A body at B must enter a buffer before it can be released.</summary>
    B = 1
}
