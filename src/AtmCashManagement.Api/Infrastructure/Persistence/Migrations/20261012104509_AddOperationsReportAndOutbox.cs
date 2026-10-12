using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtmCashManagement.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationsReportAndOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "outbox_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    AtmId = table.Column<Guid>(type: "uuid", nullable: false),
                    CassetteId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SentAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RetryCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_events", x => x.Id);
                    table.CheckConstraint("ck_outbox_events_retry_count", "\"RetryCount\" >= 0");
                    table.CheckConstraint("ck_outbox_events_sent_at", "(\"Status\" = 'Pending' AND \"SentAtUtc\" IS NULL) OR (\"Status\" = 'Sent' AND \"SentAtUtc\" IS NOT NULL)");
                    table.CheckConstraint("ck_outbox_events_status", "\"Status\" IN ('Pending', 'Sent')");
                    table.ForeignKey(
                        name: "FK_outbox_events_atms_AtmId",
                        column: x => x.AtmId,
                        principalTable: "atms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_outbox_events_cassettes_CassetteId",
                        column: x => x.CassetteId,
                        principalTable: "cassettes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_events_AtmId",
                table: "outbox_events",
                column: "AtmId");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_events_CassetteId",
                table: "outbox_events",
                column: "CassetteId");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_events_EventKey",
                table: "outbox_events",
                column: "EventKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_outbox_events_Status_CreatedAtUtc",
                table: "outbox_events",
                columns: new[] { "Status", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox_events");
        }
    }
}
