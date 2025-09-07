using DigitalSignServer.dto;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DigitalSignServer.Reposetories
{
    public sealed class TemplateFillResult
    {
        public Guid InstanceId { get; set; }
        public string S3KeyDocx { get; set; } = default!;
        public string S3KeyPdf { get; set; } = default!;

        // חדשים – יאוישו כשלא שולחים מייל
        public string? SignUrl { get; set; }
        public string? Otp { get; set; }
        public string? InviteToken { get; set; }
        public bool? RequiresPassword { get; set; }
        public DateTime? InviteExpiresAt { get; set; }
    }

    public interface ITemplateFillRepository
    {
        Task<TemplateFillResult> FillAndRenderAsync(
            Guid templateId,
            Guid customerId,
            IDictionary<string, string> values,
            SignatureDeliveryRequest? signatureDelivery,
            CancellationToken ct);
    }
}
