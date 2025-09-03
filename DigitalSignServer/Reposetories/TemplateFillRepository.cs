using DigitalSignServer.context;
using DigitalSignServer.dto;
using DigitalSignServer.models;
using DigitalSignServer.Options;
using DigitalSignServer.services;
using DigitalSignServer.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Syncfusion.DocIO;
using Syncfusion.DocIO.DLS;
using Syncfusion.DocIORenderer;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Parsing;
using Syncfusion.Pdf.Graphics;
using Syncfusion.Drawing;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;

namespace DigitalSignServer.Reposetories
{
    public sealed class TemplateFillRepository : ITemplateFillRepository
    {
        private readonly AppDbContext _db;
        private readonly IFileStorage _storage;
        private readonly INotificationService _notifier;
        private readonly PublicOptions _public;
        private readonly ILogger<TemplateFillRepository> _logger;

        public TemplateFillRepository(
            AppDbContext db,
            IFileStorage storage,
            INotificationService notifier,
            IOptions<PublicOptions> pubOpt,
            ILogger<TemplateFillRepository> logger)
        {
            _db = db;
            _storage = storage;
            _notifier = notifier;
            _public = pubOpt.Value;
            _logger = logger;
        }

        public async Task<TemplateFillResult> FillAndRenderAsync(
            Guid templateId,
            Guid customerId,
            IDictionary<string, string> values,
            SignatureDeliveryRequest? signatureDelivery,
            CancellationToken ct)
        {
            // 1) אימות בעלות וטעינת התבנית
            var template = await _db.Templates
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == templateId && t.CustomerId == customerId, ct)
                ?? throw new KeyNotFoundException("Template not found or not owned by this customer.");

            // 2) פתיחת המסמך מה-S3 (הזרם אינו Seekable -> מעתיקים ל-MemoryStream)
            await using var originalStream = await _storage.OpenReadAsync(template.S3Key, ct);
            using var ms = new MemoryStream();
            await originalStream.CopyToAsync(ms, ct);
            ms.Position = 0;

            using var doc = new WordDocument(ms, FormatType.Docx);

            // === (3) בקרת איכות: השוואה לשדות המצופים ב-DB + מילוי עם דו"ח ===
            var expectedKeys = await _db.TemplateFields
                .Where(f => f.TemplateId == templateId)
                .Select(f => f.Key)
                .ToListAsync(ct);

            var expected = new HashSet<string>(expectedKeys.Select(NormalizeKey), StringComparer.OrdinalIgnoreCase);

            var summary = ApplyValuesToContentControls(doc, values ?? new Dictionary<string, string>());

            var missing = expected.Except(summary.AppliedKeys, StringComparer.OrdinalIgnoreCase).ToArray();
            if (missing.Length > 0)
                throw new DigitalSignServer.Exceptions.TemplateFillValidationException(missing, summary.NoMatchKeys);

            // 4) שמירת DOCX + יצירת PDF עם סלוטים (Token Injection → PDF → Find → Erase)
            var instanceId = Guid.NewGuid();
            var baseKey = $"templates/{template.CustomerId}/{template.Id}/filled/{instanceId}";
            var filledDocxKey = $"{baseKey}/filled.docx";
            var filledPdfKey = $"{baseKey}/filled.pdf";

            // 4.1 DOCX מקורי (ללא טוקנים) — נשמר כפי שהוא
            using (var outDocx = new MemoryStream())
            {
                doc.Save(outDocx, FormatType.Docx);
                outDocx.Position = 0;
                await _storage.SaveAsync(
                    outDocx,
                    filledDocxKey,
                    "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                    ct);
            }

            // 4.2 שליפת עוגני חתימה (SIGN) ששייכים לתבנית
            var anchors = await _db.TemplateSignatureAnchors
                .Where(a => a.TemplateId == templateId)
                .OrderBy(a => a.Order)
                .ToListAsync(ct);

            // 4.3 הפקת PDF מעותק DOCX שבו מוזרקים טוקנים במקום ה-SIGN CCs
            //     ואז חיפוש הטוקנים ב-PDF, חישוב קואורדינטות יחסיות, ניקוי ושמירה ל-S3
            List<SlotPlan> plans;
            List<FoundSlot> foundSlots;
            using (var msForPdf = new MemoryStream())
            {
                // שומרים את doc לזכרון ופותחים עותק חדש כדי לא לזהם את ה-DOCX ששמרנו
                doc.Save(msForPdf, FormatType.Docx);
                msForPdf.Position = 0;

                using var docForPdf = new WordDocument(msForPdf, FormatType.Docx);

                // הזרקת טוקנים לפי anchors (Tag="SIGN" בלבד)
                plans = InjectSignTokens(docForPdf, anchors);

                // המרה ל-PDF
                using var renderer = new DocIORenderer();
                using PdfDocument pdf = renderer.ConvertToPDF(docForPdf);

                // Serialize ה-PDF ל-MemoryStream כדי לפתוח אותו כ-PdfLoadedDocument (למציאת טקסט)
                using var pdfMs = new MemoryStream();
                pdf.Save(pdfMs);
                pdfMs.Position = 0;

                using var loaded = new PdfLoadedDocument(pdfMs);

                // דיפולטים גלובליים לגודל חתימה יחסית לעמוד
                const double DEFAULT_W = 0.15; // 25% מרוחב
                const double DEFAULT_H = 0.04; // 8% מגובה

                // חיפוש טוקנים, חישוב קואורדינטות יחסיות, ניקוי הטקסט מה-PDF
                foundSlots = FindAndEraseTokensInPdf(loaded, plans, DEFAULT_W, DEFAULT_H);

                // שמירת ה-PDF הנקי ל-S3
                using (var outPdf = new MemoryStream())
                {
                    loaded.Save(outPdf);
                    outPdf.Position = 0;
                    await _storage.SaveAsync(outPdf, filledPdfKey, "application/pdf", ct);
                }
            }

            // 5) כתיבת רשומת מופע
            var now = DateTimeOffset.UtcNow;
            var instance = new TemplateInstance
            {
                Id = instanceId,
                TemplateId = templateId,
                CustomerId = customerId,
                S3KeyDocx = filledDocxKey,
                S3KeyPdf = filledPdfKey,
                CreatedAt = now,
                UpdatedAt = now,
                Status = "PdfReady"
            };
            _db.TemplateInstances.Add(instance);
            await _db.SaveChangesAsync(ct);

            // 5.1 שמירת סלוטים (אם נמצאו)
            if (foundSlots.Count > 0)
            {
                var toAdd = foundSlots.Select(s => new TemplateInstanceSignatureSlot
                {
                    Id = Guid.NewGuid(),
                    TemplateInstanceId = instance.Id,
                    SlotKey = s.SlotKey,     // "default.1", "default.2", ...
                    PageIndex = s.PageIndex,
                    X = s.X,                 // 0..1 – שמאל/עליון יחסיים
                    Y = s.Y,
                    W = s.W,
                    H = s.H,
                    Order = s.Order,
                    CreatedAt = DateTimeOffset.UtcNow
                }).ToList();

                _db.TemplateInstanceSignatureSlots.AddRange(toAdd);
                await _db.SaveChangesAsync(ct);

                instance.Status = "PdfReadyWithSlots";
                instance.UpdatedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(ct);
            }

            // === (6) יצירת הזמנת חתימה ושליחה במייל רק לפי signatureDelivery ===
            if (signatureDelivery is null || !string.Equals(signatureDelivery.Channel, "Email", StringComparison.OrdinalIgnoreCase))
            {
                throw new DigitalSignServer.Exceptions.TemplateFillValidationException(
                    new[] { "signatureDelivery.channel" }, Array.Empty<string>());
            }

            var signerEmail = signatureDelivery.SignerEmail?.Trim();
            var signerName = signatureDelivery.SignerName?.Trim();

            if (string.IsNullOrWhiteSpace(signerEmail))
            {
                throw new DigitalSignServer.Exceptions.TemplateFillValidationException(
                    new[] { "signatureDelivery.signerEmail" }, Array.Empty<string>());
            }

            // יצירת token + OTP
            var token = CreateUrlToken(48);
            var otp = CreateNumericOtp(6);
            var (otpHash, _) = HashOtp(otp);

            var invite = new SignatureInvite
            {
                Id = Guid.NewGuid(),
                TemplateInstanceId = instance.Id,
                Token = token,
                OtpHash = otpHash,
                OtpExpiresAt = DateTime.UtcNow.AddMinutes(20),
                RequiresPassword = true,
                DeliveryChannel = "Email",
                RecipientEmail = signerEmail,
                SignerName = string.IsNullOrWhiteSpace(signerName) ? null : signerName,
                SignerEmail = signerEmail,
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                Status = "Pending",
                CreatedAt = DateTime.UtcNow
            };

            _db.SignatureInvites.Add(invite);

            instance.Status = "AwaitingSignature";
            instance.UpdatedAt = DateTimeOffset.UtcNow;

            await _db.SaveChangesAsync(ct);

            // שליחת מייל – אם נכשל, נזרוק חריגה
            var signLink = $"{_public.WebBaseUrl.TrimEnd('/')}/sign/{token}";
            var mailSubject = "מסמך מוכן לחתימה";
            var greeting = string.IsNullOrWhiteSpace(invite.SignerName) ? "" : $"שלום {invite.SignerName},<br/>";
            var mailHtml = $@"
                <html><body style=""font-family:Arial,Helvetica,sans-serif;font-size:14px;direction:rtl"">
                    {greeting}
                    <p>לחץ/י לקישור החתימה: <a href=""{WebUtility.HtmlEncode(signLink)}"">{WebUtility.HtmlEncode(signLink)}</a></p>
                    <p>קוד חד-פעמי (OTP): <b>{WebUtility.HtmlEncode(otp)}</b> (בתוקף 20 דקות)</p>
                    <p style=""color:#777"">אם לא ציפית לקבל הודעה זו - ניתן להתעלם ממנה.</p>
                </body></html>";

            _logger.LogInformation("Sending invite email to {Email}", invite.RecipientEmail);
            await _notifier.SendEmailAsync(invite.RecipientEmail!, mailSubject, mailHtml, ct);
            _logger.LogInformation("Invite email sent to {Email}", invite.RecipientEmail);

            // 7) החזרה
            return new TemplateFillResult
            {
                InstanceId = instanceId,
                S3KeyDocx = filledDocxKey,
                S3KeyPdf = filledPdfKey
            };
        }

        // ==========================
        // ===== Helper Methods =====
        // ==========================

        private static ReplacementSummary ApplyValuesToContentControls(
            Syncfusion.DocIO.DLS.WordDocument document,
            IDictionary<string, string> valuesRaw)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in valuesRaw)
                values[NormalizeKey(kv.Key)] = kv.Value ?? string.Empty;

            var summary = new ReplacementSummary();

            foreach (Syncfusion.DocIO.DLS.WSection section in document.Sections)
            {
                if (section?.Body is Syncfusion.DocIO.DLS.ICompositeEntity body)
                    ReplaceInComposite(body, values, summary);

                var hf = section?.HeadersFooters;
                if (hf != null)
                {
                    TryReplaceHeaderFooter(hf.OddHeader, values, summary);
                    TryReplaceHeaderFooter(hf.OddFooter, values, summary);
                    TryReplaceHeaderFooter(hf.EvenHeader, values, summary);
                    TryReplaceHeaderFooter(hf.EvenFooter, values, summary);
                    TryReplaceHeaderFooter(hf.FirstPageHeader, values, summary);
                    TryReplaceHeaderFooter(hf.FirstPageFooter, values, summary);
                }
            }

            return summary;
        }

        private static void ReplaceInComposite(
            Syncfusion.DocIO.DLS.ICompositeEntity composite,
            IDictionary<string, string> values,
            ReplacementSummary summary)
        {
            var children = composite.ChildEntities;
            for (int i = 0; i < children.Count; i++)
            {
                var entity = children[i];

                if (entity is Syncfusion.DocIO.DLS.InlineContentControl ic)
                {
                    summary.ControlsVisited++;
                    var rawTag = ic.ContentControlProperties?.Tag ?? string.Empty;
                    var tag = NormalizeKey(rawTag);

                    //  חדש: דילוג מוחלט על SIGN — לא נספר, לא מוחלף, לא NoMatch
                    if (IsPureSignTag(tag))
                    {
                        // לא לרדת פנימה ולא לגעת בתוכן — נשאיר לשלב הזרקת הטוקנים
                        if (ic is Syncfusion.DocIO.DLS.ICompositeEntity _) { /* לא מחליפים ולא סורקים פנימה */ }
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(tag))
                    {
                        summary.EncounteredKeys.Add(tag);
                        if (values.TryGetValue(tag, out var val))
                        {
                            var tr = new Syncfusion.DocIO.DLS.WTextRange(ic.Document) { Text = val ?? string.Empty };
                            children.RemoveAt(i);
                            children.Insert(i, tr);

                            summary.ControlsReplaced++;
                            summary.AppliedKeys.Add(tag);

                            // חשוב: לא לרדת פנימה ולא להמשיך לגעת ב-ic שהוסר
                            continue;
                        }
                        else
                        {
                            summary.NoMatchKeys.Add(tag);
                        }
                    }

                    if (ic is Syncfusion.DocIO.DLS.ICompositeEntity icNest)
                        ReplaceInComposite(icNest, values, summary);

                    continue;
                }

                if (entity is Syncfusion.DocIO.DLS.BlockContentControl bc)
                {
                    summary.ControlsVisited++;
                    var rawTag = bc.ContentControlProperties?.Tag ?? string.Empty;
                    var tag = NormalizeKey(rawTag);

                    if (IsPureSignTag(tag))
                    {
                        // לא לרדת פנימה — משאירים את ה־CC כפי שהוא לשלב הטוקנים
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(tag))
                    {
                        summary.EncounteredKeys.Add(tag);
                        if (values.TryGetValue(tag, out var val))
                        {
                            bc.TextBody.ChildEntities.Clear();
                            var par = new Syncfusion.DocIO.DLS.WParagraph(bc.Document);
                            par.ChildEntities.Add(new Syncfusion.DocIO.DLS.WTextRange(bc.Document) { Text = val ?? string.Empty });
                            bc.TextBody.ChildEntities.Add(par);
                            summary.ControlsReplaced++;
                            summary.AppliedKeys.Add(tag);
                        }
                        else
                        {
                            summary.NoMatchKeys.Add(tag);
                        }
                    }

                    ReplaceInComposite(bc.TextBody, values, summary);
                    continue;
                }

                if (entity is Syncfusion.DocIO.DLS.WParagraph p)
                {
                    ReplaceInComposite(p, values, summary);
                    continue;
                }

                if (entity is Syncfusion.DocIO.DLS.WTable table)
                {
                    foreach (Syncfusion.DocIO.DLS.WTableRow row in table.Rows)
                        foreach (Syncfusion.DocIO.DLS.WTableCell cell in row.Cells)
                            ReplaceInComposite(cell, values, summary);
                    continue;
                }
            }
        }

        private static string NormalizeKey(string s)
        {
            if (s == null) return string.Empty;

            // מסיר תווי כיווניות/בקרה נפוצים, וממיר NBSP לרווח רגיל
            var cleaned = s
                .Replace('\u00A0', ' ')   // NBSP
                .Replace("\u200E", "")    // LRM
                .Replace("\u200F", "")    // RLM
                .Replace("\u202A", "")    // LRE
                .Replace("\u202B", "")    // RLE
                .Replace("\u202C", "")    // PDF
                .Replace("\u202D", "")    // LRO
                .Replace("\u202E", "");   // RLO

            return cleaned.Trim();
        }


        private sealed class ReplacementSummary
        {
            public HashSet<string> AppliedKeys { get; } = new(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> EncounteredKeys { get; } = new(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> NoMatchKeys { get; } = new(StringComparer.OrdinalIgnoreCase);
            public int ControlsVisited { get; set; }
            public int ControlsReplaced { get; set; }
        }

        private static void TryReplaceHeaderFooter(
            Syncfusion.DocIO.DLS.HeaderFooter? part,
            IDictionary<string, string> values,
            ReplacementSummary summary)
        {
            if (part is Syncfusion.DocIO.DLS.ICompositeEntity tb)
                ReplaceInComposite(tb, values, summary);
        }

        private static string? TryGet(IDictionary<string, string> dict, string key)
            => dict.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

        // ===== OTP / Token Utilities =====

        private static string CreateUrlToken(int bytes = 48)
        {
            var buf = RandomNumberGenerator.GetBytes(bytes);
            return Convert.ToBase64String(buf).TrimEnd('=').Replace('+', '-').Replace('/', '_'); // Base64Url
        }

        private static string CreateNumericOtp(int digits = 6)
        {
            var min = (int)Math.Pow(10, digits - 1);
            var max = (int)Math.Pow(10, digits) - 1;
            var num = RandomNumberGenerator.GetInt32(min, max + 1);
            return num.ToString(CultureInfo.InvariantCulture);
        }

        // שומר בפורמט: "{saltBase64}:{hashBase64}"
        private static (string Hash, string Salt) HashOtp(string otp)
        {
            var salt = RandomNumberGenerator.GetBytes(16);
            using var sha = SHA256.Create();
            var data = new byte[salt.Length + System.Text.Encoding.UTF8.GetByteCount(otp)];
            Buffer.BlockCopy(salt, 0, data, 0, salt.Length);
            System.Text.Encoding.UTF8.GetBytes(otp, 0, otp.Length, data, salt.Length);
            var hash = sha.ComputeHash(data);
            var packed = $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
            return (packed, Convert.ToBase64String(salt));
        }

        // ===== Signature Slots: Token Injection & PDF Search =====

        // ===== Token builder (נשאר כמו שהיה) =====
        private static string BuildSignToken(string slotKey, Guid guid)
            => $"§§SIGN::{slotKey}::{guid:N}§§";

        // תכנית הזרקה (לכל Anchor נוצרת תכנית slotKey + token)
        private sealed record SlotPlan(string SlotKey, string Token, int Order);

        // === גרסה חדשה: InjectSignTokens שמזריקה גם ל-Block וגם ל-Inline ===
        private static List<SlotPlan> InjectSignTokens(WordDocument docForPdf, IEnumerable<TemplateSignatureAnchor> anchors)
        {
            var plans = new List<SlotPlan>();
            int running = 0;

            // בונים את רשימת התכניות לפי Order של העוגנים
            foreach (var a in anchors.OrderBy(x => x.Order))
            {
                var slotKey = $"default.{++running}";
                var token = BuildSignToken(slotKey, Guid.NewGuid());
                plans.Add(new SlotPlan(slotKey, token, a.Order));
            }

            if (plans.Count == 0)
                return plans;

            // סורקים את כל ה-CC במסמך (Inline + Block), לפי סדר הופעה, ומחליפים ל-SIGN הראשונים
            int applied = 0;
            foreach (var (ctrl, rawTag, isBlock) in EnumerateSignControls(docForPdf))
            {
                if (applied >= plans.Count) break;

                if (IsPureSignTag(rawTag))
                {
                    var plan = plans[applied++];
                    ReplaceContentControlWithToken(ctrl, isBlock, plan.Token);
                }
            }

            return plans;
        }

        // בדיקה פשוטה לתגית SIGN
        private static bool IsPureSignTag(string? tag)
        {
            var t = (tag ?? string.Empty).Trim();
            return t.Equals("SIGN", StringComparison.OrdinalIgnoreCase);
        }

        // הלפר להחלפה בפועל של התוכן לטקסט הטוקן
        private static void ReplaceContentControlWithToken(Entity ctrl, bool isBlock, string token)
        {
            if (isBlock)
            {
                var bc = (BlockContentControl)ctrl;
                // מנקים את הגוף ומכניסים פסקה עם טקסט הטוקן
                bc.TextBody.ChildEntities.Clear();
                var par = new WParagraph(bc.Document);
                par.ChildEntities.Add(new WTextRange(bc.Document) { Text = token });
                bc.TextBody.ChildEntities.Add(par);
            }
            else
            {
                var ic = (InlineContentControl)ctrl;
                if (ic is ICompositeEntity comp)
                {
                    comp.ChildEntities.Clear();
                    comp.ChildEntities.Add(new WTextRange(ic.Document) { Text = token });
                }
                else
                {
                    // fall-back נדיר: אם משום מה לא קומפוזיט, ננסה להכניס לפסקה חדשה מסביב
                    // (אמור כמעט לא לקרות ב-Syncfusion)
                    var par = new WParagraph(ic.Document);
                    par.ChildEntities.Add(new WTextRange(ic.Document) { Text = token });
                    // אם יש לך הקשר של ההורה—אפשר להחליף את ה-inline כולו; אחרת נתעלם.
                }
            }
        }

        // === גרסה חדשה: Enumerator שמחזיר גם Inline וגם Block ===
        private static IEnumerable<(Entity Ctrl, string RawTag, bool IsBlock)> EnumerateSignControls(WordDocument doc)
        {
            foreach (WSection sec in doc.Sections)
            {
                if (sec?.Body is ICompositeEntity body)
                {
                    foreach (var t in EnumerateComposite(body))
                        yield return t;
                }

                var hf = sec.HeadersFooters;
                if (hf != null)
                {
                    foreach (var part in new[] { hf.OddHeader, hf.OddFooter, hf.EvenHeader, hf.EvenFooter, hf.FirstPageHeader, hf.FirstPageFooter })
                    {
                        if (part is ICompositeEntity comp)
                        {
                            foreach (var t in EnumerateComposite(comp))
                                yield return t;
                        }
                    }
                }
            }

            // יורד רקורסיבית על כל הישויות, ומחזיר גם Inline וגם Block + tag + האם זה Block
            static IEnumerable<(Entity, string, bool)> EnumerateComposite(ICompositeEntity comp)
            {
                var children = comp.ChildEntities;
                for (int i = 0; i < children.Count; i++)
                {
                    var ent = children[i];

                    if (ent is InlineContentControl ic)
                    {
                        var tag = ic.ContentControlProperties?.Tag ?? string.Empty;
                        yield return (ic, tag, false);

                        if (ic is ICompositeEntity nest)
                        {
                            foreach (var t in EnumerateComposite(nest))
                                yield return t;
                        }
                    }
                    else if (ent is BlockContentControl bc)
                    {
                        var tag = bc.ContentControlProperties?.Tag ?? string.Empty;
                        // חשוב: כאן **מוסיפים** את ה-Block עצמו לרשימה (לא רק יורדים פנימה)
                        yield return (bc, tag, true);

                        // וגם נרד פנימה כדי לאסוף CCs מקוננים
                        foreach (var t in EnumerateComposite(bc.TextBody))
                            yield return t;
                    }
                    else if (ent is WParagraph p)
                    {
                        if (p is ICompositeEntity pi)
                        {
                            foreach (var t in EnumerateComposite(pi))
                                yield return t;
                        }
                    }
                    else if (ent is WTable tbl)
                    {
                        foreach (WTableRow r in tbl.Rows)
                            foreach (WTableCell c in r.Cells)
                                foreach (var t in EnumerateComposite(c))
                                    yield return t;
                    }
                }
            }
        }

        private sealed record FoundSlot(string SlotKey, int PageIndex, double X, double Y, double W, double H, int Order);

        // חיפוש טוקנים ב-PDF, חישוב קואורדינטות יחסיות (0..1), ניקוי הטקסט
        private static List<FoundSlot> FindAndEraseTokensInPdf(
    PdfLoadedDocument loaded,
    IEnumerable<SlotPlan> plans,
    double defaultW,
    double defaultH)
        {
            var res = new List<FoundSlot>();

            foreach (var plan in plans)
            {
                // מחפש את הטקסט במסמך
                if (!loaded.FindText(plan.Token, out Dictionary<int, List<Syncfusion.Drawing.RectangleF>> matches)
                    || matches.Count == 0)
                    continue;

                foreach (var kv in matches)
                {
                    var pageIndex = kv.Key;
                    if (pageIndex < 0 || pageIndex >= loaded.Pages.Count) continue;

                    var page = (PdfLoadedPage)loaded.Pages[pageIndex];
                    var pw = page.Size.Width;
                    var ph = page.Size.Height;

                    foreach (var r in kv.Value)
                    {
                        // r כבר Syncfusion.Drawing.RectangleF
                        // “מחיקה” ויזואלית של הטוקן (כיסוי בלבן)
                        page.Graphics.DrawRectangle(PdfBrushes.White, r);

                        // חישוב קואורדינטות יחסיות
                        var x = r.X / pw;
                        var yTop = (r.Y + r.Height) / ph;
                        var y = 1.0 - yTop;

                        res.Add(new FoundSlot(
                            SlotKey: plan.SlotKey,
                            PageIndex: pageIndex,
                            X: Clamp01(x),
                            Y: Clamp01(y),
                            W: Clamp01(defaultW),
                            H: Clamp01(defaultH),
                            Order: plan.Order
                        ));
                    }
                }
            }

            return res;

            static double Clamp01(double v) => Math.Max(0, Math.Min(1, v));
        }
    }
}
