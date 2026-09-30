using System.Text.Json;

namespace GuardWatch.Mcp;

public sealed class GuardWatchSettings
{
    public string BaseUrl { get; init; } = "";
    public int LdapProviderId { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
    public string? Token { get; init; }

    public static GuardWatchSettings Load()
    {
        var file = ReadFile(FindConfigPath());
        return Merge(
            file,
            Environment.GetEnvironmentVariable("GUARDWATCH_BASE_URL"),
            Environment.GetEnvironmentVariable("GUARDWATCH_LDAP_PROVIDER_ID"),
            Environment.GetEnvironmentVariable("GUARDWATCH_LDAP_USERNAME"),
            Environment.GetEnvironmentVariable("GUARDWATCH_LDAP_PASSWORD"),
            Environment.GetEnvironmentVariable("GUARDWATCH_TOKEN"));
    }

    public static GuardWatchSettings Merge(
        GuardWatchSettings file,
        string? baseUrl,
        string? ldapProviderId,
        string? username,
        string? password,
        string? token)
    {
        var rawUrl = First(baseUrl, file.BaseUrl);
        if (rawUrl is null)
            throw new GuardWatchException("Defina a URL do GuardWatch em baseUrl ou na variável GUARDWATCH_BASE_URL.");

        var providerId = file.LdapProviderId;
        if (!string.IsNullOrWhiteSpace(ldapProviderId))
        {
            if (!int.TryParse(ldapProviderId, out providerId) || providerId <= 0)
                throw new GuardWatchException("GUARDWATCH_LDAP_PROVIDER_ID precisa ser um número maior que zero.");
        }

        return new GuardWatchSettings
        {
            BaseUrl = NormalizeBaseUrl(rawUrl),
            LdapProviderId = providerId,
            Username = First(username, file.Username),
            Password = First(password, file.Password),
            Token = First(token, file.Token)
        };
    }

    public static string NormalizeBaseUrl(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new GuardWatchException($"URL base inválida: {url}");
        }

        return $"{uri.Scheme}://{uri.Authority}";
    }

    private static GuardWatchSettings ReadFile(string? path)
    {
        if (path is null || !File.Exists(path))
            return new GuardWatchSettings();

        try
        {
            var json = File.ReadAllText(path);
            var file = JsonSerializer.Deserialize<FileModel>(json, JsonOptions.Web)
                ?? throw new GuardWatchException($"Arquivo de configuração vazio: {path}");
            return new GuardWatchSettings
            {
                BaseUrl = file.BaseUrl?.Trim() ?? "",
                LdapProviderId = file.LdapProviderId is > 0 ? file.LdapProviderId.Value : 0,
                Username = BlankToNull(file.Username),
                Password = BlankToNull(file.Password),
                Token = BlankToNull(file.Token)
            };
        }
        catch (JsonException ex)
        {
            throw new GuardWatchException($"Não foi possível ler {path}: {ex.Message}");
        }
    }

    private static string? FindConfigPath()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("GUARDWATCH_CONFIG"),
            Path.Combine(Directory.GetCurrentDirectory(), "guardwatch.local.json"),
            Path.Combine(AppContext.BaseDirectory, "guardwatch.local.json"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".guardwatch", "mcp.json")
        };

        return candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
    }

    private static string? First(params string?[] values) =>
        values.Select(BlankToNull).FirstOrDefault(value => value is not null);

    private static string? BlankToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class FileModel
    {
        public string? BaseUrl { get; set; }
        public int? LdapProviderId { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? Token { get; set; }
    }
}
