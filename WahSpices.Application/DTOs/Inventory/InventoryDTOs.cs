using WahSpices.Domain.Enums;

namespace WahSpices.Application.DTOs.Inventory;

public record InventoryItemDto(
    int Id,
    int ProductId,
    string ProductName,
    string? ProductImage,
    string Sku,
    int CurrentStock,
    int ReorderPoint,
    bool IsLowStock,
    string? BatchNumber,
    DateTime? ExpiryDate,
    string Location,
    decimal CostPrice,
    string Unit
);

public record AdjustStockRequest(
    int Quantity,
    StockAdjustmentType Type,
    string Reason,
    string? BatchNumber
);

public record InventoryLedgerDto(
    int Id,
    string ProductName,
    StockAdjustmentType Type,
    int Quantity,
    int BalanceAfter,
    string? Reason,
    string? ReferenceId,
    string PerformedBy,
    DateTime CreatedAt
);

public class InventoryFilterParams
{
    public int Page { get; init; } = 1;
    public int Limit { get; init; } = 50;
    public string? Search { get; init; }
    public bool? LowStockOnly { get; init; }
}
