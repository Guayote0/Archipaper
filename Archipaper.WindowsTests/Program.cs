using Archipaper.Models;
using Archipaper.Services;
using System.IO;
using System.Collections.Concurrent;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var dir = Path.Combine(Path.GetTempPath(),"Archipaper-tests-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("ARCHIPAPER_DATA_DIR",dir);
        try { RunAsync().GetAwaiter().GetResult(); Console.WriteLine("5/5 Windows checks passed."); return 0; }
        catch (Exception ex) { Console.WriteLine("FAIL " + ex); return 1; }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir,true); }
    }
    private static async Task RunAsync()
    {
        AppPaths.EnsureCreated(); var dir = Path.Combine(AppPaths.Root,"source"); Directory.CreateDirectory(dir);
        var landscape = Path.Combine(dir,"landscape.png"); var portrait = Path.Combine(dir,"portrait.png");
        var small = Path.Combine(dir,"small.png"); var bad = Path.Combine(dir,"bad.jpg"); var copy = Path.Combine(dir,"copy.png");
        CreateImage(landscape,1920,1200,Colors.DarkRed); CreateImage(portrait,1200,1920,Colors.DarkGreen);
        CreateImage(small,100,100,Colors.Black); File.WriteAllText(bad,"broken"); File.Copy(landscape,copy);
        Check(ImageValidation.Inspect(portrait,true,true).Height == 1920,"Decode portrait");
        foreach (var invalid in new[]{small,bad})
        {
            try { ImageValidation.Inspect(invalid,true,true); throw new Exception("Invalid image accepted"); }
            catch (InvalidDataException) { }
        }
        Console.WriteLine("PASS WPF image validation");
        var store = new JsonStore();
        await using (var queue = await ReviewQueueService.CreateAsync(store))
        {
            Check(await queue.ImportLocalFilesAsync([landscape,bad,portrait,small,copy]) == 2,"Import continues and deduplicates");
            Check(queue.Approved.All(x=>x.ContentHash.Length==64),"Hashes persisted");
            Check(await queue.ImportLocalFilesAsync([copy]) == 0,"Repeat import");
        }
        Console.WriteLine("PASS Import validation and content deduplication");
        var preview = Path.Combine(AppPaths.CandidateCache,"cached.png"); CreateImage(preview,1920,1200,Colors.DarkBlue);
        var candidate = new OnlineCandidate{Id="cached",OriginalUrl="https://example.invalid/image.png",PreviewUrl="https://example.invalid/image.png",PreviewFilePath=preview,Title="Cached image"};
        await store.SaveAsync(AppPaths.ReviewQueue,new[]{candidate});
        await using (var queue = await ReviewQueueService.CreateAsync(store))
        {
            Check(await queue.ApproveAsync(queue.Current()!,CancellationToken.None),"Cached full image should not request invalid host");
            Check(queue.Approved.Count==3 && queue.Pending.Count==0,"Approval committed");
            await queue.ApproveAsync(candidate,CancellationToken.None); Check(queue.Approved.Count==3,"Idempotent approval");
        }
        Console.WriteLine("PASS Offline cached approval and idempotence");
        var captions = new CaptionedWallpaperService(); var metadata = new ApprovedImageMetadata{Title="Test project",Architect="Test architect"};
        var output = captions.Create(landscape,metadata,new("caption",3840,2160)); var time = File.GetLastWriteTimeUtc(output);
        Check(captions.Create(landscape,metadata,new("caption",3840,2160))==output && File.GetLastWriteTimeUtc(output)==time,"Caption reuse");
        Check(ImageValidation.Inspect(output).Width==3840,"4K canvas");
        Console.WriteLine("PASS 4K caption generation and cache reuse");
        var schedule = new RotationSchedule(); var old = DateTimeOffset.UtcNow.AddHours(-2);
        schedule.Succeeded("landscape",old); schedule.Succeeded("portrait",old);
        var state = new DesktopState();
        await using (var rotation = new RotationService(new(){WallpaperSourceMode=AppSettings.ApprovedOnly,AvoidRecentImages=false},[],store,schedule,()=>new FakeDesktop(state)))
        {
            await rotation.StartTimerAsync();
            Check(state.Applied.Count==1 && state.Applied[0]=="landscape" && schedule.Monitors["portrait"].Failures==1,"Independent monitor failure");
            var due = schedule.Monitors["landscape"].DueAt; await rotation.StartTimerAsync();
            Check(schedule.Monitors["landscape"].DueAt==due && state.Applied.Count==1,"Save preserves deadline");
            schedule.Monitors["portrait"].DueAt=DateTimeOffset.UtcNow.AddSeconds(-1); await rotation.StartTimerAsync();
            Check(state.Applied.Count==2 && state.Applied.Count(x=>x=="landscape")==1,"Only failed monitor retries");
            foreach(var item in schedule.Monitors.Values) item.DueAt=DateTimeOffset.UtcNow.AddSeconds(-1);
            await rotation.StartTimerAsync(true); Check(state.Applied.Count==4,"Save and manual change happen once");
        }
        Check(state.Threads.Distinct().Count()==1 && state.Apartments.All(x=>x==ApartmentState.STA),"One STA for all desktop calls/disposal");
        Console.WriteLine("PASS Real scheduler and single-STA ownership");
    }
    private static void Check(bool success,string message) { if (!success) throw new Exception(message); }
    private static void CreateImage(string path,int width,int height,Color color)
    {
        var pixels = new byte[width*height*3];
        for(int i=0;i<pixels.Length;i+=3){pixels[i]=color.B;pixels[i+1]=color.G;pixels[i+2]=color.R;}
        var bitmap=BitmapSource.Create(width,height,96,96,PixelFormats.Bgr24,null,pixels,width*3);
        var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file=File.Create(path); encoder.Save(file);
    }
}
internal sealed class DesktopState
{
    public ConcurrentBag<int> Threads {get;}=new();
    public ConcurrentBag<ApartmentState> Apartments {get;}=new();
    public List<string> Applied {get;}=[];
    public bool FailPortrait {get;set;}=true;
}
internal sealed class FakeDesktop(DesktopState state) : IWallpaperDesktop
{
    private void Record(){state.Threads.Add(Environment.CurrentManagedThreadId);state.Apartments.Add(Thread.CurrentThread.GetApartmentState());}
    public IReadOnlyList<MonitorInfo> GetMonitors(){Record();return [new("landscape",3840,2160),new("portrait",2160,3840)];}
    public void Set(string id,string path)
    {
        Record(); if(id=="portrait" && state.FailPortrait){state.FailPortrait=false;throw new IOException("Simulated monitor failure");}
        if(!File.Exists(path))throw new FileNotFoundException(); state.Applied.Add(id);
    }
    public void Dispose()=>Record();
}
