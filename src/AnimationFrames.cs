using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace XiaoGDesktop
{
    // 经审核的动作提前绘制为高清帧；播放只解压取图，不在界面线程重新拉伸人物。
    internal sealed class AnimationFrames
    {
        internal const int Rate = 120;
        private const int CacheFrames = 96;
        private readonly ZipArchive archive;
        private readonly string resourceName;
        private readonly bool heldCels;
        private ZipArchive aheadArchive;
        private readonly object cacheGate = new object();
        private readonly Dictionary<string, string[]> timelines = new Dictionary<string, string[]>();
        private readonly Dictionary<string, double> rates = new Dictionary<string, double>();
        private readonly Dictionary<string, BitmapSource> cache = new Dictionary<string, BitmapSource>();
        private readonly LinkedList<string> order = new LinkedList<string>();
        private readonly Dictionary<string, LinkedListNode<string>> positions = new Dictionary<string, LinkedListNode<string>>();
        private readonly byte[] pixels, aheadPixels;
        private string[] requestedTimeline;
        private int requestedIndex;
        private bool requestedLoop;
        private int requestedAhead;
        private int requestedStep = 1;
        private bool preparing;
        private string[] displayedTimeline, preparedTimeline;
        private double displayedElapsed = double.NegativeInfinity;
        private int previousIndex = -1, displayedIndex = -1;
        private long displayedCycle = -1;
        private BitmapSource displayedImage;
        private double displayedSample;

        // 生产导出默认同步；桌面播放显式启用后，界面线程只读取已准备的画面。
        internal bool Realtime { get; set; }
        // 与当前返回图像一致的时间，供手臂、眼睑、装饰及跳跃位移共用。
        internal double SampleTime { get; private set; }

        internal AnimationFrames() : this("animation-frames.zip") { }

        internal AnimationFrames(string resource, bool heldCels = false)
        {
            resourceName = resource;
            this.heldCels = heldCels;
            Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
            // 未制作帧包的源码构建沿用原画过渡，供正常制作新动作时使用。
            if (stream == null) return;
            archive = new ZipArchive(stream, ZipArchiveMode.Read);
            // BitmapSource.Create 会复制像素；每条读取路径复用一块缓冲，避免每帧制造大对象垃圾。
            pixels = new byte[SourceArtwork.Width * SourceArtwork.Height * 4];
            aheadPixels = new byte[pixels.Length];
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (!entry.FullName.StartsWith("timelines/", StringComparison.Ordinal) || !entry.FullName.EndsWith(".txt", StringComparison.Ordinal)) continue;
                string name = Path.GetFileNameWithoutExtension(entry.FullName);
                using (StreamReader reader = new StreamReader(entry.Open()))
                    timelines.Add(name, reader.ReadToEnd().Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
                ZipArchiveEntry rateEntry = archive.GetEntry("timelines/" + name + ".fps");
                double rate = 60;
                if (rateEntry != null)
                    using (StreamReader reader = new StreamReader(rateEntry.Open()))
                        if (!double.TryParse(reader.ReadToEnd().Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out rate)
                            || rate < 1 || rate > 240) throw new InvalidDataException("动作播放频率不正确：" + name);
                if (timelines[name].Length == 0) throw new InvalidDataException("动作没有画面：" + name);
                rates.Add(name, rate);
            }
        }

        internal bool Contains(string name) { return timelines.ContainsKey(name); }

        internal void Prepare(string name)
        {
            string[] timeline;
            if (timelines.TryGetValue(name, out timeline))
            {
                lock (cacheGate) preparedTimeline = timeline;
                PrepareAhead(timeline, 0, name == "run-left" || name == "run-right", rates[name]);
            }
        }

        internal bool TryGet(string name, double elapsed, out BitmapSource image)
        {
            image = null;
            string[] timeline;
            if (!timelines.TryGetValue(name, out timeline)) return false;
            bool loop = name == "run-left" || name == "run-right";
            double frame = Math.Max(0, elapsed) * rates[name];
            long cycle = loop ? (long)Math.Floor(frame / timeline.Length) : 0;
            if (loop) frame %= timeline.Length;
            // 循环帧表示完整曝光区间；四舍五入会让首帧减半、尾帧多停半帧。
            int index = Math.Max(0, Math.Min(timeline.Length - 1,
                (int)(heldCels || loop ? Math.Floor(frame + 1e-7) : Math.Round(frame))));
            string key = timeline[index];
            int step = 1;
            SampleTime = elapsed;
            if (Realtime)
            {
                lock (cacheGate)
                {
                    if (ReferenceEquals(displayedTimeline, timeline) && elapsed > displayedElapsed)
                        step = Math.Max(1, Math.Min(8, (int)Math.Round((elapsed - displayedElapsed) * rates[name])));
                    // 动作切换、显式重播或循环回绕，都不能沿用上一段播放的旧姿势。
                    bool restarted = !ReferenceEquals(displayedTimeline, timeline) || ReferenceEquals(preparedTimeline, timeline)
                        || cycle != displayedCycle || elapsed < displayedElapsed || index < previousIndex;
                    // 连续奔跑的自然回绕可以短暂保留末帧，不必闪回站立姿势。
                    bool nextCycle = loop && ReferenceEquals(displayedTimeline, timeline)
                        && !ReferenceEquals(preparedTimeline, timeline) && cycle == displayedCycle + 1 && elapsed >= displayedElapsed;
                    if (restarted)
                    {
                        if (!nextCycle) { displayedImage = null; displayedSample = 0; }
                        displayedIndex = -1;
                    }
                    if (ReferenceEquals(preparedTimeline, timeline)) preparedTimeline = null;
                    displayedTimeline = timeline;
                    displayedElapsed = elapsed;
                    displayedCycle = cycle;
                    previousIndex = index;
                    if (cache.TryGetValue(key, out image))
                    {
                        order.Remove(positions[key]); order.AddLast(positions[key]);
                        if (index >= displayedIndex) { displayedImage = image; displayedIndex = index; displayedSample = elapsed; }
                    }
                    else
                    {
                        // 解码略慢时，取本段播放已经走过且比当前显示更新的最近画面。
                        // 只等精确索引会一直追着过期请求跑，冷启动时可能全程不首显。
                        for (int previous = index - 1; previous > displayedIndex; previous--)
                        {
                            string ready = timeline[previous];
                            if (!cache.TryGetValue(ready, out image)) continue;
                            order.Remove(positions[ready]); order.AddLast(positions[ready]);
                            displayedImage = image;
                            displayedIndex = previous;
                            displayedSample = (cycle * timeline.Length + previous) / rates[name];
                            break;
                        }
                        if (image == null) image = displayedImage;
                        SampleTime = displayedSample;
                    }
                    if (image == null) SampleTime = 0;
                }
            }
            else image = Load(key, archive, pixels, true);
            PrepareAhead(timeline, index, loop, rates[name], step);
            return true;
        }

        private BitmapSource Load(string key, ZipArchive sourceArchive, byte[] buffer, bool touch)
        {
            BitmapSource image;
            lock (cacheGate)
                if (cache.TryGetValue(key, out image))
                {
                    if (touch) { order.Remove(positions[key]); order.AddLast(positions[key]); }
                    return image;
                }
            ZipArchiveEntry entry = sourceArchive.GetEntry("frames/" + key + ".bgra");
            if (entry == null || entry.Length != buffer.Length) throw new InvalidDataException("动作画面损坏：" + key);
            using (Stream source = entry.Open())
            {
                int read = 0;
                while (read < buffer.Length)
                {
                    int count = source.Read(buffer, read, buffer.Length - read);
                    if (count == 0) throw new EndOfStreamException("动作画面没有读取完整：" + key);
                    read += count;
                }
            }
            image = BitmapSource.Create(SourceArtwork.Width, SourceArtwork.Height, 96 * SourceArtwork.Scale, 96 * SourceArtwork.Scale,
                PixelFormats.Pbgra32, null, buffer, SourceArtwork.Width * 4);
            image.Freeze();
            lock (cacheGate)
            {
                BitmapSource existing;
                if (cache.TryGetValue(key, out existing))
                {
                    if (touch) { order.Remove(positions[key]); order.AddLast(positions[key]); }
                    return existing;
                }
                if (cache.Count >= CacheFrames)
                {
                    string oldest = order.First.Value;
                    order.RemoveFirst(); cache.Remove(oldest); positions.Remove(oldest);
                }
                cache.Add(key, image); positions.Add(key, order.AddLast(key));
            }
            return image;
        }

        private void PrepareAhead(string[] timeline, int index, bool loop, double rate, int step = 1)
        {
            lock (cacheGate)
            {
                requestedTimeline = timeline; requestedIndex = index; requestedLoop = loop;
                requestedStep = step;
                // 按时间覆盖未来画面，长曝光也会提前读到下一个姿势。
                // 包含当前帧后，总窗口不能超过缓存，否则满窗会互相驱逐并反复读取。
                requestedAhead = Math.Min(timeline.Length - 1,
                    Math.Min(CacheFrames - 1, Math.Max(16, (int)Math.Ceiling(rate * .40))));
                if (preparing) return;
                if (NextMissing() == null) return;
                preparing = true;
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    // 后台有独立压缩包读取位置，不与界面线程争用同一个流。
                    if (aheadArchive == null)
                        aheadArchive = new ZipArchive(Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName), ZipArchiveMode.Read);
                    while (true)
                    {
                        string next;
                        lock (cacheGate)
                        {
                            // 每读完一帧重新查看最新动作，切换时最多只多读旧动作的一帧。
                            next = NextMissing();
                            if (next == null) { preparing = false; return; }
                        }
                        Load(next, aheadArchive, aheadPixels, false);
                    }
                }
                catch (InvalidDataException) { lock (cacheGate) preparing = false; }
                catch (IOException) { lock (cacheGate) preparing = false; }
            });
        }

        // 调用方持有 cacheGate；重复曝光已在缓存中时直接跳过。
        private string NextMissing()
        {
            // 先准备接下来几次呈现会用到的采样，再填中间帧。
            // 60Hz屏幕播放120Hz素材时，不能总被不会显示的奇数帧挤占解压时间。
            int urgent = Math.Min(requestedAhead, requestedStep * 6);
            for (int offset = 0; offset <= urgent; offset += requestedStep)
            {
                int next = requestedIndex + offset;
                if (requestedLoop) next %= requestedTimeline.Length;
                else if (next >= requestedTimeline.Length) break;
                string key = requestedTimeline[next];
                if (!cache.ContainsKey(key)) return key;
            }
            for (int offset = 0; offset <= requestedAhead; offset++)
            {
                int next = requestedIndex + offset;
                if (requestedLoop) next %= requestedTimeline.Length;
                else if (next >= requestedTimeline.Length) break;
                string key = requestedTimeline[next];
                if (!cache.ContainsKey(key)) return key;
            }
            return null;
        }
    }
}
