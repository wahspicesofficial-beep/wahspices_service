using WahSpices.Application.Interfaces.Services;
using WahSpices.Domain.Enums;

namespace WahSpices.Infrastructure.Services;

/// <summary>
/// All Indian GST formulas in one place.
///
/// CGST  = taxableValue × (gstRate / 2) / 100      [Intra-state only]
/// SGST  = taxableValue × (gstRate / 2) / 100      [Intra-state only]
/// IGST  = taxableValue × gstRate / 100            [Inter-state only]
///
/// Inclusive price → taxable: price × 100 / (100 + gstRate)
/// Exclusive price → total:   taxableValue × (1 + gstRate / 100)
///
/// TCS (Section 52): 1% of net taxable supplies
///   Intra-state: 0.5% CGST-TCS + 0.5% SGST-TCS
///   Inter-state: 1% IGST-TCS
/// </summary>
public class TaxCalculationService : ITaxCalculationService
{
    // ── Public API ────────────────────────────────────────────────────────────

    public GstBreakdown CalculateGst(
        decimal amount,
        decimal gstRate,
        GSTType gstType,
        TaxPriceType priceType = TaxPriceType.Inclusive)
    {
        if (gstRate < 0) gstRate = 0;

        decimal taxableValue = priceType == TaxPriceType.Inclusive
            ? ExtractTaxableValue(amount, gstRate)
            : amount;

        decimal cgstRate = 0, sgstRate = 0, igstRate = 0;
        decimal cgst = 0, sgst = 0, igst = 0;

        if (gstType == GSTType.Export)
        {
            // Zero-rated — no GST applicable
        }
        else if (gstType == GSTType.InterState)
        {
            igstRate = gstRate;
            igst = Round(taxableValue * igstRate / 100);
        }
        else // IntraState
        {
            cgstRate = gstRate / 2;
            sgstRate = gstRate / 2;
            cgst = Round(taxableValue * cgstRate / 100);
            sgst = Round(taxableValue * sgstRate / 100);
        }

        decimal totalGst = cgst + sgst + igst;

        return new GstBreakdown
        {
            TaxableValue = Round(taxableValue),
            GstRate = gstRate,
            CgstRate = cgstRate,
            SgstRate = sgstRate,
            IgstRate = igstRate,
            CgstAmount = cgst,
            SgstAmount = sgst,
            IgstAmount = igst,
            TotalGst = totalGst,
            TotalAmount = Round(taxableValue + totalGst),
            GstType = gstType
        };
    }

    public TcsBreakdown CalculateTcs(decimal taxableValue, GSTType gstType, decimal tcsRate = 1m)
    {
        if (taxableValue <= 0 || tcsRate <= 0)
            return new TcsBreakdown { TaxableValue = taxableValue, TcsRate = tcsRate };

        decimal totalTcs = Round(taxableValue * tcsRate / 100);
        decimal cgstTcs = 0, sgstTcs = 0, igstTcs = 0;

        if (gstType == GSTType.InterState)
        {
            igstTcs = totalTcs;
        }
        else
        {
            cgstTcs = Round(taxableValue * (tcsRate / 2) / 100);
            sgstTcs = Round(taxableValue * (tcsRate / 2) / 100);
            totalTcs = cgstTcs + sgstTcs;
        }

        return new TcsBreakdown
        {
            TaxableValue = taxableValue,
            TcsRate = tcsRate,
            CgstTcs = cgstTcs,
            SgstTcs = sgstTcs,
            IgstTcs = igstTcs,
            TotalTcs = totalTcs
        };
    }

    public ReturnTaxSummary CalculateReturnTax(
        List<ReturnItemTaxInput> items,
        GSTType gstType,
        TaxPriceType priceType = TaxPriceType.Inclusive,
        bool includeTcs = false,
        decimal tcsRate = 1m)
    {
        var summary = new ReturnTaxSummary
        {
            GstType = gstType,
            PriceType = priceType,
            TcsRate = includeTcs ? tcsRate : 0
        };

        foreach (var item in items)
        {
            decimal lineTotal = Round(item.UnitPrice * item.Quantity);
            var gst = CalculateGst(lineTotal, item.GstRate, gstType, priceType);

            summary.SubtotalSelling += lineTotal;
            summary.TotalTaxableValue += gst.TaxableValue;
            summary.TotalCgst += gst.CgstAmount;
            summary.TotalSgst += gst.SgstAmount;
            summary.TotalIgst += gst.IgstAmount;
            summary.TotalGst += gst.TotalGst;

            summary.ItemBreakdowns.Add(new ReturnItemTaxResult
            {
                ProductName = item.ProductName,
                HsnCode = item.HsnCode,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                LineTotal = lineTotal,
                Tax = gst
            });
        }

        // Round totals
        summary.SubtotalSelling = Round(summary.SubtotalSelling);
        summary.TotalTaxableValue = Round(summary.TotalTaxableValue);
        summary.TotalCgst = Round(summary.TotalCgst);
        summary.TotalSgst = Round(summary.TotalSgst);
        summary.TotalIgst = Round(summary.TotalIgst);
        summary.TotalGst = Round(summary.TotalGst);

        // TCS under Section 52
        if (includeTcs && tcsRate > 0)
        {
            var tcs = CalculateTcs(summary.TotalTaxableValue, gstType, tcsRate);
            summary.TcsAmount = tcs.TotalTcs;
        }

        // Credit note = taxable value + GST reversed (what GST authority gets back)
        summary.CreditNoteAmount = Round(summary.TotalTaxableValue + summary.TotalGst);

        // Grand refund = selling price paid by customer (includes GST already paid by customer)
        // TCS if applicable is also returned
        summary.GrandRefundAmount = Round(summary.SubtotalSelling + summary.TcsAmount);

        // Store rates for display
        if (summary.ItemBreakdowns.Count > 0)
        {
            var first = summary.ItemBreakdowns[0].Tax;
            summary.CgstRate = first.CgstRate;
            summary.SgstRate = first.SgstRate;
            summary.IgstRate = first.IgstRate;
        }

        return summary;
    }

    public GSTType DetermineGstType(string supplierState, string customerState)
        // GST state codes when recognisable ("Gujarat" == "GJ" == "24" == "24-Gujarat"), else a name comparison
        => WahSpices.Application.Common.GstStates.SameState(supplierState, customerState) ? GSTType.IntraState : GSTType.InterState;

    public decimal ExtractTaxableValue(decimal inclusivePrice, decimal gstRate)
    {
        if (gstRate <= 0) return inclusivePrice;
        return Round(inclusivePrice * 100 / (100 + gstRate));
    }

    public decimal ExtractGstAmount(decimal inclusivePrice, decimal gstRate)
    {
        if (gstRate <= 0) return 0;
        decimal taxable = ExtractTaxableValue(inclusivePrice, gstRate);
        return Round(inclusivePrice - taxable);
    }

    public decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
