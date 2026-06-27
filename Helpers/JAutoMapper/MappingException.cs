namespace JAM;

public class MappingException : InvalidOperationException
{
    public Type SourceType { get; }
    public Type DestinationType { get; }
    public string? PropertyName { get; }

    public MappingException(string message, Type sourceType, Type destType, string? propertyName = null, Exception? inner = null)
        : base(message, inner)
    {
        SourceType = sourceType;
        DestinationType = destType;
        PropertyName = propertyName;
    }
}
