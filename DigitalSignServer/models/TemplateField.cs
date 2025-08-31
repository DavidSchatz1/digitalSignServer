using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace DigitalSignServer.Models;

public class TemplateField
{
    [Key]
    public Guid Id { get; set; }


    public Guid TemplateId { get; set; }

    [JsonIgnore]
    public Template Template { get; set; } = default!;


    [Required]
    public string Key { get; set; } = default!; // e.g., ClientName


    public string Label { get; set; } = string.Empty; // editable in mapping screen


    public string Type { get; set; } = "text"; // future: enum


    public bool IsRequired { get; set; }


    public int Order { get; set; }


    public string? DefaultValue { get; set; }


    public string DetectedFrom { get; set; } = "auto"; // mergefield / {{ }} / sdt
}
