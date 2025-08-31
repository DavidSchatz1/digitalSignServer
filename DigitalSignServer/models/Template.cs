using DigitalSignServer.models;
using System.ComponentModel.DataAnnotations;


namespace DigitalSignServer.Models;


public class Template
{
    [Key]
    public Guid Id { get; set; }


    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = default!; // assuming you have a Customer entity


    [Required]
    public string OriginalFileName { get; set; } = default!;


    [Required]
    public string S3Key { get; set; } = default!;


    [Required]
    public string MimeType { get; set; } = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";


    public long FileSizeBytes { get; set; }


    public string? Sha256 { get; set; }


    public string Status { get; set; } = "Uploaded"; // Uploaded -> FieldsDetected -> Mapped


    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }


    public ICollection<TemplateField> Fields { get; set; } = new List<TemplateField>();
}
