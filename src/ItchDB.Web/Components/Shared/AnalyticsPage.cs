using ItchDB.Core.Analytics;
using ItchDB.Infrastructure.Analytics;
using ItchDB.Web.Services;
using Microsoft.AspNetCore.Components;

namespace ItchDB.Web.Components.Shared;

/// <summary>
/// Base for pages built on the analytics snapshot: follows dataset reloads and the global filters of the app bar,
/// and recomputes the page's view model off the render thread.
/// </summary>
public abstract class AnalyticsPage : ComponentBase, IDisposable
{
    [Inject] protected AnalyticsService Analytics { get; set; } = default!;
    [Inject] protected UiState Ui { get; set; } = default!;

    protected AnalyticsSnapshot A { get; private set; } = AnalyticsSnapshot.Empty;
    protected AnalyticsFilter F { get; private set; } = AnalyticsFilter.Default;
    protected AnalyticsDataset D => A.Data;
    protected bool Computing { get; private set; }
    protected string? ComputeError { get; private set; }

    protected override async Task OnInitializedAsync()
    {
        Analytics.Updated += OnAnalyticsUpdated;
        Ui.Changed += OnFiltersChanged;
        A = Analytics.Current;
        F = new AnalyticsFilter(Ui.ShowNsfw, Ui.MinRatings);
        if (!Analytics.IsLoaded) _ = Analytics.EnsureLoadedAsync();
        await RecomputeAsync();
    }

    /// <summary>Build the page's view model from <see cref="A"/> and <see cref="F"/>. Runs on a worker thread.</summary>
    protected abstract void Compute();

    protected async Task RecomputeAsync()
    {
        Computing = true;
        ComputeError = null;
        try
        {
            var snapshot = A;
            if (!snapshot.IsEmpty) await Task.Run(Compute);
        }
        catch (Exception ex)
        {
            ComputeError = ex.Message;
        }
        finally
        {
            Computing = false;
        }
    }

    private void OnAnalyticsUpdated() => InvokeAsync(async () =>
    {
        if (!ReferenceEquals(A, Analytics.Current))
        {
            A = Analytics.Current;
            await RecomputeAsync();
        }
        StateHasChanged();
    });

    private void OnFiltersChanged() => InvokeAsync(async () =>
    {
        F = new AnalyticsFilter(Ui.ShowNsfw, Ui.MinRatings);
        await RecomputeAsync();
        StateHasChanged();
    });

    public virtual void Dispose()
    {
        Analytics.Updated -= OnAnalyticsUpdated;
        Ui.Changed -= OnFiltersChanged;
        GC.SuppressFinalize(this);
    }
}
