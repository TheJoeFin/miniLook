using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using miniLook.Contracts.Services;
using miniLook.Helpers;
using System.Diagnostics;
using Windows.System;

namespace miniLook.ViewModels;

public partial class GraphExplainerViewModel : ObservableRecipient
{
    INavigationService NavigationService { get; }
    private readonly IGraphService _graphService;

    private string _signInError = string.Empty;
    public string SignInError
    {
        get => _signInError;
        set
        {
            if (SetProperty(ref _signInError, value))
                OnPropertyChanged(nameof(HasSignInError));
        }
    }

    public bool HasSignInError => !string.IsNullOrEmpty(SignInError);


    public GraphExplainerViewModel(INavigationService navigationService, IGraphService graphService)
    {
        NavigationService = navigationService;
        _graphService = graphService;
    }

    [RelayCommand]
    public async Task GetSignedIn()
    {
        SignInError = string.Empty;
        try
        {
            await _graphService.SignInAsync();
            if (_graphService.IsAuthenticated)
                NavigationService.NavigateTo(typeof(ListDetailsViewModel).FullName!);
            else
                SignInError = "SignIn_Error".GetLocalized();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Auth] Interactive sign-in failed: {ex.Message}");
            SignInError = "SignIn_Error".GetLocalized();
        }
    }

    [RelayCommand]
    public async Task EmailMe()
    {
        _ = await Launcher.LaunchUriAsync(new Uri(string.Format("mailto:joe@joefinapps.com")));
    }
}