using WahSpices.Domain.Enums;

namespace WahSpices.Application.DTOs.Admin;

// ── Auth ─────────────────────────────────────────────
public record AdminLoginRequest(string Email, string Password);
public record AdminAuthResponse(AdminDto Admin, string Token, string RefreshToken, int ExpiresIn);
public record AdminDto(int Id, string Name, string Email, string Role);

// ── Dashboard ────────────────────────────────────────
public record DashboardStats(
    KpiStat Revenue,
    KpiStat Orders,
    KpiStat Customers,
    KpiStat AvgOrderValue,
    int LowStockCount,
    int PendingOrders,
    int PendingApprovals
);

public record KpiStat(decimal Current, decimal Previous, string Change);

public record MonthlyRevenue(string Month, decimal Revenue, int Orders);

// ── Inventory ────────────────────────────────────────
public record InventoryDto(
    int Id,
    string Sku,
    int ProductId,
    string ProductName,
    string Category,
    string Unit,
    int CurrentStock,
    int ReorderPoint,
    decimal CostPrice,
    decimal StockValue,
    string? BatchNumber,
    DateTime? ExpiryDate,
    string Location,
    bool IsLowStock
);

public record StockAdjustmentRequest(
    int Quantity,
    string Type,
    string Reason,
    string? BatchNumber,
    DateTime? ExpiryDate
);

public record InventoryLedgerDto(
    int Id,
    DateTime Date,
    string ProductName,
    string Type,
    int Quantity,
    int BalanceAfter,
    string Reason,
    string PerformedBy
);

// ── Orders Admin ─────────────────────────────────────
public record UpdateOrderStatusRequest(
    string Status,
    string? Courier,
    string? AwbNumber,
    string? Note
);

// ── Billing ──────────────────────────────────────────
public record CreateInvoiceRequest(
    string? CustomerId,
    string CustomerName,
    string? CustomerGstin,
    string Type,
    List<InvoiceItemRequest> Items,
    string? Notes
);

public record InvoiceItemRequest(
    int ProductId,
    string Name,
    string? HsnCode,
    int Quantity,
    decimal UnitPrice,
    decimal GstRate
);

public record InvoiceDto(
    int Id,
    string InvoiceNumber,
    string CustomerName,
    string Type,
    string Status,
    decimal Subtotal,
    decimal CgstAmount,
    decimal SgstAmount,
    decimal IgstAmount,
    decimal TotalAmount,
    string? Irn,
    string? PdfUrl,
    DateTime CreatedAt
);

// ── Payroll ──────────────────────────────────────────
public record RunPayrollRequest(int Month, int Year);

public record PayrollEntryDto(
    string EmpId,
    string Name,
    string Department,
    int DaysWorked,
    int LossOfPayDays,
    decimal GrossSalary,
    decimal Epf,
    decimal Esic,
    decimal ProfessionalTax,
    decimal Tds,
    decimal NetSalary,
    string Status
);

// ── Staff ────────────────────────────────────────────
public record CreateEmployeeRequest(
    string Name,
    string? Email,
    string Phone,
    string Department,
    string Designation,
    string EmploymentType,
    decimal Salary,
    DateTime JoinedAt,
    DateTime? ConfirmationDate = null,
    int NoticePeriodDays = 30,
    string? Branch = null,
    string? AadharNumber = null,
    string? PanNumber = null,
    string? BankAccountNo = null,
    string? BankIfsc = null,
    string? PfNumber = null,
    string? EsicNumber = null,
    string? ReportingManagerId = null,
    string? PhotoUrl = null
);

public record EmployeeDto(
    int Id,
    string EmployeeId,
    string Name,
    string? Email,
    string Phone,
    string Department,
    string Designation,
    string EmploymentType,
    decimal Salary,
    DateTime JoinedAt,
    bool IsActive,
    DateTime? ConfirmationDate = null,
    int NoticePeriodDays = 30,
    string? Branch = null,
    string? AadharNumber = null,
    string? PanNumber = null,
    string? BankAccountNo = null,
    string? BankIfsc = null,
    string? PfNumber = null,
    string? EsicNumber = null,
    string? ReportingManagerId = null,
    string? PhotoUrl = null
);

// ── Attendance ───────────────────────────────────────
public record MarkAttendanceRequest(
    DateTime Date,
    List<AttendanceEntryRequest> Entries
);

public record AttendanceEntryRequest(
    string EmpId,
    string Status,
    string? InTime,
    string? OutTime,
    string? Reason
);

// ── Leave ────────────────────────────────────────────
public record LeaveApplicationDto(
    int Id,
    string EmployeeName,
    string LeaveType,
    DateTime FromDate,
    DateTime ToDate,
    int TotalDays,
    string Reason,
    string Status,
    DateTime CreatedAt,
    int EmployeeId,
    string? ApprovedBy,
    string? RejectionReason,
    DateTime? ActionAt
);

public record LeaveActionRequest(string? Remarks, string? Reason);

// ── CRM ──────────────────────────────────────────────
public record CrmCustomerDto(
    int Id,
    string Name,
    string? Email,
    string Phone,
    string City,
    string Category,
    string? Gstin,
    decimal CreditLimit,
    decimal OutstandingDues,
    int LoyaltyPoints,
    DateTime? LastOrderDate
);

// ── Expense ──────────────────────────────────────────
public record CreateExpenseRequest(
    DateTime Date,
    string Category,
    string Description,
    decimal Amount,
    string PaidBy,
    string? Notes
);

public record ExpenseDto(
    int Id,
    DateTime Date,
    string Category,
    string Description,
    decimal Amount,
    string PaidBy,
    bool IsApproved,
    string? ApprovedBy,
    string? ReceiptUrl,
    DateTime CreatedAt
);

// ── Roles ─────────────────────────────────────────────
public record AdminRoleDto(
    int Id,
    string Name,
    string? Description,
    string Color,
    bool IsSystem,
    bool IsActive,
    int UserCount,
    Dictionary<string, bool>? Permissions
);

public record CreateRoleRequest(string Name, string? Description, string Color, Dictionary<string, bool> Permissions);

public record UpdateRoleRequest(string Name, string? Description, string Color, Dictionary<string, bool> Permissions, bool IsActive);

// ── Access Control ───────────────────────────────────
public record CreateAdminUserRequest(string Name, string Email, string Password, int RoleId);

public record UpdateAdminUserRequest(string Name, int RoleId, bool IsActive);

public record AdminUserDetailDto(
    int Id,
    string Name,
    string Email,
    string Role,
    int? RoleId,
    bool IsActive,
    bool IsTwoFactorEnabled,
    bool IsLocked,
    DateTime? LastLoginAt,
    DateTime CreatedAt,
    Dictionary<string, bool>? Permissions,
    Dictionary<string, bool>? EffectivePermissions,  // role perms merged with user overrides
    bool MustChangePassword = false                    // invited / reset — first sign-in not completed yet
);

public record UpdatePermissionsRequest(Dictionary<string, bool> Permissions);

public record ChangeAdminPasswordRequest(string CurrentPassword, string NewPassword);

public record AdminSessionDto(
    int Id,
    string? IpAddress,
    string? UserAgent,
    string? Location,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    DateTime LastActiveAt,
    bool IsCurrentSession
);

public record Setup2FAResponse(string Secret, string QrUri, string[] BackupCodes);

public record Verify2FARequest(string Code, string Secret);

public record Confirm2FARequest(string Code);

public record AuditLogDto(
    int Id,
    string UserId,
    string UserName,
    string Module,
    string Action,
    string? Details,
    string? IpAddress,
    DateTime CreatedAt
);

// ── Reports ──────────────────────────────────────────
public record PlDto(
    string Period,
    decimal Revenue,
    decimal Cogs,
    decimal GrossProfit,
    string GrossMargin,
    ExpenseBreakdown Expenses,
    decimal TotalExpenses,
    decimal NetProfit,
    string NetMargin
);

public record ExpenseBreakdown(
    decimal Salaries,
    decimal Rent,
    decimal Electricity,
    decimal Transport,
    decimal Marketing,
    decimal Other
);

// ── GST ──────────────────────────────────────────────
public record Gstr1Row(
    string Period,
    decimal B2bTaxable,
    decimal B2bTax,
    decimal B2cTaxable,
    decimal B2cTax,
    decimal TotalGst
);

public record Gstr3bDto(
    string Period,
    decimal OutputCgst,
    decimal OutputSgst,
    decimal OutputIgst,
    decimal TotalOutput,
    decimal ItcCgst,
    decimal ItcSgst,
    decimal ItcIgst,
    decimal TotalItc,
    decimal NetPayable
);
