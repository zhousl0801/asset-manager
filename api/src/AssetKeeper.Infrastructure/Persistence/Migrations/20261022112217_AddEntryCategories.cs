using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AssetKeeper.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEntryCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "entry_categories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    name = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    system_key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entry_categories", x => x.id);
                    table.ForeignKey(
                        name: "FK_entry_categories_ledgers_ledger_id",
                        column: x => x.ledger_id,
                        principalTable: "ledgers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_entry_categories_ledger_id_kind_name",
                table: "entry_categories",
                columns: new[] { "ledger_id", "kind", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_entry_categories_ledger_id_kind_sort_order",
                table: "entry_categories",
                columns: new[] { "ledger_id", "kind", "sort_order" });

            migrationBuilder.Sql(
                """
                INSERT INTO entry_categories (id, ledger_id, kind, name, sort_order, is_active, system_key, created_at, updated_at, version)
                SELECT gen_random_uuid(), ledger.id, defaults.kind, defaults.name, defaults.sort_order, TRUE,
                    defaults.system_key, now(), now(), 0
                FROM ledgers AS ledger
                CROSS JOIN (VALUES
                    ('Expense', '餐饮', 0, NULL),
                    ('Expense', '购物', 1, NULL),
                    ('Expense', '日用', 2, NULL),
                    ('Expense', '交通', 3, NULL),
                    ('Expense', '居家', 4, NULL),
                    ('Expense', '服饰', 5, NULL),
                    ('Expense', '娱乐', 6, NULL),
                    ('Expense', '通讯', 7, NULL),
                    ('Expense', '医疗', 8, NULL),
                    ('Expense', '学习', 9, NULL),
                    ('Expense', '运动', 10, NULL),
                    ('Expense', '旅行', 11, NULL),
                    ('Expense', '宠物', 12, NULL),
                    ('Expense', '人情', 13, NULL),
                    ('Expense', '其他', 14, NULL),
                    ('Income', '工资', 0, 'salary'),
                    ('Income', '奖金', 1, NULL),
                    ('Income', '其他收入', 2, 'other-income')
                ) AS defaults(kind, name, sort_order, system_key);
                """);

            migrationBuilder.Sql(
                """
                INSERT INTO entry_categories (id, ledger_id, kind, name, sort_order, is_active, system_key, created_at, updated_at, version)
                SELECT gen_random_uuid(), existing.ledger_id, existing.kind, existing.category, 100, TRUE, NULL, now(), now(), 0
                FROM (
                    SELECT DISTINCT ledger_id, kind, category
                    FROM ledger_entries
                    WHERE category IS NOT NULL AND category <> ''
                ) AS existing
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM entry_categories AS category
                    WHERE category.ledger_id = existing.ledger_id
                        AND category.kind = existing.kind
                        AND category.name = existing.category
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "entry_categories");
        }
    }
}
