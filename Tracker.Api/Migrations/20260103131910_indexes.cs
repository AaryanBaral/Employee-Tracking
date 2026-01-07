using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tracker.Api.Migrations
{
    /// <inheritdoc />
    public partial class indexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppSummaryRows",
                columns: table => new
                {
                    ProcessName = table.Column<string>(type: "text", nullable: false),
                    Seconds = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                });

            migrationBuilder.CreateTable(
                name: "DomainSummaryRows",
                columns: table => new
                {
                    Domain = table.Column<string>(type: "text", nullable: false),
                    Seconds = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                });

            migrationBuilder.CreateTable(
                name: "IdleSecondsRows",
                columns: table => new
                {
                    Seconds = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                });

            migrationBuilder.CreateIndex(
                name: "IX_WebSessions_DeviceId_EndAt",
                table: "WebSessions",
                columns: new[] { "DeviceId", "EndAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WebSessions_DeviceId_StartAt",
                table: "WebSessions",
                columns: new[] { "DeviceId", "StartAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IdleSessions_DeviceId_EndAt",
                table: "IdleSessions",
                columns: new[] { "DeviceId", "EndAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IdleSessions_DeviceId_StartAt",
                table: "IdleSessions",
                columns: new[] { "DeviceId", "StartAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Devices_LastSeenAt",
                table: "Devices",
                column: "LastSeenAt");

            migrationBuilder.CreateIndex(
                name: "IX_AppSessions_DeviceId_EndAt",
                table: "AppSessions",
                columns: new[] { "DeviceId", "EndAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AppSessions_DeviceId_StartAt",
                table: "AppSessions",
                columns: new[] { "DeviceId", "StartAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSummaryRows");

            migrationBuilder.DropTable(
                name: "DomainSummaryRows");

            migrationBuilder.DropTable(
                name: "IdleSecondsRows");

            migrationBuilder.DropIndex(
                name: "IX_WebSessions_DeviceId_EndAt",
                table: "WebSessions");

            migrationBuilder.DropIndex(
                name: "IX_WebSessions_DeviceId_StartAt",
                table: "WebSessions");

            migrationBuilder.DropIndex(
                name: "IX_IdleSessions_DeviceId_EndAt",
                table: "IdleSessions");

            migrationBuilder.DropIndex(
                name: "IX_IdleSessions_DeviceId_StartAt",
                table: "IdleSessions");

            migrationBuilder.DropIndex(
                name: "IX_Devices_LastSeenAt",
                table: "Devices");

            migrationBuilder.DropIndex(
                name: "IX_AppSessions_DeviceId_EndAt",
                table: "AppSessions");

            migrationBuilder.DropIndex(
                name: "IX_AppSessions_DeviceId_StartAt",
                table: "AppSessions");
        }
    }
}
