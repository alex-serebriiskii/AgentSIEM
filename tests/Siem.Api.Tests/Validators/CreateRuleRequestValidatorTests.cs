using FluentAssertions;
using FluentValidation.TestHelper;
using Siem.Api.Models.Requests;
using Siem.Api.Validators;

namespace Siem.Api.Tests.Validators;

public class CreateRuleRequestValidatorTests
{
    private readonly CreateRuleRequestValidator _validator = new();

    [Test]
    public async Task ValidRequest_PassesValidation()
    {
        var request = new CreateRuleRequest
        {
            Name = "Test Rule",
            Severity = "high",
            EvaluationType = "SingleEvent",
            CreatedBy = "admin"
        };

        var result = await _validator.TestValidateAsync(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    public async Task EmptyName_FailsValidation(string name)
    {
        var request = new CreateRuleRequest { Name = name, CreatedBy = "admin" };
        var result = await _validator.TestValidateAsync(request);
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Test]
    [Arguments("invalid")]
    [Arguments("EXTREME")]
    [Arguments("")]
    public async Task InvalidSeverity_FailsValidation(string severity)
    {
        var request = new CreateRuleRequest
        {
            Name = "Test",
            Severity = severity,
            CreatedBy = "admin"
        };

        var result = await _validator.TestValidateAsync(request);
        result.ShouldHaveValidationErrorFor(x => x.Severity);
    }

    [Test]
    [Arguments("low")]
    [Arguments("medium")]
    [Arguments("HIGH")]
    [Arguments("Critical")]
    public async Task ValidSeverity_PassesValidation(string severity)
    {
        var request = new CreateRuleRequest
        {
            Name = "Test",
            Severity = severity,
            CreatedBy = "admin"
        };

        var result = await _validator.TestValidateAsync(request);
        result.ShouldNotHaveValidationErrorFor(x => x.Severity);
    }

    [Test]
    [Arguments("invalid")]
    [Arguments("singleevent")]
    public async Task InvalidEvaluationType_FailsValidation(string evalType)
    {
        var request = new CreateRuleRequest
        {
            Name = "Test",
            EvaluationType = evalType,
            CreatedBy = "admin"
        };

        var result = await _validator.TestValidateAsync(request);
        result.ShouldHaveValidationErrorFor(x => x.EvaluationType);
    }

    [Test]
    public async Task TemporalType_WithoutTemporalConfig_FailsValidation()
    {
        var request = new CreateRuleRequest
        {
            Name = "Test",
            EvaluationType = "Temporal",
            TemporalConfig = null,
            CreatedBy = "admin"
        };

        var result = await _validator.TestValidateAsync(request);
        result.ShouldHaveValidationErrorFor(x => x.TemporalConfig);
    }

    [Test]
    public async Task SequenceType_WithoutSequenceConfig_FailsValidation()
    {
        var request = new CreateRuleRequest
        {
            Name = "Test",
            EvaluationType = "Sequence",
            SequenceConfig = null,
            CreatedBy = "admin"
        };

        var result = await _validator.TestValidateAsync(request);
        result.ShouldHaveValidationErrorFor(x => x.SequenceConfig);
    }

    [Test]
    public async Task SingleEventType_WithoutConfigs_PassesValidation()
    {
        var request = new CreateRuleRequest
        {
            Name = "Test",
            EvaluationType = "SingleEvent",
            TemporalConfig = null,
            SequenceConfig = null,
            CreatedBy = "admin"
        };

        var result = await _validator.TestValidateAsync(request);
        result.ShouldNotHaveValidationErrorFor(x => x.TemporalConfig);
        result.ShouldNotHaveValidationErrorFor(x => x.SequenceConfig);
    }

    [Test]
    public async Task EmptyCreatedBy_FailsValidation()
    {
        var request = new CreateRuleRequest
        {
            Name = "Test",
            CreatedBy = ""
        };

        var result = await _validator.TestValidateAsync(request);
        result.ShouldHaveValidationErrorFor(x => x.CreatedBy);
    }
}
