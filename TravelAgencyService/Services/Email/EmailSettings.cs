namespace TravelAgencyService.Services.Email
{
    public class EmailSettings
    {
        public string FromEmail { get; set; } = "";
        public string FromName { get; set; } = "";
        public string SmtpHost { get; set; } = "";
        public int SmtpPort { get; set; } = 587;
        public string SmtpUser { get; set; } = "";
        public string SmtpPass { get; set; } = "";
    }
}
