namespace DigitalSignServer.models
{
    // models/SignatureInvite.cs
    public class SignatureInvite
    {
        public Guid Id { get; set; }
        public Guid TemplateInstanceId { get; set; }
        public TemplateInstance TemplateInstance { get; set; } = default!;

        public string Token { get; set; } = default!;            // URL-safe, unique
        public string OtpHash { get; set; } = default!;          // BCrypt/Argon2
        public DateTime OtpExpiresAt { get; set; }
        public bool RequiresPassword { get; set; } = true;

        public string DeliveryChannel { get; set; } = "Email";    // Email
        public string? RecipientEmail { get; set; }               // required

        public DateTime ExpiresAt { get; set; }                  // link validity
        public int MaxUses { get; set; } = 1;
        public int Uses { get; set; } = 0;
        public string Status { get; set; } = "Pending";          // Pending/Open/Signed/Expired/Revoked

        public string? SignerName { get; set; }
        public string? SignerEmail { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? OpenedAt { get; set; }
        public DateTime? SignedAt { get; set; }
    }

    // models/SignatureDelivery.cs  (אופציונלי אבל מומלץ ללוג משלוח)
    public class SignatureDelivery
    {
        public Guid Id { get; set; }
        public Guid InviteId { get; set; }
        public SignatureInvite Invite { get; set; } = default!;
        public string Channel { get; set; } = "Email";
        public string Status { get; set; } = "Sent";             // Sent/Failed
        public string? ProviderMessageId { get; set; }
        public DateTime AttemptedAt { get; set; }
        public string? Error { get; set; }
    }

}
