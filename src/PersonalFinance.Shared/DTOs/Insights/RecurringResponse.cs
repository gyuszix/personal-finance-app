namespace PersonalFinance.Shared.DTOs.Insights;

// Recurring charges - subscriptions and bills - detected from spending (#125).
public class RecurringResponse
{
    public string Month { get; set; } = string.Empty;

    // The day the detection looked back from: today for the current month,
    // the month's last day for a past one.
    public DateOnly AsOf { get; set; }

    // Soonest next charge first.
    public List<RecurringSeries> Series { get; set; } = [];

    // Every series' cost normalised to a month (a yearly 120 counts 10).
    public decimal TotalMonthlyCost { get; set; }
}

public class RecurringSeries
{
    public string Merchant { get; set; } = string.Empty;
    public string? Category { get; set; }

    // weekly, biweekly, monthly, quarterly, yearly
    public string Cadence { get; set; } = string.Empty;

    // Median of the charges.
    public decimal TypicalAmount { get; set; }
    public decimal MonthlyCost { get; set; }
    public int Occurrences { get; set; }
    public DateOnly LastDate { get; set; }
    public DateOnly NextDate { get; set; }
}
