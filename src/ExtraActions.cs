using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace XiaoGDesktop
{
    internal struct ExtraActionPose
    {
        internal string Name;
        internal double Time, Strength, Angle, OffsetY;
        internal int Eye;
    }

    // 姿势和肢体由独立高清素材负责；这里提供整体姿态、眨眼节奏和矢量装饰。
    internal static class ExtraActions
    {
        private static readonly Brush Lilac = Brush(151, 126, 184);
        private static readonly Brush Pale = Brush(246, 238, 255);
        private static readonly Pen Outline = new Pen(Lilac, 1.15) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };

        internal static double Duration(string name)
        {
            return name == "sleepy" ? 3.3 : name == "idea" ? 3.1 : 0;
        }

        internal static ExtraActionPose Sample(string name, double elapsed)
        {
            double duration = Duration(name), t = Math.Max(0, elapsed);
            ExtraActionPose pose = new ExtraActionPose { Name = name, Time = t, Angle = 0, OffsetY = 0 };
            if (duration == 0 || t >= duration) { pose.Name = null; return pose; }
            // 两端速度归零；中途切换动作时由主窗口立即复位整体变换。
            double envelope = Smooth(t / .25) * Smooth((duration - t) / .35);
            pose.Strength = envelope;
            if (name == "sleepy")
            {
                pose.Eye = Blink(t, .65, .14, 1.45, .28);
            }
            else if (name == "idea")
            {
                pose.Eye = Blink(t, .20, .10, .12, .12);
            }
            return pose;
        }

        private static int Blink(double t, double start, double close, double hold, double open)
        {
            return BlinkSequence.Sample(t, start, close, hold, open);
        }

        internal static void Draw(DrawingContext dc, ExtraActionPose pose)
        {
            if (pose.Name == null) return;
            double t = pose.Time, amount = pose.Strength;
            if (pose.Name == "rest")
            {
                // 休息用慢慢浮起的两枚z，独立于揉眼动作的较短节奏。
                for (int i = 0; i < 2; i++)
                {
                    double phase = t - .65 - i * 1.35;
                    if (phase < 0) continue;
                    double life = phase % 4.8;
                    double opacity = Smooth(life / .85) * Smooth((4.8 - life) / 1.2) * amount;
                    double x = 146 + i * 8 + Math.Sin(life * 1.1) * 1.6;
                    double y = 78 - i * 9 - life * 4.5;
                    dc.PushOpacity(opacity * .72);
                    FormattedText letter = new FormattedText("z", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        new Typeface("Georgia"), 10 + i * 2, Lilac, 1.0);
                    dc.DrawText(letter, new Point(x, y));
                    dc.Pop();
                }
            }
            else if (pose.Name == "sleepy")
            {
                for (int i = 0; i < 3; i++)
                {
                    double phase = t - .85 - i * .48;
                    if (phase < 0) continue;
                    double life = phase % 2.1;
                    double opacity = Smooth(life / .30) * Smooth((2.1 - life) / .50) * amount;
                    double x = 145 + i * 6 + life * 3, y = 75 - i * 10 - life * 11;
                    dc.PushOpacity(opacity * .9);
                    FormattedText letter = new FormattedText("z", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        new Typeface("Georgia"), 9 + i * 2, Lilac, 1.0);
                    dc.DrawText(letter, new Point(x, y));
                    dc.Pop();
                }
            }
            else if (pose.Name == "idea")
            {
                double light = Smooth((t - .72) / .16) * Smooth((2.65 - t) / .45);
                if (light <= 0) return;
                dc.PushOpacity(light);
                dc.PushTransform(new TranslateTransform(148, 54 - 2 * light));
                dc.PushTransform(new ScaleTransform(.85 + .15 * light, .85 + .15 * light));
                dc.DrawEllipse(new RadialGradientBrush(Color.FromArgb(82, 223, 199, 255), Colors.Transparent), null, new Point(0, -6), 21, 23);
                dc.DrawEllipse(Pale, Outline, new Point(0, -6), 7.8, 9);
                dc.DrawLine(Outline, new Point(-4, 1), new Point(-3.5, 7));
                dc.DrawLine(Outline, new Point(4, 1), new Point(3.5, 7));
                dc.DrawLine(Outline, new Point(-3.5, 7), new Point(3.5, 7));
                dc.DrawLine(Outline, new Point(-2.5, 9.5), new Point(2.5, 9.5));
                dc.DrawLine(Outline, new Point(-3, -7), new Point(0, -3));
                dc.DrawLine(Outline, new Point(0, -3), new Point(3, -7));
                for (int i = 0; i < 5; i++)
                {
                    double angle = (195 + i * 37.5) * Math.PI / 180;
                    dc.DrawLine(Outline, new Point(Math.Cos(angle) * 13, -6 + Math.Sin(angle) * 14),
                        new Point(Math.Cos(angle) * 16, -6 + Math.Sin(angle) * 17));
                }
                dc.Pop(); dc.Pop();
                Sparkle(dc, 42, 68, 3 + .7 * Math.Sin(t * 5));
                Sparkle(dc, 146, 89, 2.3 + .4 * Math.Cos(t * 6));
                dc.Pop();
            }
        }

        private static void Sparkle(DrawingContext dc, double x, double y, double radius)
        {
            StreamGeometry shape = new StreamGeometry();
            using (StreamGeometryContext path = shape.Open())
            {
                path.BeginFigure(new Point(x, y - radius), true, true);
                path.LineTo(new Point(x + radius * .3, y - radius * .3), true, false);
                path.LineTo(new Point(x + radius, y), true, false);
                path.LineTo(new Point(x + radius * .3, y + radius * .3), true, false);
                path.LineTo(new Point(x, y + radius), true, false);
                path.LineTo(new Point(x - radius * .3, y + radius * .3), true, false);
                path.LineTo(new Point(x - radius, y), true, false);
                path.LineTo(new Point(x - radius * .3, y - radius * .3), true, false);
            }
            dc.DrawGeometry(Pale, new Pen(Lilac, .65), shape);
        }

        private static double Smooth(double value)
        {
            value = Math.Max(0, Math.Min(1, value));
            return value * value * value * (value * (value * 6 - 15) + 10);
        }

        private static Brush Brush(byte r, byte g, byte b)
        {
            SolidColorBrush brush = new SolidColorBrush(Color.FromRgb(r, g, b)); brush.Freeze(); return brush;
        }

    }

    internal sealed class ActionDecorations : FrameworkElement
    {
        private ExtraActionPose pose;

        internal void Update(ExtraActionPose value) { pose = value; InvalidateVisual(); }
        internal void Clear()
        {
            if (pose.Name == null) return;
            pose = new ExtraActionPose(); InvalidateVisual();
        }

        protected override void OnRender(DrawingContext context)
        {
            base.OnRender(context);
            if (ActualWidth <= 0 || ActualHeight <= 0) return;
            context.PushTransform(new ScaleTransform(ActualWidth / 192, ActualHeight / 208));
            ExtraActions.Draw(context, pose);
            context.Pop();
        }
    }
}
