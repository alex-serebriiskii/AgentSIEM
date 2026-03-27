using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Siem.Api.Validators;

public class FluentValidationFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        foreach (var (_, value) in context.ActionArguments)
        {
            if (value is null) continue;

            var validatorType = typeof(IValidator<>).MakeGenericType(value.GetType());
            if (context.HttpContext.RequestServices.GetService(validatorType) is not IValidator validator)
                continue;

            var validationContext = new ValidationContext<object>(value);
            var result = await validator.ValidateAsync(validationContext, context.HttpContext.RequestAborted);

            if (!result.IsValid)
            {
                foreach (var error in result.Errors)
                {
                    context.ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
                }
            }
        }

        if (!context.ModelState.IsValid)
        {
            context.Result = new BadRequestObjectResult(
                new ValidationProblemDetails(context.ModelState));
            return;
        }

        await next();
    }
}
