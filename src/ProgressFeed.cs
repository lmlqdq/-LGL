using System;
using System.Collections.Generic;
using System.Globalization;

namespace Liuguang.Core
{
    /// <summary>
    /// 下载过程的明细来源。状态栏只有一个百分比，用户看不到「在下什么、下到哪、卡没卡住」，
    /// 点开状态栏看到的这堆信息就来自这里。
    ///
    /// 所有写入都来自下载线程，读取来自 UI 线程，所以全部走同一把锁；
    /// 只保存字符串和计数，不做任何 UI 操作。
    /// </summary>
    public static class ProgressFeed
    {
        private static readonly object Gate = new object();

        private static string label = "";
        private static int total;
        private static int done;
        private static int failed;
        private static long bytes;
        private static DateTime started = DateTime.Now;
        private static readonly List<string> active = new List<string>();
        private static readonly List<string> recent = new List<string>();
        private static readonly List<string> errors = new List<string>();

        /// <summary>换阶段（校验 / 下载 / 安装 Java）时调用，会清空上一阶段的明细</summary>
        public static void SetLabel(string text)
        {
            if (text == null || text.Length == 0) return;
            lock (Gate)
            {
                if (label == text) return;
                label = text;
                total = 0;
                done = 0;
                failed = 0;
                bytes = 0;
                started = DateTime.Now;
                active.Clear();
                recent.Clear();
                errors.Clear();
            }
        }

        /// <summary>一批下载开始前登记总量</summary>
        public static void Plan(int fileCount)
        {
            lock (Gate)
            {
                if (fileCount <= 0) return;
                total = fileCount;
                done = 0;
                failed = 0;
                active.Clear();
                if (label.Length == 0) label = "下载游戏文件";
            }
        }

        public static void StartFile(string name)
        {
            if (name == null || name.Length == 0) return;
            lock (Gate)
            {
                if (!active.Contains(name)) active.Add(name);
            }
        }

        public static void EndFile(string name, bool ok)
        {
            lock (Gate)
            {
                if (name == null) name = "";
                active.Remove(name);
                done++;
                if (!ok) failed++;
                string line = (ok ? "✓ " : "✗ ") + name;
                recent.Insert(0, line);
                while (recent.Count > 14) recent.RemoveAt(recent.Count - 1);
            }
        }

        public static void Fail(string name, string reason)
        {
            lock (Gate)
            {
                string line = name;
                if (reason != null && reason.Length > 0) line += "  ·  " + reason;
                errors.Insert(0, line);
                while (errors.Count > 8) errors.RemoveAt(errors.Count - 1);
            }
        }

        public static void AddBytes(long count)
        {
            if (count <= 0) return;
            lock (Gate) bytes += count;
        }

        public static void Reset()
        {
            lock (Gate)
            {
                label = "";
                total = 0;
                done = 0;
                failed = 0;
                bytes = 0;
                started = DateTime.Now;
                active.Clear();
                recent.Clear();
                errors.Clear();
            }
        }

        /// <summary>给详情面板用的多行文本</summary>
        public static string[] Render()
        {
            lock (Gate)
            {
                double seconds = (DateTime.Now - started).TotalSeconds;
                if (seconds < 0.2) seconds = 0.2;

                List<string> lines = new List<string>();
                lines.Add(label.Length > 0 ? label : "当前没有进行中的任务");

                if (total > 0)
                {
                    int percent = (int)(done * 100.0 / total);
                    if (percent > 100) percent = 100;
                    string p = "进度    " + done + " / " + total + " 个文件    " + percent + "%";
                    if (failed > 0) p += "    失败 " + failed;
                    lines.Add(p);
                }
                else
                {
                    lines.Add("进度    准备中");
                }

                lines.Add("速度    " + Size((long)(bytes / seconds)) + "/s    已传输 " + Size(bytes) + "    用时 " + Clock((int)seconds));

                lines.Add("");
                lines.Add("正在下载（线程 " + active.Count + "）");
                if (active.Count == 0) lines.Add("  ——");
                for (int i = 0; i < active.Count && i < 8; i++) lines.Add("  " + active[i]);

                if (errors.Count > 0)
                {
                    lines.Add("");
                    lines.Add("失败 " + errors.Count + " 项");
                    for (int i = 0; i < errors.Count && i < 4; i++) lines.Add("  " + errors[i]);
                }
                else if (recent.Count > 0)
                {
                    lines.Add("");
                    lines.Add("最近完成");
                    for (int i = 0; i < recent.Count && i < 8; i++) lines.Add("  " + recent[i]);
                }

                return lines.ToArray();
            }
        }

        public static string Size(long value)
        {
            if (value < 1024) return value.ToString(CultureInfo.InvariantCulture) + " B";
            double kb = value / 1024.0;
            if (kb < 1024) return kb.ToString("0.0", CultureInfo.InvariantCulture) + " KB";
            double mb = kb / 1024.0;
            if (mb < 1024) return mb.ToString("0.0", CultureInfo.InvariantCulture) + " MB";
            return (mb / 1024.0).ToString("0.00", CultureInfo.InvariantCulture) + " GB";
        }

        public static string Clock(int seconds)
        {
            if (seconds < 0) seconds = 0;
            int m = seconds / 60;
            int s = seconds % 60;
            if (m < 60) return m.ToString("00", CultureInfo.InvariantCulture) + ":" + s.ToString("00", CultureInfo.InvariantCulture);
            int h = m / 60;
            return h.ToString("00", CultureInfo.InvariantCulture) + ":" + (m % 60).ToString("00", CultureInfo.InvariantCulture) + ":" + s.ToString("00", CultureInfo.InvariantCulture);
        }
    }
}
