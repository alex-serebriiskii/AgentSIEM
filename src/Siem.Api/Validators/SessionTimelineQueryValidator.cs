using FluentValidation;
using Siem.Api.Models.Requests;

namespace Siem.Api.Validators;

public class SessionTimelineQueryValidator : AbstractValidator<SessionTimelineQuery>
{
    public SessionTimelineQueryValidator()
    {
        RuleFor(x => x.Limit)
            .InclusiveBetween(1, 5000)
            .WithMessage("Limit must be between 1 and 5000.");
    }
}
