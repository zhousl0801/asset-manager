using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AssetKeeper.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CloseG0Correctness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF (SELECT COUNT(*) FROM ledgers) > 1 THEN
                        RAISE EXCEPTION 'G0 migration refused: multiple ledgers exist; reconcile their records before retrying';
                    END IF;
                END $$;
                """);

            migrationBuilder.AddColumn<int>(
                name: "singleton_slot",
                table: "ledgers",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "mutation_receipts",
                columns: table => new
                {
                    ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    response_json = table.Column<string>(type: "jsonb", nullable: false),
                    before_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mutation_receipts", x => new { x.ledger_id, x.request_id });
                    table.ForeignKey(
                        name: "FK_mutation_receipts_ledgers_ledger_id",
                        column: x => x.ledger_id,
                        principalTable: "ledgers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ledgers_singleton_slot",
                table: "ledgers",
                column: "singleton_slot",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_ledgers_singleton_slot",
                table: "ledgers",
                sql: "singleton_slot = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mutation_receipts");

            migrationBuilder.DropIndex(
                name: "IX_ledgers_singleton_slot",
                table: "ledgers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ledgers_singleton_slot",
                table: "ledgers");

            migrationBuilder.DropColumn(
                name: "singleton_slot",
                table: "ledgers");
        }
    }
}
