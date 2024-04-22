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

        if ((dto - DateTimeOffset.Now).TotalHours < 3)
            return $"{dto.Humanize()} at {dto:hh:mm}";
        else
            return dto.ToString("dddd hh:mmt");
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
