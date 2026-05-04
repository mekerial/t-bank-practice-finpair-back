using System.Net.Mail;
using System.Text.RegularExpressions;

namespace FinPair.Contracts.Validation;

public static partial class DomainValidation
{
    public const decimal MoneyMaxValue = 9999999999.99m;

    private static readonly HashSet<string> TransactionTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "income",
        "expense"
    };

    private static readonly HashSet<string> SplitTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "equal",
        "income",
        "income_ratio",
        "custom"
    };

    private static readonly HashSet<string> SortFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "date",
        "amount",
        "created_at",
        "createdat"
    };

    private static readonly HashSet<string> SortOrders = new(StringComparer.OrdinalIgnoreCase)
    {
        "asc",
        "desc"
    };

    public static bool IsTransactionType(string? value) =>
        !string.IsNullOrWhiteSpace(value) && TransactionTypes.Contains(value);

    public static bool IsSplitType(string? value) =>
        !string.IsNullOrWhiteSpace(value) && SplitTypes.Contains(value);

    public static bool IsSortField(string? value) =>
        string.IsNullOrWhiteSpace(value) || SortFields.Contains(value);

    public static bool IsSortOrder(string? value) =>
        string.IsNullOrWhiteSpace(value) || SortOrders.Contains(value);

    public static void RequireEmail(ValidationErrors errors, string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(field, "Email is required.");
            return;
        }

        if (value.Length > 254)
        {
            errors.Add(field, "Email must contain 254 characters or fewer.");
            return;
        }

        try
        {
            var address = new MailAddress(value);
            if (!string.Equals(address.Address, value.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(field, "Email is invalid.");
            }
        }
        catch (FormatException)
        {
            errors.Add(field, "Email is invalid.");
        }
    }

    public static void RequirePassword(ValidationErrors errors, string field, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            errors.Add(field, "Password is required.");
            return;
        }

        if (value.Length < 8)
        {
            errors.Add(field, "Password must contain at least 8 characters.");
        }

        if (value.Length > 128)
        {
            errors.Add(field, "Password must contain 128 characters or fewer.");
        }

        if (!value.Any(char.IsLetter))
        {
            errors.Add(field, "Password must contain at least one letter.");
        }

        if (!value.Any(char.IsDigit))
        {
            errors.Add(field, "Password must contain at least one digit.");
        }
    }

    public static void OptionalText(
        ValidationErrors errors,
        string field,
        string? value,
        int maxLength,
        bool allowBlank = true)
    {
        if (value is null)
        {
            return;
        }

        if (!allowBlank && string.IsNullOrWhiteSpace(value))
        {
            errors.Add(field, $"{ToLabel(field)} must not be empty.");
            return;
        }

        if (value.Trim().Length > maxLength)
        {
            errors.Add(field, $"{ToLabel(field)} must contain {maxLength} characters or fewer.");
        }
    }

    public static void RequiredText(ValidationErrors errors, string field, string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(field, $"{ToLabel(field)} is required.");
            return;
        }

        if (value.Trim().Length > maxLength)
        {
            errors.Add(field, $"{ToLabel(field)} must contain {maxLength} characters or fewer.");
        }
    }

    public static void RequireTransactionType(ValidationErrors errors, string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(field, "Type is required.");
            return;
        }

        if (!IsTransactionType(value))
        {
            errors.Add(field, "Type must be income or expense.");
        }
    }

    public static void OptionalTransactionType(ValidationErrors errors, string field, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !IsTransactionType(value))
        {
            errors.Add(field, "Type must be income or expense.");
        }
    }

    public static void RequireSplitType(ValidationErrors errors, string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(field, "Split type is required.");
            return;
        }

        OptionalSplitType(errors, field, value);
    }

    public static void OptionalSplitType(ValidationErrors errors, string field, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !IsSplitType(value))
        {
            errors.Add(field, "Split type must be equal, income, income_ratio or custom.");
        }
    }

    public static void OptionalCurrency(ValidationErrors errors, string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var normalized = value.Trim().ToUpperInvariant();
        if (!CurrencyRegex().IsMatch(normalized))
        {
            errors.Add(field, "Currency must be a 3-letter ISO code.");
        }
    }

    public static void RequireCurrency(ValidationErrors errors, string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(field, "Currency is required.");
            return;
        }

        OptionalCurrency(errors, field, value);
    }

    public static void OptionalNotifications(
        ValidationErrors errors,
        string field,
        IReadOnlyDictionary<string, bool>? value)
    {
        if (value is null)
        {
            return;
        }

        if (value.Count > 20)
        {
            errors.Add(field, "Notifications must contain 20 entries or fewer.");
        }

        foreach (var key in value.Keys)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                errors.Add(field, "Notification keys must not be empty.");
            }
            else if (key.Length > 50)
            {
                errors.Add(field, "Notification keys must contain 50 characters or fewer.");
            }
            else if (!NotificationKeyRegex().IsMatch(key))
            {
                errors.Add(field, "Notification keys may contain only letters, digits, underscores and hyphens.");
            }
        }
    }

    public static void RequireInviteCode(ValidationErrors errors, string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(field, "Invite code is required.");
            return;
        }

        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length is < 6 or > 32 || !InviteCodeRegex().IsMatch(normalized))
        {
            errors.Add(field, "Invite code format is invalid.");
        }
    }

    public static void RequiredMoney(
        ValidationErrors errors,
        string field,
        decimal? value,
        bool allowZero = false)
    {
        if (value is null)
        {
            errors.Add(field, $"{ToLabel(field)} is required.");
            return;
        }

        Money(errors, field, value.Value, allowZero);
    }

    public static void OptionalMoney(
        ValidationErrors errors,
        string field,
        decimal? value,
        bool allowZero = false)
    {
        if (value is not null)
        {
            Money(errors, field, value.Value, allowZero);
        }
    }

    public static void RequiredDate(ValidationErrors errors, string field, DateOnly? value)
    {
        if (value is null)
        {
            errors.Add(field, $"{ToLabel(field)} is required.");
            return;
        }

        Date(errors, field, value.Value);
    }

    public static void OptionalDate(ValidationErrors errors, string field, DateOnly? value)
    {
        if (value is not null)
        {
            Date(errors, field, value.Value);
        }
    }

    public static void RequiredGuid(ValidationErrors errors, string field, Guid value)
    {
        if (value == Guid.Empty)
        {
            errors.Add(field, $"{ToLabel(field)} is required.");
        }
    }

    public static bool HasMoneyScale(decimal value, int scale)
    {
        var bits = decimal.GetBits(value);
        var actualScale = (bits[3] >> 16) & 0x7F;
        return actualScale <= scale;
    }

    private static void Money(ValidationErrors errors, string field, decimal value, bool allowZero)
    {
        if (allowZero ? value < 0 : value <= 0)
        {
            errors.Add(field, allowZero
                ? $"{ToLabel(field)} must be greater than or equal to 0."
                : $"{ToLabel(field)} must be greater than 0.");
            return;
        }

        if (value > MoneyMaxValue)
        {
            errors.Add(field, $"{ToLabel(field)} must be less than or equal to {MoneyMaxValue}.");
        }

        if (!HasMoneyScale(value, 2))
        {
            errors.Add(field, $"{ToLabel(field)} must have no more than 2 decimal places.");
        }
    }

    private static void Date(ValidationErrors errors, string field, DateOnly value)
    {
        if (value == default)
        {
            errors.Add(field, $"{ToLabel(field)} is invalid.");
        }
    }

    private static string ToLabel(string field)
    {
        if (string.IsNullOrWhiteSpace(field))
        {
            return "Value";
        }

        return char.ToUpperInvariant(field[0]) + field[1..];
    }

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex CurrencyRegex();

    [GeneratedRegex("^[A-Z0-9-]+$")]
    private static partial Regex InviteCodeRegex();

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex NotificationKeyRegex();
}
