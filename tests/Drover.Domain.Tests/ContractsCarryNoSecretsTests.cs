namespace Drover.Domain.Tests;

/// <summary>
/// No type in <c>Drover.Contracts</c> can carry a secret value. A property whose name says it holds one fails the
/// test, whether it sits on the type itself or on a type reachable from it. The only exceptions are the two
/// inbound forms a person fills in with their own credentials, which no response may contain.
/// </summary>
public sealed class ContractsCarryNoSecretsTests
{
    #region Private Fields

    private static readonly string[] secretWords =
        ["password", "secret", "token", "apikey", "credential", "ciphertext", "passphrase", "privatekey", "code", "value"];

    private static readonly string[] allowedWords = ["hasvalue", "requirescode"];

    private static readonly string[] inboundCredentialForms = ["SignInRequest", "SetupRequest"];

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

    #endregion

    #region Private Methods

    private static IEnumerable<Type> ContractTypes() =>
        typeof(Drover.Contracts.Auth.SessionResponse).Assembly.GetExportedTypes().Where(type => type.IsClass || type.IsValueType);

    private static IEnumerable<string> ReachableProperties(Type type, HashSet<Type>? seen = null)
    {
        seen ??= [];

        if (!seen.Add(type))
        {
            yield break;
        }

        foreach (var property in type.GetProperties())
        {
            if (LooksLikeASecret(property.Name))
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

    private static bool LooksLikeASecret(string name)
    {
        var lower = name.ToLowerInvariant();
        return !allowedWords.Contains(lower) && secretWords.Any(word => lower.Contains(word, StringComparison.Ordinal));
    }

    #endregion

    #region Nested Types

    private sealed record LeakyResponse(string ApiKey);

    private sealed record WrapsALeak(List<LeakyResponse> Inner);

    #endregion
}
