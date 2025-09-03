namespace DigitalSignServer.dto;

public sealed class ClientInfoDto
{
    public string? UserAgent { get; set; }
    public string? Platform { get; set; }   // navigator.platform
    public string? Language { get; set; }   // navigator.language
    public string? Timezone { get; set; }   // Intl.DateTimeFormat().resolvedOptions().timeZone
    public string? Screen { get; set; }   // "1920x1080"
    public int? TouchPoints { get; set; } // navigator.maxTouchPoints
}

