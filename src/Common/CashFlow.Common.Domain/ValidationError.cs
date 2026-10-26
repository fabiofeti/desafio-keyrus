namespace CashFlow.Common.Domain;

/// <summary>
/// The one error that carries more than a single message: a failed validation
/// has to name every field that failed, or the client has to guess.
/// </summary>
public sealed record ValidationError : Error
{
    public ValidationError(Error[] errors)
        : base("Validation.General", "One or more validation errors occurred.", ErrorType.Validation)
    {
        Errors = errors;
    }

    public Error[] Errors { get; }

    public static ValidationError FromResults(IEnumerable<Result> results) =>
        new([.. results.Where(r => r.IsFailure).Select(r => r.Error)]);
}
