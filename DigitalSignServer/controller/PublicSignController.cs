using DigitalSignServer.context;
using DigitalSignServer.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using System.Text;
using Syncfusion.Pdf.Graphics;
using Syncfusion.Pdf.Parsing;
using System.Text.RegularExpressions;
using Syncfusion.Pdf;
using System.Text.RegularExpressions;
using DigitalSignServer.models;
using DigitalSignServer.services;
using System.Net;
using DigitalSignServer.Utils;
using DigitalSignServer.dto;
using DigitalSignServer.Services;
using Syncfusion.Pdf.Security;
using Syncfusion.Pdf.Interactive;


namespace DigitalSignServer.controller
{
    // controllers/PublicSignController.cs
    [ApiController]
    [Route("api/sign")]
    public class PublicSignController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IFileStorage _storage;
        private readonly IPasswordHasher<object> _passwordHasher;
        private readonly INotificationService _notifier;
        private readonly ILogger<PublicSignController> _logger;
        private readonly ISigningCertProvider _signingCertProvider;


        public PublicSignController(AppDbContext db, IFileStorage storage, INotificationService notifier, ILogger<PublicSignController> logger, ISigningCertProvider signingCertProvider, IPasswordHasher<object> passwordHasher)
        {
            _db = db;
            _storage = storage;
            _notifier = notifier;
            _logger = logger;
            _signingCertProvider = signingCertProvider;
            _passwordHasher = passwordHasher; //make sure this doesnt break anything
        }

        [HttpGet("{token}/bootstrap")]
        [AllowAnonymous]
        public async Task<IActionResult> Bootstrap(string token, CancellationToken ct)
        {
            var inv = await _db.SignatureInvites
                .Include(i => i.TemplateInstance)
                .FirstOrDefaultAsync(i => i.Token == token, ct);
            if (inv is null || inv.Status is "Signed" || inv.ExpiresAt < DateTime.UtcNow)
                return NotFound();

            if (inv.Status == "Pending") { inv.Status = "Opened"; inv.OpenedAt = DateTime.UtcNow; await _db.SaveChangesAsync(ct); }

            var inst = inv.TemplateInstance!;
            var slots = await _db.TemplateInstanceSignatureSlots
                .Where(s => s.TemplateInstanceId == inst.Id)
                .OrderBy(s => s.Order)
                .Select(s => new {
                    key = s.SlotKey,
                    s.PageIndex,
                    x = s.X,
                    y = s.Y,
                    w = s.W,
                    h = s.H,
                    s.Order
                })
                .ToListAsync(ct);
           

            return Ok(new
            {
                requiresPassword = inv.RequiresPassword,
                pdfStreamUrl = $"/api/sign/{token}/pdf",
                signerName = inv.SignerName,
                expiresAt = inv.ExpiresAt,
                slots
            });
        }

        public sealed class VerifyOtpReq
        {
            public string Otp { get; set; } = default!;
            public ClientInfoDto? ClientInfo { get; set; }
        }

        [HttpPost("{token}/verify-otp")]
        [AllowAnonymous]
        public async Task<IActionResult> VerifyOtp(string token, [FromBody] VerifyOtpReq req, CancellationToken ct)
        {
            var inv = await _db.SignatureInvites.FirstOrDefaultAsync(i => i.Token == token, ct);
            if (inv is null || inv.ExpiresAt < DateTime.UtcNow) return NotFound();

            if (inv.OtpExpiresAt < DateTime.UtcNow)
                return UnprocessableEntity(new { error = "OtpExpired" });

            var provided = (req?.Otp ?? "").Trim();

            if (!VerifyOtpAgainstStored(provided, inv.OtpHash))
                return Unauthorized(new { error = "OtpInvalid" });

            Response.Cookies.Append($"sign_{token}_ok", "1", new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Expires = DateTimeOffset.UtcNow.AddMinutes(20)
            });

            await LogAuditAsync(inv, "OtpVerified", req?.ClientInfo, ct);

            return Ok();
        }

        // ========= helpers =========
        private static bool VerifyOtpAgainstStored(string otp, string stored)
        {
            if (string.IsNullOrWhiteSpace(otp) || string.IsNullOrWhiteSpace(stored))
                return false;

            // פורמט מותאם: "saltBase64:hashBase64"
            var colon = stored.IndexOf(':');
            if (colon > 0)
            {
                var saltB64 = stored.Substring(0, colon);
                var hashB64 = stored.Substring(colon + 1);
                try
                {
                    var salt = Convert.FromBase64String(saltB64);
                    var expected = Convert.FromBase64String(hashB64);

                    using var sha = SHA256.Create();
                    var otpBytes = Encoding.UTF8.GetBytes(otp);
                    var data = new byte[salt.Length + otpBytes.Length];
                    Buffer.BlockCopy(salt, 0, data, 0, salt.Length);
                    Buffer.BlockCopy(otpBytes, 0, data, salt.Length, otpBytes.Length);
                    var actual = sha.ComputeHash(data);

                    return CryptographicOperations.FixedTimeEquals(actual, expected);
                }
                catch
                {
                    return false;
                }
            }
            return false;
        }


        [HttpGet("{token}/pdf")]
        [AllowAnonymous]
        public async Task<IActionResult> Pdf(string token, CancellationToken ct)
        {
            if (!Request.Cookies.TryGetValue($"sign_{token}_ok", out var ok) || ok != "1")
                return Unauthorized();

            var inv = await _db.SignatureInvites.Include(i => i.TemplateInstance)
                .FirstOrDefaultAsync(i => i.Token == token, ct);
            if (inv is null || inv.ExpiresAt < DateTime.UtcNow) return NotFound();

            var key = inv.TemplateInstance.S3KeyPdf!;
            var stream = await _storage.OpenReadAsync(key, ct);
            return File(stream, "application/pdf");
        }


    [HttpPost("{token}/submit")]
    [AllowAnonymous]
    public async Task<IActionResult> Submit(string token, [FromBody] SubmitReq req, CancellationToken ct)
    {
        // 1) טעינת הזמנה + ולידציות בסיסיות
        var invite = await _db.SignatureInvites
            .Include(i => i.TemplateInstance)
            .FirstOrDefaultAsync(i => i.Token == token, ct);

        if (invite is null || invite.ExpiresAt < DateTime.UtcNow)
            return NotFound();

        // דרישה: ה-OTP אומת (קוקי קצר טווח שהוגדר ב-verify-otp)
        if (!Request.Cookies.TryGetValue($"sign_{token}_ok", out var ok) || ok != "1")
            return Unauthorized(new { error = "OtpNotVerified" });

        if (invite.Status == "Signed")
            return UnprocessableEntity(new { error = "AlreadySigned" });

        var instance = invite.TemplateInstance ?? throw new InvalidOperationException("TemplateInstance not found.");
        if (string.IsNullOrWhiteSpace(instance.S3KeyPdf))
            return NotFound(new { error = "PdfNotFound" });

        // 2) קריאת ה-PDF המקורי ל-MemoryStream עצמאי
        byte[] pdfBytes;
        await using (var s3In = await _storage.OpenReadAsync(instance.S3KeyPdf, ct))
               
        using (var msIn = new MemoryStream())
        {
            await s3In.CopyToAsync(msIn, ct);
            pdfBytes = msIn.ToArray();
        }

        // 3) טעינת המסמך מערך הבייטים (בלי להשאיר תלות ב-Stream חיצוני)
        using var loadedDoc = new PdfLoadedDocument(new MemoryStream(pdfBytes));

        // 4) קביעת יעדי החתימה (targets) לפי סלוטים/ידני
        var slotsForInstance = await _db.TemplateInstanceSignatureSlots
            .Where(s => s.TemplateInstanceId == instance.Id)
            .OrderBy(s => s.Order)
            .ToListAsync(ct);

        var targets = new List<(int page, double x, double y, double w, double h)>();

        if (slotsForInstance.Count > 0)
        {
            // יש סלוטים – חובה לבחור אחד/כולם
            if (req.ApplyAllSlots == true)
            {
                targets.AddRange(slotsForInstance.Select(s => (s.PageIndex, s.X, s.Y, s.W, s.H)));
            }
            else if (!string.IsNullOrWhiteSpace(req.SlotKey))
            {
                var s = slotsForInstance.FirstOrDefault(x => x.SlotKey.Equals(req.SlotKey, StringComparison.OrdinalIgnoreCase));
                if (s == null) return UnprocessableEntity(new { error = "UnknownSlotKey" });
                targets.Add((s.PageIndex, s.X, s.Y, s.W, s.H));
            }
            else
            {
                return UnprocessableEntity(new { error = "SlotRequired", message = "יש לבחור סלוט חתימה או 'חתימה בכל הסלוטים'." });
            }
        }
        else
        {
            // אין סלוטים – מאפשרים fallback ידני (כפי שהיה)
            if (req.PageIndex is null || req.X is null || req.Y is null || req.Width is null || req.Height is null)
                return UnprocessableEntity(new { error = "ManualCoordinatesRequired" });

            targets.Add((req.PageIndex.Value, req.X.Value, req.Y.Value, req.Width.Value, req.Height.Value));
        }

        // 5) המרת חתימת ה-PNG מ-Data URL → bytes (פעם אחת)
        if (string.IsNullOrWhiteSpace(req.SignatureImageBase64))
            return UnprocessableEntity(new { error = "BadSignatureImage" });

        var sigDataUrl = req.SignatureImageBase64;
        var m = Regex.Match(sigDataUrl, @"^data:image\/png;base64,(.+)$", RegexOptions.IgnoreCase);
        var b64 = m.Success ? m.Groups[1].Value : sigDataUrl; // תומך גם במקרה שנשלחה מחרוזת base64 חשופה

        byte[] sigBytes;
        try
        {
            sigBytes = Convert.FromBase64String(b64);
        }
        catch
        {
            return UnprocessableEntity(new { error = "BadSignatureImage" });
        }

            // 6) ציור החתימה/שם/זמן על כל היעדים
            foreach (var t in targets)
            {
                var pageIndex = Math.Clamp(t.page, 0, loadedDoc.Pages.Count - 1);
                var page = (PdfLoadedPage)loadedDoc.Pages[pageIndex];

                var pageW = page.Size.Width;
                var pageH = page.Size.Height;

                // יחסיים (0..1) → נקודות PDF
                var drawW = Math.Max(1, t.w * pageW);
                var drawH = Math.Max(1, t.h * pageH);

                var left = Math.Clamp(t.x * pageW, 0, pageW - drawW);
                // yTop הוא יחס מהחלק העליון; הופכים לתחתית:
                var yTop = t.y * pageH;
                var bottom = Math.Clamp(pageH - (yTop + drawH), 0, pageH - drawH);

                var rect = new Syncfusion.Drawing.RectangleF((float)left, (float)bottom, (float)drawW, (float)drawH);

                // ציור החתימה (כל יעד מקבל stream חדש מהבייטים)
                using (var bmpStream = new MemoryStream(sigBytes, writable: false))
                {
                    var bmp = new PdfBitmap(bmpStream);
                    page.Graphics.DrawImage(bmp, rect);
                }

                // טקסט שם/זמן (אופציונלי)
                if (req.DrawName || req.DrawTimestamp)
                {
                    var font = new PdfStandardFont(PdfFontFamily.Helvetica, 10f, PdfFontStyle.Regular);
                    var brush = PdfBrushes.Black;

                    float textY = rect.Y - 12f; // שורה אחת מעל החתימה
                    if (textY < 0) textY = rect.Bottom + 2f; // אם אין מקום מעל, מציירים מתחת

                    var parts = new List<string>();
                    if (req.DrawName && !string.IsNullOrWhiteSpace(invite.SignerName))
                        parts.Add(invite.SignerName!);

                    if (req.DrawTimestamp)
                    {
                        var stamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'");
                        parts.Add(stamp);
                    }

                    if (parts.Count > 0)
                    {
                        var text = string.Join("  |  ", parts);
                        page.Graphics.DrawString(text, font, brush, new Syncfusion.Drawing.PointF(rect.X, textY));
                    }
                }
            }

            // === חתימה דיגיטלית בסיסית (Invisible) — לפני שלב 7 ===
            try
            {
                // שולף את התעודה עם המפתח הפרטי מספק החתימות שלך
                var x509 = _signingCertProvider.GetCertificate();

                // יוצר PdfCertificate ישירות מ-X509Certificate2
                var pdfCert = new PdfCertificate(x509);

                // חותם בעמוד הראשון (החתימה בלתי-נראית: Bounds 0x0)
                var firstPage = (PdfLoadedPage)loadedDoc.Pages[0];
                var signature = new PdfSignature(loadedDoc, firstPage, pdfCert, "DigitalSignature");

                // החתימה לא מצוירת על הדף (בלתי-נראית), אך נוכחת בקריפטוגרפיה ונועלת את המסמך
                signature.Bounds = new Syncfusion.Drawing.RectangleF(0, 0, 0, 0);

                // (אופציונלי) פרטי Reason/Location/Contact
                signature.Reason = "Digitally signed by the signer";
                signature.LocationInfo = "IL";
                signature.ContactInfo = invite.RecipientEmail ?? invite.SignerEmail;

                // לא נוגעים כרגע ב-Settings (Digest/CAdES) כדי להימנע מתלויות גרסה — ברירת המחדל היא SHA-256.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to attach digital signature; proceeding without DS.");
                // בפרודקשן אפשר לשקול להפוך את זה ל-blocking אם נדרש שנעילה תהיה חובה.
            }


            // 7) שמירה החוצה ל־MemoryStream חדש → העלאה ל-S3
            byte[] signedPdfBytes;
            using (var msOut = new MemoryStream())
            {
                loadedDoc.Save(msOut); // בשמירה מוטבעת החתימה הדיגיטלית
                signedPdfBytes = msOut.ToArray();
            }

            var signedKey = $"{Path.GetDirectoryName(instance.S3KeyPdf)!.Replace('\\', '/')}/signed.pdf";
            await using (var up = new MemoryStream(signedPdfBytes, writable: false))
            {
                await _storage.SaveAsync(up, signedKey, "application/pdf", ct);
            }

            //לעת הצורך - אני משאיר כאן קוד מתאים לשילוב של תמונה בתוך החתימה הדיגטלית - מתאים להכנסת לוגו במקום חתימה בלתי נראית
            // using למעלה:
            //using Syncfusion.Pdf.Security;
            //using Syncfusion.Pdf.Graphics;
            //using System.Security.Cryptography.X509Certificates;
            //using System.Drawing; // ל-SizeF/PointF במידת הצורך

            //try
            //{
            //    var x509 = _signingCertProvider.GetCertificate();
            //    var pdfCert = new PdfCertificate(x509);

            //    var firstPage = (PdfLoadedPage)loadedDoc.Pages[0];

            //    // גודל/מיקום בפיקסלים של PDF (נקודות). ממקמים בפינה הימנית-תחתונה עם שוליים.
            //    const float boxW = 120f;   // ~4.2 ס״מ
            //    const float boxH = 40f;    // ~1.4 ס״מ
            //    const float margin = 18f;  // 0.25"

            //    var pageW = firstPage.Size.Width;
            //    var pageH = firstPage.Size.Height;

            //    // חישוב פינה ימנית-תחתונה
            //    var x = pageW - boxW - margin;
            //    var y = margin; // ב-Syncfusion מקור הצירים בתחתית: y קטן = קרוב לתחתית
            //    var bounds = new Syncfusion.Drawing.RectangleF(x, y, boxW, boxH);

            //    // חתימה דיגיטלית עם הופעה נראית
            //    var signature = new PdfSignature(loadedDoc, firstPage, pdfCert, "DigitalSignature");
            //    signature.Bounds = bounds;

            //    // פרטים (לא חובה)
            //    signature.Reason = "Digitally signed by the signer";
            //    signature.LocationInfo = "IL";
            //    signature.ContactInfo = invite.RecipientEmail ?? invite.SignerEmail;

            //    // Appearance: ציור עדין עם שקיפות ולוגו/טקסט
            //    var g = signature.Appearance.Normal.Graphics;

            //    // שקיפות כוללת (0..1)
            //    g.SetTransparency(0.35f);

            //    // רקע עדין
            //    g.DrawRectangle(PdfPens.Gray, PdfBrushes.WhiteSmoke, bounds);

            //    // נסה לצייר לוגו אם יש (למשל PNG קטן שנשלף מס3/תיקיית משאבים)
            //    // אם אין לך עכשיו לוגו — בטל את הבלוק הזה ויישאר טקסט בלבד.
            //    try
            //    {
            //        // דוגמה: לוגו מתוך byte[] logoBytes
            //        // byte[] logoBytes = await _storage.ReadAllBytesAsync("branding/signature-stamp.png", ct);
            //        // using var logoMs = new MemoryStream(logoBytes);
            //        // var logo = new PdfBitmap(logoMs);
            //        // float imgH = bounds.Height - 8f, imgW = imgH; // ריבוע קטן
            //        // g.DrawImage(logo, bounds.X + 4f, bounds.Y + 4f, imgW, imgH);
            //    }
            //    catch { /* לוגו לא קריטי */ }

            //    // טקסט “Digitally signed”
            //    var font = new PdfStandardFont(PdfFontFamily.Helvetica, 9f, PdfFontStyle.Regular);
            //    var brush = PdfBrushes.Black;

            //    // מקם טקסט משמאל לאזור הלוגו (או מתחילת הריבוע אם אין לוגו)
            //    float textLeft = bounds.X + 8f; // אם ציירת לוגו, תן מרווח גדול יותר, למשל + (imgW + 8f)
            //    float textTop = bounds.Y + (bounds.Height / 2f) - 6f;

            //    g.DrawString("Digitally signed", font, brush, new Syncfusion.Drawing.PointF(textLeft, textTop));

            //    // אם תרצה להוסיף תאריך קצר בתוך ההופעה (ויזואלי בלבד):
            //    // var ts = DateTime.UtcNow.ToString("yyyy-MM-dd");
            //    // g.DrawString(ts, font, PdfBrushes.DarkGray, new Syncfusion.Drawing.PointF(textLeft, textTop - 12f));

            //    // מחזיר שקיפות לברירת מחדל לשאר ציורים עתידיים (לא חובה כאן)
            //    g.SetTransparency(1f);
            //}
            //catch (Exception ex)
            //{
            //    _logger.LogError(ex, "Failed to create visible digital signature; proceeding without DS.");
            //}


            // 8) עדכון סטטוס המופע
            instance.Status = "Signed";
            instance.SignedPdfS3Key = signedKey;
            instance.SignedAt = DateTime.UtcNow;
            instance.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
            await LogAuditAsync(invite, "SignatureSubmitted", req.ClientInfo, ct);


            // 9) שליחת מייל ליעדי החתימה
            instance = invite.TemplateInstance!;
            signedKey = instance.SignedPdfS3Key;
            if (string.IsNullOrWhiteSpace(signedKey))
                return NotFound(new { error = "SignedPdfNotFound" });

            // נטען את ה-PDF לבייטים (כדי לצרף כקובץ)
            byte[] signedBytes;
            await using (var s = await _storage.OpenReadAsync(signedKey, ct))
            using (var ms = new MemoryStream())
            {
                await s.CopyToAsync(ms, ct);
                signedBytes = ms.ToArray();
            }

            // מיילים יעד
            var signerEmail = invite.SignerEmail?.Trim();
            var customerEmail = await _db.customers
                .Where(c => c.Id == instance.CustomerId)
                .Select(c => c.Email)
                .FirstOrDefaultAsync(ct);

            var auditHtml = BuildAudit.BuildSignatureAuditHtml(invite, instance);
            var clientInfoHtml = await ClientEvidenceHelper.BuildClientInfoHtmlAsync(_db, invite, ct);


            // בונים נושא וגוף (פשוטים; תוכל לייפות)
            var subject = "המסמך החתום שלך";
            var bodySigner = $@"
                <html><body style=""font-family:Arial,Helvetica,sans-serif;direction:rtl"">
                    <p>שלום{(string.IsNullOrWhiteSpace(invite.SignerName) ? "" : " " + WebUtility.HtmlEncode(invite.SignerName))},</p>
                    <p>המסמך נחתם בהצלחה. מצורף קובץ PDF חתום.</p>
                    <p style=""color:#777"">לשאלות ותמיכה פנה/י לשולח המסמך.</p>
                    </body></html>";

                                var bodySender = $@"
                    <html><body style=""font-family:Arial,Helvetica,sans-serif;direction:rtl"">
                    <p>שלום,</p>
                    <p>המסמך שנשלח לחתימה נחתם בהצלחה. מצורף ה-PDF החתום.</p>
                     {auditHtml}
                      <!-- טבלת פרטי סביבת החותם -->
                      {clientInfoHtml}
                    </body>
                </html>";

            // מצרף (שם קובץ ידידותי):
            var attachment = new EmailAttachment(
                FileName: $"signed-{instance.Id}.pdf",
                ContentType: "application/pdf",
                Content: signedBytes
            );

            // שולחים לחותם (אם יש מייל)
            try
            {
                if (!string.IsNullOrWhiteSpace(signerEmail))
                {
                    await _notifier.SendEmailAsync(signerEmail!, subject, bodySigner, new[] { attachment }, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed sending signed PDF to signer {Email}", signerEmail);
                // לא מפילים את ה-API על זה; החתימה הצליחה והקובץ נשמר.
            }

            // שולחים לשולח (הלקוח)
            try
            {
                if (!string.IsNullOrWhiteSpace(customerEmail))
                {
                    await _notifier.SendEmailAsync(customerEmail!, subject, bodySender, new[] { attachment }, ct);
                    _logger.LogInformation("Signed PDF email sent to sender {Email}", customerEmail);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed sending signed PDF to sender {Email}", customerEmail);
            }


            // ===== מחיקת הקבצים מה-S3: filled.docx, filled.pdf, signed.pdf =====
            var keysToDelete = new List<string>();

            if (!string.IsNullOrWhiteSpace(instance.S3KeyDocx))
                keysToDelete.Add(instance.S3KeyDocx!);      // filled.docx

            if (!string.IsNullOrWhiteSpace(instance.S3KeyPdf))
                keysToDelete.Add(instance.S3KeyPdf!);       // filled.pdf

            if (!string.IsNullOrWhiteSpace(signedKey))
                keysToDelete.Add(signedKey);                // signed.pdf (כרגע שמור; מוחקים מיידית)

            foreach (var key in keysToDelete)
            {
                try
                {
                    await _storage.DeleteAsync(key, ct);
                    _logger.LogInformation("Deleted S3 object: {Key}", key);
                }
                catch (Exception ex)
                {
                    // לא מפילים את ההליך — רק לוג ברמת Warning
                    _logger.LogWarning(ex, "Failed to delete S3 object: {Key}", key);
                }
            }
       
            instance.SignedPdfS3Key = null;

            // אפשרות: עדכן סטטוס סופי שמרמז שאין קבצים שמורים (אופציונלי)
            instance.Status = "Completed";
            instance.UpdatedAt = DateTimeOffset.UtcNow;

            await _db.SaveChangesAsync(ct);

            _db.SignatureAuditEvents.Add(new SignatureAuditEvent
            {
                Id = Guid.NewGuid(),
                InviteId = invite.Id,
                Action = "FilesPurged",
                CreatedAt = DateTime.UtcNow,
            });
            var slots = await _db.TemplateInstanceSignatureSlots
                .Where(s => s.TemplateInstanceId == instance.Id)
                .ToListAsync(ct);
            if (slots.Count > 0)
            {
                _db.TemplateInstanceSignatureSlots.RemoveRange(slots);
            }
            await _db.SaveChangesAsync(ct);



            // תשובת ה-API (ללקוח ציבורי זה לא מציג לינק)
            return Ok(new { ok = true, signedKey = instance.SignedPdfS3Key });
        }

        //=========== helper - audit log =========
        private async Task LogAuditAsync(SignatureInvite invite, string action, ClientInfoDto? ci, CancellationToken ct)
        {
            // IP — מעדיפים X-Forwarded-For אם קיים
            var ip = HttpContext.Request.Headers.TryGetValue("X-Forwarded-For", out var xff)
                ? xff.ToString().Split(',').FirstOrDefault()?.Trim()
                : HttpContext.Connection.RemoteIpAddress?.ToString();

            var ua = HttpContext.Request.Headers.UserAgent.ToString();

            var ev = new SignatureAuditEvent
            {
                Id = Guid.NewGuid(),
                InviteId = invite.Id,
                Action = action,
                IpAddress = ip,
                UserAgent = string.IsNullOrWhiteSpace(ci?.UserAgent) ? ua : ci!.UserAgent,
                Platform = ci?.Platform,
                Language = ci?.Language,
                Timezone = ci?.Timezone,
                Screen = ci?.Screen,
                TouchPoints = ci?.TouchPoints,
                CreatedAt = DateTimeOffset.UtcNow
            };

            _db.SignatureAuditEvents.Add(ev);
            await _db.SaveChangesAsync(ct);
        }


    }
}
