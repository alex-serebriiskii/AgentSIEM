using FluentValidation;
using Siem.Api.Models.Requests;

namespace Siem.Api.Validators;

public class CreateSuppressionRequestValidator : AbstractValidator<CreateSuppressionRequest>
{
    public CreateSuppressionRequestValidator()
    {
        RuleFor(x => x)
            .Must(x => x.RuleId.HasValue || !string.IsNullOrWhiteSpace(x.AgentId))
            .WithMessage("At least one of RuleId or AgentId must be provided.");

        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Reason is required.");

        RuleFor(x => x.CreatedBy)
            .NotEmpty().WithMessage("CreatedBy is required.");

        RuleFor(x => x.DurationMinutes)
            .GreaterThan(0).WithMessage("DurationMinutes must be greater than 0.");
    }
}
