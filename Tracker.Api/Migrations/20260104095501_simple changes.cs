using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tracker.Api.Migrations
{
    /// <inheritdoc />
    public partial class simplechanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Url",
                table: "WebSessions",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 2048,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "WebSessions",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 512,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Domain",
                table: "WebSessions",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldMaxLength: 255);

            migrationBuilder.AddColumn<Guid>(
                name: "SessionId",
                table: "WebSessions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "SessionId",
                table: "IdleSessions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "SessionId",
                table: "AppSessions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "IngestCursors",
                columns: table => new
                {
                    DeviceId = table.Column<string>(type: "text", nullable: false),
                    Stream = table.Column<string>(type: "text", nullable: false),
                    LastSequence = table.Column<long>(type: "bigint", nullable: false),
                    LastBatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastSentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngestCursors", x => new { x.DeviceId, x.Stream });
                });

            migrationBuilder.CreateTable(
                name: "UrlSummaryRows",
                columns: table => new
                {
                    Url = table.Column<string>(type: "text", nullable: false),
                    Seconds = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                });

            migrationBuilder.CreateTable(
                name: "WebEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Domain = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Browser = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WebSessions_SessionId",
                table: "WebSessions",
                column: "SessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IdleSessions_SessionId",
                table: "IdleSessions",
                column: "SessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppSessions_SessionId",
                table: "AppSessions",
                column: "SessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WebEvents_DeviceId_Timestamp",
                table: "WebEvents",
                columns: new[] { "DeviceId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_WebEvents_EventId",
                table: "WebEvents",
                column: "EventId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IngestCursors");

            migrationBuilder.DropTable(
                name: "UrlSummaryRows");

            migrationBuilder.DropTable(
                name: "WebEvents");

            migrationBuilder.DropIndex(
                name: "IX_WebSessions_SessionId",
                table: "WebSessions");

            migrationBuilder.DropIndex(
                name: "IX_IdleSessions_SessionId",
                table: "IdleSessions");

            migrationBuilder.DropIndex(
                name: "IX_AppSessions_SessionId",
                table: "AppSessions");

            migrationBuilder.DropColumn(
                name: "SessionId",
                table: "WebSessions");

            migrationBuilder.DropColumn(
                name: "SessionId",
                table: "IdleSessions");

            migrationBuilder.DropColumn(
                name: "SessionId",
                table: "AppSessions");

            migrationBuilder.AlterColumn<string>(
                name: "Url",
                table: "WebSessions",
                type: "text",
                maxLength: 2048,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2048)",
                oldMaxLength: 2048,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "WebSessions",
                type: "text",
                maxLength: 512,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Domain",
                table: "WebSessions",
                type: "text",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);
        }
    }
}
