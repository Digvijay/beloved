using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Beloved.ControlPlane.Data;
using Beloved.ControlPlane.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Beloved.ControlPlane.Services;

/// <summary>
/// Injectable email delivery engine utilizing the Transactional Outbox pattern.
/// Emails are written to the database in a transaction, ensuring they are never lost
/// even if the network or external mail APIs go down.
/// </summary>
public interface IEmailSender
{
    Task SendPaymentFailedEmailAsync(string tenantEmail, string tenantName, decimal amountDue, string updateUrl);
}

public sealed class EmailSender : IEmailSender
{
    private readonly BelovedDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<EmailSender> _logger;

    public EmailSender(BelovedDbContext db, IWebHostEnvironment env, ILogger<EmailSender> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _env = env ?? throw new ArgumentNullException(nameof(env));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task SendPaymentFailedEmailAsync(string tenantEmail, string tenantName, decimal amountDue, string updateUrl)
    {
        if (string.IsNullOrWhiteSpace(tenantEmail))
            throw new ArgumentException("Recipient email cannot be empty.", nameof(tenantEmail));

        var body = await GetPaymentFailedTemplateAsync(tenantName, amountDue, updateUrl);

        // Resilient Outbox pattern: Queue to DB instead of direct inline sending.
        var emailJob = new EmailQueueJob
        {
            RecipientEmail = tenantEmail,
            Subject = "Action Required: Payment Failed for Beloved Plan",
            Body = body,
            Status = "Pending",
            RetryCount = 0
        };

        _db.EmailQueueJobs.Add(emailJob);
        await _db.SaveChangesAsync();

        _logger.LogInformation("resilient_billing_email_queued: JobId={JobId} Recipient={Recipient}", emailJob.Id, tenantEmail);
    }

    private async Task<string> GetPaymentFailedTemplateAsync(string tenantName, decimal amountDue, string updateUrl)
    {
        var customTemplatePath = Path.Combine(_env.ContentRootPath, "Templates", "payment_failed.html");
        if (File.Exists(customTemplatePath))
        {
            try
            {
                var customHtml = await File.ReadAllTextAsync(customTemplatePath);
                return customHtml
                    .Replace("{TenantName}", tenantName)
                    .Replace("{AmountDue}", amountDue.ToString("F2"))
                    .Replace("{UpdateUrl}", updateUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load custom email template from {Path}. Falling back to default layout.", customTemplatePath);
            }
        }

        return $@"
<!DOCTYPE html>
<html>
<body>
    <h2>Payment Failed</h2>
    <p>Hi {tenantName}, your billing renewal of ${amountDue:F2} failed.</p>
    <p><a href='{updateUrl}'>Update Payment Details</a></p>
</body>
</html>";
    }
}

/// <summary>
/// Pluggable client implementation that sends emails using SMTP or a transactional HTTP mail API (e.g. SendGrid, Mailgun).
/// Written to the Fowler/Edwards standard.
/// </summary>
public interface IMailClient
{
    Task SendEmailAsync(string to, string subject, string body);
}

public sealed class LogMailClient : IMailClient
{
    private readonly ILogger<LogMailClient> _logger;
    private readonly string _from;

    public LogMailClient(IConfiguration config, ILogger<LogMailClient> logger)
    {
        _logger = logger;
        _from = config["Email:FromEmail"] ?? "billing@beloved.build";
    }

    public Task SendEmailAsync(string to, string subject, string body)
    {
        // Default safe logging implementation for dev environments
        _logger.LogInformation("Sending SMTP Email:\nFrom: {From}\nTo: {To}\nSubject: {Subject}\nBody: {Body}", 
            _from, to, subject, body);
        return Task.CompletedTask;
    }
}
