using Syncfusion.DocIO;
using Syncfusion.DocIO.DLS;
using DigitalSignServer.Utils;

namespace DigitalSignServer.Services;

public sealed class TemplateFieldDetector
{
    private static bool IsPureSignTag(string? tag)
    {
        var t = (tag ?? string.Empty).Trim();
        return t.Equals("SIGN", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<DetectResult> DetectFieldsAndAnchorsAsync(Stream docxStream, CancellationToken ct)
    {
        if (docxStream == null) throw new ArgumentNullException(nameof(docxStream));

        using var ms = new MemoryStream();
        await docxStream.CopyToAsync(ms, ct);
        ms.Position = 0;

        // איסוף גולמי
        var rawFields = new List<(string tag, string? title)>();
        var signAnchorsOrder = new List<int>(); // רק סדר (1..N)

        using (var document = new WordDocument(ms, FormatType.Docx))
        {
            int signCounter = 0;

            // גוף + כותרות/כותרות תחתונות
            foreach (WSection section in document.Sections)
            {
                // גוף המסמך
                if (section?.Body is ICompositeEntity body)
                    TraverseComposite(body, rawFields, ref signCounter, signAnchorsOrder);

                // headers/footers
                var hf = section?.HeadersFooters;
                if (hf != null)
                {
                    foreach (var part in new[] { hf.OddHeader, hf.OddFooter, hf.EvenHeader, hf.EvenFooter, hf.FirstPageHeader, hf.FirstPageFooter })
                        if (part is ICompositeEntity comp)
                            TraverseComposite(comp, rawFields, ref signCounter, signAnchorsOrder);
                }
            }
        }

        // שדות: קיבוץ לפי Tag (ללא SIGN), כמו קודם
        var fields = rawFields
            .Where(x => !string.IsNullOrWhiteSpace(x.tag) && !IsPureSignTag(x.tag))
            .GroupBy(x => x.tag.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select((g, idx) =>
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
                    IsRequired = false,
                    Order = idx + 1
                };
            })
            .ToList();

        // Anchors: לפי סדר הופעה (1..N)
        var anchors = signAnchorsOrder
            .Select(ord => new DetectedAnchor { Order = ord })
            .ToList();

        return new DetectResult
        {
            Fields = fields,
            Anchors = anchors
        };

    }

    /// <summary>
    /// יורד רקורסיבית ואוסף:
    /// - כל ה-CC לשדות (מלבד SIGN)
    /// - כל CC עם Tag="SIGN" רק כספירת Anchor (Order)
    /// </summary>
    private static void TraverseComposite(
        ICompositeEntity composite,
        List<(string tag, string? title)> fieldsSink,
        ref int signCounter,
        List<int> signOrderSink)
    {
        var children = composite.ChildEntities;
        for (int i = 0; i < children.Count; i++)
        {
            var entity = children[i];

            // Inline Content Control
            if (entity is InlineContentControl ic)
            {
                var props = ic.ContentControlProperties;
                var tag = props?.Tag ?? string.Empty;
                var title = props?.Title;

                if (IsPureSignTag(tag))
                {
                    // Anchor בלבד – לא מצרפים ל-fieldsSink
                    signOrderSink.Add(++signCounter);
                }
                else
                {
                    fieldsSink.Add((tag, title));
                }

                if (ic is ICompositeEntity icComp)
                    TraverseComposite(icComp, fieldsSink, ref signCounter, signOrderSink);

                continue;
            }

            // Block Content Control
            if (entity is BlockContentControl bc)
            {
                var props = bc.ContentControlProperties;
                var tag = props?.Tag ?? string.Empty;
                var title = props?.Title;

                if (IsPureSignTag(tag))
                {
                    signOrderSink.Add(++signCounter);
                }
                else
                {
                    fieldsSink.Add((tag, title));
                }

                if (bc is ICompositeEntity bcComp)
                    TraverseComposite(bcComp, fieldsSink, ref signCounter, signOrderSink);

                continue;
            }

            // פסקה
            if (entity is WParagraph p)
            {
                TraverseComposite(p, fieldsSink, ref signCounter, signOrderSink);
                continue;
            }

            // טבלה
            if (entity is WTable table)
            {
                foreach (WTableRow row in table.Rows)
                    foreach (WTableCell cell in row.Cells)
                        TraverseComposite(cell, fieldsSink, ref signCounter, signOrderSink);

                continue;
            }
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
public sealed class DetectResult
{
    public List<DetectedField> Fields { get; set; } = new();
    public List<DetectedAnchor> Anchors { get; set; } = new();
}

public sealed class DetectedAnchor
{
    public int Order { get; set; }   // סדר הופעה במסמך
}
