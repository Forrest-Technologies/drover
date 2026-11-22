namespace Drover.Domain.Secrets;

/// <summary>
/// What a sandbox secret may be. Each check returns the sentence a person is told, or null when the input is fine.
/// The checks never repeat a value.
/// </summary>
public static partial class SandboxSecretRules
{
    #region Private Fields

    // Names that change how shells and common runtimes start, load code, reach the network, or trust certificates.
    // A deny list: an unlisted name is allowed, which is not proof that it is harmless.
    private static readonly HashSet<string> reservedNames =
    [
        "PATH", "HOME", "USER", "LOGNAME", "SHELL", "PWD", "OLDPWD", "TERM", "IFS", "ENV", "BASH_ENV", "BASHOPTS",
        "SHELLOPTS", "CDPATH", "GLOBIGNORE", "PROMPT_COMMAND", "PS4", "TMPDIR", "DISPLAY", "XAUTHORITY",
        "SSH_AUTH_SOCK", "NODE_OPTIONS", "NODE_PATH", "PYTHONPATH", "PYTHONHOME", "PYTHONSTARTUP", "PERL5LIB",
        "PERL5OPT", "RUBYOPT", "JAVA_TOOL_OPTIONS", "JDK_JAVA_OPTIONS", "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY",
        "NO_PROXY", "SSL_CERT_FILE", "SSL_CERT_DIR", "SSLKEYLOGFILE", "REQUESTS_CA_BUNDLE", "CURL_CA_BUNDLE",
        "NODE_EXTRA_CA_CERTS", "NODE_TLS_REJECT_UNAUTHORIZED", "GIT_SSL_CAINFO", "GIT_SSL_NO_VERIFY", "OPENSSL_CONF",
        "OPENSSL_ENGINES", "OPENSSL_MODULES", "GIT_SSH", "GIT_SSH_COMMAND", "GIT_ASKPASS", "GIT_PROXY_COMMAND",
        "GIT_EXTERNAL_DIFF", "GIT_PAGER", "GIT_EDITOR", "GIT_EXEC_PATH", "GIT_TEMPLATE_DIR", "GCONV_PATH",
        "SSH_ASKPASS", "SSH_ASKPASS_REQUIRE", "_JAVA_OPTIONS", "GIT_CONFIG", "GIT_DIR", "GIT_WORK_TREE",
        "GIT_CURL_VERBOSE", "FTP_PROXY", "GOPROXY", "GOFLAGS", "GOINSECURE", "NIX_SSL_CERT_FILE", "AWS_CA_BUNDLE",
        "DENO_CERT", "GRPC_DEFAULT_SSL_ROOTS_FILE_PATH", "PYTHONHTTPSVERIFY", "RUBYLIB", "GEM_PATH", "ZDOTDIR",
        "XDG_CONFIG_HOME", "KUBECONFIG", "DOCKER_HOST",
    ];

    private static readonly string[] reservedPrefixes =
        ["LD_", "DYLD_", "DROVER_", "GIT_CONFIG_", "GIT_TRACE", "NPM_CONFIG_", "PIP_", "CARGO_HTTP_", "YARN_", "BUNDLE_", "GLOBAL_AGENT_"];

    // Names that only resolve inside a private network or a cloud machine. A value must never be sent there.
    private static readonly string[] internalSuffixes =
        [".internal", ".local", ".localhost", ".lan", ".home.arpa", ".localdomain", ".cluster.local", ".intranet", ".corp"];

    #endregion

    #region Properties

    /// <summary>The most characters a value may have.</summary>
    public const int MaxValueCharacters = 16 * 1024;

    public const int MaxNameCharacters = 64;

    public const int MaxDescriptionCharacters = 200;

    /// <summary>The most bots a secret may be given to one by one.</summary>
    public const int MaxBots = 200;

    /// <summary>The most hosts one brokered secret may be sent to.</summary>
    public const int MaxHosts = 20;

    public const int MaxHostCharacters = 253;

    /// <summary>Below this length a value is protected less in output: it is hidden whole and encoded, not in parts.</summary>
    public const int ShortValueCharacters = 16;

    public const string NamePattern = "^[A-Z_][A-Z0-9_]{0,63}$";

    #endregion

    #region Public Methods

    public static string? NameProblem(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "Give the secret a name, such as DEPLOY_TOKEN.";
        }

        if (!ValidName().IsMatch(name))
        {
            return "A name uses capital letters, digits and underscores, starts with a letter or underscore, and has at most 64 characters.";
        }

        return IsReserved(name)
            ? $"{name} is reserved, because it changes how programs start or reach the network. Choose another name."
            : null;
    }

    public static bool IsReserved(string name) =>
        reservedNames.Contains(name) || reservedPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal));

    public static string? ValueProblem(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "Paste the value to save.";
        }

        if (value.Length > MaxValueCharacters)
        {
            return $"A value has at most {MaxValueCharacters} characters.";
        }

        return value.Contains('\0', StringComparison.Ordinal) ? "A value cannot contain a null character." : null;
    }

    /// <summary>A value the broker puts into requests is one line: a line break could start a header of its own.</summary>
    public static string? BrokeredValueProblem(SandboxSecretMode mode, string value) =>
        mode is SandboxSecretMode.Brokered && value.Any(letter => letter is '\r' or '\n')
            ? "A value sent through the broker is one line. Keep a value with line breaks in the sandbox instead."
            : null;

    public static string? DescriptionProblem(string description) =>
        description.Length > MaxDescriptionCharacters
            ? $"A description has at most {MaxDescriptionCharacters} characters."
            : description.Any(char.IsControl) ? "A description is one line of plain text." : null;

    /// <summary>
    /// Checks where a value may go: a brokered value needs at least one host name and a raw one has none, since the
    /// broker never sees it. A project may hold only brokered values, because a raw value in a sandbox reaches every
    /// project's work there.
    /// </summary>
    public static string? BindingProblem(SandboxSecretMode mode, IReadOnlyList<string> hosts, SandboxSecretScope scope)
    {
        if (mode is SandboxSecretMode.Raw)
        {
            if (hosts.Count > 0)
            {
                return "A value kept in the sandbox is not sent through the broker, so it takes no hosts.";
            }

            return scope is SandboxSecretScope.Project
                ? "A project secret must go through the broker, so its value never sits in a sandbox other projects use."
                : null;
        }

        if (hosts.Count == 0)
        {
            return "Add at least one host this value may be sent to, such as api.example.com.";
        }

        if (hosts.Count > MaxHosts)
        {
            return $"A secret may be sent to at most {MaxHosts} hosts.";
        }

        return hosts.Select(HostProblem).FirstOrDefault(problem => problem is not null);
    }

    public static string? HostProblem(string host)
    {
        if (host.Length is 0 or > MaxHostCharacters)
        {
            return "A host is a name such as api.example.com.";
        }

        if (IPAddress.TryParse(host.Trim('[', ']'), out _))
        {
            return $"{host} is an address. Give the host name the service uses, such as api.example.com.";
        }

        if (IsInternal(host))
        {
            return $"{host} is a name for a private network. A value may only be sent to a public host.";
        }

        return ValidHost().IsMatch(host)
            ? null
            : $"{host} is not a host name. Write it as api.example.com, or *.example.com for every name under it, with no scheme, port or path.";
    }

    /// <summary>A host as stored: trimmed, lowercased, with a trailing dot removed.</summary>
    public static string NormalizeHost(string host) => host.Trim().TrimEnd('.').ToLowerInvariant();

    /// <summary>Whether a request's host is one this secret may be sent to. <c>*.example.com</c> covers names under it, not itself.</summary>
    public static bool Covers(IEnumerable<string> hosts, string requestHost)
    {
        var host = NormalizeHost(requestHost);
        return hosts.Any(allowed => allowed.StartsWith("*.", StringComparison.Ordinal)
            ? host.EndsWith(allowed[1..], StringComparison.Ordinal) && host.Length > allowed.Length - 1
            : string.Equals(allowed, host, StringComparison.Ordinal));
    }

    #endregion

    #region Private Methods

    private static bool IsInternal(string host)
    {
        var name = $".{host.TrimStart('*', '.')}";
        return internalSuffixes.Any(suffix => name.EndsWith(suffix, StringComparison.Ordinal));
    }

    [GeneratedRegex(NamePattern, RegexOptions.CultureInvariant)]
    private static partial Regex ValidName();

    // One or more labels of letters, digits and hyphens, not starting or ending with a hyphen, at least two labels,
    // optionally after "*." for every name under a domain.
    [GeneratedRegex(@"^(\*\.)?([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidHost();

    #endregion
}
