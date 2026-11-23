using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WahSpices.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DynamicPackTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop auto-generated default constraint on PackType before altering
            migrationBuilder.Sql(@"
                DECLARE @con NVARCHAR(128) = (
                    SELECT name FROM sys.default_constraints
                    WHERE parent_object_id = OBJECT_ID('Products') AND COL_NAME(parent_object_id, parent_column_id) = 'PackType'
                );
                IF @con IS NOT NULL EXEC('ALTER TABLE Products DROP CONSTRAINT ' + @con);
            ");

            // Convert tinyint PackType to string name
            migrationBuilder.Sql("ALTER TABLE Products ADD PackTypeTemp NVARCHAR(100) NULL;");
            migrationBuilder.Sql(@"UPDATE Products SET PackTypeTemp = CASE CAST(PackType AS TINYINT)
                WHEN 0 THEN 'BoxPack'
                WHEN 1 THEN 'PouchPack'
                ELSE 'PouchPack' END;");
            migrationBuilder.Sql("ALTER TABLE Products DROP COLUMN PackType;");
            migrationBuilder.Sql("EXEC sp_rename 'Products.PackTypeTemp', 'PackType', 'COLUMN';");
            migrationBuilder.Sql("ALTER TABLE Products ALTER COLUMN PackType NVARCHAR(100) NOT NULL;");

            migrationBuilder.CreateTable(
                name: "PackTypeConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackTypeConfigs", x => x.Id);
                });

            // Seed default pack types
            migrationBuilder.Sql(@"
                INSERT INTO PackTypeConfigs (Name, IsActive, CreatedAt, IsDeleted, CreatedBy)
                VALUES
                    ('BoxPack',   1, GETUTCDATE(), 0, 'system'),
                    ('PouchPack', 1, GETUTCDATE(), 0, 'system');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "PackTypeConfigs");

            migrationBuilder.Sql("ALTER TABLE Products ADD PackTypeTemp TINYINT NULL;");
            migrationBuilder.Sql(@"UPDATE Products SET PackTypeTemp = CASE PackType
                WHEN 'BoxPack' THEN 0 ELSE 1 END;");
            migrationBuilder.Sql("ALTER TABLE Products DROP COLUMN PackType;");
            migrationBuilder.Sql("EXEC sp_rename 'Products.PackTypeTemp', 'PackType', 'COLUMN';");
            migrationBuilder.Sql("ALTER TABLE Products ALTER COLUMN PackType TINYINT NOT NULL;");
        }
    }
}
