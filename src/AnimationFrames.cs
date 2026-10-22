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
        private readonly Queue<string> order = new Queue<string>();
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

        internal bool TryGet(string name, double elapsed, out BitmapSource image)
        {
            image = null;
            string[] timeline;
            if (!timelines.TryGetValue(name, out timeline)) return false;
            double frame = elapsed * rates[name];
            int index = Math.Max(0, Math.Min(timeline.Length - 1,
                (int)(heldCels ? Math.Floor(frame + 1e-7) : Math.Round(frame))));
            string key = timeline[index];
            image = Load(key, archive);
            PrepareAhead(timeline, index, name == "run-left" || name == "run-right");
            return true;
        }

        private BitmapSource Load(string key, ZipArchive sourceArchive)
        {
            BitmapSource image;
            lock (cacheGate) if (cache.TryGetValue(key, out image)) return image;
            ZipArchiveEntry entry = sourceArchive.GetEntry("frames/" + key + ".bgra");
            int length = SourceArtwork.Width * SourceArtwork.Height * 4;
            if (entry == null || entry.Length != length) throw new InvalidDataException("动作画面损坏：" + key);
            byte[] pixels = new byte[length];
            using (Stream source = entry.Open())
            {
                int read = 0;
                while (read < pixels.Length)
                {
                    int count = source.Read(pixels, read, pixels.Length - read);
                    if (count == 0) throw new EndOfStreamException("动作画面没有读取完整：" + key);
                    read += count;
                }
            }
            image = BitmapSource.Create(SourceArtwork.Width, SourceArtwork.Height, 96 * SourceArtwork.Scale, 96 * SourceArtwork.Scale,
                PixelFormats.Pbgra32, null, pixels, SourceArtwork.Width * 4);
            image.Freeze();
            lock (cacheGate)
            {
                BitmapSource existing;
                if (cache.TryGetValue(key, out existing)) return existing;
                if (cache.Count >= CacheFrames) cache.Remove(order.Dequeue());
                cache.Add(key, image); order.Enqueue(key);
            }
            return image;
        }

        private void PrepareAhead(string[] timeline, int index, bool loop)
        {
            lock (cacheGate)
            {
                if (preparing) return;
                bool missing = false;
                for (int offset = 1; offset <= AheadFrames; offset++)
                {
                    int next = index + offset;
                    if (loop) next %= timeline.Length;
                    else if (next >= timeline.Length) break;
                    if (!cache.ContainsKey(timeline[next])) { missing = true; break; }
                }
                if (!missing) return;
                preparing = true;
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    // 后台有独立压缩包读取位置，不与界面线程争用同一个流。
                    if (aheadArchive == null)
                        aheadArchive = new ZipArchive(Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName), ZipArchiveMode.Read);
                    for (int offset = 1; offset <= AheadFrames; offset++)
                    {
                        int next = index + offset;
                        if (loop) next %= timeline.Length;
                        else if (next >= timeline.Length) break;
                        Load(timeline[next], aheadArchive);
                    }
                }
                catch (InvalidDataException) { /* 当前帧仍由主读取路径正常处理素材错误。 */ }
                catch (IOException) { /* 预读失败不阻断当前动作，下一帧走主读取路径。 */ }
                finally { lock (cacheGate) preparing = false; }
            });
        }
    }
}
