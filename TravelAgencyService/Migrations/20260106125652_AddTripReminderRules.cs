using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelAgencyService.Migrations
{
    /// <inheritdoc />
    public partial class AddTripReminderRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TripReminderRules",
                columns: table => new
                {
                    TripReminderRuleId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TripId = table.Column<int>(type: "int", nullable: false),
                    OffsetAmount = table.Column<int>(type: "int", nullable: false),
                    OffsetUnit = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SubjectTemplate = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripReminderRules", x => x.TripReminderRuleId);
                    table.ForeignKey(
                        name: "FK_TripReminderRules_Trips_TripId",
                        column: x => x.TripId,
                        principalTable: "Trips",
                        principalColumn: "TripId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TripReminderSendLogs",
                columns: table => new
                {
                    TripReminderSendLogId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TripReminderRuleId = table.Column<int>(type: "int", nullable: false),
                    BookingId = table.Column<int>(type: "int", nullable: false),
                    ToEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripReminderSendLogs", x => x.TripReminderSendLogId);
                    table.ForeignKey(
                        name: "FK_TripReminderSendLogs_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "BookingId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TripReminderSendLogs_TripReminderRules_TripReminderRuleId",
                        column: x => x.TripReminderRuleId,
                        principalTable: "TripReminderRules",
                        principalColumn: "TripReminderRuleId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TripReminderRules_TripId",
                table: "TripReminderRules",
                column: "TripId");

            migrationBuilder.CreateIndex(
                name: "IX_TripReminderSendLogs_BookingId",
                table: "TripReminderSendLogs",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_TripReminderSendLogs_TripReminderRuleId_BookingId",
                table: "TripReminderSendLogs",
                columns: new[] { "TripReminderRuleId", "BookingId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TripReminderSendLogs");

            migrationBuilder.DropTable(
                name: "TripReminderRules");
        }
    }
}
