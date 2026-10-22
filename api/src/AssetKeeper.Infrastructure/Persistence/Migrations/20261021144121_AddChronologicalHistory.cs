using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AssetKeeper.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChronologicalHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "next_fact_sequence",
                table: "ledgers",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "version",
                table: "ledgers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "business_sequence",
                table: "ledger_entries",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "related_cash_adjustment_id",
                table: "ledger_entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "business_sequence",
                table: "cash_adjustments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<bool>(
                name: "is_reversed",
                table: "cash_adjustments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "recorded_difference",
                table: "cash_adjustments",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "version",
                table: "cash_adjustments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "history_confirmed",
                table: "cash_accounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "opening_balance",
                table: "cash_accounts",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "opening_date",
                table: "cash_accounts",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "business_sequence",
                table: "asset_transactions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<bool>(
                name: "is_reversed",
                table: "asset_transactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "needs_date_confirmation",
                table: "asset_transactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "price_date",
                table: "asset_transactions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "related_cash_adjustment_id",
                table: "asset_transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "version",
                table: "asset_transactions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "price_observations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    fund_position_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: false),
                    source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    obtained_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    revision_order = table.Column<long>(type: "bigint", nullable: false),
                    is_superseded = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_price_observations", x => x.id);
                    table.ForeignKey(
                        name: "FK_price_observations_asset_transactions_source_transaction_id",
                        column: x => x.source_transaction_id,
                        principalTable: "asset_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_price_observations_fund_positions_fund_position_id",
                        column: x => x.fund_position_id,
                        principalTable: "fund_positions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_price_observations_ledgers_ledger_id",
                        column: x => x.ledger_id,
                        principalTable: "ledgers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_price_observations_fund_position_id",
                table: "price_observations",
                column: "fund_position_id");

            migrationBuilder.CreateIndex(
                name: "IX_price_observations_ledger_id_asset_type_fund_position_id_ef~",
                table: "price_observations",
                columns: new[] { "ledger_id", "asset_type", "fund_position_id", "effective_date" });

            migrationBuilder.CreateIndex(
                name: "IX_price_observations_source_transaction_id",
                table: "price_observations",
                column: "source_transaction_id");

            migrationBuilder.Sql("""
                -- Import order is persisted once. It does not infer unknown holding dates.
                CREATE TEMP TABLE g1_import_order ON COMMIT DROP AS
                SELECT id, ledger_id, source, row_number() OVER (PARTITION BY ledger_id ORDER BY created_at, source, id) AS sequence
                FROM (
                    SELECT id, ledger_id, created_at, 'entry' AS source FROM ledger_entries
                    UNION ALL SELECT id, ledger_id, created_at, 'adjustment' FROM cash_adjustments
                    UNION ALL SELECT id, ledger_id, created_at, 'asset' FROM asset_transactions
                ) facts;
                UPDATE ledger_entries e SET business_sequence = f.sequence FROM g1_import_order f WHERE f.source = 'entry' AND e.id = f.id;
                UPDATE cash_adjustments a SET business_sequence = f.sequence, recorded_difference = a.difference FROM g1_import_order f WHERE f.source = 'adjustment' AND a.id = f.id;
                UPDATE asset_transactions a SET business_sequence = f.sequence FROM g1_import_order f WHERE f.source = 'asset' AND a.id = f.id;
                UPDATE ledgers l SET next_fact_sequence = COALESCE((SELECT MAX(sequence) FROM g1_import_order f WHERE f.ledger_id = l.id), 0);

                -- The old amount/cash-effect contract permits exact reconstruction, not guessing.
                WITH opening AS (
                    SELECT c.id, l.start_date, c.balance
                        - COALESCE((SELECT SUM(CASE WHEN e.kind = 'Income' THEN e.amount ELSE -e.amount END) FROM ledger_entries e WHERE e.ledger_id = c.ledger_id AND e.status = 'Active'), 0)
                        - COALESCE((SELECT SUM(t.cash_effect) FROM asset_transactions t WHERE t.ledger_id = c.ledger_id), 0)
                        - COALESCE((SELECT SUM(a.difference) FROM cash_adjustments a WHERE a.ledger_id = c.ledger_id), 0) AS balance
                    FROM cash_accounts c JOIN ledgers l ON l.id = c.ledger_id
                )
                UPDATE cash_accounts c SET opening_balance = CASE WHEN o.balance BETWEEN 0 AND 1000000000 THEN o.balance END,
                    opening_date = o.start_date, history_confirmed = (o.balance BETWEEN 0 AND 1000000000)
                FROM opening o WHERE c.id = o.id;

                UPDATE asset_transactions SET needs_date_confirmation = true WHERE kind IN ('FundOpening', 'GoldCorrection');
                UPDATE asset_transactions SET price_date = business_date WHERE kind IN ('FundOpening', 'GoldCorrection', 'FundQuote', 'GoldQuote');
                INSERT INTO price_observations (id, ledger_id, asset_type, fund_position_id, source_transaction_id, price, effective_date, source, obtained_at, revision_order, is_superseded)
                SELECT gen_random_uuid(), ledger_id, asset_type, fund_position_id, id, price, price_date, 'migration:recorded-quote', created_at, business_sequence, false
                FROM asset_transactions WHERE price_date IS NOT NULL AND price > 0;
                -- A known cached price is linked to its recorded quote when possible.
                -- Otherwise create an explicit, editable quote fact; never create an orphan price.
                CREATE TEMP TABLE g1_cached_quotes ON COMMIT DROP AS
                WITH cached AS (
                    SELECT p.ledger_id, 'Fund' AS asset_type, p.id AS position_id, p.net_value AS price,
                        p.net_value_date AS effective_date, p.updated_at AS obtained_at, p.name AS subject
                    FROM fund_positions p
                    UNION ALL
                    SELECT g.ledger_id, 'Gold', NULL::uuid, g.quote, g.quote_date, g.updated_at, '积存金'
                    FROM gold_holdings g
                ), attributed AS (
                    SELECT c.*, (SELECT q.source_transaction_id FROM price_observations q
                        WHERE q.ledger_id = c.ledger_id AND q.asset_type = c.asset_type
                            AND q.fund_position_id IS NOT DISTINCT FROM c.position_id
                            AND q.price = c.price AND q.effective_date = c.effective_date
                        ORDER BY q.revision_order DESC LIMIT 1) AS matched_transaction_id
                    FROM cached c
                )
                SELECT a.*, COALESCE(a.matched_transaction_id, gen_random_uuid()) AS transaction_id,
                    l.next_fact_sequence + row_number() OVER (PARTITION BY a.ledger_id ORDER BY a.asset_type, a.position_id) AS sequence
                FROM attributed a JOIN ledgers l ON l.id = a.ledger_id;

                INSERT INTO asset_transactions (id, ledger_id, request_id, asset_type, kind, fund_position_id, quantity,
                    amount, cash_effect, price, subject, note, business_date, created_at, business_sequence, price_date,
                    needs_date_confirmation, is_reversed, related_cash_adjustment_id, version)
                SELECT transaction_id, ledger_id, gen_random_uuid(), asset_type,
                    CASE WHEN asset_type = 'Fund' THEN 'FundQuote' ELSE 'GoldQuote' END, position_id, NULL,
                    0, 0, price, subject, '迁移保留的最新已知行情', effective_date, obtained_at, sequence, effective_date,
                    false, false, NULL, 0
                FROM g1_cached_quotes WHERE matched_transaction_id IS NULL;

                INSERT INTO price_observations (id, ledger_id, asset_type, fund_position_id, source_transaction_id,
                    price, effective_date, source, obtained_at, revision_order, is_superseded)
                SELECT gen_random_uuid(), ledger_id, asset_type, position_id, transaction_id,
                    price, effective_date, 'migration:last-known', obtained_at, sequence, false
                FROM g1_cached_quotes;
                UPDATE ledgers l SET next_fact_sequence = GREATEST(l.next_fact_sequence,
                    COALESCE((SELECT MAX(revision_order) FROM price_observations p WHERE p.ledger_id = l.id), 0));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "price_observations");

            migrationBuilder.DropColumn(
                name: "next_fact_sequence",
                table: "ledgers");

            migrationBuilder.DropColumn(
                name: "version",
                table: "ledgers");

            migrationBuilder.DropColumn(
                name: "business_sequence",
                table: "ledger_entries");

            migrationBuilder.DropColumn(
                name: "related_cash_adjustment_id",
                table: "ledger_entries");

            migrationBuilder.DropColumn(
                name: "business_sequence",
                table: "cash_adjustments");

            migrationBuilder.DropColumn(
                name: "is_reversed",
                table: "cash_adjustments");

            migrationBuilder.DropColumn(
                name: "recorded_difference",
                table: "cash_adjustments");

            migrationBuilder.DropColumn(
                name: "version",
                table: "cash_adjustments");

            migrationBuilder.DropColumn(
                name: "history_confirmed",
                table: "cash_accounts");

            migrationBuilder.DropColumn(
                name: "opening_balance",
                table: "cash_accounts");

            migrationBuilder.DropColumn(
                name: "opening_date",
                table: "cash_accounts");

            migrationBuilder.DropColumn(
                name: "business_sequence",
                table: "asset_transactions");

            migrationBuilder.DropColumn(
                name: "is_reversed",
                table: "asset_transactions");

            migrationBuilder.DropColumn(
                name: "needs_date_confirmation",
                table: "asset_transactions");

            migrationBuilder.DropColumn(
                name: "price_date",
                table: "asset_transactions");

            migrationBuilder.DropColumn(
                name: "related_cash_adjustment_id",
                table: "asset_transactions");

            migrationBuilder.DropColumn(
                name: "version",
                table: "asset_transactions");
        }
    }
}
