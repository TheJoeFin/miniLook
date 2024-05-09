

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace miniLook.Helpers;
public static class VisualTreeHelperExtensions
{
    public static T? FindParentOfType<T>(this DependencyObject child) where T : DependencyObject
    {
        DependencyObject parentObject = VisualTreeHelper.GetParent(child);

        if (parentObject == null)
            return null;

        if (parentObject is T parent)
            return parent;

        return FindParentOfType<T>(parentObject);
    }
}
