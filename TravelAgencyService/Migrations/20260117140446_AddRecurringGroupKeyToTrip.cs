using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelAgencyService.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurringGroupKeyToTrip : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add RecurringGroupKey column
            migrationBuilder.AddColumn<string>(
                name: "RecurringGroupKey",
                table: "Trips",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            // Create index on RecurringGroupKey for faster lookups
            migrationBuilder.CreateIndex(
                name: "IX_Trips_RecurringGroupKey",
                table: "Trips",
                column: "RecurringGroupKey");

            // Create unique index to prevent duplicate year + recurring group combinations
            migrationBuilder.CreateIndex(
                name: "IX_Trips_RecurringGroupKey_StartDate_Year",
                table: "Trips",
                columns: new[] { "RecurringGroupKey", "StartDate" },
                unique: true,
                filter: "[RecurringGroupKey] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Trips_RecurringGroupKey_StartDate_Year",
                table: "Trips");

            migrationBuilder.DropIndex(
                name: "IX_Trips_RecurringGroupKey",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "RecurringGroupKey",
                table: "Trips");
        }
    }
}
