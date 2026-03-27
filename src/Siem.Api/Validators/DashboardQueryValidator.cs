using FluentValidation;
using Siem.Api.Models.Requests;

namespace Siem.Api.Validators;

public class DashboardQueryValidator : AbstractValidator<DashboardQuery>
{
    public DashboardQueryValidator()
    {
        RuleFor(x => x.Hours)
            .InclusiveBetween(1, 720)
            .WithMessage("Hours must be between 1 and 720.");

        RuleFor(x => x.Limit)
            .InclusiveBetween(1, 100)
            .WithMessage("Limit must be between 1 and 100.");
    }
}
