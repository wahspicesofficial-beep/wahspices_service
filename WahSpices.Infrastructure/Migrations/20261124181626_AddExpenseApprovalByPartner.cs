using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WahSpices.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddExpenseApprovalByPartner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AdminUserId",
                table: "Partners",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                table: "ExpenseEntries",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ApprovedByAdminId",
                table: "ExpenseEntries",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CreatedByAdminId",
                table: "ExpenseEntries",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Partners_AdminUserId",
                table: "Partners",
                column: "AdminUserId",
                unique: true,
                filter: "[AdminUserId] IS NOT NULL AND [IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Partners_AdminUserId",
                table: "Partners");

            migrationBuilder.DropColumn(
                name: "AdminUserId",
                table: "Partners");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "ExpenseEntries");

            migrationBuilder.DropColumn(
                name: "ApprovedByAdminId",
                table: "ExpenseEntries");

            migrationBuilder.DropColumn(
                name: "CreatedByAdminId",
                table: "ExpenseEntries");
        }
    }
}
