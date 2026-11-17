using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Entities;
using PersonalFinance.Shared.DTOs;

namespace PersonalFinance.Api.Services;

// Applies a user's categorisation rules (#124). Transaction.CategoryPrimary is
// the *effective* category every insight, budget and filter already reads;
// PlaidCategoryPrimary keeps what Plaid said, so removing a rule puts it back.
// Rules run in priority order (then oldest first) and the first match wins.
public static class CategoryRules
{
    public static List<CategoryRule> Ordered(IEnumerable<CategoryRule> rules) =>
        rules.OrderBy(r => r.Priority).ThenBy(r => r.CategoryRuleId).ToList();

    public static bool Matches(CategoryRule rule, Transaction t)
    {
        if (rule.AccountId is { } accountId && t.AccountId != accountId) return false;
        if (rule.MinAmount is { } min && t.Amount < min) return false;
        if (rule.MaxAmount is { } max && t.Amount > max) return false;

        var description = t.Description ?? "";
        return rule.MatchType == RuleMatchTypes.Equals
            ? string.Equals(description.Trim(), rule.Pattern.Trim(), StringComparison.OrdinalIgnoreCase)
            : description.Contains(rule.Pattern.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    // Sets each transaction's effective category from the rules (or Plaid's
    // when none match). Returns how many changed.
    public static int Apply(IReadOnlyList<CategoryRule> orderedRules, IEnumerable<Transaction> transactions)
    {
        var changed = 0;
        foreach (var t in transactions)
        {
            var rule = orderedRules.FirstOrDefault(r => Matches(r, t));
            var category = rule?.Category ?? t.PlaidCategoryPrimary;
            if (t.CategoryPrimary == category && t.CategoryRuleId == rule?.CategoryRuleId) continue;
            t.CategoryPrimary = category;
            t.CategoryRuleId = rule?.CategoryRuleId;
            changed++;
        }
        return changed;
    }

    // Re-applies every rule to all of one user's transactions - after a rule
    // is created, changed or deleted. Saves.
    public static async Task<int> ReapplyAsync(AppDbContext db, string userId, CancellationToken cancellationToken = default)
    {
        var rules = Ordered(await db.CategoryRules.IgnoreQueryFilters().Where(r => r.UserId == userId).ToListAsync(cancellationToken));
        var transactions = await db.Transactions.IgnoreQueryFilters().Where(t => t.UserId == userId).ToListAsync(cancellationToken);
        var changed = Apply(rules, transactions);
        await db.SaveChangesAsync(cancellationToken);
        return changed;
    }
}
