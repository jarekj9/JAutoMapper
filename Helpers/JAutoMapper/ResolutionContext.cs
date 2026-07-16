namespace JAM;

/// <summary>Provides context during resolver invocation. Can be extended in the future.</summary>
public class ResolutionContext
{
    internal int Depth { get; set; }
    internal Dictionary<(Type Src, Type Dest, object Instance), object> InstanceCache { get; } = new();

    internal void EnterMap() => Depth++;
    internal void LeaveMap() => Depth--;
}
