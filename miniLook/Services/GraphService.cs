using Microsoft.Graph;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Broker;
using Microsoft.Identity.Client.Extensions.Msal;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Windows.ApplicationModel.Resources;
using miniLook.Contracts.Services;
using System.Diagnostics;
using Windows.Storage;

namespace miniLook.Services;

public class GraphService : IGraphService
{
    private static readonly string[] Scopes =
        ["User.Read", "People.Read", "Mail.Send", "Mail.ReadWrite", "offline_access", "Calendars.ReadWrite", "MailboxSettings.Read"];

    private IPublicClientApplication? _pca;
    private IPublicClientApplication? _pcaFallback;
    private IPublicClientApplication? _activePca;
    private string _clientId = string.Empty;
    private bool _wamAvailable = true;

    public bool IsAuthenticated { get; private set; }

    public GraphServiceClient? Client { get; private set; }

    public event EventHandler<bool>? AuthenticationStateChanged;

    public async Task InitializeAsync()
    {
        ResourceManager resourceManager = new();
        ResourceMap resourceMap = resourceManager.MainResourceMap.GetSubtree("OAuth");
        ResourceContext resourceContext = resourceManager.CreateResourceContext();
        _clientId = resourceMap.GetValue("ClientId", resourceContext).ValueAsString;

        if (string.IsNullOrEmpty(_clientId))
            throw new Exception("Client ID not set");

        string cacheFileName = "msal_cache.dat";
        string cacheDirectory = ApplicationData.Current.LocalCacheFolder.Path;
        StorageCreationProperties storageProperties = new StorageCreationPropertiesBuilder(
            cacheFileName, cacheDirectory).Build();
        MsalCacheHelper cacheHelper = await MsalCacheHelper.CreateAsync(storageProperties);

        // Primary: WAM broker for Windows-native SSO
        _pca = PublicClientApplicationBuilder
            .Create(_clientId)
            .WithAuthority(AadAuthorityAudience.AzureAdAndPersonalMicrosoftAccount)
            .WithDefaultRedirectUri()
            .WithBroker(new BrokerOptions(BrokerOptions.OperatingSystems.Windows))
            .WithParentActivityOrWindow(() => WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow))
            .Build();
        cacheHelper.RegisterCache(_pca.UserTokenCache);

        // Fallback: system browser (works when WAM fails or app reg lacks broker redirect)
        _pcaFallback = PublicClientApplicationBuilder
            .Create(_clientId)
            .WithAuthority(AadAuthorityAudience.AzureAdAndPersonalMicrosoftAccount)
            .WithRedirectUri("http://localhost")
            .Build();
        cacheHelper.RegisterCache(_pcaFallback.UserTokenCache);

        _activePca = _pca;

        await TrySilentSignInAsync();
    }

    public async Task SignInAsync()
    {
        if (_pcaFallback is null)
            return;

        if (_wamAvailable && _pca is not null)
        {
            Debug.WriteLine("[Auth] Attempting WAM interactive sign-in...");
            try
            {
                await _pca
                    .AcquireTokenInteractive(Scopes)
                    .ExecuteAsync();

                _activePca = _pca;
                Debug.WriteLine("[Auth] WAM interactive sign-in succeeded.");
                SetSignedIn();
                return;
            }
            catch (MsalServiceException ex)
            {
                // WAM not usable — remember so we skip it next time
                _wamAvailable = false;
                Debug.WriteLine($"[Auth] WAM interactive failed (MsalServiceException), marking unavailable. Code={ex.ErrorCode} Msg={ex.Message}");
            }
            catch (MsalClientException ex)
            {
                // User cancelled WAM prompt
                Debug.WriteLine($"[Auth] WAM cancelled by user. Code={ex.ErrorCode} Msg={ex.Message}");
                return;
            }
        }
        else
        {
            Debug.WriteLine($"[Auth] Skipping WAM (wamAvailable={_wamAvailable}, pca={(_pca is null ? "null" : "set")})");
        }

        Debug.WriteLine("[Auth] Attempting system browser interactive sign-in...");
        try
        {
            await _pcaFallback
                .AcquireTokenInteractive(Scopes)
                .WithUseEmbeddedWebView(false)
                .ExecuteAsync();

            _activePca = _pcaFallback;
            Debug.WriteLine("[Auth] Browser interactive sign-in succeeded.");
            SetSignedIn();
        }
        catch (MsalClientException ex)
        {
            Debug.WriteLine($"[Auth] Browser sign-in cancelled. Code={ex.ErrorCode} Msg={ex.Message}");
        }
    }

    public async Task SignOutAsync()
    {
        if (_activePca is null)
            return;

        IEnumerable<IAccount> accounts = await _activePca.GetAccountsAsync();
        foreach (IAccount account in accounts)
            await _activePca.RemoveAsync(account);

        Client = null;
        IsAuthenticated = false;
        AuthenticationStateChanged?.Invoke(this, false);
    }

    private async Task TrySilentSignInAsync()
    {
        Debug.WriteLine("[Auth] Attempting silent sign-in (WAM)...");
        if (await TrySilentOnAsync(_pca))
            return;

        Debug.WriteLine("[Auth] Attempting silent sign-in (browser fallback)...");
        if (await TrySilentOnAsync(_pcaFallback))
            return;

        Debug.WriteLine("[Auth] No cached tokens found — interactive sign-in required.");
    }

    private async Task<bool> TrySilentOnAsync(IPublicClientApplication? pca)
    {
        if (pca is null)
            return false;

        IEnumerable<IAccount> accounts = await pca.GetAccountsAsync();
        IAccount? account = accounts.FirstOrDefault();
        string pcaLabel = pca == _pca ? "WAM" : "browser";

        if (account is null)
        {
            Debug.WriteLine($"[Auth] Silent ({pcaLabel}): no cached account.");
            return false;
        }

        Debug.WriteLine($"[Auth] Silent ({pcaLabel}): found account {account.Username}, acquiring token...");
        try
        {
            await pca
                .AcquireTokenSilent(Scopes, account)
                .ExecuteAsync();

            _activePca = pca;
            Debug.WriteLine($"[Auth] Silent ({pcaLabel}): succeeded.");
            SetSignedIn();
            return true;
        }
        catch (MsalUiRequiredException ex)
        {
            Debug.WriteLine($"[Auth] Silent ({pcaLabel}): UI required. Code={ex.ErrorCode} Msg={ex.Message}");
            return false;
        }
        catch (MsalServiceException ex)
        {
            // WAM broker not usable — skip it for interactive auth too
            if (pca == _pca)
                _wamAvailable = false;
            Debug.WriteLine($"[Auth] Silent ({pcaLabel}): MsalServiceException, marking WAM unavailable. Code={ex.ErrorCode} Msg={ex.Message}");
            return false;
        }
    }

    private void SetSignedIn()
    {
        if (_activePca is null)
            return;

        Client = new GraphServiceClient(
            new BaseBearerTokenAuthenticationProvider(
                new MsalTokenProvider(_activePca, Scopes)));

        IsAuthenticated = true;
        AuthenticationStateChanged?.Invoke(this, true);
    }

    /// <summary>
    /// Bridges MSAL token acquisition into the Kiota IAccessTokenProvider expected by GraphServiceClient.
    /// </summary>
    private sealed class MsalTokenProvider(IPublicClientApplication pca, string[] scopes) : IAccessTokenProvider
    {
        public AllowedHostsValidator AllowedHostsValidator { get; } = new();

        public async Task<string> GetAuthorizationTokenAsync(
            Uri uri,
            Dictionary<string, object>? additionalAuthenticationContext = null,
            CancellationToken cancellationToken = default)
        {
            IEnumerable<IAccount> accounts = await pca.GetAccountsAsync();
            AuthenticationResult result;

            try
            {
                result = await pca
                    .AcquireTokenSilent(scopes, accounts.FirstOrDefault())
                    .ExecuteAsync(cancellationToken);
            }
            catch (MsalUiRequiredException)
            {
                result = await pca
                    .AcquireTokenInteractive(scopes)
                    .ExecuteAsync(cancellationToken);
            }

            return result.AccessToken;
        }
    }
}
