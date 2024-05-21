using Microsoft.UI.Xaml.Data;

namespace miniLook.Converters;
internal class StringNavigateUriConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string uriString && Uri.TryCreate(uriString, UriKind.Absolute, out Uri? newUri))
            return newUri;

        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
