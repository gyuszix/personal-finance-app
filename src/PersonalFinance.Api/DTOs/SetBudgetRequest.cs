using System.ComponentModel.DataAnnotations;

namespace PersonalFinance.Api.DTOs;

public class SetBudgetRequest
{
    // Zero is allowed ("spend nothing here"); the ceiling just rejects
    // typos and nonsense.
    [Required]
    [Range(typeof(decimal), "0", "1000000", ErrorMessage = "MonthlyLimit must be between 0 and 1,000,000")]
    public decimal? MonthlyLimit { get; set; }
}
