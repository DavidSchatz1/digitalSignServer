namespace DigitalSignServer.Options
{
    // options/EmailOptions.cs
    public sealed class EmailOptions
    {
        public string Host { get; set; } = default!;
        public int Port { get; set; } = 587;
        public string From { get; set; } = default!;
        public string? DisplayName { get; set; }
        public string? User { get; set; }
        public string? Pass { get; set; }
    }

    // options/PublicOptions.cs
    public sealed class PublicOptions
    {
        public string WebBaseUrl { get; set; } = "https://localhost:5173";
    }

}
