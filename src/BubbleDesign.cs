using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace XiaoGDesktop
{
    internal sealed class BubbleCard : Grid
    {
        internal Path Tail;
        internal Border Frame;
        internal bool Below { get; private set; }

        internal void SetBelow(bool value)
        {
            if (Below == value) return;
            Below = value;
            Tail.VerticalAlignment = value ? VerticalAlignment.Top : VerticalAlignment.Bottom;
            Tail.Margin = value ? new Thickness(0, 3, 58, 0) : new Thickness(0, 0, 58, 3);
            Tail.RenderTransformOrigin = new Point(.5, .5);
            Tail.RenderTransform = new ScaleTransform(1, value ? -1 : 1);
            Frame.Margin = value ? new Thickness(5, 15, 5, 5) : new Thickness(5, 5, 5, 15);
        }
    }

    // 小G的银白、淡紫花结气泡：全部使用界面图形绘制，不增加图片依赖。
    internal static class BubbleDesign
    {
        internal const double Width = 236;

        internal static Point Locate(Rect area, Rect character, Size size, bool wasBelow, out bool below)
        {
            double aboveY = character.Top - size.Height - 8;
            double belowY = character.Bottom + 8;
            below = aboveY < area.Top + (wasBelow ? 21 : 5) && belowY + size.Height <= area.Bottom - 5;
            double maximumLeft = Math.Max(area.Left + 5, area.Right - size.Width - 5);
            double maximumTop = Math.Max(area.Top + 5, area.Bottom - size.Height - 5);
            return new Point(
                Math.Max(area.Left + 5, Math.Min(maximumLeft, character.Left + character.Width / 2 - size.Width / 2)),
                Math.Max(area.Top + 5, Math.Min(maximumTop, below ? belowY : aboveY)));
        }

        internal static BubbleCard Create(string text)
        {
            BubbleCard root = new BubbleCard { Width = Width, Background = Brushes.Transparent };
            Path tail = Shape("M 0,0 C 4,7 10,11 14,13 C 13,8 15,3 20,0 Z", "#EFE8F8", "#C5B3DB", 1);
            tail.Width = 20;
            tail.Height = 14;
            tail.Stretch = Stretch.Fill;
            tail.HorizontalAlignment = HorizontalAlignment.Right;
            tail.VerticalAlignment = VerticalAlignment.Bottom;
            tail.Margin = new Thickness(0, 0, 58, 3);
            root.Children.Add(tail);
            root.Tail = tail;

            Border outer = new Border {
                Margin = new Thickness(5, 5, 5, 15), CornerRadius = new CornerRadius(19),
                BorderBrush = Brush("#C5B3DB"), BorderThickness = new Thickness(1.2),
                Background = new LinearGradientBrush(ColorOf("#FFFDFF"), ColorOf("#F0E9F9"), new Point(0, 0), new Point(1, 1)),
                Effect = new DropShadowEffect { Color = ColorOf("#89709F"), BlurRadius = 7, ShadowDepth = 2, Opacity = 0.19 }
            };
            root.Children.Add(outer);
            root.Frame = outer;
            Border inner = new Border {
                Margin = new Thickness(3), CornerRadius = new CornerRadius(15),
                BorderBrush = Brush("#E0D4EE"), BorderThickness = new Thickness(0.6)
            };
            outer.Child = inner;
            Grid card = new Grid { Margin = new Thickness(12, 8, 12, 8) };
            card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            inner.Child = card;

            Grid header = new Grid { Height = 26 };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(35) });
            Canvas flower = Flower(23);
            flower.VerticalAlignment = VerticalAlignment.Center;
            header.Children.Add(flower);
            TextBlock name = new TextBlock {
                Text = "小G的悄悄话", FontSize = 11.5, FontWeight = FontWeights.Medium,
                Foreground = Brush("#9982AF"), FontFamily = new FontFamily("Microsoft YaHei UI"),
                Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(name, 1);
            header.Children.Add(name);
            Canvas wings = Wings();
            Grid.SetColumn(wings, 2);
            header.Children.Add(wings);
            card.Children.Add(header);

            TextBlock message = new TextBlock {
                Text = FormatMessage(text), TextWrapping = TextWrapping.Wrap, FontSize = 14,
                LineHeight = 22, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                Foreground = Brush("#665373"), FontFamily = new FontFamily("Microsoft YaHei UI"),
                Margin = new Thickness(3, 5, 3, 9)
            };
            Grid.SetRow(message, 1);
            card.Children.Add(message);

            // 下沿花边：细线、叶片和小花结，装饰与正文分开，保持文字清楚。
            Canvas lace = new Canvas { Height = 11, HorizontalAlignment = HorizontalAlignment.Stretch, IsHitTestVisible = false };
            Path stem = Shape("M 3,5 C 19,1 34,9 54,5 C 66,2 77,3 84,5 M 104,5 C 114,2 124,2 136,5 C 154,9 169,1 187,5", null, "#CDBDE1", 0.9);
            lace.Children.Add(stem);
            Path leaves = Shape("M 42,5 Q 35,-2 31,1 Q 33,6 42,5 M 57,5 Q 50,12 46,9 Q 48,4 57,5 M 133,5 Q 140,-2 144,1 Q 142,6 133,5 M 148,5 Q 155,12 159,9 Q 157,4 148,5", "#DFD2ED", "#CDBDE1", 0.65);
            lace.Children.Add(leaves);
            Canvas tiny = Flower(10);
            Canvas.SetLeft(tiny, 89);
            Canvas.SetTop(tiny, 0);
            lace.Children.Add(tiny);
            Grid.SetRow(lace, 2);
            card.Children.Add(lace);
            return root;
        }

        private static Canvas Flower(double size)
        {
            Canvas flower = new Canvas { Width = size, Height = size, IsHitTestVisible = false };
            for (int i = 0; i < 6; i++)
            {
                Ellipse petal = new Ellipse {
                    Width = size * 0.32, Height = size * 0.56,
                    Fill = new LinearGradientBrush(ColorOf("#FFFDFF"), ColorOf("#E5D8F0"), 90),
                    Stroke = Brush("#BBA7CE"), StrokeThickness = Math.Max(0.55, size * 0.035),
                    RenderTransform = new RotateTransform(i * 60, size * 0.16, size * 0.43)
                };
                Canvas.SetLeft(petal, size * 0.34);
                Canvas.SetTop(petal, size * 0.07);
                flower.Children.Add(petal);
            }
            Ellipse center = new Ellipse { Width = size * 0.23, Height = size * 0.23, Fill = Brush("#FFFDFE"), Stroke = Brush("#B49ACB"), StrokeThickness = 0.7 };
            Canvas.SetLeft(center, size * 0.385);
            Canvas.SetTop(center, size * 0.385);
            flower.Children.Add(center);
            return flower;
        }

        private static string FormatMessage(string text)
        {
            // 长句优先在逗号处换行，避免最后两个字挤成单独一行。
            int comma = text.IndexOf('，');
            if (text.Length <= 12) return text;
            if (comma > 0) return text.Insert(comma + 1, "\n");
            // 无逗号的长句分成相近的两行，避免尾音符号独占一行。
            int split = Math.Min(text.Length - 4, (int)Math.Ceiling(text.Length * .55));
            return text.Insert(split, "\n");
        }

        private static Canvas Wings()
        {
            Canvas wings = new Canvas { Width = 35, Height = 26, IsHitTestVisible = false };
            Path left = Shape("M 16,17 C 11,8 5,6 1,5 Q 2,13 6,19 Q 8,15 10,18 Q 12,14 16,17 Z", "#E6DCF1", "#BFA9D4", 0.8);
            Path right = Shape("M 19,17 C 24,8 30,6 34,5 Q 33,13 29,19 Q 27,15 25,18 Q 23,14 19,17 Z", "#E6DCF1", "#BFA9D4", 0.8);
            wings.Children.Add(left);
            wings.Children.Add(right);
            Path ribs = Shape("M 16,17 Q 8,12 1,5 M 19,17 Q 27,12 34,5", null, "#C9B6DC", 0.65);
            wings.Children.Add(ribs);
            Path star = Shape("M 17.5,7 L 19,10.5 L 22,12 L 19,13.5 L 17.5,17 L 16,13.5 L 13,12 L 16,10.5 Z", "#FFFDFF", "#BFA9D4", 0.6);
            wings.Children.Add(star);
            return wings;
        }

        private static Path Shape(string data, string fill, string stroke, double thickness)
        {
            return new Path { Data = Geometry.Parse(data), Fill = fill == null ? null : Brush(fill), Stroke = Brush(stroke), StrokeThickness = thickness, StrokeLineJoin = PenLineJoin.Round };
        }

        private static Color ColorOf(string color) { return (Color)ColorConverter.ConvertFromString(color); }
        private static Brush Brush(string color) { return new SolidColorBrush(ColorOf(color)); }
    }
}
