using System;
using System.IO;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace XiaoGDesktop
{
    // 手臂与被遮挡的身体分别保存，补帧时不把翅膀、衣服花纹拖进手臂网格。
    internal static class GestureLayers
    {
        private static readonly byte[][] Arms = Load("thinking-arms");
        private static readonly byte[][] Bodies = Load("thinking-backdrops");

        internal static byte[] Arm(int row, int column) { return Arms[row == 0 ? 0 : column + 1]; }
        internal static byte[] Body(int row, int column) { return Bodies[row == 0 ? 0 : column + 1]; }

        internal static byte[] Pose(int row, int column)
        {
            byte[] arm = Arm(row, column), body = (byte[])Body(row, column).Clone();
            for (int p = 0; p < body.Length; p += 4)
            {
                double through = 1 - arm[p + 3] / 255.0;
                for (int c = 0; c < 4; c++) body[p + c] = (byte)Math.Min(255, arm[p + c] + body[p + c] * through + .5);
            }
            return body;
        }

        private static byte[][] Load(string name)
        {
            BitmapImage image = new BitmapImage();
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("artwork." + name + ".png"))
            {
                if (stream == null) throw new InvalidDataException("缺少思考分层原图：" + name);
                image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit();
            }
            int width = SourceArtwork.Width, height = SourceArtwork.Height;
            if (image.PixelWidth != width * 3 || image.PixelHeight != height * 3)
                throw new InvalidDataException("思考分层原图尺寸不正确。");
            FormatConvertedBitmap converted = new FormatConvertedBitmap(image, PixelFormats.Pbgra32, null, 0);
            byte[] all = new byte[width * height * 9 * 4]; converted.CopyPixels(all, width * 3 * 4, 0);
            byte[][] result = new byte[9][];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = new byte[width * height * 4];
                for (int y = 0; y < height; y++)
                    Buffer.BlockCopy(all, (((i / 3) * height + y) * width * 3 + (i % 3) * width) * 4,
                        result[i], y * width * 4, width * 4);
            }
            return result;
        }
    }
}
