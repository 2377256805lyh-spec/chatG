using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace XiaoGDesktop
{
    // 界面只取已完成的转头画面；后台始终只计算一张，并覆盖尚未执行的旧鼠标请求。
    internal sealed class GazeRenderer
    {
        private const int CacheFrames = 24;
        private readonly HeadMotion motion;
        private readonly BitmapSource neutral;
        private readonly object gate = new object();
        private readonly Dictionary<int, CachedFrame> cache = new Dictionary<int, CachedFrame>();
        private readonly LinkedList<int> order = new LinkedList<int>();
        private BitmapSource latest;
        private int generation, requestedDirection, requestedStrength, requestedKey = -1;
        private int requestedRevision, displayedRevision;
        private bool pending, working, active, failed;

        private sealed class CachedFrame
        {
            internal BitmapSource Image;
            internal LinkedListNode<int> Position;
        }

        internal GazeRenderer(HeadMotion motion, BitmapSource neutral)
        {
            if (motion == null) throw new ArgumentNullException("motion");
            if (neutral == null) throw new ArgumentNullException("neutral");
            this.motion = motion;
            this.neutral = neutral;
            latest = neutral;
        }

        internal BitmapSource Get(int direction, int strength)
        {
            lock (gate)
            {
                if (strength == 0)
                {
                    if (active) ResetLocked();
                    return neutral;
                }
                active = true;
                requestedDirection = direction;
                requestedStrength = strength;
                int key = direction * (FrameComposer.StrengthSteps + 1) + strength;
                if (key != requestedKey) requestedRevision++;
                requestedKey = key;
                BitmapSource image;
                if (TryCached(requestedKey, out image))
                {
                    pending = false;
                    latest = image;
                    displayedRevision = requestedRevision;
                    return image;
                }
                if (!failed)
                {
                    pending = true;
                    StartWorkerLocked();
                }
                return latest;
            }
        }

        internal void Reset()
        {
            lock (gate) ResetLocked();
        }

        private void ResetLocked()
        {
            generation++;
            pending = active = failed = false;
            requestedKey = -1;
            requestedRevision = displayedRevision = 0;
            latest = neutral;
            // 完成的冻结位图只取决于方向、强度，可跨会话复用；在途旧结果不能发布。
        }

        // 调用方持有gate；同一时刻最多一个worker，不为每个鼠标位置创建线程。
        private void StartWorkerLocked()
        {
            if (working || failed || !pending) return;
            working = true;
            if (!ThreadPool.QueueUserWorkItem(RenderPending))
            {
                working = false;
                failed = true;
                pending = false;
            }
        }

        private void RenderPending(object state)
        {
            int session = -1;
            try
            {
                while (true)
                {
                    int direction, strength, key, revision;
                    lock (gate)
                    {
                        if (!pending || failed) return;
                        session = generation;
                        direction = requestedDirection;
                        strength = requestedStrength;
                        key = requestedKey;
                        revision = requestedRevision;
                        pending = false;
                        BitmapSource cached;
                        if (TryCached(key, out cached))
                        {
                            latest = cached;
                            displayedRevision = revision;
                            continue;
                        }
                    }

                    byte[] pixels = motion.Render(direction, strength);
                    BitmapSource image = BitmapSource.Create(SourceArtwork.Width, SourceArtwork.Height,
                        96 * SourceArtwork.Scale, 96 * SourceArtwork.Scale, PixelFormats.Pbgra32,
                        null, pixels, SourceArtwork.Width * 4);
                    image.Freeze();

                    lock (gate)
                    {
                        if (session != generation) continue;
                        image = Remember(key, image);
                        // 同一会话允许迟到一张，持续移动时仍有画面；缓存命中的更新位置不被旧帧倒退覆盖。
                        if (requestedKey == key)
                        {
                            latest = image;
                            displayedRevision = requestedRevision;
                        }
                        else if (pending && displayedRevision <= revision)
                        {
                            latest = image;
                            displayedRevision = revision;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // 转头生成失败时保留当前有效画面；本会话不反复重试，下一次Reset可恢复。
                lock (gate)
                    if (session == generation)
                    {
                        failed = true;
                        pending = false;
                    }
            }
            finally
            {
                lock (gate)
                {
                    working = false;
                    // Get可能恰好发生在worker准备退出时；不能漏掉这个最新请求。
                    StartWorkerLocked();
                }
            }
        }

        // 缓存仅由持有gate的调用访问，与FrameComposer的其他动作缓存完全分开。
        private bool TryCached(int key, out BitmapSource image)
        {
            CachedFrame frame;
            if (!cache.TryGetValue(key, out frame)) { image = null; return false; }
            order.Remove(frame.Position);
            order.AddLast(frame.Position);
            image = frame.Image;
            return true;
        }

        private BitmapSource Remember(int key, BitmapSource image)
        {
            BitmapSource existing;
            if (TryCached(key, out existing)) return existing;
            if (cache.Count >= CacheFrames)
            {
                int oldest = order.First.Value;
                order.RemoveFirst();
                cache.Remove(oldest);
            }
            cache.Add(key, new CachedFrame { Image = image, Position = order.AddLast(key) });
            return image;
        }
    }
}
