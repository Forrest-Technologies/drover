namespace Drover.Server.Accounts;

public static class AccountEndpoints
{
    #region Public Methods

    public static IEndpointRouteBuilder MapAccountEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/antiforgery", IssueAntiforgeryToken)
            .WithName("GetAntiforgeryToken")
            .WithSummary("Sets the XSRF-TOKEN cookie to send back in the X-XSRF-TOKEN header on every state-changing request.");

        // Only the attempt is limited. Asking whether setup is open costs nothing to answer, and limiting it
        // would let anyone spend the sign-in allowance of everyone behind the same address.
        var setup = api.MapGroup("/setup");
        setup.MapGet("/", GetSetupStatus).WithName("GetSetupStatus");
        setup.MapPost("/", CreateFirstAccount)
            .RequireRateLimiting(SecurityServiceExtensions.SignInRateLimit)
            .WithName("CreateFirstAccount");

        api.MapPost("/sign-in", SignIn)
            .RequireRateLimiting(SecurityServiceExtensions.SignInRateLimit)
            .WithName("SignIn");
        api.MapPost("/sign-out", (Delegate)SignOut).RequireAuthorization().WithName("SignOut");
        api.MapGet("/session", GetSession).RequireAuthorization().WithName("GetSession");

        // Changing the password checks the current one, so it is limited like a sign-in. So is redeeming a reset code.
        api.MapPost("/account/password", ChangePassword)
            .RequireAuthorization()
            .RequireRateLimiting(SecurityServiceExtensions.SignInRateLimit)
            .WithName("ChangePassword");
        api.MapPost("/password-reset", ResetPassword)
            .RequireRateLimiting(SecurityServiceExtensions.SignInRateLimit)
            .WithName("ResetPassword");
        return api;
    }

    #endregion

    #region Endpoints

    private static NoContent IssueAntiforgeryToken(HttpContext context, IAntiforgery antiforgery)
    {
        AntiforgeryTokens.Issue(context, antiforgery);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<SetupStatus>, NotFound>> GetSetupStatus(
        HttpContext context,
        AccountService accounts,
        PolicyService policy)
    {
        if (!await accounts.IsSetupOpen(context.RequestAborted))
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(new SetupStatus(RequiresCode: policy.Deployment.RequireSetupCode));
    }

    private static async Task<Results<Created<SessionResponse>, NotFound, ProblemHttpResult>> CreateFirstAccount(
        SetupRequest request,
        HttpContext context,
        AccountService accounts,
        PolicyService policy,
        SetupCode code,
        IAntiforgery antiforgery)
    {
        if (!await accounts.IsSetupOpen(context.RequestAborted))
        {
            return TypedResults.NotFound();
        }

        if (policy.Deployment.RequireSetupCode && !code.Matches(request.SetupCode))
        {
            return TypedResults.Problem(
                title: "The setup code is required. It is printed in the server log.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName) || request.DisplayName.Length > 200)
        {
            return TypedResults.Problem(title: "A display name of up to 200 characters is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await accounts.CreateFirstAccount(
            request.Email,
            request.DisplayName.Trim(),
            request.Password,
            context.RequestAborted);

        return result.Status switch
        {
            SetupOutcome.Closed => TypedResults.NotFound(),
            SetupOutcome.Invalid => TypedResults.Problem(
                title: string.Join(" ", result.Problems),
                statusCode: StatusCodes.Status400BadRequest),
            _ => TypedResults.Created(
                "/api/session",
                await StartSession(
                    context,
                    accounts,
                    antiforgery,
                    new CredentialResult(true, result.UserId, request.Email, request.DisplayName.Trim(), result.SecurityStamp))),
        };
    }

    private static async Task<Results<Ok<SessionResponse>, ProblemHttpResult>> SignIn(
        SignInRequest request,
        HttpContext context,
        AccountService accounts,
        IAntiforgery antiforgery)
    {
        var credentials = await accounts.CheckCredentials(
            request.Email ?? string.Empty,
            request.Password ?? string.Empty,
            context.RequestAborted);

        if (!credentials.Succeeded)
        {
            return TypedResults.Problem(title: "The email or password is not right.", statusCode: StatusCodes.Status401Unauthorized);
        }

        return TypedResults.Ok(await StartSession(context, accounts, antiforgery, credentials));
    }

    private static async Task<NoContent> SignOut(HttpContext context, SessionService sessions)
    {
        if (context.User.GetSessionId() is { } sessionId)
        {
            await sessions.End(context.User.GetUserId(), sessionId, context.RequestAborted);
        }

        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> ChangePassword(
        ChangePasswordRequest request,
        HttpContext context,
        PasswordService passwords)
    {
        var principal = context.User;

        if (principal.GetSessionId() is not { } sessionId)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized);
        }

        var result = await passwords.Change(
            principal.GetUserId(),
            sessionId,
            request.CurrentPassword ?? string.Empty,
            request.NewPassword ?? string.Empty,
            context.RequestAborted);

        if (result.Status is not PasswordOutcome.Changed)
        {
            return RefusedPassword(result, "The current password is not right.");
        }

        // The stamp changed with the password, so this session's cookie is issued again with the new one.
        var now = context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            SessionStamp.BuildPrincipal(
                principal.GetUserId(),
                principal.FindFirstValue(ClaimTypes.Email) ?? string.Empty,
                principal.FindFirstValue(ClaimTypes.Name) ?? string.Empty,
                result.SecurityStamp,
                sessionId,
                now));
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> ResetPassword(
        ResetPasswordRequest request,
        HttpContext context,
        PasswordService passwords)
    {
        var result = await passwords.RedeemResetCode(
            request.Email ?? string.Empty,
            request.Code ?? string.Empty,
            request.NewPassword ?? string.Empty,
            context.RequestAborted);

        return result.Status is PasswordOutcome.Changed
            ? TypedResults.NoContent()
            : RefusedPassword(result, "The reset code is not right, has been used, or has run out. Ask the operator for a new one.");
    }

    private static async Task<Ok<SessionResponse>> GetSession(HttpContext context, AccountService accounts)
    {
        var principal = context.User;
        var spaces = await accounts.ListSpaces(principal.GetUserId(), context.RequestAborted);
        return TypedResults.Ok(BuildSession(
            principal.GetUserId(),
            principal.FindFirstValue(ClaimTypes.Email) ?? string.Empty,
            principal.FindFirstValue(ClaimTypes.Name) ?? string.Empty,
            spaces));
    }

    #endregion

    #region Private Methods

    internal static async Task<SessionResponse> StartSession(
        HttpContext context,
        AccountService accounts,
        IAntiforgery antiforgery,
        CredentialResult person)
    {
        var (_, userId, email, displayName, stamp) = person;
        var now = context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
        var sessionId = await context.RequestServices.GetRequiredService<SessionService>().Open(userId, context.RequestAborted);
        var principal = SessionStamp.BuildPrincipal(userId, email, displayName, stamp, sessionId, now);
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

        // The antiforgery token belongs to the signed-in person, so the client gets a fresh one with the session.
        context.User = principal;
        AntiforgeryTokens.Issue(context, antiforgery);

        var spaces = await accounts.ListSpaces(userId, context.RequestAborted);
        return BuildSession(userId, email, displayName, spaces);
    }

    private static ProblemHttpResult RefusedPassword(PasswordResult result, string refusal) =>
        TypedResults.Problem(
            title: result.Status is PasswordOutcome.Invalid ? string.Join(" ", result.Problems) : refusal,
            statusCode: StatusCodes.Status400BadRequest);

    private static SessionResponse BuildSession(
        Guid userId,
        string email,
        string displayName,
        IReadOnlyList<SpaceMembership> spaces) =>
        new(
            userId,
            email,
            displayName,
            [.. spaces.Select(space => new SpaceSummary(space.SpaceId, space.Kind, space.Name, space.Slug, space.Role))]);

    #endregion
}
