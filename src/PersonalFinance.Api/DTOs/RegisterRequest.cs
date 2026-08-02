using System.ComponentModel.DataAnnotations;

namespace PersonalFinance.Api.DTOs;

public class RegisterRequest
{
   [Required]
   [EmailAddress]
   public string Email {get; set;} = string.Empty;

   [Required]
   [MinLength(8, ErrorMessage = "Password must be at least 8 chars")]
   public string Password {get; set;} = string.Empty;
}