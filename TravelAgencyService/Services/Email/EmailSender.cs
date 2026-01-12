namespace TravelAgencyService.Services.Email
{
    public interface IEmailSender
    {
        Task SendAsync(string toEmail, string subject, string htmlBody);
        Task SendWithAttachmentAsync(string toEmail, string subject, string htmlBody, byte[] attachment, string attachmentName, string mimeType);
    }
}