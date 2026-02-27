using Microsoft.Graph;

namespace miniLook.Contracts.Services;
public interface IGraphService
{
    bool IsAuthenticated { get; }

    GraphServiceClient? Client { get; }

    event EventHandler<bool>? AuthenticationStateChanged;

    Task InitializeAsync();

    Task SignInAsync();

    Task SignOutAsync();
}
