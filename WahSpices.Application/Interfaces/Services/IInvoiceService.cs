using WahSpices.Application.Common;
using WahSpices.Application.DTOs.Invoice;

namespace WahSpices.Application.Interfaces.Services;

public interface IInvoiceService
{
    Task<InvoiceDto> GenerateFromOrderAsync(int orderId);
    Task<InvoiceDto> CreateManualAsync(CreateInvoiceRequest request, string createdBy);
    Task<PagedResult<InvoiceListItemDto>> GetAllAsync(InvoiceFilterParams filter);
    Task<InvoiceSummaryDto> GetSummaryAsync(InvoiceFilterParams filter);
    Task<InvoiceDto?> GetByIdAsync(int id);
    Task<InvoiceDto?> GetByOrderIdAsync(int orderId);
    Task<InvoiceDto?> GetByInvoiceNumberAsync(string invoiceNumber);
    Task<InvoiceSettingsDto> GetSettingsAsync();
    Task<InvoiceSettingsDto> UpdateSettingsAsync(UpdateInvoiceSettingsRequest request, string updatedBy);
    Task<SendInvoiceResultDto> SendInvoiceAsync(int id, SendInvoiceRequest request);
    Task<InvoiceDto> UpdateStatusAsync(int id, string status, string updatedBy);

    /// <summary>Rendered PDF + download file name. Null when the invoice doesn't exist.</summary>
    Task<(byte[] Pdf, string FileName)?> GetPdfAsync(int id);
    Task<(byte[] Pdf, string FileName)?> GetPdfByOrderAsync(int orderId);
    /// <summary>Sample invoice rendered with unsaved template settings (Template tab "Preview").</summary>
    Task<byte[]> PreviewPdfAsync(UpdateInvoiceSettingsRequest settings);
    Task<InvoiceDto> UpdateEInvoiceAsync(int id, UpdateEInvoiceRequest request, string updatedBy);
    /// <summary>GST e-invoice (INV-01, schema 1.1) JSON for uploading to the IRP. B2B with buyer GSTIN only.</summary>
    Task<(string Json, string FileName)> GetEInvoiceJsonAsync(int id);
    /// <summary>Creates the order's invoice if the template's auto-generate switch is on and the order is billable. Never throws.</summary>
    Task<int?> TryAutoGenerateAsync(int orderId);
    /// <summary>Keeps an order's invoice status in line with the order (paid / cancelled). Never throws.</summary>
    Task SyncWithOrderAsync(int orderId, string updatedBy);
}
