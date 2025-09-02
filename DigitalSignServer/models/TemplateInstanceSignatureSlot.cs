namespace DigitalSignServer.models
{
    public sealed class TemplateInstanceSignatureSlot
    {
        public Guid Id { get; set; }
        public Guid TemplateInstanceId { get; set; }
        public string SlotKey { get; set; } = "";   // למשל "client.1"
        public int PageIndex { get; set; }
        public double X { get; set; }               // 0..1 (שמאל-עליון)
        public double Y { get; set; }               // 0..1
        public double W { get; set; }               // 0..1
        public double H { get; set; }               // 0..1
        public int Order { get; set; }              // סדר חתימה רצוי
        public DateTimeOffset CreatedAt { get; set; }
    }
}

