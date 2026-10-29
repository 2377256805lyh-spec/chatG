using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace XiaoGDesktop
{
    // 肢体前景单独播放，固定身体保留原来的脸、脖子与服装细节。
    internal sealed class ExtraGestureArtwork
    {
        private readonly AnimationFrames arms = new AnimationFrames("extra-gestures.zip");
        private readonly Dictionary<string, BlinkSurface> bodies = new Dictionary<string, BlinkSurface>();

        internal ExtraGestureArtwork(BlinkSequence blinks)
        {
            int width = SourceArtwork.Width, height = SourceArtwork.Height, stride = width * 4;
            foreach (string name in new string[] { "sleepy", "idea" })
            {
                BitmapImage image = new BitmapImage();
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("artwork.extra-" + name + "-body.png"))
                {
                    if (stream == null) throw new InvalidDataException("缺少新动作身体底图：" + name);
                    image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit();
                }
                if (image.PixelWidth != width || image.PixelHeight != height) throw new InvalidDataException("新动作底图尺寸不正确。");
                byte[] original = new byte[stride * height];
                new FormatConvertedBitmap(image, PixelFormats.Pbgra32, null, 0).CopyPixels(original, stride, 0);
                BitmapSource body = BitmapSource.Create(width, height, 288, 288, PixelFormats.Pbgra32, null, original, stride);
                body.Freeze();
                bodies.Add(name, new BlinkSurface(body, blinks));
            }
        }

        internal BitmapSource Body(string name, int eye) { return bodies[name].Frame(eye); }

        internal void Prepare(string name) { arms.Prepare(name); }

        internal void EnableRealtime() { arms.Realtime = true; }
        internal double SampleTime { get { return arms.SampleTime; } }

        internal BitmapSource Arm(string name, double seconds)
        {
            BitmapSource image;
            if (!arms.TryGet(name, seconds, out image)) throw new InvalidDataException("缺少新动作肢体画面：" + name);
            return image;
        }
    }

    // 只更新眼部矩形的像素；不重绘身体，也不为每个眨眼阶段复制整张高清人物。
    internal sealed class BlinkSequence
    {
        internal const int HalfSteps = 12;
        internal readonly Int32Rect Region = new Int32Rect(63 * 3, 94 * 3, 63 * 3, 22 * 3);
        private readonly byte[][] patches = new byte[HalfSteps * 2 + 1][];

        internal BlinkSequence(BitmapSource neutral, BitmapSource[] cels)
        {
            BitmapSource[] anchors = { neutral, cels[0], cels[1], cels[2], neutral };
            byte[][] source = new byte[anchors.Length][];
            int stride = Region.Width * 4;
            for (int i = 0; i < source.Length; i++)
            {
                source[i] = new byte[stride * Region.Height];
                anchors[i].CopyPixels(Region, source[i], stride, 0);
            }
            for (int phase = 0; phase < patches.Length; phase++)
            {
                double position = phase * 2.0 / HalfSteps;
                int first = Math.Min(3, (int)position);
                double t = position - first;
                patches[phase] = new byte[source[0].Length];
                for (int p = 0; p < patches[phase].Length; p++)
                    patches[phase][p] = (byte)Math.Round(source[first][p] * (1 - t) + source[first + 1][p] * t);
            }
        }

        internal byte[] Patch(int phase) { return patches[phase]; }

        internal static int Sample(double t, double start, double close, double hold, double open)
        {
            t -= start;
            if (t <= 0 || t >= close + hold + open) return 0;
            if (t < close) return (int)Math.Round(t / close * HalfSteps);
            if (t < close + hold) return HalfSteps;
            return HalfSteps + (int)Math.Round((t - close - hold) / open * HalfSteps);
        }
    }

    internal sealed class BlinkSurface
    {
        private readonly BitmapSource neutral;
        private readonly WriteableBitmap surface;
        private readonly BlinkSequence sequence;
        private int phase = -1;

        internal BlinkSurface(BitmapSource original, BlinkSequence sequence)
        {
            neutral = original;
            surface = new WriteableBitmap(original);
            this.sequence = sequence;
        }

        internal BitmapSource Frame(int value)
        {
            value = Math.Max(0, Math.Min(BlinkSequence.HalfSteps * 2, value));
            if (value == 0 || value == BlinkSequence.HalfSteps * 2) return neutral;
            if (phase != value)
            {
                surface.WritePixels(sequence.Region, sequence.Patch(value), sequence.Region.Width * 4, 0);
                phase = value;
            }
            return surface;
        }
    }
}
