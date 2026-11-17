namespace PersonalFinance.Api.Entities;

// A user's rule that re-categorises their transactions (#124): e.g. anything
// whose description contains "PURE GYM" is PERSONAL_CARE, not whatever Plaid
// guessed.
public class CategoryRule
{
    public int CategoryRuleId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Pattern { get; set; } = string.Empty;
    public string MatchType { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int? AccountId { get; set; }
    public decimal? MinAmount { get; set; }
    public decimal? MaxAmount { get; set; }
    public int Priority { get; set; }
    public DateTime CreatedAt { get; set; }
}
