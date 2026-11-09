using Archipaper.Models;
using Archipaper.Services;
using System.Text.Json;

var tests = new List<(string Name, Func<Task> Run)>();
void Check(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
void Add(string name, Action test) => tests.Add((name, () => { test(); return Task.CompletedTask; }));
var now = new DateTimeOffset(2026, 11, 9, 10, 0, 0, TimeSpan.Zero);
Add("Saving preferences preserves deadline", () =>
{
    var s = new RotationSchedule(); var due = s.Ensure("m", now).DueAt;
    for (int i = 1; i < 60; i++) s.Configure(60, now.AddMinutes(i));
    Check(s.Monitors["m"].DueAt == due && s.IsDue("m", now.AddHours(1)));
});
Add("Restart and missed-deadline catch-up", () =>
{
    var s = new RotationSchedule(); s.Succeeded("m", now);
    s = JsonSerializer.Deserialize<RotationSchedule>(JsonSerializer.Serialize(s))!;
    Check(s.IsDue("m", now.AddHours(8))); s.Succeeded("m", now.AddHours(8));
    Check(!s.IsDue("m", now.AddHours(8).AddSeconds(1)));
});
Add("Independent retries with capped backoff", () =>
{
    var s = new RotationSchedule(); s.Succeeded("a", now); s.Failed("b", now);
    Check(!s.IsDue("a", now.AddMinutes(1)) && s.IsDue("b", now.AddMinutes(1)));
    for (int i = 0; i < 10; i++) s.Failed("b", now);
    Check(s.Monitors["b"].DueAt == now.AddMinutes(15)); s.Succeeded("b", now); Check(s.Monitors["b"].Failures == 0);
});
Add("Interval and backward-clock handling", () =>
{
    var s = new RotationSchedule(); s.Succeeded("m", now); s.Configure(15, now.AddMinutes(20));
    Check(s.IsDue("m", now.AddMinutes(20))); s.Configure(15, now.AddHours(-1));
    Check(s.Monitors["m"].DueAt == now.AddHours(-1).AddMinutes(15));
});
Add("Distinct monitor choices with exhausted recent pool", () =>
{
    var items = new[] { Item("a"), Item("b") }; var empty = new HashSet<string>();
    foreach (var avoid in new[] { true, false })
        Check(WallpaperSelector.Select(items, false, null, new HashSet<string>{"a"}, new HashSet<string>{"a","b"}, empty, true, avoid, new Random(1))!.Id == "b");
});
Add("Orientation, current image avoidance and tiny pools", () =>
{
    var p = new WallpaperItem("p", "p", "", "", 2160, 3840); var l = Item("l"); var l2 = Item("l2"); var e = new HashSet<string>();
    Check(WallpaperSelector.Select([p,l], true, null, e,e,e,true,true,new(1)) == p);
    Check(WallpaperSelector.Select([l,l2], false, "l", e,e,e,true,true,new(1)) == l2);
    Check(WallpaperSelector.Select([p], true, "p", e,e,e,true,true,new(1)) == p);
    Check(WallpaperSelector.Select([], true, null, e,e,e,true,true,new(1)) is null);
});
tests.Add(("Concurrent persistence snapshots and recovery", async () =>
{
    var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
    try
    {
        var store = new JsonStore(); var path = Path.Combine(directory, "state.json"); var list = new List<int>{1,2};
        var writing = store.SaveAsync(path, list); list.Add(3); await writing;
        Check((await store.LoadAsync(path, () => new List<int>())).Count == 2);
        await Task.WhenAll(Enumerable.Range(0,40).Select(i => new JsonStore().SaveAsync(path, new[] { i,i })));
        var data = await store.LoadAsync(path, () => Array.Empty<int>()); Check(data.Length == 2 && data[0] == data[1]);
        var backup = await File.ReadAllTextAsync(path + ".bak"); await File.WriteAllTextAsync(path,"{bad");
        Check((await store.LoadAsync(path, () => Array.Empty<int>())).SequenceEqual(JsonSerializer.Deserialize<int[]>(backup)!));
        Check(Directory.GetFiles(directory,"*.corrupt-*").Length == 1 && Directory.GetFiles(directory,"*.tmp").Length == 0);
    }
    finally { Directory.Delete(directory,true); }
}));
Add("Empty selections do not silently broaden searches", () =>
{
    var settings = new AppSettings{ PreferredArchitects = [] };
    Check(DiscoveryService.BuildRequests(settings).Count == 0); settings.StrictArchitectSearch = false;
    Check(DiscoveryService.BuildRequests(settings).Count == 5); settings.EnabledCategories.Clear();
    Check(DiscoveryService.BuildRequests(settings).Count == 0);
});
tests.Add(("Provider timeout isolation and caller cancellation", async () =>
{
    var bad = new Provider("bad", (_,_) => throw new OperationCanceledException());
    var good = new Provider("good", (_,_) => Task.FromResult<IReadOnlyList<OnlineCandidate>>([Candidate("ok")]));
    var service = new DiscoveryService([bad,good]);
    var result = await service.DiscoverAsync(Query(),new(),1,(_,_) => Task.FromResult(true),CancellationToken.None);
    Check(result.Added == 1 && result.Errors.Count == 1);
    using var cts = new CancellationTokenSource(); cts.Cancel();
    try { await service.DiscoverAsync(Query(),new(),1,(_,_) => Task.FromResult(true),cts.Token); throw new Exception("Cancellation missing"); }
    catch (OperationCanceledException) { }
}));
tests.Add(("Pagination and cross-provider URL deduplication", async () =>
{
    var state = new DiscoveryState(); state.Seen.Add("known"); var pages = new List<int>();
    var provider = new Provider("test", (page,_) => { pages.Add(page); return Task.FromResult<IReadOnlyList<OnlineCandidate>>([Candidate(page == 0 ? "known" : "new")]); });
    var result = await new DiscoveryService([provider]).DiscoverAsync(Query(),state,1,(_,_) => Task.FromResult(true),CancellationToken.None);
    Check(result.Added == 1 && pages.SequenceEqual(new[]{0,1}));
    var duplicate = Candidate("new"); duplicate.Id = "other"; duplicate.OriginalUrl += "#fragment";
    provider = new("other",(_,_) => Task.FromResult<IReadOnlyList<OnlineCandidate>>([duplicate]));
    result = await new DiscoveryService([provider]).DiscoverAsync(Query(),state,1,(_,_) => throw new Exception("Duplicate downloaded"),CancellationToken.None);
    Check(result.Added == 0 && result.Duplicates > 0);
}));
tests.Add(("HTTP bounds and cleanup preserve existing files", async () =>
{
    var dir = Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
    try
    {
        var path = Path.Combine(dir,"image"); await File.WriteAllTextAsync(path,"original");
        foreach (var content in new HttpContent[]{new StringContent("html",System.Text.Encoding.UTF8,"text/html"),new ByteArrayContent(new byte[2000]),new StreamContent(new StalledStream())})
        {
            using var http = new HttpClient(new Handler(() => new(System.Net.HttpStatusCode.OK){Content=content}));
            try { await HttpTransfer.DownloadAsync(http,"https://example.com/image",path,1000,TimeSpan.FromMilliseconds(100),CancellationToken.None); throw new Exception("Invalid transfer accepted"); }
            catch (Exception ex) when (ex is InvalidDataException or TimeoutException) { }
        }
        Check(await File.ReadAllTextAsync(path) == "original" && Directory.GetFiles(dir,"*.part").Length == 0);
    }
    finally { Directory.Delete(dir,true); }
}));
Add("Rate limits preserve Retry-After", () =>
{
    using var response = new HttpResponseMessage(System.Net.HttpStatusCode.TooManyRequests);
    response.Headers.RetryAfter = new(TimeSpan.FromMinutes(3));
    try { HttpTransfer.EnsureSuccess(response); throw new Exception("Rate limit ignored"); }
    catch (ProviderRateLimitException ex) { Check(ex.RetryAfter == TimeSpan.FromMinutes(3)); }
});
int failures = 0;
foreach (var test in tests)
{
    try { await test.Run(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception ex) { failures++; Console.WriteLine("FAIL " + test.Name + ": " + ex); }
}
Console.WriteLine($"{tests.Count-failures}/{tests.Count} checks passed.");
return failures == 0 ? 0 : 1;

static WallpaperItem Item(string id) => new(id,id,"","",3840,2160);
static AppSettings Query() => new(){EnabledCategories=["Buildings"],PreferredArchitects=["Enric Miralles"]};
static OnlineCandidate Candidate(string id) => new(){Id=id,OriginalUrl=$"https://example.com/{id}.jpg",PreviewUrl=$"https://example.com/{id}-preview.jpg"};
sealed class Provider(string id, Func<int,CancellationToken,Task<IReadOnlyList<OnlineCandidate>>> search) : IImageProvider
{
    public string Id => id; public string Name => id;
    public bool IsEnabled(AppSettings settings, DiscoveryRequest request) => true;
    public Task<IReadOnlyList<OnlineCandidate>> SearchAsync(DiscoveryRequest request,int limit,CancellationToken token,int page=0) => search(page,token);
    public void Dispose() { }
}
sealed class Handler(Func<HttpResponseMessage> response) : HttpMessageHandler
{ protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) => Task.FromResult(response()); }
sealed class StalledStream : Stream
{
    public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException(); public override long Position { get => 0; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer,int offset,int count) => throw new NotSupportedException();
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken cancellationToken=default) { await Task.Delay(Timeout.Infinite,cancellationToken); return 0; }
    public override long Seek(long offset,SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer,int offset,int count) => throw new NotSupportedException();
}
