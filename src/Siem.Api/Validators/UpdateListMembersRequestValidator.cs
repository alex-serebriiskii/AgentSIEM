using FluentValidation;
using Siem.Api.Models.Requests;

namespace Siem.Api.Validators;

public class UpdateListMembersRequestValidator : AbstractValidator<UpdateListMembersRequest>
{
    public UpdateListMembersRequestValidator()
    {
        RuleFor(x => x.Members)
            .Must(m => m.Count == m.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            .WithMessage("Members must not contain duplicates.")
            .When(x => x.Members.Count > 0);
    }
}
