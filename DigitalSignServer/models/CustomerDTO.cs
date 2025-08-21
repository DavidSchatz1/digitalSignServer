namespace DigitalSignServer.models
{
    public class CustomerDTO
    {
        public int Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
