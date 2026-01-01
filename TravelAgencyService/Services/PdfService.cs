using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using TravelAgencyService.Models;
using System.Globalization;

namespace TravelAgencyService.Services
{
    public class PdfService
    {
        public byte[] GenerateItinerary(Booking booking)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            // Force English date format
            var culture = CultureInfo.InvariantCulture;

            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(40);
                    page.DefaultTextStyle(x => x.FontSize(11));

                    // === HEADER ===
                    page.Header().Container().BorderBottom(2).BorderColor(Colors.Blue.Medium).PaddingBottom(10).Row(row =>
                    {
                        row.RelativeItem().Column(col =>
                        {
                            col.Item().Text("TRAVEL AGENCY").FontSize(28).Bold().FontColor(Colors.Blue.Darken2);
                            col.Item().Text("Your Journey Begins Here").FontSize(10).Italic().FontColor(Colors.Grey.Medium);
                        });
                        row.ConstantItem(120).AlignRight().Column(col =>
                        {
                            col.Item().Text("BOOKING").FontSize(10).FontColor(Colors.Grey.Medium);
                            col.Item().Text($"#{booking.BookingId}").FontSize(20).Bold().FontColor(Colors.Blue.Medium);
                        });
                    });

                    // === CONTENT ===
                    page.Content().PaddingTop(20).Column(column =>
                    {
                        column.Spacing(15);

                        // Status Banner
                        var statusColor = booking.Status == BookingStatus.Confirmed ? Colors.Green.Darken1 : Colors.Orange.Medium;
                        column.Item().Background(statusColor).Padding(10).AlignCenter()
                            .Text($"STATUS: {booking.Status.ToString().ToUpper()}")
                            .FontSize(12).Bold().FontColor(Colors.White);

                        // Booking Info Box
                        column.Item().Background(Colors.Grey.Lighten4).Padding(15).Row(row =>
                        {
                            row.RelativeItem().Column(col =>
                            {
                                col.Item().Text("BOOKING DATE").FontSize(9).FontColor(Colors.Grey.Medium);
                                col.Item().Text(booking.BookingDate.ToString("MMMM dd, yyyy", culture)).FontSize(12).Bold();
                            });
                            row.RelativeItem().Column(col =>
                            {
                                col.Item().Text("PAYMENT STATUS").FontSize(9).FontColor(Colors.Grey.Medium);
                                col.Item().Text(booking.IsPaid ? "PAID" : "PENDING").FontSize(12).Bold()
                                    .FontColor(booking.IsPaid ? Colors.Green.Darken1 : Colors.Orange.Medium);
                            });
                        });

                        // Traveler Section
                        column.Item().Text("TRAVELER INFORMATION").FontSize(14).Bold().FontColor(Colors.Blue.Darken2);
                        column.Item().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(15).Row(row =>
                        {
                            row.RelativeItem().Column(col =>
                            {
                                col.Item().Text("Full Name").FontSize(9).FontColor(Colors.Grey.Medium);
                                col.Item().Text($"{booking.User?.FirstName} {booking.User?.LastName}").FontSize(13).Bold();
                            });
                            row.RelativeItem().Column(col =>
                            {
                                col.Item().Text("Email").FontSize(9).FontColor(Colors.Grey.Medium);
                                col.Item().Text(booking.User?.Email ?? "N/A").FontSize(12);
                            });
                        });

                        // Trip Section
                        column.Item().Text("TRIP DETAILS").FontSize(14).Bold().FontColor(Colors.Blue.Darken2);
                        column.Item().Border(1).BorderColor(Colors.Blue.Lighten3).Background(Colors.Blue.Lighten5).Padding(15).Column(inner =>
                        {
                            inner.Spacing(8);
                            inner.Item().Text(booking.Trip?.PackageName ?? "N/A").FontSize(18).Bold().FontColor(Colors.Blue.Darken3);
                            inner.Item().Text($"{booking.Trip?.Destination}, {booking.Trip?.Country}").FontSize(13).FontColor(Colors.Grey.Darken1);

                            inner.Item().PaddingTop(10).Row(row =>
                            {
                                row.RelativeItem().Column(col =>
                                {
                                    col.Item().Text("DEPARTURE").FontSize(9).FontColor(Colors.Grey.Medium);
                                    col.Item().Text(booking.Trip?.StartDate.ToString("ddd, MMM dd, yyyy", culture) ?? "N/A").FontSize(11).Bold();
                                });
                                row.RelativeItem().Column(col =>
                                {
                                    col.Item().Text("RETURN").FontSize(9).FontColor(Colors.Grey.Medium);
                                    col.Item().Text(booking.Trip?.EndDate.ToString("ddd, MMM dd, yyyy", culture) ?? "N/A").FontSize(11).Bold();
                                });
                                row.RelativeItem().Column(col =>
                                {
                                    col.Item().Text("DURATION").FontSize(9).FontColor(Colors.Grey.Medium);
                                    col.Item().Text($"{booking.Trip?.TripDurationDays} Days").FontSize(11).Bold();
                                });
                            });
                        });

                        // Payment Section
                        column.Item().Text("PAYMENT SUMMARY").FontSize(14).Bold().FontColor(Colors.Blue.Darken2);
                        column.Item().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(15).Column(inner =>
                        {
                            inner.Item().Row(row =>
                            {
                                row.RelativeItem().Text("Rooms Booked");
                                row.ConstantItem(100).AlignRight().Text($"{booking.NumberOfRooms}");
                            });
                            inner.Item().Row(row =>
                            {
                                row.RelativeItem().Text("Price per Room");
                                row.ConstantItem(100).AlignRight().Text($"${booking.Trip?.Price:N2}");
                            });
                            inner.Item().PaddingTop(5).BorderTop(1).BorderColor(Colors.Grey.Lighten2).PaddingTop(5).Row(row =>
                            {
                                row.RelativeItem().Text("TOTAL PAID").Bold().FontSize(13);
                                row.ConstantItem(100).AlignRight().Text($"${booking.TotalPrice:N2}").Bold().FontSize(13).FontColor(Colors.Green.Darken2);
                            });
                        });

                        // Important Info
                        column.Item().Background(Colors.Orange.Lighten4).Border(1).BorderColor(Colors.Orange.Lighten2).Padding(15).Column(inner =>
                        {
                            inner.Item().Text("IMPORTANT INFORMATION").FontSize(12).Bold().FontColor(Colors.Orange.Darken2);
                            inner.Item().PaddingTop(5).Text("• Please arrive at the airport at least 3 hours before departure").FontSize(10);
                            inner.Item().Text("• Bring valid passport and all travel documents").FontSize(10);
                            inner.Item().Text("• Check destination entry requirements before traveling").FontSize(10);
                            inner.Item().Text("• Keep this confirmation for your records").FontSize(10);
                        });
                    });

                    // === FOOTER ===
                    page.Footer().BorderTop(1).BorderColor(Colors.Grey.Lighten2).PaddingTop(10).Row(row =>
                    {
                        row.RelativeItem().Text("Travel Agency | support@travelagency.com | +1-800-TRAVEL").FontSize(9).FontColor(Colors.Grey.Medium);
                        row.RelativeItem().AlignRight().Text($"Generated: {DateTime.Now.ToString("MMM dd, yyyy", culture)}").FontSize(9).FontColor(Colors.Grey.Medium);
                    });
                });
            });

            return document.GeneratePdf();
        }
    }
}