using WahSpices.Domain.Entities;

namespace WahSpices.API.Services;

/// <summary>
/// Single home for payroll statutory rules (EPF, EPS, EDLI, ESIC, Professional Tax) and the
/// default salary split. Payroll run, salary structures, challan summaries and payslips all use this,
/// so the admin UI never re-implements a rate.
/// </summary>
public static class PayrollCalculator
{
    // ── Default salary split when an employee has no configured structure (% of monthly salary) ──
    public const decimal DefaultBasicPct = 60m;
    public const decimal DefaultHraPct   = 25m;
    public const decimal DefaultTaPct    = 7m;
    public const decimal DefaultDaPct    = 5m;   // remainder (3%) goes to special allowance

    // ── EPF (Employees' Provident Funds & Misc. Provisions Act) ──
    public const decimal EpfEmployeeRate  = 12m;     // % of PF wage (Basic + DA)
    public const decimal EpfEmployerRate  = 12m;     // % of PF wage, split into EPS + EPF A/c
    public const decimal EpsRate          = 8.33m;   // pension share of the employer 12%
    public const decimal EdliRate         = 0.5m;    // employer, insurance
    public const decimal EpfAdminRate     = 0.5m;    // employer, admin charges
    public const decimal EpfWageCeiling   = 15000m;  // statutory PF wage ceiling

    // ── ESIC ──
    public const decimal EsicEmployeeRate = 0.75m;
    public const decimal EsicEmployerRate = 3.25m;
    public const decimal EsicWageCeiling  = 21000m;  // covered when monthly gross <= this

    // ── Professional Tax (Gujarat slab) ──
    public const string  PtState          = "Gujarat";
    public const decimal PtThreshold      = 12000m;  // monthly gross >= threshold → PtAmount
    public const decimal PtAmount         = 200m;

    public const int ChallanDueDay = 15;             // EPF / ESIC / PT due by 15th of the following month

    public static decimal ProfessionalTaxFor(decimal monthlyGross) => monthlyGross >= PtThreshold ? PtAmount : 0m;

    public static object Rates() => new
    {
        DefaultBasicPct, DefaultHraPct, DefaultTaPct, DefaultDaPct,
        EpfEmployeeRate, EpfEmployerRate, EpsRate, EdliRate, EpfAdminRate, EpfWageCeiling,
        EsicEmployeeRate, EsicEmployerRate, EsicWageCeiling,
        PtState, PtThreshold, PtAmount, ChallanDueDay,
        Summary = $"EPF {EpfEmployeeRate:0.##}% of Basic+DA (wage ceiling ₹{EpfWageCeiling:N0}) • ESIC {EsicEmployeeRate:0.##}% (gross ≤ ₹{EsicWageCeiling:N0}) • PT ₹{PtAmount:0} ({PtState}, gross ≥ ₹{PtThreshold:N0})",
    };

    public record Components(decimal Basic, decimal Hra, decimal Ta, decimal Da, decimal Special)
    {
        public decimal Gross => Basic + Hra + Ta + Da + Special;
    }

    /// <summary>Monthly (full-month) components from a structure, or the default split of Employee.Salary.</summary>
    public static Components MonthlyComponents(Employee emp, SalaryStructure? s)
    {
        if (s != null)
        {
            var basic = s.BasicSalary;
            return new Components(basic, R(basic * s.HraPercent / 100m), s.TaAmount, R(basic * s.DaPercent / 100m), s.SpecialAllowance);
        }
        var sal = emp.Salary;
        var b   = R(sal * DefaultBasicPct / 100m);
        var hra = R(sal * DefaultHraPct / 100m);
        var ta  = R(sal * DefaultTaPct / 100m);
        var da  = R(sal * DefaultDaPct / 100m);
        return new Components(b, hra, ta, da, Math.Max(0, sal - b - hra - ta - da));
    }

    public record Deductions(decimal PfWage, decimal Epf, decimal Esic, decimal ProfessionalTax, bool EsicCovered)
    {
        public decimal Total => Epf + Esic + ProfessionalTax;
    }

    /// <summary>Employee-side deductions on earned components. fullMonthGross decides ESIC coverage.</summary>
    public static Deductions EmployeeDeductions(Components earned, decimal fullMonthGross, SalaryStructure? s)
    {
        var pfRate  = s?.PfPercent ?? EpfEmployeeRate;
        var pfWage  = Math.Min(earned.Basic + earned.Da, EpfWageCeiling);
        var epf     = R(pfWage * pfRate / 100m);
        var covered = fullMonthGross <= EsicWageCeiling && fullMonthGross > 0;
        var esicRate= s?.EsicPercent ?? EsicEmployeeRate;
        var esic    = covered ? Math.Ceiling(earned.Gross * esicRate / 100m) : 0m;   // ESIC rounds up
        var pt      = earned.Gross >= PtThreshold ? (s?.ProfessionalTax ?? PtAmount) : 0m;
        return new Deductions(pfWage, epf, esic, pt, covered);
    }

    public record EmployerShare(decimal PfWage, decimal EpfAccount, decimal Eps, decimal Edli, decimal Admin, decimal Esic)
    {
        public decimal EpfTotal => EpfAccount + Eps;          // employer 12%
        public decimal EpfChallanExtras => Edli + Admin;
    }

    /// <summary>Employer contributions derived from a stored payroll entry (earned Basic/DA/Gross).</summary>
    public static EmployerShare Employer(PayrollEntry e)
    {
        var pfWage = e.Epf > 0 ? Math.Min(e.BasicSalary + e.Da, EpfWageCeiling) : 0m;
        var total  = R(pfWage * EpfEmployerRate / 100m);
        var eps    = R(Math.Min(pfWage, EpfWageCeiling) * EpsRate / 100m);
        var esic   = e.Esic > 0 ? Math.Ceiling(e.GrossSalary * EsicEmployerRate / 100m) : 0m;
        return new EmployerShare(pfWage, total - eps, eps, R(pfWage * EdliRate / 100m), R(pfWage * EpfAdminRate / 100m), esic);
    }

    /// <summary>Full calculation for a month. lopDays may be fractional (half days).</summary>
    public static PayrollEntry Calculate(Employee emp, SalaryStructure? s, int month, int year, decimal lopDays)
    {
        var daysInMonth = DateTime.DaysInMonth(year, month);
        lopDays = Math.Clamp(lopDays, 0, daysInMonth);
        var factor  = (daysInMonth - lopDays) / daysInMonth;
        var full    = MonthlyComponents(emp, s);
        var earned  = new Components(R(full.Basic * factor), R(full.Hra * factor), R(full.Ta * factor), R(full.Da * factor), R(full.Special * factor));
        var d       = EmployeeDeductions(earned, full.Gross, s);
        return new PayrollEntry
        {
            EmployeeId      = emp.Id,
            Month           = month,
            Year            = year,
            DaysWorked      = (int)Math.Floor(daysInMonth - lopDays),
            LossOfPayDays   = (int)Math.Ceiling(lopDays),
            BasicSalary     = earned.Basic,
            Hra             = earned.Hra,
            Ta              = earned.Ta,
            Da              = earned.Da,
            Bonus           = earned.Special,     // special allowance stored in Bonus column
            GrossSalary     = earned.Gross,
            Epf             = d.Epf,
            Esic            = d.Esic,
            ProfessionalTax = d.ProfessionalTax,
            Tds             = 0,
            NetSalary       = earned.Gross - d.Total,
        };
    }

    public static DateTime DueDate(int month, int year) => new DateTime(year, month, 1).AddMonths(1).AddDays(ChallanDueDay - 1);

    private static decimal R(decimal v) => Math.Round(v, 0, MidpointRounding.AwayFromZero);
}
