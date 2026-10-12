using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtmCashManagement.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "atms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_atms", x => x.Id);
                    table.CheckConstraint("ck_atms_state", "\"State\" IN ('Unconfigured', 'Configured', 'Loaded', 'InService', 'Maintenance')");
                });

            migrationBuilder.CreateTable(
                name: "cassettes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AtmId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    Denomination = table.Column<int>(type: "integer", nullable: true),
                    Capacity = table.Column<int>(type: "integer", nullable: false, defaultValue: 100)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cassettes", x => x.Id);
                    table.CheckConstraint("ck_cassettes_capacity", "\"Capacity\" BETWEEN 1 AND 100");
                    table.CheckConstraint("ck_cassettes_currency", "\"Currency\" IS NULL OR \"Currency\" IN ('TRY', 'USD', 'EUR')");
                    table.CheckConstraint("ck_cassettes_number", "\"Number\" BETWEEN 1 AND 5");
                    table.CheckConstraint("ck_cassettes_type", "\"Type\" IN (0, 1)");
                    table.CheckConstraint("ck_cassettes_type_configuration", "(\"Type\" = 0 AND \"Currency\" IS NULL AND \"Denomination\" IS NULL) OR (\"Type\" = 1 AND \"Currency\" IS NOT NULL AND \"Denomination\" IN (20, 50, 100, 500))");
                    table.ForeignKey(
                        name: "FK_cassettes_atms_AtmId",
                        column: x => x.AtmId,
                        principalTable: "atms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cassette_balances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CassetteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Denomination = table.Column<int>(type: "integer", nullable: false),
                    PhysicalCount = table.Column<int>(type: "integer", nullable: false),
                    ReservedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cassette_balances", x => x.Id);
                    table.CheckConstraint("ck_cassette_balances_currency", "\"Currency\" IN ('TRY', 'USD', 'EUR')");
                    table.CheckConstraint("ck_cassette_balances_denomination", "\"Denomination\" IN (20, 50, 100, 500)");
                    table.CheckConstraint("ck_cassette_balances_physical_count", "\"PhysicalCount\" BETWEEN 0 AND 100");
                    table.CheckConstraint("ck_cassette_balances_reserved_count", "\"ReservedCount\" BETWEEN 0 AND \"PhysicalCount\"");
                    table.ForeignKey(
                        name: "FK_cassette_balances_cassettes_CassetteId",
                        column: x => x.CassetteId,
                        principalTable: "cassettes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_atms_Code",
                table: "atms",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cassette_balances_CassetteId_Currency_Denomination",
                table: "cassette_balances",
                columns: new[] { "CassetteId", "Currency", "Denomination" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cassettes_AtmId",
                table: "cassettes",
                column: "AtmId",
                unique: true,
                filter: "\"Type\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_cassettes_AtmId_Number",
                table: "cassettes",
                columns: new[] { "AtmId", "Number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cassette_balances");

            migrationBuilder.DropTable(
                name: "cassettes");

            migrationBuilder.DropTable(
                name: "atms");
        }
    }
}
