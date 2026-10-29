using System;
using System.Windows.Controls;
using Forms = System.Windows.Forms;

namespace XiaoGDesktop
{
    public sealed partial class Companion
    {
        private double idleDueAt, glanceStarted = -1, restStarted = -1, wakeStarted = -1;
        private double glanceAngle, glanceAmount, wakeClosure;
        private int idleChoice, previousIdleChoice = -1;
        private bool idlePrepared, idleAction, menuOpen;

        private static double Gentle(double value)
        {
            value = Math.Max(0, Math.Min(1, value));
            return value * value * value * (value * (value * 6 - 15) + 10);
        }

        private void ScheduleIdle()
        {
            idleDueAt = clock.Elapsed.TotalSeconds + 22 + random.NextDouble() * 24;
            idlePrepared = false;
            // 看四周最常见，避免短时间内重复同一个完整动作。
            do
            {
                double pick = random.NextDouble();
                idleChoice = pick < .50 ? 0 : pick < .73 ? 1 : pick < .92 ? 2 : 3;
            } while (idleChoice == previousIdleChoice);
        }

        private string IdleAnimation()
        {
            return idleChoice == 1 ? "tiptoe" : idleChoice == 2 ? "sleepy" : "thinking";
        }

        private void ResetIdleBehavior()
        {
            idleAction = false;
            glanceStarted = restStarted = wakeStarted = -1;
            ScheduleIdle();
        }

        private void WakeForInteraction()
        {
            double time = clock.Elapsed.TotalSeconds;
            if (idleAction) ReturnToIdle();
            if (restStarted >= 0)
            {
                wakeClosure = Gentle((time - restStarted) / .48);
                restStarted = -1;
                wakeStarted = time;
                decorations.Clear();
            }
            // 保留当前头部朝向，由鼠标注视的阻尼自然接过去。
            glanceStarted = -1;
            ScheduleIdle();
        }

        private void BeginRest()
        {
            pendingClickAt = -1;
            // 不能把空中的小G瞬间切成闭眼站立；落地后才开始休息。
            if (action == "jump") { pendingAction = "rest"; return; }
            ReturnToIdle();
            restStarted = clock.Elapsed.TotalSeconds;
        }

        private bool RenderRest(double time)
        {
            if (restStarted >= 0)
            {
                double elapsed = time - restStarted;
                double amount = Gentle(elapsed / .48);
                ShowFrame(artwork.RestEyes(amount));
                decorations.Update(new ExtraActionPose { Name = "rest", Time = elapsed, Strength = amount });
                return true;
            }
            if (wakeStarted < 0) return false;
            double phase = (time - wakeStarted) / .24;
            if (phase < 1)
            {
                ShowFrame(artwork.RestEyes(wakeClosure * (1 - Gentle(phase))));
                return true;
            }
            wakeStarted = -1;
            ScheduleBlink();
            return false;
        }

        private void UpdateIdleBehavior(double time)
        {
            bool busy = pressed || pendingClickAt >= 0 || menuOpen || bubble != null
                || focusEndsAt >= 0 || focusReminderPending;
            if (glanceStarted >= 0)
            {
                if (busy || gazeActive || !preferences.NaturalIdle)
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
            if (!preferences.NaturalIdle) return;
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
            previousIdleChoice = idleChoice;
            if (idleChoice == 0)
            {
                glanceStarted = time;
                // 轻轻看看左上或右上，不使用容易夸张的满幅侧转。
                glanceAngle = random.Next(2) == 0 ? 62 : 298;
                glanceAmount = .28 + random.NextDouble() * .12;
            }
            else
            {
                string next = IdleAnimation();
                StartAction(next);
                idleAction = true;
            }
        }

        private void ToggleNaturalIdle()
        {
            preferences.NaturalIdle = !preferences.NaturalIdle;
            if (idleAction) ReturnToIdle();
            glanceStarted = -1;
            ScheduleIdle();
            SavePreferences();
        }

        private string RestLabel()
        {
            return restStarted >= 0 || pendingAction == "rest" ? "叫醒小G" : "让小G小憩一下";
        }

        private void ToggleRest()
        {
            if (restStarted >= 0 || pendingAction == "rest")
            {
                if (pendingAction == "rest") pendingAction = null;
                WakeForInteraction();
            }
            else BeginRest();
        }

        private MenuItem CreateIdleMenu()
        {
            MenuItem root = new MenuItem { Header = "待机陪伴" };
            MenuItem natural = new MenuItem {
                Header = "自然待机（偶尔做小动作）", IsCheckable = true, IsChecked = preferences.NaturalIdle
            };
            natural.Click += delegate { ToggleNaturalIdle(); };
            root.Items.Add(natural);
            root.Items.Add(new MenuItem { Header = "专注计时时暂停自发小动作", IsEnabled = false });
            root.Items.Add(new Separator());
            MenuItem rest = new MenuItem { Header = RestLabel() };
            rest.Click += delegate { ToggleRest(); };
            root.Items.Add(rest);
            root.Items.Add(new MenuItem { Header = "小憩时点一下即可叫醒并跳跃", IsEnabled = false });
            return root;
        }

        private Forms.ToolStripMenuItem CreateIdleTrayMenu()
        {
            Forms.ToolStripMenuItem root = new Forms.ToolStripMenuItem("待机陪伴");
            Forms.ToolStripMenuItem natural = new Forms.ToolStripMenuItem("自然待机（偶尔做小动作）");
            Forms.ToolStripMenuItem rest = new Forms.ToolStripMenuItem(RestLabel());
            natural.Click += delegate { Dispatcher.Invoke(new Action(ToggleNaturalIdle)); };
            rest.Click += delegate { Dispatcher.Invoke(new Action(ToggleRest)); };
            root.DropDownItems.Add(natural);
            root.DropDownItems.Add(new Forms.ToolStripSeparator());
            root.DropDownItems.Add(rest);
            root.DropDownOpening += delegate
            {
                natural.Checked = preferences.NaturalIdle;
                rest.Text = RestLabel();
            };
            return root;
        }
    }
}
