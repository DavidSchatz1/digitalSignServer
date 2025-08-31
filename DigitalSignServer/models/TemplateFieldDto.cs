namespace DigitalSignServer.models
{
    public record TemplateFieldDto(
    Guid Id, string Key, string Label, string Type,
    bool IsRequired, int Order, string DetectedFrom
    );
}
