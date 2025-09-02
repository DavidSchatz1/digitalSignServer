using DigitalSignServer.context;
using DigitalSignServer.models;
using DigitalSignServer.Models;
using DigitalSignServer.Storage;
using DigitalSignServer.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;


namespace DigitalSignServer.Services;

public sealed class TemplateService
{
    private readonly IFileStorage _storage;
    private readonly AppDbContext _db;
    private readonly ILogger<TemplateService> _log;



    public TemplateService(IFileStorage storage, AppDbContext db, ILogger<TemplateService> log)
    {
        _storage = storage; _db = db; _log = log;
    }


    public async Task<Template> UploadAsync(Guid customerId, string originalFileName, string mimeType, long fileLength, Stream fileStream, CancellationToken ct)
    {
        var templateId = Guid.NewGuid();
        var key = $"templates/{customerId}/{templateId}/original.docx";
        _log.LogInformation("Upload start: cid={Cid} tid={Tid} fname={File} len={Len} key={Key}", customerId, templateId, originalFileName, fileLength, key);


        // Compute hash (optional but recommended). We copy to a MemoryStream to both hash and upload.
        using var ms = new MemoryStream();
        await fileStream.CopyToAsync(ms, ct);
        ms.Position = 0;
        var sha256 = Hashing.ComputeSha256(ms);
        ms.Position = 0;


        // 1) upload to S3
        await _storage.SaveAsync(ms, key, mimeType, ct);


        // 2) insert DB row
        var now = DateTimeOffset.UtcNow;
        var entity = new Template
        {
            Id = templateId,
            CustomerId = customerId,
            OriginalFileName = originalFileName,
            S3Key = key,
            MimeType = mimeType,
            FileSizeBytes = fileLength,
            Sha256 = sha256,
            CreatedAt = now,
            UpdatedAt = now,
            Status = "Uploaded"
        };

        //
        _db.Templates.Add(entity);
        var rows = await _db.SaveChangesAsync(ct);
        _log.LogInformation("DB SaveChanges rows={Rows} templateId={Tid}", rows, entity.Id);
        //

        try
        {
            await _db.SaveChangesAsync(ct);

        }
        catch
        {
            await _storage.DeleteAsync(key, ct);
            throw;
        }


        return entity;
    }


    public Task<List<Template>> GetByCustomerAsync(Guid customerId, CancellationToken ct)
    => _db.Templates.Where(t => t.CustomerId == customerId)
    .OrderByDescending(t => t.CreatedAt)
    .ToListAsync(ct);


    public async Task<(Template template, Stream stream)> OpenForDownloadAsync(Guid templateId, CancellationToken ct)
    {
        var t = await _db.Templates.FirstOrDefaultAsync(x => x.Id == templateId, ct)
        ?? throw new KeyNotFoundException("Template not found");
        var s = await _storage.OpenReadAsync(t.S3Key, ct);
        return (t, s);
    }


    public async Task DeleteAsync(Guid templateId, CancellationToken ct)
    {
        var t = await _db.Templates.FirstOrDefaultAsync(x => x.Id == templateId, ct)
        ?? throw new KeyNotFoundException("Template not found");
        await _storage.DeleteAsync(t.S3Key, ct);
        _db.Templates.Remove(t);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<List<TemplateField>> DetectAndSaveFieldsFromContentControlsAsync(Guid templateId, CancellationToken ct)
    {
        // משוך את ה-DOCX מ-S3
        var (t, stream) = await OpenForDownloadAsync(templateId, ct);

        // זיהוי: שדות רגילים + עוגני SIGN (Tag="SIGN")
        var detector = new TemplateFieldDetector();
        var detect = await detector.DetectFieldsAndAnchorsAsync(stream, ct);

        // ----- שמירת TemplateFields -----
        var existingFields = await _db.TemplateFields
            .Where(f => f.TemplateId == templateId)
            .ToListAsync(ct);
        _db.TemplateFields.RemoveRange(existingFields);
        await _db.SaveChangesAsync(ct);

        var toInsertFields = detect.Fields.Select(f => new TemplateField
        {
            Id = Guid.NewGuid(),
            TemplateId = templateId,
            Key = f.Key.Trim(),
            Label = string.IsNullOrWhiteSpace(f.Label) ? f.Key : f.Label.Trim(),
            Type = string.IsNullOrWhiteSpace(f.Type) ? "text" : f.Type,
            IsRequired = f.IsRequired,
            Order = f.Order,
            DetectedFrom = f.DetectedFrom
        }).ToList();

        _db.TemplateFields.AddRange(toInsertFields);

        // ----- שמירת TemplateSignatureAnchors (רק SIGN) -----
        var existingAnchors = await _db.TemplateSignatureAnchors
            .Where(a => a.TemplateId == templateId)
            .ToListAsync(ct);
        _db.TemplateSignatureAnchors.RemoveRange(existingAnchors);

        var toInsertAnchors = detect.Anchors.Select(a => new TemplateSignatureAnchor
        {
            Id = Guid.NewGuid(),
            TemplateId = templateId,
            Tag = "SIGN", // שומרים אחיד
            Order = a.Order,
            Meta = null,
            CreatedAt = DateTimeOffset.UtcNow
        }).ToList();

        if (toInsertAnchors.Count > 0)
            _db.TemplateSignatureAnchors.AddRange(toInsertAnchors);

        // ----- עדכון סטטוס בתבנית -----
        t.Status = "FieldsDetected";
        t.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);

        return toInsertFields;
    }


}