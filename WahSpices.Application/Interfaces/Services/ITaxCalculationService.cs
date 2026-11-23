using WahSpices.Domain.Enums;

namespace WahSpices.Application.Interfaces.Services;

// ── Output models ─────────────────────────────────────────────────────────────

public class GstBreakdown
{
    public decimal TaxableValue { get; set; }
    public decimal GstRate { get; set; }
    public decimal CgstRate { get; set; }
    public decimal SgstRate { get; set; }
    public decimal IgstRate { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal TotalGst { get; set; }
    public decimal TotalAmount { get; set; }    // TaxableValue + TotalGst
    public GSTType GstType { get; set; }
}

public class TcsBreakdown
{
    public decimal TaxableValue { get; set; }
    public decimal TcsRate { get; set; }        // 1% under Section 52
    public decimal CgstTcs { get; set; }        // 0.5% intra-state
    public decimal SgstTcs { get; set; }        // 0.5% intra-state
    public decimal IgstTcs { get; set; }        // 1% inter-state
    public decimal TotalTcs { get; set; }
}

public class ReturnItemTaxInput
{
    public string ProductName { get; set; } = string.Empty;
    public string HsnCode { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }      // Price per unit
    public decimal UnitMrp { get; set; }
    public decimal GstRate { get; set; }        // e.g. 5, 12, 18
}

public class ReturnItemTaxResult
{
    public string ProductName { get; set; } = string.Empty;
    public string HsnCode { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
    public GstBreakdown Tax { get; set; } = null!;
}

public class ReturnTaxSummary
{
    public GSTType GstType { get; set; }
    public TaxPriceType PriceType { get; set; }

    public decimal SubtotalSelling { get; set; }    // Sum of line totals
    public decimal TotalTaxableValue { get; set; }  // Ex-GST total
    public decimal TotalCgst { get; set; }
    public decimal TotalSgst { get; set; }
    public decimal TotalIgst { get; set; }
    public decimal TotalGst { get; set; }
    public decimal TcsAmount { get; set; }
    public decimal CreditNoteAmount { get; set; }   // TaxableValue + GST reversed
    public decimal GrandRefundAmount { get; set; }  // Final refund to customer

    public List<ReturnItemTaxResult> ItemBreakdowns { get; set; } = new();

    // GST rates used (for display)
    public decimal CgstRate { get; set; }
    public decimal SgstRate { get; set; }
    public decimal IgstRate { get; set; }
    public decimal TcsRate { get; set; }

    // Summary labels for reports
    public string GstTypeLabel => GstType switch
    {
        GSTType.IntraState => "Intra-State (CGST + SGST)",
        GSTType.InterState => "Inter-State (IGST)",
        GSTType.Export => "Export (Zero Rated)",
        _ => "Unknown"
    };
}

// ── Interface ─────────────────────────────────────────────────────────────────

public interface ITaxCalculationService
{
    // Core GST calculation (intra = CGST+SGST, inter = IGST, export = zero)
    GstBreakdown CalculateGst(decimal amount, decimal gstRate, GSTType gstType, TaxPriceType priceType = TaxPriceType.Inclusive);

    // Section 52 TCS — 1% collected by e-commerce operator on taxable supplies
    TcsBreakdown CalculateTcs(decimal taxableValue, GSTType gstType, decimal tcsRate = 1m);

    // Full return tax summary for a list of items
    ReturnTaxSummary CalculateReturnTax(
        List<ReturnItemTaxInput> items,
        GSTType gstType,
        TaxPriceType priceType = TaxPriceType.Inclusive,
        bool includeTcs = false,
        decimal tcsRate = 1m);

    // Determine GST type based on supplier state vs customer state
    GSTType DetermineGstType(string supplierState, string customerState);

    // Extract taxable value from a GST-inclusive price
    decimal ExtractTaxableValue(decimal inclusivePrice, decimal gstRate);

    // Extract GST amount from a GST-inclusive price
    decimal ExtractGstAmount(decimal inclusivePrice, decimal gstRate);

    // Round to 2 decimal places (GSTIN rounding rules)
    decimal Round(decimal value);
}
