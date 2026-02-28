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
    private readonly ILocalSettingsService _localSettingsService;

    internal const string AlwaysRenderHtmlSettingsKey = "AlwaysRenderHtml";

    [ObservableProperty]
    private ElementTheme _elementTheme;

    [ObservableProperty]
    private string _versionDescription;

    [ObservableProperty]
    private bool canClearCache = true;

    [ObservableProperty]
    private bool _alwaysRenderHtml;

    public ICommand SwitchThemeCommand { get; }

    public INavigationService NavigationService { get; }

    public IMailCacheService MailCacheService { get; }

    public SettingsViewModel(IThemeSelectorService themeSelectorService, INavigationService navigationService, IMailCacheService mailCacheService, ILocalSettingsService localSettingsService)
    {
        _themeSelectorService = themeSelectorService;
        _localSettingsService = localSettingsService;
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

        InitializeSettingsAsync();
    }

    private async void InitializeSettingsAsync()
    {
        bool value = await _localSettingsService.ReadSettingAsync<bool>(AlwaysRenderHtmlSettingsKey);
        _alwaysRenderHtml = value;
        OnPropertyChanged(nameof(AlwaysRenderHtml));
    }

    partial void OnAlwaysRenderHtmlChanged(bool value)
    {
        _ = _localSettingsService.SaveSettingAsync(AlwaysRenderHtmlSettingsKey, value);
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
        ListDetailsViewModel listDetailsViewModel = App.GetService<ListDetailsViewModel>();
        await listDetailsViewModel.ClearOutContents();
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
