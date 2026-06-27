namespace JAM;

internal class CustomEntry
{
    public string DestMemberName { get; init; } = "";
    public Delegate Resolver { get; init; } = null!;
}
