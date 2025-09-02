namespace DigitalSignServer.models
{
    public sealed class TemplateSignatureAnchor
    {
        public Guid Id { get; set; }
        public Guid TemplateId { get; set; }
        public string Tag { get; set; } = "";     // לדוגמה: "SIGN", "SIGN:client|w=0.25|h=0.08"
        public int Order { get; set; }            // סדר הופעה במסמך
        public string? Meta { get; set; }         // עותק מלא של ה-Tag לצורך פרסום/ניתוח
        public DateTimeOffset CreatedAt { get; set; }
    }
}
