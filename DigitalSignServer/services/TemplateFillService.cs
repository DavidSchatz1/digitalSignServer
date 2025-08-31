using Syncfusion.DocIO;
using Syncfusion.DocIO.DLS;
using Syncfusion.DocIORenderer;
using Syncfusion.Pdf;
using Microsoft.EntityFrameworkCore;
using DigitalSignServer.context;
using DigitalSignServer.models;
using DigitalSignServer.Storage;

namespace DigitalSignServer.Services;

public sealed class TemplateFillService
{
    private readonly AppDbContext _db;
    private readonly IFileStorage _storage;

    public TemplateFillService(AppDbContext db, IFileStorage storage)
    {
        _db = db;
        _storage = storage;
    }

    public sealed class FillRequest
    {
        public Guid TemplateId { get; set; }
        public Guid CustomerId { get; set; } // ולידציה שהלקוח הזה הוא בעל התבנית/מורשה
        public Dictionary<string, string> Values { get; set; } = new(); // key => value
    }

    public sealed class FillResult
    {
        public Guid InstanceId { get; set; }
        public string S3KeyDocx { get; set; } = default!;
        public string S3KeyPdf { get; set; } = default!;
    }

    public async Task<FillResult> FillAndRenderAsync(FillRequest req, CancellationToken ct)
    {
        // 1) טען את התבנית וה-DOCX המקורי
        var template = await _db.Templates
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == req.TemplateId && t.CustomerId == req.CustomerId, ct)
            ?? throw new KeyNotFoundException("Template not found or not owned by this customer.");

        // פתח Stream מה-S3
        await using var originalStream = await _storage.OpenReadAsync(template.S3Key, ct);

        // 2) טען את ה-WordDocument
        using var doc = new WordDocument(originalStream, FormatType.Docx);

        // 3) מלא Content Controls לפי Tag (ה-Key)
        ApplyValuesToContentControls(doc, req.Values);

        // 4) שמור DOCX ממולא ל-S3
        var instanceId = Guid.NewGuid();
        var baseKey = $"templates/{template.CustomerId}/{template.Id}/filled/{instanceId}";
        var filledDocxKey = $"{baseKey}/filled.docx";
        var filledPdfKey = $"{baseKey}/filled.pdf";

        using (var outDocx = new MemoryStream())
        {
            doc.Save(outDocx, FormatType.Docx);
            outDocx.Position = 0;
            await _storage.SaveAsync(outDocx, filledDocxKey,
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document", ct);
        }

        // 5) המר ל-PDF ושמור ל-S3
        using (var renderer = new DocIORenderer())
        using (PdfDocument pdf = renderer.ConvertToPDF(doc))
        using (var outPdf = new MemoryStream())
        {
            pdf.Save(outPdf);
            outPdf.Position = 0;
            await _storage.SaveAsync(outPdf, filledPdfKey, "application/pdf", ct);
        }

        // 6) רשומת DB של המופע
        var now = DateTimeOffset.UtcNow;
        var inst = new TemplateInstance
        {
            Id = instanceId,
            TemplateId = req.TemplateId,
            CustomerId = req.CustomerId,
            S3KeyDocx = filledDocxKey,
            S3KeyPdf = filledPdfKey,
            CreatedAt = now,
            UpdatedAt = now,
            Status = "PdfReady"
        };
        _db.Set<TemplateInstance>().Add(inst);
        await _db.SaveChangesAsync(ct);

        return new FillResult
        {
            InstanceId = instanceId,
            S3KeyDocx = filledDocxKey,
            S3KeyPdf = filledPdfKey
        };
    }

    /// <summary>
    /// ממלא Content Controls לפי Tag (Key) בערכי מילוי.
    /// עובד גם ל-Inline וגם ל-Block.
    /// </summary>
    private static void ApplyValuesToContentControls(WordDocument document, Dictionary<string, string> values)
    {
        if (values == null || values.Count == 0) return;

        foreach (WSection section in document.Sections)
        {
            if (section?.Body is ICompositeEntity body)
                ReplaceInComposite(body, values);
        }
    }

    private static void ReplaceInComposite(ICompositeEntity composite, Dictionary<string, string> values)
    {
        var children = composite.ChildEntities;
        for (int i = 0; i < children.Count; i++)
        {
            var entity = children[i];

            if (entity is InlineContentControl ic)
            {
                var tag = ic.ContentControlProperties?.Tag;
                if (!string.IsNullOrWhiteSpace(tag) && values.TryGetValue(tag, out var val))
                {
                    // ננקה את התכולה הפנימית של ה-content control ונכניס טקסט אחד:
                    if (ic is ICompositeEntity icComp)
                    {
                        icComp.ChildEntities.Clear();
                        var tr = new WTextRange(ic.Document) { Text = val ?? string.Empty };
                        // Inline control מכיל לרוב פסקה פנימית/פריטים — נוסיף ישירות את ה-TextRange
                        icComp.ChildEntities.Add(tr);
                    }
                }
                // המשך רקורסיה כדי לתפוס CCs מקוננים
                if (ic is ICompositeEntity icCompNest)
                    ReplaceInComposite(icCompNest, values);
                continue;
            }

            if (entity is BlockContentControl bc)
            {
                var tag = bc.ContentControlProperties?.Tag;
                if (!string.IsNullOrWhiteSpace(tag) && values.TryGetValue(tag, out var val))
                {
                    bc.TextBody.ChildEntities.Clear();
                    var par = new WParagraph(bc.Document);
                    var tr = new WTextRange(bc.Document) { Text = val ?? string.Empty };
                    par.ChildEntities.Add(tr);
                    bc.TextBody.ChildEntities.Add(par);
                }
                ReplaceInComposite(bc.TextBody, values);
                continue;
            }

            if (entity is WParagraph p)
            {
                ReplaceInComposite(p, values);
                continue;
            }

            if (entity is WTable table)
            {
                foreach (WTableRow row in table.Rows)
                    foreach (WTableCell cell in row.Cells)
                        ReplaceInComposite(cell, values);
                continue;
            }
        }
    }
}
