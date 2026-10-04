using ItchDB.Infrastructure.Settings;

namespace ItchDB.Web.Services;

/// <summary>Global filters from the app bar, one instance per browser circuit.</summary>
public sealed class UiState
{
    private readonly SettingsService _settings;

    public UiState(SettingsService settings)
    {
        _settings = settings;
        var s = settings.Current;
        _showNsfw = s.DefaultShowNsfw;
        _minRatings = Math.Max(1, s.DefaultMinRatings);
        _includeUnrated = s.DefaultIncludeUnrated;
    }

    public static readonly int[] MinRatingOptions = [1, 2, 4, 5, 10, 25, 50, 100];

    private bool _showNsfw;
    private int _minRatings;
    private bool _includeUnrated;

    public event Action? Changed;

    public bool ShowNsfw
    {
        get => _showNsfw;
        set
        {
            if (_showNsfw == value) return;
            _showNsfw = value;
            _ = PersistAsync();
            Changed?.Invoke();
        }
    }

    public int MinRatings
    {
        get => _minRatings;
        set
        {
            value = Math.Max(1, value);
            if (_minRatings == value) return;
            _minRatings = value;
            _ = PersistAsync();
            Changed?.Invoke();
        }
    }

    /// <summary>When set, success and competition include games with zero ratings via the list counter.</summary>
    public bool IncludeUnrated
    {
        get => _includeUnrated;
        set
        {
            if (_includeUnrated == value) return;
            _includeUnrated = value;
            _ = PersistAsync();
            Changed?.Invoke();
        }
    }

    private async Task PersistAsync()
    {
        var model = _settings.Clone();
        model.DefaultShowNsfw = _showNsfw;
        model.DefaultMinRatings = _minRatings;
        model.DefaultIncludeUnrated = _includeUnrated;
        await _settings.SaveAsync(model);
    }
}
