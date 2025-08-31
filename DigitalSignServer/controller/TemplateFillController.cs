using DigitalSignServer.Reposetories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DigitalSignServer.Controllers
{
    [Route("api/templates")]
    [ApiController]
    public class TemplateFillController : ControllerBase
    {
        private readonly ITemplateFillRepository _repo;

        public TemplateFillController(ITemplateFillRepository repo)
        {
            _repo = repo;
        }

        public sealed class FillContractRequest
        {
            public Dictionary<string, string> Values { get; set; } = new();
        }

        [HttpPost("{templateId:guid}/fill")]
        [Authorize(Roles = "Customer")]
        public async Task<IActionResult> Fill(Guid templateId, [FromBody] FillContractRequest req, CancellationToken ct)
        {
            var customerIdClaim = User.FindFirst("CustomerId")?.Value;
            if (!Guid.TryParse(customerIdClaim, out var customerId)) return Forbid();

            var result = await _repo.FillAndRenderAsync(templateId, customerId, req.Values ?? new(), ct);
            return Ok(new { instanceId = result.InstanceId, docxKey = result.S3KeyDocx, pdfKey = result.S3KeyPdf });
        }
    }
}
