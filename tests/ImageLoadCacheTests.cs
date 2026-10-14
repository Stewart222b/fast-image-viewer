using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FastImageViewer;

internal static class ImageLoadCacheTests
{
    private sealed class Image { public string Path; public int Disposals; }
    private static string root;
    private static int passed;
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static string FilePath(string name) { string path = Path.Combine(root, name); File.WriteAllText(path, name); return path; }
    private static T Await<T>(Task<T> task) { Check(task.Wait(5000), "Timed out"); return task.Result; }
    private static void Test(string name, Action test) { test(); passed++; Console.WriteLine("PASS " + name); }
    private static void Main(string[] args)
    {
        try { RunAll(args.Length == 0 ? Path.Combine(Environment.CurrentDirectory, "artifacts") : args[0]); }
        catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
    }

    public static void RunAll(string outputDirectory)
    {
        passed = 0;
        string outputRoot = Path.GetFullPath(outputDirectory);
        if (outputRoot.Length > Path.GetPathRoot(outputRoot).Length)
            outputRoot = outputRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        root = Path.Combine(outputRoot, "cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Test("Concurrent requests share one decode and independent leases", delegate
            {
                string path = FilePath("same"); int count = 0;
                var entered = new ManualResetEventSlim(); var finish = new ManualResetEventSlim();
                using (var cache = new ImageLoadCache<Image>(p => { Interlocked.Increment(ref count); entered.Set(); finish.Wait(); return new Image { Path = p }; }, i => 10, i => i.Disposals++))
                {
                    var first = cache.LoadAsync(path); Check(entered.Wait(5000), "No worker");
                    var second = cache.LoadAsync(path); finish.Set();
                    var a = Await(first); var b = Await(second); Check(count == 1 && Object.ReferenceEquals(a.Value, b.Value), "Duplicate decode");
                    a.Dispose(); cache.Clear(); Check(b.Value.Disposals == 0, "Live lease disposed"); b.Dispose(); Check(b.Value.Disposals == 1, "Missing retirement"); b.Dispose(); Check(b.Value.Disposals == 1, "Double disposal");
                }
            });
            Test("New foreground runs before queued preload and stale neighbours are replaced", delegate
            {
                string a = FilePath("slow"), b = FilePath("obsolete"), c = FilePath("foreground"), d = FilePath("new-neighbour");
                var entered = new ManualResetEventSlim(); var finish = new ManualResetEventSlim(); var done = new ManualResetEventSlim();
                var order = new List<string>();
                using (var cache = new ImageLoadCache<Image>(p => { lock (order) order.Add(p); if (p == a) { entered.Set(); finish.Wait(); } if (p == d) done.Set(); return new Image { Path = p }; }, i => 10))
                {
                    cache.Preload(new[] { a, b }); Check(entered.Wait(5000), "Preload did not start");
                    var foreground = cache.LoadAsync(c); cache.Preload(new[] { d, d }); finish.Set();
                    Await(foreground).Dispose(); Check(done.Wait(5000), "New neighbour not loaded");
                    lock (order) Check(order.Count == 3 && order[0] == a && order[1] == c && order[2] == d, "Wrong scheduling");
                }
            });
            Test("Latest foreground request is decoded before older queued navigation", delegate
            {
                string a = FilePath("foreground-block"), b = FilePath("foreground-old"), c = FilePath("foreground-new");
                var entered = new ManualResetEventSlim(); var finish = new ManualResetEventSlim(); var order = new List<string>();
                using (var cache = new ImageLoadCache<Image>(p => { lock (order) order.Add(p); if (p == a) { entered.Set(); finish.Wait(); } return new Image(); }, i => 10))
                {
                    var first = cache.LoadAsync(a); Check(entered.Wait(5000), "No decode"); var old = cache.LoadAsync(b); var latest = cache.LoadAsync(c); finish.Set();
                    Await(first).Dispose(); Await(latest).Dispose(); Await(old).Dispose();
                    lock (order) Check(order.Count == 3 && order[1] == c && order[2] == b, "Newest foreground did not take priority");
                }
            });
            Test("Canceled queued foreground is skipped without decoding", delegate
            {
                string a = FilePath("cancel-block"), b = FilePath("cancel-old"), c = FilePath("cancel-new");
                var entered = new ManualResetEventSlim(); var finish = new ManualResetEventSlim(); int count = 0;
                using (var cache = new ImageLoadCache<Image>(p => { Interlocked.Increment(ref count); Check(p != b, "Canceled file was decoded"); if (p == a) { entered.Set(); finish.Wait(); } return new Image(); }, i => 10))
                using (var cancellation = new CancellationTokenSource())
                {
                    var first = cache.LoadAsync(a); Check(entered.Wait(5000), "No decode"); var old = cache.LoadAsync(b, cancellation.Token); cancellation.Cancel(); var latest = cache.LoadAsync(c); finish.Set();
                    Await(first).Dispose(); Await(latest).Dispose(); Check(old.IsCanceled && count == 2, "Cancellation ignored");
                }
            });
            Test("Canceling one shared requester leaves other consumer intact", delegate
            {
                string p = FilePath("cancel-shared"); var entered = new ManualResetEventSlim(); var finish = new ManualResetEventSlim(); int count = 0;
                using (var cache = new ImageLoadCache<Image>(s => { count++; entered.Set(); finish.Wait(); return new Image(); }, i => 10, i => i.Disposals++))
                using (var cancellation = new CancellationTokenSource())
                {
                    var canceled = cache.LoadAsync(p, cancellation.Token); Check(entered.Wait(5000), "No decode"); var active = cache.LoadAsync(p); cancellation.Cancel(); finish.Set();
                    var lease = Await(active); Check(canceled.IsCanceled && count == 1 && lease.Value.Disposals == 0, "Shared result lost"); lease.Dispose();
                }
            });
            Test("Many canceled same-path consumers leave no hidden image pins", delegate
            {
                string p = FilePath("cancel-many"); var entered = new ManualResetEventSlim(); var finish = new ManualResetEventSlim();
                using (var cache = new ImageLoadCache<Image>(s => { entered.Set(); finish.Wait(); return new Image(); }, i => 100, i => i.Disposals++, budgetBytes: 0))
                {
                    var consumers = new List<CancellationTokenSource>(); var tasks = new List<Task<ImageLoadCache<Image>.Lease>>();
                    for (int i = 0; i < 64; i++)
                    {
                        var cancellation = new CancellationTokenSource(); consumers.Add(cancellation); tasks.Add(cache.LoadAsync(p, cancellation.Token));
                    }
                    Check(entered.Wait(5000), "No decode"); var active = cache.LoadAsync(p);
                    foreach (var cancellation in consumers) cancellation.Cancel();
                    finish.Set(); var lease = Await(active);
                    foreach (var task in tasks) Check(task.IsCanceled, "Consumer not canceled");
                    Check(lease.Value.Disposals == 0, "Active image retired"); lease.Dispose();
                    Check(lease.Value.Disposals == 1, "Canceled consumers leaked pins");
                    foreach (var cancellation in consumers) cancellation.Dispose();
                }
            });
            Test("Oversized active image survives budget and is reclaimed on release", delegate
            {
                string p = FilePath("oversized");
                using (var cache = new ImageLoadCache<Image>(s => new Image(), i => 1024 * 1024, i => i.Disposals++, budgetBytes: 32))
                {
                    var lease = Await(cache.LoadAsync(p)); Check(lease.Value.Disposals == 0, "Oversized active image disposed");
                    lease.Dispose(); Check(lease.Value.Disposals == 1, "Oversized image retained beyond budget");
                }
            });
            Test("Foreground joins an executing preload without another decode", delegate
            {
                string p = FilePath("promote"); int count = 0;
                var entered = new ManualResetEventSlim(); var finish = new ManualResetEventSlim();
                using (var cache = new ImageLoadCache<Image>(s => { count++; entered.Set(); finish.Wait(); return new Image(); }, i => 10))
                {
                    cache.Preload(new[] { p }); Check(entered.Wait(5000), "No preload");
                    var task = cache.LoadAsync(p); finish.Set(); Await(task).Dispose(); Check(count == 1, "Repeated preload decode");
                }
            });
            Test("Missing speculative file does not block later foreground", delegate
            {
                string p = FilePath("after-missing"); int count = 0;
                using (var cache = new ImageLoadCache<Image>(s => { count++; return new Image(); }, i => 10))
                {
                    cache.Preload(new[] { Path.Combine(root, "missing"), p });
                    Await(cache.LoadAsync(p)).Dispose(); Check(count == 1, "Missing file reached decoder");
                }
            });
            Test("File modified during decoding fails safely and releases decoder output", delegate
            {
                string p = FilePath("changing-inflight"); Image image = null;
                using (var cache = new ImageLoadCache<Image>(s => { File.AppendAllText(s, "modified"); image = new Image(); return image; }, i => 10, i => i.Disposals++))
                {
                    try { Await(cache.LoadAsync(p)); throw new Exception("Accepted stale image"); } catch (AggregateException ex) { Check(ex.InnerException is IOException, "Wrong error"); }
                    Check(image.Disposals == 1, "Changed image leaked");
                }
            });
            Test("File length and timestamp invalidate cached images while old lease remains safe", delegate
            {
                string p = FilePath("changing"); int count = 0;
                using (var cache = new ImageLoadCache<Image>(s => { count++; return new Image { Path = s }; }, i => 10, i => i.Disposals++))
                {
                    var a = Await(cache.LoadAsync(p)); var hit = Await(cache.LoadAsync(p)); hit.Dispose(); Check(count == 1, "Cache missed");
                    File.AppendAllText(p, "longer"); var b = Await(cache.LoadAsync(p)); Check(count == 2 && a.Value.Disposals == 0, "Invalidation failed"); a.Dispose(); Check(a.Value.Disposals == 1, "Old image leaked");
                    File.SetLastWriteTimeUtc(p, DateTime.UtcNow.AddMinutes(2)); var c = Await(cache.LoadAsync(p)); Check(count == 3, "Timestamp ignored"); b.Dispose(); c.Dispose();
                }
            });
            Test("Byte budget evicts oldest released image and protects pinned image", delegate
            {
                string a = FilePath("budget-a"), b = FilePath("budget-b"); int count = 0;
                using (var cache = new ImageLoadCache<Image>(s => { count++; return new Image { Path = s }; }, i => 10, i => i.Disposals++, budgetBytes: 10))
                {
                    var x = Await(cache.LoadAsync(a)); var y = Await(cache.LoadAsync(b)); Check(x.Value.Disposals == 0 && y.Value.Disposals == 0, "Pinned image evicted");
                    x.Dispose(); Check(x.Value.Disposals == 1, "Budget did not evict"); var hit = Await(cache.LoadAsync(b)); hit.Dispose(); Check(count == 2, "Newest image not retained"); y.Dispose();
                }
            });
            Test("Failed decode is not cached and successful retry is possible", delegate
            {
                string p = FilePath("retry"); int count = 0;
                using (var cache = new ImageLoadCache<Image>(s => { if (++count == 1) throw new IOException("fake failure"); return new Image(); }, i => 10))
                {
                    try { Await(cache.LoadAsync(p)); throw new Exception("Expected failure"); } catch (AggregateException ex) { Check(ex.InnerException is IOException, "Wrong error"); }
                    Await(cache.LoadAsync(p)).Dispose(); Check(count == 2, "Failure cached");
                }
            });
            Test("Clear preserves pending foreground completion without retaining its image", delegate
            {
                string p = FilePath("clear"); var entered = new ManualResetEventSlim(); var finish = new ManualResetEventSlim();
                using (var cache = new ImageLoadCache<Image>(s => { entered.Set(); finish.Wait(); return new Image(); }, i => 10, i => i.Disposals++))
                {
                    var task = cache.LoadAsync(p); Check(entered.Wait(5000), "No decode"); cache.Clear(); finish.Set(); var lease = Await(task);
                    Check(lease.Value.Disposals == 0, "Clear disposed result"); lease.Dispose(); Check(lease.Value.Disposals == 1, "Retired result leaked");
                }
            });
            Test("Dispose preserves active image until its last lease is released", delegate
            {
                string p = FilePath("dispose"); var cache = new ImageLoadCache<Image>(s => new Image(), i => 10, i => i.Disposals++);
                var lease = Await(cache.LoadAsync(p)); cache.Dispose(); Check(lease.Value.Disposals == 0, "Disposed active image"); lease.Dispose(); Check(lease.Value.Disposals == 1, "Active image leaked");
                try { cache.LoadAsync(p); throw new Exception("Accepted request after disposal"); } catch (ObjectDisposedException) { }
            });
            Test("Dispose faults pending requests and disposes eventual decoder output", delegate
            {
                string p = FilePath("dispose-inflight"); var entered = new ManualResetEventSlim(); var finish = new ManualResetEventSlim(); var destroyed = new ManualResetEventSlim();
                var cache = new ImageLoadCache<Image>(s => { entered.Set(); finish.Wait(); return new Image(); }, i => 10, i => { i.Disposals++; destroyed.Set(); });
                var task = cache.LoadAsync(p); Check(entered.Wait(5000), "No decode"); cache.Dispose(); finish.Set();
                try { Await(task); throw new Exception("Expected disposal failure"); } catch (AggregateException ex) { Check(ex.InnerException is ObjectDisposedException, "Wrong error"); }
                Check(destroyed.Wait(5000), "Decoder output leaked");
            });
            Console.WriteLine(passed + " passed, 0 failed");
        }
        finally
        {
            string fullRoot = Path.GetFullPath(root);
            string parent = Path.GetDirectoryName(fullRoot);
            Check(ImageFiles.PathComparer.Equals(parent, outputRoot)
                && Path.GetFileName(fullRoot).StartsWith("cache-", StringComparison.Ordinal), "Unsafe cache test cleanup path");
            Directory.Delete(fullRoot, true);
        }
    }
}
