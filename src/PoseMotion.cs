using System;

namespace XiaoGDesktop
{
    // 在低分辨率标尺上找相邻姿势的位移，高清原画只重采样一次，避免叠图残影。
    internal sealed class PoseMotion
    {
        private const int W = 192, H = 208;
        private readonly int Step, Columns, Rows;
        private readonly byte[] first, second;
        private readonly byte[] firstBody, secondBody;
        private readonly Vector[] forward;
        private readonly Vector[] backward;
        private readonly Vector?[] firstHands, secondHands;
        private readonly Vector firstFace, secondFace;
        private readonly bool coordinated;
        private readonly bool verticalOnly;

        internal PoseMotion(byte[] a, byte[] b, byte[] highA, byte[] highB, int rowA, int colA, int rowB, int colB)
        {
            first = highA; second = highB;
            bool aGesture = rowA == 7, bGesture = rowB == 7;
            coordinated = aGesture && bGesture || aGesture && rowB == 0 && colB == 0 || bGesture && rowA == 0 && colA == 0;
            Step = coordinated ? 4 : 8; Columns = W / Step + 1; Rows = H / Step + 1;
            firstHands = new Vector?[] { Hand(rowA, colA, false), Hand(rowA, colA, true) };
            secondHands = new Vector?[] { Hand(rowB, colB, false), Hand(rowB, colB, true) };
            double chin;
            firstFace = coordinated ? GestureFace(a) : Face(a, out chin);
            secondFace = coordinated ? GestureFace(b) : Face(b, out chin);
            if (rowA == 7 && rowB == 7 && colA == 5 && colB == 6) secondFace = firstFace;
            verticalOnly = !coordinated && Math.Abs(firstFace.Y - secondFace.Y) > 10;
            for (int h = 0; h < firstHands.Length; h++)
                if (!coordinated && firstHands[h].HasValue && secondHands[h].HasValue && Math.Abs(firstHands[h].Value.Y - secondHands[h].Value.Y) > 20) verticalOnly = true;
            if (verticalOnly) return;
            if (coordinated)
            {
                first = GestureLayers.Arm(rowA, colA); second = GestureLayers.Arm(rowB, colB);
                firstBody = GestureLayers.Body(rowA, colA); secondBody = GestureLayers.Body(rowB, colB);
            }
            forward = Match(a, b, firstHands, secondHands, firstFace, secondFace);
            if (coordinated)
            {
                backward = Match(b, a, secondHands, firstHands, secondFace, firstFace);
                ProtectFace(forward, firstFace, secondFace);
                ProtectFace(backward, secondFace, firstFace);
            }
            for (int y = 0; y < Rows - 1; y++)
                for (int x = 0; x < Columns - 1; x++)
                {
                    int i = y * Columns + x;
                    Vector p = new Vector(forward[i + 1].X - forward[i].X + Step, forward[i + 1].Y - forward[i].Y);
                    Vector q = new Vector(forward[i + Columns].X - forward[i].X, forward[i + Columns].Y - forward[i].Y + Step);
                    if (!coordinated && p.X * q.Y - p.Y * q.X < 10) verticalOnly = true;
                    p = new Vector(forward[i + Columns + 1].X - forward[i + 1].X, forward[i + Columns + 1].Y - forward[i + 1].Y + Step);
                    q = new Vector(forward[i + Columns].X - forward[i + 1].X - Step, forward[i + Columns].Y - forward[i + 1].Y + Step);
                    if (!coordinated && p.X * q.Y - p.Y * q.X < 10) verticalOnly = true;
                }
        }

        internal byte[] Render(double amount)
        {
            if (amount <= 0) return coordinated ? ComposeBody((byte[])first.Clone(), 0) : first;
            if (amount >= 1) return coordinated ? ComposeBody((byte[])second.Clone(), 1) : second;
            if (verticalOnly) return Vertical(amount);
            bool fromFirst = amount < 0.5;
            byte[] texture = fromFirst ? first : second;
            Vector[] flow = coordinated && !fromFirst ? backward : forward;
            double position = coordinated && !fromFirst ? 1 - amount : amount;
            int width = SourceArtwork.Width, height = SourceArtwork.Height;
            byte[] result = new byte[width * height * 4];
            Vector[] original = new Vector[Columns * Rows], target = new Vector[Columns * Rows];
            for (int y = 0; y < Rows; y++)
                for (int x = 0; x < Columns; x++)
                {
                    int i = y * Columns + x;
                    Vector start = new Vector(Math.Min(W - 1, x * Step), Math.Min(H - 1, y * Step));
                    original[i] = fromFirst || coordinated ? start : new Vector(start.X + flow[i].X, start.Y + flow[i].Y);
                    target[i] = new Vector(start.X + flow[i].X * position, start.Y + flow[i].Y * position);
                }
            // 手抬到脸前时在身体上方绘制，避免低处的空白网格擦掉手部。
            for (int layer = 0; layer < 2; layer++)
                for (int y = 0; y < Rows - 1; y++)
                    for (int x = 0; x < Columns - 1; x++)
                    {
                        bool nearHand = false;
                        foreach (Vector? hand in coordinated && !fromFirst ? secondHands : firstHands)
                            if (hand.HasValue && Math.Abs(x * Step + Step / 2 - hand.Value.X) < 16 && Math.Abs(y * Step + Step / 2 - hand.Value.Y) < 18) nearHand = true;
                        if (nearHand != (layer == 1)) continue;
                        int i = y * Columns + x;
                        Triangle(texture, result, original, target, i, i + 1, i + Columns);
                        Triangle(texture, result, original, target, i + 1, i + Columns + 1, i + Columns);
                    }
            if (coordinated) RestoreFace(texture, result, fromFirst, amount);
            return coordinated ? ComposeBody(result, amount) : result;
        }

        private byte[] ComposeBody(byte[] arm, double amount)
        {
            int scale = SourceArtwork.Scale, width = SourceArtwork.Width, height = SourceArtwork.Height;
            double faceX = firstFace.X + (secondFace.X - firstFace.X) * amount;
            double faceY = firstFace.Y + (secondFace.Y - firstFace.Y) * amount;
            double chin = faceY + 16;
            bool fromFirst = amount < .5;
            byte[] body = fromFirst ? firstBody : secondBody;
            Vector sourceFace = fromFirst ? firstFace : secondFace;
            byte[] pixel = new byte[4];
            // 身体只沿原来整张脸的位置轻微跟随；不再按手腕的位置局部拉扯衣服和翅膀。
            for (int y = 0; y < height; y++)
            {
                double py = (y + .5) / scale - .5;
                double weight = py <= chin ? 1 : Math.Max(0, (198 - py) / (198 - chin));
                double dx = (sourceFace.X - faceX) * weight * scale, dy = (sourceFace.Y - faceY) * weight * scale;
                for (int x = 0; x < width; x++)
                {
                    Array.Clear(pixel, 0, 4);
                    SourceArtwork.Sample(body, width, height, x + dx, y + dy, pixel, 0);
                    int p = (y * width + x) * 4;
                    double through = 1 - arm[p + 3] / 255.0;
                    for (int c = 0; c < 4; c++)
                        arm[p + c] = (byte)Math.Min(255, arm[p + c] + pixel[c] * through + .5);
                }
            }
            return arm;
        }

        private static void Triangle(byte[] texture, byte[] result, Vector[] original, Vector[] target, int ia, int ib, int ic)
        {
            Vector a = target[ia], b = target[ib], c = target[ic];
            int scale = SourceArtwork.Scale, width = SourceArtwork.Width, height = SourceArtwork.Height;
            double determinant = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
            if (Math.Abs(determinant) < 0.001) return;
            int left = Math.Max(0, (int)Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X)) * scale));
            int right = Math.Min(width - 1, (int)Math.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X)) * scale));
            int top = Math.Max(0, (int)Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y)) * scale));
            int bottom = Math.Min(height - 1, (int)Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y)) * scale));
            double du = (b.Y - c.Y) / determinant / scale, dv = (c.Y - a.Y) / determinant / scale;
            for (int y = top; y <= bottom; y++)
            {
                double py = (y + 0.5) / scale - 0.5, px = (left + 0.5) / scale - 0.5;
                double u = ((b.Y - c.Y) * (px - c.X) + (c.X - b.X) * (py - c.Y)) / determinant;
                double v = ((c.Y - a.Y) * (px - c.X) + (a.X - c.X) * (py - c.Y)) / determinant;
                for (int x = left; x <= right; x++, u += du, v += dv)
                {
                    double w = 1 - u - v;
                    if (u < -0.00001 || v < -0.00001 || w < -0.00001) continue;
                    double sx = original[ia].X * u + original[ib].X * v + original[ic].X * w;
                    double sy = original[ia].Y * u + original[ib].Y * v + original[ic].Y * w;
                    SourceArtwork.Sample(texture, width, height, (sx + 0.5) * scale - 0.5, (sy + 0.5) * scale - 0.5, result, (y * width + x) * 4, true);
                }
            }
        }

        private Vector[] Match(byte[] a, byte[] b, Vector?[] handsA, Vector?[] handsB, Vector faceA, Vector faceB)
        {
            Vector[] result = new Vector[Columns * Rows], seed = new Vector[Columns * Rows];
            for (int gy = 0; gy < Rows; gy++)
                for (int gx = 0; gx < Columns; gx++)
                {
                    int x = Math.Min(W - 1, gx * Step), y = Math.Min(H - 1, gy * Step), index = gy * Columns + gx;
                    double bodyWeight = y <= faceA.Y ? 1 : Math.Max(0, (198 - y) / (198 - faceA.Y));
                    Vector initial = new Vector((faceB.X - faceA.X) * bodyWeight, (faceB.Y - faceA.Y) * bodyWeight);
                    for (int h = 0; h < handsA.Length; h++)
                    if (handsA[h].HasValue && handsB[h].HasValue)
                    {
                        Vector ha = handsA[h].Value, hb = handsB[h].Value;
                        double dx = x - ha.X, dy = y - ha.Y;
                        double weight = Math.Exp(-(dx * dx + dy * dy) / (coordinated ? 110.0 : 190.0));
                        initial.X += (hb.X - ha.X - initial.X) * weight;
                        initial.Y += (hb.Y - ha.Y - initial.Y) * weight;
                    }
                    seed[index] = initial;
                    if (x < 24 || x > 170 || y < 24 || y > 201) { result[index] = new Vector(0, 0); seed[index] = result[index]; continue; }
                    int centerX = (int)Math.Round(initial.X), centerY = (int)Math.Round(initial.Y);
                    int bestX = centerX, bestY = centerY;
                    double best = double.MaxValue;
                    for (int dy = centerY - 6; dy <= centerY + 6; dy += 2)
                        for (int dx = centerX - 6; dx <= centerX + 6; dx += 2)
                        {
                            double error = Error(a, b, x, y, dx, dy) + 12 * ((dx - initial.X) * (dx - initial.X) + (dy - initial.Y) * (dy - initial.Y));
                            if (error < best) { best = error; bestX = dx; bestY = dy; }
                        }
                    int refineX = bestX, refineY = bestY;
                    for (int dy = refineY - 1; dy <= refineY + 1; dy++)
                        for (int dx = refineX - 1; dx <= refineX + 1; dx++)
                        {
                            double error = Error(a, b, x, y, dx, dy) + 12 * ((dx - initial.X) * (dx - initial.X) + (dy - initial.Y) * (dy - initial.Y));
                            if (error < best) { best = error; bestX = dx; bestY = dy; }
                        }
                    result[index] = new Vector(bestX, bestY);
                }
            // 邻近网格一起走，抑制逐块匹配带来的细碎拉扯。
            for (int pass = 0; pass < 3; pass++)
            {
                Vector[] smoothed = (Vector[])result.Clone();
                for (int y = 1; y < Rows - 1; y++)
                    for (int x = 1; x < Columns - 1; x++)
                    {
                        int i = y * Columns + x;
                        double dx = result[i - 1].X + result[i + 1].X + result[i - Columns].X + result[i + Columns].X;
                        double dy = result[i - 1].Y + result[i + 1].Y + result[i - Columns].Y + result[i + Columns].Y;
                        smoothed[i] = new Vector(result[i].X * 0.45 + dx * 0.1 + seed[i].X * 0.15,
                            result[i].Y * 0.45 + dy * 0.1 + seed[i].Y * 0.15);
                    }
                result = smoothed;
            }
            for (int h = 0; h < handsA.Length; h++)
            if (handsA[h].HasValue && handsB[h].HasValue)
            {
                Vector ha = handsA[h].Value, hb = handsB[h].Value, actual = At(result, ha.X, ha.Y);
                double correctionX = hb.X - ha.X - actual.X, correctionY = hb.Y - ha.Y - actual.Y;
                for (int y = 1; y < Rows - 1; y++)
                    for (int x = 1; x < Columns - 1; x++)
                    {
                        double dx = x * Step - ha.X, dy = y * Step - ha.Y;
                        double weight = Math.Exp(-(dx * dx + dy * dy) / (coordinated ? 130.0 : 230.0));
                        int i = y * Columns + x;
                        result[i] = new Vector(result[i].X + correctionX * weight, result[i].Y + correctionY * weight);
                    }
            }
            return result;
        }

        private Vector At(Vector[] flow, double x, double y)
        {
            x = Math.Max(0, Math.Min(Columns - 1.001, x / Step)); y = Math.Max(0, Math.Min(Rows - 1.001, y / Step));
            int ix = (int)x, iy = (int)y, i = iy * Columns + ix;
            double tx = x - ix, ty = y - iy;
            return new Vector(
                (flow[i].X * (1 - tx) + flow[i + 1].X * tx) * (1 - ty) + (flow[i + Columns].X * (1 - tx) + flow[i + Columns + 1].X * tx) * ty,
                (flow[i].Y * (1 - tx) + flow[i + 1].Y * tx) * (1 - ty) + (flow[i + Columns].Y * (1 - tx) + flow[i + Columns + 1].Y * tx) * ty);
        }

        private static double Error(byte[] a, byte[] b, int x, int y, int dx, int dy)
        {
            double error = 0;
            for (int py = -4; py <= 4; py += 2)
                for (int px = -4; px <= 4; px += 2)
                {
                    int ax = x + px, ay = y + py, bx = ax + dx, by = ay + dy;
                    if (ax < 0 || ax >= W || ay < 0 || ay >= H || bx < 0 || bx >= W || by < 0 || by >= H) { error += 50000; continue; }
                    int p = (ay * W + ax) * 4, q = (by * W + bx) * 4;
                    for (int c = 0; c < 4; c++)
                    { int difference = a[p + c] - b[q + c]; error += difference * difference * (c == 3 ? 1.5 : 1); }
                }
            return error;
        }

        private static Vector Face(byte[] image, out double chin, int maximumY = 164)
        {
            bool[] mask = new bool[W * H];
            for (int y = 30; y < maximumY; y++)
                for (int x = 56; x < 150; x++)
                {
                    int p = (y * W + x) * 4;
                    mask[y * W + x] = image[p + 3] > 210 && image[p + 2] > 175 && image[p + 2] - image[p + 1] > 5 && image[p + 1] - image[p] > 3 && image[p + 2] - image[p] > 20;
                }
            int[] queue = new int[W * H];
            int largest = 0; Vector center = new Vector(92, 105);
            chin = 117;
            for (int i = 0; i < mask.Length; i++)
            {
                if (!mask[i]) continue;
                int count = 1, read = 0, sx = 0, sy = 0, bottom = 0;
                mask[i] = false; queue[0] = i;
                while (read < count)
                {
                    int p = queue[read++]; sx += p % W; sy += p / W; bottom = Math.Max(bottom, p / W);
                    int[] neighbors = { p - 1, p + 1, p - W, p + W };
                    foreach (int n in neighbors) if (n >= 0 && n < mask.Length && mask[n]) { mask[n] = false; queue[count++] = n; }
                }
                if (count > largest) { largest = count; center = new Vector(sx / (double)count, sy / (double)count); chin = bottom + 2; }
            }
            return center;
        }

        private static Vector GestureFace(byte[] image)
        {
            // 截止到下巴附近再找最大的肤色区域，排除下方的拳头与裸露肩膀。
            double chin;
            Vector center = Face(image, out chin, 116);
            return new Vector(center.X, chin - 12);
        }

        private void ProtectFace(Vector[] flow, Vector from, Vector to)
        {
            Vector move = new Vector(to.X - from.X, to.Y - from.Y);
            for (int y = 0; y < Rows; y++)
                for (int x = 0; x < Columns; x++)
                {
                    double dx = (x * Step - from.X) / 31.0;
                    double dy = (y * Step - from.Y) / (y * Step < from.Y ? 48.0 : 16.0);
                    double weight = FrameComposer.Ease((1.45 - Math.Sqrt(dx * dx + dy * dy)) / 0.4);
                    weight *= 1 - FrameComposer.Ease((y * Step - from.Y - 12) / 3.0);
                    int i = y * Columns + x;
                    flow[i] = new Vector(flow[i].X * (1 - weight) + move.X * weight, flow[i].Y * (1 - weight) + move.Y * weight);
                }
        }

        private void RestoreFace(byte[] texture, byte[] result, bool fromFirst, double amount)
        {
            Vector center = new Vector(firstFace.X + (secondFace.X - firstFace.X) * amount,
                firstFace.Y + (secondFace.Y - firstFace.Y) * amount);
            Vector source = fromFirst ? firstFace : secondFace;
            int scale = SourceArtwork.Scale, width = SourceArtwork.Width;
            byte[] pixel = new byte[4];
            // 脸和下巴按整块原画取样；下巴外侧仍允许手臂独立接近。
            for (int y = (int)((center.Y - 39) * scale); y < (center.Y + 13.3) * scale; y++)
                for (int x = (int)((center.X - 31) * scale); x < (center.X + 31) * scale; x++)
                {
                    double px = (x + .5) / scale - .5, py = (y + .5) / scale - .5;
                    double weight = FrameComposer.Ease((31 - Math.Abs(px - center.X)) / 5)
                        * FrameComposer.Ease((py - center.Y + 39) / 3)
                        * FrameComposer.Ease((center.Y + 13.3 - py) / 1.8);
                    int p = (y * width + x) * 4;
                    Array.Clear(pixel, 0, 4);
                    SourceArtwork.Sample(texture, width, SourceArtwork.Height,
                        x + (source.X - center.X) * scale, y + (source.Y - center.Y) * scale, pixel, 0);
                    for (int c = 0; c < 4; c++) result[p + c] = (byte)(result[p + c] * (1 - weight) + pixel[c] * weight + .5);
                }
        }

        private byte[] Vertical(double amount)
        {
            bool fromFirst = amount < 0.5;
            byte[] texture = fromFirst ? first : second;
            int scale = SourceArtwork.Scale, width = SourceArtwork.Width, height = SourceArtwork.Height;
            byte[] result = new byte[width * height * 4];
            double faceX = firstFace.X + (secondFace.X - firstFace.X) * amount;
            double faceY = firstFace.Y + (secondFace.Y - firstFace.Y) * amount;
            double chin = faceY + 16;
            // 大幅动作保持整张脸的形状，姿势细节交给原画；只让颈部以下平顺衔接到固定脚底。
            for (int y = 0; y < height - scale; y++)
            {
                double py = (y + 0.5) / scale - 0.5;
                double weight = py <= chin ? 1 : Math.Max(0, (198 - py) / (198 - chin));
                double sy = py + ((fromFirst ? firstFace.Y : secondFace.Y) - faceY) * weight;
                double offsetX = ((fromFirst ? firstFace.X : secondFace.X) - faceX) * weight;
                for (int x = 18 * scale; x < width - 14 * scale; x++)
                {
                    double sx = (x + 0.5) / scale - 0.5 + offsetX;
                    SourceArtwork.Sample(texture, width, height, (sx + 0.5) * scale - 0.5, (sy + 0.5) * scale - 0.5, result, (y * width + x) * 4);
                }
            }
            return result;
        }

        private static Vector? Hand(int row, int col, bool right)
        {
            if (row == 0) return new Vector(right ? 117 : 66, 151);
            if (row == 5)
            {
                double[] x = right ? new double[] { 117, 99, 98, 100, 99, 99, 98, 117 } : new double[] { 65, 77, 77, 86, 82, 82, 77, 65 };
                double[] y = { 151, 152, 150, 137, 150, 150, 150, 151 };
                return new Vector(x[col], y[col]);
            }
            if (row == 7)
            {
                if (right) return new Vector(115, 151);
                double[] x = { 66.7, 71.1, 87, 86, 85, 85, 85, 85.4 }, y = { 148.6, 133.2, 120, 118.7, 119.2, 118.8, 118.8, 119.5 };
                return new Vector(x[col], y[col]);
            }
            return null;
        }

        private struct Vector
        {
            internal double X, Y;
            internal Vector(double x, double y) { X = x; Y = y; }
        }
    }
}
