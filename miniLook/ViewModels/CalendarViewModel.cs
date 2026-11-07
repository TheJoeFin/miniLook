using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Graph;
using Microsoft.Graph.Models;

using miniLook.Contracts.Services;
using miniLook.Core.Helpers;
using miniLook.Models;

namespace miniLook.ViewModels;

public sealed partial class CalendarViewModel : ObservableObject
{
    private static readonly TimeSpan QueryPast = TimeSpan.FromDays(1);
    private static readonly TimeSpan QueryFuture = TimeSpan.FromDays(30);

    private readonly IGraphService _graphService;
    private CancellationTokenSource? _operationCancellation;
    private bool _isShown;
    private string? _editingEventId;
    private CalendarEventItem? _editingEvent;

    public ObservableCollection<CalendarEventItem> Events { get; } = [];

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExistingEvent))]
    private bool isEditing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExistingEvent))]
    [NotifyPropertyChangedFor(nameof(EditorTitle))]
    private bool isNewEvent;

    [ObservableProperty]
    private bool isAllDay;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string errorMessage = string.Empty;

    [ObservableProperty]
    private bool needsSignIn;

    [ObservableProperty]
    private bool hasEvents;

    [ObservableProperty]
    private string eventSubject = string.Empty;

    [ObservableProperty]
    private string eventLocation = string.Empty;

    [ObservableProperty]
    private string eventBody = string.Empty;

    [ObservableProperty]
    private DateTimeOffset startDate = DateTimeOffset.Now;

    [ObservableProperty]
    private DateTimeOffset endDate = DateTimeOffset.Now.AddHours(1);

    [ObservableProperty]
    private TimeSpan? startTime = DateTime.Now.TimeOfDay;

    [ObservableProperty]
    private TimeSpan? endTime = DateTime.Now.AddHours(1).TimeOfDay;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool IsExistingEvent => IsEditing && !IsNewEvent;

    public string EditorTitle => IsNewEvent ? "New event" : "Edit event";

    public CalendarViewModel(IGraphService graphService)
    {
        _graphService = graphService;
    }

    public void OnShown()
    {
        _isShown = true;
        if (!IsEditing)
            _ = LoadAsync();
    }

    public void OnHidden()
    {
        _isShown = false;
        CancelPendingOperation();
    }

    public void OnClosed()
    {
        _isShown = false;
        CancelPendingOperation();
    }

    public void StartNewEvent()
    {
        DateTime start = CalendarEventPolicy.GetDefaultStart(DateTime.Now);
        DateTime end = start.AddHours(1);

        _editingEventId = null;
        _editingEvent = null;
        IsNewEvent = true;
        IsEditing = true;
        IsAllDay = false;
        EventSubject = string.Empty;
        EventLocation = string.Empty;
        EventBody = string.Empty;
        StartDate = start;
        EndDate = end;
        StartTime = start.TimeOfDay;
        EndTime = end.TimeOfDay;
        ErrorMessage = string.Empty;
    }

    [RelayCommand]
    private void EditEvent(CalendarEventItem item)
    {
        _editingEventId = item.Id;
        _editingEvent = item;
        IsNewEvent = false;
        IsEditing = true;
        IsAllDay = item.IsAllDay;
        EventSubject = item.Subject;
        EventLocation = item.Location;
        EventBody = item.Body;
        StartDate = item.Start;
        EndDate = item.End;
        StartTime = item.Start.TimeOfDay;
        EndTime = item.End.TimeOfDay;
        ErrorMessage = string.Empty;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        _editingEventId = null;
        _editingEvent = null;
        IsEditing = false;
        IsNewEvent = false;
        ErrorMessage = string.Empty;
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync();

    [RelayCommand]
    private Task SaveAsync() => SaveEventAsync();

    [RelayCommand]
    private async Task SignInAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            await _graphService.SignInAsync();
            NeedsSignIn = !_graphService.IsAuthenticated;
            if (_graphService.IsAuthenticated)
                await LoadEventsAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Calendar] Sign-in failed: {ex.Message}");
            ErrorMessage = "Sign-in could not be completed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DeleteEventAsync(CalendarEventItem item)
    {
        if (!TryBeginOperation(out CancellationToken cancellationToken))
            return;

        try
        {
            GraphServiceClient client = GetClient();
            if (item.Id == _editingEventId)
                CancelEdit();
            await client.Me.Events[item.Id].DeleteAsync(cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await LoadEventsAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (SignInRequiredException)
        {
            HandleSignInRequired();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Calendar] Could not delete event '{item.Id}': {ex.Message}");
            ErrorMessage = "The event could not be deleted. Try again.";
        }
        finally
        {
            EndOperation();
        }
    }

    public void CancelPendingOperation() => _operationCancellation?.Cancel();

    public CalendarEventItem? GetEditingEvent() => _editingEvent;

    private async Task LoadAsync()
    {
        if (!TryBeginOperation(out CancellationToken cancellationToken))
            return;

        try
        {
            await LoadEventsAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (SignInRequiredException)
        {
            HandleSignInRequired();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Calendar] Could not load events: {ex.Message}");
            ErrorMessage = "The calendar could not be refreshed. Try again.";
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task LoadEventsAsync(CancellationToken cancellationToken)
    {
        if (!_graphService.IsAuthenticated)
        {
            Events.Clear();
            HasEvents = false;
            NeedsSignIn = true;
            ErrorMessage = "Sign in to view and manage your calendar.";
            App.SetUpcomingEvents([]);
            return;
        }

        NeedsSignIn = false;
        ErrorMessage = string.Empty;
        GraphServiceClient client = GetClient();
        DateTimeOffset now = DateTimeOffset.Now;

        EventCollectionResponse? response = await client.Me.CalendarView.GetAsync(config =>
        {
            config.QueryParameters.StartDateTime = (now - QueryPast).UtcDateTime.ToString("o", CultureInfo.InvariantCulture);
            config.QueryParameters.EndDateTime = (now + QueryFuture).UtcDateTime.ToString("o", CultureInfo.InvariantCulture);
            config.QueryParameters.Orderby = ["start/dateTime"];
            config.QueryParameters.Select =
                ["id", "subject", "start", "end", "isAllDay", "location", "body", "bodyPreview", "webLink"];
            config.QueryParameters.Top = 100;
            config.Headers.Add("Prefer", $"outlook.timezone=\"{TimeZoneInfo.Local.Id}\"");
        }, cancellationToken: cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        if (response?.Value is null)
            throw new InvalidDataException("Graph returned no calendar collection.");

        List<Event> graphEvents = [.. response.Value];
        while (!string.IsNullOrWhiteSpace(response.OdataNextLink))
        {
            cancellationToken.ThrowIfCancellationRequested();
            response = await client.Me.CalendarView.WithUrl(response.OdataNextLink).GetAsync(
                config => config.Headers.Add("Prefer", $"outlook.timezone=\"{TimeZoneInfo.Local.Id}\""),
                cancellationToken);
            if (response?.Value is null)
                throw new InvalidDataException("Graph returned an incomplete calendar collection.");
            graphEvents.AddRange(response.Value);
        }

        List<CalendarEventItem> items = graphEvents
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .Select(CreateCalendarItem)
            .ToList();

        Events.Clear();
        foreach (CalendarEventItem item in items)
            Events.Add(item);
        HasEvents = Events.Count > 0;

        App.SetUpcomingEvents(graphEvents);
    }

    private async Task SaveEventAsync()
    {
        if (string.IsNullOrWhiteSpace(EventSubject))
        {
            ErrorMessage = "Enter a subject.";
            return;
        }

        if (!CalendarEventPolicy.TryGetRange(
                StartDate,
                EndDate,
                StartTime,
                EndTime,
                IsAllDay,
                out DateTime start,
                out DateTime end,
                out string validationError))
        {
            ErrorMessage = validationError;
            return;
        }

        if (!TryBeginOperation(out CancellationToken cancellationToken))
            return;

        try
        {
            GraphServiceClient client = GetClient();
            Event request = CreateGraphEvent(start, end);
            if (IsNewEvent)
            {
                await client.Me.Events.PostAsync(request, cancellationToken: cancellationToken);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(_editingEventId))
                    throw new InvalidOperationException("No event is selected for editing.");
                await client.Me.Events[_editingEventId].PatchAsync(request, cancellationToken: cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            CancelEdit();
            await LoadEventsAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (SignInRequiredException)
        {
            HandleSignInRequired();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Calendar] Could not save event: {ex.Message}");
            ErrorMessage = "The event could not be saved. Check the details and try again.";
        }
        finally
        {
            EndOperation();
        }
    }

    private Event CreateGraphEvent(DateTime start, DateTime end)
    {
        string timeZone = TimeZoneInfo.Local.Id;
        return new Event
        {
            Subject = EventSubject.Trim(),
            Start = new DateTimeTimeZone
            {
                DateTime = start.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
                TimeZone = timeZone,
            },
            End = new DateTimeTimeZone
            {
                DateTime = end.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
                TimeZone = timeZone,
            },
            IsAllDay = IsAllDay,
            Location = string.IsNullOrWhiteSpace(EventLocation)
                ? null
                : new Location { DisplayName = EventLocation.Trim() },
            Body = new ItemBody
            {
                ContentType = BodyType.Text,
                Content = EventBody.Trim(),
            },
        };
    }

    private static CalendarEventItem CreateCalendarItem(Event graphEvent)
    {
        DateTime start = ParseGraphDateTime(graphEvent.Start);
        DateTime end = ParseGraphDateTime(graphEvent.End);

        return new CalendarEventItem
        {
            Id = graphEvent.Id!,
            Subject = string.IsNullOrWhiteSpace(graphEvent.Subject) ? "(No subject)" : graphEvent.Subject,
            Start = start,
            End = end,
            IsAllDay = graphEvent.IsAllDay is true,
            Location = graphEvent.Location?.DisplayName ?? string.Empty,
            Body = graphEvent.Body?.Content ?? graphEvent.BodyPreview ?? string.Empty,
            WebLink = string.IsNullOrWhiteSpace(graphEvent.WebLink) ? null : graphEvent.WebLink,
        };
    }

    private static DateTime ParseGraphDateTime(DateTimeTimeZone? value)
    {
        if (value?.DateTime is null
            || !DateTime.TryParse(
                value.DateTime,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out DateTime result))
        {
            return DateTime.MinValue;
        }

        if (string.IsNullOrWhiteSpace(value.TimeZone))
            return result;

        try
        {
            TimeZoneInfo source = TimeZoneInfo.FindSystemTimeZoneById(value.TimeZone);
            return TimeZoneInfo.ConvertTime(result, source, TimeZoneInfo.Local);
        }
        catch (TimeZoneNotFoundException)
        {
            return result;
        }
        catch (InvalidTimeZoneException)
        {
            return result;
        }
    }

    private GraphServiceClient GetClient()
    {
        if (!_graphService.IsAuthenticated || _graphService.Client is null)
            throw new SignInRequiredException(
                new InvalidOperationException("The Graph client is not authenticated."));
        return _graphService.Client;
    }

    private void HandleSignInRequired()
    {
        NeedsSignIn = true;
        ErrorMessage = "Sign in again to manage your calendar.";
    }

    private bool TryBeginOperation(out CancellationToken cancellationToken)
    {
        cancellationToken = CancellationToken.None;
        if (IsBusy || !_isShown)
            return false;

        _operationCancellation?.Dispose();
        _operationCancellation = new CancellationTokenSource();
        cancellationToken = _operationCancellation.Token;
        IsBusy = true;
        ErrorMessage = string.Empty;
        return true;
    }

    private void EndOperation()
    {
        IsBusy = false;
        _operationCancellation?.Dispose();
        _operationCancellation = null;
    }

    partial void OnIsAllDayChanged(bool value)
    {
        if (!value)
            return;

        StartTime = TimeSpan.Zero;
        EndTime = TimeSpan.Zero;
        if (EndDate.Date <= StartDate.Date)
            EndDate = StartDate.AddDays(1);
    }
}
