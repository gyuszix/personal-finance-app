using System.ComponentModel.DataAnnotations;

namespace PersonalFinance.Api.DTOs;

public class TransactionQueryParameters
{
    // Nullable, not defaulted via property initializer - [AsParameters] binds
    // non-nullable value types as required query params and ignores C#
    // property initializers, so "no page given" would 400 instead of
    // defaulting. Nullable + resolved manually avoids that.
    [Range(1, int.MaxValue, ErrorMessage = "Page must be at least 1")]
    public int? Page { get; set; }

    [Range(1, 200, ErrorMessage = "PageSize must be between 1 and 200")]
    public int? PageSize { get; set; }

    // Filters by Transaction.CategoryPrimary - lets the GUI filter the list
    // by tapping a category/pie slice
    public string? Category { get; set; }

    public int ResolvedPage => Page ?? 1;
    public int ResolvedPageSize => PageSize ?? 50;
}
