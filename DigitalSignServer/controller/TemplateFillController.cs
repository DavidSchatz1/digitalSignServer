using DigitalSignServer.Exceptions;
using DigitalSignServer.Reposetories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DigitalSignServer.dto;

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

        [HttpPost("{templateId:guid}/fill")]
        [Authorize(Roles = "Customer")]
        public async Task<IActionResult> Fill(Guid templateId, [FromBody] FillContractRequest req, CancellationToken ct)
        {
            var customerIdClaim = User.FindFirst("CustomerId")?.Value;
            if (!Guid.TryParse(customerIdClaim, out var customerId)) return Forbid();

            try
            {
                var result = await _repo.FillAndRenderAsync(
                    templateId,
                    customerId,
                    req.Values ?? new Dictionary<string, string>(),
                    req.SignatureDelivery,                    // <-- חדש: מעבירים הלאה
                    ct);

                return Ok(new
                {
                    instanceId = result.InstanceId,
                    docxKey = result.S3KeyDocx,
                    pdfKey = result.S3KeyPdf
                });
            }
            catch (TemplateFillValidationException ex)
            {
                return UnprocessableEntity(new
                {
                    error = "MissingReplacements",
                    message = "לא כל השדות מולאו במסמך.",
                    missingKeys = ex.MissingKeys,
                    seenButNoMatch = ex.NoMatchSeen
                });
            }
        }
    }
}
