using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace XiaoGDesktop
{
    // 肢体前景单独播放，固定身体保留原来的脸、脖子与服装细节。
    internal sealed class ExtraGestureArtwork
    {
        private readonly AnimationFrames arms = new AnimationFrames("extra-gestures.zip", true);
        private readonly Dictionary<string, BitmapSource[]> bodies = new Dictionary<string, BitmapSource[]>();

        internal ExtraGestureArtwork(BitmapSource[] blinks)
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
                BitmapSource[] set = new BitmapSource[4];
                for (int eye = 0; eye < set.Length; eye++)
                {
                    byte[] pixels = (byte[])original.Clone();
                    if (eye > 0)
                    {
                        byte[] blink = new byte[pixels.Length]; blinks[eye - 1].CopyPixels(blink, stride, 0);
                        for (int y = 94 * 3; y < 116 * 3; y++)
                            Buffer.BlockCopy(blink, y * stride + 63 * 3 * 4, pixels, y * stride + 63 * 3 * 4, (126 - 63) * 3 * 4);
                    }
                    set[eye] = BitmapSource.Create(width, height, 288, 288, PixelFormats.Pbgra32, null, pixels, stride);
                    set[eye].Freeze();
                }
                bodies.Add(name, set);
            }
        }

        internal BitmapSource Body(string name, int eye) { return bodies[name][eye]; }

        internal BitmapSource Arm(string name, double seconds)
        {
            BitmapSource image;
            if (!arms.TryGet(name, seconds, out image)) throw new InvalidDataException("缺少新动作肢体画面：" + name);
            return image;
        }
    }
}
