using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace XiaoGDesktop
{
    // 从创作时的原始动作图取细节，旧图集只提供位置标尺。
    internal static class SourceArtwork
    {
        internal const int Scale = 3;
        internal const int Width = 192 * Scale, Height = 208 * Scale;

        internal static byte[,][] Load(byte[,][] reference, int[] counts, bool gazeOnly = false)
        {
            string[] names = { "idle", "running-right", null, null, "jumping", null, "waiting", null, "review", "look-row-9", "look-row-10" };
            byte[,][] result = new byte[11, 8][];
            for (int row = 0; row < names.Length; row++)
            {
                if (gazeOnly && row != 0 && row != 9 && row != 10) continue;
                if (names[row] == null) continue;
                BitmapImage sheet = new BitmapImage();
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("artwork." + names[row] + ".png"))
                {
                    if (stream == null) throw new InvalidDataException("缺少动作原图：" + names[row]);
                    sheet.BeginInit(); sheet.CacheOption = BitmapCacheOption.OnLoad;
                    sheet.StreamSource = stream; sheet.EndInit();
                }
                FormatConvertedBitmap converted = new FormatConvertedBitmap(sheet, PixelFormats.Bgra32, null, 0);
                int sw = sheet.PixelWidth, sh = sheet.PixelHeight;
                byte[] source = new byte[sw * sh * 4];
                converted.CopyPixels(source, sw * 4, 0);
                for (int p = 0; p < source.Length; p += 4)
                {
                    int excess = Math.Max(0, source[p + 1] - Math.Max(source[p], source[p + 2]));
                    int alpha = Math.Max(0, 255 - (int)Math.Round(excess * 255.0 / 240));
                    source[p] = (byte)Math.Min(alpha, source[p]);
                    source[p + 1] = (byte)Math.Max(0, Math.Min(alpha, source[p + 1] - (255 - alpha)));
                    source[p + 2] = (byte)Math.Min(alpha, source[p + 2]);
                    source[p + 3] = (byte)alpha;
                }
                int[] borders = Borders(source, sw, sh, counts[row]);
                for (int col = 0; col < counts[row]; col++)
                {
                    int start = borders[col], end = borders[col + 1];
                    Rect original = Bounds(source, sw, sh, start, end);
                    Rect target = Bounds(reference[row, col], 192, 208, 0, 192);
                    byte[] pixels = new byte[Width * Height * 4];
                    double sx = original.Width / target.Width, sy = original.Height / target.Height;
                    for (int y = 0; y < Height; y++)
                    {
                        double oy = original.Y + ((y + 0.5) / Scale - target.Y) * sy - 0.5;
                        if (oy < original.Y - 2 || oy > original.Bottom + 2) continue;
                        for (int x = 0; x < Width; x++)
                        {
                            double ox = original.X + ((x + 0.5) / Scale - target.X) * sx - 0.5;
                            if (ox < original.X - 2 || ox > original.Right + 2 || ox < start || ox >= end - 1) continue;
                            Sample(source, sw, sh, ox, oy, pixels, (y * Width + x) * 4);
                        }
                    }
                    result[row, col] = pixels;
                }
            }
            for (int col = 0; !gazeOnly && col < 8; col++)
            {
                byte[] left = new byte[Width * Height * 4];
                for (int y = 0; y < Height; y++)
                    for (int x = 0; x < Width; x++)
                        Buffer.BlockCopy(result[1, col], (y * Width + Width - 1 - x) * 4, left, (y * Width + x) * 4, 4);
                result[2, col] = left;
            }
            return result;
        }

        private static int[] Borders(byte[] image, int width, int height, int count)
        {
            int[] mass = new int[width], borders = new int[count + 1];
            borders[count] = width;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++) if (image[(y * width + x) * 4 + 3] >= 128) mass[x]++;
            for (int i = 1; i < count; i++)
            {
                double ideal = width * i / (double)count, best = double.MaxValue;
                int radius = width / count / 3;
                for (int x = Math.Max(borders[i - 1] + 1, (int)ideal - radius); x < Math.Min(width, ideal + radius); x++)
                {
                    double score = mass[x] + 0.002 * (x - ideal) * (x - ideal);
                    if (score < best) { best = score; borders[i] = x; }
                }
            }
            return borders;
        }

        internal static byte[][] LoadSequence(string name, byte[] reference)
        {
            BitmapImage sheet = new BitmapImage();
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("artwork." + name + ".png"))
            {
                if (stream == null) throw new InvalidDataException("缺少补充动作原画：" + name);
                sheet.BeginInit(); sheet.CacheOption = BitmapCacheOption.OnLoad; sheet.StreamSource = stream; sheet.EndInit();
            }
            int sw = sheet.PixelWidth, sh = sheet.PixelHeight / 2;
            FormatConvertedBitmap converted = new FormatConvertedBitmap(sheet, PixelFormats.Pbgra32, null, 0);
            byte[] all = new byte[sw * sheet.PixelHeight * 4]; converted.CopyPixels(all, sw * 4, 0);
            byte[][] rows = new byte[2][], result = new byte[8][];
            Rect[] bounds = new Rect[8]; int[][] borders = new int[2][];
            double tallest = 0;
            for (int row = 0; row < 2; row++)
            {
                rows[row] = new byte[sw * sh * 4]; Buffer.BlockCopy(all, row * rows[row].Length, rows[row], 0, rows[row].Length);
                borders[row] = Borders(rows[row], sw, sh, 4);
                for (int col = 0; col < 4; col++)
                {
                    int i = row * 4 + col;
                    bounds[i] = Bounds(rows[row], sw, sh, borders[row][col], borders[row][col + 1]);
                    tallest = Math.Max(tallest, bounds[i].Height);
                }
            }
            Rect referenceBounds = Bounds(reference, 192, 208, 0, 192);
            double targetFoot = FootCenter(reference, 192, referenceBounds) * Scale;
            double ratio = referenceBounds.Height * Scale / tallest;
            for (int i = 0; i < 8; i++)
            {
                byte[] source = rows[i / 4], pixels = new byte[Width * Height * 4];
                double foot = FootCenter(source, sw, bounds[i]);
                double left = targetFoot - foot * ratio, top = referenceBounds.Bottom * Scale - bounds[i].Bottom * ratio;
                for (int y = 0; y < Height; y++)
                    for (int x = 0; x < Width; x++)
                    {
                        double sx = (x + 0.5 - left) / ratio - 0.5, sy = (y + 0.5 - top) / ratio - 0.5;
                        if (sx < borders[i / 4][i % 4] || sx >= borders[i / 4][i % 4 + 1] - 1) continue;
                        Sample(source, sw, sh, sx, sy, pixels, (y * Width + x) * 4);
                    }
                result[i] = pixels;
            }
            return result;
        }

        private static double FootCenter(byte[] image, int width, Rect bounds)
        {
            double sum = 0, count = 0;
            for (int y = (int)(bounds.Bottom - Math.Max(3, bounds.Height * 0.035)); y < (int)bounds.Bottom; y++)
                for (int x = (int)bounds.Left; x < (int)bounds.Right; x++)
                {
                    double weight = image[(y * width + x) * 4 + 3] / 255.0;
                    sum += (x + 0.5) * weight; count += weight;
                }
            return count > 0 ? sum / count : bounds.X + bounds.Width / 2;
        }

        private static Rect Bounds(byte[] image, int width, int height, int start, int end)
        {
            int left = end, right = start, top = height, bottom = 0;
            for (int y = 0; y < height; y++)
                for (int x = start; x < end; x++)
                    if (image[(y * width + x) * 4 + 3] >= 128)
                    { left = Math.Min(left, x); right = Math.Max(right, x + 1); top = Math.Min(top, y); bottom = Math.Max(bottom, y + 1); }
            if (right <= left || bottom <= top) throw new InvalidDataException("动作原图中没有找到人物。");
            return new Rect(left, top, right - left, bottom - top);
        }

        internal static void Sample(byte[] image, int width, int height, double x, double y, byte[] result, int output, bool preserveTransparent = false)
        {
            if (x < 0 || y < 0 || x >= width - 1 || y >= height - 1) return;
            int ix = (int)x, iy = (int)y, p = (iy * width + ix) * 4;
            if ((image[p + 3] | image[p + 7] | image[p + width * 4 + 3] | image[p + width * 4 + 7]) == 0)
            {
                if (!preserveTransparent) { result[output] = result[output + 1] = result[output + 2] = result[output + 3] = 0; }
                return;
            }
            double tx = x - ix, ty = y - iy;
            for (int c = 0; c < 4; c++)
                result[output + c] = (byte)(0.5 +
                    (image[p + c] * (1 - tx) + image[p + 4 + c] * tx) * (1 - ty) +
                    (image[p + width * 4 + c] * (1 - tx) + image[p + width * 4 + 4 + c] * tx) * ty);
        }
    }
}
