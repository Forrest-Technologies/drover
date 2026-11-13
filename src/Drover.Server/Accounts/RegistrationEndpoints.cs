namespace Drover.Server.Accounts;

/// <summary>
/// Self-registration, for a server whose deployment policy opens it. The first account is made through setup, never
/// here, so registration answers 404 until setup is done and whenever the policy leaves registration closed.
/// </summary>
public static class RegistrationEndpoints
{
    #region Public Methods

    public static IEndpointRouteBuilder MapRegistrationEndpoints(this RouteGroupBuilder api)
    {
        var register = api.MapGroup("/register");
        register.MapGet("/", GetRegistrationStatus).WithName("GetRegistrationStatus");
        register.MapPost("/", Register)
            .RequireRateLimiting(SecurityServiceExtensions.SignInRateLimit)
            .WithName("Register");
        return api;
    }

    #endregion

    #region Endpoints

    private static async Task<Results<Ok<RegistrationStatus>, NotFound>> GetRegistrationStatus(
        HttpContext context,
        AccountService accounts,
        PolicyService policy)
    {
        if (!await IsOpen(context, accounts, policy))
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(new RegistrationStatus(
            policy.Deployment.RequireTermsAcceptance ? policy.TermsVersion : null));
    }

    private static async Task<Results<Created<SessionResponse>, NotFound, ProblemHttpResult>> Register(
        RegisterRequest request,
        HttpContext context,
        AccountService accounts,
        PolicyService policy,
        IAntiforgery antiforgery)
    {
        if (!await IsOpen(context, accounts, policy))
        {
            return TypedResults.NotFound();
        }

        if (policy.Deployment.RequireEmailVerification)
        {
            return TypedResults.Problem(
                title: "Registration needs email verification, and this server cannot send email yet.",
                statusCode: StatusCodes.Status501NotImplemented);
        }

        if (RefusedTerms(request, policy) is { } refusal)
        {
            return TypedResults.Problem(title: refusal, statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName) || request.DisplayName.Length > 200)
        {
            return TypedResults.Problem(title: "A display name of up to 200 characters is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await accounts.Register(
            request.Email,
            request.DisplayName.Trim(),
            request.Password,
            policy.Deployment.RequireTermsAcceptance ? policy.TermsVersion : null,
            context.RequestAborted);

        return result.Status is SetupOutcome.Created
            ? TypedResults.Created(
                "/api/session",
                await AccountEndpoints.StartSession(
                    context,
                    accounts,
                    antiforgery,
                    new CredentialResult(true, result.UserId, request.Email, request.DisplayName.Trim(), result.SecurityStamp)))
            : TypedResults.Problem(title: string.Join(" ", result.Problems), statusCode: StatusCodes.Status400BadRequest);
    }

    #endregion

    #region Private Methods

    private static async Task<bool> IsOpen(HttpContext context, AccountService accounts, PolicyService policy) =>
        policy.Deployment.OpenRegistration && !await accounts.IsSetupOpen(context.RequestAborted);

    private static string? RefusedTerms(RegisterRequest request, PolicyService policy) =>
        policy.Deployment.RequireTermsAcceptance && request.AcceptedTermsVersion != policy.TermsVersion
            ? $"You need to accept the current terms of service, version {policy.TermsVersion}."
            : null;

    #endregion
}
