using System;
using System.Windows.Controls;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace XiaoGDesktop
{
    public sealed partial class Companion
    {
        private double focusEndsAt = -1;
        private int focusMinutes;
        private bool focusReminderPending;
        private DispatcherTimer focusTimer;

        private void StartFocus(int minutes)
        {
            WakeForInteraction();
            focusMinutes = minutes;
            focusEndsAt = clock.Elapsed.TotalSeconds + minutes * 60;
            focusReminderPending = false;
            // 计时不依赖人物是否正在绘制；窗口被遮挡时也按真实经过时间检查。
            if (focusTimer == null)
            {
                focusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                focusTimer.Tick += delegate
                {
                    double time = clock.Elapsed.TotalSeconds;
                    UpdateBubble(time);
                    UpdateFocus(time);
                };
            }
            focusTimer.Start();
        }

        private void EndFocus()
        {
            focusEndsAt = -1;
            focusReminderPending = false;
            if (focusTimer != null) focusTimer.Stop();
            ScheduleIdle();
        }

        private void UpdateFocus(double time)
        {
            if (focusEndsAt >= 0 && time >= focusEndsAt)
            {
                focusEndsAt = -1;
                focusReminderPending = true;
            }
            if (!focusReminderPending || bubble != null) return;

            // 等已有语录说完再提醒；不改变语录、声音开关，也不重复打断用户。
            focusReminderPending = false;
            if (focusTimer != null) focusTimer.Stop();
            WakeForInteraction();
            if (preferences.Speech) SpeakPhrase(7);
            else if (tray != null)
                tray.ShowBalloonTip(5000, "小G · 专注完成",
                    focusMinutes + "分钟的专注结束啦，可以休息一下了。", Forms.ToolTipIcon.None);
        }

        private string FocusStatus()
        {
            if (focusReminderPending) return "专注已完成，等待当前语录结束后提醒";
            if (focusEndsAt < 0) return "尚未开始；退出小G会结束本次计时";
            int remaining = (int)Math.Max(0, Math.Ceiling(focusEndsAt - clock.Elapsed.TotalSeconds));
            return "剩余 " + (remaining / 60).ToString("00") + ":" + (remaining % 60).ToString("00");
        }

        private MenuItem CreateFocusMenu()
        {
            MenuItem root = new MenuItem { Header = "专注计时" };
            MenuItem status = new MenuItem { Header = FocusStatus(), IsEnabled = false };
            root.Items.Add(status);
            root.Items.Add(new Separator());
            foreach (int duration in new int[] { 25, 50 })
            {
                int minutes = duration;
                MenuItem start = new MenuItem {
                    Header = (focusEndsAt >= 0 ? "重新开始 " : "开始 ") + minutes + " 分钟"
                };
                start.Click += delegate { StartFocus(minutes); };
                root.SubmenuOpened += delegate {
                    start.Header = (focusEndsAt >= 0 ? "重新开始 " : "开始 ") + minutes + " 分钟";
                };
                root.Items.Add(start);
            }
            MenuItem stop = new MenuItem {
                Header = "提前结束，不再提醒", IsEnabled = focusEndsAt >= 0 || focusReminderPending
            };
            stop.Click += delegate { EndFocus(); };
            root.Items.Add(stop);
            root.SubmenuOpened += delegate {
                status.Header = FocusStatus();
                stop.IsEnabled = focusEndsAt >= 0 || focusReminderPending;
            };
            return root;
        }

        private Forms.ToolStripMenuItem CreateFocusTrayMenu()
        {
            Forms.ToolStripMenuItem root = new Forms.ToolStripMenuItem("专注计时");
            Forms.ToolStripMenuItem status = new Forms.ToolStripMenuItem { Enabled = false };
            Forms.ToolStripMenuItem start25 = new Forms.ToolStripMenuItem();
            Forms.ToolStripMenuItem start50 = new Forms.ToolStripMenuItem();
            Forms.ToolStripMenuItem stop = new Forms.ToolStripMenuItem("提前结束，不再提醒");
            root.DropDownItems.Add(status);
            root.DropDownItems.Add(new Forms.ToolStripSeparator());
            root.DropDownItems.Add(start25);
            root.DropDownItems.Add(start50);
            root.DropDownItems.Add(stop);
            start25.Click += delegate { Dispatcher.Invoke(new Action(delegate { StartFocus(25); })); };
            start50.Click += delegate { Dispatcher.Invoke(new Action(delegate { StartFocus(50); })); };
            stop.Click += delegate { Dispatcher.Invoke(new Action(EndFocus)); };
            root.DropDownOpening += delegate
            {
                Dispatcher.Invoke(new Action(delegate
                {
                    status.Text = FocusStatus();
                    string prefix = focusEndsAt >= 0 ? "重新开始 " : "开始 ";
                    start25.Text = prefix + "25 分钟";
                    start50.Text = prefix + "50 分钟";
                    stop.Enabled = focusEndsAt >= 0 || focusReminderPending;
                }));
            };
            // 首次展开前也提供完整文案，避免工具条测量空标题。
            status.Text = FocusStatus();
            start25.Text = "开始 25 分钟";
            start50.Text = "开始 50 分钟";
            stop.Enabled = false;
            return root;
        }
    }
}
