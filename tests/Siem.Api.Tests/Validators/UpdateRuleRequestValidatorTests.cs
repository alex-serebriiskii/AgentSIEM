using FluentAssertions;
using FluentValidation.TestHelper;
using Siem.Api.Models.Requests;
using Siem.Api.Validators;

namespace Siem.Api.Tests.Validators;

public class UpdateRuleRequestValidatorTests
{
    private readonly UpdateRuleRequestValidator _validator = new();

    [Test]
    public async Task AllNullFields_PassesValidation()
    {
        var request = new UpdateRuleRequest();
        var result = await _validator.TestValidateAsync(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Test]
    public async Task EmptyName_FailsValidation()
    {
        var request = new UpdateRuleRequest { Name = "" };
        var result = await _validator.TestValidateAsync(request);
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Test]
    public async Task NullName_SkipsValidation()
    {
        var request = new UpdateRuleRequest { Name = null };
        var result = await _validator.TestValidateAsync(request);
        result.ShouldNotHaveValidationErrorFor(x => x.Name);
    }

    [Test]
    public async Task InvalidSeverity_FailsValidation()
    {
        var request = new UpdateRuleRequest { Severity = "invalid" };
        var result = await _validator.TestValidateAsync(request);
        result.ShouldHaveValidationErrorFor(x => x.Severity);
    }

    [Test]
    public async Task NullSeverity_SkipsValidation()
    {
        var request = new UpdateRuleRequest { Severity = null };
        var result = await _validator.TestValidateAsync(request);
        result.ShouldNotHaveValidationErrorFor(x => x.Severity);
    }

    [Test]
    public async Task TemporalType_WithoutConfig_FailsValidation()
    {
        var request = new UpdateRuleRequest
        {
            EvaluationType = "Temporal",
            TemporalConfig = null
        };

        var result = await _validator.TestValidateAsync(request);
        result.ShouldHaveValidationErrorFor(x => x.TemporalConfig);
    }
}
