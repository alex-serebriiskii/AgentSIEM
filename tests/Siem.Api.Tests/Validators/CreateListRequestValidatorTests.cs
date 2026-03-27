using FluentAssertions;
using FluentValidation.TestHelper;
using Siem.Api.Models.Requests;
using Siem.Api.Validators;

namespace Siem.Api.Tests.Validators;

public class CreateListRequestValidatorTests
{
    private readonly CreateListRequestValidator _validator = new();

    [Test]
    public async Task ValidRequest_PassesValidation()
    {
        var request = new CreateListRequest
        {
            Name = "Approved Tools",
            Members = ["tool-a", "tool-b"]
        };

        var result = await _validator.TestValidateAsync(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Test]
    public async Task EmptyName_FailsValidation()
    {
        var request = new CreateListRequest { Name = "" };
        var result = await _validator.TestValidateAsync(request);
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Test]
    public async Task DuplicateMembers_FailsValidation()
    {
        var request = new CreateListRequest
        {
            Name = "Test",
            Members = ["tool-a", "Tool-A"]
        };

        var result = await _validator.TestValidateAsync(request);
        result.ShouldHaveValidationErrorFor(x => x.Members);
    }

    [Test]
    public async Task EmptyMembers_PassesValidation()
    {
        var request = new CreateListRequest
        {
            Name = "Test",
            Members = []
        };

        var result = await _validator.TestValidateAsync(request);
        result.ShouldNotHaveAnyValidationErrors();
    }
}
