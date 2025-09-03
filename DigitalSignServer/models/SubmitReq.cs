using DigitalSignServer.dto;

namespace DigitalSignServer.models
{
    public record SubmitReq
    {
        public string? SignatureImageBase64 { get; init; }

        // Fallback ידני (כשאין סלוטים)
        public int? PageIndex { get; init; }
        public double? X { get; init; }
        public double? Y { get; init; }
        public double? Width { get; init; }
        public double? Height { get; init; }

        // סלוטים (חדש)
        public string? SlotKey { get; init; }          // למשל "default.1"
        public bool? ApplyAllSlots { get; init; }       // true = כל הסלוטים

        public bool DrawName { get; init; } = true;
        public bool DrawTimestamp { get; init; } = true;
        public string? Tz { get; init; }

        public ClientInfoDto? ClientInfo { get; set; } 

    }

}
