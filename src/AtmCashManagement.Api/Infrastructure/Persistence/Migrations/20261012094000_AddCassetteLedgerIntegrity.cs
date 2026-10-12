using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtmCashManagement.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCassetteLedgerIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_cassette_movements_event",
                table: "cassette_movements",
                columns: new[] { "TransactionReference", "CassetteId", "Type", "Currency", "Denomination" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_cassette_movements_delta_semantics",
                table: "cassette_movements",
                sql: "((\"Type\" IN ('Load', 'Deposit') AND \"PhysicalCountDelta\" > 0 AND \"ReservedCountDelta\" = 0) OR (\"Type\" = 'Reservation' AND \"PhysicalCountDelta\" = 0 AND \"ReservedCountDelta\" > 0) OR (\"Type\" = 'ReservationRelease' AND \"PhysicalCountDelta\" = 0 AND \"ReservedCountDelta\" < 0) OR (\"Type\" = 'Withdrawal' AND \"PhysicalCountDelta\" < 0 AND \"ReservedCountDelta\" <= 0) OR (\"Type\" = 'ReconciliationAdjustment' AND \"PhysicalCountDelta\" <> 0 AND \"ReservedCountDelta\" = 0) OR (\"Type\" = 'Unload' AND \"PhysicalCountDelta\" < 0 AND \"ReservedCountDelta\" = 0))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_cassette_movements_event",
                table: "cassette_movements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_cassette_movements_delta_semantics",
                table: "cassette_movements");
        }
    }
}
