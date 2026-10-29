using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace XiaoGDesktop
{
    internal sealed class FrameComposer
    {
        private const int W = 192, H = 208, PoseColumns = 32;
        internal const int DirectionCount = 512, StrengthSteps = 48;
        internal readonly BitmapSource[,] Frames = new BitmapSource[11, PoseColumns];
        private readonly byte[,][] raw = new byte[11, PoseColumns][];
        private readonly BitmapSource[] blinks = new BitmapSource[3];
        private readonly BlinkSurface idleBlink;
        private readonly BitmapSource[] thinkingBlinks = new BitmapSource[4];
        private readonly HeadMotion headMotion;
        private readonly byte[] restPixels;
        private readonly AnimationFrames animationFrames = new AnimationFrames();
        private readonly ExtraGestureArtwork extraGestures;
        private GazeRenderer realtimeGaze;
        private readonly Dictionary<int, BitmapSource> cache = new Dictionary<int, BitmapSource>();
        private readonly Queue<int> cacheOrder = new Queue<int>();
        private readonly Dictionary<int, PoseMotion> motions = new Dictionary<int, PoseMotion>();
        private readonly Queue<int> motionOrder = new Queue<int>();

        internal FrameComposer(BitmapSource sheet) : this(sheet, false) { }

        internal FrameComposer(BitmapSource sheet, bool preferPreparedFrames)
        {
            bool compact = preferPreparedFrames;
            foreach (string name in new string[] { "run-right", "run-left", "jump", "tiptoe", "thinking", "failed", "waiting", "review", "shake" })
                if (!animationFrames.Contains(name)) compact = false;
            int[] counts = { 6, 8, 8, 4, 5, 8, 6, 6, 6, 8, 8 };
            for (int row = 0; row < counts.Length; row++)
            {
                if (compact && row != 0 && row != 9 && row != 10) continue;
                for (int col = 0; col < counts[row]; col++)
                {
                    BitmapSource crop = new CroppedBitmap(sheet, new Int32Rect(col * W, row * H, W, H));
                    FormatConvertedBitmap converted = new FormatConvertedBitmap(crop, PixelFormats.Pbgra32, null, 0);
                    byte[] bytes = new byte[W * H * 4];
                    converted.CopyPixels(bytes, W * 4, 0); raw[row, col] = bytes;
                }
            }
            // 正式运行已有完整帧包，不再重复生成只供素材制作/缺帧回退使用的原画。
            for (int col = 0; !compact && col < 8; col++) raw[2, col] = Mirror(raw[1, col], W, H);
            byte[,][] high = SourceArtwork.Load(raw, counts, compact);
            byte[] neutral = high[0, 0];
            restPixels = neutral;
            byte[][] looks = new byte[16][], highLooks = new byte[16][];
            for (int i = 0; i < 16; i++) { looks[i] = raw[9 + i / 8, i % 8]; highLooks[i] = high[9 + i / 8, i % 8]; }
            headMotion = new HeadMotion(raw[0, 0], looks, neutral, highLooks);
            // 跳跃姿势先统一脚底，抛物线只由窗口的位置变换负责。
            int[] baselines = { 199, 181, 162, 185, 199 };
            for (int col = 0; !compact && col < 5; col++)
            {
                raw[4, col] = Shift(raw[4, col], W, H, 198 - baselines[col]);
                high[4, col] = Shift(high[4, col], SourceArtwork.Width, SourceArtwork.Height, (198 - baselines[col]) * SourceArtwork.Scale);
            }
            for (int row = 0; row < counts.Length; row++)
                for (int col = 0; col < counts[row]; col++)
                    if (high[row, col] != null && (!compact || row == 0 && col == 0)) Frames[row, col] = Bitmap(high[row, col]);
            if (!compact) foreach (int row in new int[] { 5, 7 })
            {
                byte[][] sequence = SourceArtwork.LoadSequence(row == 5 ? "failed-smooth" : "thinking-coordinated", raw[0, 0]);
                for (int col = 0; col < sequence.Length; col++)
                {
                    Frames[row, col] = Bitmap(row == 7 ? GestureLayers.Pose(row, col) : sequence[col]);
                    raw[row, col] = Reduce(sequence[col]);
                }
            }
            if (!compact) CreateThinkingBlinks();
            // 眨眼只替换眼睛附近，身体不参与重绘或缩放。
            int scale = SourceArtwork.Scale, width = SourceArtwork.Width;
            for (int phase = 0; phase < 3; phase++)
            {
                byte[] result = (byte[])neutral.Clone(), eyelids = high[0, phase + 2];
                for (int y = 94 * scale; y < 116 * scale; y++)
                    for (int x = 63 * scale; x < 126 * scale; x++)
                    {
                        double weight = Math.Min(1, Math.Min(Math.Min(x - 63 * scale, 126 * scale - 1 - x), Math.Min(y - 94 * scale, 116 * scale - 1 - y)) / (2.0 * scale));
                        int p = (y * width + x) * 4;
                        for (int c = 0; c < 4; c++) result[p + c] = Mix(neutral[p + c], eyelids[p + c], weight);
                    }
                blinks[phase] = Bitmap(result);
            }
            BlinkSequence blinkSequence = new BlinkSequence(Frames[0, 0], blinks);
            idleBlink = new BlinkSurface(Frames[0, 0], blinkSequence);
            extraGestures = new ExtraGestureArtwork(blinkSequence);
        }

        internal BitmapSource Blink(int phase) { return blinks[phase]; }
        internal BitmapSource BlinkAt(double seconds)
        {
            return idleBlink.Frame(BlinkSequence.Sample(seconds, 0, .08, .08, .10));
        }
        internal BitmapSource RestEyes(double closure)
        {
            return idleBlink.Frame((int)Math.Round(Math.Max(0, Math.Min(1, closure)) * BlinkSequence.HalfSteps));
        }
        internal double AnimationTime { get { return animationFrames.SampleTime; } }
        internal double ExtraArmTime { get { return extraGestures.SampleTime; } }
        internal BitmapSource ExtraBody(string name, int eye) { return extraGestures.Body(name, eye); }
        internal BitmapSource ExtraArm(string name, double seconds) { return extraGestures.Arm(name, seconds); }

        // 只有实际桌面窗口启用；生产导出器仍逐张同步取图，不能把缺帧兜底画面导出成素材。
        internal void EnableRealtime()
        {
            animationFrames.Realtime = true;
            extraGestures.EnableRealtime();
            realtimeGaze = new GazeRenderer(headMotion, Frames[0, 0]);
        }

        internal void ResetGaze()
        {
            if (realtimeGaze != null) realtimeGaze.Reset();
        }

        internal void PrepareAnimation(string name)
        {
            if (ExtraActions.Duration(name) > 0) extraGestures.Prepare(name);
            else animationFrames.Prepare(name);
        }

        internal BitmapSource Animation(string name, double elapsed)
        {
            bool running = name == "run-right" || name == "run-left";
            if (!running)
            {
                double duration = name == "tiptoe" ? 1.80 : name == "thinking" ? 4.40 : name == "failed" ? 4.10
                    : name == "waiting" ? 4.84 : name == "review" ? 5.06 : name == "jump" ? .95 : name == "shake" ? 2.60 : 0;
                if (elapsed >= duration) return Frames[0, 0];
            }
            BitmapSource prepared;
            if (animationFrames.TryGet(name, elapsed, out prepared)) return prepared ?? Frames[0, 0];
            if (name == "thinking") return Thinking(elapsed);
            if (name == "tiptoe") return Tiptoe(elapsed);
            if (running)
            {
                elapsed = Math.Max(0, elapsed) % .68;
                int row = name == "run-right" ? 1 : 2;
                double position = elapsed / .085;
                int pose = (int)position % 8;
                return Motion(row, pose, row, (pose + 1) % 8, position - Math.Floor(position));
            }
            if (name == "jump")
            {
                if (elapsed < .09) return Motion(0, 0, 4, 0, Ease(elapsed / .09));
                if (elapsed < .81) return Jump((elapsed - .09) / .72);
                if (elapsed < .95) return Motion(4, 4, 0, 0, Ease((elapsed - .81) / .14));
                return Frames[0, 0];
            }
            if (name == "waiting" || name == "review")
            {
                int row = name == "waiting" ? 6 : 8;
                double duration = name == "waiting" ? 4.84 : 5.06;
                if (elapsed >= duration) return Frames[0, 0];
                if (elapsed < .16) return Motion(0, 0, row, 0, Ease(elapsed / .16));
                if (elapsed > duration - .20) return Motion(row, 0, 0, 0, Ease((elapsed - duration + .20) / .20));
                double position = (elapsed - .16) * 10 / (duration - .36);
                int step = Math.Min(10, (int)position), next = Math.Min(10, step + 1);
                int a = step < 6 ? step : 10 - step, b = next < 6 ? next : 10 - next;
                return Motion(row, a, row, b, Ease(position - Math.Floor(position)));
            }
            if (name == "failed")
            {
                double[] ends = { .35, .75, 1.20, 1.75, 2.70, 3.25, 3.70, 4.10 };
                int pose = 0;
                while (pose < ends.Length && elapsed >= ends[pose]) pose++;
                if (pose >= ends.Length) return Frames[0, 0];
                if (elapsed < .16) return Motion(0, 0, 5, 0, Ease(elapsed / .16));
                double start = pose == 0 ? 0 : ends[pose - 1];
                double transition = Math.Min(.32, (ends[pose] - start) * .85);
                double amount = Ease((elapsed - ends[pose] + transition) / transition);
                return pose == ends.Length - 1 ? Motion(5, pose, 0, 0, amount) : Motion(5, pose, 5, pose + 1, amount);
            }
            if (name == "shake")
                return Shake(elapsed);
            return Frames[0, 0];
        }

        private static readonly double[] ShakeTimes = { 0, .24, .64, 1.12, 1.64, 2.05, 2.60 };
        private static readonly double[] ShakeYaw = { 0, .10, -.34, .30, -.18, .065, 0 };
        private static readonly double[] ShakePitch = { 0, -.01, .025, .018, .012, .006, 0 };

        internal BitmapSource Shake(double elapsed)
        {
            if (elapsed <= 0 || elapsed >= 2.60) return Frames[0, 0];
            int step = 0;
            while (elapsed > ShakeTimes[step + 1]) step++;
            double t = (elapsed - ShakeTimes[step]) / (ShakeTimes[step + 1] - ShakeTimes[step]);
            // 转向时速度、加速度都平缓归零；左右节奏和幅度不再机械重复。
            t = t * t * t * (t * (t * 6 - 15) + 10);
            double yaw = ShakeYaw[step] + (ShakeYaw[step + 1] - ShakeYaw[step]) * t;
            double pitch = ShakePitch[step] + (ShakePitch[step + 1] - ShakePitch[step]) * t;
            double strength = Math.Sqrt(yaw * yaw + pitch * pitch);
            if (strength < .000001) return Frames[0, 0];
            double direction = (Math.Atan2(yaw, -pitch) / (2 * Math.PI) + 1) * DirectionCount % DirectionCount;
            return StabilizeShake(Bitmap(headMotion.RenderAmount(direction, strength)));
        }

        internal BitmapSource StabilizeShake(BitmapSource frame)
        {
            byte[] pixels = Pixels(frame);
            int scale = SourceArtwork.Scale, width = SourceArtwork.Width;
            // 下巴仍随头转动；下巴下面的颈部、衣领和胸前花结保持待机原画。
            // 顶部留柔和过渡，两侧绕开长发，避免在整个脖子高度横切头发。
            for (int y = 117 * scale; y < 134 * scale; y++)
                for (int x = 76 * scale; x < 115 * scale; x++)
                {
                    double px = (x + .5) / scale, py = (y + .5) / scale;
                    // 下巴中央最低，保护边界顺着下颚弧线向两边抬高，不冻结下巴下沿。
                    double side = Math.Min(1, Math.Abs(px - 95) / 20);
                    double boundary = 120.5 - 3.5 * side * side;
                    double weight = Ease((py - boundary) / 3) * Ease((px - 76) / 6) * Ease((115 - px) / 6);
                    int p = (y * width + x) * 4;
                    for (int c = 0; c < 4; c++) pixels[p + c] = Mix(pixels[p + c], restPixels[p + c], weight);
                }
            return Bitmap(pixels);
        }

        internal BitmapSource Tiptoe(double elapsed)
        {
            // 两次轻轻踮脚；整张脸和身体统一抬升，只有鞋子附近渐进抬起脚跟。
            double phase = elapsed < .88 ? elapsed : elapsed - .88;
            double amount = phase < .12 || phase >= .76 ? 0 : phase < .34 ? Ease((phase - .12) / .22)
                : phase < .44 ? 1 : 1 - Ease((phase - .44) / .32);
            if (amount <= 0) return Frames[0, 0];
            int width = SourceArtwork.Width, height = SourceArtwork.Height, scale = SourceArtwork.Scale;
            double rise = amount * 3 * scale, ankle = 181 * scale, toe = 200 * scale;
            byte[] pixels = new byte[width * height * 4];
            for (int x = 0; x < width; x++)
            {
                double foot = Ease((x / (double)scale - 67) / 7) * Ease((135 - x / (double)scale) / 7);
                for (int y = 0; y < height; y++)
                {
                    // 脚尖不离地；鞋子上方始终是同一个位移，不改变五官的相对位置。
                    double sourceY = y + rise - foot * rise * Ease((y - ankle + rise) / (toe - ankle + rise));
                    SourceArtwork.Sample(restPixels, width, height, x, sourceY, pixels, (y * width + x) * 4);
                }
            }
            return Bitmap(pixels);
        }

        private void CreateThinkingBlinks()
        {
            byte[] held = Pixels(Frames[7, 5]);
            byte[][] eyelids = SourceArtwork.LoadSequence("thinking-blink", raw[7, 5]);
            int scale = SourceArtwork.Scale, left = 62 * scale, top = 84 * scale;
            int width = 65 * scale, height = 28 * scale;
            bool[] hair = new bool[width * height];
            int[] queue = new int[hair.Length];
            int count = 0, read = 0;
            // 从眼区的上沿和侧边找连续的灰白刘海，避免把亮色眼珠当成头发。
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    if (y != 0 && x != 0 && x != width - 1) continue;
                    int i = y * width + x;
                    if (!ThinkingHair(held, ((top + y) * SourceArtwork.Width + left + x) * 4)) continue;
                    hair[i] = true; queue[count++] = i;
                }
            while (read < count)
            {
                int i = queue[read++], x = i % width;
                foreach (int n in new int[] { i - 1, i + 1, i - width, i + width })
                {
                    if (n < 0 || n >= hair.Length || hair[n] || Math.Abs(n % width - x) > 1) continue;
                    int p = ((top + n / width) * SourceArtwork.Width + left + n % width) * 4;
                    if (!ThinkingHair(held, p)) continue;
                    hair[n] = true; queue[count++] = n;
                }
            }
            thinkingBlinks[0] = Frames[7, 5];
            for (int phase = 1; phase < thinkingBlinks.Length; phase++)
            {
                byte[] result = (byte[])held.Clone();
                for (int y = top; y < top + height; y++)
                    for (int x = left; x < left + width; x++)
                    {
                        if (hair[(y - top) * width + x - left]) continue;
                        double px = (x + .5) / scale, py = (y + .5) / scale;
                        double dx = (px - (px < 95 ? 78 : 111)) / 14, dy = (py - 96) / 11;
                        double weight = Math.Max(0, Math.Min(1, (1.10 - Math.Sqrt(dx * dx + dy * dy)) / .15));
                        int p = (y * SourceArtwork.Width + x) * 4;
                        for (int c = 0; c < 4; c++) result[p + c] = Mix(held[p + c], eyelids[phase][p + c], weight);
                    }
                thinkingBlinks[phase] = Bitmap(result);
            }
            Frames[7, 6] = thinkingBlinks[3]; raw[7, 6] = Reduce(Pixels(thinkingBlinks[3]));
        }

        private static bool ThinkingHair(byte[] image, int p)
        {
            int b = image[p], g = image[p + 1], r = image[p + 2];
            return image[p + 3] > 210 && b >= r - 5 && b > g + 2 && r - g < 14
                && Math.Max(b, r) - Math.Min(g, r) < 45;
        }

        internal BitmapSource Gaze(int direction, int strength)
        {
            strength = Math.Max(0, Math.Min(StrengthSteps, strength));
            direction = (direction % DirectionCount + DirectionCount) % DirectionCount;
            if (realtimeGaze != null) return realtimeGaze.Get(direction, strength);
            if (strength == 0) return Frames[0, 0];
            // 注视使用负键，方向加密后也不会与正键的动作补帧缓存相撞。
            int key = -1 - direction * (StrengthSteps + 1) - strength;
            BitmapSource image;
            if (cache.TryGetValue(key, out image)) return image;
            return Remember(key, Bitmap(headMotion.Render(direction, strength)));
        }

        internal BitmapSource Motion(int rowA, int colA, int rowB, int colB, double amount)
        {
            if (amount <= 0 || rowA == rowB && colA == colB) return Frames[rowA, colA];
            if (amount >= 1) return Frames[rowB, colB];
            int a = rowA * PoseColumns + colA, b = rowB * PoseColumns + colB;
            if (a > b) { int r = rowA, c = colA; rowA = rowB; colA = colB; rowB = r; colB = c; int temp = a; a = b; b = temp; amount = 1 - amount; }
            int pair = a * (11 * PoseColumns) + b, phase = (int)Math.Round(amount * 64);
            if (phase <= 0) return Frames[rowA, colA];
            if (phase >= 64) return Frames[rowB, colB];
            int key = 4096 + pair * 64 + phase;
            BitmapSource image;
            if (cache.TryGetValue(key, out image)) return image;
            PoseMotion motion;
            if (!motions.TryGetValue(pair, out motion))
            {
                motion = new PoseMotion(raw[rowA, colA], raw[rowB, colB], Pixels(Frames[rowA, colA]), Pixels(Frames[rowB, colB]), rowA, colA, rowB, colB);
                if (motions.Count >= 12) motions.Remove(motionOrder.Dequeue());
                motions.Add(pair, motion); motionOrder.Enqueue(pair);
            }
            return Remember(key, Bitmap(motion.Render(phase / 64.0)));
        }

        internal BitmapSource Jump(double progress)
        {
            double[] times = { 0, 0.18, 0.50, 0.82, 1 };
            for (int i = 0; i < times.Length - 1; i++)
                if (progress <= times[i + 1])
                    return Motion(4, i, 4, i + 1, Ease((progress - times[i]) / (times[i + 1] - times[i])));
            return Frames[4, 4];
        }

        // 抬手、停留、收手分别安排时间；思考停住手臂，只保留抬眼和短眨眼。
        private static readonly double[] ThinkTimes = { 0, .18, .40, .66, .90, 1.16, 1.62, 2.60, 2.84, 3.12, 3.40, 3.69, 3.99, 4.22, 4.40 };
        private static readonly int[] ThinkPoses = { -1, 0, 1, 2, 3, 4, 5, 5, 7, 4, 3, 2, 1, 0, -1 };

        internal BitmapSource Thinking(double elapsed)
        {
            const int row = 7;
            double[] times = ThinkTimes;
            int[] poses = ThinkPoses;
            if (elapsed <= 0 || elapsed >= times[times.Length - 1]) return Frames[0, 0];
            // 短眨眼直接播放完整眼睑，不让身体网格参与眼睛的开合。
            if (elapsed >= 2.22 && elapsed < 2.48)
            {
                double blink = elapsed - 2.22;
                int phase = blink < .04 ? 1 : blink < .08 ? 2 : blink < .16 ? 3 : blink < .20 ? 2 : blink < .24 ? 1 : 0;
                return thinkingBlinks[phase];
            }
            int step = 0;
            while (elapsed > times[step + 1]) step++;
            int a = poses[step], b = poses[step + 1];
            double amount = (elapsed - times[step]) / (times[step + 1] - times[step]);
            if (a < 0 || b < 0) amount = Ease(amount);
            return Motion(a < 0 ? 0 : row, Math.Max(0, a), b < 0 ? 0 : row, Math.Max(0, b), amount);
        }

        internal static double Ease(double t) { t = Math.Max(0, Math.Min(1, t)); return t * t * (3 - 2 * t); }

        private BitmapSource Remember(int key, BitmapSource image)
        {
            // 高清补帧按需保存，避免把所有方向和所有动作展开成数千张图片。
            if (cache.Count >= 48) cache.Remove(cacheOrder.Dequeue());
            cache.Add(key, image); cacheOrder.Enqueue(key);
            return image;
        }

        private static byte[] Pixels(BitmapSource image)
        {
            byte[] bytes = new byte[SourceArtwork.Width * SourceArtwork.Height * 4];
            image.CopyPixels(bytes, SourceArtwork.Width * 4, 0); return bytes;
        }

        private static byte[] Reduce(byte[] image)
        {
            int scale = SourceArtwork.Scale, width = SourceArtwork.Width;
            byte[] result = new byte[W * H * 4];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    for (int c = 0; c < 4; c++)
                    {
                        int sum = 0;
                        for (int dy = 0; dy < scale; dy++)
                            for (int dx = 0; dx < scale; dx++) sum += image[((y * scale + dy) * width + x * scale + dx) * 4 + c];
                        result[(y * W + x) * 4 + c] = (byte)(sum / (scale * scale));
                    }
            return result;
        }

        private static BitmapSource Bitmap(byte[] bytes)
        {
            BitmapSource image = BitmapSource.Create(SourceArtwork.Width, SourceArtwork.Height, 96 * SourceArtwork.Scale, 96 * SourceArtwork.Scale, PixelFormats.Pbgra32, null, bytes, SourceArtwork.Width * 4);
            image.Freeze(); return image;
        }

        private static byte[] Mirror(byte[] image, int width, int height)
        {
            byte[] result = new byte[image.Length];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++) Buffer.BlockCopy(image, (y * width + width - 1 - x) * 4, result, (y * width + x) * 4, 4);
            return result;
        }

        private static byte[] Shift(byte[] image, int width, int height, int offset)
        {
            byte[] result = new byte[image.Length];
            int sourceY = Math.Max(0, -offset), targetY = Math.Max(0, offset), rows = height - Math.Abs(offset);
            if (rows > 0) Buffer.BlockCopy(image, sourceY * width * 4, result, targetY * width * 4, rows * width * 4);
            return result;
        }

        private static byte Mix(double first, double second, double t) { return (byte)Math.Max(0, Math.Min(255, Math.Round(first * (1 - t) + second * t))); }
    }
}
