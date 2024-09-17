using System.Reflection;
using System.Windows.Input;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.UI.Xaml;

using miniLook.Contracts.Services;
using miniLook.Helpers;

using Windows.ApplicationModel;

namespace miniLook.ViewModels;

public partial class SettingsViewModel : ObservableRecipient
{
    private readonly IThemeSelectorService _themeSelectorService;

    [ObservableProperty]
    private ElementTheme _elementTheme;

    [ObservableProperty]
    private string _versionDescription;

    [ObservableProperty]
    private bool canClearCache = true;

    public ICommand SwitchThemeCommand { get; }

    public INavigationService NavigationService { get; }

    public IMailCacheService MailCacheService { get; }

    public SettingsViewModel(IThemeSelectorService themeSelectorService, INavigationService navigationService, IMailCacheService mailCacheService)
    {
        _themeSelectorService = themeSelectorService;
        _elementTheme = _themeSelectorService.Theme;
        _versionDescription = GetVersionDescription();

        SwitchThemeCommand = new RelayCommand<ElementTheme>(
            async (param) =>
            {
                if (ElementTheme != param)
                {
                    ElementTheme = param;
                    await _themeSelectorService.SetThemeAsync(param);
                }
            });

        NavigationService = navigationService;
        MailCacheService = mailCacheService;
    }

    [RelayCommand]
    private void GoBack()
    {
        if (NavigationService.CanGoBack)
            NavigationService.GoBack();
    }

    [RelayCommand]
    private async Task ClearCachedData()
    {
        await MailCacheService.ClearMailCacheAsync();
        await MailCacheService.SaveDeltaLink(null);
        CanClearCache = false;
    }

    private static string GetVersionDescription()
    {
        Version version;

        if (RuntimeHelper.IsMSIX)
        {
            PackageVersion packageVersion = Package.Current.Id.Version;
            version = new(packageVersion.Major, packageVersion.Minor, packageVersion.Build, packageVersion.Revision);
        }
        else
        {
            version = Assembly.GetExecutingAssembly().GetName().Version!;
        }

        return $"{"AppDisplayName".GetLocalized()} - {version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
    }
}
