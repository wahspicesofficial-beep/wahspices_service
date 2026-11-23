using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WahSpices.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRawMaterialPacking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InventoryItems_ProductId",
                table: "InventoryItems");

            migrationBuilder.AddColumn<int>(
                name: "VariantId",
                table: "InventoryItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RawMaterialId",
                table: "GrnItems",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RawMaterials",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    DisplayUnit = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    CurrentStockKg = table.Column<decimal>(type: "decimal(14,3)", nullable: false),
                    ReorderLevelKg = table.Column<decimal>(type: "decimal(14,3)", nullable: false),
                    CostPerKg = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    HsnCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Location = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DefaultProductId = table.Column<int>(type: "int", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawMaterials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RawMaterials_Products_DefaultProductId",
                        column: x => x.DefaultProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PackingEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PackingNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RawMaterialId = table.Column<int>(type: "int", nullable: false),
                    RawUsedKg = table.Column<decimal>(type: "decimal(14,3)", nullable: false),
                    PackedKg = table.Column<decimal>(type: "decimal(14,3)", nullable: false),
                    WastageKg = table.Column<decimal>(type: "decimal(14,3)", nullable: false),
                    VarianceKg = table.Column<decimal>(type: "decimal(14,3)", nullable: false),
                    CostPerKg = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    PackedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BatchNumber = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    PackedBy = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ReversedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReversedBy = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    ReversalReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackingEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PackingEntries_RawMaterials_RawMaterialId",
                        column: x => x.RawMaterialId,
                        principalTable: "RawMaterials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RawMaterialLedgers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RawMaterialId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    QuantityKg = table.Column<decimal>(type: "decimal(14,3)", nullable: false),
                    BalanceAfterKg = table.Column<decimal>(type: "decimal(14,3)", nullable: false),
                    UnitCostPerKg = table.Column<decimal>(type: "decimal(12,2)", nullable: true),
                    ReferenceType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    ReferenceId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    BatchNumber = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PerformedBy = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawMaterialLedgers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RawMaterialLedgers_RawMaterials_RawMaterialId",
                        column: x => x.RawMaterialId,
                        principalTable: "RawMaterials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PackingEntryLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PackingEntryId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    VariantId = table.Column<int>(type: "int", nullable: true),
                    PackLabel = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PackWeightGrams = table.Column<int>(type: "int", nullable: false),
                    Packets = table.Column<int>(type: "int", nullable: false),
                    TotalKg = table.Column<decimal>(type: "decimal(14,3)", nullable: false),
                    InventoryItemId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackingEntryLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PackingEntryLines_InventoryItems_InventoryItemId",
                        column: x => x.InventoryItemId,
                        principalTable: "InventoryItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PackingEntryLines_PackingEntries_PackingEntryId",
                        column: x => x.PackingEntryId,
                        principalTable: "PackingEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PackingEntryLines_ProductVariants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "ProductVariants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PackingEntryLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryItems_ProductId",
                table: "InventoryItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryItems_VariantId",
                table: "InventoryItems",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_PackingEntries_PackingNumber",
                table: "PackingEntries",
                column: "PackingNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PackingEntries_RawMaterialId_PackedOn",
                table: "PackingEntries",
                columns: new[] { "RawMaterialId", "PackedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_PackingEntryLines_InventoryItemId",
                table: "PackingEntryLines",
                column: "InventoryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PackingEntryLines_PackingEntryId",
                table: "PackingEntryLines",
                column: "PackingEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_PackingEntryLines_ProductId",
                table: "PackingEntryLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_PackingEntryLines_VariantId",
                table: "PackingEntryLines",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_RawMaterialLedgers_RawMaterialId_CreatedAt",
                table: "RawMaterialLedgers",
                columns: new[] { "RawMaterialId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RawMaterialLedgers_ReferenceId",
                table: "RawMaterialLedgers",
                column: "ReferenceId");

            migrationBuilder.CreateIndex(
                name: "IX_RawMaterials_Code",
                table: "RawMaterials",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RawMaterials_DefaultProductId",
                table: "RawMaterials",
                column: "DefaultProductId");

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryItems_ProductVariants_VariantId",
                table: "InventoryItems",
                column: "VariantId",
                principalTable: "ProductVariants",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InventoryItems_ProductVariants_VariantId",
                table: "InventoryItems");

            migrationBuilder.DropTable(
                name: "PackingEntryLines");

            migrationBuilder.DropTable(
                name: "RawMaterialLedgers");

            migrationBuilder.DropTable(
                name: "PackingEntries");

            migrationBuilder.DropTable(
                name: "RawMaterials");

            migrationBuilder.DropIndex(
                name: "IX_InventoryItems_ProductId",
                table: "InventoryItems");

            migrationBuilder.DropIndex(
                name: "IX_InventoryItems_VariantId",
                table: "InventoryItems");

            migrationBuilder.DropColumn(
                name: "VariantId",
                table: "InventoryItems");

            migrationBuilder.DropColumn(
                name: "RawMaterialId",
                table: "GrnItems");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryItems_ProductId",
                table: "InventoryItems",
                column: "ProductId",
                unique: true);
        }
    }
}
