using System;
using System.Windows.Controls;
using Forms = System.Windows.Forms;

namespace XiaoGDesktop
{
    public sealed partial class Companion
    {
        private enum CompanionMode { Natural, Quiet, Rest }
        private CompanionMode companionMode;
        private double idleDueAt, glanceStarted = -1, restStarted = -1, wakeStarted = -1;
        private double glanceAngle, glanceAmount, wakeClosure, restClosure, eyeClosure, restGlyphAge;
        private double lastAttentionAt, nextIdleGestureAt;
        private int idleChoice, previousIdleGesture = -1;
        private bool idlePrepared, idleAction, menuOpen, glanceRight;

        private CompanionMode AwakeMode { get { return preferences.NaturalIdle ? CompanionMode.Natural : CompanionMode.Quiet; } }

        private static double Gentle(double value)
        {
            value = Math.Max(0, Math.Min(1, value));
            return value * value * value * (value * (value * 6 - 15) + 10);
        }

        private void ScheduleIdle()
        {
            double time = clock.Elapsed.TotalSeconds;
            bool settled = time - lastAttentionAt > 180;
            idleDueAt = time + (settled ? 32 : 22) + random.NextDouble() * (settled ? 26 : 24);
            idlePrepared = false;
            // 完整动作之间留出安静时间；长时间没有互动时更多地轻轻看四周。
            if (time < nextIdleGestureAt) { idleChoice = 0; return; }
            do
            {
                double pick = random.NextDouble();
                idleChoice = settled ? (pick < .70 ? 0 : pick < .80 ? 1 : pick < .96 ? 2 : 3)
                    : (pick < .50 ? 0 : pick < .73 ? 1 : pick < .92 ? 2 : 3);
            } while (idleChoice != 0 && idleChoice == previousIdleGesture);
        }

        private string IdleAnimation()
        {
            return idleChoice == 1 ? "tiptoe" : idleChoice == 2 ? "sleepy" : "thinking";
        }

        private void ResetIdleBehavior()
        {
            companionMode = AwakeMode;
            idleAction = false;
            glanceStarted = restStarted = wakeStarted = -1;
            eyeClosure = restGlyphAge = 0;
            ScheduleIdle();
        }

        private void WakeForInteraction()
        {
            lastAttentionAt = clock.Elapsed.TotalSeconds;
            ResumeCompanion(AwakeMode);
        }

        // 所有唤醒入口共用这个状态转换；陪伴模式不能再与休息状态并存。
        private void ResumeCompanion(CompanionMode mode)
        {
            if (idleAction) ReturnToIdle();
            if (pendingAction == "rest") pendingAction = null;
            if (companionMode == CompanionMode.Rest && restStarted >= 0 && wakeStarted < 0)
            {
                // 从实际已经显示的眼睑位置继续，快速反复切换也不会先睁开再闭上。
                wakeClosure = eyeClosure;
                wakeStarted = clock.Elapsed.TotalSeconds;
                blinkStart = -1;
                gazeActive = false;
                gazeStrength = 0;
                artwork.ResetGaze();
            }
            companionMode = mode;
            // 保留当前头部朝向，由鼠标注视的阻尼自然接过去。
            glanceStarted = -1;
            ScheduleIdle();
        }

        private void SelectCompanionMode(CompanionMode mode)
        {
            pendingClickAt = -1;
            lastAttentionAt = clock.Elapsed.TotalSeconds;
            if (mode == CompanionMode.Rest) { BeginRest(); return; }
            bool changed = preferences.NaturalIdle != (mode == CompanionMode.Natural);
            preferences.NaturalIdle = mode == CompanionMode.Natural;
            ResumeCompanion(mode);
            if (changed) SavePreferences();
        }

        private void BeginRest()
        {
            pendingClickAt = -1;
            if (companionMode == CompanionMode.Rest && restStarted >= 0) return;
            // 不能把空中的小G瞬间切成闭眼站立；落地后才开始休息。
            if (action == "jump") { companionMode = CompanionMode.Rest; pendingAction = "rest"; return; }
            double closure = eyeClosure;
            ReturnToIdle();
            companionMode = CompanionMode.Rest;
            restClosure = closure;
            eyeClosure = closure;
            restStarted = clock.Elapsed.TotalSeconds;
            ShowFrame(artwork.RestEyes(closure));
        }

        private bool RenderRest(double time)
        {
            if (companionMode == CompanionMode.Rest && restStarted >= 0)
            {
                double elapsed = time - restStarted;
                double duration = .20 + .32 * (1 - restClosure);
                ShowRestEyes(restClosure + (1 - restClosure) * Gentle(elapsed / duration));
                restGlyphAge = elapsed;
                decorations.Update(new ExtraActionPose { Name = "rest", Time = elapsed, Strength = eyeClosure });
                return true;
            }
            if (wakeStarted < 0) return false;
            double phase = (time - wakeStarted) / (.20 + .12 * wakeClosure);
            if (phase < 1)
            {
                ShowRestEyes(wakeClosure * (1 - Gentle(phase)));
                decorations.Update(new ExtraActionPose { Name = "rest", Time = restGlyphAge + Math.Max(0, time - wakeStarted), Strength = eyeClosure });
                return true;
            }
            restStarted = wakeStarted = -1;
            eyeClosure = restGlyphAge = 0;
            decorations.Clear();
            // 明确交还睁眼的中性画面，不能等待下一次转头或眨眼才恢复。
            ShowFrame(frames[0, 0]);
            resumeGazeAt = time + .10;
            ScheduleBlink();
            return false;
        }

        private void ShowRestEyes(double closure)
        {
            eyeClosure = Math.Round(Math.Max(0, Math.Min(1, closure)) * BlinkSequence.HalfSteps) / BlinkSequence.HalfSteps;
            ShowFrame(artwork.RestEyes(eyeClosure));
        }

        private void UpdateIdleBehavior(double time)
        {
            bool busy = pressed || pendingClickAt >= 0 || menuOpen || bubble != null
                || focusEndsAt >= 0 || focusReminderPending;
            if (glanceStarted >= 0)
            {
                if (busy || gazeActive || companionMode != CompanionMode.Natural)
                {
                    glanceStarted = -1;
                    ScheduleIdle();
                    return;
                }
                double t = time - glanceStarted;
                if (t >= 3.2)
                {
                    glanceStarted = -1;
                    ScheduleIdle();
                    return;
                }
                // 先转向、稍停，再沿原方向回正；幅度只用手动注视的一小部分。
                double envelope = Gentle(t / .75) * Gentle((3.2 - t) / .90);
                double drift = 7 * Math.Sin(t * Math.PI / 3.2);
                gazeAngle = (glanceAngle + drift + 360) % 360;
                gaze = (int)Math.Round(gazeAngle * FrameComposer.DirectionCount / 360) % FrameComposer.DirectionCount;
                gazeStrength = glanceAmount * envelope;
                return;
            }
            if (companionMode != CompanionMode.Natural) return;
            if (busy || gazeActive || gazeStrength > .03 || blinkStart >= 0)
            {
                // 注意力在用户这里时，不突然开始自己的表演，也不积压待机动作。
                if (idleDueAt < time + 5) idleDueAt = time + 5;
                return;
            }
            if (!idlePrepared && idleChoice != 0 && time >= idleDueAt - 1)
            {
                artwork.PrepareAnimation(IdleAnimation());
                idlePrepared = true;
            }
            if (time < idleDueAt) return;
            if (idleChoice == 0)
            {
                glanceStarted = time;
                // 轻轻看看左上或右上，不使用容易夸张的满幅侧转。
                glanceRight = !glanceRight;
                glanceAngle = glanceRight ? 62 : 298;
                glanceAmount = .26 + random.NextDouble() * .10;
            }
            else
            {
                previousIdleGesture = idleChoice;
                string next = IdleAnimation();
                nextIdleGestureAt = time + 70;
                StartAction(next);
                idleAction = true;
            }
        }

        private string CompanionStatus()
        {
            if (companionMode == CompanionMode.Rest)
                return restStarted >= 0 ? "当前：休息，点击即可叫醒" : "当前：休息，落地后小憩";
            string name = companionMode == CompanionMode.Natural ? "自然陪伴" : "安静陪伴";
            if (wakeStarted >= 0) return "当前：" + name + "，正在睁眼";
            if (focusEndsAt >= 0 || focusReminderPending) return "当前：" + name + "，专注期间暂时安静";
            return "当前：" + name;
        }

        private static string ModeLabel(CompanionMode mode)
        {
            return mode == CompanionMode.Natural ? "自然陪伴（偶尔做小动作）"
                : mode == CompanionMode.Quiet ? "安静陪伴（眨眼、看着你）" : "休息（闭眼小憩）";
        }

        private MenuItem CreateCompanionMenu()
        {
            MenuItem root = new MenuItem { Header = "陪伴模式" };
            MenuItem status = new MenuItem { Header = CompanionStatus(), IsEnabled = false };
            root.Items.Add(status);
            root.Items.Add(new Separator());
            foreach (CompanionMode value in Enum.GetValues(typeof(CompanionMode)))
            {
                CompanionMode mode = value;
                MenuItem item = new MenuItem { Header = ModeLabel(mode), IsCheckable = true, IsChecked = companionMode == mode };
                item.Click += delegate { SelectCompanionMode(mode); };
                root.Items.Add(item);
                root.SubmenuOpened += delegate { item.IsChecked = companionMode == mode; };
            }
            root.SubmenuOpened += delegate { status.Header = CompanionStatus(); };
            root.Items.Add(new Separator());
            root.Items.Add(CreateFocusMenu());
            root.Items.Add(new MenuItem { Header = "专注时自动安静，到时唤醒提醒", IsEnabled = false });
            return root;
        }

        private Forms.ToolStripMenuItem CreateCompanionTrayMenu()
        {
            Forms.ToolStripMenuItem root = new Forms.ToolStripMenuItem("陪伴模式");
            Forms.ToolStripMenuItem status = new Forms.ToolStripMenuItem { Text = CompanionStatus(), Enabled = false };
            root.DropDownItems.Add(status);
            root.DropDownItems.Add(new Forms.ToolStripSeparator());
            foreach (CompanionMode value in Enum.GetValues(typeof(CompanionMode)))
            {
                CompanionMode mode = value;
                Forms.ToolStripMenuItem item = new Forms.ToolStripMenuItem(ModeLabel(mode)) { Checked = companionMode == mode };
                item.Click += delegate { Dispatcher.Invoke(new Action(delegate { SelectCompanionMode(mode); })); };
                root.DropDownItems.Add(item);
                root.DropDownOpening += delegate { item.Checked = companionMode == mode; };
            }
            root.DropDownOpening += delegate { status.Text = CompanionStatus(); };
            root.DropDownItems.Add(new Forms.ToolStripSeparator());
            root.DropDownItems.Add(CreateFocusTrayMenu());
            return root;
        }
    }
}
