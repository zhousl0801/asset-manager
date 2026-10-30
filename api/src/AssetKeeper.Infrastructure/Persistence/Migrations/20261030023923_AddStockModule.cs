using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AssetKeeper.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStockModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "stock_position_id",
                table: "price_observations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "stock_not_held",
                table: "ledgers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "stock_position_id",
                table: "asset_transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "stock_positions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    market = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    shares = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    price_date = table.Column<DateOnly>(type: "date", nullable: false),
                    previous_close = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    previous_close_date = table.Column<DateOnly>(type: "date", nullable: true),
                    realized_profit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    last_quote_attempt_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    last_quote_source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    last_quote_error = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_positions", x => x.id);
                    table.ForeignKey(
                        name: "FK_stock_positions_ledgers_ledger_id",
                        column: x => x.ledger_id,
                        principalTable: "ledgers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_price_observations_ledger_id_asset_type_stock_position_id_e~",
                table: "price_observations",
                columns: new[] { "ledger_id", "asset_type", "stock_position_id", "effective_date" });

            migrationBuilder.CreateIndex(
                name: "IX_price_observations_stock_position_id",
                table: "price_observations",
                column: "stock_position_id");

            migrationBuilder.CreateIndex(
                name: "IX_asset_transactions_stock_position_id",
                table: "asset_transactions",
                column: "stock_position_id");

            migrationBuilder.CreateIndex(
                name: "IX_stock_positions_ledger_id",
                table: "stock_positions",
                column: "ledger_id");

            migrationBuilder.CreateIndex(
                name: "IX_stock_positions_ledger_id_market_code",
                table: "stock_positions",
                columns: new[] { "ledger_id", "market", "code" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_asset_transactions_stock_positions_stock_position_id",
                table: "asset_transactions",
                column: "stock_position_id",
                principalTable: "stock_positions",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_price_observations_stock_positions_stock_position_id",
                table: "price_observations",
                column: "stock_position_id",
                principalTable: "stock_positions",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_asset_transactions_stock_positions_stock_position_id",
                table: "asset_transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_price_observations_stock_positions_stock_position_id",
                table: "price_observations");

            migrationBuilder.DropTable(
                name: "stock_positions");

            migrationBuilder.DropIndex(
                name: "IX_price_observations_ledger_id_asset_type_stock_position_id_e~",
                table: "price_observations");

            migrationBuilder.DropIndex(
                name: "IX_price_observations_stock_position_id",
                table: "price_observations");

            migrationBuilder.DropIndex(
                name: "IX_asset_transactions_stock_position_id",
                table: "asset_transactions");

            migrationBuilder.DropColumn(
                name: "stock_position_id",
                table: "price_observations");

            migrationBuilder.DropColumn(
                name: "stock_not_held",
                table: "ledgers");

            migrationBuilder.DropColumn(
                name: "stock_position_id",
                table: "asset_transactions");
        }
    }
}
