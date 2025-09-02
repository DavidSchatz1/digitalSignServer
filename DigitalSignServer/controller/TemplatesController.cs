using System.ComponentModel.DataAnnotations;
using DigitalSignServer.context;
using DigitalSignServer.models;
using DigitalSignServer.Models;
using DigitalSignServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;


namespace DigitalSignServer.Controllers;


[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Customer")]
public class TemplatesController : ControllerBase
{
    private readonly TemplateService _svc;
    private readonly long _maxBytes;
    private readonly AppDbContext _db;


    public TemplatesController(TemplateService svc, AppDbContext db, Microsoft.Extensions.Options.IOptions<DigitalSignServer.Options.UploadLimitsOptions> uploadOpts)
    {
        _svc = svc;
        _maxBytes = uploadOpts.Value.MaxDocxBytes;
        _db = db;
    }


    private static bool IsDocx(string fileName, string contentType)
    {
        var okExt = Path.GetExtension(fileName).Equals(".docx", StringComparison.OrdinalIgnoreCase);
        var okCt = string.Equals(contentType, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", StringComparison.OrdinalIgnoreCase)
        || string.Equals(contentType, "application/octet-stream", StringComparison.OrdinalIgnoreCase); // some browsers send octet-stream
        return okExt && okCt;
    }
    [HttpPost("upload")]
    [Authorize(Roles = "Admin")]
    [RequestFormLimits(MultipartBodyLengthLimit = 50_000_000)]
    [RequestSizeLimit(50_000_000)]
    public async Task<IActionResult> Upload([FromQuery, Required] Guid customerId, [FromForm(Name = "file")] IFormFile file, CancellationToken ct)
    {
        if (file == null || file.Length == 0) return BadRequest("No file uploaded");
        if (file.Length > _maxBytes) return BadRequest($"File too large. Max {_maxBytes} bytes");
        if (!IsDocx(file.FileName, file.ContentType)) return BadRequest("Only .docx files are allowed");


        await using var fs = file.OpenReadStream();
        var result = await _svc.UploadAsync(customerId, file.FileName, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", file.Length, fs, ct);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }


    // List by customer (Admin or the owning Customer via separate endpoint)
    [HttpGet]
    public async Task<IActionResult> List([FromQuery, Required] Guid customerId, CancellationToken ct)
    {
        // NOTE: Enforce that Customers can only query their own ID if you expose this to them.
        var items = await _svc.GetByCustomerAsync(customerId, ct);
        return Ok(items);
    }

    // Convenience for logged-in Customer to list their own templates (requires CustomerId claim)
    [HttpGet("mine")]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> ListMine(CancellationToken ct)
    {
        var customerIdClaim = User.FindFirst("CustomerId")?.Value;
        if (!Guid.TryParse(customerIdClaim, out var customerId)) return Forbid();
        var items = await _svc.GetByCustomerAsync(customerId, ct);
        return Ok(items);
    }


    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        // You can expand to include Fields etc.
        var (t, _) = await _svc.OpenForDownloadAsync(id, ct);
        return Ok(t);
    }


    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        var (t, stream) = await _svc.OpenForDownloadAsync(id, ct);


        // Authorization: Admin always ok; Customer only if owns it (CustomerId claim)
        if (User.IsInRole("Customer"))
        {
            var customerIdClaim = User.FindFirst("CustomerId")?.Value;
            if (!Guid.TryParse(customerIdClaim, out var customerId) || customerId != t.CustomerId)
                return Forbid();
        }


        return File(stream, t.MimeType, fileDownloadName: t.OriginalFileName);
    }


    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _svc.DeleteAsync(id, ct);
        return NoContent();
    }

    public sealed class CommitFieldsResponse
    {
        public List<TemplateField> Fields { get; set; } = new();
    }

    [HttpPost("{id:guid}/detect-and-save")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DetectAndSave(Guid id, CancellationToken ct)
    {
        var saved = await _svc.DetectAndSaveFieldsFromContentControlsAsync(id, ct);

        var dto = saved.Select(f => new TemplateFieldDto(
            f.Id, f.Key, f.Label, f.Type, f.IsRequired, f.Order, f.DetectedFrom)).ToList();

        var keys = dto.Select(f => f.Key).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        return Ok(new
        {
            count = dto.Count,
            keys,
            fields = dto
        });
    }

    [HttpGet("{id:guid}/fields")]
    [Authorize(Roles = "Admin,Customer")]
    public async Task<IActionResult> GetFields(Guid id, CancellationToken ct)
    {
        var fields = await _db.TemplateFields
            .Where(f => f.TemplateId == id)
            .OrderBy(f => f.Order)
            .Select(f => new TemplateFieldDto(
                f.Id, f.Key, f.Label, f.Type, f.IsRequired, f.Order, f.DetectedFrom))
            .ToListAsync(ct);

        return Ok(fields);
    }
}
