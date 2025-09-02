using System.Net.Mail;
using System.Net;
using DigitalSignServer.Options;
using Microsoft.Extensions.Options;

namespace DigitalSignServer.services
{
    // services/notifications/INotificationService.cs
    public interface INotificationService
    {
        Task SendEmailAsync(string to, string subject, string htmlBody, CancellationToken ct);
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
    }


}
