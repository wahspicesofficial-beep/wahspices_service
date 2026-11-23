-- ============================================================
--  WAH! SPICES — Add CreatedBy / UpdatedBy to all tables
--  Safe to run multiple times (IF NOT EXISTS checks)
-- ============================================================

USE db55965;
GO

-- ── Products ──────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Products') AND name = 'CreatedBy')
    ALTER TABLE Products ADD CreatedBy NVARCHAR(256) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Products') AND name = 'UpdatedBy')
    ALTER TABLE Products ADD UpdatedBy NVARCHAR(256) NULL;

-- ── Addresses ─────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Addresses') AND name = 'CreatedBy')
    ALTER TABLE Addresses ADD CreatedBy NVARCHAR(256) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Addresses') AND name = 'UpdatedBy')
    ALTER TABLE Addresses ADD UpdatedBy NVARCHAR(256) NULL;

-- ── Reviews ───────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Reviews') AND name = 'CreatedBy')
    ALTER TABLE Reviews ADD CreatedBy NVARCHAR(256) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Reviews') AND name = 'UpdatedBy')
    ALTER TABLE Reviews ADD UpdatedBy NVARCHAR(256) NULL;

-- ── Coupons ───────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Coupons') AND name = 'CreatedBy')
    ALTER TABLE Coupons ADD CreatedBy NVARCHAR(256) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Coupons') AND name = 'UpdatedBy')
    ALTER TABLE Coupons ADD UpdatedBy NVARCHAR(256) NULL;

-- ── CartItems ─────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CartItems') AND name = 'CreatedBy')
    ALTER TABLE CartItems ADD CreatedBy NVARCHAR(256) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CartItems') AND name = 'UpdatedBy')
    ALTER TABLE CartItems ADD UpdatedBy NVARCHAR(256) NULL;

-- ── WishlistItems ─────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('WishlistItems') AND name = 'CreatedBy')
    ALTER TABLE WishlistItems ADD CreatedBy NVARCHAR(256) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('WishlistItems') AND name = 'UpdatedBy')
    ALTER TABLE WishlistItems ADD UpdatedBy NVARCHAR(256) NULL;

-- ── OrderItems ────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('OrderItems') AND name = 'CreatedBy')
    ALTER TABLE OrderItems ADD CreatedBy NVARCHAR(256) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('OrderItems') AND name = 'UpdatedBy')
    ALTER TABLE OrderItems ADD UpdatedBy NVARCHAR(256) NULL;

-- ── OrderStatusHistories ──────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('OrderStatusHistories') AND name = 'CreatedBy')
    ALTER TABLE OrderStatusHistories ADD CreatedBy NVARCHAR(256) NULL;
-- UpdatedBy already exists in schema

-- ── ReturnItems ───────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ReturnItems') AND name = 'CreatedBy')
    ALTER TABLE ReturnItems ADD CreatedBy NVARCHAR(256) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ReturnItems') AND name = 'UpdatedBy')
    ALTER TABLE ReturnItems ADD UpdatedBy NVARCHAR(256) NULL;

-- ── BlogPosts ─────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('BlogPosts') AND name = 'CreatedBy')
    ALTER TABLE BlogPosts ADD CreatedBy NVARCHAR(256) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('BlogPosts') AND name = 'UpdatedBy')
    ALTER TABLE BlogPosts ADD UpdatedBy NVARCHAR(256) NULL;

-- ── OtpRecords ────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('OtpRecords') AND name = 'CreatedBy')
    ALTER TABLE OtpRecords ADD CreatedBy NVARCHAR(256) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('OtpRecords') AND name = 'UpdatedBy')
    ALTER TABLE OtpRecords ADD UpdatedBy NVARCHAR(256) NULL;

-- ── NewsletterSubscribers ─────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('NewsletterSubscribers') AND name = 'CreatedBy')
    ALTER TABLE NewsletterSubscribers ADD CreatedBy NVARCHAR(256) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('NewsletterSubscribers') AND name = 'UpdatedBy')
    ALTER TABLE NewsletterSubscribers ADD UpdatedBy NVARCHAR(256) NULL;

-- ── ContactMessages ───────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ContactMessages') AND name = 'CreatedBy')
    ALTER TABLE ContactMessages ADD CreatedBy NVARCHAR(256) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ContactMessages') AND name = 'UpdatedBy')
    ALTER TABLE ContactMessages ADD UpdatedBy NVARCHAR(256) NULL;

-- ── Feedbacks ─────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Feedbacks') AND name = 'CreatedBy')
    ALTER TABLE Feedbacks ADD CreatedBy NVARCHAR(256) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Feedbacks') AND name = 'UpdatedBy')
    ALTER TABLE Feedbacks ADD UpdatedBy NVARCHAR(256) NULL;

-- ── AdminUsers ────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('AdminUsers') AND name = 'CreatedBy')
    ALTER TABLE AdminUsers ADD CreatedBy NVARCHAR(256) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('AdminUsers') AND name = 'UpdatedBy')
    ALTER TABLE AdminUsers ADD UpdatedBy NVARCHAR(256) NULL;

-- ── AuditLogs ─────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('AuditLogs') AND name = 'CreatedBy')
    ALTER TABLE AuditLogs ADD CreatedBy NVARCHAR(256) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('AuditLogs') AND name = 'UpdatedBy')
    ALTER TABLE AuditLogs ADD UpdatedBy NVARCHAR(256) NULL;

-- ── InventoryItems ────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('InventoryItems') AND name = 'CreatedBy')
    ALTER TABLE InventoryItems ADD CreatedBy NVARCHAR(256) NULL;
-- UpdatedBy already exists in schema

-- ── InventoryLedgers ──────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('InventoryLedgers') AND name = 'CreatedBy')
    ALTER TABLE InventoryLedgers ADD CreatedBy NVARCHAR(256) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('InventoryLedgers') AND name = 'UpdatedBy')
    ALTER TABLE InventoryLedgers ADD UpdatedBy NVARCHAR(256) NULL;

-- ── Vendors ───────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Vendors') AND name = 'CreatedBy')
    ALTER TABLE Vendors ADD CreatedBy NVARCHAR(256) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Vendors') AND name = 'UpdatedBy')
    ALTER TABLE Vendors ADD UpdatedBy NVARCHAR(256) NULL;

-- ── PurchaseOrders ────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('PurchaseOrders') AND name = 'CreatedBy')
    ALTER TABLE PurchaseOrders ADD CreatedBy NVARCHAR(256) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('PurchaseOrders') AND name = 'UpdatedBy')
    ALTER TABLE PurchaseOrders ADD UpdatedBy NVARCHAR(256) NULL;

-- ── Invoices ──────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Invoices') AND name = 'CreatedBy')
    ALTER TABLE Invoices ADD CreatedBy NVARCHAR(256) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Invoices') AND name = 'UpdatedBy')
    ALTER TABLE Invoices ADD UpdatedBy NVARCHAR(256) NULL;

-- ── InvoiceLineItems ──────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('InvoiceLineItems') AND name = 'CreatedBy')
    ALTER TABLE InvoiceLineItems ADD CreatedBy NVARCHAR(256) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('InvoiceLineItems') AND name = 'UpdatedBy')
    ALTER TABLE InvoiceLineItems ADD UpdatedBy NVARCHAR(256) NULL;

-- ── InvoiceSettings ───────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('InvoiceSettings') AND name = 'CreatedBy')
    ALTER TABLE InvoiceSettings ADD CreatedBy NVARCHAR(256) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('InvoiceSettings') AND name = 'UpdatedBy')
    ALTER TABLE InvoiceSettings ADD UpdatedBy NVARCHAR(256) NULL;

-- ── HsnCodes ──────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('HsnCodes') AND name = 'CreatedBy')
    ALTER TABLE HsnCodes ADD CreatedBy NVARCHAR(256) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('HsnCodes') AND name = 'UpdatedBy')
    ALTER TABLE HsnCodes ADD UpdatedBy NVARCHAR(256) NULL;

-- ── ItcEntries ────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ItcEntries') AND name = 'CreatedBy')
    ALTER TABLE ItcEntries ADD CreatedBy NVARCHAR(256) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ItcEntries') AND name = 'UpdatedBy')
    ALTER TABLE ItcEntries ADD UpdatedBy NVARCHAR(256) NULL;

-- ── CrmCustomers ──────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CrmCustomers') AND name = 'CreatedBy')
    ALTER TABLE CrmCustomers ADD CreatedBy NVARCHAR(256) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CrmCustomers') AND name = 'UpdatedBy')
    ALTER TABLE CrmCustomers ADD UpdatedBy NVARCHAR(256) NULL;

-- ── ExpenseEntries ────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ExpenseEntries') AND name = 'CreatedBy')
    ALTER TABLE ExpenseEntries ADD CreatedBy NVARCHAR(256) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ExpenseEntries') AND name = 'UpdatedBy')
    ALTER TABLE ExpenseEntries ADD UpdatedBy NVARCHAR(256) NULL;

-- ── Employees ─────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Employees') AND name = 'CreatedBy')
    ALTER TABLE Employees ADD CreatedBy NVARCHAR(256) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Employees') AND name = 'UpdatedBy')
    ALTER TABLE Employees ADD UpdatedBy NVARCHAR(256) NULL;

-- ── AttendanceRecords ─────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('AttendanceRecords') AND name = 'CreatedBy')
    ALTER TABLE AttendanceRecords ADD CreatedBy NVARCHAR(256) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('AttendanceRecords') AND name = 'UpdatedBy')
    ALTER TABLE AttendanceRecords ADD UpdatedBy NVARCHAR(256) NULL;

-- ── LeaveApplications ─────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('LeaveApplications') AND name = 'CreatedBy')
    ALTER TABLE LeaveApplications ADD CreatedBy NVARCHAR(256) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('LeaveApplications') AND name = 'UpdatedBy')
    ALTER TABLE LeaveApplications ADD UpdatedBy NVARCHAR(256) NULL;

-- ── LeaveBalances ─────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('LeaveBalances') AND name = 'CreatedBy')
    ALTER TABLE LeaveBalances ADD CreatedBy NVARCHAR(256) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('LeaveBalances') AND name = 'UpdatedBy')
    ALTER TABLE LeaveBalances ADD UpdatedBy NVARCHAR(256) NULL;

-- ── PayrollEntries ────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('PayrollEntries') AND name = 'CreatedBy')
    ALTER TABLE PayrollEntries ADD CreatedBy NVARCHAR(256) NULL;
-- UpdatedBy already exists in schema

-- ── Holidays ──────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Holidays') AND name = 'CreatedBy')
    ALTER TABLE Holidays ADD CreatedBy NVARCHAR(256) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Holidays') AND name = 'UpdatedBy')
    ALTER TABLE Holidays ADD UpdatedBy NVARCHAR(256) NULL;

PRINT 'CreatedBy / UpdatedBy columns added to all tables successfully.';
GO
