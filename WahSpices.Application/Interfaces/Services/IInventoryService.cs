using WahSpices.Application.Common;
using WahSpices.Application.DTOs.Inventory;

namespace WahSpices.Application.Interfaces.Services;

public interface IInventoryService
{
    Task DeductStockAsync(int orderId, string performedBy = "System");
    Task RestoreStockAsync(int orderId, string performedBy = "System");
    Task<PagedResult<InventoryItemDto>> GetAllAsync(InventoryFilterParams filter);
    Task<InventoryItemDto?> GetByProductIdAsync(int productId);
    Task AdjustStockAsync(int inventoryItemId, AdjustStockRequest request, string performedBy);
    Task<List<InventoryLedgerDto>> GetLedgerAsync(int inventoryItemId, int limit = 50);
    Task<List<InventoryItemDto>> GetLowStockAsync();
}
