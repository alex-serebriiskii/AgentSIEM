using FluentAssertions;
using FluentValidation.TestHelper;
using Siem.Api.Models.Requests;
using Siem.Api.Validators;

namespace Siem.Api.Tests.Validators;

public class SessionTimelineQueryValidatorTests
{
    private readonly SessionTimelineQueryValidator _validator = new();

    [Test]
    public async Task DefaultQuery_PassesValidation()
    {
        var query = new SessionTimelineQuery();
        var result = await _validator.TestValidateAsync(query);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    [Arguments(5001)]
    public async Task InvalidLimit_FailsValidation(int limit)
    {
        var query = new SessionTimelineQuery { Limit = limit };
        var result = await _validator.TestValidateAsync(query);
        result.ShouldHaveValidationErrorFor(x => x.Limit);
    }

    [Test]
    [Arguments(1)]
    [Arguments(5000)]
    public async Task BoundaryValues_PassValidation(int limit)
    {
        var query = new SessionTimelineQuery { Limit = limit };
        var result = await _validator.TestValidateAsync(query);
        result.ShouldNotHaveAnyValidationErrors();
    }
}
