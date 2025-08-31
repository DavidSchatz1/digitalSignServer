using System.ComponentModel.DataAnnotations;

namespace DigitalSignServer.models
{
    public class Customer
    {
        public Guid Id { get; set; }
        public string Password { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    }
}