using DigitalSignServer.models;
using System.Net;

namespace DigitalSignServer.Utils
{
    public class BuildAudit
    {
        public static string BuildSignatureAuditHtml(SignatureInvite invite, TemplateInstance instance)
        {
            static string F(DateTimeOffset? dto)
                => dto.HasValue ? dto.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "—";

            // נתונים זמינים שקיימים היום בבסיס הנתונים
            var createdPdfAt = F(instance.CreatedAt);
            var inviteSentAt = F(invite.CreatedAt);
            var inviteExpiresAt = F(invite.ExpiresAt);
            var signedAt = F(instance.SignedAt);

            // אופציונלי: מציגים גם למי נשלח + מי חתם (אם יש שם)
            var signerName = string.IsNullOrWhiteSpace(invite.SignerName) ? "—" : invite.SignerName;

            return $@"
                <div style=""font-family:Arial,Helvetica,sans-serif;font-size:14px;direction:rtl"">
                  <h3 style=""margin:0 0 8px"">מטא־דאטה על המסמך</h3>
                  <table style=""border-collapse:collapse;width:100%;max-width:640px"">
                    <tbody>
                      <tr>
                        <td style=""border:1px solid #ddd;padding:6px;white-space:nowrap"">PDF נוצר</td>
                        <td style=""border:1px solid #ddd;padding:6px"">{createdPdfAt}</td>
                      </tr>
                      <tr>
                        <td style=""border:1px solid #ddd;padding:6px;white-space:nowrap"">הזמנה לחתימה נשלחה</td>
                        <td style=""border:1px solid #ddd;padding:6px"">{inviteSentAt}</td>
                      </tr>
                      <tr>
                        <td style=""border:1px solid #ddd;padding:6px;white-space:nowrap"">תוקף ההזמנה</td>
                        <td style=""border:1px solid #ddd;padding:6px"">{inviteExpiresAt}</td>
                      </tr>
                      <tr>
                        <td style=""border:1px solid #ddd;padding:6px;white-space:nowrap"">נחתם בפועל</td>
                        <td style=""border:1px solid #ddd;padding:6px"">{signedAt}</td>
                      </tr>
                      <tr>
                        <td style=""border:1px solid #ddd;padding:6px;white-space:nowrap"">שם חותם</td>
                        <td style=""border:1px solid #ddd;padding:6px"">{WebUtility.HtmlEncode(signerName)}</td>
                      </tr>
                      <tr>
                        <td style=""border:1px solid #ddd;padding:6px;white-space:nowrap"">אימייל חותם</td>
                        <td style=""border:1px solid #ddd;padding:6px"">{WebUtility.HtmlEncode(invite.SignerEmail ?? invite.RecipientEmail ?? "—")}</td>
                      </tr>
                    </tbody>
                  </table>
                </div>";
        }

    }
}
