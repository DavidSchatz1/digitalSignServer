using DigitalSignServer.context;
using DigitalSignServer.models;
using DigitalSignServer.Storage;
using Microsoft.EntityFrameworkCore;
using Syncfusion.DocIO;
using Syncfusion.DocIO.DLS;
using Syncfusion.DocIORenderer;
using Syncfusion.Pdf;

namespace DigitalSignServer.Reposetories
{
    public sealed class TemplateFillRepository : ITemplateFillRepository
    {
        private readonly AppDbContext _db;
        private readonly IFileStorage _storage;

        public TemplateFillRepository(AppDbContext db, IFileStorage storage)
        {
            _db = db;
            _storage = storage;
        }

        public async Task<TemplateFillResult> FillAndRenderAsync(
            Guid templateId,
            Guid customerId,
            IDictionary<string, string> values,
            CancellationToken ct)
        {
            // 1) אימות בעלות וטעינת התבנית
            var template = await _db.Templates
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == templateId && t.CustomerId == customerId, ct)
                ?? throw new KeyNotFoundException("Template not found or not owned by this customer.");

            // 2) פתיחת המסמך מה-S3
            await using var originalStream = await _storage.OpenReadAsync(template.S3Key, ct);
            using var ms = new MemoryStream();
            await originalStream.CopyToAsync(ms, ct);
            ms.Position = 0;

            // עכשיו זה בטוח ל-DocIO
            using var doc = new WordDocument(ms, FormatType.Docx);

            // 3) מילוי Content Controls לפי Tag
            ApplyValuesToContentControls(doc, values ?? new Dictionary<string, string>());

            // 4) שמירת DOCX/‏PDF ל-S3
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

            using (var renderer = new DocIORenderer())
            using (PdfDocument pdf = renderer.ConvertToPDF(doc))
            using (var outPdf = new MemoryStream())
            {
                pdf.Save(outPdf);
                outPdf.Position = 0;
                await _storage.SaveAsync(outPdf, filledPdfKey, "application/pdf", ct);
            }

            // 5) כתיבת רשומת מופע (אם יש ישות כזו אצלך)
            var now = DateTimeOffset.UtcNow;
            _db.TemplateInstances.Add(new TemplateInstance
            {
                Id = instanceId,
                TemplateId = templateId,
                CustomerId = customerId,
                S3KeyDocx = filledDocxKey,
                S3KeyPdf = filledPdfKey,
                CreatedAt = now,
                UpdatedAt = now,
                Status = "PdfReady"
            });
            await _db.SaveChangesAsync(ct);

            return new TemplateFillResult
            {
                InstanceId = instanceId,
                S3KeyDocx = filledDocxKey,
                S3KeyPdf = filledPdfKey
            };
        }

        private static void ApplyValuesToContentControls(WordDocument document, IDictionary<string, string> values)
        {
            if (values.Count == 0) return;

            foreach (WSection section in document.Sections)
            {
                if (section?.Body is ICompositeEntity body)
                    ReplaceInComposite(body, values);
            }
        }

        private static void ReplaceInComposite(ICompositeEntity composite, IDictionary<string, string> values)
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
                        if (ic is ICompositeEntity icComp)
                        {
                            icComp.ChildEntities.Clear();
                            icComp.ChildEntities.Add(new WTextRange(ic.Document) { Text = val ?? string.Empty });
                        }
                    }
                    if (ic is ICompositeEntity icNest) ReplaceInComposite(icNest, values);
                    continue;
                }

                if (entity is BlockContentControl bc)
                {
                    var tag = bc.ContentControlProperties?.Tag;
                    if (!string.IsNullOrWhiteSpace(tag) && values.TryGetValue(tag, out var val))
                    {
                        bc.TextBody.ChildEntities.Clear();
                        var paragraph = new WParagraph(bc.Document);
                        paragraph.ChildEntities.Add(new WTextRange(bc.Document) { Text = val ?? string.Empty });
                        bc.TextBody.ChildEntities.Add(paragraph);
                    }
                    ReplaceInComposite(bc.TextBody, values);
                    continue;
                }

                if (entity is WParagraph par)
                {
                    ReplaceInComposite(par, values);
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
}
