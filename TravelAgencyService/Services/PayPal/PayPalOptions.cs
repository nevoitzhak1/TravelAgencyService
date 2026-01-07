namespace TravelAgencyService.Services.PayPal;

public class PayPalOptions
{
    public string Mode { get; set; } = "Sandbox"; // Sandbox / Live
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
}
