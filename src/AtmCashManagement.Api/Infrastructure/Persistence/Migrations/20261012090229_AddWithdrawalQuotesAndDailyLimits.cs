using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtmCashManagement.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWithdrawalQuotesAndDailyLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "card_daily_limit_usages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CardIdentifier = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ConfirmedAmount = table.Column<int>(type: "integer", nullable: false),
                    ReservedAmount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_card_daily_limit_usages", x => x.Id);
                    table.CheckConstraint("ck_card_daily_limit_usages_confirmed", "\"ConfirmedAmount\" >= 0");
                    table.CheckConstraint("ck_card_daily_limit_usages_currency", "\"Currency\" IN ('TRY', 'USD', 'EUR')");
                    table.CheckConstraint("ck_card_daily_limit_usages_identifier", "length(\"CardIdentifier\") = 64");
                    table.CheckConstraint("ck_card_daily_limit_usages_reserved", "\"ReservedAmount\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "withdrawal_quotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AtmId = table.Column<Guid>(type: "uuid", nullable: false),
                    CardIdentifier = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    RequestedAmount = table.Column<int>(type: "integer", nullable: false),
                    LimitBusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    LimitReservedAmount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClosedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_withdrawal_quotes", x => x.Id);
                    table.CheckConstraint("ck_withdrawal_quotes_card_identifier", "length(\"CardIdentifier\") = 64");
                    table.CheckConstraint("ck_withdrawal_quotes_currency", "\"Currency\" IN ('TRY', 'USD', 'EUR')");
                    table.CheckConstraint("ck_withdrawal_quotes_limit_reservation", "\"LimitReservedAmount\" > 0");
                    table.CheckConstraint("ck_withdrawal_quotes_requested_amount", "\"RequestedAmount\" > 0");
                    table.CheckConstraint("ck_withdrawal_quotes_status", "\"Status\" IN ('Active', 'Confirmed', 'Cancelled', 'Expired')");
                    table.CheckConstraint("ck_withdrawal_quotes_utc_window", "\"ExpiresAtUtc\" > \"CreatedAtUtc\"");
                    table.ForeignKey(
                        name: "FK_withdrawal_quotes_atms_AtmId",
                        column: x => x.AtmId,
                        principalTable: "atms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "quote_reservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uuid", nullable: false),
                    CassetteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Denomination = table.Column<int>(type: "integer", nullable: false),
                    ReservedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quote_reservations", x => x.Id);
                    table.CheckConstraint("ck_quote_reservations_count", "\"ReservedCount\" > 0");
                    table.CheckConstraint("ck_quote_reservations_currency", "\"Currency\" IN ('TRY', 'USD', 'EUR')");
                    table.CheckConstraint("ck_quote_reservations_denomination", "\"Denomination\" IN (20, 50, 100, 500)");
                    table.ForeignKey(
                        name: "FK_quote_reservations_cassettes_CassetteId",
                        column: x => x.CassetteId,
                        principalTable: "cassettes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_quote_reservations_withdrawal_quotes_QuoteId",
                        column: x => x.QuoteId,
                        principalTable: "withdrawal_quotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "withdrawal_quote_options",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Amount = table.Column<int>(type: "integer", nullable: false),
                    NoteCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_withdrawal_quote_options", x => x.Id);
                    table.CheckConstraint("ck_withdrawal_quote_options_amount", "\"Amount\" > 0");
                    table.CheckConstraint("ck_withdrawal_quote_options_currency", "\"Currency\" IN ('TRY', 'USD', 'EUR')");
                    table.CheckConstraint("ck_withdrawal_quote_options_kind", "\"Kind\" IN ('Exact', 'Lower', 'Upper')");
                    table.CheckConstraint("ck_withdrawal_quote_options_note_count", "\"NoteCount\" > 0");
                    table.ForeignKey(
                        name: "FK_withdrawal_quote_options_withdrawal_quotes_QuoteId",
                        column: x => x.QuoteId,
                        principalTable: "withdrawal_quotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cash_withdrawals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AtmId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uuid", nullable: false),
                    OptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CardIdentifier = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Amount = table.Column<int>(type: "integer", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cash_withdrawals", x => x.Id);
                    table.CheckConstraint("ck_cash_withdrawals_amount", "\"Amount\" > 0");
                    table.CheckConstraint("ck_cash_withdrawals_currency", "\"Currency\" IN ('TRY', 'USD', 'EUR')");
                    table.CheckConstraint("ck_cash_withdrawals_identifier", "length(\"CardIdentifier\") = 64");
                    table.ForeignKey(
                        name: "FK_cash_withdrawals_atms_AtmId",
                        column: x => x.AtmId,
                        principalTable: "atms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cash_withdrawals_withdrawal_quote_options_OptionId",
                        column: x => x.OptionId,
                        principalTable: "withdrawal_quote_options",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cash_withdrawals_withdrawal_quotes_QuoteId",
                        column: x => x.QuoteId,
                        principalTable: "withdrawal_quotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "withdrawal_quote_option_allocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CassetteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Denomination = table.Column<int>(type: "integer", nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_withdrawal_quote_option_allocations", x => x.Id);
                    table.CheckConstraint("ck_withdrawal_quote_option_allocations_count", "\"Count\" > 0");
                    table.CheckConstraint("ck_withdrawal_quote_option_allocations_currency", "\"Currency\" IN ('TRY', 'USD', 'EUR')");
                    table.CheckConstraint("ck_withdrawal_quote_option_allocations_denomination", "\"Denomination\" IN (20, 50, 100, 500)");
                    table.ForeignKey(
                        name: "FK_withdrawal_quote_option_allocations_cassettes_CassetteId",
                        column: x => x.CassetteId,
                        principalTable: "cassettes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_withdrawal_quote_option_allocations_withdrawal_quote_option~",
                        column: x => x.OptionId,
                        principalTable: "withdrawal_quote_options",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "confirm_idempotency_records",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    QuoteId = table.Column<Guid>(type: "uuid", nullable: false),
                    SelectedOptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    WithdrawalId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_confirm_idempotency_records", x => x.Id);
                    table.ForeignKey(
                        name: "FK_confirm_idempotency_records_cash_withdrawals_WithdrawalId",
                        column: x => x.WithdrawalId,
                        principalTable: "cash_withdrawals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_confirm_idempotency_records_withdrawal_quote_options_Select~",
                        column: x => x.SelectedOptionId,
                        principalTable: "withdrawal_quote_options",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_confirm_idempotency_records_withdrawal_quotes_QuoteId",
                        column: x => x.QuoteId,
                        principalTable: "withdrawal_quotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_card_daily_limit_usages_CardIdentifier_Currency_BusinessDate",
                table: "card_daily_limit_usages",
                columns: new[] { "CardIdentifier", "Currency", "BusinessDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cash_withdrawals_AtmId",
                table: "cash_withdrawals",
                column: "AtmId");

            migrationBuilder.CreateIndex(
                name: "IX_cash_withdrawals_OptionId",
                table: "cash_withdrawals",
                column: "OptionId");

            migrationBuilder.CreateIndex(
                name: "IX_cash_withdrawals_QuoteId",
                table: "cash_withdrawals",
                column: "QuoteId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_confirm_idempotency_records_Key",
                table: "confirm_idempotency_records",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_confirm_idempotency_records_QuoteId",
                table: "confirm_idempotency_records",
                column: "QuoteId");

            migrationBuilder.CreateIndex(
                name: "IX_confirm_idempotency_records_SelectedOptionId",
                table: "confirm_idempotency_records",
                column: "SelectedOptionId");

            migrationBuilder.CreateIndex(
                name: "IX_confirm_idempotency_records_WithdrawalId",
                table: "confirm_idempotency_records",
                column: "WithdrawalId");

            migrationBuilder.CreateIndex(
                name: "IX_quote_reservations_CassetteId",
                table: "quote_reservations",
                column: "CassetteId");

            migrationBuilder.CreateIndex(
                name: "IX_quote_reservations_QuoteId_CassetteId_Currency_Denomination",
                table: "quote_reservations",
                columns: new[] { "QuoteId", "CassetteId", "Currency", "Denomination" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_withdrawal_quote_option_allocations_CassetteId",
                table: "withdrawal_quote_option_allocations",
                column: "CassetteId");

            migrationBuilder.CreateIndex(
                name: "IX_withdrawal_quote_option_allocations_OptionId_CassetteId_Cur~",
                table: "withdrawal_quote_option_allocations",
                columns: new[] { "OptionId", "CassetteId", "Currency", "Denomination" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_withdrawal_quote_options_QuoteId_Kind",
                table: "withdrawal_quote_options",
                columns: new[] { "QuoteId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_withdrawal_quotes_AtmId_Status_ExpiresAtUtc",
                table: "withdrawal_quotes",
                columns: new[] { "AtmId", "Status", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_withdrawal_quotes_CardIdentifier_LimitBusinessDate_Currency",
                table: "withdrawal_quotes",
                columns: new[] { "CardIdentifier", "LimitBusinessDate", "Currency" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "card_daily_limit_usages");

            migrationBuilder.DropTable(
                name: "confirm_idempotency_records");

            migrationBuilder.DropTable(
                name: "quote_reservations");

            migrationBuilder.DropTable(
                name: "withdrawal_quote_option_allocations");

            migrationBuilder.DropTable(
                name: "cash_withdrawals");

            migrationBuilder.DropTable(
                name: "withdrawal_quote_options");

            migrationBuilder.DropTable(
                name: "withdrawal_quotes");
        }
    }
}
