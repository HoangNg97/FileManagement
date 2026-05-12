using System.IO;
using System.Linq;
using System.Reflection;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Util.Store;

namespace LauncherCatalog.Services;

public sealed class GeminiAuthService
{
    private const string EmbeddedClientSecretsResourceName = "LauncherCatalog.client_secret.json";

    private static readonly string[] Scopes =
    [
        "https://www.googleapis.com/auth/cloud-platform",
        "https://www.googleapis.com/auth/generative-language.retriever"
    ];

    private readonly string _tokenStoreDirectory;

    public GeminiAuthService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _tokenStoreDirectory = Path.Combine(appData, "LauncherCatalog", "Auth", "GeminiOAuth");
    }

    public bool HasCachedCredentials()
    {
        return Directory.Exists(_tokenStoreDirectory)
            && Directory.EnumerateFiles(_tokenStoreDirectory, "*", SearchOption.AllDirectories).Any();
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await using var stream = OpenEmbeddedClientSecretsStream();
        if (stream is null)
        {
            throw new FileNotFoundException(
                $"Could not load embedded client_secret.json resource \"{EmbeddedClientSecretsResourceName}\".");
        }

        Directory.CreateDirectory(_tokenStoreDirectory);
        var clientSecrets = GoogleClientSecrets.FromStream(stream).Secrets;

        await GoogleWebAuthorizationBroker.AuthorizeAsync(
            clientSecrets,
            Scopes,
            "launcher-catalog-user",
            cancellationToken,
            new FileDataStore(_tokenStoreDirectory, fullPath: true));
    }

    private static Stream? OpenEmbeddedClientSecretsStream()
    {
        return Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(EmbeddedClientSecretsResourceName);
    }
}
