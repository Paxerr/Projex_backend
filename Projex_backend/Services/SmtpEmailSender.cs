using System.Net;
using System.Net.Mail;

namespace Projex_backend.Services
{
    public class SmtpEmailSender : IEmailSender
    {
        private readonly IConfiguration _config;

        public SmtpEmailSender(IConfiguration config)
        {
            _config = config;
        }

        public async Task SendPasswordResetCodeAsync(string toEmail, string code, DateTime expiresAt)
        {
            var host = _config["Smtp:Host"];
            var fromEmail = _config["Smtp:FromEmail"];
            var fromName = _config["Smtp:FromName"] ?? "Projex";

            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(fromEmail))
            {
                throw new InvalidOperationException("Email service is not configured.");
            }

            var port = _config.GetValue("Smtp:Port", 587);
            var enableSsl = _config.GetValue("Smtp:EnableSsl", true);
            var username = _config["Smtp:Username"];
            var password = _config["Smtp:Password"];

            using var message = new MailMessage
            {
                From = new MailAddress(fromEmail, fromName),
                Subject = "Projex password reset code",
                Body = $"Your Projex password reset code is {code}. This code expires at {expiresAt:yyyy-MM-dd HH:mm:ss} UTC.",
                IsBodyHtml = false
            };
            message.To.Add(toEmail);

            using var client = new SmtpClient(host, port)
            {
                EnableSsl = enableSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false
            };

            if (!string.IsNullOrWhiteSpace(username))
            {
                client.Credentials = new NetworkCredential(username, password);
            }

            await client.SendMailAsync(message);
        }
    }
}
