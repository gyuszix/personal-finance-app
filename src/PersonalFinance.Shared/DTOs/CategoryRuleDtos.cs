namespace PersonalFinance.Shared.DTOs;

// Categorisation rules (#124).

public static class RuleMatchTypes
{
    public const string Contains = "contains";
    public const string Equals = "equals";
    public static readonly IReadOnlyList<string> All = [Contains, Equals];
}

public class CategoryRuleRequest
{
    // Matched against the transaction description, ignoring case.
    public string Pattern { get; set; } = string.Empty;
    public string MatchType { get; set; } = RuleMatchTypes.Contains;
    public string Category { get; set; } = string.Empty;

    // Optional narrowing. Amounts as Plaid reports them: positive = money out.
    public int? AccountId { get; set; }
    public decimal? MinAmount { get; set; }
    public decimal? MaxAmount { get; set; }

    // Lower runs first; the first matching rule wins.
    public int Priority { get; set; } = 100;
}

public class CategoryRuleResponse
{
    public int Id { get; set; }
    public string Pattern { get; set; } = string.Empty;
    public string MatchType { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int? AccountId { get; set; }
    public decimal? MinAmount { get; set; }
    public decimal? MaxAmount { get; set; }
    public int Priority { get; set; }

    // How many of the user's transactions this rule categorises right now.
    public int MatchedTransactions { get; set; }
}
