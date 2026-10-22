using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AssetKeeper.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQuoteSourcesAndSyncStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_quote_attempt_at",
                table: "gold_holdings",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_quote_error",
                table: "gold_holdings",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_quote_source",
                table: "gold_holdings",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_quote_attempt_at",
                table: "fund_positions",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_quote_error",
                table: "fund_positions",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_quote_source",
                table: "fund_positions",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "quote_source",
                table: "asset_transactions",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_quote_attempt_at",
                table: "gold_holdings");

            migrationBuilder.DropColumn(
                name: "last_quote_error",
                table: "gold_holdings");

            migrationBuilder.DropColumn(
                name: "last_quote_source",
                table: "gold_holdings");

            migrationBuilder.DropColumn(
                name: "last_quote_attempt_at",
                table: "fund_positions");

            migrationBuilder.DropColumn(
                name: "last_quote_error",
                table: "fund_positions");

            migrationBuilder.DropColumn(
                name: "last_quote_source",
                table: "fund_positions");

            migrationBuilder.DropColumn(
                name: "quote_source",
                table: "asset_transactions");
        }
    }
}
