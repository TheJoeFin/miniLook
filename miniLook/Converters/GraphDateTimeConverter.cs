using Humanizer;
using Microsoft.Graph;
using Microsoft.UI.Xaml.Data;

namespace miniLook.Converters;
internal class GraphDateTimeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string valString)
            return string.Empty;

        DateTimeOffset dto = DateTimeOffset.Parse(valString);
        dto = dto.ToLocalTime();
        return dto.Humanize();
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
