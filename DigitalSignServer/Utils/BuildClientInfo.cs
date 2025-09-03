// Utils/ClientEvidenceHelper.cs
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using DigitalSignServer.context;
using DigitalSignServer.models;

namespace DigitalSignServer.Utils
{
    public static class ClientEvidenceHelper
    {
        /// <summary>
        /// בונה טבלת HTML של "פרטי סביבת החותם" מתוך האירוע האחרון מסוג SignatureSubmitted (אם קיים).
        /// אם אין אירוע — מחזיר מחרוזת ריקה.
        /// </summary>
        public static async Task<string> BuildClientInfoHtmlAsync(
            AppDbContext db,
            SignatureInvite invite,
            CancellationToken ct)
        {
            var lastClientEv = await db.SignatureAuditEvents
                .Where(e => e.InviteId == invite.Id && e.Action == "SignatureSubmitted")
                .OrderByDescending(e => e.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (lastClientEv is null)
                return string.Empty;

            string countryCity = (lastClientEv.GeoCountry ?? string.Empty) +
                                 (string.IsNullOrWhiteSpace(lastClientEv.GeoCity) ? string.Empty : " / " + lastClientEv.GeoCity);

            // HTML-encode לכל הערכים
            string enc(string? v) => WebUtility.HtmlEncode(v ?? string.Empty);

            var html = $@"
<table border='1' cellpadding='6' cellspacing='0' style='border-collapse:collapse; width:100%; font-family:Arial; font-size:13px'>
  <thead>
    <tr style='background:#f2f2f2'>
      <th colspan='2' style='text-align:right'>פרטי סביבת החותם</th>
    </tr>
  </thead>
  <tbody>
    <tr><td style='width:30%'>כתובת IP</td><td>{enc(lastClientEv.IpAddress)}</td></tr>
    <tr><td>מדינה / עיר</td><td>{enc(countryCity)}</td></tr>
    <tr><td>מערכת / דפדפן (User-Agent)</td><td>{enc(lastClientEv.UserAgent)}</td></tr>
    <tr><td>פלטפורמה</td><td>{enc(lastClientEv.Platform)}</td></tr>
    <tr><td>שפה</td><td>{enc(lastClientEv.Language)}</td></tr>
    <tr><td>אזור זמן</td><td>{enc(lastClientEv.Timezone)}</td></tr>
    <tr><td>רזולוציית מסך</td><td>{enc(lastClientEv.Screen)}</td></tr>
    <tr><td>מס’ נקודות מגע</td><td>{(lastClientEv.TouchPoints?.ToString() ?? "")}</td></tr>
  </tbody>
</table>";

            return html;
        }
    }
}
