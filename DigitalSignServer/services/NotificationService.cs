using System.Net.Mail;
using System.Net;
using DigitalSignServer.Options;
using Microsoft.Extensions.Options;

namespace DigitalSignServer.services
{
    public sealed record EmailAttachment(string FileName, string ContentType, byte[] Content);

    // services/notifications/INotificationService.cs
    public interface INotificationService
    {
        Task SendEmailAsync(string to, string subject, string htmlBody, CancellationToken ct);
        Task SendEmailAsync(string to, string subject, string html, IEnumerable<EmailAttachment> attachments, CancellationToken ct); // חדש

    }

    // services/notifications/SmtpEmailService.cs

    public class SmtpEmailService : INotificationService
    {
        private readonly EmailOptions _opt;

        public SmtpEmailService(IOptions<EmailOptions> opt)
        {
            _opt = opt.Value;
        }

        public async Task SendEmailAsync(string to, string subject, string htmlBody, CancellationToken ct)
        {
            using var client = new SmtpClient(_opt.Host)
            {
                Port = _opt.Port,
                EnableSsl = true,
                Credentials = (!string.IsNullOrWhiteSpace(_opt.User) && !string.IsNullOrWhiteSpace(_opt.Pass))
                    ? new NetworkCredential(_opt.User, _opt.Pass)
                    : CredentialCache.DefaultNetworkCredentials
            };

            var fromAddress = string.IsNullOrWhiteSpace(_opt.DisplayName)
                ? _opt.From
                : $"{_opt.DisplayName} <{_opt.From}>";

            using var msg = new MailMessage
            {
                From = new MailAddress(_opt.From, _opt.DisplayName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };
            msg.To.Add(to);

            await client.SendMailAsync(msg, ct);
        }
        public async Task SendEmailAsync(string to, string subject, string htmlBody,
                                     IEnumerable<EmailAttachment> attachments,
                                     CancellationToken ct)
        {
            using var client = new SmtpClient(_opt.Host)
            {
                Port = _opt.Port,
                EnableSsl = true,
                Credentials = (!string.IsNullOrWhiteSpace(_opt.User) && !string.IsNullOrWhiteSpace(_opt.Pass))
                    ? new NetworkCredential(_opt.User, _opt.Pass)
                    : CredentialCache.DefaultNetworkCredentials
            };

            using var msg = new MailMessage
            {
                From = new MailAddress(_opt.From, _opt.DisplayName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };
            msg.To.Add(to);

            // מצרפים קבצים (שומרים את ה־MemoryStream עד אחרי השליחה)
            if (attachments != null)
            {
                foreach (var att in attachments)
                {
                    var stream = new MemoryStream(att.Content, writable: false);
                    var a = new Attachment(stream, att.FileName, att.ContentType);
                    msg.Attachments.Add(a);
                }
            }

            await client.SendMailAsync(msg, ct);
            // סגירת msg תסגור גם את ה־Attachment ואת ה־Stream
        }
    }
}
