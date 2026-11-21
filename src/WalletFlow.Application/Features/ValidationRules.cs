using FluentValidation;

namespace WalletFlow.Application.Features;

internal static class ValidationRules
{
    public const decimal MaxAmount = 1_000_000m;

    public static IRuleBuilderOptions<T, decimal> MoneyAmount<T>(this IRuleBuilder<T, decimal> rule) =>
        rule.GreaterThan(0).LessThanOrEqualTo(MaxAmount)
            .PrecisionScale(18, 2, ignoreTrailingZeros: true).WithMessage("Amount can have at most 2 decimal places.");

    public static IRuleBuilderOptions<T, string> IdempotencyKey<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("The Idempotency-Key header is required.")
            .Length(8, 64).Matches("^[A-Za-z0-9_-]+$")
            .WithMessage("Idempotency-Key must be 8-64 characters of letters, digits, '-' or '_'.");
}
