using Microsoft.UI.Xaml.Data;

namespace miniLook.Converters;
internal class StringNavigateUriConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string uriString)
            return new Uri(uriString);

        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
