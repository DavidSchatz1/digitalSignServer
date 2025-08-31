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
    }

    public interface ITemplateFillRepository
    {
        Task<TemplateFillResult> FillAndRenderAsync(
            Guid templateId,
            Guid customerId,
            IDictionary<string, string> values,
            CancellationToken ct);
    }
}
