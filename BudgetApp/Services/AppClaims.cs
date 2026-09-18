namespace BudgetApp.Services;

/// <summary>Custom claim types BudgetApp adds to the cookie principal.</summary>
public static class AppClaims
{
    /// <summary>
    /// The internal Users.Id, stamped in OnCreatingTicket after provisioning so
    /// ClaimsCurrentUserService never needs a database round-trip.
    /// </summary>
    public const string UserId = "urn:budgetapp:user-id";
}
