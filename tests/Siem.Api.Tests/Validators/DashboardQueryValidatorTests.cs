using FluentAssertions;
using FluentValidation.TestHelper;
using Siem.Api.Models.Requests;
using Siem.Api.Validators;

namespace Siem.Api.Tests.Validators;

public class DashboardQueryValidatorTests
{
    private readonly DashboardQueryValidator _validator = new();

    [Test]
    public async Task DefaultQuery_PassesValidation()
    {
        var query = new DashboardQuery();
        var result = await _validator.TestValidateAsync(query);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    [Arguments(721)]
    public async Task InvalidHours_FailsValidation(int hours)
    {
        var query = new DashboardQuery { Hours = hours };
        var result = await _validator.TestValidateAsync(query);
        result.ShouldHaveValidationErrorFor(x => x.Hours);
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    [Arguments(101)]
    public async Task InvalidLimit_FailsValidation(int limit)
    {
        var query = new DashboardQuery { Limit = limit };
        var result = await _validator.TestValidateAsync(query);
        result.ShouldHaveValidationErrorFor(x => x.Limit);
    }

    [Test]
    public async Task MaxValidValues_PassesValidation()
    {
        var query = new DashboardQuery { Hours = 720, Limit = 100 };
        var result = await _validator.TestValidateAsync(query);
        result.ShouldNotHaveAnyValidationErrors();
    }
}
