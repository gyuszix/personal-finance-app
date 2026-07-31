using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using PersonalFinance.Api.Entities;

namespace PersonalFinance.Api.Authorization;

public class TransactionOwnerHandler : AuthorizationHandler<ResourceOwnerRequirement, Transaction> 
{
  protected override Task HandleRequirementAsync(
      AuthorizationHandlerContext context,
      ResourceOwnerRequirement requirement,
      Transaction resouce)
  {
    var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);

    if (resouce.UserId == userId) 
    {
      context.Succeed(requirement);
    }
    return Task.CompletedTask;
  }
}
