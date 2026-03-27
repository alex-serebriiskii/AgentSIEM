using FluentAssertions;
using FluentValidation.TestHelper;
using Siem.Api.Models.Requests;
using Siem.Api.Validators;

namespace Siem.Api.Tests.Validators;

public class UpdateListMembersRequestValidatorTests
{
    private readonly UpdateListMembersRequestValidator _validator = new();

    [Test]
    public async Task UniqueMembers_PassesValidation()
    {
        var request = new UpdateListMembersRequest { Members = ["a", "b", "c"] };
        var result = await _validator.TestValidateAsync(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Test]
    public async Task DuplicateMembers_FailsValidation()
    {
        var request = new UpdateListMembersRequest { Members = ["a", "A"] };
        var result = await _validator.TestValidateAsync(request);
        result.ShouldHaveValidationErrorFor(x => x.Members);
    }
}
