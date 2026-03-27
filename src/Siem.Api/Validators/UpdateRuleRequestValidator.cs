using FluentValidation;
using Siem.Api.Data.Enums;
using Siem.Api.Models.Requests;

namespace Siem.Api.Validators;

public class UpdateRuleRequestValidator : AbstractValidator<UpdateRuleRequest>
{
    private static readonly string[] ValidEvaluationTypes = ["SingleEvent", "Temporal", "Sequence"];

    public UpdateRuleRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name cannot be empty.")
            .When(x => x.Name is not null);

        RuleFor(x => x.Severity)
            .Must(s => EnumExtensions.TryParseSeverity(s!, out _))
            .WithMessage("Severity must be one of: low, medium, high, critical.")
            .When(x => x.Severity is not null);

        RuleFor(x => x.EvaluationType)
            .Must(e => ValidEvaluationTypes.Contains(e!, StringComparer.Ordinal))
            .WithMessage($"EvaluationType must be one of: {string.Join(", ", ValidEvaluationTypes)}.")
            .When(x => x.EvaluationType is not null);

        RuleFor(x => x.TemporalConfig)
            .NotNull().WithMessage("TemporalConfig is required when EvaluationType is Temporal.")
            .When(x => x.EvaluationType == "Temporal");

        RuleFor(x => x.SequenceConfig)
            .NotNull().WithMessage("SequenceConfig is required when EvaluationType is Sequence.")
            .When(x => x.EvaluationType == "Sequence");
    }
}
