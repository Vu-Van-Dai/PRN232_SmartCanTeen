using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SendGrid;
using SendGrid.Helpers.Mail;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace API.Services.Email
{
    public class SendGridOptions
    {
        public string? ApiKey { get; set; }
        public string FromEmail { get; set; } = "no-reply@example.com";
        public string FromName { get; set; } = "SmartCanteen";
    }

    public class SendGridEmailSender : IEmailSender
    {
        private readonly SendGridOptions _options;
        private readonly ILogger<SendGridEmailSender> _logger;

        public SendGridEmailSender(IOptions<SendGridOptions> options, ILogger<SendGridEmailSender> logger)
        {
            _options = options.Value;
            _logger = logger;
        }

        public async Task SendAsync(string toEmail, string subject, string htmlBody, string? textBody = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(_options.ApiKey))
            {
                _logger.LogWarning("SendGrid ApiKey is not configured. Skipping email to {ToEmail} (Subject: {Subject}).", toEmail, subject);
                return;
            }

            var client = new SendGridClient(_options.ApiKey);
            var msg = new SendGridMessage
            {
                From = new EmailAddress(_options.FromEmail, _options.FromName),
                Subject = subject,
                HtmlContent = htmlBody,
                PlainTextContent = textBody ?? StripHtml(htmlBody)
            };
            msg.AddTo(new EmailAddress(toEmail));

            var res = await client.SendEmailAsync(msg, ct);

            if ((int)res.StatusCode >= 400)
            {
                _logger.LogWarning("SendGrid send failed with status {StatusCode} for {ToEmail}", (int)res.StatusCode, toEmail);
            }
        }

        private static string StripHtml(string html)
        {
            // simple fallback; keep it minimal (we have HTML anyway)
            return html
                .Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
                .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
                .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase)
                .Replace("</p>", "\n\n", StringComparison.OrdinalIgnoreCase);
        }
    }
}
