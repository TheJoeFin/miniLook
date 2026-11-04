using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;

using Windows.UI;

namespace miniLook.Controls;

public sealed partial class ShellBackdrop : Microsoft.UI.Xaml.Media.SystemBackdrop
{
    private readonly SystemBackdropConfiguration _configuration = new() { IsInputActive = true };
    private DesktopAcrylicController? _controller;
    private Color? _accentTint;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        _controller = new DesktopAcrylicController();
        ApplyTint();
        _controller.SetSystemBackdropConfiguration(_configuration);
        _controller.AddSystemBackdropTarget(connectedTarget);
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);
        _controller?.RemoveSystemBackdropTarget(disconnectedTarget);
        _controller?.Dispose();
        _controller = null;
    }

    public void Update(bool isLight, Color? accentTint)
    {
        _configuration.Theme = isLight ? SystemBackdropTheme.Light : SystemBackdropTheme.Dark;
        _accentTint = accentTint;
        ApplyTint();
    }

    private void ApplyTint()
    {
        if (_controller is null)
            return;

        if (_accentTint is Color tint)
        {
            _controller.TintColor = tint;
            _controller.FallbackColor = tint;
            _controller.TintOpacity = 0.7f;
            _controller.LuminosityOpacity = 0.9f;
        }
        else
        {
            _controller.ResetProperties();
        }
    }
}