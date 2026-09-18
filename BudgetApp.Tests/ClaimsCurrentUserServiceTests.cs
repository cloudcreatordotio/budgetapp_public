using System.Security.Claims;
using BudgetApp.Services;

namespace BudgetApp.Tests;

public class ClaimsCurrentUserServiceTests
{
    private static ClaimsPrincipal AuthenticatedPrincipal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "Cookies"));

    [Fact]
    public void Maps_a_fully_claimed_principal()
    {
        var principal = AuthenticatedPrincipal(
            new Claim(AppClaims.UserId, "42"),
            new Claim(ClaimTypes.Name, "testuser"),
            new Claim("urn:github:name", "Test User"));

        var user = ClaimsCurrentUserService.ToCurrentUser(principal);

        Assert.Equal(42, user.Id);
        Assert.Equal("testuser", user.Login);
        Assert.Equal("Test User", user.DisplayName);
        Assert.Equal("Test User", user.Name);
    }

    [Fact]
    public void Display_name_is_optional_and_falls_back_to_login()
    {
        var principal = AuthenticatedPrincipal(
            new Claim(AppClaims.UserId, "7"),
            new Claim(ClaimTypes.Name, "testuser"));

        var user = ClaimsCurrentUserService.ToCurrentUser(principal);

        Assert.Null(user.DisplayName);
        Assert.Equal("testuser", user.Name);
    }

    [Fact]
    public void Throws_for_an_unauthenticated_principal()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
        Assert.Throws<InvalidOperationException>(() => ClaimsCurrentUserService.ToCurrentUser(anonymous));
    }

    [Fact]
    public void Throws_when_the_user_id_claim_is_missing()
    {
        var principal = AuthenticatedPrincipal(new Claim(ClaimTypes.Name, "testuser"));
        Assert.Throws<InvalidOperationException>(() => ClaimsCurrentUserService.ToCurrentUser(principal));
    }
}
