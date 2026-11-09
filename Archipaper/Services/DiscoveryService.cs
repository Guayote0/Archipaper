using Archipaper.Models;
namespace Archipaper.Services;

public sealed class DiscoveryState
{
    public int NextJob { get; set; }
    public Dictionary<string, int> Pages { get; set; } = new();
    public HashSet<string> Seen { get; set; } = new();
}
public sealed record DiscoveryResult(int Added, int Duplicates, int FailedPreviews, IReadOnlyList<string> Errors)
{
    public string Message => $"Added {Added} images; {Duplicates} already known, {FailedPreviews} unavailable previews. " + string.Join(" ", Errors);
}
public sealed class DiscoveryService(IReadOnlyList<IImageProvider> providers)
{
    private readonly Dictionary<string, DateTimeOffset> _cooldowns = new();
    public static IReadOnlyList<DiscoveryRequest> BuildRequests(AppSettings settings)
    {
        var categories = settings.EnabledCategories.Where(x => x is "Buildings" or "Interiors" or "Details" or "Drawings" or "Models").Distinct().ToList();
        var architects = settings.PreferredArchitects.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return categories.SelectMany(c => architects.Select(a => new DiscoveryRequest(a, c, settings.StrictArchitectSearch))
            .Concat(settings.StrictArchitectSearch ? [] : new[] { new DiscoveryRequest("", c, false) })).ToList();
    }
    public async Task<DiscoveryResult> DiscoverAsync(AppSettings settings, DiscoveryState state, int target,
        Func<OnlineCandidate, CancellationToken, Task<bool>> accept, CancellationToken token)
    {
        var jobs = BuildRequests(settings).SelectMany(r => providers.Where(p => p.IsEnabled(settings, r)).Select(p => (Request: r, Provider: p))).ToList();
        if (jobs.Count == 0) return new(0, 0, 0, ["Select a source, category and (in architect-only mode) an architect."]);
        var unavailable = new HashSet<string>(); var exhausted = new HashSet<int>(); var errors = new List<string>();
        var added = 0; var duplicates = 0; var failed = 0; var searches = 0;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token); budget.CancelAfter(TimeSpan.FromSeconds(90));
        try
        {
            for (var visited = 0; visited < jobs.Count * 10 && searches < 24 && added < target; visited++)
            {
                var index = Math.Abs(state.NextJob % jobs.Count); state.NextJob = (index + 1) % jobs.Count;
                var (request, provider) = jobs[index];
                if (exhausted.Contains(index) || unavailable.Contains(provider.Id)) continue;
                if (_cooldowns.TryGetValue(provider.Id, out var until) && until > DateTimeOffset.UtcNow)
                { unavailable.Add(provider.Id); errors.Add($"{provider.Name} cooling down until {until.ToLocalTime():t}."); continue; }
                budget.Token.ThrowIfCancellationRequested(); searches++;
                var key = provider.Id + "|" + request.QueryText; var page = state.Pages.GetValueOrDefault(key);
                IReadOnlyList<OnlineCandidate> candidates;
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(budget.Token); deadline.CancelAfter(TimeSpan.FromSeconds(15));
                try { candidates = await provider.SearchAsync(request, 25, deadline.Token, page); }
                catch (OperationCanceledException) when (budget.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    AppLog.Error(ex); unavailable.Add(provider.Id);
                    _cooldowns[provider.Id] = DateTimeOffset.UtcNow + (ex is ProviderRateLimitException rate ? rate.RetryAfter : TimeSpan.FromMinutes(1));
                    errors.Add($"{provider.Name}: {(ex is OperationCanceledException or TimeoutException ? "timed out" : "unavailable")}."); continue;
                }
                if (candidates.Count == 0) { exhausted.Add(index); state.Pages[key] = 0; continue; }
                var consumed = true;
                foreach (var candidate in candidates)
                {
                    budget.Token.ThrowIfCancellationRequested();
                    string url;
                    try { url = HttpTransfer.UrlKey(candidate.OriginalUrl); HttpTransfer.UrlKey(candidate.PreviewUrl); }
                    catch (InvalidDataException) { failed++; continue; }
                    if (state.Seen.Contains(candidate.Id) || state.Seen.Contains(url)) { duplicates++; continue; }
                    if (await accept(candidate, budget.Token)) { state.Seen.Add(candidate.Id); state.Seen.Add(url); added++; }
                    else failed++;
                    if (added >= target) { consumed = false; break; }
                }
                if (consumed) state.Pages[key] = page >= 9 ? 0 : page + 1;
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { errors.Add("Time limit reached. Progress saved; discover again to continue."); }
        return new(added, duplicates, failed, errors);
    }
}
