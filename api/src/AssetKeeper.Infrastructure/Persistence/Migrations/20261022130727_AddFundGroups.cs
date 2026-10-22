using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AssetKeeper.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFundGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "fund_group_id",
                table: "fund_positions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "fund_groups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fund_groups", x => x.id);
                    table.ForeignKey(
                        name: "FK_fund_groups_ledgers_ledger_id",
                        column: x => x.ledger_id,
                        principalTable: "ledgers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_fund_positions_fund_group_id",
                table: "fund_positions",
                column: "fund_group_id");

            migrationBuilder.CreateIndex(
                name: "IX_fund_groups_ledger_id_name",
                table: "fund_groups",
                columns: new[] { "ledger_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fund_groups_ledger_id_sort_order",
                table: "fund_groups",
                columns: new[] { "ledger_id", "sort_order" });

            migrationBuilder.AddForeignKey(
                name: "FK_fund_positions_fund_groups_fund_group_id",
                table: "fund_positions",
                column: "fund_group_id",
                principalTable: "fund_groups",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_fund_positions_fund_groups_fund_group_id",
                table: "fund_positions");

            migrationBuilder.DropTable(
                name: "fund_groups");

            migrationBuilder.DropIndex(
                name: "IX_fund_positions_fund_group_id",
                table: "fund_positions");

            migrationBuilder.DropColumn(
                name: "fund_group_id",
                table: "fund_positions");
        }
    }
}
