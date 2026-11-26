namespace Drover.Domain.Tests;

/// <summary>
/// No type in <c>Drover.Contracts</c> can carry a secret value. A property whose name says it holds one fails the
/// test, whether it sits on the type itself or on a type reachable from it. The only exceptions are the
/// inbound forms a person fills in with their own credentials, which no response may contain.
/// </summary>
public sealed class ContractsCarryNoSecretsTests
{
    #region Private Fields

    private static readonly string[] secretWords =
        ["password", "secret", "token", "apikey", "credential", "ciphertext", "passphrase", "privatekey", "code", "value"];

    private static readonly string[] allowedWords = ["hasvalue", "requirescode"];

    private static readonly string[] inboundCredentialForms = ["SignInRequest", "SetupRequest", "RegisterRequest", "CreateModelConnectionRequest", "UpdateModelKeyRequest", "CreateToolConnectionRequest", "ConnectExtensionRequest", "ReplaceToolHeaderRequest", "ChangePasswordRequest", "ResetPasswordRequest", "PushDeviceRequest", "MovePushDeviceRequest", "FinishToolSignInRequest", "CreateSandboxSecretRequest", "RotateSandboxSecretRequest"];

    // A card's figure, row and answer values are the words shown on the card, not credentials. Their names are fixed
    // by the card contract; #880 refuses a field label that names a credential, and an answer is the person's message.
    private static readonly (Type Type, string Name)[] cardDisplayValues =
    [
        (typeof(Drover.Contracts.Cards.CardAnswer), "Values"),
        (typeof(Drover.Contracts.Cards.CardFigure), "Value"),
        (typeof(Drover.Contracts.Cards.CardRow), "Value"),
    ];

    #endregion

    #region Public Methods

    [Fact]
    public void No_contract_type_has_a_property_that_can_carry_a_secret_value()
    {
        var offenders = ContractTypes()
            .Where(type => !inboundCredentialForms.Contains(type.Name))
            .SelectMany(type => ReachableProperties(type).Select(property => $"{type.Name} reaches {property}"))
            .ToList();

        offenders.Should().BeEmpty();
    }

    [Fact]
    public void The_inbound_credential_forms_are_requests_and_print_nothing_when_logged()
    {
        var sample = new Drover.Contracts.Auth.SignInRequest("a@example.test", "a-password-value");
        var setup = new Drover.Contracts.Auth.SetupRequest("a@example.test", "A", "a-password-value", "CODE");

        new Drover.Contracts.Models.CreateModelConnectionRequest("venice", "V", null, "a-model-key-value", null)
            .ToString().Should().NotContain("a-model-key-value");
        new Drover.Contracts.Models.UpdateModelKeyRequest("a-model-key-value").ToString().Should().NotContain("a-model-key-value");
        new Drover.Contracts.Tools.CreateToolConnectionRequest("T", "https://tools.example.test/", "header", "X-Api-Key", "a-header-value")
            .ToString().Should().NotContain("a-header-value");
        new Drover.Contracts.Tools.ReplaceToolHeaderRequest("X-Api-Key", "a-header-value").ToString().Should().NotContain("a-header-value");
        new Drover.Contracts.Auth.ChangePasswordRequest("a-password-value", "a-new-password-value")
            .ToString().Should().NotContain("password-value");
        new Drover.Contracts.Auth.ResetPasswordRequest("a@example.test", "A-RESET-CODE", "a-new-password-value")
            .ToString().Should().NotContain("password-value").And.NotContain("A-RESET-CODE");
        new Drover.Contracts.Push.PushDeviceRequest("https://push.example.test/a-device", "a-device-key", "a-device-secret")
            .ToString().Should().NotContain("a-device");
        new Drover.Contracts.Push.MovePushDeviceRequest(
                "https://push.example.test/a-previous-device",
                new("https://push.example.test/a-device", "a-device-key", "a-device-secret"))
            .ToString().Should().NotContain("a-device").And.NotContain("a-previous-device");
        new Drover.Contracts.Extensions.ConnectExtensionRequest(false, "a-header-value") { Settings = new Dictionary<string, string> { ["token"] = "a-setting-value" } }
            .ToString().Should().NotContain("a-header-value").And.NotContain("a-setting-value");
        new Drover.Contracts.Tools.FinishToolSignInRequest("a-ticket-value").ToString().Should().NotContain("a-ticket-value");
        new Drover.Contracts.Sandboxes.CreateSandboxSecretRequest("DEPLOY_TOKEN", null, "a-sandbox-secret-value", "brokered", ["api.example.test"], false, false, "space", null, null)
            .ToString().Should().NotContain("a-sandbox-secret-value");
        new Drover.Contracts.Sandboxes.RotateSandboxSecretRequest("a-sandbox-secret-value").ToString().Should().NotContain("a-sandbox-secret-value");
        inboundCredentialForms.Should().OnlyContain(name => name.EndsWith("Request", StringComparison.Ordinal));
        sample.ToString().Should().NotContain("a-password-value");
        setup.ToString().Should().NotContain("a-password-value").And.NotContain("CODE");
    }

    [Fact]
    public void The_check_would_catch_a_response_that_carried_a_secret()
    {
        ReachableProperties(typeof(LeakyResponse)).Should().NotBeEmpty();
        ReachableProperties(typeof(WrapsALeak)).Should().NotBeEmpty();
    }

    [Fact]
    public void A_count_of_tokens_is_not_a_secret_and_text_under_the_same_name_still_is()
    {
        ReachableProperties(typeof(CountsTokens)).Should().BeEmpty();
        ReachableProperties(typeof(HoldsTokens)).Should().Equal("HoldsTokens.RefreshTokens", "HoldsTokens.Token");
    }

    [Fact]
    public void The_card_value_exceptions_are_only_the_display_values()
    {
        ReachableProperties(typeof(Drover.Contracts.Cards.CardAnswer)).Should().BeEmpty();
        ReachableProperties(typeof(Drover.Contracts.Cards.CardFigure)).Should().BeEmpty();
        ReachableProperties(typeof(Drover.Contracts.Cards.CardRow)).Should().BeEmpty();
        ReachableProperties(typeof(HoldsAValue)).Should().Equal("HoldsAValue.Value");
    }

    #endregion

    #region Private Methods

    private static IEnumerable<Type> ContractTypes() =>
        typeof(Drover.Contracts.Auth.SessionResponse).Assembly.GetExportedTypes()
            .Where(type => type.IsClass || type.IsValueType)
            // An exception is thrown and caught, never sent: what the runtime hangs on one is not part of any contract.
            .Where(type => !typeof(Exception).IsAssignableFrom(type));

    private static IEnumerable<string> ReachableProperties(Type type, HashSet<Type>? seen = null)
    {
        seen ??= [];

        // A runtime type's own members (the kind of a JSON element, say) are named by the runtime, not by a contract.
        if (type.Namespace?.StartsWith("System", StringComparison.Ordinal) is true || !seen.Add(type))
        {
            yield break;
        }

        foreach (var property in type.GetProperties())
        {
            if (LooksLikeASecret(property))
            {
                yield return $"{property.DeclaringType!.Name}.{property.Name}";
            }

            foreach (var inner in ReachableProperties(Unwrap(property.PropertyType), seen))
            {
                yield return inner;
            }
        }
    }

    private static Type Unwrap(Type type)
    {
        if (type.HasElementType)
        {
            return Unwrap(type.GetElementType()!);
        }

        return type.IsGenericType ? Unwrap(type.GetGenericArguments()[0]) : type;
    }

    private static bool LooksLikeASecret(PropertyInfo property)
    {
        var lower = property.Name.ToLowerInvariant();
        return !allowedWords.Contains(lower)
            && !IsATokenCount(property)
            && !IsCardDisplayValue(property)
            && secretWords.Any(word => lower.Contains(word, StringComparison.Ordinal));
    }

    private static bool IsCardDisplayValue(PropertyInfo property) =>
        Array.Exists(cardDisplayValues, item => item.Type == property.DeclaringType && item.Name == property.Name);

    // "Tokens" also names what a model counts its text in. A whole number called "...Tokens" is such a count, a size
    // and never a credential, and it keeps the name the data model gives it (max_output_tokens). The type is what
    // makes it safe: text under the same name is still refused.
    private static bool IsATokenCount(PropertyInfo property)
    {
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        return property.Name.EndsWith("Tokens", StringComparison.Ordinal) && (type == typeof(int) || type == typeof(long));
    }

    #endregion

    #region Nested Types

    private sealed record LeakyResponse(string ApiKey);

    private sealed record CountsTokens(int? MaxOutputTokens, long UsedTokens);

    private sealed record HoldsTokens(string RefreshTokens, int Token);

    private sealed record HoldsAValue(string Value);

    private sealed record WrapsALeak(List<LeakyResponse> Inner);

    #endregion
}
