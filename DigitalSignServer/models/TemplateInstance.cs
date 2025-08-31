namespace DigitalSignServer.models
{
    public class TemplateInstance
    {
        public Guid Id { get; set; }
        public Guid TemplateId { get; set; }
        public Guid CustomerId { get; set; }

        public string S3KeyDocx { get; set; } = default!;
        public string S3KeyPdf { get; set; } = default!;   // ימולא אחרי המרה
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public string Status { get; set; } = "Filling";     // Filling -> Filled -> PdfReady
    }

}
