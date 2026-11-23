using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WahSpices.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMasterDataAndGstFilings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GstReturnFilings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReturnType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    FiledOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Arn = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    TaxableValue = table.Column<decimal>(type: "decimal(14,2)", nullable: false),
                    TotalTax = table.Column<decimal>(type: "decimal(14,2)", nullable: false),
                    ItcClaimed = table.Column<decimal>(type: "decimal(14,2)", nullable: false),
                    NetPayable = table.Column<decimal>(type: "decimal(14,2)", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FiledBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GstReturnFilings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MasterDataValues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsSystem = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MasterDataValues", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GstReturnFilings_ReturnType_Year_Month",
                table: "GstReturnFilings",
                columns: new[] { "ReturnType", "Year", "Month" });

            migrationBuilder.CreateIndex(
                name: "IX_MasterDataValues_Type_Value",
                table: "MasterDataValues",
                columns: new[] { "Type", "Value" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GstReturnFilings");

            migrationBuilder.DropTable(
                name: "MasterDataValues");
        }
    }
}
