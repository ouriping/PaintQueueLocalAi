namespace PaintQueueLocalAi;

/// <summary>向涂装缓存送车的喷房出口。</summary>
public enum BoothExitId
{
    /// <summary>喷房出口 A。唯一允许把车身直接放到快车道上放行的出口。</summary>
    A = 0,

    /// <summary>喷房出口 B。停在 B 口的车身必须先进入缓存，才能放行。</summary>
    B = 1
}
