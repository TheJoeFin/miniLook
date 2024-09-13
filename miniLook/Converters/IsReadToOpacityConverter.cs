using Microsoft.UI.Xaml.Data;

namespace miniLook.Converters;
internal class IsReadToOpacityConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool isRead)
            return isRead ? 0.7 : 1;

        return 1;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
