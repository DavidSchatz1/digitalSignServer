namespace DigitalSignServer.dto
{
    public class FillContractRequest
    {
        public Dictionary<string, string> Values { get; set; } = new();
        public SignatureDeliveryRequest? SignatureDelivery { get; set; }
    }
}
