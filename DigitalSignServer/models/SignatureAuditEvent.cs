using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DigitalSignServer.models;

public class SignatureAuditEvent
{
    [Key] public Guid Id { get; set; }
    [Required] public Guid InviteId { get; set; }
    [ForeignKey(nameof(InviteId))] public SignatureInvite Invite { get; set; } = default!;

    // מה קרה?
    [Required, MaxLength(64)] public string Action { get; set; } = default!;
    // דוגמאות: "InviteSent", "LinkOpened", "OtpVerified", "SignatureSubmitted"

    // נתוני שרת/לקוח
    [MaxLength(64)] public string? IpAddress { get; set; }
    [MaxLength(512)] public string? UserAgent { get; set; }

    [MaxLength(64)] public string? Platform { get; set; }    // e.g. Win32
    [MaxLength(16)] public string? Language { get; set; }    // e.g. he-IL
    [MaxLength(64)] public string? Timezone { get; set; }    // e.g. Asia/Jerusalem
    [MaxLength(32)] public string? Screen { get; set; }      // e.g. 1920x1080
    public int? TouchPoints { get; set; }                     // e.g. 0/5

    // GeoIP (אופציונלי, עתידי — כרגע יכול להישאר null)
    [MaxLength(64)] public string? GeoCountry { get; set; }
    [MaxLength(64)] public string? GeoCity { get; set; }

    // לכל מקרה שתרצה לדחוף payload קטן בפורמט JSON
    public string? ExtraJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

