namespace AutoFlow.Api.Validation;

/// <summary>
/// A request body that checks its own shape (required fields, lengths,
/// ranges) before any use case runs. Format and business rules (a VIN's check
/// digit, mileage never going backwards) stay in the domain and answer 422.
/// See ADR 0008.
/// </summary>
public interface IValidatableRequest
{
    void Validate(ValidationErrors errors);
}
