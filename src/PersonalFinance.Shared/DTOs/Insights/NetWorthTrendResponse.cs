namespace PersonalFinance.Shared.DTOs.Insights;

public class NetWorthTrendResponse
{
    // The last day covered: today, or the end of a past selected month.
    public string End { get; set; } = string.Empty;
    // One per day, oldest first, up to 365; starts at the first snapshot.
    public List<NetWorthPoint> Points { get; set; } = [];
}

public class NetWorthPoint
{
    public string Date { get; set; } = string.Empty;   // yyyy-MM-dd
    public decimal Assets { get; set; }
    public decimal Liabilities { get; set; }
    public decimal NetWorth { get; set; }
}
