namespace DigitalSignServer.Options;
public sealed class S3Options
{
    public string? Bucket { get; set; }
    public string? Region { get; set; }
    public string? BasePrefix { get; set; }
}
