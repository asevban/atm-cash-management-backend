using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AtmCashManagement.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReconciliationApprovalWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApprovedAt",
                table: "cash_reconciliations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedBy",
                table: "cash_reconciliations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "cash_reconciliations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EurToTryRateSnapshot",
                table: "cash_reconciliations",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "cash_reconciliations",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalAbsoluteDifferenceTry",
                table: "cash_reconciliations",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TryToTryRateSnapshot",
                table: "cash_reconciliations",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UsdToTryRateSnapshot",
                table: "cash_reconciliations",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE cash_reconciliations SET \"CreatedBy\" = 'legacy-unknown', \"Status\" = 'Completed'");

            migrationBuilder.AlterColumn<string>(
                name: "CreatedBy",
                table: "cash_reconciliations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "cash_reconciliations",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30,
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "exchange_rates",
                columns: table => new
                {
                    BaseCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    QuoteCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Rate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exchange_rates", x => new { x.BaseCurrency, x.QuoteCurrency });
                    table.CheckConstraint("ck_exchange_rates_supported_pair", "((\"BaseCurrency\" = 'TRY' AND \"QuoteCurrency\" = 'TRY' AND \"Rate\" = 1) OR (\"BaseCurrency\" IN ('USD', 'EUR') AND \"QuoteCurrency\" = 'TRY' AND \"Rate\" > 0))");
                });

            migrationBuilder.InsertData(
                table: "exchange_rates",
                columns: new[] { "BaseCurrency", "QuoteCurrency", "Rate" },
                values: new object[,]
                {
                    { "EUR", "TRY", 38m },
                    { "TRY", "TRY", 1m },
                    { "USD", "TRY", 35m }
                });

            migrationBuilder.CreateIndex(
                name: "IX_cash_reconciliations_pending_approval",
                table: "cash_reconciliations",
                column: "AtmId",
                unique: true,
                filter: "\"Status\" = 'AwaitingApproval'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_cash_reconciliations_status_approval",
                table: "cash_reconciliations",
                sql: "((\"Status\" = 'Approved' AND \"ApprovedBy\" IS NOT NULL AND \"ApprovedAt\" IS NOT NULL AND lower(\"ApprovedBy\") <> lower(\"CreatedBy\")) OR (\"Status\" <> 'Approved' AND \"ApprovedBy\" IS NULL AND \"ApprovedAt\" IS NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_cash_reconciliations_threshold_status",
                table: "cash_reconciliations",
                sql: "((\"Status\" = 'Completed' AND (\"TotalAbsoluteDifferenceTry\" IS NULL OR \"TotalAbsoluteDifferenceTry\" <= 1000)) OR (\"Status\" IN ('AwaitingApproval', 'Approved') AND \"TotalAbsoluteDifferenceTry\" IS NOT NULL AND \"TotalAbsoluteDifferenceTry\" > 1000))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_cash_reconciliations_valuation",
                table: "cash_reconciliations",
                sql: "(\"TotalAbsoluteDifferenceTry\" IS NULL OR (\"TotalAbsoluteDifferenceTry\" >= 0 AND \"TryToTryRateSnapshot\" IS NOT NULL AND \"TryToTryRateSnapshot\" = 1 AND \"UsdToTryRateSnapshot\" IS NOT NULL AND \"UsdToTryRateSnapshot\" > 0 AND \"EurToTryRateSnapshot\" IS NOT NULL AND \"EurToTryRateSnapshot\" > 0))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "exchange_rates");

            migrationBuilder.DropIndex(
                name: "IX_cash_reconciliations_pending_approval",
                table: "cash_reconciliations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_cash_reconciliations_status_approval",
                table: "cash_reconciliations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_cash_reconciliations_threshold_status",
                table: "cash_reconciliations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_cash_reconciliations_valuation",
                table: "cash_reconciliations");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "cash_reconciliations");

            migrationBuilder.DropColumn(
                name: "ApprovedBy",
                table: "cash_reconciliations");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "cash_reconciliations");

            migrationBuilder.DropColumn(
                name: "EurToTryRateSnapshot",
                table: "cash_reconciliations");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "cash_reconciliations");

            migrationBuilder.DropColumn(
                name: "TotalAbsoluteDifferenceTry",
                table: "cash_reconciliations");

            migrationBuilder.DropColumn(
                name: "TryToTryRateSnapshot",
                table: "cash_reconciliations");

            migrationBuilder.DropColumn(
                name: "UsdToTryRateSnapshot",
                table: "cash_reconciliations");
        }
    }
}
