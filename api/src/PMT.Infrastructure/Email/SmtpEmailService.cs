using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using PMT.Application.Common.Interfaces;
namespace PMT.Infrastructure.Email;
public sealed class SmtpEmailService(IOptions<SmtpOptions> options) : IEmailService
{
    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        using var message = new MailMessage { From = new MailAddress(settings.FromAddress, settings.FromName), Subject = subject, Body = htmlBody, IsBodyHtml = true };
        message.To.Add(to);
        using var client = new SmtpClient(settings.Host, settings.Port)
        {
            EnableSsl = settings.EnableSsl,
            Credentials = string.IsNullOrWhiteSpace(settings.UserName) ? CredentialCache.DefaultNetworkCredentials : new NetworkCredential(settings.UserName, settings.Password)
        };
        cancellationToken.ThrowIfCancellationRequested();
        await client.SendMailAsync(message, cancellationToken);
    }
}
