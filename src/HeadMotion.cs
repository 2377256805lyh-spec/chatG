using System;
using System.IO;
using System.Reflection;

namespace XiaoGDesktop
{
    // 使用原画的侧脸和俯仰姿势补帧；固定身体，避免用整块平移冒充转头。
    internal sealed class HeadMotion
    {
        private const int W = 192, MotionHeight = 144;
        private readonly byte[] neutral, reference;
        private readonly byte[][] poses = new byte[16][];
        private readonly MotionPair[] around = new MotionPair[16];

        internal HeadMotion(byte[] rest, byte[][] originals, byte[] highRest, byte[][] highOriginals)
        {
            reference = rest; neutral = highRest;
            for (int i = 0; i < 16; i++)
                Attach(originals[i], highOriginals[i], out poses[i]);
            float[][] flow = LoadFlow();
            for (int i = 0; i < 16; i++)
                around[i] = new MotionPair(flow[i * 2], flow[i * 2 + 1], neutral, poses[i], poses[(i + 1) % 16]);
        }

        private static float[][] LoadFlow()
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("gaze-flow.bin"))
            {
                if (stream == null) throw new InvalidDataException("缺少转头细节位移。");
                using (BinaryReader reader = new BinaryReader(stream))
                {
                    if (reader.ReadUInt32() != 0x32484758 || reader.ReadInt32() != W
                        || reader.ReadInt32() != MotionHeight || reader.ReadInt32() != 32
                        || stream.Length != 16 + 32 * W * MotionHeight * 2 * sizeof(float))
                        throw new InvalidDataException("转头细节位移格式不正确。");
                    float[][] flow = new float[32][];
                    for (int i = 0; i < flow.Length; i++)
                    {
                        flow[i] = new float[W * MotionHeight * 2];
                        for (int p = 0; p < flow[i].Length; p++)
                        {
                            float value = reader.ReadSingle();
                            if (float.IsNaN(value) || float.IsInfinity(value) || Math.Abs(value) > 48)
                                throw new InvalidDataException("转头细节位移超出有效范围。");
                            flow[i][p] = value;
                        }
                    }
                    return flow;
                }
            }
        }

        internal byte[] Render(int direction, int strength)
        {
            return RenderAmount(direction, strength / (double)FrameComposer.StrengthSteps);
        }

        // 有限动作按连续强度提前绘制，避免点头和摇头只能逐级切换。
        internal byte[] RenderAmount(int direction, double strength)
        {
            int divisions = FrameComposer.DirectionCount / 16;
            int first = direction / divisions, second = (first + 1) % 16;
            double between = (direction % divisions) / (double)divisions;
            byte[] result = around[first].Morph(neutral, poses[first], poses[second], between, Math.Max(0, Math.Min(1, strength)));
            // 过渡使用曲线边界，肩膀和翅膀处始终取待机图，不在脖子处横切一刀。
            for (int y = 110 * SourceArtwork.Scale; y < MotionHeight * SourceArtwork.Scale; y++)
                for (int x = 0; x < SourceArtwork.Width; x++)
                {
                    double weight = HeadWeight(x / (double)SourceArtwork.Scale, y / (double)SourceArtwork.Scale);
                    int p = (y * SourceArtwork.Width + x) * 4;
                    for (int c = 0; c < 4; c++) result[p + c] = Blend(neutral[p + c], result[p + c], weight);
                }
            return result;
        }

        private void Attach(byte[] original, byte[] highOriginal, out byte[] high)
        {
            byte[] neutral = this.reference;
            // 用衣领中央的小区域找位置，保留头部相对脖子的朝向和下巴变化。
            int offsetX = 0, offsetY = 0;
            double best = double.MaxValue;
            for (int dy = -10; dy <= 10; dy++)
                for (int dx = -5; dx <= 5; dx++)
                {
                    double error = (dx * dx + dy * dy) * 180;
                    for (int y = 124; y < 138; y++)
                        for (int x = 85; x < 102; x++)
                        {
                            int p = (y * W + x) * 4, q = ((y + dy) * W + x + dx) * 4;
                            for (int c = 0; c < 3; c++)
                            {
                                double difference = original[q + c] - neutral[p + c];
                                error += difference * difference;
                            }
                        }
                    if (error < best) { best = error; offsetX = dx; offsetY = dy; }
                }
            double[] reference = HairColor(neutral), source = HairColor(original);
            high = (byte[])this.neutral.Clone();
            for (int y = 0; y < MotionHeight * SourceArtwork.Scale; y++)
                for (int x = 0; x < SourceArtwork.Width; x++)
                {
                    double weight = HeadWeight(x / (double)SourceArtwork.Scale, y / (double)SourceArtwork.Scale);
                    int p = (y * SourceArtwork.Width + x) * 4;
                    double sx = x + offsetX * SourceArtwork.Scale, sy = y + offsetY * SourceArtwork.Scale;
                    double alpha = SampleHigh(highOriginal, sx, sy, 3);
                    for (int c = 0; c < 4; c++)
                    {
                        double value = c == 3 ? alpha : SampleHigh(highOriginal, sx, sy, c);
                        if (c < 3) value = Math.Max(0, Math.Min(alpha, value + (reference[c] - source[c]) * alpha / 255.0));
                        high[p + c] = Blend(this.neutral[p + c], value, weight);
                    }
                }
        }

        private static double[] HairColor(byte[] image)
        {
            double[] mean = new double[3];
            int count = 0;
            for (int y = 42; y < 112; y++)
                for (int x = 48; x < 144; x++)
                {
                    int p = (y * W + x) * 4;
                    int b = image[p], g = image[p + 1], r = image[p + 2];
                    if (image[p + 3] < 245 || Math.Min(b, Math.Min(g, r)) < 145 || Math.Max(b, Math.Max(g, r)) - Math.Min(b, Math.Min(g, r)) > 35) continue;
                    for (int c = 0; c < 3; c++) mean[c] += image[p + c];
                    count++;
                }
            for (int c = 0; c < 3; c++) mean[c] = count > 0 ? mean[c] / count : 205;
            return mean;
        }

        private static double HeadWeight(double x, double y)
        {
            if (y <= 110) return 1;
            double left = (x - 68) / 13.0, right = (x - 124) / 13.0, neck = (x - 95) / 20.0;
            double bottom = 116 + 16 * Math.Exp(-left * left) + 16 * Math.Exp(-right * right) + 12 * Math.Exp(-neck * neck);
            double value = Math.Max(0, Math.Min(1, (bottom - y) / 8));
            return value * value * (3 - 2 * value);
        }

        private static byte Blend(double a, double b, double amount)
        {
            return (byte)Math.Max(0, Math.Min(255, Math.Round(a + (b - a) * amount)));
        }

        private static double SampleHigh(byte[] image, double x, double y, int channel)
        {
            int width = SourceArtwork.Width, height = SourceArtwork.Height;
            if (x < 0 || y < 0 || x >= width - 1 || y >= height - 1) return 0;
            int ix = (int)x, iy = (int)y, p = (iy * width + ix) * 4 + channel;
            double tx = x - ix, ty = y - iy;
            return (image[p] * (1 - tx) + image[p + 4] * tx) * (1 - ty) + (image[p + width * 4] * (1 - tx) + image[p + width * 4 + 4] * tx) * ty;
        }

        private sealed class MotionPair
        {
            private readonly Point[] first, second, rest;
            private readonly int[][] triangles;

            internal MotionPair(float[] a, float[] b, byte[] restImage, byte[] firstImage, byte[] secondImage)
            {
                // 每3个原图像素放一个对应点，龙角、发饰、眼睑和下巴都有独立对应位置。
                const int step = 3, columns = 65, rows = 49;
                first = new Point[columns * rows]; second = new Point[first.Length]; rest = new Point[first.Length];
                for (int y = 0; y < rows; y++)
                    for (int x = 0; x < columns; x++)
                    {
                        int px = Math.Min(W - 1, x * step), py = Math.Min(MotionHeight - 1, y * step);
                        int i = y * columns + x, p = (py * W + px) * 2;
                        double weight = HeadWeight(px, py);
                        first[i] = new Point(px, py);
                        rest[i] = new Point(px + a[p] * weight, py + a[p + 1] * weight);
                        second[i] = new Point(px + b[p] * weight, py + b[p + 1] * weight);
                    }
                var entries = new System.Collections.Generic.List<int[]>();
                for (int y = 0; y < rows - 1; y++)
                    for (int x = 0; x < columns - 1; x++)
                    {
                        int i = y * columns + x;
                        foreach (int[] triangle in new int[][] {
                            new int[] { i, i + 1, i + columns },
                            new int[] { i + 1, i + columns + 1, i + columns } })
                        {
                            // 三张原画都透明的网格不必逐像素绘制；边缘留一个取样像素的余量。
                            if (HasPixels(restImage, rest, triangle) || HasPixels(firstImage, first, triangle)
                                || HasPixels(secondImage, second, triangle)) entries.Add(triangle);
                        }
                    }
                triangles = entries.ToArray();
            }

            private static bool HasPixels(byte[] image, Point[] mesh, int[] triangle)
            {
                Point a = mesh[triangle[0]], b = mesh[triangle[1]], c = mesh[triangle[2]];
                int width = SourceArtwork.Width, scale = SourceArtwork.Scale;
                int left = Math.Max(0, (int)Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X)) * scale) - 1);
                int right = Math.Min(width - 1, (int)Math.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X)) * scale) + 1);
                int top = Math.Max(0, (int)Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y)) * scale) - 1);
                int bottom = Math.Min(SourceArtwork.Height - 1, (int)Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y)) * scale) + 1);
                for (int y = top; y <= bottom; y++)
                    for (int x = left; x <= right; x++) if (image[(y * width + x) * 4 + 3] != 0) return true;
                return false;
            }

            internal byte[] Morph(byte[] neutral, byte[] a, byte[] b, double amount, double strength)
            {
                Point[] target = new Point[first.Length];
                for (int i = 0; i < first.Length; i++)
                    target[i] = Interpolate(rest[i], Interpolate(first[i], second[i], amount), strength);
                // 三张原画先各自对齐到同一个中间姿势，再连续交接细节。
                // 直接在0.5处换纹理，即使网格位置连续，龙角和发丝仍会突然跳变。
                double restWeight = 1 - strength, firstWeight = strength * (1 - amount), secondWeight = strength * amount;
                byte[] restPixel = new byte[4], firstPixel = new byte[4], secondPixel = new byte[4];
                byte[] result = (byte[])neutral.Clone();
                // 先清空旧头部，防止轮廓移开后在未绘制区域留下待机头部。
                Array.Clear(result, 0, SourceArtwork.Width * MotionHeight * SourceArtwork.Scale * 4);
                int width = SourceArtwork.Width, scale = SourceArtwork.Scale;
                foreach (int[] triangle in triangles)
                {
                    int ia = triangle[0], ib = triangle[1], ic = triangle[2];
                    Point pa = target[ia], pb = target[ib], pc = target[ic];
                    double divisor = (pb.Y-pc.Y)*(pa.X-pc.X)+(pc.X-pb.X)*(pa.Y-pc.Y);
                    if (Math.Abs(divisor) < 0.0001) continue;
                    int minX = Math.Max(0,(int)Math.Floor(Math.Min(pa.X,Math.Min(pb.X,pc.X))*scale));
                    int maxX = Math.Min(width-1,(int)Math.Ceiling(Math.Max(pa.X,Math.Max(pb.X,pc.X))*scale));
                    int minY = Math.Max(0,(int)Math.Floor(Math.Min(pa.Y,Math.Min(pb.Y,pc.Y))*scale));
                    int maxY = Math.Min(MotionHeight*scale-1,(int)Math.Ceiling(Math.Max(pa.Y,Math.Max(pb.Y,pc.Y))*scale));
                    double stepU = (pb.Y - pc.Y) / (divisor * scale), stepV = (pc.Y - pa.Y) / (divisor * scale);
                    for (int y = minY; y <= maxY; y++)
                    {
                        double px = (minX + .5) / scale - .5, py = (y + .5) / scale - .5;
                        double u = ((pb.Y-pc.Y)*(px-pc.X)+(pc.X-pb.X)*(py-pc.Y))/divisor;
                        double v = ((pc.Y-pa.Y)*(px-pc.X)+(pa.X-pc.X)*(py-pc.Y))/divisor;
                        for (int x = minX; x <= maxX; x++, u += stepU, v += stepV)
                        {
                            double w = 1-u-v;
                            if (u < -0.000001 || v < -0.000001 || w < -0.000001) continue;
                            if (restWeight > 0) SamplePose(neutral, rest, ia, ib, ic, u, v, w, restPixel);
                            if (firstWeight > 0) SamplePose(a, first, ia, ib, ic, u, v, w, firstPixel);
                            if (secondWeight > 0) SamplePose(b, second, ia, ib, ic, u, v, w, secondPixel);
                            int output = (y * width + x) * 4;
                            for (int c = 0; c < 4; c++)
                                result[output + c] = (byte)(.5 + restPixel[c] * restWeight
                                    + firstPixel[c] * firstWeight + secondPixel[c] * secondWeight);
                        }
                    }
                }
                return result;
            }

            private static void SamplePose(byte[] texture, Point[] mesh, int ia, int ib, int ic, double u, double v, double w, byte[] pixel)
            {
                double sx = (mesh[ia].X * u + mesh[ib].X * v + mesh[ic].X * w + .5) * SourceArtwork.Scale - .5;
                double sy = (mesh[ia].Y * u + mesh[ib].Y * v + mesh[ic].Y * w + .5) * SourceArtwork.Scale - .5;
                pixel[0] = pixel[1] = pixel[2] = pixel[3] = 0;
                SourceArtwork.Sample(texture, SourceArtwork.Width, SourceArtwork.Height, sx, sy, pixel, 0);
            }

            private static Point Interpolate(Point a, Point b, double amount)
            {
                return new Point(a.X+(b.X-a.X)*amount,a.Y+(b.Y-a.Y)*amount);
            }

            private struct Point
            {
                internal double X,Y;
                internal Point(double x,double y) {X=x;Y=y;}
            }
        }
    }
}
