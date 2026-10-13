using ItchDB.Core;
using ItchDB.Infrastructure.Analytics;
using ItchDB.Infrastructure.Settings;

namespace ItchDB.Web.Services;

/// <summary>Global filters from the app bar, one instance per browser circuit.</summary>
public sealed class UiState
{
    private readonly SettingsService _settings;
    private readonly AnalyticsService _analytics;

    public UiState(SettingsService settings, AnalyticsService analytics)
    {
        _settings = settings;
        _analytics = analytics;
        var s = settings.Current;
        _showNsfw = s.DefaultShowNsfw;
        _minRatings = Math.Max(1, s.DefaultMinRatings);
        _includeUnrated = s.DefaultIncludeUnrated;
        _smallGameThreshold = Math.Clamp(s.SmallGameThreshold > 0 ? s.SmallGameThreshold : s.MinSegmentSample, 1, 100_000);
        _demandUsesMean = s.DemandUsesMean;
        _language = TextLang.Parse(s.Language);
        TextLang.PreferApp = true;
        TextLang.SetApp(_language);
        TextLang.Set(_language);
    }

    public static readonly int[] MinRatingOptions = [1, 2, 4, 5, 10, 25, 50, 100];

    private bool _showNsfw;
    private int _minRatings;
    private bool _includeUnrated;
    private int _smallGameThreshold;
    private bool _demandUsesMean;
    private UiLanguage _language;

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

    /// <summary>Hide segments (and map points) with fewer games that cleared the ratings minimum.</summary>
    public int SmallGameThreshold
    {
        get => _smallGameThreshold;
        set
        {
            value = Math.Clamp(value, 1, 100_000);
            if (_smallGameThreshold == value) return;
            _smallGameThreshold = value;
            _ = PersistAsync();
            Changed?.Invoke();
        }
    }

    /// <summary>Arithmetic mean of rating counts instead of the median, in tables and on the opportunity map.</summary>
    public bool DemandUsesMean
    {
        get => _demandUsesMean;
        set
        {
            if (_demandUsesMean == value) return;
            _demandUsesMean = value;
            _ = PersistAsync();
            Changed?.Invoke();
        }
    }

    public UiLanguage Language
    {
        get => _language;
        set
        {
            if (_language == value) return;
            _language = value;
            TextLang.SetApp(value);
            TextLang.Set(value);
            _analytics.UseLanguage(value);
            Changed?.Invoke();
            _ = PersistAsync();
        }
    }

    private async Task PersistAsync()
    {
        var model = _settings.Clone();
        model.DefaultShowNsfw = _showNsfw;
        model.DefaultMinRatings = _minRatings;
        model.DefaultIncludeUnrated = _includeUnrated;
        model.SmallGameThreshold = _smallGameThreshold;
        model.DemandUsesMean = _demandUsesMean;
        model.Language = TextLang.Code(_language);
        await _settings.SaveAsync(model);
    }
}
