using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Liuguang.UI
{
    /// <summary>
    /// 全局补间动画引擎：一个共享 Timer 驱动所有注册的补间。
    ///
    /// 之前所有控件的 hover / 按下 / 开关都是「布尔值瞬切」：鼠标一进按钮，
    /// 颜色「啪」地跳；一点开关，滑块「咔」地瞬移。那叫「有状态」，不叫「有动画」。
    /// 液态玻璃讲的是流动感，过渡必须连续，不能跳。
    ///
    /// 每段补间是一段「指数逼近」：每帧 current += (target - current) * rate，
    /// rate 越大收敛越快，视觉是「起手快、落定缓」的 ease-out，和 CSS 一致。
    /// 收敛到误差 < 0.002 自动移除；全部收敛后停表，不空转。
    ///
    /// 用法：控件持有一个 <see cref="AnimFloat"/>，状态变化时
    ///   anim.Target = 1f; Anime.Sync(this, anim);
    /// 绘制时读 anim.Value。Anime 负责推进值并触发 Invalidate。
    /// </summary>
    internal static class Anime
    {
        private class Entry
        {
            public Control Owner;
            public AnimFloat Anim;
        }

        private static readonly List<Entry> entries = new List<Entry>();
        private static Timer timer;

        /// <summary>让 owner 持有的 anim 向它的 Target 平滑推进，并在每帧重绘 owner。</summary>
        public static void Sync(Control owner, AnimFloat anim)
        {
            if (owner == null || anim == null) return;
            // 去重：同一 (owner, anim) 只留一条
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Owner == owner && entries[i].Anim == anim)
                {
                    EnsureTimer();
                    return;
                }
            }
            Entry e = new Entry();
            e.Owner = owner;
            e.Anim = anim;
            entries.Add(e);
            EnsureTimer();
        }

        private static void EnsureTimer()
        {
            if (timer != null) return;
            timer = new Timer();
            timer.Interval = 15;
            timer.Tick += new EventHandler(OnTick);
            timer.Start();
        }

        private static void OnTick(object sender, EventArgs e)
        {
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                Entry en = entries[i];
                if (en.Owner == null || en.Owner.IsDisposed)
                {
                    entries.RemoveAt(i);
                    continue;
                }
                AnimFloat anim = en.Anim;
                float next = anim.Value + (anim.Target - anim.Value) * anim.Rate;
                if (Math.Abs(next - anim.Target) < 0.002f) next = anim.Target;
                anim.Value = next;
                try
                {
                    en.Owner.Invalidate();
                }
                catch
                {
                    entries.RemoveAt(i);
                    continue;
                }
                if (next == anim.Target)
                {
                    entries.RemoveAt(i);
                }
            }
            if (entries.Count == 0 && timer != null)
            {
                timer.Stop();
                timer.Dispose();
                timer = null;
            }
        }
    }

    /// <summary>一个带缓动补间的 float 状态。控件持有它，绘制时读 Value 而非布尔。</summary>
    internal sealed class AnimFloat
    {
        public float Value;
        public float Target;
        public float Rate = 0.24f;

        public AnimFloat()
        {
            Value = 0f;
            Target = 0f;
        }

        public AnimFloat(float rate)
        {
            Value = 0f;
            Target = 0f;
            Rate = rate;
        }
    }
}
