using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WahSpices.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DynamicExpenseCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Step 1: add temp column
            migrationBuilder.Sql("ALTER TABLE ExpenseEntries ADD CategoryTemp NVARCHAR(100) NULL;");
            // Step 2: copy converted values (each statement its own batch)
            migrationBuilder.Sql(@"UPDATE ExpenseEntries SET CategoryTemp = CASE CAST(Category AS TINYINT)
                WHEN 0 THEN 'Rent'
                WHEN 1 THEN 'Electricity'
                WHEN 2 THEN 'Transport'
                WHEN 3 THEN 'Salaries'
                WHEN 4 THEN 'RawMaterial'
                WHEN 5 THEN 'Marketing'
                WHEN 6 THEN 'Maintenance'
                WHEN 7 THEN 'Miscellaneous'
                WHEN 8 THEN 'License'
                ELSE 'Miscellaneous' END;");
            // Step 3: drop old, rename new, set not null
            migrationBuilder.Sql("ALTER TABLE ExpenseEntries DROP COLUMN Category;");
            migrationBuilder.Sql("EXEC sp_rename 'ExpenseEntries.CategoryTemp', 'Category', 'COLUMN';");
            migrationBuilder.Sql("ALTER TABLE ExpenseEntries ALTER COLUMN Category NVARCHAR(100) NOT NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "Category",
                table: "ExpenseBudgets",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(byte),
                oldType: "tinyint");

            migrationBuilder.CreateTable(
                name: "ExpenseCategoryConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Color = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseCategoryConfigs", x => x.Id);
                });

            // Seed default categories
            migrationBuilder.Sql(@"
                INSERT INTO ExpenseCategoryConfigs (Name, Color, IsActive, CreatedAt, IsDeleted, CreatedBy)
                VALUES
                    ('Rent',          'blue',   1, GETUTCDATE(), 0, 'system'),
                    ('Electricity',   'yellow', 1, GETUTCDATE(), 0, 'system'),
                    ('Transport',     'purple', 1, GETUTCDATE(), 0, 'system'),
                    ('Salaries',      'green',  1, GETUTCDATE(), 0, 'system'),
                    ('RawMaterial',   'orange', 1, GETUTCDATE(), 0, 'system'),
                    ('Marketing',     'pink',   1, GETUTCDATE(), 0, 'system'),
                    ('Maintenance',   'red',    1, GETUTCDATE(), 0, 'system'),
                    ('Miscellaneous', 'gray',   1, GETUTCDATE(), 0, 'system'),
                    ('License',       'indigo', 1, GETUTCDATE(), 0, 'system');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ExpenseCategoryConfigs");

            migrationBuilder.Sql("ALTER TABLE ExpenseEntries ADD CategoryTemp TINYINT NULL;");
            migrationBuilder.Sql(@"UPDATE ExpenseEntries SET CategoryTemp = CASE Category
                WHEN 'Rent' THEN 0 WHEN 'Electricity' THEN 1 WHEN 'Transport' THEN 2
                WHEN 'Salaries' THEN 3 WHEN 'RawMaterial' THEN 4 WHEN 'Marketing' THEN 5
                WHEN 'Maintenance' THEN 6 WHEN 'License' THEN 8 ELSE 7 END;");
            migrationBuilder.Sql("ALTER TABLE ExpenseEntries DROP COLUMN Category;");
            migrationBuilder.Sql("EXEC sp_rename 'ExpenseEntries.CategoryTemp', 'Category', 'COLUMN';");
            migrationBuilder.Sql("ALTER TABLE ExpenseEntries ALTER COLUMN Category TINYINT NOT NULL;");

            migrationBuilder.AlterColumn<byte>(
                name: "Category",
                table: "ExpenseBudgets",
                type: "tinyint",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");
        }
    }
}
