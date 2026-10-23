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
        private const int CacheFrames = 96, AheadFrames = 16;
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
        private bool preparing;

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

        internal bool TryGet(string name, double elapsed, out BitmapSource image)
        {
            image = null;
            string[] timeline;
            if (!timelines.TryGetValue(name, out timeline)) return false;
            bool loop = name == "run-left" || name == "run-right";
            double frame = Math.Max(0, elapsed) * rates[name];
            // 循环帧表示完整曝光区间；四舍五入会让首帧减半、尾帧多停半帧。
            int index = Math.Max(0, Math.Min(timeline.Length - 1,
                (int)(heldCels || loop ? Math.Floor(frame + 1e-7) : Math.Round(frame))));
            string key = timeline[index];
            image = Load(key, archive, pixels, true);
            PrepareAhead(timeline, index, loop);
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

        private void PrepareAhead(string[] timeline, int index, bool loop)
        {
            lock (cacheGate)
            {
                requestedTimeline = timeline; requestedIndex = index; requestedLoop = loop;
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
            for (int offset = 1; offset <= AheadFrames; offset++)
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
