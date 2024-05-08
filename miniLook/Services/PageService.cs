using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.UI.Xaml.Controls;

using miniLook.Contracts.Services;
using miniLook.ViewModels;
using miniLook.Views;

namespace miniLook.Services;

public class PageService : IPageService
{
    private readonly Dictionary<string, Type> _pages = [];

    public PageService()
    {
        Configure<ShellViewModel, ShellPage>();
        Configure<ListDetailsViewModel, ListDetailsPage>();
        Configure<SettingsViewModel, SettingsPage>();
        Configure<WelcomeViewModel, WelcomePage>();
    }

    public Type GetPageType(string key)
    {
        Type? pageType;
        lock (_pages)
        {
            if (!_pages.TryGetValue(key, out pageType))
                throw new ArgumentException($"Page not found: {key}. Did you forget to call PageService.Configure?");
        }

        return pageType;
    }

    private void Configure<VM, V>()
        where VM : ObservableObject
        where V : Page
    {
        lock (_pages)
        {
            string key = typeof(VM).FullName!;
            if (_pages.ContainsKey(key))
                throw new ArgumentException($"The key {key} is already configured in PageService");

            Type type = typeof(V);
            if (_pages.ContainsValue(type))
                throw new ArgumentException($"This type is already configured with key {_pages.First(p => p.Value == type).Key}");

            _pages.Add(key, type);
        }
    }
}
