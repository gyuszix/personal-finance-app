using System.ComponentModel.DataAnnotations;

namespace PersonalFinance.Api.Middleware;

public class ValidationFilter<T> : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next
    )
    {
        var argument = context.Arguments.OfType<T>().FirstOrDefault();

        if (argument is null)
        {
            return Results.BadRequest("Request body is missing or malformed");
        }

        var validationContext = new ValidationContext(argument);
        var validationResults = new List<ValidationResult>();

        bool isValid = Validator.TryValidateObject(
            argument, validationContext, validationResults, validateAllProperties: true);
        
        if (!isValid)
        {
            var errors = validationResults
                .SelectMany(r => r.MemberNames.Select(m => new { Field = m, r.ErrorMessage }))
                .GroupBy(e => e.Field)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());

            return Results.ValidationProblem(errors!);
        }

        return await next(context);
    }
}