using Humanizer;
using Microsoft.Graph;
using Microsoft.UI.Xaml.Data;

namespace miniLook.Converters;
internal class GraphDateTimeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not Event graphEvent)
            return string.Empty;

        DateTimeOffset dtoStart = DateTimeOffset.Parse(graphEvent.Start.DateTime.ToString());
        dtoStart = dtoStart.ToLocalTime();

        DateTimeOffset dtoEnd = DateTimeOffset.Parse(graphEvent.End.DateTime.ToString());
        dtoEnd = dtoEnd.ToLocalTime();

        DateTimeOffset now = DateTimeOffset.Now.LocalDateTime;


        if (now < dtoEnd && now > dtoStart)
        {
            if (graphEvent.IsAllDay is true)
                return "All day";

            return "Now";
        }

        if ((dtoStart - DateTimeOffset.Now).TotalHours < 3)
            return $"{dtoStart.Humanize()} at {dtoStart:hh:mm}";
        else if (dtoStart.Date == now.Date)
            return $"Today at {dtoStart:hh:mmt}";
        else if (dtoStart.Date == now.Date.AddDays(1))
            return $"Tomorrow at {dtoStart:hh:mmt}";
        else
            return dtoStart.ToString("dddd hh:mmt");
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
