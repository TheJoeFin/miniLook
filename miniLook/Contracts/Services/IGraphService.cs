namespace miniLook.Contracts.Services;
public interface IGraphService
{
    public bool IsAuthenticated { get; set; }

    Task SignInAsync();

    Task InitializeAsync();
}
