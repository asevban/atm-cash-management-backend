using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtmCashManagement.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAtmCoreBusinessLogic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "InitialLoadCompletedAt",
                table: "atms",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "cassette_movements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AtmId = table.Column<Guid>(type: "uuid", nullable: false),
                    CassetteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Denomination = table.Column<int>(type: "integer", nullable: false),
                    PhysicalCountDelta = table.Column<int>(type: "integer", nullable: false),
                    ReservedCountDelta = table.Column<int>(type: "integer", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TransactionReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsInitialLoad = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cassette_movements", x => x.Id);
                    table.CheckConstraint("ck_cassette_movements_currency", "\"Currency\" IN ('TRY', 'USD', 'EUR')");
                    table.CheckConstraint("ck_cassette_movements_delta", "\"PhysicalCountDelta\" <> 0 OR \"ReservedCountDelta\" <> 0");
                    table.CheckConstraint("ck_cassette_movements_denomination", "\"Denomination\" IN (20, 50, 100, 500)");
                    table.CheckConstraint("ck_cassette_movements_initial_load_type", "NOT \"IsInitialLoad\" OR \"Type\" = 'Load'");
                    table.CheckConstraint("ck_cassette_movements_type", "\"Type\" IN ('Load', 'Unload', 'Withdrawal', 'Deposit', 'Reservation', 'ReservationRelease', 'ReconciliationAdjustment')");
                    table.ForeignKey(
                        name: "FK_cassette_movements_atms_AtmId",
                        column: x => x.AtmId,
                        principalTable: "atms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cassette_movements_cassettes_CassetteId",
                        column: x => x.CassetteId,
                        principalTable: "cassettes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cassette_movements_AtmId_OccurredAt",
                table: "cassette_movements",
                columns: new[] { "AtmId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_cassette_movements_CassetteId",
                table: "cassette_movements",
                column: "CassetteId");

            migrationBuilder.CreateIndex(
                name: "IX_cassette_movements_TransactionReference",
                table: "cassette_movements",
                column: "TransactionReference");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cassette_movements");

            migrationBuilder.DropColumn(
                name: "InitialLoadCompletedAt",
                table: "atms");
        }
    }
}
