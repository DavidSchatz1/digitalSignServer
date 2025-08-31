using Syncfusion.DocIO;
using Syncfusion.DocIO.DLS;

namespace DigitalSignServer.Services;

public sealed class TemplateFieldDetector
{
    public async Task<List<DetectedField>> DetectContentControlsAsync(Stream docxStream, CancellationToken ct)
    {
        if (docxStream == null) throw new ArgumentNullException(nameof(docxStream));

        using var ms = new MemoryStream();
        await docxStream.CopyToAsync(ms, ct);
        ms.Position = 0;

        var found = new List<(string tag, string? title)>();

        using (var document = new WordDocument(ms, FormatType.Docx))
        {
            // סריקה של כל הסקשנים/גוף המסמך
            foreach (WSection section in document.Sections)
            {
                if (section?.Body is ICompositeEntity body)
                {
                    TraverseComposite(body, found);
                }
            }
        }

        // המרה ל-DetectedField + דה-דופ לפי Tag (Key)
        var result = found
            .Where(x => !string.IsNullOrWhiteSpace(x.tag))
            .GroupBy(x => x.tag.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var first = g.First();
                var key = first.tag.Trim();
                var label = string.IsNullOrWhiteSpace(first.title) ? key : first.title!.Trim();
                return new DetectedField
                {
                    Key = key,
                    Label = label,
                    DetectedFrom = "contentControl",
                    Type = "text",
                    IsRequired = false
                };
            })
            .ToList();

        for (int i = 0; i < result.Count; i++)
            result[i].Order = i + 1;

        return result;
    }

    /// <summary>
    /// יורד רקורסיבית בישויות מרכיבות (ICompositeEntity) ואוסף ContentControls.
    /// </summary>
    private static void TraverseComposite(ICompositeEntity composite, List<(string tag, string? title)> sink)
    {
        var children = composite.ChildEntities; // שים לב: Property, לא מתודה
        for (int i = 0; i < children.Count; i++)
        {
            var entity = children[i];

            // Inline Content Control
            if (entity is InlineContentControl ic)
            {
                var props = ic.ContentControlProperties;
                sink.Add((props?.Tag ?? string.Empty, props?.Title));

                // גם ל-inline יש ChildEntities (התוכן הפנימי) — נרד פנימה
                if (ic is ICompositeEntity icComp)
                    TraverseComposite(icComp, sink);

                continue;
            }

            // Block Content Control
            if (entity is BlockContentControl bc)
            {
                var props = bc.ContentControlProperties;
                sink.Add((props?.Tag ?? string.Empty, props?.Title));

                if (bc is ICompositeEntity bcComp)
                    TraverseComposite(bcComp, sink);

                continue;
            }

            // פסקה — מרכיב מורכב
            if (entity is WParagraph p)
            {
                TraverseComposite(p, sink);
                continue;
            }

            // טבלה → תאים (כל תא הוא ICompositeEntity)
            if (entity is WTable table)
            {
                foreach (WTableRow row in table.Rows)
                    foreach (WTableCell cell in row.Cells)
                        TraverseComposite(cell, sink);

                continue;
            }

            // אפשר להוסיף כאן תמיכה ב־TextBox/Shapes/HeadersFooters בהמשך — כרגע לא דרוש
        }
    }
}

public sealed class DetectedField
{
    public string Key { get; set; } = default!;
    public string Label { get; set; } = default!;
    public string DetectedFrom { get; set; } = "contentControl";
    public bool IsRequired { get; set; }
    public int Order { get; set; }
    public string Type { get; set; } = "text";
}
