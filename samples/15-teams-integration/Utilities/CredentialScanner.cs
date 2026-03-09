using System.Text.RegularExpressions;

namespace McpProxy.Samples.TeamsIntegration.Utilities;

/// <summary>
/// Result of a credential scan.
/// </summary>
public sealed record CredentialScanResult
{
    /// <summary>
    /// Whether any credentials were detected.
    /// </summary>
    public required bool HasCredentials { get; init; }

    /// <summary>
    /// List of credential types detected.
    /// </summary>
    public required IReadOnlyList<string> DetectedTypes { get; init; }

    /// <summary>
    /// Human-readable summary of what was detected.
    /// </summary>
    public string? Summary { get; init; }
}

/// <summary>
/// Scans text for credentials and sensitive data that should not be sent in Teams messages.
/// </summary>
public static partial class CredentialScanner
{
    /// <summary>
    /// Scans text for credentials and returns the result.
    /// </summary>
    /// <param name="text">The text to scan.</param>
    /// <returns>The scan result indicating what was found.</returns>
    public static CredentialScanResult Scan(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new CredentialScanResult
            {
                HasCredentials = false,
                DetectedTypes = []
            };
        }

        var detectedTypes = new List<string>();

        // Check each pattern
        if (ApiKeyPattern().IsMatch(text))
        {
            detectedTypes.Add("API key");
        }

        if (BearerTokenPattern().IsMatch(text))
        {
            detectedTypes.Add("Bearer token");
        }

        if (PrivateKeyPattern().IsMatch(text))
        {
            detectedTypes.Add("Private key");
        }

        if (ConnectionStringPattern().IsMatch(text))
        {
            detectedTypes.Add("Connection string");
        }

        if (AwsKeyPattern().IsMatch(text))
        {
            detectedTypes.Add("AWS access key");
        }

        if (AzureStorageKeyPattern().IsMatch(text))
        {
            detectedTypes.Add("Azure storage key");
        }

        if (GitHubTokenPattern().IsMatch(text))
        {
            detectedTypes.Add("GitHub token");
        }

        if (JwtTokenPattern().IsMatch(text))
        {
            detectedTypes.Add("JWT token");
        }

        if (PasswordPattern().IsMatch(text))
        {
            detectedTypes.Add("Password");
        }

        if (SasTokenPattern().IsMatch(text))
        {
            detectedTypes.Add("SAS token");
        }

        string? summary = null;
        if (detectedTypes.Count > 0)
        {
            summary = detectedTypes.Count == 1
                ? $"Detected: {detectedTypes[0]}"
                : $"Detected {detectedTypes.Count} credential types: {string.Join(", ", detectedTypes)}";
        }

        return new CredentialScanResult
        {
            HasCredentials = detectedTypes.Count > 0,
            DetectedTypes = detectedTypes,
            Summary = summary
        };
    }

    /// <summary>
    /// Checks if text contains any credentials.
    /// </summary>
    /// <param name="text">The text to check.</param>
    /// <returns>True if credentials are detected.</returns>
    public static bool ContainsCredentials(string? text) => Scan(text).HasCredentials;

    // ═══════════════════════════════════════════════════════════════════════
    // Compiled Regex Patterns (Source Generated)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Matches API keys in various formats.
    /// Examples: api_key: "abc123...", apikey=xyz789..., access_token: 'def456...'
    /// </summary>
    [GeneratedRegex(
        @"(?:api[_-]?key|apikey|access[_-]?token|secret[_-]?key)[:\s=]*['""]?([A-Za-z0-9_\-]{20,})['""]?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ApiKeyPattern();

    /// <summary>
    /// Matches Bearer tokens (typically JWTs).
    /// Example: Bearer eyJhbGciOiJIUzI1...
    /// </summary>
    [GeneratedRegex(
        @"Bearer\s+[A-Za-z0-9\-_]+\.[A-Za-z0-9\-_]+\.[A-Za-z0-9\-_]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex BearerTokenPattern();

    /// <summary>
    /// Matches the beginning of PEM-encoded private keys.
    /// </summary>
    [GeneratedRegex(
        @"-----BEGIN (?:RSA |EC |DSA |OPENSSH )?PRIVATE KEY-----",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex PrivateKeyPattern();

    /// <summary>
    /// Matches database connection strings with passwords.
    /// </summary>
    [GeneratedRegex(
        @"(?:Server|Data Source|Host|Provider)=[^;]+;.*(?:Password|Pwd|Secret)=[^;]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ConnectionStringPattern();

    /// <summary>
    /// Matches AWS access key IDs.
    /// Format: AKIA followed by 16 alphanumeric characters.
    /// </summary>
    [GeneratedRegex(
        @"AKIA[0-9A-Z]{16}",
        RegexOptions.Compiled)]
    private static partial Regex AwsKeyPattern();

    /// <summary>
    /// Matches Azure Storage account keys (Base64, typically 88 chars).
    /// </summary>
    [GeneratedRegex(
        @"(?:AccountKey|SharedAccessKey)[=:]\s*[A-Za-z0-9+/=]{40,}",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex AzureStorageKeyPattern();

    /// <summary>
    /// Matches GitHub personal access tokens and fine-grained tokens.
    /// </summary>
    [GeneratedRegex(
        @"(?:ghp_|gho_|ghu_|ghs_|ghr_|github_pat_)[A-Za-z0-9_]{36,}",
        RegexOptions.Compiled)]
    private static partial Regex GitHubTokenPattern();

    /// <summary>
    /// Matches JWT tokens (three Base64url segments separated by dots).
    /// Must be at least 100 chars to avoid false positives.
    /// </summary>
    [GeneratedRegex(
        @"eyJ[A-Za-z0-9_-]{50,}\.eyJ[A-Za-z0-9_-]{50,}\.[A-Za-z0-9_-]{40,}",
        RegexOptions.Compiled)]
    private static partial Regex JwtTokenPattern();

    /// <summary>
    /// Matches password assignments in various formats.
    /// </summary>
    [GeneratedRegex(
        @"(?:password|passwd|pwd)[:\s=]+['""]?([^\s'""]{8,})['""]?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex PasswordPattern();

    /// <summary>
    /// Matches Azure SAS tokens.
    /// </summary>
    [GeneratedRegex(
        @"[?&](?:sv|sig|se|sp)=[^&\s]+(?:&(?:sv|sig|se|sp|sr|spr|st)=[^&\s]+){2,}",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SasTokenPattern();
}
