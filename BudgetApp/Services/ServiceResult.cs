namespace BudgetApp.Services;

/// <summary>Outcome of a write operation; <see cref="Error"/> is user-displayable.</summary>
public sealed record ServiceResult(bool Succeeded, string? Error)
{
    public static readonly ServiceResult Success = new(true, null);

    public static ServiceResult Fail(string error) => new(false, error);
}
