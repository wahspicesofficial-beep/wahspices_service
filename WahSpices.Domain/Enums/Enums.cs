namespace WahSpices.Domain.Enums;

public enum OrderStatus
{
    Pending = 0,
    Processing = 1,
    Confirmed = 2,
    Packed = 3,
    Dispatched = 4,
    InTransit = 5,
    OutForDelivery = 6,
    Delivered = 7,
    Cancelled = 8,
    ReturnRequested = 9,
    Returned = 10,
    Refunded = 11
}

public enum PaymentStatus
{
    Pending = 0,
    Initiated = 1,
    Paid = 2,
    Failed = 3,
    Refunded = 4,
    PartiallyRefunded = 5
}

public enum PaymentMethod
{
    UPI = 0,
    Card = 1,
    NetBanking = 2,
    CashOnDelivery = 3,
    Wallet = 4
}

public enum UserRole
{
    Customer = 0,
    Admin = 1,
    Manager = 2,
    Accountant = 3,
    Production = 4,
    SalesExec = 5,
    HRManager = 6
}

public enum ProductCategory
{
    BlendMasala = 0,
    Powder = 1,
    Specialty = 2,
    WholeSpice = 3,
    Gift = 4
}

public enum PackType
{
    BoxPack = 0,
    PouchPack = 1
}

public enum AddressType
{
    Home = 0,
    Office = 1,
    Other = 2
}

public enum InvoiceType
{
    B2C = 0,
    B2B = 1
}

public enum InvoiceStatus
{
    Unpaid = 0,
    Paid = 1,
    PartiallyPaid = 2,
    Overdue = 3,
    Cancelled = 4
}

public enum AttendanceStatus
{
    Present = 0,
    Absent = 1,
    HalfDay = 2,
    Late = 3,
    WorkFromHome = 4,
    Overtime = 5,
    FieldVisit = 6
}

public enum LeaveStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Cancelled = 3
}

public enum LeaveType
{
    CasualLeave = 0,
    EarnedLeave = 1,
    SickLeave = 2,
    MaternityLeave = 3,
    PaternityLeave = 4
}

public enum EmploymentType
{
    Permanent = 0,
    Contract = 1,
    DailyWage = 2,
    Intern = 3
}

public enum ExpenseCategory
{
    Rent = 0,
    Electricity = 1,
    Transport = 2,
    Salaries = 3,
    RawMaterial = 4,
    Marketing = 5,
    Maintenance = 6,
    Miscellaneous = 7,
    License = 8
}

public enum StockAdjustmentType
{
    Addition = 0,
    Reduction = 1,
    Damage = 2,
    Return = 3,
    OpeningBalance = 4,
    GRNReceipt = 5,
    SaleDeduction = 6,
    SaleReversal = 7,
    Packing = 8,          // finished packets added by a packing (repacking) run
    PackingReversal = 9   // packets removed again when a packing run is reversed
}

public enum GSTType
{
    IntraState = 0,
    InterState = 1,
    Export = 2
}

public enum PurchaseOrderStatus
{
    Draft = 0,
    Pending = 1,
    Approved = 2,
    Received = 3,
    Cancelled = 4
}

public enum NotificationChannel
{
    SMS = 0,
    Email = 1,
    WhatsApp = 2
}

public enum ReturnStatus
{
    Requested = 0,
    Approved = 1,
    PickupScheduled = 2,
    PickedUp = 3,
    Received = 4,
    QualityCheck = 5,
    Processed = 6,
    Refunded = 7,
    Rejected = 8,
    Cancelled = 9
}

public enum ReturnType
{
    Refund = 0,
    Replacement = 1,
    StoreCredit = 2
}

public enum ReturnItemCondition
{
    Unopened = 0,
    Opened = 1,
    Damaged = 2,
    WrongProduct = 3,
    Expired = 4,
    DefectivePacking = 5
}

public enum RefundMethod
{
    OriginalPaymentMethod = 0,
    BankTransfer = 1,
    WalletCredit = 2,
    StoreCoupon = 3,
    UPI = 4
}

public enum TaxPriceType
{
    Inclusive = 0,
    Exclusive = 1
}

public enum RefundStatus
{
    Pending = 0,
    Initiated = 1,
    Processed = 2,
    Failed = 3,
    Cancelled = 4
}

// Field sales (stored as strings on SalesOrder.Status / SalesOrderBill.PaymentStatus)
public enum SalesOrderStatus
{
    Draft = 0,
    Confirmed = 1,
    Dispatched = 2,
    Delivered = 3,
    Billed = 4,
    Cancelled = 5
}

public enum SalesBillStatus
{
    Unpaid = 0,
    PartialPaid = 1,
    Paid = 2
}

// CRM customer account filter (derived from CrmCustomer.IsActive / IsBlocked)
public enum CustomerAccountStatus
{
    Active = 0,
    Blocked = 1,
    Inactive = 2
}
