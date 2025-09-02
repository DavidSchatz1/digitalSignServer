namespace DigitalSignServer.dto
{
    public class SignatureDeliveryRequest
    {
        public string Channel { get; set; } = "Email";            // כרגע רק Email
        public string? SignerName { get; set; }
        public string? SignerEmail { get; set; }
    }
}
