using System.ComponentModel.DataAnnotations;

namespace Restaurantes.Payments.Contracts.Requests;

public sealed class SetPayableOrderAmountRequest : IValidatableObject
{
    public decimal Amount { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Amount is <= 0 or > 1_000_000)
        {
            yield return new ValidationResult(
                "Amount must be greater than zero and no more than 1000000.",
                [nameof(Amount)]
            );
        }
    }
}
