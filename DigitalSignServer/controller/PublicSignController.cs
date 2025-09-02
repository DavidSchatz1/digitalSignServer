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
using DocumentFormat.OpenXml.VariantTypes;
using Syncfusion.Pdf;
using System.Text.RegularExpressions;
using DigitalSignServer.models;


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

        public PublicSignController(
            AppDbContext db,
            IFileStorage storage,
            IPasswordHasher<object> passwordHasher)
        {
            _db = db;
            _storage = storage;
            _passwordHasher = passwordHasher;
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

        public record VerifyOtpReq(string Otp);

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
                    var tz = req.Tz ?? "UTC";
                    // לשלב הבא: המרת TZ אמיתית; לעת עתה UTC
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

        // 7) שמירה החוצה ל־MemoryStream חדש → העלאה ל-S3
        byte[] signedPdfBytes;
        using (var msOut = new MemoryStream())
        {
            loadedDoc.Save(msOut);
            signedPdfBytes = msOut.ToArray();
        }

        var signedKey = $"{Path.GetDirectoryName(instance.S3KeyPdf)!.Replace('\\', '/')}/signed.pdf";
        await using (var up = new MemoryStream(signedPdfBytes, writable: false))
        {
            await _storage.SaveAsync(up, signedKey, "application/pdf", ct);
        }

        // 8) עדכון סטטוס המופע
        instance.Status = "Signed";
        instance.SignedPdfS3Key = signedKey;
        instance.SignedAt = DateTime.UtcNow;
        instance.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(new { ok = true, signedKey });
    }


    //public record SubmitReq(
    //        string SignatureImageBase64, int PageIndex,
    //        double X, double Y, double Width, double Height,
    //        bool DrawName, bool DrawTimestamp, string? Tz);

        //[HttpPost("{token}/submit")]
        //[AllowAnonymous]
        //public async Task<IActionResult> Submit(string token, [FromBody] SubmitReq req, CancellationToken ct)
        //{
        //    // 1) טעינת הזמנה + ולידציות בסיסיות
        //    var invite = await _db.SignatureInvites
        //        .Include(i => i.TemplateInstance)
        //        .FirstOrDefaultAsync(i => i.Token == token, ct);

        //    if (invite is null || invite.ExpiresAt < DateTime.UtcNow)
        //        return NotFound();

        //    // דרישה: ה-OTP אומת (קוקי קצר טווח שהוגדר ב-verify-otp)
        //    if (!Request.Cookies.TryGetValue($"sign_{token}_ok", out var ok) || ok != "1")
        //        return Unauthorized(new { error = "OtpNotVerified" });

        //    if (invite.Status == "Signed")
        //        return UnprocessableEntity(new { error = "AlreadySigned" });

        //    var instance = invite.TemplateInstance ?? throw new InvalidOperationException("TemplateInstance not found.");
        //    if (string.IsNullOrWhiteSpace(instance.S3KeyPdf))
        //        return NotFound(new { error = "PdfNotFound" });

        //    // 2) קריאת ה-PDF המקורי ל-MemoryStream עצמאי
        //    byte[] pdfBytes;
        //    await using (var s3In = await _storage.OpenReadAsync(instance.S3KeyPdf, ct))
        //    using (var msIn = new MemoryStream())
        //    {
        //        await s3In.CopyToAsync(msIn, ct);
        //        pdfBytes = msIn.ToArray(); // שומרים בבייטים להמשך עבודה בטוח
        //    }

        //    // 3) טעינת המסמך מערך הבייטים (בלי להשאיר תלות ב-Stream חיצוני)
        //    using var loadedDoc = new PdfLoadedDocument(new MemoryStream(pdfBytes));

        //    // 4) חישוב עמוד ו-קואורדינטות (ה-Client שלח יחסיים 0..1 מפינת שמאל-עליון)
        //    var pageIndex = Math.Clamp(req.PageIndex, 0, loadedDoc.Pages.Count - 1);
        //    var page = loadedDoc.Pages[pageIndex];

        //    var pageW = page.Size.Width;
        //    var pageH = page.Size.Height;

        //    // הנורמליזציה שנשלחת מה-UI היא פינת שמאל-עליון: (x,y,width,height)
        //    // ב-PDF ציר Y עולה מלמטה, לכן צריך להפוך:
        //    var drawW = Math.Max(1, req.Width * pageW);
        //    var drawH = Math.Max(1, req.Height * pageH);

        //    var left = Math.Clamp(req.X * pageW, 0, pageW - drawW);
        //    // yTop הוא המרחק מהחלק העליון; הופכים לתחתית:
        //    var yTop = req.Y * pageH;
        //    var bottom = Math.Clamp(pageH - (yTop + drawH), 0, pageH - drawH);

        //    var rect = new Syncfusion.Drawing.RectangleF((float)left, (float)bottom, (float)drawW, (float)drawH);

        //    // 5) המרת חתימת ה-PNG מ-Data URL → bytes
        //    var sigDataUrl = req.SignatureImageBase64 ?? "";
        //    var m = Regex.Match(sigDataUrl, @"^data:image\/png;base64,(.+)$", RegexOptions.IgnoreCase);
        //    var b64 = m.Success ? m.Groups[1].Value : sigDataUrl; // תומך גם במקרה שנשלחה מחרוזת base64 חשופה
        //    byte[] sigBytes;
        //    try
        //    {
        //        sigBytes = Convert.FromBase64String(b64);
        //    }
        //    catch
        //    {
        //        return UnprocessableEntity(new { error = "BadSignatureImage" });
        //    }

        //    // 6) ציור החתימה/שם/זמן על דף ה-PDF
        //    var gfx = page.Graphics; // ← גרפיקה של העמוד פעם אחת, מחוץ ל־using

        //    using (var bmpStream = new MemoryStream(sigBytes, writable: false))
        //    {
        //        var bmp = new PdfBitmap(bmpStream);
        //        gfx.DrawImage(bmp, rect);
        //    }

        //    if (req.DrawName || req.DrawTimestamp)
        //    {
        //        var font = new PdfStandardFont(PdfFontFamily.Helvetica, 10f, PdfFontStyle.Regular);
        //        var brush = PdfBrushes.Black;

        //        float textY = rect.Y - 12f; // שורה אחת מעל החתימה
        //        if (textY < 0) textY = rect.Bottom + 2f; // אם אין מקום מעל, מציירים מתחת

        //        var parts = new List<string>();
        //        if (req.DrawName && !string.IsNullOrWhiteSpace(invite.SignerName))
        //            parts.Add(invite.SignerName!);

        //        if (req.DrawTimestamp)
        //        {
        //            var tz = req.Tz ?? "UTC";
        //            var stamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'");
        //            parts.Add(stamp);
        //        }

        //        if (parts.Count > 0)
        //        {
        //            var text = string.Join("  |  ", parts);
        //            gfx.DrawString(text, font, brush, new Syncfusion.Drawing.PointF(rect.X, textY));
        //        }
        //    }

        //    // 7) שמירה החוצה ל-MemoryStream חדש → העלאה ל-S3
        //    byte[] signedPdfBytes;
        //    using (var msOut = new MemoryStream())
        //    {
        //        loadedDoc.Save(msOut);
        //        signedPdfBytes = msOut.ToArray();
        //    }

        //    var signedKey = $"{Path.GetDirectoryName(instance.S3KeyPdf)!.Replace('\\', '/')}/signed.pdf";
        //    await using (var up = new MemoryStream(signedPdfBytes, writable: false))
        //    {
        //        await _storage.SaveAsync(up, signedKey, "application/pdf", ct);
        //    }

        //    // 8) עדכון סטטוס המופע
        //    instance.Status = "Signed";
        //    instance.SignedPdfS3Key = signedKey;
        //    instance.SignedAt = DateTime.UtcNow;
        //    instance.UpdatedAt = DateTimeOffset.UtcNow;
        //    await _db.SaveChangesAsync(ct);

        //    return Ok(new { ok = true, signedKey });
        //}






    }
}
