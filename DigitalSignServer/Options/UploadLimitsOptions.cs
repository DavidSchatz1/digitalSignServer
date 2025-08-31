namespace DigitalSignServer.Options;
public sealed class UploadLimitsOptions
{
    public long MaxDocxBytes { get; set; } = 10 * 1024 * 1024; // default 10MB
}
