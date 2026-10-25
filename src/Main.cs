using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace XiaoGDesktop
{
    [DataContract]
    public sealed class Preferences
    {
        [DataMember] public double Size = 224;
        [DataMember] public double Left = double.NaN;
        [DataMember] public double Top = double.NaN;
        [DataMember] public bool Topmost = true;
        [DataMember] public bool Speech = true;
        [DataMember] public bool Voice = true;
        [DataMember] public int VoiceVolume = 55;
        [OnDeserializing] private void Defaults(StreamingContext context)
        {
            Size = 224; Left = Top = double.NaN; Topmost = Speech = Voice = true; VoiceVolume = 55;
        }
    }

    public static class Program
    {
        [STAThread]
        public static void Main(string[] arguments)
        {
            RemovePreviousVersion();
            if (arguments.Length > 0 && arguments[0] == "--watch-codex") { CodexLink.Watch(); return; }
            if (arguments.Length > 0 && arguments[0] == "--enable-codex-link") { CodexLink.Enable(); return; }
            bool created;
            using (Mutex instance = new Mutex(true, "Local\\XiaoGDesktop-20261007", out created))
            {
                if (!created) return;
                try
                {
                    Application app = new Application();
                    app.ShutdownMode = ShutdownMode.OnMainWindowClose;
                    app.Run(new Companion());
                }
                catch (Exception error)
                {
                    MessageBox.Show("小G暂时无法启动：\n" + error.Message, "小G", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private static void RemovePreviousVersion()
        {
            // 更新时旧进程可能仍占用映像；释放后，下次启动（含登录等待程序）自动清理。
            foreach (string name in new string[] { "小G.previous", "小G.previous-v3" })
            {
                string previous = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name);
                try { if (File.Exists(previous)) File.Delete(previous); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    public sealed class Companion : Window
    {
        private const int CellWidth = 192, CellHeight = 208;
        private const double Headroom = 64;
        private const double ClickDelayMilliseconds = 280;
        private const double JumpPreparation = 0.09;
        private readonly Image portrait = new Image();
        private readonly Image limbPortrait = new Image { IsHitTestVisible = false };
        private readonly TranslateTransform movement = new TranslateTransform();
        private readonly ScaleTransform breathing = new ScaleTransform(1, 1);
        private readonly RotateTransform inclination = new RotateTransform();
        private readonly ActionDecorations decorations = new ActionDecorations { IsHitTestVisible = false };
        private FrameComposer artwork;
        private BitmapSource[,] frames;
        private readonly byte[] hitPixel = new byte[4];
        private BitmapSource currentImage;
        private BitmapSource currentLimb;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
        private readonly Random random = new Random();
        private Preferences preferences;
        private Forms.NotifyIcon tray;
        private HwndSource source;
        private double nextBlink, blinkStart = -1, actionStart = -1, actionTime;
        private string action = "idle";
        private int currentRow = -1, currentColumn = -1, gaze, dragDirection = 1, cycleIndex;
        private bool pressed, dragging, saveWarningShown, gazeActive, secondClick, firstActionFrame;
        private double gazeAngle, gazeStrength, lastGazeInside, pendingClickAt = -1;
        private double resumeGazeAt, dragPhase, lastDragMotion;
        private TimeSpan lastPresentation = TimeSpan.MinValue;
        private Point pendingClickScreen;
        private int interactions, speechThreshold, previousPhrase = -1;
        private double bubbleStarted, bubbleDuration = 4.8;
        private SpeechBubble bubble;
        private VoiceClips voice;
        private readonly string[] cycle = { "tiptoe", "thinking", "shake", "failed", "waiting", "review", "sleepy", "idea" };
        private readonly string[] phrases = {
            "小G萌萌哒，学习也要棒棒哒！", "小G督促你学习哟～", "先自己想两分钟，小G陪你。",
            "今天也一点点变厉害～", "摸摸结束，来学一个小知识吧。", "喝口水，看看远处再继续～",
            "把为什么想明白，比只记住答案更厉害。", "休息一下，再解决下一个小问题。",
            "今天也给努力的你一朵小紫花～", "先讲讲你的思路，小G认真听着呢。",
            "卡住也没关系，拆成小步骤再来～", "坐久啦，伸个懒腰再继续吧！"
        };
        private Point pressScreen, lastDragScreen, lastMotionScreen;
        private double pressLeft, pressTop;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { public int X, Y; }
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr window, int index, int value);

        public Companion()
        {
            Title = "小G · 本地桌面挂件";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            UseLayoutRounding = true;
            preferences = LoadPreferences();
            Topmost = preferences.Topmost;
            voice = new VoiceClips(phrases.Length, preferences.VoiceVolume);

            LoadFrames();
            Canvas surface = new Canvas();
            portrait.Stretch = Stretch.Fill;
            portrait.SnapsToDevicePixels = true;
            portrait.RenderTransformOrigin = new Point(0.5, 198.0 / CellHeight);
            TransformGroup transforms = new TransformGroup();
            transforms.Children.Add(breathing);
            transforms.Children.Add(inclination);
            transforms.Children.Add(movement);
            portrait.RenderTransform = transforms;
            RenderOptions.SetBitmapScalingMode(portrait, BitmapScalingMode.HighQuality);
            surface.Children.Add(portrait);
            limbPortrait.Stretch = Stretch.Fill;
            limbPortrait.RenderTransformOrigin = portrait.RenderTransformOrigin;
            limbPortrait.RenderTransform = transforms;
            RenderOptions.SetBitmapScalingMode(limbPortrait, BitmapScalingMode.HighQuality);
            surface.Children.Add(limbPortrait);
            decorations.RenderTransformOrigin = portrait.RenderTransformOrigin;
            decorations.RenderTransform = transforms;
            surface.Children.Add(decorations);
            Content = surface;
            ApplySize();
            SetFrame(0, 0);
            ScheduleBlink();
            speechThreshold = random.Next(5, 8);

            MouseMove += OnMouseMove;
            MouseLeftButtonDown += OnPointerDown;
            MouseLeftButtonUp += OnPointerUp;
            LostMouseCapture += OnPointerCaptureLost;
            MouseRightButtonUp += delegate(object sender, MouseButtonEventArgs e)
            {
                OpenMenu();
                e.Handled = true;
            };
            SourceInitialized += OnSourceInitialized;
            Loaded += delegate { CreateTray(); CompositionTarget.Rendering += Render; if (CodexLink.Enabled) CodexLink.StartWatcher(); };
            Closing += delegate { SavePreferences(); };
            Closed += delegate
            {
                CompositionTarget.Rendering -= Render;
                if (source != null) source.RemoveHook(WindowHook);
                if (tray != null) { tray.Visible = false; tray.Dispose(); }
                if (bubble != null) bubble.Close();
                voice.Dispose();
            };
        }

        private Preferences LoadPreferences()
        {
            try
            {
                if (File.Exists(settingsPath))
                    using (FileStream stream = File.OpenRead(settingsPath))
                    {
                        Preferences value = (Preferences)new DataContractJsonSerializer(typeof(Preferences)).ReadObject(stream);
                        if (value != null && !double.IsNaN(value.Size) && !double.IsInfinity(value.Size))
                        {
                            value.Size = Math.Max(144, Math.Min(384, value.Size));
                            value.VoiceVolume = Math.Max(0, Math.Min(100, value.VoiceVolume));
                            return value;
                        }
                    }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (SerializationException) { }
            return new Preferences();
        }

        private void SavePreferences()
        {
            preferences.Left = Left;
            preferences.Top = Top;
            preferences.Topmost = Topmost;
            try
            {
                using (MemoryStream memory = new MemoryStream())
                {
                    new DataContractJsonSerializer(typeof(Preferences)).WriteObject(memory, preferences);
                    string temporaryPath = settingsPath + ".tmp";
                    File.WriteAllBytes(temporaryPath, memory.ToArray());
                    if (File.Exists(settingsPath)) File.Replace(temporaryPath, settingsPath, null);
                    else File.Move(temporaryPath, settingsPath);
                }
            }
            catch (Exception error)
            {
                if (!(error is IOException) && !(error is UnauthorizedAccessException)) throw;
                if (!saveWarningShown && tray != null)
                {
                    saveWarningShown = true;
                    tray.ShowBalloonTip(4000, "小G", "位置暂时无法保存，请将整个文件夹放在允许写入的位置。", Forms.ToolTipIcon.Info);
                }
            }
        }

        private void LoadFrames()
        {
            BitmapImage sheet = new BitmapImage();
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("xiaog-atlas.png"))
            {
                sheet.BeginInit();
                sheet.CacheOption = BitmapCacheOption.OnLoad;
                sheet.StreamSource = stream;
                sheet.EndInit();
            }
            sheet.Freeze();
            artwork = new FrameComposer(sheet, true);
            frames = artwork.Frames;
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            source = (HwndSource)PresentationSource.FromVisual(this);
            // 不抢走编辑器或游戏的键盘焦点，也不占任务栏。
            int style = GetWindowLong(source.Handle, -20);
            SetWindowLong(source.Handle, -20, style | 0x08000000 | 0x00000080);
            source.AddHook(WindowHook);
            Rect area = SystemParameters.WorkArea;
            Left = IsFinite(preferences.Left) ? preferences.Left : area.Right - Width - 32;
            Top = IsFinite(preferences.Top) ? preferences.Top : area.Bottom - Height - 24;
            KeepOnScreen();
        }

        private static bool IsFinite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }

        private IntPtr WindowHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // 拔插显示器或任务栏改变工作区后，自动把人物和气泡收回可见区域。
            if (message == 0x007E || message == 0x001A && wParam.ToInt64() == 0x002F)
                Dispatcher.BeginInvoke(new Action(delegate { KeepOnScreen(); PositionBubble(); SavePreferences(); }));
            // 透明空白处把鼠标交还给后面的窗口；按住拖动时继续接收松开事件。
            if (message == 0x0084 && !pressed && currentImage != null)
            {
                NativePoint cursor;
                if (GetCursorPos(out cursor))
                {
                    Point local = portrait.PointFromScreen(new Point(cursor.X, cursor.Y));
                    int x = (int)Math.Floor(local.X * currentImage.PixelWidth / portrait.Width);
                    int y = (int)Math.Floor(local.Y * currentImage.PixelHeight / portrait.Height);
                    bool transparent = x < 0 || x >= currentImage.PixelWidth || y < 0 || y >= currentImage.PixelHeight;
                    if (!transparent)
                    {
                        currentImage.CopyPixels(new Int32Rect(x, y, 1, 1), hitPixel, 4, 0);
                        transparent = hitPixel[3] < 12;
                        if (transparent && currentLimb != null)
                        {
                            currentLimb.CopyPixels(new Int32Rect(x, y, 1, 1), hitPixel, 4, 0);
                            transparent = hitPixel[3] < 12;
                        }
                    }
                    if (transparent)
                    {
                        handled = true;
                        return new IntPtr(-1);
                    }
                }
            }
            return IntPtr.Zero;
        }

        private void ApplySize()
        {
            double ratio = preferences.Size / CellWidth;
            Width = preferences.Size;
            Height = Headroom + CellHeight * ratio + 10;
            portrait.Width = preferences.Size;
            portrait.Height = CellHeight * ratio;
            Canvas.SetTop(portrait, Headroom);
            limbPortrait.Width = portrait.Width;
            limbPortrait.Height = portrait.Height;
            Canvas.SetTop(limbPortrait, Headroom);
            decorations.Width = portrait.Width;
            decorations.Height = portrait.Height;
            Canvas.SetTop(decorations, Headroom);
        }

        private void ResizeBy(double factor)
        {
            double previousWidth = Width, previousHeight = Height;
            preferences.Size = Math.Max(144, Math.Min(384, preferences.Size * factor));
            ApplySize();
            Left += (previousWidth - Width) / 2;
            Top += previousHeight - Height;
            KeepOnScreen();
            SavePreferences();
        }

        private void KeepOnScreen()
        {
            Rect area = SystemParameters.WorkArea;
            if (source != null)
            {
                System.Drawing.Rectangle pixels = Forms.Screen.FromHandle(source.Handle).WorkingArea;
                Matrix inverse = source.CompositionTarget.TransformFromDevice;
                Point corner = inverse.Transform(new Point(pixels.Left, pixels.Top));
                Point end = inverse.Transform(new Point(pixels.Right, pixels.Bottom));
                area = new Rect(corner, end);
            }
            Left = Math.Max(area.Left, Math.Min(area.Right - Width, Left));
            Top = Math.Max(area.Top - Headroom, Math.Min(area.Bottom - Height, Top));
        }

        private void OnPointerDown(object sender, MouseButtonEventArgs e)
        {
            NativePoint cursor;
            if (!GetCursorPos(out cursor)) return;
            pressed = true;
            artwork.PrepareAnimation("jump");
            dragging = false;
            pressScreen = new Point(cursor.X, cursor.Y);
            lastDragScreen = pressScreen;
            lastMotionScreen = pressScreen;
            pressLeft = Left;
            pressTop = Top;
            double now = clock.Elapsed.TotalSeconds;
            System.Drawing.Size tolerance = Forms.SystemInformation.DoubleClickSize;
            secondClick = pendingClickAt >= 0 && (now - pendingClickAt) * 1000 <= ClickWindowMilliseconds() &&
                Math.Abs(pressScreen.X - pendingClickScreen.X) <= Math.Max(6, tolerance.Width / 2) &&
                Math.Abs(pressScreen.Y - pendingClickScreen.Y) <= Math.Max(6, tolerance.Height / 2);
            if (secondClick) pendingClickAt = -1;
            else if (pendingClickAt >= 0) ConfirmSingleClick();
            CaptureMouse();
            e.Handled = true;
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!pressed) return;
            NativePoint cursor;
            if (!GetCursorPos(out cursor)) return;
            Point now = new Point(cursor.X, cursor.Y);
            Vector delta = now - pressScreen;
            if (!dragging && delta.Length < 6) return;
            if (!dragging)
            {
                ReturnToIdle();
                dragPhase = 0;
            }
            dragging = true;
            pendingClickAt = -1;
            secondClick = false;
            if ((now - lastMotionScreen).LengthSquared >= 1)
            {
                lastDragMotion = clock.Elapsed.TotalSeconds;
                lastMotionScreen = now;
            }
            double horizontal = now.X - lastDragScreen.X;
            if (Math.Abs(horizontal) > 2)
            {
                int direction = horizontal < 0 ? -1 : 1;
                if (direction != dragDirection || dragPhase == 0)
                    artwork.PrepareAnimation(direction > 0 ? "run-right" : "run-left");
                dragDirection = direction;
                lastDragScreen = now;
            }
            Matrix inverse = source.CompositionTarget.TransformFromDevice;
            Vector offset = inverse.Transform(delta);
            Left = pressLeft + offset.X;
            Top = pressTop + offset.Y;
        }

        private void OnPointerUp(object sender, MouseButtonEventArgs e)
        {
            if (!pressed) return;
            bool moved = dragging;
            pressed = false;
            dragging = false;
            ReleaseMouseCapture();
            if (moved) { ReturnToIdle(); KeepOnScreen(); SavePreferences(); }
            else if (secondClick)
            {
                StartAction(cycle[cycleIndex]);
                cycleIndex = (cycleIndex + 1) % cycle.Length;
                RegisterInteraction();
            }
            else
            {
                pendingClickAt = clock.Elapsed.TotalSeconds;
                pendingClickScreen = pressScreen;
                // 等待期间保持当前动作，避免双击之间夹入跳跃准备姿势。
            }
            secondClick = false;
            e.Handled = true;
        }

        private void OnPointerCaptureLost(object sender, MouseEventArgs e)
        {
            if ((!pressed && !dragging) || Mouse.Captured == this) return;
            bool moved = dragging;
            pressed = dragging = secondClick = false;
            pendingClickAt = -1;
            if (moved) { ReturnToIdle(); KeepOnScreen(); SavePreferences(); }
        }

        private void ReturnToIdle()
        {
            action = "idle";
            firstActionFrame = false;
            blinkStart = -1;
            gazeStrength = 0;
            gazeActive = false;
            lastGazeInside = double.NegativeInfinity;
            resumeGazeAt = clock.Elapsed.TotalSeconds + .10;
            decorations.Clear();
            currentLimb = null;
            limbPortrait.Source = null;
            movement.X = movement.Y = inclination.Angle = 0;
            breathing.ScaleY = 1;
            ShowFrame(frames[0, 0]);
            ScheduleBlink();
        }

        private void StartAction(string value)
        {
            pendingClickAt = -1;
            if (value == "idle") { ReturnToIdle(); return; }
            // 跳跃过程中不因连点重新起跳；其他动作可以随时切换。
            if (action == "jump" && value == "jump") return;
            action = value;
            actionStart = clock.Elapsed.TotalSeconds;
            firstActionFrame = true;
            artwork.PrepareAnimation(value);
            blinkStart = -1;
            gazeStrength = 0;
            gazeActive = false;
            lastGazeInside = double.NegativeInfinity;
        }

        private static double ClickWindowMilliseconds()
        {
            return Math.Min(ClickDelayMilliseconds, Forms.SystemInformation.DoubleClickTime);
        }

        private void ConfirmSingleClick()
        {
            pendingClickAt = -1;
            if (action != "jump")
            {
                StartAction("jump");
                // 判定已占用一小段时间，单击直接进入起跳阶段。
                actionStart -= JumpPreparation;
            }
            RegisterInteraction();
        }

        private void SetFrame(int row, int column)
        {
            ShowFrame(frames[row, column]);
            currentRow = row;
            currentColumn = column;
        }

        private void ShowFrame(BitmapSource image)
        {
            if (ReferenceEquals(currentImage, image)) return;
            portrait.Source = image;
            currentImage = image;
            currentRow = currentColumn = 0;
        }

        private void ScheduleBlink() { nextBlink = clock.Elapsed.TotalSeconds + 3.0 + random.NextDouble() * 3.5; }

        private void UpdateGaze(double time, double delta)
        {
            if (time < resumeGazeAt) return;
            NativePoint cursor;
            if (!GetCursorPos(out cursor)) return;
            Point point = portrait.PointFromScreen(new Point(cursor.X, cursor.Y));
            // 使用稳定的头部坐标与全局鼠标位置，透明轮廓变化不会再触发 MouseLeave 闪回待机。
            double dx = point.X * CellWidth / portrait.Width - 96;
            double dy = point.Y * CellHeight / portrait.Height - 86;
            double rx = gazeActive ? 100 : 82, ry = gazeActive ? 86 : 68;
            bool inside = dx * dx / (rx * rx) + dy * dy / (ry * ry) <= 1;
            if (inside) { gazeActive = true; lastGazeInside = time; }
            else if (time - lastGazeInside > 0.20) gazeActive = false;
            double target = gazeActive && dx * dx + dy * dy > 64 ? 1 : 0;
            gazeStrength += (target - gazeStrength) * (1 - Math.Exp(-delta / 0.11));
            if (target == 0) return;
            double angle = (Math.Atan2(dx, -dy) * 180 / Math.PI + 360) % 360;
            double difference = (angle - gazeAngle + 540) % 360 - 180;
            gazeAngle = (gazeAngle + difference * (1 - Math.Exp(-delta / 0.075)) + 360) % 360;
            double sector = 360.0 / FrameComposer.DirectionCount;
            gaze = (int)Math.Floor((gazeAngle + sector / 2) / sector) % FrameComposer.DirectionCount;
        }

        private void Render(object sender, EventArgs e)
        {
            RenderingEventArgs presentation = e as RenderingEventArgs;
            if (presentation == null || presentation.RenderingTime == lastPresentation) return;
            double delta = lastPresentation == TimeSpan.MinValue ? 1.0 / 60
                : Math.Max(0, Math.Min(.10, (presentation.RenderingTime - lastPresentation).TotalSeconds));
            lastPresentation = presentation.RenderingTime;
            double time = clock.Elapsed.TotalSeconds;
            if (pendingClickAt >= 0 && !pressed && (time - pendingClickAt) * 1000 >= ClickWindowMilliseconds())
                ConfirmSingleClick();
            UpdateBubble(time);
            // 每次实际绘制按时间选帧；不再叠加60/120Hz取整，避免与屏幕刷新节奏拍频。
            if (action != "idle")
            {
                // 首帧补上输入到绘制的时间；之后沿屏幕呈现时间推进，避免解压耗时扰动动作节奏。
                if (firstActionFrame) { actionTime = Math.Max(0, time - actionStart); firstActionFrame = false; }
                else actionTime += delta;
            }
            double elapsed = actionTime;
            double ratio = preferences.Size / CellWidth;
            movement.X = 0;
            movement.Y = 0;
            breathing.ScaleY = 1;
            inclination.Angle = 0;
            if (dragging || ExtraActions.Duration(action) == 0)
            {
                decorations.Clear();
                if (currentLimb != null) { currentLimb = null; limbPortrait.Source = null; }
            }
            if (dragging)
            {
                if (time - lastDragMotion < .12)
                {
                    dragPhase += delta;
                    ShowFrame(artwork.Animation(dragDirection > 0 ? "run-right" : "run-left", dragPhase));
                }
                else { dragPhase = 0; ShowFrame(frames[0, 0]); }
                return;
            }
            if (ExtraActions.Duration(action) > 0)
            {
                if (elapsed >= ExtraActions.Duration(action))
                {
                    ReturnToIdle();
                }
                else
                {
                    ExtraActionPose pose = ExtraActions.Sample(action, elapsed);
                    ShowFrame(artwork.ExtraBody(action, pose.Eye));
                    currentLimb = artwork.ExtraArm(action, elapsed);
                    limbPortrait.Source = currentLimb;
                    inclination.Angle = pose.Angle;
                    movement.Y = pose.OffsetY * ratio;
                    decorations.Update(pose);
                }
            }
            else if (action == "jump")
            {
                const double preparation = JumpPreparation, flight = 0.72, landing = 0.14;
                if (elapsed < preparation)
                {
                    ShowFrame(artwork.Animation("jump", elapsed));
                    double progress = elapsed / preparation;
                    movement.Y = -ratio * progress * progress * (3 - 2 * progress);
                }
                else if (elapsed < preparation + flight)
                {
                    double progress = (elapsed - preparation) / flight;
                    ShowFrame(artwork.Animation("jump", elapsed));
                    movement.Y = -ratio - 4 * 36 * ratio * progress * (1 - progress);
                }
                else if (elapsed < preparation + flight + landing)
                {
                    double progress = (elapsed - preparation - flight) / landing;
                    ShowFrame(artwork.Animation("jump", elapsed));
                    double settle = 1 - progress * progress * (3 - 2 * progress);
                    movement.Y = -ratio * settle + 1.4 * ratio * Math.Sin(progress * Math.PI);
                }
                else ReturnToIdle();
            }
            else if (action == "tiptoe")
            {
                if (elapsed >= 1.80) ReturnToIdle();
                else ShowFrame(artwork.Animation("tiptoe", elapsed));
            }
            else if (action == "waiting")
            {
                if (elapsed >= 4.84) ReturnToIdle();
                else ShowFrame(artwork.Animation("waiting", elapsed));
            }
            else if (action == "thinking")
            {
                if (elapsed >= 4.40) ReturnToIdle();
                else ShowFrame(artwork.Animation("thinking", elapsed));
            }
            else if (action == "review")
            {
                if (elapsed >= 5.06) ReturnToIdle();
                else ShowFrame(artwork.Animation("review", elapsed));
            }
            else if (action == "failed")
            {
                if (elapsed >= 4.10) ReturnToIdle();
                else ShowFrame(artwork.Animation("failed", elapsed));
            }
            else if (action == "shake")
            {
                if (elapsed < 2.6)
                {
                    ShowFrame(artwork.Animation("shake", elapsed));
                }
                else ReturnToIdle();
            }
            if (action == "idle")
            {
                UpdateGaze(time, delta);
                if (blinkStart < 0 && time >= nextBlink && gazeStrength < 0.06) blinkStart = time;
                if (blinkStart >= 0)
                {
                    double phase = time - blinkStart;
                    if (gazeStrength > 0.08) { blinkStart = -1; ScheduleBlink(); }
                    else if (phase < 0.26) ShowFrame(artwork.BlinkAt(phase));
                    else { blinkStart = -1; ScheduleBlink(); }
                }
                if (blinkStart < 0) ShowFrame(artwork.Gaze(gaze, (int)Math.Round(gazeStrength * FrameComposer.StrengthSteps)));
            }
        }

        private void RegisterInteraction()
        {
            // 这是功能必需的互动进度：一次单击或一次双击各计一次，拖动不计入。
            if (!preferences.Speech) return;
            interactions = Math.Min(interactions + 1, speechThreshold);
            // 正在说话时仍累计点击；留到气泡结束后的下一次互动再开口，避免打断和叠音。
            if (interactions < speechThreshold || bubble != null) return;
            interactions = 0;
            speechThreshold = random.Next(5, 8);
            int index;
            do { index = random.Next(phrases.Length); } while (index == previousPhrase);
            previousPhrase = index;
            if (bubble != null) bubble.Close();
            bubble = new SpeechBubble(this, phrases[index]);
            bubble.SizeChanged += delegate { PositionBubble(); };
            bubbleStarted = clock.Elapsed.TotalSeconds;
            PositionBubble();
            bubble.Show();
            bubbleDuration = Math.Max(4.8, voice.Play(index, preferences.Voice) + 0.55);
        }

        private void PositionBubble()
        {
            if (bubble == null) return;
            Rect area = SystemParameters.WorkArea;
            if (source != null)
            {
                System.Drawing.Rectangle pixels = Forms.Screen.FromHandle(source.Handle).WorkingArea;
                Matrix inverse = source.CompositionTarget.TransformFromDevice;
                area = new Rect(inverse.Transform(new Point(pixels.Left, pixels.Top)), inverse.Transform(new Point(pixels.Right, pixels.Bottom)));
            }
            double height = bubble.ActualHeight > 0 ? bubble.ActualHeight : 126;
            // 顶部空间不足就放到人物下方；留一点切换余量，拖动时不在上下两侧反复跳。
            bool placeBelow;
            Point location = BubbleDesign.Locate(area, new Rect(Left, Top + Headroom, Width, portrait.Height),
                new Size(bubble.Width, height), bubble.Below, out placeBelow);
            bubble.SetBelow(placeBelow);
            bubble.Left = location.X;
            bubble.Top = location.Y;
            bubble.Topmost = Topmost;
        }

        private void UpdateBubble(double time)
        {
            if (bubble == null) return;
            double elapsed = time - bubbleStarted;
            if (elapsed > bubbleDuration || !preferences.Speech) { bubble.Close(); bubble = null; voice.Stop(); return; }
            PositionBubble();
            bubble.Opacity = Math.Max(0, Math.Min(1, Math.Min(elapsed / 0.18, (bubbleDuration - elapsed) / 0.35)));
        }

        private void ToggleVoice()
        {
            preferences.Voice = !preferences.Voice;
            if (!preferences.Voice) voice.Stop();
            SavePreferences();
        }

        private void SetVoiceVolume(int value)
        {
            preferences.VoiceVolume = value;
            voice.SetVolume(value);
            SavePreferences();
        }

        private void ToggleCodexLink()
        {
            try { if (CodexLink.Enabled) CodexLink.Disable(); else CodexLink.Enable(); }
            catch (Exception error)
            {
                if (!(error is IOException) && !(error is UnauthorizedAccessException) && !(error is COMException)) throw;
                tray.ShowBalloonTip(4000, "小G", "启动联动暂时无法保存：" + error.Message, Forms.ToolTipIcon.Info);
            }
        }

        private void OpenMenu()
        {
            pendingClickAt = -1;
            ContextMenu menu = new ContextMenu();
            AddMenuItem(menu, "放大一点（5%）", delegate { ResizeBy(1.05); });
            AddMenuItem(menu, "缩小一点（5%）", delegate { ResizeBy(1 / 1.05); });
            MenuItem onTop = AddMenuItem(menu, "始终置顶", delegate { Topmost = !Topmost; SavePreferences(); });
            onTop.IsCheckable = true;
            onTop.IsChecked = Topmost;
            MenuItem speech = AddMenuItem(menu, "互动时冒出短句", delegate { preferences.Speech = !preferences.Speech; interactions = 0; if (!preferences.Speech) voice.Stop(); SavePreferences(); });
            speech.IsCheckable = true;
            speech.IsChecked = preferences.Speech;
            MenuItem spoken = AddMenuItem(menu, "小G奶萌语音", ToggleVoice);
            spoken.IsCheckable = true;
            spoken.IsChecked = preferences.Voice;
            MenuItem volume = new MenuItem { Header = "声音大小" };
            string[] volumeNames = { "轻声（25%）", "柔和（55%）", "响亮（80%）" };
            int[] volumes = { 25, 55, 80 };
            for (int i = 0; i < volumes.Length; i++)
            {
                int value = volumes[i];
                MenuItem item = new MenuItem { Header = volumeNames[i], IsCheckable = true, IsChecked = preferences.VoiceVolume == value };
                item.Click += delegate { menu.IsOpen = false; SetVoiceVolume(value); };
                volume.Items.Add(item);
            }
            menu.Items.Add(volume);
            MenuItem linked = AddMenuItem(menu, "随桌面 Codex 启动", ToggleCodexLink);
            linked.IsCheckable = true;
            linked.IsChecked = CodexLink.Enabled;
            menu.Items.Add(new Separator());
            MenuItem actions = new MenuItem { Header = "播放动作" };
            string[] names = { "跳一下", "踮踮脚", "等等你", "想一想", "检查一下", "小沮丧", "摇摇头", "困困打盹", "灵光一闪", "回到待机" };
            string[] values = { "jump", "tiptoe", "waiting", "thinking", "review", "failed", "shake", "sleepy", "idea", "idle" };
            for (int i = 0; i < names.Length; i++)
            {
                string value = values[i];
                MenuItem item = new MenuItem { Header = names[i] };
                item.Click += delegate { menu.IsOpen = false; StartAction(value); };
                actions.Items.Add(item);
            }
            menu.Items.Add(actions);
            menu.Items.Add(new Separator());
            AddMenuItem(menu, "退出小G", Close);
            menu.PlacementTarget = portrait;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            menu.IsOpen = true;
        }

        private static MenuItem AddMenuItem(ContextMenu menu, string label, Action handler)
        {
            MenuItem item = new MenuItem { Header = label };
            item.Click += delegate { menu.IsOpen = false; handler(); };
            menu.Items.Add(item);
            return item;
        }

        private void CreateTray()
        {
            tray = new Forms.NotifyIcon();
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("xiaog.ico"))
                tray.Icon = new System.Drawing.Icon(stream);
            tray.Text = "小G：单击跳跃 · 双击换动作 · 拖动奔跑";
            Forms.ContextMenuStrip menu = new Forms.ContextMenuStrip();
            menu.Items.Add("把小G移回主屏", null, delegate { Dispatcher.Invoke(new Action(ReturnHome)); });
            menu.Items.Add("全部设置和动作…", null, delegate { Dispatcher.BeginInvoke(new Action(OpenMenu)); });
            menu.Items.Add(new Forms.ToolStripSeparator());
            Forms.ToolStripMenuItem keepOnTop = new Forms.ToolStripMenuItem("始终置顶");
            keepOnTop.Checked = Topmost;
            keepOnTop.Click += delegate { Dispatcher.Invoke(new Action(delegate { Topmost = !Topmost; keepOnTop.Checked = Topmost; SavePreferences(); })); };
            menu.Opening += delegate { keepOnTop.Checked = Topmost; };
            menu.Items.Add(keepOnTop);
            Forms.ToolStripMenuItem spoken = new Forms.ToolStripMenuItem("小G奶萌语音");
            spoken.Click += delegate { Dispatcher.Invoke(new Action(ToggleVoice)); };
            menu.Opening += delegate { spoken.Checked = preferences.Voice; };
            menu.Items.Add(spoken);
            Forms.ToolStripMenuItem linked = new Forms.ToolStripMenuItem("随桌面 Codex 启动");
            linked.Click += delegate { Dispatcher.Invoke(new Action(ToggleCodexLink)); };
            menu.Opening += delegate { linked.Checked = CodexLink.Enabled; };
            menu.Items.Add(linked);
            menu.Items.Add("踮踮脚", null, delegate { Dispatcher.Invoke(new Action(delegate { StartAction("tiptoe"); })); });
            menu.Items.Add("等等你", null, delegate { Dispatcher.Invoke(new Action(delegate { StartAction("waiting"); })); });
            menu.Items.Add("想一想", null, delegate { Dispatcher.Invoke(new Action(delegate { StartAction("thinking"); })); });
            menu.Items.Add("检查一下", null, delegate { Dispatcher.Invoke(new Action(delegate { StartAction("review"); })); });
            menu.Items.Add("小沮丧", null, delegate { Dispatcher.Invoke(new Action(delegate { StartAction("failed"); })); });
            menu.Items.Add("摇摇头", null, delegate { Dispatcher.Invoke(new Action(delegate { StartAction("shake"); })); });
            menu.Items.Add("困困打盹", null, delegate { Dispatcher.Invoke(new Action(delegate { StartAction("sleepy"); })); });
            menu.Items.Add("灵光一闪", null, delegate { Dispatcher.Invoke(new Action(delegate { StartAction("idea"); })); });
            menu.Items.Add("退出小G", null, delegate { Dispatcher.Invoke(new Action(Close)); });
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { Dispatcher.Invoke(new Action(ReturnHome)); };
            tray.Visible = true;
        }

        private void ReturnHome()
        {
            Rect area = SystemParameters.WorkArea;
            Left = area.Right - Width - 32;
            Top = area.Bottom - Height - 24;
            SavePreferences();
        }
    }

    internal sealed class SpeechBubble : Window
    {
        private readonly BubbleCard card;
        internal bool Below { get { return card.Below; } }
        internal void SetBelow(bool value) { card.SetBelow(value); }
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr window, int index, int value);

        internal SpeechBubble(Window owner, string text)
        {
            Owner = owner;
            Title = "小G的悄悄话";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Width = BubbleDesign.Width;
            SizeToContent = SizeToContent.Height;
            Opacity = 0;
            card = BubbleDesign.Create(text);
            Content = card;
            SourceInitialized += delegate
            {
                HwndSource source = (HwndSource)PresentationSource.FromVisual(this);
                SetWindowLong(source.Handle, -20, GetWindowLong(source.Handle, -20) | 0x08000000 | 0x00000080 | 0x00000020);
                source.AddHook(delegate(IntPtr h, int message, IntPtr w, IntPtr l, ref bool handled)
                { if (message == 0x0084) { handled = true; return new IntPtr(-1); } return IntPtr.Zero; });
            };
        }
    }
}
