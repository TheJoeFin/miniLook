using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using miniLook.Models;
using Windows.System;

namespace miniLook.Views;

public sealed partial class ListDetailsDetailControl : UserControl
{
    public MailData? ListDetailsMenuItem
    {
        get => GetValue(ListDetailsMenuItemProperty) as MailData;
        set => SetValue(ListDetailsMenuItemProperty, value);
    }

    public static readonly DependencyProperty ListDetailsMenuItemProperty = DependencyProperty.Register("ListDetailsMenuItem", typeof(MailData), typeof(ListDetailsDetailControl), new PropertyMetadata(null, OnListDetailsMenuItemPropertyChanged));

    public ListDetailsDetailControl()
    {
        InitializeComponent();
    }

    private static void OnListDetailsMenuItemPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ListDetailsDetailControl control)
        {
            control.ForegroundElement.ChangeView(0, 0, 1);
        }
    }

    private void BrowserLink_Click(object sender, RoutedEventArgs e)
    {
        if (ListDetailsMenuItem is null)
            return;

        // Launch the URI
        _ = Launcher.LaunchUriAsync(new Uri(ListDetailsMenuItem.WebLink));
    }

    private void ArchiveHyperlinkButton_Click(object sender, RoutedEventArgs e)
    {
        // TODO set up archiving a mail
    }
}
