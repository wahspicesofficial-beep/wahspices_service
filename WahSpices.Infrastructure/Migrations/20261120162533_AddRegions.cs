using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WahSpices.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRegions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Regions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    MapStateId = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    Tagline = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    CardText = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    Landmark = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    FeaturedMasala = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    AccentColor = table.Column<string>(type: "nvarchar(9)", maxLength: 9, nullable: false),
                    HeroImageUrl = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    HeroImageCredit = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CardImageUrl = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    SignatureMasalasJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    ProducesJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    DishesJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    ProductSlugsJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    AutoLinkProducts = table.Column<bool>(type: "bit", nullable: false),
                    SeoTitle = table.Column<string>(type: "nvarchar(70)", maxLength: 70, nullable: true),
                    SeoDescription = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsPublished = table.Column<bool>(type: "bit", nullable: false),
                    ShowOnHome = table.Column<bool>(type: "bit", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Regions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RegionStorySections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RegionId = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Heading = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    ImageUrl = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    ImageAlt = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ImageCaption = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ImageCredit = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegionStorySections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RegionStorySections_Regions_RegionId",
                        column: x => x.RegionId,
                        principalTable: "Regions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Regions_IsPublished_DisplayOrder",
                table: "Regions",
                columns: new[] { "IsPublished", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_Regions_Slug",
                table: "Regions",
                column: "Slug",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RegionStorySections_RegionId_SortOrder",
                table: "RegionStorySections",
                columns: new[] { "RegionId", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RegionStorySections");

            migrationBuilder.DropTable(
                name: "Regions");
        }
    }
}
