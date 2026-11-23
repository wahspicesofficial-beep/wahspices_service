using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WahSpices.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoicePdfAndEInvoice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AttachPdfToEmails",
                table: "InvoiceSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "AuthorisedSignatory",
                table: "InvoiceSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AutoGenerateOnOrder",
                table: "InvoiceSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "Declaration",
                table: "InvoiceSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EInvoiceEnabled",
                table: "InvoiceSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceTitle",
                table: "InvoiceSettings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "Tax Invoice");

            migrationBuilder.AddColumn<string>(
                name: "NumberFormat",
                table: "InvoiceSettings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "FY");

            migrationBuilder.AddColumn<bool>(
                name: "ShowAmountInWords",
                table: "InvoiceSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShowBankDetails",
                table: "InvoiceSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShowHsnSummary",
                table: "InvoiceSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShowMrpColumn",
                table: "InvoiceSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShowUpiQr",
                table: "InvoiceSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "SignatureImage",
                table: "InvoiceSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StampImage",
                table: "InvoiceSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UdyamNumber",
                table: "InvoiceSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpiId",
                table: "InvoiceSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CouponDiscount",
                table: "Invoices",
                type: "decimal(12,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "InvoiceDate",
                table: "Invoices",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastSentAt",
                table: "Invoices",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastSentTo",
                table: "Invoices",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentMethod",
                table: "Invoices",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlaceOfSupply",
                table: "Invoices",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RoundOff",
                table: "Invoices",
                type: "decimal(6,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "SentCount",
                table: "Invoices",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SignedQrCode",
                table: "Invoices",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "Invoices",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Online");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AttachPdfToEmails",
                table: "InvoiceSettings");

            migrationBuilder.DropColumn(
                name: "AuthorisedSignatory",
                table: "InvoiceSettings");

            migrationBuilder.DropColumn(
                name: "AutoGenerateOnOrder",
                table: "InvoiceSettings");

            migrationBuilder.DropColumn(
                name: "Declaration",
                table: "InvoiceSettings");

            migrationBuilder.DropColumn(
                name: "EInvoiceEnabled",
                table: "InvoiceSettings");

            migrationBuilder.DropColumn(
                name: "InvoiceTitle",
                table: "InvoiceSettings");

            migrationBuilder.DropColumn(
                name: "NumberFormat",
                table: "InvoiceSettings");

            migrationBuilder.DropColumn(
                name: "ShowAmountInWords",
                table: "InvoiceSettings");

            migrationBuilder.DropColumn(
                name: "ShowBankDetails",
                table: "InvoiceSettings");

            migrationBuilder.DropColumn(
                name: "ShowHsnSummary",
                table: "InvoiceSettings");

            migrationBuilder.DropColumn(
                name: "ShowMrpColumn",
                table: "InvoiceSettings");

            migrationBuilder.DropColumn(
                name: "ShowUpiQr",
                table: "InvoiceSettings");

            migrationBuilder.DropColumn(
                name: "SignatureImage",
                table: "InvoiceSettings");

            migrationBuilder.DropColumn(
                name: "StampImage",
                table: "InvoiceSettings");

            migrationBuilder.DropColumn(
                name: "UdyamNumber",
                table: "InvoiceSettings");

            migrationBuilder.DropColumn(
                name: "UpiId",
                table: "InvoiceSettings");

            migrationBuilder.DropColumn(
                name: "CouponDiscount",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "InvoiceDate",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "LastSentAt",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "LastSentTo",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "PlaceOfSupply",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "RoundOff",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "SentCount",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "SignedQrCode",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "Invoices");
        }
    }
}
