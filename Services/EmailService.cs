using MimeKit;
using MailKit.Net.Smtp;
using MailKit.Security;

namespace InkVault.Services
{
    public interface IEmailService
    {
        Task SendOTPAsync(string email, string otp);
        Task SendEmailAsync(string email, string subject, string htmlBody);
    }

    public class EmailService : IEmailService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task SendOTPAsync(string email, string otp)
        {
            _logger.LogInformation("Preparing to send OTP email to {Email}", email);
            
            var subject = "Your OTP for InkVault";
            var body = $@"
                <html>
                <body>
                    <h2>InkVault - One-Time Password</h2>
                    <p>Your OTP is: <strong>{otp}</strong></p>
                    <p>This OTP will expire in 10 minutes.</p>
                    <p>If you didn't request this, please ignore this email.</p>
                </body>
                </html>";

            try
            {
                await SendEmailAsync(email, subject, body);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send OTP email to {Email}. User can still verify via manual entry.", email);
            }
        }

        public async Task SendEmailAsync(string email, string subject, string htmlBody)
        {
            try
            {
                var smtpSettings = _configuration.GetSection("SmtpSettings");
                var smtpServer = smtpSettings["Server"];

                if (string.IsNullOrEmpty(smtpServer))
                {
                    _logger.LogError("SMTP configuration is missing. SmtpSettings:Server is null or empty.");
                    throw new InvalidOperationException("SMTP configuration is missing. Please ensure SmtpSettings are configured in appsettings.json or Environment Variables.");
                }

                var smtpPortStr = smtpSettings["Port"] ?? "587";
                if (!int.TryParse(smtpPortStr, out var smtpPort))
                {
                    smtpPort = 587;
                }
                
                var senderEmail = smtpSettings["SenderEmail"];
                var senderPassword = smtpSettings["SenderPassword"];

                if (string.IsNullOrEmpty(senderEmail) || string.IsNullOrEmpty(senderPassword))
                {
                    _logger.LogError("SMTP credentials are missing.");
                    throw new InvalidOperationException("SMTP credentials are missing.");
                }

                _logger.LogInformation("Connecting to SMTP server {SmtpServer}:{SmtpPort}", smtpServer, smtpPort);

                var message = new MimeMessage();
                message.From.Add(new MailboxAddress("InkVault", senderEmail));
                message.To.Add(new MailboxAddress("", email));
                message.Subject = subject;

                var bodyBuilder = new BodyBuilder { HtmlBody = htmlBody };
                message.Body = bodyBuilder.ToMessageBody();

                using (var client = new SmtpClient())
                {
                    // Set a timeout of 10 seconds for connection
                    client.Timeout = 10000; 

                    // Connect
                    await client.ConnectAsync(smtpServer, smtpPort, SecureSocketOptions.StartTls);
                    
                    _logger.LogInformation("Authenticated with SMTP server");
                    await client.AuthenticateAsync(senderEmail, senderPassword);
                    
                    _logger.LogInformation("Sending email to {Email}", email);
                    await client.SendAsync(message);
                    
                    await client.DisconnectAsync(true);
                    _logger.LogInformation("Email sent successfully");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email to {Email}", email);
                throw new InvalidOperationException($"Error sending email: {ex.Message}", ex);
            }
        }
    }
}
