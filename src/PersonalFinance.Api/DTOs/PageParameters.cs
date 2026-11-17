using System.ComponentModel.DataAnnotations;

namespace PersonalFinance.Api.DTOs;

// Nullable for the same reason as TransactionQueryParameters: [AsParameters]
// would otherwise make them required.
public class PageParameters
{
    [Range(1, int.MaxValue, ErrorMessage = "Page must be at least 1")]
    public int? Page { get; set; }

    [Range(1, 200, ErrorMessage = "PageSize must be between 1 and 200")]
    public int? PageSize { get; set; }

    public int ResolvedPage => Page ?? 1;
    public int ResolvedPageSize => PageSize ?? 50;
}
