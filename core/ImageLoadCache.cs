using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FastImageViewer
{
    // A single decoder serves foreground requests before speculative neighbours.
    // Each displayed image must keep its Lease alive, including after cache disposal.
    public sealed class ImageLoadCache<T> : IDisposable
    {
        public sealed class Lease : IDisposable
        {
            private ImageLoadCache<T> owner;
            private Entry entry;
            internal Lease(ImageLoadCache<T> owner, Entry entry) { this.owner = owner; this.entry = entry; }
            public T Value { get { return entry.Value; } }
            public void Dispose()
            {
                ImageLoadCache<T> cache = System.Threading.Interlocked.Exchange(ref owner, null);
                if (cache != null) cache.Release(entry);
            }
        }

        internal sealed class Entry
        {
            internal T Value;
            internal long Bytes, Access;
            internal Stamp Stamp;
            internal int Pins;
            internal bool Retired;
        }
        internal struct Stamp
        {
            internal long Length, Ticks;
            internal bool Equals(Stamp other) { return Length == other.Length && Ticks == other.Ticks; }
        }
        private sealed class Request
        {
            internal string Path;
            internal int Generation;
            internal readonly List<Waiter> Waiters = new List<Waiter>();
        }
        private sealed class Waiter
        {
            internal readonly TaskCompletionSource<Lease> Completion = new TaskCompletionSource<Lease>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal CancellationTokenRegistration Registration;
        }

        private readonly object gate = new object();
        private readonly Func<string, T> loader;
        private readonly Func<T, long> estimateBytes;
        private readonly Action<T> dispose;
        private readonly long budget;
        private readonly Dictionary<string, Entry> entries;
        private readonly Dictionary<string, Request> requests;
        private readonly LinkedList<Request> foreground = new LinkedList<Request>();
        private Queue<string> neighbours = new Queue<string>();
        private long bytes, clock;
        private int generation;
        private bool working, disposed;

        public ImageLoadCache(Func<string, T> loader, Func<T, long> estimateBytes,
            Action<T> dispose = null, StringComparer comparer = null, long budgetBytes = 128L * 1024 * 1024)
        {
            if (loader == null) throw new ArgumentNullException("loader");
            if (estimateBytes == null) throw new ArgumentNullException("estimateBytes");
            if (budgetBytes < 0) throw new ArgumentOutOfRangeException("budgetBytes");
            this.loader = loader; this.estimateBytes = estimateBytes; this.dispose = dispose; budget = budgetBytes;
            comparer = comparer ?? ImageFiles.PathComparer;
            entries = new Dictionary<string, Entry>(comparer);
            requests = new Dictionary<string, Request>(comparer);
        }

        public Task<Lease> LoadAsync(string path, CancellationToken cancellation = default(CancellationToken))
        {
            path = Path.GetFullPath(path);
            lock (gate)
            {
                if (disposed) throw new ObjectDisposedException("ImageLoadCache");
                if (cancellation.IsCancellationRequested)
                {
                    var canceled = new TaskCompletionSource<Lease>(); canceled.SetCanceled(); return canceled.Task;
                }
                Entry entry = Find(path);
                if (entry != null) return Task.FromResult(Pin(entry));
                Request request;
                if (!requests.TryGetValue(path, out request))
                {
                    request = new Request { Path = path, Generation = generation };
                    requests.Add(path, request); foreground.AddFirst(request);
                }
                else
                {
                    LinkedListNode<Request> queued = foreground.Find(request);
                    if (queued != null) { foreground.Remove(queued); foreground.AddFirst(queued); }
                }
                var waiter = new Waiter();
                if (cancellation.CanBeCanceled)
                    waiter.Registration = cancellation.Register(delegate { waiter.Completion.TrySetCanceled(); });
                request.Waiters.Add(waiter);
                Start();
                return waiter.Completion.Task;
            }
        }

        public void Preload(IEnumerable<string> paths)
        {
            var candidates = new Queue<string>();
            var seen = new HashSet<string>(entries.Comparer);
            if (paths != null)
                foreach (string path in paths)
                {
                    if (String.IsNullOrWhiteSpace(path)) continue;
                    string full = Path.GetFullPath(path);
                    if (seen.Add(full)) candidates.Enqueue(full);
                }
            lock (gate)
            {
                if (disposed) return;
                neighbours = candidates;
                Start();
            }
        }

        public void Clear()
        {
            lock (gate)
            {
                generation++; neighbours.Clear();
                foreach (Entry entry in entries.Values) Retire(entry);
                entries.Clear(); bytes = 0;
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                Clear();
                foreach (Request request in requests.Values)
                    foreach (var waiter in request.Waiters)
                    { waiter.Completion.TrySetException(new ObjectDisposedException("ImageLoadCache")); waiter.Registration.Dispose(); }
                foreground.Clear(); requests.Clear();
            }
        }

        private void Start()
        {
            if (working || (foreground.Count == 0 && neighbours.Count == 0)) return;
            working = true;
            Task.Run((Action)Work);
        }

        private void Work()
        {
            while (true)
            {
                Request request = null;
                lock (gate)
                {
                    while (!disposed && foreground.Count != 0 && request == null)
                    {
                        request = foreground.First.Value;
                        foreground.RemoveFirst();
                        bool needed = false;
                        foreach (var waiter in request.Waiters) if (!waiter.Completion.Task.IsCompleted) needed = true;
                        if (!needed)
                        {
                            requests.Remove(request.Path);
                            foreach (var waiter in request.Waiters) waiter.Registration.Dispose();
                            request = null;
                        }
                    }
                    while (!disposed && request == null && neighbours.Count != 0)
                    {
                        string path = neighbours.Dequeue();
                        if (requests.ContainsKey(path) || Find(path) != null) continue;
                        request = new Request { Path = path, Generation = generation };
                        requests.Add(path, request);
                    }
                    if (request == null) { working = false; return; }
                }
                T value = default(T);
                bool loaded = false;
                Entry entry = null;
                Exception failure = null;
                try
                {
                    Stamp stamp = ReadStamp(request.Path);
                    value = loader(request.Path); loaded = true;
                    if (!stamp.Equals(ReadStamp(request.Path))) throw new IOException("Image changed while decoding: " + request.Path);
                    entry = new Entry { Value = value, Bytes = Math.Max(0, estimateBytes(value)), Stamp = stamp };
                }
                catch (Exception ex) { failure = ex; }
                lock (gate)
                {
                    requests.Remove(request.Path);
                    if (disposed)
                    {
                        if (loaded) Destroy(value);
                        continue;
                    }
                    if (failure != null)
                    {
                        if (loaded) Destroy(value);
                        foreach (var waiter in request.Waiters)
                        { waiter.Completion.TrySetException(failure); waiter.Registration.Dispose(); }
                        continue;
                    }
                    entry.Access = ++clock;
                    entry.Retired = request.Generation != generation;
                    if (!entry.Retired) { entries[request.Path] = entry; bytes += entry.Bytes; }
                    foreach (var waiter in request.Waiters)
                    {
                        if (!waiter.Completion.Task.IsCompleted)
                        {
                            Lease lease = Pin(entry);
                            if (!waiter.Completion.TrySetResult(lease)) entry.Pins--;
                        }
                        waiter.Registration.Dispose();
                    }
                    if (entry.Retired && entry.Pins == 0) Destroy(entry.Value);
                    Trim();
                }
            }
        }

        private Entry Find(string path)
        {
            Entry entry;
            if (!entries.TryGetValue(path, out entry)) return null;
            bool valid = false;
            try { valid = entry.Stamp.Equals(ReadStamp(path)); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            if (!valid)
            {
                entries.Remove(path); bytes -= entry.Bytes; Retire(entry); return null;
            }
            entry.Access = ++clock;
            return entry;
        }

        private Lease Pin(Entry entry) { entry.Pins++; entry.Access = ++clock; return new Lease(this, entry); }
        private void Release(Entry entry)
        {
            lock (gate)
            {
                entry.Pins--;
                if (entry.Retired && entry.Pins == 0) Destroy(entry.Value);
                Trim();
            }
        }
        private void Retire(Entry entry)
        {
            entry.Retired = true;
            if (entry.Pins == 0) Destroy(entry.Value);
        }
        private void Trim()
        {
            while (bytes > budget)
            {
                string key = null;
                Entry oldest = null;
                foreach (var pair in entries)
                    if (pair.Value.Pins == 0 && (oldest == null || pair.Value.Access < oldest.Access))
                    { key = pair.Key; oldest = pair.Value; }
                if (oldest == null) return; // Active images may temporarily exceed the budget.
                entries.Remove(key); bytes -= oldest.Bytes; Retire(oldest);
            }
        }
        private void Destroy(T value)
        {
            if (dispose != null) dispose(value);
        }
        private static Stamp ReadStamp(string path)
        {
            var file = new FileInfo(path);
            if (!file.Exists) throw new FileNotFoundException("Image not found", path);
            return new Stamp { Length = file.Length, Ticks = file.LastWriteTimeUtc.Ticks };
        }
    }
}
