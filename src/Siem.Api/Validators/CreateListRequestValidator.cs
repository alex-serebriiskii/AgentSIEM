using FluentValidation;
using Siem.Api.Models.Requests;

namespace Siem.Api.Validators;

public class CreateListRequestValidator : AbstractValidator<CreateListRequest>
{
    public CreateListRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.");

        RuleFor(x => x.Members)
            .Must(m => m.Count == m.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            .WithMessage("Members must not contain duplicates.")
            .When(x => x.Members.Count > 0);
    }
}
