using CommunityToolkit.Authentication;
using CommunityToolkit.Authentication.Extensions;
using Microsoft.Identity.Client.Extensions.Msal;
using Microsoft.Windows.ApplicationModel.Resources;
using miniLook.Contracts.Services;
using Windows.Storage;

namespace miniLook.Services;
public class GraphService : IGraphService
{
    public GraphService()
    {

    }

    public bool IsAuthenticated { get; set; } = false;

    public async Task InitializeAsync()
    {
        ResourceManager resourceManager = new();
        ResourceMap resourceMap = resourceManager.MainResourceMap.GetSubtree("OAuth");
        ResourceContext resourceContext = resourceManager.CreateResourceContext();
        string clientId = resourceMap.GetValue("ClientId", resourceContext).ValueAsString;

        if (string.IsNullOrEmpty(clientId))
            throw new Exception("Client ID not set");

        string[] scopes = ["User.Read", "Mail.ReadWrite", "offline_access", "Calendars.Read", "MailboxSettings.Read"];

        MsalProvider provider = new(clientId, scopes, null, false, true);


        string cacheFileName = "msal_cache.dat";
        string cacheDirectory = ApplicationData.Current.LocalCacheFolder.Path;
        // Configure the token cache storage for non-UWP applications.
        // https://github.com/AzureAD/microsoft-authentication-extensions-for-dotnet/wiki/Cross-platform-Token-Cache
        // https://github.com/Richasy/Graph-Controls/tree/main/Samples/ManualGraphRequestSample
        StorageCreationProperties storageProperties = new StorageCreationPropertiesBuilder(
            cacheFileName, cacheDirectory).Build();
        await provider.InitTokenCacheAsync(storageProperties);

        ProviderManager.Instance.GlobalProvider = provider;
        ProviderManager.Instance.GlobalProvider.StateChanged += (s, e) =>
        {
            if (e.NewState == ProviderState.SignedIn)
                IsAuthenticated = true;
            else
                IsAuthenticated = false;
        };

        _ = await provider.TrySilentSignInAsync();
    }

    public async Task SignInAsync()
    {
        IProvider provider = ProviderManager.Instance.GlobalProvider;
        await provider.SignInAsync();
    }
}
