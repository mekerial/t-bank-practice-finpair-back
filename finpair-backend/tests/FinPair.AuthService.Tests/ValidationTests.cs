namespace FinPair.AuthService.Tests;

using FinPair.Contracts.Validation;

public class ValidationTests
{
    [Fact]
    public void MoneyValidation_RejectsValuesOutsideNumeric12Scale2()
    {
        var errors = new ValidationErrors();

        DomainValidation.RequiredMoney(errors, "amount", 10.999m);
        DomainValidation.RequiredMoney(errors, "largeAmount", 10000000000m);

        var details = errors.ToDictionary();
        Assert.Contains("amount", details.Keys);
        Assert.Contains("largeAmount", details.Keys);
    }

    [Fact]
    public void PasswordValidation_RequiresLengthLetterAndDigit()
    {
        var errors = new ValidationErrors();

        DomainValidation.RequirePassword(errors, "password", "12345678");

        var details = errors.ToDictionary();
        Assert.Contains("password", details.Keys);
        Assert.Contains(details["password"], message => message.Contains("letter", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("equal")]
    [InlineData("income")]
    [InlineData("income_ratio")]
    [InlineData("custom")]
    public void SplitTypeValidation_AcceptsDocumentedValues(string splitType)
    {
        var errors = new ValidationErrors();

        DomainValidation.RequireSplitType(errors, "splitType", splitType);

        Assert.False(errors.HasErrors);
    }

    [Theory]
    [InlineData("FINPAIR-ABC123")]
    [InlineData("ABCD1234")]
    public void InviteCodeValidation_AcceptsCurrentAndContractExamples(string inviteCode)
    {
        var errors = new ValidationErrors();

        DomainValidation.RequireInviteCode(errors, "inviteCode", inviteCode);

        Assert.False(errors.HasErrors);
    }

    [Fact]
    public void NotificationsValidation_RejectsInvalidKeys()
    {
        var errors = new ValidationErrors();

        DomainValidation.OptionalNotifications(
            errors,
            "notifications",
            new Dictionary<string, bool> { ["bad key"] = true });

        Assert.Contains("notifications", errors.ToDictionary().Keys);
    }
}
