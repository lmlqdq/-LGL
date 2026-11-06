using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;
using Liuguang.Core;

namespace Liuguang.UI
{
    public class NavButton : Control
    {
        public string Glyph = "";
        public bool Active = false;
        private bool hover;

        public NavButton(string glyph, string text)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Glyph = glyph;
            Text = text;
            // 64 是高，不是宽：宽度由 LayoutChildren 给（76）。
            // 竖排要放「30px 图标 + 19px 文字」两行，46 高会挤成一团。
            Height = 64;
            Cursor = Cursors.Hand;
            Font = Theme.Body;
            BackColor = Theme.Sidebar;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.EnableQuality(g);
            // 和侧栏用同一张壁纸 + 同一种白色浓度，按钮底才不会是浮在玻璃上的白斑。
            // 用 PaintBarFlat 而不是 PaintGlassBar：导航项在侧栏里，顶光属于整条侧栏，
            // 每个 46px 的导航项各画一道「顶部 40% 高光」就是五道假高光。
            Theme.PaintBarFlat(this, g, ClientRectangle, Theme.ChromeBarAlpha);
            // 图标轨里左右各留 4px：76px 宽的导航块再往外贴就会顶到侧栏边缘
            Rectangle rect = new Rectangle(4, 2, Math.Max(1, Width - 8), Height - 4);
            if (rect.Width <= 0 || rect.Height <= 0) return;

            // 圆角固定 18 而不是「半径取半高」：
            // 76x60 的方块取半高会变成一个圆球，那是 Dock 不是导航。
            int nr = Theme.LiquidMode ? 18 : 12;
            if (Theme.LiquidMode || Theme.MillenniumMode)
            {
                // 玻璃皮肤：选中态是一块凸起的圆角玻璃，背景条纹在里面被挤弯
                if (Active || hover)
                {
                    Theme.PaintPill(this, g, rect, nr, Active ? 52 : 24,
                        Active ? Theme.Accent : Color.Empty, Active ? 64 : 0);
                }
                if (Theme.MillenniumMode && Active)
                {
                    // 千禧年再加一道清漆顶光和灰蓝细边，不然只是一块蓝底
                    Theme.PaintGloss(g, rect, nr, 110);
                    Theme.DrawRound(g, rect, nr, Theme.Alpha(Theme.BorderStrong, 180), 1f);
                }
            }
            else if (Active)
            {
                Theme.FillRound(g, rect, 12, Theme.Alpha(Theme.Accent, 26));
            }
            else if (hover)
            {
                Theme.FillRound(g, rect, 12, Theme.Alpha(Theme.Accent, 12));
            }

            // 竖排：图标在上、两字标签在下。
            // 92px 宽的轨道放不下横排的「图标 + 文字」，但纯图标对中文用户不可读，
            // 所以牺牲宽度换「图标 + 小字」的竖排 —— 这是 macOS 侧栏的排法。
            Color color = Active ? Theme.Text : Theme.SubText;
            TextRenderer.DrawText(g, Glyph, Theme.IconNav,
                new Rectangle(rect.X, rect.Y + 7, rect.Width, 30),
                Active ? Theme.Accent : color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, Text, Theme.Small,
                new Rectangle(rect.X, rect.Bottom - 23, rect.Width, 19), color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    public class MainForm : Form
    {
        public static MainForm Instance;

        private readonly Config cfg;
        private LgPanel titleBar;
        private LgPanel sidebar;
        private LgPanel content;
        private LgPanel statusBar;
        private LgProgress statusProgress;
        private LgCard detailsCard;
        // 声明必须是 LgTextBox 而不是 TextBox：LogStyle 是子类成员，
        // 用基类声明时编译器按 TextBox 查成员，找不到。
        private LgTextBox detailsBox;
        private System.Windows.Forms.Timer detailsAnim;
        private System.Windows.Forms.Timer detailsTick;
        private int detailsHeight;
        private bool detailsOpen;
        private int detailsTickCount;

        /// <summary>状态栏文字。以前是一个 Label，透明底在玻璃条上会压出色块，改成直接画。</summary>
        private string statusMessage = "就绪";

        private readonly List<NavButton> navs = new List<NavButton>();
        private readonly List<PageBase> pages = new List<PageBase>();
        private System.Windows.Forms.Timer animTimer;
        private float phase;
        private int currentPage = -1;
        private bool busy;
        private int lastReportTick;
        private string lastReportMessage = "";

        public Config Cfg
        {
            get { return cfg; }
        }

        public bool Busy
        {
            get { return busy; }
        }

        public MainForm(Config config)
        {
            Instance = this;
            cfg = config;

            Text = "流光启动器";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1130, 706);
            MinimumSize = new Size(980, 640);
            BackColor = Theme.Bg;
            DoubleBuffered = true;
            Font = Theme.Body;
            KeyPreview = true;

            BuildUi();
            ApplyRoundRegion();

            animTimer = new System.Windows.Forms.Timer();
            animTimer.Interval = 45;
            animTimer.Tick += new EventHandler(OnAnimTick);
            animTimer.Start();

            Log.Message += new Action<string>(OnLogMessage);
            KeyDown += new KeyEventHandler(OnFormKeyDown);
        }

        private void OnFormKeyDown(object sender, KeyEventArgs e)
        {
            // Esc 收起下载详情：面板盖住了内容区，用户的第一反应是「按 Esc 关掉」
            if (e.KeyCode == Keys.Escape && detailsOpen)
            {
                ToggleDetails();
                e.Handled = true;
            }
        }

        private void BuildUi()
        {
            statusBar = new LgPanel();
            statusBar.BackColor = Theme.Sidebar;
            statusBar.Paint += new PaintEventHandler(StatusBarPaint);
            statusBar.Cursor = Cursors.Hand;
            statusBar.Click += new EventHandler(delegate(object s, EventArgs e) { ToggleDetails(); });
            Controls.Add(statusBar);

            statusProgress = new LgProgress();
            statusProgress.Height = 6;
            statusProgress.BackColor = Theme.Sidebar;
            statusProgress.Cursor = Cursors.Hand;
            statusProgress.Click += new EventHandler(delegate(object s, EventArgs e) { ToggleDetails(); });
            statusBar.Controls.Add(statusProgress);

            titleBar = new LgPanel();
            titleBar.BackColor = Theme.Bg;
            titleBar.CaptionDrag = true;
            titleBar.Paint += new PaintEventHandler(TitleBarPaint);
            Controls.Add(titleBar);

            LgIconButton close = new LgIconButton("✕");
            close.Danger = true;
            close.Click += new EventHandler(delegate(object s, EventArgs e) { Close(); });
            titleBar.Controls.Add(close);

            LgIconButton min = new LgIconButton("—");
            min.Click += new EventHandler(delegate(object s, EventArgs e) { WindowState = FormWindowState.Minimized; });
            titleBar.Controls.Add(min);

            titleBar.Tag = new object[] { min, close };

            sidebar = new LgPanel();
            sidebar.BackColor = Theme.Sidebar;
            sidebar.CaptionDrag = true;   // 侧栏空白处也能拖窗口，这是无边框窗体的常识
            sidebar.Paint += new PaintEventHandler(SidebarPaint);
            Controls.Add(sidebar);

            string[] names = new string[] { "主页", "版本", "下载", "资源", "设置" };
            // Win10/11 用 MDL2 线性图标（粗细一致、有网格感）；
            // 老系统退回符号字符，避免私用区码点渲染成方块。
            string[] glyphs = Theme.HasMdl2
                ? new string[] { "\uE80F", "\uE8F1", "\uE896", "\uE719", "\uE713" }
                : new string[] { "⌂", "▤", "⤓", "✦", "⚙" };
            for (int i = 0; i < names.Length; i++)
            {
                NavButton nav = new NavButton(glyphs[i], names[i]);
                nav.Tag = i;
                nav.Click += new EventHandler(OnNavClick);
                sidebar.Controls.Add(nav);
                navs.Add(nav);
            }

            content = new LgPanel();
            content.BackColor = Theme.Bg;
            Controls.Add(content);

            pages.Add(new HomePage(this));
            pages.Add(new VersionsPage(this));
            pages.Add(new DownloadPage(this));
            pages.Add(new ModsPage(this));
            pages.Add(new SettingsPage(this));
            for (int i = 0; i < pages.Count; i++)
            {
                pages[i].Visible = false;
                content.Controls.Add(pages[i]);
            }

            // 下载详情：盖在内容区底部、状态栏上方。默认收起，点状态栏展开。
            detailsCard = new LgCard();
            // 和首页的日志卡保持同一种形态：下载详情也是终端输出
            detailsCard.Dark = true;
            detailsCard.Visible = false;
            detailsCard.Paint += new PaintEventHandler(DetailsPaint);
            Controls.Add(detailsCard);

            detailsBox = new LgTextBox();
            detailsBox.LogStyle = true;
            detailsBox.Multiline = true;
            detailsBox.ReadOnly = true;
            detailsBox.ScrollBars = ScrollBars.None;
            detailsBox.BorderStyle = BorderStyle.None;
            detailsBox.ApplySkin();
            detailsBox.ForeColor = Theme.LogText;
            detailsBox.Font = Theme.Mono;
            detailsBox.WordWrap = true;
            detailsCard.Controls.Add(detailsBox);

            detailsAnim = new System.Windows.Forms.Timer();
            detailsAnim.Interval = 15;
            detailsAnim.Tick += new EventHandler(OnDetailsAnim);
            detailsTick = new System.Windows.Forms.Timer();
            detailsTick.Interval = 400;
            detailsTick.Tick += new EventHandler(OnDetailsTick);

            LayoutChildren();
            ShowPage(0);
        }

        private const int DetailsHeight = 178;

        private void ToggleDetails()
        {
            detailsOpen = !detailsOpen;
            if (detailsOpen)
            {
                detailsTickCount = 0;
                RefreshDetailsText();
                detailsTick.Start();
            }
            else
            {
                detailsTick.Stop();
            }
            detailsAnim.Start();
            if (statusBar != null) statusBar.Invalidate();
        }

        private void OnDetailsAnim(object sender, EventArgs e)
        {
            int target = detailsOpen ? DetailsHeight : 0;
            if (detailsHeight == target)
            {
                detailsAnim.Stop();
                if (detailsHeight == 0)
                {
                    detailsCard.Visible = false;
                    detailsTick.Stop();
                }
                return;
            }
            int distance = Math.Abs(target - detailsHeight);
            int step = Math.Max(14, distance / 3);
            if (detailsHeight < target) detailsHeight = Math.Min(target, detailsHeight + step);
            else detailsHeight = Math.Max(target, detailsHeight - step);
            LayoutDetails();
        }

        private void OnDetailsTick(object sender, EventArgs e)
        {
            detailsTickCount++;
            RefreshDetailsText();
        }

        private void RefreshDetailsText()
        {
            if (detailsBox == null || detailsBox.IsDisposed) return;
            try
            {
                string[] lines = ProgressFeed.Render();
                string text = string.Join("\r\n", lines);
                if (detailsBox.Text != text) detailsBox.Text = text;
            }
            catch
            {
            }
        }

        private void DetailsPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.EnableQuality(g);
            TextRenderer.DrawText(g, "下载详情", Theme.H3, new Rectangle(20, 10, 200, 24), Theme.LogText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, "点状态栏可收起", Theme.Tiny, new Rectangle(Math.Max(20, detailsCard.Width - 150), 10, 130, 24),
                Theme.LogTextDim, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }

        private void LayoutDetails()
        {
            if (detailsCard == null) return;
            int w = ClientSize.Width;
            int h = ClientSize.Height;
            const int statusHeight = 32;
            const int sidebarWidth = 92;

            if (detailsHeight <= 0)
            {
                detailsCard.Visible = false;
                return;
            }
            detailsCard.Visible = true;
            detailsCard.SetBounds(sidebarWidth + Theme.Pad, h - statusHeight - detailsHeight,
                Math.Max(120, w - sidebarWidth - Theme.Pad * 2), detailsHeight);
            if (detailsBox != null)
            {
                detailsBox.SetBounds(24, 44, Math.Max(60, detailsCard.Width - 48), Math.Max(20, detailsCard.Height - 60));
            }
            detailsCard.BringToFront();
        }

        private void ApplyRoundRegion()
        {
            try
            {
                if (WindowState == FormWindowState.Maximized)
                {
                    Region = null;
                    return;
                }
                using (GraphicsPath path = Theme.Round(new Rectangle(0, 0, Width, Height), 14))
                {
                    Region old = Region;
                    Region = new Region(path);
                    if (old != null) old.Dispose();
                }
            }
            catch
            {
            }
        }

        private void LayoutChildren()
        {
            int w = ClientSize.Width;
            int h = ClientSize.Height;
            // 侧栏 92 = 76 图标块 + 两侧各 8。原来是 216 的文字栏，
            // 占掉近 1/5 窗口宽度去放五个两字菜单，那是后台管理系统的排法。
            const int titleHeight = 56;
            const int statusHeight = 32;
            const int sidebarWidth = 92;

            if (titleBar != null) titleBar.SetBounds(0, 0, w, titleHeight);
            if (statusBar != null) statusBar.SetBounds(0, h - statusHeight, w, statusHeight);
            if (sidebar != null) sidebar.SetBounds(0, titleHeight, sidebarWidth, h - titleHeight - statusHeight);
            if (content != null) content.SetBounds(sidebarWidth, titleHeight, w - sidebarWidth, h - titleHeight - statusHeight);

            // 进度条贴右，左侧留出文字区；最右边 72px 是「详情」按钮的位置
            if (statusProgress != null) statusProgress.SetBounds(w - 254, (statusHeight - 5) / 2, 170, 5);

            if (titleBar != null && titleBar.Tag is object[])
            {
                object[] buttons = (object[])titleBar.Tag;
                LgIconButton min = (LgIconButton)buttons[0];
                LgIconButton close = (LgIconButton)buttons[1];
                close.SetBounds(w - 48, 14, 36, 30);
                min.SetBounds(w - 86, 14, 36, 30);
            }

            if (sidebar != null)
            {
                // 从标题栏下 16px 起，项间 8px；5 项共 5*64+4*8=352px，
                // 窗口最小高度 640 下侧栏可用 552px，放得下。
                for (int i = 0; i < navs.Count; i++)
                {
                    navs[i].SetBounds(8, 16 + i * 72, sidebarWidth - 16, 64);
                }
            }

            for (int i = 0; i < pages.Count; i++)
            {
                pages[i].SetBounds(0, 0, content.ClientSize.Width, content.ClientSize.Height);
            }

            LayoutDetails();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutChildren();
            ApplyRoundRegion();
        }

        private void TitleBarPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.EnableQuality(g);
            // 标题栏也是玻璃：先铺壁纸，再盖一层很淡的白，让光斑透上来。
            // 浓度用 Theme.TitleBarAlpha —— 图标按钮要拿同一个值铺自己的底，
            // 两处写死两个数就会错位成暗方块。
            Theme.PaintGlassBar(titleBar, g, titleBar.ClientRectangle, Theme.TitleBarAlpha);

            int w = titleBar.Width;
            int h = titleBar.Height;

            // 底部 1px 流光条：原来 2px 且高光很重，看起来像一条描边而不是光。
            // 细到 1px、只在中间扫过一段亮斑，才是「流光」而不是「彩色线」。
            using (LinearGradientBrush brush = Theme.Gradient(new Rectangle(0, h - 1, Math.Max(1, w), 1),
                Theme.Alpha(Theme.Accent, 10), Theme.Alpha(Theme.Accent2, 160), 0f))
            {
                ColorBlend blend = new ColorBlend(3);
                blend.Colors = new Color[] { Theme.Alpha(Theme.Accent, 0), Theme.Accent, Theme.Alpha(Theme.Accent2, 0) };
                blend.Positions = new float[] { 0f, (float)(0.5 + 0.35 * Math.Sin(phase)), 1f };
                try
                {
                    brush.InterpolationColors = blend;
                }
                catch
                {
                }
                // 加上描边：浅色光条落在浅色玻璃上会整段消失，需要一条极淡的边界把它托住
                using (Pen edge = new Pen(Theme.Alpha(Theme.Accent2, 70), 1f))
                {
                    g.DrawLine(edge, 0, h - 1, w, h - 1);
                }
                g.FillRectangle(brush, 0, h - 1, w, 1);
            }

            // 标题栏 56 高，26px 的 logo 居中即 y=(56-26)/2=15
            Rectangle logoRect = new Rectangle(20, 15, 26, 26);
            using (GraphicsPath path = Theme.Round(logoRect, 8))
            {
                using (LinearGradientBrush brush = new LinearGradientBrush(logoRect, Theme.Accent, Theme.Accent2, 55f))
                {
                    g.FillPath(brush, path);
                }
            }
            using (Font f = Theme.MakeFont(10f, FontStyle.Bold))
            {
                TextRenderer.DrawText(g, "流", f, logoRect, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            // 标题改用实色：渐变文字在浅底上会让笔画发虚，反而显得不精致。
            // 版本号按标题实际宽度接在后面，不再写死 x 坐标（换字体或改字号就会错位）。
            using (Font titleFont = Theme.MakeFont(13f, FontStyle.Bold))
            {
                Size titleSize = TextRenderer.MeasureText("流光启动器", titleFont);
                TextRenderer.DrawText(g, "流光启动器", titleFont,
                    new Rectangle(56, 0, titleSize.Width + 6, h), Theme.Text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
                using (Font verFont = Theme.MakeFont(8.5f, FontStyle.Regular))
                {
                    TextRenderer.DrawText(g, "v" + Paths.Version, verFont,
                        new Rectangle(56 + titleSize.Width + 8, 0, 90, h), Theme.SubText,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
                }
            }
        }

        private void SidebarPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.PaintGlassBar(sidebar, g, sidebar.ClientRectangle, Theme.ChromeBarAlpha);
            // 侧栏现在只有 92px 宽，放不下品牌区。
            // 而且标题栏已经有「流」logo + 名字，这里再画一遍是重复 ——
            // 一块界面上出现两次 logo，是让人一眼觉得「不专业」的细节。
            // 同样去掉右侧那条 1px 竖线：现代界面的分栏靠留白，不靠描边。
        }

        private void StatusBarPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.EnableQuality(g);
            Theme.PaintGlassBar(statusBar, g, statusBar.ClientRectangle, Theme.ChromeBarAlpha);
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(170, Theme.Border)))
            {
                g.FillRectangle(brush, 0, 0, statusBar.Width, 1);
            }

            int h = statusBar.Height;
            TextRenderer.DrawText(g, statusMessage, Theme.Small, new Rectangle(20, 0, Math.Max(80, statusBar.Width - 340), h),
                Theme.SubText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            // 右侧「详情」：告诉用户这条状态栏是可以点开的
            Rectangle hint = new Rectangle(statusBar.Width - 62, 0, 36, h);
            TextRenderer.DrawText(g, detailsOpen ? "收起" : "详情", Theme.Tiny, hint, Theme.Accent,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            int cx = statusBar.Width - 18;
            int cy = h / 2;
            Point[] arrow = detailsOpen
                ? new Point[] { new Point(cx - 4, cy - 1), new Point(cx + 4, cy - 1), new Point(cx, cy + 3) }
                : new Point[] { new Point(cx - 4, cy + 2), new Point(cx + 4, cy + 2), new Point(cx, cy - 2) };
            using (SolidBrush brush = new SolidBrush(Theme.Accent))
            {
                g.FillPolygon(brush, arrow);
            }
        }

        private void OnAnimTick(object sender, EventArgs e)
        {
            phase += 0.06f;
            if (phase > Math.PI * 2) phase -= (float)(Math.PI * 2);
            if (titleBar == null || titleBar.Width <= 0 || titleBar.Height < 2) return;
            // 只重画标题栏底部 2 像素的流光条。
            // 原来对整个标题栏 Invalidate，标题文字每秒被重画 22 次，这是「字一直闪」的直接原因。
            titleBar.Invalidate(new Rectangle(0, titleBar.Height - 2, titleBar.Width, 2));
        }

        private void OnNavClick(object sender, EventArgs e)
        {
            NavButton nav = sender as NavButton;
            if (nav == null) return;
            ShowPage((int)nav.Tag);
        }

        public void ShowPage(int index)
        {
            if (index < 0 || index >= pages.Count) return;
            currentPage = index;
            for (int i = 0; i < pages.Count; i++)
            {
                pages[i].Visible = (i == index);
            }
            for (int i = 0; i < navs.Count; i++)
            {
                navs[i].Active = (i == index);
                navs[i].Invalidate();
            }
            pages[index].BringToFront();
            try
            {
                pages[index].Reload();
            }
            catch (Exception ex)
            {
                Log.Error("页面加载失败：" + ex.Message);
            }
        }

        public void Ui(MethodInvoker action)
        {
            try
            {
                if (IsDisposed) return;
                if (InvokeRequired) BeginInvoke(action);
                else action();
            }
            catch
            {
            }
        }

        public void Report(int done, int total, string message)
        {
            int now = Environment.TickCount;
            bool newMessage = message != null && message.Length > 0 && message != lastReportMessage;
            bool allDone = total > 0 && done >= total;
            // 安装时按文件回调，动辄几千次。逐次刷新状态栏等于让文字每秒重画几十遍，
            // 肉眼看到的就是「字一直在闪」。这里节流：换消息或全部完成立即刷新，其余最多 12 次/秒。
            if (!newMessage && !allDone && (now - lastReportTick) < 80) return;
            lastReportTick = now;
            if (newMessage) lastReportMessage = message;

            // total<=0 表示「换阶段」（校验 / 下载 / 装 Java），下载中的单文件回调一定是 total>0。
            // 详情面板按这个切分组，把上一阶段的计数清掉。
            if (newMessage && total <= 0) ProgressFeed.SetLabel(message);

            Ui(delegate
            {
                if (message != null && message.Length > 0) statusMessage = message;
                if (statusProgress != null)
                {
                    if (total <= 0)
                    {
                        statusProgress.Indeterminate = true;
                    }
                    else
                    {
                        statusProgress.Indeterminate = false;
                        statusProgress.Value = (double)done / total;
                    }
                }
                if (statusBar != null) statusBar.Invalidate();
            });
        }

        public void Notify(string message)
        {
            Ui(delegate
            {
                statusMessage = message;
                if (statusBar != null) statusBar.Invalidate();
            });
        }

        public void SetIdle()
        {
            Ui(delegate
            {
                if (statusProgress != null)
                {
                    statusProgress.Indeterminate = false;
                    statusProgress.Value = 0;
                }
                statusMessage = "就绪";
                if (statusBar != null) statusBar.Invalidate();
            });
        }

        public void RunBackground(string title, Action work, Action onDone)
        {
            if (busy)
            {
                Notify("已有任务正在进行，请稍候");
                return;
            }
            busy = true;
            Report(0, 0, title);
            Thread thread = new Thread(delegate()
            {
                Exception error = null;
                try
                {
                    work();
                }
                catch (Exception ex)
                {
                    error = ex;
                    Log.Error(ex.Message);
                }
                finally
                {
                    busy = false;
                }
                if (error != null)
                {
                    string message = error.Message;
                    Ui(delegate
                    {
                        SetIdle();
                        MessageBox.Show(this, message, "流光启动器", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    });
                }
                else
                {
                    Ui(delegate
                    {
                        SetIdle();
                        if (onDone != null) onDone();
                    });
                }
            });
            thread.IsBackground = true;
            thread.Start();
        }

        public void LaunchVersion(string versionId)
        {
            if (busy)
            {
                Notify("已有任务正在进行，请稍候");
                return;
            }
            if (string.IsNullOrEmpty(versionId))
            {
                Notify("请先选择一个游戏版本");
                return;
            }
            cfg.LastVersion = versionId;
            cfg.Save();

            // 启动游戏前的过场视频：点「启动」先放一段动画（任意键跳过），
            // 播完 / 跳过后才真正开始校验文件、准备 Java、拉起进程。
            // 视频文件不存在（splash.mp4 被删）时静默跳过，不挡启动。
            if (cfg.Splash && SplashForm.VideoAvailable)
            {
                try
                {
                    using (SplashForm splash = new SplashForm())
                    {
                        splash.ShowDialog(this);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("启动动画失败：" + ex.Message);
                }
            }

            RunBackground("准备启动 " + versionId, delegate
            {
                Report(0, 0, "检查游戏文件完整性");
                GameVersion version = VersionMeta.Load(versionId);

                // 启动前自检：核心 jar / 依赖库 / 资源文件 / native 缺什么补什么。
                // 缺文件时不再直接抛「缺少游戏核心文件」，而是自动补齐后再启动。
                EnvironmentRepair.Ensure(version, cfg, new ProgressHandler(Report));

                Report(0, 0, "检查 Java 运行环境");
                string java = JavaInstaller.Ensure(version.JavaMajor, cfg.JavaPath, new ProgressHandler(Report));
                if (java.Length == 0)
                {
                    throw new Exception("Java " + version.JavaMajor + " 准备失败，请到「设置」里手动指定 java.exe");
                }
                Report(0, 0, "使用 Java " + version.JavaMajor + "：" + java);
                LaunchPlan plan = GameLauncher.Build(version, cfg, java);
                Log.Write("启动命令：" + plan.CommandLine);
                Process process = GameLauncher.Launch(plan);
                Notify("游戏已启动（PID " + process.Id + "）");
                WatchProcess(process);
            }, delegate
            {
                if (cfg.CloseOnLaunch) Close();
                else ShowPage(0);
            });
        }

        /// <summary>
        /// 盯一下刚拉起的游戏进程：20 秒内就退出的基本都是环境问题（缺组件、内存不足、模组冲突）。
        /// 把退出码打到状态栏，用户才不会只看到「窗口闪一下就没了」。
        /// </summary>
        private void WatchProcess(Process process)
        {
            Thread watcher = new Thread(delegate()
            {
                try
                {
                    if (process.WaitForExit(20000))
                    {
                        int code = process.ExitCode;
                        Log.Write("游戏进程已退出，退出码 " + code);
                        if (code != 0) Notify("游戏启动失败（退出码 " + code + "），请查看主页日志");
                        else Notify("游戏已退出");
                    }
                }
                catch
                {
                }
            });
            watcher.IsBackground = true;
            watcher.Start();
        }

        private void OnLogMessage(string line)
        {
            // AppendLog 只写加锁缓冲区，可在后台线程直接调用。
            // 原来每行日志都排一次 BeginInvoke，游戏启动刷屏时会把消息队列塞满。
            HomePage home = pages.Count > 0 ? pages[0] as HomePage : null;
            if (home != null) home.AppendLog(line);
        }

        /// <summary>
        /// 整窗合成（WS_EX_COMPOSITED）：让 Windows 先把整棵控件树离屏合成好再一次性上屏，
        /// 拖动缩放、切页、切主题时不会先擦白底再重画，从根上消除闪烁。
        /// 这是 WinForms 无边框自绘窗体最省事的抗闪烁手段。
        ///
        /// 同时补上 WS_THICKFRAME：FormBorderStyle.None 会把系统「可缩放」样式去掉，
        /// 导致 WM_NCHITTEST 返回 HTLEFT/HTRIGHT 等码时窗口根本不动。有了这个样式，
        /// WndProc 里按边缘返回的 HT 码才会被系统接住、进入原生的缩放循环。
        /// </summary>
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x02000000;   // WS_EX_COMPOSITED
                cp.Style |= 0x00040000;     // WS_THICKFRAME：无边框窗体恢复可缩放
                return cp;
            }
        }

        /// <summary>
        /// 背景不用系统默认的纯色擦除，而是直接铺壁纸。
        /// 若不重写，每次重绘前 WinForms 会先用 BackColor（浅蓝）把整个客户区刷一遍，
        /// 再让各个 Panel 各自重画，中间那一下就是肉眼可见的闪烁。
        /// </summary>
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            try
            {
                Theme.PaintBackdrop(this, e.Graphics, ClientRectangle);
            }
            catch
            {
                // 壁纸取不到（窗口还没成型）时退回底色，不能因为背景失败把整窗绘制打断
                e.Graphics.Clear(BackColor);
            }
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x84;
            const int HTCLIENT = 1;
            const int HTCAPTION = 2;
            const int HTLEFT = 10;
            const int HTRIGHT = 11;
            const int HTTOP = 12;
            const int HTTOPLEFT = 13;
            const int HTTOPRIGHT = 14;
            const int HTBOTTOM = 15;
            const int HTBOTTOMLEFT = 16;
            const int HTBOTTOMRIGHT = 17;

            if (m.Msg == WM_NCHITTEST)
            {
                base.WndProc(ref m);
                if ((int)m.Result != HTCLIENT) return;

                Point position = PointToClient(Cursor.Position);
                int edge = 6;
                bool left = position.X <= edge;
                bool right = position.X >= ClientSize.Width - edge;
                bool top = position.Y <= edge;
                bool bottom = position.Y >= ClientSize.Height - edge;

                int hit = HTCLIENT;
                if (top && left) hit = HTTOPLEFT;
                else if (top && right) hit = HTTOPRIGHT;
                else if (bottom && left) hit = HTBOTTOMLEFT;
                else if (bottom && right) hit = HTBOTTOMRIGHT;
                else if (left) hit = HTLEFT;
                else if (right) hit = HTRIGHT;
                else if (top) hit = HTTOP;
                else if (bottom) hit = HTBOTTOM;
                else if (position.Y < 48 && position.X < ClientSize.Width - 100) hit = HTCAPTION;

                m.Result = (IntPtr)hit;
                return;
            }
            base.WndProc(ref m);
        }
    }
}
