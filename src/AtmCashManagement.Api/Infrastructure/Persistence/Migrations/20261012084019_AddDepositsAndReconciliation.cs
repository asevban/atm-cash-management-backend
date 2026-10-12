using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtmCashManagement.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDepositsAndReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cash_deposits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AtmId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UnrecognizedNoteCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cash_deposits", x => x.Id);
                    table.CheckConstraint("ck_cash_deposits_unrecognized_count", "\"UnrecognizedNoteCount\" >= 0");
                    table.ForeignKey(
                        name: "FK_cash_deposits_atms_AtmId",
                        column: x => x.AtmId,
                        principalTable: "atms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cash_reconciliations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AtmId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cash_reconciliations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cash_reconciliations_atms_AtmId",
                        column: x => x.AtmId,
                        principalTable: "atms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cash_deposit_notes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CashDepositId = table.Column<Guid>(type: "uuid", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Denomination = table.Column<int>(type: "integer", nullable: false),
                    InsertedCount = table.Column<int>(type: "integer", nullable: false),
                    AcceptedCount = table.Column<int>(type: "integer", nullable: false),
                    ReturnedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cash_deposit_notes", x => x.Id);
                    table.CheckConstraint("ck_cash_deposit_notes_counts", "\"AcceptedCount\" >= 0 AND \"ReturnedCount\" >= 0 AND \"InsertedCount\" = \"AcceptedCount\" + \"ReturnedCount\"");
                    table.CheckConstraint("ck_cash_deposit_notes_currency", "\"Currency\" IN ('TRY', 'USD', 'EUR')");
                    table.CheckConstraint("ck_cash_deposit_notes_denomination", "\"Denomination\" IN (20, 50, 100, 500)");
                    table.CheckConstraint("ck_cash_deposit_notes_inserted_count", "\"InsertedCount\" > 0");
                    table.ForeignKey(
                        name: "FK_cash_deposit_notes_cash_deposits_CashDepositId",
                        column: x => x.CashDepositId,
                        principalTable: "cash_deposits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cash_reconciliation_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CashReconciliationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CassetteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Denomination = table.Column<int>(type: "integer", nullable: false),
                    ExpectedCount = table.Column<int>(type: "integer", nullable: false),
                    CountedCount = table.Column<int>(type: "integer", nullable: false),
                    DifferenceCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cash_reconciliation_lines", x => x.Id);
                    table.CheckConstraint("ck_cash_reconciliation_lines_counted", "\"CountedCount\" BETWEEN 0 AND 100");
                    table.CheckConstraint("ck_cash_reconciliation_lines_currency", "\"Currency\" IN ('TRY', 'USD', 'EUR')");
                    table.CheckConstraint("ck_cash_reconciliation_lines_denomination", "\"Denomination\" IN (20, 50, 100, 500)");
                    table.CheckConstraint("ck_cash_reconciliation_lines_difference", "\"DifferenceCount\" = \"CountedCount\" - \"ExpectedCount\"");
                    table.CheckConstraint("ck_cash_reconciliation_lines_expected", "\"ExpectedCount\" BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "FK_cash_reconciliation_lines_cash_reconciliations_CashReconcil~",
                        column: x => x.CashReconciliationId,
                        principalTable: "cash_reconciliations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_cash_reconciliation_lines_cassettes_CassetteId",
                        column: x => x.CassetteId,
                        principalTable: "cassettes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cash_deposit_notes_CashDepositId_Currency_Denomination",
                table: "cash_deposit_notes",
                columns: new[] { "CashDepositId", "Currency", "Denomination" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cash_deposits_AtmId_OccurredAt",
                table: "cash_deposits",
                columns: new[] { "AtmId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_cash_reconciliation_lines_CashReconciliationId_CassetteId_C~",
                table: "cash_reconciliation_lines",
                columns: new[] { "CashReconciliationId", "CassetteId", "Currency", "Denomination" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cash_reconciliation_lines_CassetteId",
                table: "cash_reconciliation_lines",
                column: "CassetteId");

            migrationBuilder.CreateIndex(
                name: "IX_cash_reconciliations_AtmId_OccurredAt",
                table: "cash_reconciliations",
                columns: new[] { "AtmId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cash_deposit_notes");

            migrationBuilder.DropTable(
                name: "cash_reconciliation_lines");

            migrationBuilder.DropTable(
                name: "cash_deposits");

            migrationBuilder.DropTable(
                name: "cash_reconciliations");
        }
    }
}
