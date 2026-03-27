using FluentAssertions;
using FluentValidation.TestHelper;
using Siem.Api.Models.Requests;
using Siem.Api.Validators;

namespace Siem.Api.Tests.Validators;

public class CreateSuppressionRequestValidatorTests
{
    private readonly CreateSuppressionRequestValidator _validator = new();

    [Test]
    public async Task ValidRequest_WithRuleId_PassesValidation()
    {
        var request = new CreateSuppressionRequest
        {
            RuleId = Guid.NewGuid(),
            Reason = "False positive",
            CreatedBy = "admin",
            DurationMinutes = 60
        };

        var result = await _validator.TestValidateAsync(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Test]
    public async Task ValidRequest_WithAgentId_PassesValidation()
    {
        var request = new CreateSuppressionRequest
        {
            AgentId = "agent-123",
            Reason = "Maintenance",
            CreatedBy = "admin",
            DurationMinutes = 30
        };

        var result = await _validator.TestValidateAsync(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Test]
    public async Task NoRuleIdOrAgentId_FailsValidation()
    {
        var request = new CreateSuppressionRequest
        {
            Reason = "Test",
            CreatedBy = "admin",
            DurationMinutes = 60
        };

        var result = await _validator.TestValidateAsync(request);
        result.ShouldHaveAnyValidationError();
    }

    [Test]
    public async Task EmptyReason_FailsValidation()
    {
        var request = new CreateSuppressionRequest
        {
            RuleId = Guid.NewGuid(),
            Reason = "",
            CreatedBy = "admin",
            DurationMinutes = 60
        };

        var result = await _validator.TestValidateAsync(request);
        result.ShouldHaveValidationErrorFor(x => x.Reason);
    }

    [Test]
    public async Task ZeroDuration_FailsValidation()
    {
        var request = new CreateSuppressionRequest
        {
            RuleId = Guid.NewGuid(),
            Reason = "Test",
            CreatedBy = "admin",
            DurationMinutes = 0
        };

        var result = await _validator.TestValidateAsync(request);
        result.ShouldHaveValidationErrorFor(x => x.DurationMinutes);
    }
}
