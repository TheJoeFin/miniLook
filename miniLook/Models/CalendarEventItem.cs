using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace miniLook.Models;

public sealed class CalendarEventItem : INotifyPropertyChanged
{
    private string _subject = string.Empty;
    private DateTime _start;
    private DateTime _end;
    private bool _isAllDay;
    private string _location = string.Empty;
    private string _body = string.Empty;
    private string? _webLink;
    private string? _meetingLink;

    public event PropertyChangedEventHandler? PropertyChanged;

    public required string Id { get; init; }

    public required string Subject
    {
        get => _subject;
        set
        {
            if (SetProperty(ref _subject, value))
                OnPropertyChanged(nameof(AccessibilityLabel));
        }
    }

    public required DateTime Start
    {
        get => _start;
        set
        {
            if (!SetProperty(ref _start, value))
                return;
            OnPropertyChanged(nameof(DisplayDate));
            OnPropertyChanged(nameof(DisplayTime));
            OnPropertyChanged(nameof(AccessibilityLabel));
        }
    }

    public required DateTime End
    {
        get => _end;
        set
        {
            if (!SetProperty(ref _end, value))
                return;
            OnPropertyChanged(nameof(DisplayDate));
            OnPropertyChanged(nameof(DisplayTime));
            OnPropertyChanged(nameof(AccessibilityLabel));
        }
    }

    public bool IsAllDay
    {
        get => _isAllDay;
        set
        {
            if (!SetProperty(ref _isAllDay, value))
                return;
            OnPropertyChanged(nameof(DisplayDate));
            OnPropertyChanged(nameof(DisplayTime));
            OnPropertyChanged(nameof(AccessibilityLabel));
        }
    }

    public string Location
    {
        get => _location;
        set
        {
            if (!SetProperty(ref _location, value))
                return;
            OnPropertyChanged(nameof(HasLocation));
            OnPropertyChanged(nameof(AccessibilityLabel));
        }
    }

    public string Body
    {
        get => _body;
        set => SetProperty(ref _body, value);
    }

    public string? WebLink
    {
        get => _webLink;
        set => SetProperty(ref _webLink, value);
    }

    public string? MeetingLink
    {
        get => _meetingLink;
        set
        {
            if (SetProperty(ref _meetingLink, value))
                OnPropertyChanged(nameof(HasMeetingLink));
        }
    }

    [JsonIgnore]
    public string DisplayDate => IsAllDay
        ? $"{Start:ddd, MMM d} - all day"
        : Start.Date == End.Date
            ? $"{Start:ddd, MMM d}"
            : $"{Start:ddd, MMM d} - {End:ddd, MMM d}";

    [JsonIgnore]
    public string DisplayTime => IsAllDay
        ? "All day"
        : Start.Date == End.Date
            ? $"{Start:t} - {End:t}"
            : $"{Start:t} - {End:ddd, MMM d t}";

    [JsonIgnore]
    public bool HasLocation => !string.IsNullOrWhiteSpace(Location);

    [JsonIgnore]
    public bool HasMeetingLink => !string.IsNullOrWhiteSpace(MeetingLink);

    [JsonIgnore]
    public string AccessibilityLabel => HasLocation
        ? $"{Subject}, {DisplayDate}, {DisplayTime}, {Location}"
        : $"{Subject}, {DisplayDate}, {DisplayTime}";

    public void UpdateFrom(CalendarEventItem updated)
    {
        if (!string.Equals(Id, updated.Id, StringComparison.Ordinal))
            throw new ArgumentException("Cannot update an event from a different event ID.", nameof(updated));

        Subject = updated.Subject;
        Start = updated.Start;
        End = updated.End;
        IsAllDay = updated.IsAllDay;
        Location = updated.Location;
        Body = updated.Body;
        WebLink = updated.WebLink;
        MeetingLink = updated.MeetingLink;
    }

    private bool SetProperty<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
