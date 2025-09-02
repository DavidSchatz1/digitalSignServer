namespace DigitalSignServer.Exceptions;

public sealed class TemplateFillValidationException : Exception
{
    public TemplateFillValidationException(IEnumerable<string> missingKeys, IEnumerable<string> noMatchSeen)
        : base("Not all fields were filled.")
    {
        MissingKeys = missingKeys.ToArray();
        NoMatchSeen = noMatchSeen.ToArray();
    }
    public string[] MissingKeys { get; }
    public string[] NoMatchSeen { get; }
}
