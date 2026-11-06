using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Liuguang.UI
{
    /// <summary>
    /// 自绘控件画圆角前先铺底用：拿到控件实际所处的背景色。
    /// 圆角之外的像素若不清底，会保留上一帧缓冲内容，表现为残影/花屏。
    /// </summary>
    internal static class Surf
    {
        /// <summary>
        /// 圆角之外的底色。父级是玻璃卡片时返回透明——由调用方把父卡片的玻璃裁过来铺，
        /// 这样圆角外是真的玻璃，而不是一块和卡片对不上的平涂色。
        /// </summary>
        public static Color Of(Control c)
        {
            if (c == null) return Theme.Bg;
            LgCard card = c.Parent as LgCard;
            if (card != null)
            {
                // 父级是玻璃卡片时**不能**返回任何实色：
                // 卡片内部有渐变 + 光斑 + 高光，任何平涂色都会在按钮圆角外
                // 结出一块对不上的方块（白块 → 改成灰块，问题没解决，只是换了个颜色）。
                // 返回 Transparent，调用方的 g.Clear 等于不改像素，
                // 真正该画什么由 PaintCardBacked() 逐像素从父卡片表面裁。
                return Color.Transparent;
            }
            if (c.Parent != null && c.Parent.BackColor.A == 255) return c.Parent.BackColor;
            return c.BackColor.A == 255 ? c.BackColor : Theme.Bg;
        }

        /// <summary>
        /// 铺子控件的底：父级是玻璃卡片就裁父卡片的离屏表面（逐像素一致，接缝为零），
        /// 否则退回普通实色清底。
        /// 返回 true 表示已经铺好，调用方不要再 Clear。
        /// </summary>
        public static bool PaintCardBacked(Control c, Graphics g)
        {
            if (c == null || g == null) return false;
            if (c.Parent is LgCard)
            {
                GlassCrop.Paint(c, g);
                return true;
            }
            // 直接挂在页面上的控件（页面级按钮、下拉框）也要铺「背后那张壁纸」，
            // 不能只填父容器的 BackColor：液态玻璃主题下页面是蓝白条纹，
            // 平涂一个近似色会在圆角外露出四块对不上的浅色方块。
            if (c.Parent is PageBase)
            {
                try
                {
                    Theme.PaintBackdrop(c, g, c.ClientRectangle);
                }
                catch
                {
                    // 控件还没挂到窗体上（构造中 / 正在销毁）时拿不到屏幕坐标，
                    // 这一帧退回父容器底色，不要因为铺底失败把整次绘制打断。
                    g.Clear(Surf.Of(c));
                }
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// 无边框窗口的拖动 / 缩放。
    ///
    /// 两个坑都在这一个类里：
    /// 一、鼠标落在子控件（标题栏 Panel）上时，窗体根本收不到 WM_NCHITTEST，
    ///     所以只在窗体 WndProc 里返回 HTCAPTION 是永远拖不动的——坐标全被子控件吃掉了。
    /// 二、即使给顶层窗口发 WM_NCLBUTTONDOWN + HTCAPTION，让系统进入移动循环，
    ///     对 FormBorderStyle.None 的窗口并不可靠（没有 WS_CAPTION，也不带 lParam 里的
    ///     屏幕坐标，窗口会先跳到左上角）。用户看到的现象就是「按住标题栏，窗口纹丝不动」。
    ///
    /// 所以拖动改成进程内实现：按住时记住光标与窗口原点的差值，鼠标移动就设 Location。
    /// 不依赖任何窗口样式，一定会动。缩放仍然交给系统（改尺寸还要处理最小尺寸、DPI、吸附）。
    /// </summary>
    internal static class WindowDrag
    {
        private const int WM_NCLBUTTONDOWN = 0xA1;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        // 手动拖动的现场。同一时刻只可能有一个窗口在被拖，用静态字段足够。
        private static Form moveForm;
        private static Control moveSource;
        private static Point moveCursor;
        private static Point moveOrigin;

        /// <summary>在窗口边缘 7px 内按下 → 缩放；否则 caption=true 时 → 拖动</summary>
        public static bool TryStart(Control c, MouseEventArgs e, bool caption)
        {
            if (c == null || e == null || e.Button != MouseButtons.Left) return false;
            Form form = c.FindForm();
            if (form == null || form.IsDisposed) return false;
            if (form.WindowState == FormWindowState.Maximized && !caption) return false;

            Point p = form.PointToClient(c.PointToScreen(e.Location));
            const int edge = 7;
            bool left = p.X <= edge;
            bool right = p.X >= form.ClientSize.Width - edge;
            bool top = p.Y <= edge;
            bool bottom = p.Y >= form.ClientSize.Height - edge;

            int code = 0;
            if (top && left) code = 13;          // HTTOPLEFT
            else if (top && right) code = 14;    // HTTOPRIGHT
            else if (bottom && left) code = 16;  // HTBOTTOMLEFT
            else if (bottom && right) code = 17; // HTBOTTOMRIGHT
            else if (left) code = 10;            // HTLEFT
            else if (right) code = 11;           // HTRIGHT
            else if (top) code = 12;             // HTTOP
            else if (bottom) code = 15;          // HTBOTTOM
            else if (caption) code = 2;          // HTCAPTION
            if (code == 0) return false;

            if (code == 2) return BeginMove(form, c);

            try
            {
                // 缩放交给系统。lParam 必须是光标的屏幕坐标，否则窗口会先跳到左上角再开始缩放。
                Point screen = c.PointToScreen(e.Location);
                int lparam = ((screen.Y & 0xFFFF) << 16) | (screen.X & 0xFFFF);
                ReleaseCapture();
                SendMessage(form.Handle, WM_NCLBUTTONDOWN, code, lparam);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool BeginMove(Form form, Control source)
        {
            EndMove();
            moveForm = form;
            moveSource = source;
            moveCursor = Cursor.Position;
            moveOrigin = form.Location;
            source.MouseMove += new MouseEventHandler(OnMoveMove);
            source.MouseUp += new MouseEventHandler(OnMoveUp);
            // 抓住鼠标：光标划出标题栏也不会丢事件，长距离拖动才不会「拖一半停住」
            source.Capture = true;
            return true;
        }

        private static void OnMoveMove(object sender, MouseEventArgs e)
        {
            if (moveForm == null || moveForm.IsDisposed)
            {
                EndMove();
                return;
            }
            Point now = Cursor.Position;
            int x = moveOrigin.X + (now.X - moveCursor.X);
            int y = moveOrigin.Y + (now.Y - moveCursor.Y);
            if (moveForm.Location.X == x && moveForm.Location.Y == y) return;
            try
            {
                moveForm.Location = new Point(x, y);
            }
            catch
            {
                EndMove();
            }
        }

        private static void OnMoveUp(object sender, MouseEventArgs e)
        {
            EndMove();
        }

        private static void EndMove()
        {
            if (moveSource != null)
            {
                try
                {
                    moveSource.MouseMove -= new MouseEventHandler(OnMoveMove);
                    moveSource.MouseUp -= new MouseEventHandler(OnMoveUp);
                    moveSource.Capture = false;
                }
                catch
                {
                }
            }
            moveSource = null;
            moveForm = null;
        }
    }

    /// <summary>
    /// 「玻璃裁片」：把父卡片已经算好的那一块表面原样贴到子控件上。
    ///
    /// 为什么不用 Color.Transparent：WinForms 的透明子控件会去调父控件的
    /// OnPaintBackground，而父卡片是在 OnPaint 里画玻璃的，取到的只是纯底色，
    /// 结果就是每个 Label 背后压一块白方块——正是「文字底面是白色」的成因。
    /// 直接裁父卡片的离屏表面，颜色逐像素都和卡片一致，接缝为零。
    /// </summary>
    internal static class GlassCrop
    {
        public static void Paint(Control c, Graphics g)
        {
            if (c == null || g == null) return;
            LgCard card = c.Parent as LgCard;
            if (card != null)
            {
                Bitmap surface = card.Surface;
                if (surface == null)
                {
                    g.Clear(Theme.Bg);
                    return;
                }
                Rectangle src = new Rectangle(c.Left, c.Top, c.Width, c.Height);
                src.Intersect(new Rectangle(0, 0, surface.Width, surface.Height));
                if (src.Width <= 0 || src.Height <= 0) return;
                g.DrawImage(surface,
                    new Rectangle(src.X - c.Left, src.Y - c.Top, src.Width, src.Height),
                    src, GraphicsUnit.Pixel);
                return;
            }

            Theme.PaintBackdrop(c, g, c.ClientRectangle);
            PageBase page = c.Parent as PageBase;
            if (page != null && page.ClientSize.Width > 0)
            {
                // 页面顶部那团柔光的坐标是相对页面的，要平移进子控件坐标系
                Theme.PaintPageGlow(g, new Rectangle(-c.Left, -c.Top, page.ClientSize.Width, page.ClientSize.Height));
            }
        }

        /// <summary>
        /// 只铺控件内部的一小块矩形（列表行、胶囊按钮用）。
        /// 语义和 <see cref="Paint"/> 相同，但按传入的局部坐标取样，
        /// 于是列表每一行都能拿到「自己那一块玻璃」，整列看起来是一整片，而不是一行行白条。
        /// </summary>
        public static void PaintRect(Control c, Graphics g, Rectangle rect)
        {
            if (c == null || g == null) return;
            if (rect.Width <= 0 || rect.Height <= 0) return;
            LgCard card = c.Parent as LgCard;
            if (card != null)
            {
                Bitmap surface = card.Surface;
                if (surface == null) return;
                Rectangle src = new Rectangle(c.Left + rect.X, c.Top + rect.Y, rect.Width, rect.Height);
                src.Intersect(new Rectangle(0, 0, surface.Width, surface.Height));
                if (src.Width <= 0 || src.Height <= 0) return;
                g.DrawImage(surface,
                    new Rectangle(src.X - c.Left, src.Y - c.Top, src.Width, src.Height),
                    src, GraphicsUnit.Pixel);
                return;
            }
            Theme.PaintBackdrop(c, g, rect);
        }
    }

    /// <summary>
    /// 双缓冲容器面板。标题栏、侧边栏、状态栏、内容区都用它，
    /// 避免整块重绘时先擦白再画造成的闪烁。
    /// </summary>
    public class LgPanel : Panel
    {
        /// <summary>为 true 时，空白处按住即可拖动窗口（标题栏、侧栏）</summary>
        public bool CaptionDrag = false;

        public LgPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // WS_CLIPCHILDREN：重绘容器时不覆盖子控件区域，避免子控件被擦掉又重画造成闪烁。
                cp.Style |= 0x02000000;
                return cp;
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (WindowDrag.TryStart(this, e, CaptionDrag)) return;
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            base.OnPaint(e);
        }
    }

    public class LgButton : Control
    {
        public Color ColorA = Theme.Accent;
        public Color ColorB = Theme.Accent2;
        public bool Ghost = false;
        public bool FollowTheme = true;
        public int Radius = 10;

        // 过渡动画进度：hover 淡入、按下缩放，都是 0..1 的缓动值。
        // 原来 hover/down 是布尔瞬切，颜色「啪」地跳；现在读 hoverT/downT 插值，
        // 鼠标进出一瞬间有连续的淡入淡出，按下弹起有「按进去」的缓动。
        private readonly AnimFloat hoverT = new AnimFloat(0.30f);
        private readonly AnimFloat downT = new AnimFloat(0.40f);

        public LgButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(120, 40);
            Cursor = Cursors.Hand;
            Font = Theme.BodyBold;
            ForeColor = Color.White;
            BackColor = Theme.Bg;
        }

        public LgButton(string text) : this()
        {
            Text = text;
        }

        public void UseAccent()
        {
            ColorA = Theme.Accent;
            ColorB = Theme.Accent2;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hoverT.Target = 1f;
            Anime.Sync(this, hoverT);
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hoverT.Target = 0f;
            downT.Target = 0f;
            Anime.Sync(this, hoverT);
            Anime.Sync(this, downT);
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            downT.Target = 1f;
            Anime.Sync(this, downT);
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            downT.Target = 0f;
            Anime.Sync(this, downT);
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.EnableQuality(g);
            if (!Surf.PaintCardBacked(this, g)) g.Clear(Surf.Of(this));

            // 按下缩放：按下时整体缩到 0.96，松开弹回。这是「按键动画」里最直观的一条，
            // 给鼠标一个物理回弹的反馈，比单纯变色更接近 iOS 液态玻璃的「按进水面」手感。
            float press = downT.Value;
            int shrink = (int)Math.Round(Width * press * 0.02f);   // 每边各缩 2% * press
            Rectangle rect = new Rectangle(shrink, shrink,
                Math.Max(1, Width - 1 - shrink * 2), Math.Max(1, Height - 1 - shrink * 2));
            if (rect.Width <= 0 || rect.Height <= 0) return;

            // Ghost 按钮跟随主题色；但「删除实例」这类显式指定了红色又用 Ghost 的，
            // 红色语义要保留，否则白底下的危险操作看起来和普通按钮一模一样。
            Color ghostAccent = FollowTheme ? Theme.Accent : ColorA;
            // 胶囊按钮：圆角不能超过半高，否则 GDI+ 会把弧画翻
            int radius = Math.Min(Radius, Math.Max(2, rect.Height / 2));
            // 悬停/按下插值：hover 0..1 决定「透出多少悬停色」，down 0..1 决定「压暗多少」
            float hov = hoverT.Value;
            float prs = downT.Value;

            if (Theme.LiquidMode)
            {
                // 液态玻璃主题：按钮就是参考图里那颗胶囊 —— 圆角取满半高
                radius = Math.Max(6, rect.Height / 2);
                PaintLiquidBody(g, rect, radius, ghostAccent, hov, prs);
            }
            else if (Theme.MillenniumMode)
            {
                // 千禧年：圆角只取 8~10px（那个年代的按钮是「圆角矩形」，不是胶囊），
                // 材质是 Aqua 清漆：上半个亮、下半个本色、中间一条硬分界。
                radius = Math.Min(radius, 10);
                PaintAquaBody(g, rect, radius, ghostAccent, hov, prs);
            }
            else
            {
            using (GraphicsPath path = Theme.Round(rect, radius))
            {
                if (!Enabled)
                {
                    using (SolidBrush b = new SolidBrush(Theme.Disabled)) g.FillPath(b, path);
                }
                else if (Ghost)
                {
                    // 白底下的次级按钮：白填充 + 细描边。悬停才透出一点主色，不悬停保持干净
                    Color fill = Theme.Mix(Theme.Card, Theme.Alpha(ghostAccent, 24), hov);
                    Color edge = Theme.Mix(Theme.Border, ghostAccent, hov);
                    using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, path);
                    using (Pen p = new Pen(edge, 1f)) g.DrawPath(p, path);
                }
                else
                {
                    Color baseA = FollowTheme ? Theme.Accent : ColorA;
                    Color baseB = FollowTheme ? Theme.Accent2 : ColorB;
                    // 垂直渐变（上亮下深）比水平渐变更像实体按钮；
                    // 水平渐变在宽按钮上会显得像一条色带，是「廉价感」的常见来源。
                    Color top = Theme.Lighten(baseA, 0.06f);
                    Color bottom = Theme.Darken(baseB, 0.08f);
                    // 悬停提亮、按下压暗，都用插值平滑过渡，不再瞬跳
                    top = Theme.Mix(top, Theme.Lighten(top, 0.08f), hov);
                    bottom = Theme.Mix(bottom, Theme.Lighten(bottom, 0.08f), hov);
                    top = Theme.Mix(top, Theme.Darken(top, 0.10f), prs);
                    bottom = Theme.Mix(bottom, Theme.Darken(bottom, 0.10f), prs);
                    using (LinearGradientBrush brush = new LinearGradientBrush(
                        new Rectangle(0, 0, Math.Max(1, Width), Math.Max(1, Height)), top, bottom, LinearGradientMode.Vertical))
                    {
                        g.FillPath(brush, path);
                    }

                    // 顶部高光：一条由白到透明的窄渐变，给实心按钮一点厚度
                    Region old = g.Clip;
                    g.SetClip(path, CombineMode.Intersect);
                    Rectangle shine = new Rectangle(rect.X, rect.Y, Math.Max(1, rect.Width), Math.Max(2, rect.Height / 2));
                    using (LinearGradientBrush hl = new LinearGradientBrush(shine,
                        Color.FromArgb(58, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Vertical))
                    {
                        g.FillRectangle(hl, shine);
                    }
                    g.Clip = old;
                    if (old != null) old.Dispose();
                }
            }
            }

            Color textColor;
            if (!Enabled) textColor = Theme.SubText;
            else if (Ghost) textColor = FollowTheme ? Theme.Text : ghostAccent;   // 浅底按钮必须深色字
            else textColor = ForeColor;
            // 按下时文字下移 1px：鼠标按住的那一瞬间要有物理反馈，否则像在点一张图片
            Rectangle textRect = new Rectangle(0, prs > 0.5f ? 1 : 0, Width, Height);
            TextRenderer.DrawText(g, Text, Font, textRect, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        /// <summary>
        /// 液态玻璃按钮：一颗被主色染过的玻璃胶囊。
        ///
        /// 结构与参考图一致 —— 底是折射过的背景（按钮在卡片里时取卡片表面），
        /// 上面压一层半透明主色，再压顶部反光，最后描一圈内白外蓝的边。
        /// 不铺实色是刻意的：实色按钮在任何玻璃界面里都会「浮」起来，像贴上去的色块。
        /// </summary>
        private void PaintLiquidBody(Graphics g, Rectangle rect, int radius, Color accent, float hov, float prs)
        {
            Color baseA = FollowTheme ? Theme.Accent : ColorA;
            Color baseB = FollowTheme ? Theme.Accent2 : ColorB;

            if (!Enabled)
            {
                Theme.PaintPill(this, g, rect, radius, 110, Color.FromArgb(196, 205, 220), 42);
                return;
            }

            Theme.PaintPill(this, g, rect, radius, Ghost ? 40 : 8, Color.Empty, 0);

            if (Ghost)
            {
                if (hov > 0.01f)
                {
                    using (GraphicsPath path = Theme.Round(rect, radius))
                    {
                        using (SolidBrush b = new SolidBrush(Theme.Alpha(accent, (int)(46 * hov)))) g.FillPath(b, path);
                        using (Pen p = new Pen(Theme.Alpha(accent, (int)(150 * hov)), 1f)) g.DrawPath(p, path);
                    }
                }
                return;
            }

            // 主色浓度不能太低。按钮多半躺在卡片里，底是卡片那张**平滑**的玻璃面，
            // 没有条纹可弯，折射在这个尺度上等于没画；能拉开对比的只剩自身材质。
            // 之前用 152/198，预览图里「启动游戏」几乎和卡片融成一片 —— 半透明是手段，
            // 不是目的，按钮先要像个按钮，再谈它有多透。
            // 悬停提亮、按下压暗都用插值，过渡连续不断跳。
            int topA = 184 + (int)(14 * hov) + (int)(16 * prs);
            int botA = 222 + (int)(12 * hov) + (int)(10 * prs);
            Color top = Theme.Alpha(Theme.Lighten(baseA, 0.12f + 0.14f * hov), Math.Min(255, topA));
            Color bottom = Theme.Alpha(Theme.Darken(baseB, 0.02f - 0.16f * hov), Math.Min(255, botA));
            using (GraphicsPath path = Theme.Round(rect, radius))
            {
                using (LinearGradientBrush brush = new LinearGradientBrush(
                    new Rectangle(rect.X, rect.Y, Math.Max(1, rect.Width), Math.Max(1, rect.Height)),
                    top, bottom, LinearGradientMode.Vertical))
                {
                    g.FillPath(brush, path);
                }
            }
        }

        /// <summary>
        /// 千禧年按钮：Aqua 清漆。
        ///
        /// 和液态玻璃按钮的区别不是"更白一点"，而是**结构的区别**：
        ///  - 液态玻璃是「底下有什么就透什么」，靠折射和透光度说话；
        ///  - 千禧年是「不透明的高饱和底色 + 一层刷上去的清漆」，
        ///    上半个亮、下半个本色、46% 处一条明确的硬分界。
        /// 那个年代的所有按钮（XP 的 Luna、初代 iOS、百度/腾讯的 2008 版官网）
        /// 都是这个骨架，所以这里一旦画成柔和渐变就立刻不像了。
        /// </summary>
        private void PaintAquaBody(Graphics g, Rectangle rect, int radius, Color accent, float hov, float prs)
        {
            Color baseA = FollowTheme ? Theme.Accent : ColorA;
            Color baseB = FollowTheme ? Theme.Accent2 : ColorB;

            using (GraphicsPath path = Theme.Round(rect, radius))
            {
                if (!Enabled)
                {
                    using (LinearGradientBrush b = new LinearGradientBrush(
                        new Rectangle(rect.X, rect.Y, Math.Max(1, rect.Width), Math.Max(1, rect.Height)),
                        Theme.Disabled, Theme.Mix(Theme.Disabled, Theme.BorderStrong, 0.5f), LinearGradientMode.Vertical))
                    {
                        g.FillPath(b, path);
                    }
                    Theme.DrawRound(g, rect, radius, Theme.Border, 1f);
                    return;
                }

                if (Ghost)
                {
                    // 次级按钮：奶白清漆 + 灰蓝边，悬停才透出主色
                    using (LinearGradientBrush b = new LinearGradientBrush(
                        new Rectangle(rect.X, rect.Y, Math.Max(1, rect.Width), Math.Max(1, rect.Height)),
                        Color.FromArgb(252, 255, 255, 255), Color.FromArgb(226, 240, 250), LinearGradientMode.Vertical))
                    {
                        g.FillPath(b, path);
                    }
                    if (hov > 0.01f)
                    {
                        using (SolidBrush b = new SolidBrush(Theme.Alpha(accent, (int)(40 * hov)))) g.FillPath(b, path);
                    }
                    Theme.PaintGloss(g, rect, radius, 150);
                    Theme.DrawRound(g, rect, radius, Theme.Mix(Theme.BorderStrong, accent, hov), 1f);
                    return;
                }

                Color topColor = Theme.Lighten(baseA, 0.24f);
                Color bottomColor = Theme.Darken(baseB, 0.04f);
                topColor = Theme.Mix(topColor, Theme.Lighten(baseA, 0.34f), hov);
                bottomColor = Theme.Mix(bottomColor, Theme.Lighten(baseB, 0.08f), hov);
                topColor = Theme.Mix(topColor, Theme.Lighten(baseA, 0.04f), prs);
                bottomColor = Theme.Mix(bottomColor, Theme.Darken(baseB, 0.16f), prs);
                using (LinearGradientBrush b = new LinearGradientBrush(
                    new Rectangle(rect.X, rect.Y, Math.Max(1, rect.Width), Math.Max(1, rect.Height)),
                    topColor, bottomColor, LinearGradientMode.Vertical))
                {
                    g.FillPath(b, path);
                }

                // 清漆层：按下时压薄（视觉上"按进去了"）
                Theme.PaintGloss(g, rect, radius, (int)(200 - 80 * prs));

                // 外描边 + 内侧亮线：Aqua 的边是「深色细边 + 内白线」两条，
                // 只有一条边会显得像贴纸。
                Theme.DrawRound(g, rect, radius, Color.FromArgb(130, 18, 56, 104), 1f);
            }
        }
    }

    public class LgIconButton : Control
    {
        public Color HoverColor = Theme.CardHover;
        public bool Danger = false;
        private bool hover;
        private readonly AnimFloat hoverT = new AnimFloat(0.32f);

        public LgIconButton(string glyph)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Text = glyph;
            Size = new Size(34, 30);
            Cursor = Cursors.Hand;
            Font = Theme.Body;
            BackColor = Theme.Bg;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hover = true;
            hoverT.Target = 1f;
            Anime.Sync(this, hoverT);
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = false;
            hoverT.Target = 0f;
            Anime.Sync(this, hoverT);
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // 图标按钮挂在标题栏（玻璃条）上。铺底不能用 PaintBackdrop 只铺壁纸：
            // 标题栏是「壁纸 + TitleBarAlpha 白」，只铺壁纸会差整整一档白，
            // 在液金主题的蓝白条纹上就是三块偏暗的方块。这里用同一个浓度
            // 重铺一遍栏的玻璃，鼠标不悬停时按钮才真的「隐身」。
            Graphics g = e.Graphics;
            Theme.EnableQuality(g);
            Theme.PaintBarFlat(this, g, ClientRectangle, Theme.TitleBarAlpha);
            float hov = hoverT.Value;
            Rectangle inner = new Rectangle(2, 2, Math.Max(1, Width - 5), Math.Max(1, Height - 5));
            if (hov > 0.01f)
            {
                if (Theme.SolidMode)
                {
                    Color fill = Danger ? Theme.Bad : Theme.CardHover;
                    fill = Theme.Alpha(fill, (int)(255 * hov));
                    Theme.FillRound(g, inner, 8, fill);
                }
                else
                {
                    // 玻璃皮肤：悬停时浮出一颗小玻璃胶囊，而不是一块实色方块
                    int r = Theme.LiquidMode ? inner.Height / 2 : 8;
                    Theme.PaintWell(this, g, inner, r, (int)(46 * hov),
                        Danger ? Theme.Bad : Theme.Accent, Danger ? (int)(150 * hov) : (int)(54 * hov));
                }
            }
            Color color = hover ? (Danger ? Color.White : Theme.Text) : Theme.SubText;
            // 悬停时文字随 hov 从 SubText 渐变到 Text
            if (hov > 0.01f && hov < 1f)
            {
                color = Theme.Mix(Theme.SubText, Danger ? Color.White : Theme.Text, hov);
            }
            TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, Width, Height), color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    public class LgProgress : Control
    {
        private double value;
        private bool indeterminate;
        private int offset;
        private Timer timer;

        public LgProgress()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 8;
            BackColor = Theme.Card;
        }

        public double Value
        {
            get { return value; }
            set
            {
                double v = value;
                if (v < 0) v = 0;
                if (v > 1) v = 1;
                if (Math.Abs(this.value - v) < 0.0005) return;
                this.value = v;
                Invalidate();
            }
        }

        public bool Indeterminate
        {
            get { return indeterminate; }
            set
            {
                if (indeterminate == value) return;
                indeterminate = value;
                if (indeterminate)
                {
                    if (timer == null)
                    {
                        timer = new Timer();
                        timer.Interval = 33;
                        timer.Tick += new EventHandler(OnTick);
                    }
                    timer.Start();
                }
                else if (timer != null)
                {
                    timer.Stop();
                }
                Invalidate();
            }
        }

        private void OnTick(object sender, EventArgs e)
        {
            offset += 6;
            if (offset > Width + 200) offset = -120;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.EnableQuality(g);
            // 进度条挂在状态栏（玻璃条）上。用纯色清底会在玻璃上压出一小块比周围更亮的方块，
            // 所以这里先按状态栏同一套算法铺底（栏底，不含顶光），颜色逐像素对齐，
            // 看不出控件边界。注意是 PaintBarFlat 不是 PaintGlassBar ——
            // 8px 高的进度条自己画顶光会得到一道栏上并不存在的高光。
            if (Parent is LgPanel) Theme.PaintBarFlat(this, g, ClientRectangle, Theme.ChromeBarAlpha);
            else if (!Surf.PaintCardBacked(this, g)) g.Clear(Surf.Of(this));
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            if (rect.Width <= 0 || rect.Height <= 0) return;
            // 轨道：纯色 = 平涂 Track；两种玻璃皮肤 = 玻璃胶囊（千禧年的雾更厚）
            if (Theme.SolidMode) Theme.FillRound(g, rect, Height / 2, Theme.Track);
            else Theme.PaintWell(this, g, rect, Height / 2, 52);

            if (indeterminate)
            {
                int blockWidth = Math.Max(60, Width / 4);
                int x = offset;
                if (x > Width) x = -blockWidth;
                Rectangle r = new Rectangle(x, 0, blockWidth, Height - 1);
                using (GraphicsPath path = Theme.Round(new Rectangle(0, 0, Width - 1, Height - 1), Height / 2))
                {
                    Region old = g.Clip;
                    g.SetClip(path);
                    using (LinearGradientBrush brush = Theme.Gradient(new Rectangle(x, 0, blockWidth, Math.Max(1, Height)), Theme.Alpha(Theme.Accent, 0), Theme.Accent2, 0f))
                    {
                        g.FillRectangle(brush, r);
                    }
                    g.Clip = old;
                }
                return;
            }

            int fill = (int)((Width - 1) * value);
            if (fill > 2)
            {
                using (GraphicsPath path = Theme.Round(new Rectangle(0, 0, Width - 1, Height - 1), Height / 2))
                {
                    Region old = g.Clip;
                    g.SetClip(path);
                    using (LinearGradientBrush brush = Theme.Gradient(new Rectangle(0, 0, Math.Max(2, fill), Math.Max(1, Height)), Theme.Accent, Theme.Accent2, 0f))
                    {
                        g.FillRectangle(brush, new Rectangle(0, 0, fill, Height - 1));
                    }
                    if (Theme.LiquidMode)
                    {
                        // 进度条本身也是一根小玻璃棒：上沿留一条亮线，才有圆柱感
                        Rectangle shine = new Rectangle(0, 0, Math.Max(1, fill), Math.Max(1, Height / 2));
                        using (LinearGradientBrush hl = new LinearGradientBrush(shine,
                            Color.FromArgb(120, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Vertical))
                        {
                            g.FillRectangle(hl, shine);
                        }
                    }
                    g.Clip = old;
                }
            }
        }
    }

    public class LgCard : Panel
    {
        public Color Fill = Theme.GlassCard;
        public int Radius = Theme.RadiusCard;
        public bool ShowBorder = true;
        public Color BorderColor = Theme.Border;

        /// <summary>
        /// 深色卡片：日志 / 控制台用。
        ///
        /// 浅色玻璃卡里塞一块纯白 TextBox，是整屏最廉价的地方 —— 白矩形没有材质、
        /// 没有层次，右边还挂着系统灰白滚动条。深色板反过来是刻意制造的对比：
        /// 容器是浅玻璃、内容是深玻璃，这是现代界面分层的基本做法
        /// （代码块之于文档、终端之于 IDE 都是这个关系）。
        /// 一旦为 true，卡片不再跑折射和薄膜，走 <see cref="PaintDarkSurface"/>。
        /// </summary>
        public bool Dark = false;

        private Bitmap surface;
        private Size surfaceSize;
        private int surfaceRevision = -1;

        public LgCard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.GlassCard;
        }

        /// <summary>
        /// 卡片的离屏表面。子控件（文字、色块）靠裁这块图来铺自己的背景，
        /// 所以它必须和 OnPaint 画出的一模一样——两边共用 PaintSurface。
        /// </summary>
        public Bitmap Surface
        {
            get
            {
                if (Width <= 0 || Height <= 0) return null;
                if (surface != null && surfaceSize.Width == Width && surfaceSize.Height == Height
                    && surfaceRevision == Theme.Revision)
                {
                    return surface;
                }
                DropSurface();
                Bitmap bmp = new Bitmap(Width, Height);
                try
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        Theme.EnableQuality(g);
                        PaintSurface(g, new Rectangle(0, 0, Width, Height));
                    }
                }
                catch
                {
                    bmp.Dispose();
                    return null;
                }
                surface = bmp;
                surfaceSize = new Size(Width, Height);
                surfaceRevision = Theme.Revision;
                return surface;
            }
        }

        private void DropSurface()
        {
            if (surface != null)
            {
                surface.Dispose();
                surface = null;
            }
            surfaceSize = Size.Empty;
            surfaceRevision = -1;
        }

        public void PaintSurface(Graphics g, Rectangle area)
        {
            // 玻璃必须画满整个控件。
            // 曾经试过四周各留 14px 给外投影，结果是：卡片内容整体向下右缩 14px、
            // 从 x=0 起画的子控件（首页的彩色竖条）被投影压住、
            // LgList 这类矩形原生控件右沿伸出玻璃之外被切成缺角。
            // 外投影改由 Theme.CardShade 用「环形」贴着圆角外侧画，不再占用卡片内部像素。
            Rectangle rect = new Rectangle(area.X, area.Y,
                Math.Max(1, area.Width - 1), Math.Max(1, area.Height - 1));
            Rectangle full = new Rectangle(area.X, area.Y, area.Width, area.Height);

            // 1) 先铺整窗壁纸。圆角之外要显示页面背景，玻璃才像是「压在页面上」的。
            Theme.PaintBackdrop(this, g, full);

            // 0a) 深色卡片：日志 / 控制台。必须在壁纸之后画 ——
            //     圆角之外那圈像素要留给壁纸，否则深色板的四角会是一个黑方块。
            if (Dark)
            {
                PaintDarkSurface(g, full);
                return;
            }

            // 0) 纯色主题：到此为止。不折射、不磨砂、不聚光 ——
            //    那个皮肤的全部卖点就是「一块干净的白卡 + 一根细边」，
            //    继续跑玻璃算法只会让它在平涂背景上显脏。
            if (Theme.SolidMode)
            {
                Theme.FillRound(g, full, Radius, Fill);
                Theme.DrawRound(g, rect, Radius, BorderColor, 1f);
                return;
            }

            // 2) 折射层：整块液态玻璃的主体。
            //    这一步把壁纸按 SDF 做边缘位移采样 + 模糊 + 提饱和，
            //    出来的画面在边缘是「弯的」，中间几乎是原样 —— 这才是玻璃。
            //    之前的版本完全没有这一步，只有一层半透明白，所以怎么调都像塑料。
            Bitmap refracted = Liquid.Build(this, area.Width, area.Height, Radius);
            if (refracted != null)
            {
                Region old = g.Clip;
                try
                {
                    // 用 full 而不是 rect 做裁剪：rect 比控件小 1px，
                    // 那 1px 会残留没折射过的壁纸，在卡片右/下沿露出一条比玻璃更暗的细边。
                    using (GraphicsPath path = Theme.Round(full, Radius))
                    {
                        g.SetClip(path, CombineMode.Intersect);
                        g.DrawImageUnscaled(refracted, area.X, area.Y);
                    }
                }
                catch
                {
                    // 折射图贴不上就退回纯壁纸，不能整块黑掉
                }
                finally
                {
                    g.Clip = old;
                    if (old != null) old.Dispose();
                }
            }

            // 3) 圆角之外重新铺一次壁纸：折射图是按矩形裁剪的，
            //    四个角上会残留一点方形玻璃，铺一次才是真正的弧形边界。
            Theme.ClearOutsideRound(this, g, rect, Radius);

            // 4) 接触阴影 / AO 内环
            Theme.CardShade(g, rect, Radius);

            // 5) 玻璃薄膜：淡雾 + 磨砂 + 反光带 + 边缘聚光
            Theme.PaintGlass(g, rect, Radius, Theme.GlassAlpha);

            // 6) 千禧年：在玻璃之上再加一层 Aqua 顶光（上半个发白、46% 处一条硬边）
            if (Theme.MillenniumMode) Theme.PaintGloss(g, rect, Radius, 118);
        }

        /// <summary>
        /// 深色玻璃板。现在也走折射，让背景透出来 —— 之前是纯色实底，
        /// 用户反馈「日志卡片是一块不透明的黑塑料，和周围玻璃割裂」。
        /// 做法和浅色卡片一致：先折射壁纸，再叠一层半透明深色玻璃膜，
        /// 这样既保住了「终端」的深底对比，又让卡片是透的。
        /// </summary>
        private void PaintDarkSurface(Graphics g, Rectangle full)
        {
            Rectangle rect = new Rectangle(full.X, full.Y,
                Math.Max(1, full.Width - 1), Math.Max(1, full.Height - 1));

            // 纯色主题下仍然是一块实底深灰卡（纯色皮肤本来就没有玻璃）
            if (Theme.SolidMode)
            {
                using (GraphicsPath path = Theme.Round(full, Radius))
                {
                    using (SolidBrush b = new SolidBrush(Theme.LogBg))
                    {
                        g.FillPath(b, path);
                    }
                }
                Theme.DrawRound(g, rect, Radius, Color.FromArgb(40, 255, 255, 255), 1f);
                return;
            }

            // 1) 折射层：和其它玻璃卡片同源，把壁纸边缘弯折、中间糊开
            Bitmap refracted = Liquid.Build(this, full.Width, full.Height, Radius);
            if (refracted != null)
            {
                Region old = g.Clip;
                try
                {
                    using (GraphicsPath path = Theme.Round(full, Radius))
                    {
                        g.SetClip(path, CombineMode.Intersect);
                        g.DrawImageUnscaled(refracted, full.X, full.Y);
                    }
                }
                catch
                {
                }
                finally
                {
                    g.Clip = old;
                    if (old != null) old.Dispose();
                }
            }

            // 2) 半透明深色玻璃膜：在折射之上压一层深色，让「深底」成立，同时保留透感
            using (GraphicsPath path = Theme.Round(full, Radius))
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(200, Theme.LogBg)))
                {
                    g.FillPath(b, path);
                }
            }

            // 3) 顶部一层极淡的顶光：光从上来，深色玻璃也有体积
            int half = Math.Max(2, full.Height / 2);
            Rectangle top = new Rectangle(full.X, full.Y, full.Width, half);
            using (LinearGradientBrush brush = new LinearGradientBrush(top,
                Color.FromArgb(26, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Vertical))
            {
                g.FillRectangle(brush, top);
            }

            // 4) 内侧 1px 白光：深色玻璃的边缘亮线
            Theme.DrawRound(g, rect, Radius, Color.FromArgb(46, 255, 255, 255), 1f);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.EnableQuality(e.Graphics);
            Bitmap bmp = Surface;
            if (bmp != null)
            {
                e.Graphics.DrawImageUnscaled(bmp, 0, 0);
            }
            else
            {
                e.Graphics.Clear(Theme.Bg);
            }
            base.OnPaint(e);
        }

        protected override void OnResize(EventArgs e)
        {
            DropSurface();
            base.OnResize(e);
        }

        /// <summary>
        /// 位置变了也要重建表面。1.3.0 之后卡片表面里包含了「按窗体坐标裁出来的折射图」，
        /// 卡片平移会让它对应的壁纸区域变掉；如果只按尺寸判断缓存，
        /// 侧栏折叠、窗口变宽这类「尺寸没变、位置变了」的重排会留下一张错位的旧图。
        /// </summary>
        protected override void OnLocationChanged(EventArgs e)
        {
            DropSurface();
            base.OnLocationChanged(e);
        }

        protected override void OnParentChanged(EventArgs e)
        {
            DropSurface();
            base.OnParentChanged(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) DropSurface();
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// 卡片 / 页面上的文字。背景直接裁父卡片那一块玻璃，所以文字像「印在玻璃上」，
    /// 不会出现一块白底。父级不是卡片时退回页面壁纸。
    /// </summary>
    public class LgLabel : Label
    {
        public LgLabel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AutoSize = false;
            BackColor = Color.Transparent;
            UseMnemonic = false;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // 背景由 OnPaint 自己铺。这里留空是为了挡掉 Label 默认的
            // 「透明背景 → 回调父控件重绘 → 取到纯底色」这条路。
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.EnableQuality(g);
            GlassCrop.Paint(this, g);
            if (Text == null || Text.Length == 0) return;

            TextFormatFlags flags = TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak;
            switch (TextAlign)
            {
                case ContentAlignment.TopLeft:
                    flags |= TextFormatFlags.Top | TextFormatFlags.Left;
                    break;
                case ContentAlignment.TopCenter:
                    flags |= TextFormatFlags.Top | TextFormatFlags.HorizontalCenter;
                    break;
                case ContentAlignment.TopRight:
                    flags |= TextFormatFlags.Top | TextFormatFlags.Right;
                    break;
                case ContentAlignment.MiddleCenter:
                    flags |= TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter;
                    break;
                case ContentAlignment.MiddleRight:
                    flags |= TextFormatFlags.VerticalCenter | TextFormatFlags.Right;
                    break;
                case ContentAlignment.BottomLeft:
                    flags |= TextFormatFlags.Bottom | TextFormatFlags.Left;
                    break;
                case ContentAlignment.BottomCenter:
                    flags |= TextFormatFlags.Bottom | TextFormatFlags.HorizontalCenter;
                    break;
                case ContentAlignment.BottomRight:
                    flags |= TextFormatFlags.Bottom | TextFormatFlags.Right;
                    break;
                default:
                    flags |= TextFormatFlags.VerticalCenter | TextFormatFlags.Left;
                    break;
            }
            // Label 自带的 3px 内边距，对齐用，去掉会显得文字贴边
            Rectangle rect = new Rectangle(3, 0, Math.Max(1, Width - 6), Height);
            TextRenderer.DrawText(g, Text, Font, rect, Enabled ? ForeColor : Theme.SubText, flags);
        }
    }

    /// <summary>圆形主题色选择器。方色块 + FixedSingle 边框是最典型的「原生控件感」。</summary>
    public class LgSwatch : Control
    {
        public Color Value = Theme.Accent;
        public bool Selected = false;
        private bool hover;

        public LgSwatch(Color color)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Value = color;
            Size = new Size(30, 30);
            Cursor = Cursors.Hand;
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
            GlassCrop.Paint(this, g);
            int size = Math.Min(Width, Height);
            int d = Math.Max(6, size - 8);
            if (hover || Selected) d = Math.Max(6, size - 4);
            Rectangle circle = new Rectangle((Width - d) / 2, (Height - d) / 2, d, d);

            // 选中 / 悬停时外圈留一道白，色块才不会糊在背景里
            if (Selected)
            {
                using (Pen pen = new Pen(Color.FromArgb(230, 255, 255, 255), 2f))
                {
                    g.DrawEllipse(pen, circle.X - 3, circle.Y - 3, d + 5, d + 5);
                }
            }
            using (SolidBrush brush = new SolidBrush(Value))
            {
                g.FillEllipse(brush, circle);
            }
            using (Pen rim = new Pen(Color.FromArgb(50, Theme.Shadow), 1f))
            {
                g.DrawEllipse(rim, circle);
            }
            if (Selected)
            {
                // 对勾用两条线画，不依赖字体是否有 ✔
                using (Pen check = new Pen(Color.White, 2f))
                {
                    check.StartCap = LineCap.Round;
                    check.EndCap = LineCap.Round;
                    Point a = new Point(circle.X + d / 4, circle.Y + d / 2);
                    Point b = new Point(circle.X + d * 5 / 12, circle.Y + d * 2 / 3);
                    Point c = new Point(circle.X + d * 3 / 4, circle.Y + d / 3);
                    g.DrawLines(check, new Point[] { a, b, c });
                }
            }
        }
    }

    /// <summary>
    /// 统一底色的文本框。原生 TextBox 不能透明，玻璃界面上它会是一块「贴上去的白纸」，
    /// 所以至少要让它和所在玻璃的色温一致：输入框跟 InputBg，日志区跟 LogBg。
    /// 换主题时由 <see cref="Theme.ApplySkin"/> 统一刷新，不需要每个页面自己记着改。
    /// </summary>
    public class LgTextBox : TextBox
    {
        /// <summary>true = 日志区（等宽、更淡的底），false = 输入框</summary>
        public bool LogStyle = false;

        public LgTextBox()
        {
            BorderStyle = BorderStyle.None;
            Font = Theme.Body;
            BackColor = Theme.InputBg;
            ForeColor = Theme.Text;
        }

        /// <summary>
        /// 日志区底色和卡片深色玻璃膜保持一致，让 TextBox 的矩形边界彻底消失。
        /// 标准 TextBox 不支持真正的透明背景（Color.Transparent 会显示成白色），
        /// 所以这里不追求透明，而是让 TextBox 底色 = 卡片深色玻璃的等效色，
        /// 视觉上就是「一整块深色玻璃」，边界不可见。
        /// </summary>
        public void ApplySkin()
        {
            BackColor = LogStyle ? Theme.LogBg : Theme.InputBg;
            // 日志区底色是深的，用 SubText（为浅底调的中灰）会几乎看不见
            ForeColor = LogStyle ? Theme.LogText : Theme.Text;
        }
    }

    public class LgInput : Panel
    {
        private readonly TextBox box;

        public LgInput()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
            Height = 38;
            box = new LgTextBox();
            box.BorderStyle = BorderStyle.None;
            box.BackColor = Theme.InputBg;
            box.ForeColor = Theme.Text;
            box.Font = Theme.Body;
            box.Location = new Point(14, 11);
            box.Width = Width - 28;
            box.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            // TextBox 是子控件，外层的填充色管不到它。
            // 聚焦时内外一起转白，否则会看到「白框里嵌一块浅灰」的补丁。
            box.GotFocus += new EventHandler(delegate(object s, EventArgs e)
            {
                box.BackColor = Theme.Card;
                Invalidate();
            });
            box.LostFocus += new EventHandler(delegate(object s, EventArgs e)
            {
                box.BackColor = Theme.InputBg;
                Invalidate();
            });
            Controls.Add(box);
        }

        public TextBox Inner
        {
            get { return box; }
        }

        /// <summary>主题切换后重刷内嵌 TextBox 的底色（它不参与自绘）</summary>
        public void ApplySkin()
        {
            BackColor = Theme.Card;
            if (box != null && !box.Focused)
            {
                box.BackColor = Theme.InputBg;
                box.ForeColor = Theme.Text;
            }
            Invalidate();
        }

        public override string Text
        {
            get { return box.Text; }
            set { box.Text = value; }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (box != null)
            {
                box.Width = Math.Max(10, Width - 28);
                box.Top = Math.Max(4, (Height - box.PreferredHeight) / 2);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.EnableQuality(g);
            if (!Surf.PaintCardBacked(this, g)) g.Clear(Surf.Of(this));
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            if (rect.Width <= 0 || rect.Height <= 0) return;
            if (Theme.SolidMode)
            {
                // 未聚焦时用极浅的底，聚焦时转纯白 + 主色描边
                Theme.FillRound(g, rect, Theme.RadiusInput, box.Focused ? Theme.Card : Theme.InputBg);
                Theme.DrawRound(g, rect, Theme.RadiusInput, box.Focused ? Theme.Accent : Theme.Border, 1f);
            }
            else
            {
                // 玻璃皮肤：输入框是一枚「浅玻璃胶囊」，聚焦时描边换成主色 ——
                // 焦点状态要一眼能看出来，所以描边加粗到 1.6px。
                // 液态玻璃取满半高（胶囊）；千禧年是圆角矩形，圆角只给 8。
                int r = Theme.LiquidMode ? Math.Max(10, Height / 2 - 4) : 8;
                Theme.PaintWell(this, g, rect, r, 50);
                if (box.Focused)
                {
                    Theme.DrawRound(g, rect, r, Theme.Alpha(Theme.Accent, 215), 1.6f);
                }
                else if (Theme.LiquidMode)
                {
                    Theme.DrawRound(g, rect, r, Color.FromArgb(115, 255, 255, 255), 1f);
                }
            }
            base.OnPaint(e);
        }
    }

    public class LgSwitch : Control
    {
        private bool checkedState;
        private bool hover;
        // 滑块滑动动画：开关切换时滑块平滑滑到另一头，而不是瞬移。
        // 0 = 关位，1 = 开位。轨道点亮也按这个值插值。
        private readonly AnimFloat slide = new AnimFloat(0.28f);

        public event EventHandler CheckedChanged;

        public LgSwitch()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(46, 26);
            Cursor = Cursors.Hand;
            BackColor = Theme.Card;
            // 初始状态：按 checkedState 定滑块起点，避免第一帧从 0 滑到当前值
            slide.Value = 0f;
            slide.Target = 0f;
        }

        public bool Checked
        {
            get { return checkedState; }
            set
            {
                if (checkedState == value)
                {
                    // 值没变也要同步动画目标（首次赋值时让滑块落到正确位置）
                    if (Math.Abs(slide.Target - (checkedState ? 1f : 0f)) > 0.01f)
                    {
                        slide.Target = checkedState ? 1f : 0f;
                        slide.Value = slide.Target;
                        Invalidate();
                    }
                    return;
                }
                checkedState = value;
                slide.Target = checkedState ? 1f : 0f;
                Anime.Sync(this, slide);
                Invalidate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnClick(EventArgs e)
        {
            Checked = !Checked;
            base.OnClick(e);
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
            if (!Surf.PaintCardBacked(this, g)) g.Clear(Surf.Of(this));
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            if (rect.Width <= 0 || rect.Height <= 0) return;
            float s = slide.Value;   // 0..1 滑块位置/点亮程度
            if (Theme.SolidMode)
            {
                using (GraphicsPath path = Theme.Round(rect, Height / 2))
                {
                    if (s > 0.01f)
                    {
                        using (LinearGradientBrush brush = Theme.Gradient(new Rectangle(0, 0, Math.Max(1, Width), Math.Max(1, Height)), Theme.Accent, Theme.Accent2, 90f))
                        {
                            g.FillPath(brush, path);
                        }
                    }
                    else
                    {
                        using (SolidBrush brush = new SolidBrush(hover ? Theme.Mix(Theme.Track, Theme.Border, 0.45f) : Theme.Track))
                        {
                            g.FillPath(brush, path);
                        }
                        using (Pen pen = new Pen(Theme.Border, 1f))
                        {
                            g.DrawPath(pen, path);
                        }
                    }
                }
            }
            else
            {
                // 玻璃皮肤：开关轨道也是一枚玻璃胶囊，关 = 透明槽，开 = 被主色点亮的槽
                Theme.PaintWell(this, g, rect, Height / 2, 38,
                    s > 0.01f ? Theme.Accent : Color.Empty, (int)(132 * s));
                if (Theme.LiquidMode)
                {
                    int edgeA = 115 + (int)(55 * s);
                    Theme.DrawRound(g, rect, Height / 2, Color.FromArgb(edgeA, 255, 255, 255), 1f);
                }
            }
            // 滑块带一点点投影：纯白圆片压在轨道上会「飘」，加投影才像装在一个槽里
            int knob = Height - 6;
            int travel = Width - knob - 6;
            int x = 3 + (int)Math.Round(travel * s);
            using (SolidBrush shade = new SolidBrush(Color.FromArgb(26, Theme.Shadow)))
            {
                g.FillEllipse(shade, x, 4, knob, knob);
            }
            using (SolidBrush brush = new SolidBrush(Theme.Card))
            {
                g.FillEllipse(brush, x, 3, knob, knob);
            }
        }
    }

    /// <summary>
    /// 下拉选择。
    ///
    /// 原生 ComboBox 会画出系统灰边框 + 方块箭头，在玻璃界面上是一块补丁；
    /// 1.4.0 起自己实现：胶囊形玻璃按钮 + 圆角弹出层。
    /// Items 直接暴露 List&lt;string&gt;，页面里的 Items.Clear / Add / Count 用法一行都不用改。
    /// </summary>
    public class LgCombo : Control
    {
        private readonly List<string> items = new List<string>();
        private int selectedIndex = -1;
        private bool hover;
        private LgDropDown drop;
        /// <summary>弹出层被关掉的时刻。用来挡掉「点一下组合框先关旧层、又立刻开新层」的抖手。</summary>
        private int closedAt;

        public event EventHandler SelectedIndexChanged;

        public LgCombo()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(160, 34);
            Cursor = Cursors.Hand;
            Font = Theme.Body;
            ForeColor = Theme.Text;
            BackColor = Theme.Bg;
        }

        public List<string> Items
        {
            get { return items; }
        }

        public int SelectedIndex
        {
            get { return selectedIndex; }
            set
            {
                int v = value;
                if (v < -1) v = -1;
                if (v >= items.Count) v = items.Count - 1;
                if (v == selectedIndex)
                {
                    Invalidate();
                    return;
                }
                selectedIndex = v;
                Invalidate();
                if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
        }

        public string SelectedText
        {
            get
            {
                if (selectedIndex >= 0 && selectedIndex < items.Count) return items[selectedIndex];
                return "";
            }
        }

        public void CloseDrop()
        {
            if (drop != null && !drop.IsDisposed)
            {
                try { drop.Close(); }
                catch { }
            }
            drop = null;
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

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || !Enabled) return;
            if (items.Count == 0) return;
            if (Environment.TickCount - closedAt < 220) return;
            if (drop != null && !drop.IsDisposed)
            {
                CloseDrop();
                return;
            }
            Form owner = FindForm();
            if (owner == null) return;
            try
            {
                LgDropDown d = new LgDropDown(items, selectedIndex, Font);
                d.Picked += new LgDropDown.PickHandler(OnPicked);
                d.FormClosed += new FormClosedEventHandler(OnDropClosed);
                int w = Math.Max(140, Width);
                int h = 10 + items.Count * 30;
                Rectangle wa = Screen.FromControl(this).WorkingArea;
                int maxH = Math.Max(120, Math.Min(360, wa.Height - 60));
                if (h > maxH) h = maxH;
                Point below = PointToScreen(new Point(0, Height + 2));
                int top = below.Y;
                if (top + h > wa.Bottom) top = PointToScreen(Point.Empty).Y - h - 2;
                if (top < wa.Top) top = wa.Top + 4;
                d.SetBounds(below.X, top, w, h);
                d.Show(owner);
                drop = d;
            }
            catch
            {
                drop = null;
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (items.Count == 0) return;
            int i = selectedIndex < 0 ? 0 : selectedIndex;
            i += e.Delta < 0 ? 1 : -1;
            if (i < 0) i = 0;
            if (i >= items.Count) i = items.Count - 1;
            SelectedIndex = i;
        }

        private void OnPicked(int index)
        {
            drop = null;
            SelectedIndex = index;
        }

        private void OnDropClosed(object sender, FormClosedEventArgs e)
        {
            drop = null;
            closedAt = Environment.TickCount;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.EnableQuality(g);
            // 圆角之外必须铺「背后那一层」，否则四个角会出现一块和背景对不上的实色
            if (!Surf.PaintCardBacked(this, g)) g.Clear(Surf.Of(this));
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            if (rect.Width <= 0 || rect.Height <= 0) return;

            if (Theme.SolidMode)
            {
                Theme.FillRound(g, rect, Theme.RadiusInput, hover ? Theme.CardHover : Theme.InputBg);
                Theme.DrawRound(g, rect, Theme.RadiusInput, Theme.Border, 1f);
            }
            else if (Theme.MillenniumMode)
            {
                int r = 8;
                Theme.PaintWell(this, g, rect, r, hover ? 66 : 46);
            }
            else
            {
                // 液态皮肤不叠白雾：在浅色玻璃卡上，白雾只会让控件更白、更「浮」，
                // 看起来就是贴了一张白纸。表单槽要的是「凹进去」——
                // 冷色薄染压暗，再用一圈白边把上沿提亮，一暗一亮才凹得下去。
                int r = Math.Max(10, Height / 2 - 2);
                // 20/255 的冷色叠在浅蓝卡上只有 8% 的压暗，肉眼几乎看不出来，
                // 下拉框因此还是「白框」。42 才压得下去。
                Theme.FillRound(g, rect, r, Color.FromArgb(hover ? 54 : 42, 30, 62, 132));
                Theme.DrawRound(g, rect, r,
                    hover ? Color.FromArgb(200, 255, 255, 255) : Color.FromArgb(130, 255, 255, 255), 1f);
            }

            string text = SelectedText;
            if (text.Length == 0 && items.Count > 0) text = items[0];
            Color color = Enabled ? Theme.Text : Theme.SubText;
            TextRenderer.DrawText(g, text, Font,
                new Rectangle(14, 0, Math.Max(10, Width - 40), Height), color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            // 箭头用两条线画，不依赖字体里有没有 ▾
            int cx = Width - 17;
            int cy = Height / 2;
            using (Pen pen = new Pen(hover ? Theme.Accent : Theme.SubText, 1.6f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawLine(pen, cx - 5, cy - 2, cx, cy + 3);
                g.DrawLine(pen, cx, cy + 3, cx + 5, cy - 2);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) CloseDrop();
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// 下拉弹出层。做成一个无边框小窗体而不是子 Panel：
    /// 它有自己的 HWND，可以盖在玻璃卡片之上而不被父控件裁剪，
    /// 也天生拥有「点到别处就关闭」的失活行为（Deactivate）。
    /// </summary>
    internal class LgDropDown : Form
    {
        public delegate void PickHandler(int index);

        public event PickHandler Picked;

        private readonly List<string> items;
        private readonly Font font;
        private int selected;
        private int hover = -1;
        private int first;

        private const int RowHeight = 30;
        private const int PadTop = 5;

        public LgDropDown(List<string> items, int selectedIndex, Font font)
        {
            this.items = items;
            this.font = font;
            this.selected = selectedIndex;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            BackColor = Color.FromArgb(248, 251, 255);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            // 选中项尽量落在可视区中间，长列表打开时不会「只看到最后一项」
            if (selectedIndex > 2) first = selectedIndex - 2;
            Deactivate += new EventHandler(OnDeactivate);
            MouseMove += new MouseEventHandler(OnMove);
            MouseDown += new MouseEventHandler(OnDown);
            MouseWheel += new MouseEventHandler(OnWheel);
            Paint += new PaintEventHandler(OnPaint);
        }

        private void OnDeactivate(object sender, EventArgs e)
        {
            try { Close(); }
            catch { }
        }

        private int IndexAt(int y)
        {
            int y0 = y - PadTop;
            if (y0 < 0) return -1;
            int i = first + y0 / RowHeight;
            if (i < 0 || i >= items.Count) return -1;
            return i;
        }

        private void OnMove(object sender, MouseEventArgs e)
        {
            int i = IndexAt(e.Y);
            if (i != hover)
            {
                hover = i;
                Invalidate();
            }
        }

        private void OnDown(object sender, MouseEventArgs e)
        {
            int i = IndexAt(e.Y);
            if (i < 0) return;
            if (Picked != null) Picked(i);
            try { Close(); }
            catch { }
        }

        private void OnWheel(object sender, MouseEventArgs e)
        {
            int visible = Math.Max(1, (Height - PadTop * 2) / RowHeight);
            int maxFirst = Math.Max(0, items.Count - visible);
            first += e.Delta < 0 ? 1 : -1;
            if (first < 0) first = 0;
            if (first > maxFirst) first = maxFirst;
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            try
            {
                using (GraphicsPath path = Theme.Round(new Rectangle(0, 0, Width, Height), 12))
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

        private void OnPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.EnableQuality(g);
            // 弹出层是独立窗体，拿不到父卡片的表面，只能自己铺一层近似玻璃的底。
            // 两种玻璃皮肤共用这一套（千禧年只是底更白一点）。
            bool liq = Theme.LiquidMode || Theme.MillenniumMode;
            g.Clear(liq ? Color.FromArgb(247, 251, 255) : Color.FromArgb(252, 253, 254));

            using (LinearGradientBrush glow = new LinearGradientBrush(
                new Rectangle(0, 0, Math.Max(1, Width), Math.Max(2, Height)),
                Color.FromArgb(150, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Vertical))
            {
                g.FillRectangle(glow, new Rectangle(0, 0, Math.Max(1, Width), Math.Max(1, Height / 2)));
            }

            int visible = (Height - PadTop * 2) / RowHeight + 1;
            for (int k = 0; k < visible; k++)
            {
                int index = first + k;
                if (index >= items.Count) break;
                Rectangle row = new Rectangle(6, PadTop + k * RowHeight, Math.Max(10, Width - 12), RowHeight - 2);
                if (index == hover)
                {
                    Theme.FillRound(g, row, 8, liq ? Theme.Alpha(Theme.Accent, 46) : Theme.CardHover);
                }
                else if (index == selected)
                {
                    Theme.FillRound(g, row, 8, liq ? Color.FromArgb(120, 255, 255, 255) : Theme.Alpha(Theme.Accent, 18));
                }
                Color color = index == selected ? Theme.Accent : Theme.Text;
                TextRenderer.DrawText(g, items[index], font,
                    new Rectangle(row.X + 10, row.Y, Math.Max(10, row.Width - 16), row.Height), color,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }

            using (Pen border = new Pen(liq ? Color.FromArgb(160, 255, 255, 255) : Theme.Border, 1f))
            {
                using (GraphicsPath path = Theme.Round(new Rectangle(0, 0, Math.Max(2, Width - 1), Math.Max(2, Height - 1)), 12))
                {
                    g.DrawPath(border, path);
                }
            }
        }
    }

    /// <summary>
    /// 主题预览卡：设置页里让用户直接看到两套皮肤长什么样，而不是读一行「液态玻璃」的名词。
    /// 缩略图不是截图，是按缩略比例重画的 —— 条纹、胶囊、白卡都在，区别一眼可见。
    /// </summary>
    public class ThemePreview : Control
    {
        /// <summary>0 = 纯色，1 = 液态玻璃，2 = 千禧年（与 Theme.Skin 的枚举值一致）</summary>
        public int Kind = 1;
        public bool Selected = false;
        private bool hover;

        public string KindName()
        {
            if (Kind == 1) return "玻璃";
            if (Kind == 2) return "千禧年";
            return "纯色";
        }

        /// <summary>缩略图里的一颗玻璃泡泡（千禧年专用）：内部发白 + 亮边 + 左上高光</summary>
        private void Bubble(Graphics g, int cx, int cy, int r, int alpha)
        {
            if (r < 3) return;
            Rectangle box = new Rectangle(cx - r, cy - r, r * 2, r * 2);
            try
            {
                using (GraphicsPath p = new GraphicsPath())
                {
                    p.AddEllipse(box);
                    using (PathGradientBrush brush = new PathGradientBrush(p))
                    {
                        brush.CenterColor = Color.FromArgb(Math.Min(255, alpha), 255, 255, 255);
                        // SurroundColors 的长度必须和路径点数一致，否则 GDI+ 抛参数异常
                        PointF[] points = p.PathPoints;
                        Color[] surround = new Color[points.Length];
                        for (int i = 0; i < surround.Length; i++) surround[i] = Color.FromArgb(0, 255, 255, 255);
                        brush.SurroundColors = surround;
                        g.FillPath(brush, p);
                    }
                }
                using (Pen edge = new Pen(Color.FromArgb(Math.Min(255, alpha), 255, 255, 255), 1f))
                {
                    g.DrawEllipse(edge, box);
                }
                int hw = Math.Max(2, r * 2 / 5);
                int hh = Math.Max(1, r / 4);
                using (SolidBrush spec = new SolidBrush(Color.FromArgb(Math.Min(255, alpha + 60), 255, 255, 255)))
                {
                    g.FillEllipse(spec, box.X + r - hw / 2, box.Y + r - hh, hw, hh);
                }
            }
            catch
            {
            }
        }

        public ThemePreview()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(212, 84);
            Cursor = Cursors.Hand;
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
            // 预览卡挂在卡片里：圆角外要裁父卡片的玻璃，不能平涂一个实色
            GlassCrop.Paint(this, g);
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            if (rect.Width < 40 || rect.Height < 30) return;

            int radius = 14;
            using (GraphicsPath path = Theme.Round(rect, radius))
            {
                Region old = g.Clip;
                try
                {
                    g.SetClip(path, CombineMode.Intersect);
                    if (Kind == 1)
                    {
                        // 液态玻璃：蓝白 45° 斜条纹
                        using (LinearGradientBrush b = new LinearGradientBrush(rect,
                            Color.FromArgb(250, 253, 255), Color.FromArgb(150, 188, 255), 115f))
                        {
                            g.FillRectangle(b, rect);
                        }
                        using (Bitmap tile = new Bitmap(36, 36))
                        {
                            using (Graphics tg = Graphics.FromImage(tile))
                            {
                                tg.Clear(Color.Transparent);
                                using (SolidBrush sb = new SolidBrush(Color.FromArgb(150, 28, 84, 236)))
                                {
                                    tg.FillRectangle(sb, 0, 0, 36, 18);
                                }
                            }
                            using (TextureBrush tb = new TextureBrush(tile, WrapMode.Tile))
                            {
                                tb.RotateTransform(45f);
                                g.FillRectangle(tb, rect);
                            }
                        }
                        // 白纱只给 86：给到 130 时缩略图会被洗成一片白，
                        // 「蓝白斜条纹」这个唯一的信息就没了。
                        using (LinearGradientBrush wash = new LinearGradientBrush(rect,
                            Color.FromArgb(86, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 60f))
                        {
                            g.FillRectangle(wash, rect);
                        }
                    }
                    else if (Kind == 2)
                    {
                        // 千禧年：湖蓝天光 + 一道斜光带 + 两颗泡泡
                        using (LinearGradientBrush b = new LinearGradientBrush(rect,
                            Color.FromArgb(252, 254, 255), Color.FromArgb(96, 168, 232), 96f))
                        {
                            g.FillRectangle(b, rect);
                        }
                        using (LinearGradientBrush beam = new LinearGradientBrush(rect,
                            Color.FromArgb(0, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 20f))
                        {
                            ColorBlend blend = new ColorBlend(3);
                            blend.Colors = new Color[]
                            {
                                Color.FromArgb(0, 255, 255, 255),
                                Color.FromArgb(120, 255, 255, 255),
                                Color.FromArgb(0, 255, 255, 255)
                            };
                            blend.Positions = new float[] { 0.18f, 0.44f, 0.72f };
                            try
                            {
                                beam.InterpolationColors = blend;
                            }
                            catch
                            {
                            }
                            g.FillRectangle(beam, rect);
                        }
                        Bubble(g, rect.Width - 34, 22, 17, 150);
                        Bubble(g, rect.Width - 58, 44, 9, 110);
                    }
                    else
                    {
                        // 纯色：一张平涂的浅灰底，什么都没有
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(243, 245, 247)))
                        {
                            g.FillRectangle(b, rect);
                        }
                    }

                    // 缩略的「控件」：三种皮肤的按钮/卡片形态各不一样
                    int mockH = 32;
                    Rectangle mock = new Rectangle(18, 12, Math.Max(40, rect.Width - 36), mockH);
                    if (Kind == 1)
                    {
                        Theme.FillRound(g, mock, mockH / 2, Color.FromArgb(196, 255, 255, 255));
                        // 胶囊里那条偏左上的反光带：参考图的「体积感」一半来自它
                        Theme.FillRound(g, new Rectangle(mock.X + 12, mock.Y + 6, Math.Max(20, mock.Width / 2), 8), 4,
                            Color.FromArgb(130, 255, 255, 255));
                        // 内白环和外暗线必须错开 1px。画在同一条路径上时，
                        // 后画的暗线会把刚画的白环整圈盖掉，边缘就只剩一条灰线 ——
                        // 缩略图里胶囊「看不见」就是这么来的。
                        Rectangle outline = new Rectangle(mock.X - 1, mock.Y - 1, mock.Width + 2, mock.Height + 2);
                        Theme.DrawRound(g, outline, mockH / 2 + 1, Color.FromArgb(54, 26, 62, 140), 1f);
                        Theme.DrawRound(g, mock, mockH / 2, Color.FromArgb(248, 255, 255, 255), 1.4f);
                    }
                    else if (Kind == 2)
                    {
                        // 千禧年：一颗 Aqua 按钮 —— 蓝色本体 + 上半个清漆 + 一条硬分界。
                        // 顺序不能反：先铺白再叠蓝，得到的是一颗「白按钮戴蓝帽子」；
                        // 必须蓝底在上、清漆压顶，才是那个年代的按钮。
                        int half = Math.Max(2, mockH * 46 / 100);
                        using (GraphicsPath mockPath = Theme.Round(mock, 9))
                        {
                            Region mockOld = g.Clip;
                            try
                            {
                                g.SetClip(mockPath, CombineMode.Intersect);
                                using (LinearGradientBrush baseBrush = new LinearGradientBrush(
                                    new Rectangle(mock.X, mock.Y, Math.Max(1, mock.Width), Math.Max(2, mockH)),
                                    Color.FromArgb(255, 122, 186, 240), Color.FromArgb(255, 52, 130, 208),
                                    LinearGradientMode.Vertical))
                                {
                                    g.FillRectangle(baseBrush, mock);
                                }
                                using (LinearGradientBrush gloss = new LinearGradientBrush(
                                    new Rectangle(mock.X, mock.Y, Math.Max(1, mock.Width), half),
                                    Color.FromArgb(168, 255, 255, 255), Color.FromArgb(26, 255, 255, 255),
                                    LinearGradientMode.Vertical))
                                {
                                    g.FillRectangle(gloss, new Rectangle(mock.X, mock.Y, Math.Max(1, mock.Width), half));
                                }
                                using (SolidBrush edge = new SolidBrush(Color.FromArgb(200, 255, 255, 255)))
                                {
                                    g.FillRectangle(edge, mock.X, mock.Y + half, Math.Max(1, mock.Width), 1);
                                }
                            }
                            finally
                            {
                                g.Clip = mockOld;
                                if (mockOld != null) mockOld.Dispose();
                            }
                        }
                        Theme.DrawRound(g, mock, 9, Color.FromArgb(150, 18, 56, 104), 1f);
                    }
                    else
                    {
                        // 纯色：白卡 + 1px 细边 + 左侧主色竖条
                        Theme.FillRound(g, mock, 8, Color.FromArgb(255, 255, 255));
                        Theme.FillRound(g, new Rectangle(mock.X, mock.Y + 8, 3, 16), 2, Theme.Accent);
                        Theme.DrawRound(g, mock, 8, Color.FromArgb(222, 228, 234), 1f);
                        // 示意「标题 + 副标题」两行，而不是一根 7px 高的黑条 ——
                        // 黑条在缩略图里会被读成一块纯黑胶囊，和真实卡片完全不像。
                        Theme.FillRound(g, new Rectangle(mock.X + 14, mock.Y + 8, Math.Max(20, mock.Width - 44), 6), 3,
                            Theme.Alpha(Theme.Text, 190));
                        Theme.FillRound(g, new Rectangle(mock.X + 14, mock.Y + 19, Math.Max(14, (mock.Width - 44) * 5 / 8), 5), 2,
                            Theme.Alpha(Theme.SubText, 120));
                    }

                    if (hover)
                    {
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(18, 255, 255, 255)))
                        {
                            g.FillRectangle(b, rect);
                        }
                    }

                    // 底部说明条：先压一层白，再写深色字。
                    // 直接在白条纹上写白字（曾经的写法）等于没写 —— 缩略图本身的对比度
                    // 是随机的，说明文字必须有自己的一层底。
                    Rectangle capBar = new Rectangle(rect.X, rect.Bottom - 28, rect.Width, 28);
                    using (LinearGradientBrush b = new LinearGradientBrush(capBar,
                        Color.FromArgb(60, 255, 255, 255), Color.FromArgb(232, 255, 255, 255), LinearGradientMode.Vertical))
                    {
                        g.FillRectangle(b, capBar);
                    }
                    using (Font f = Theme.MakeFont(9f, FontStyle.Bold))
                    {
                        TextRenderer.DrawText(g, KindName(), f,
                            new Rectangle(16, rect.Bottom - 26, Math.Max(20, rect.Width - 56), 22), Theme.Text,
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                    }
                }
                finally
                {
                    g.Clip = old;
                    if (old != null) old.Dispose();
                }
            }

            // 选中环 + 勾
            if (Selected)
            {
                // 选中环也用主色。白色描边在缩略图那种浅底上等于没画（环和背景同色）。
                Theme.DrawRound(g, rect, radius, Theme.Accent, 2f);
                // 勾挂在右上角，不能挂在右下角 —— 底部 28px 是说明条，
                // 勾压上去会和「液态玻璃」四个字撞在一起（缩略图 98px 高，没有富余空间）。
                // 勾用主色实心圆 + 白色对勾。之前是「白色圆 + 主色勾」，
                // 而缩略图里本来就全是白胶囊白卡，白底白勾等于没有边界；
                // 主色圆在任何底色上都能一眼分出来，也不需要额外的描边。
                int cx = rect.Width - 20;
                int cy = 20;
                using (SolidBrush b = new SolidBrush(Theme.Accent))
                {
                    g.FillEllipse(b, cx - 8, cy - 8, 16, 16);
                }
                using (Pen check = new Pen(Color.White, 1.8f))
                {
                    check.StartCap = LineCap.Round;
                    check.EndCap = LineCap.Round;
                    g.DrawLines(check, new Point[]
                    {
                        new Point(cx - 4, cy),
                        new Point(cx - 1, cy + 3),
                        new Point(cx + 4, cy - 4)
                    });
                }
            }
            else
            {
                // 未选中：玻璃皮肤描一圈很淡的白，纯色皮肤描中性细边
                Theme.DrawRound(g, rect, radius,
                    Kind == 1 ? Color.FromArgb(90, 255, 255, 255) : Color.FromArgb(190, 206, 218, 224), 1f);
            }
        }
    }

    public class LgList : ListBox
    {
        public delegate void RowPaintHandler(object item, Graphics g, Rectangle rect, bool selected, bool hover, int index);

        public RowPaintHandler RowPaint;
        private int hoverIndex = -1;

        public LgList()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            DrawMode = DrawMode.OwnerDrawFixed;
            BorderStyle = BorderStyle.None;
            // 液态玻璃下，列表的空余区域会用这个底色补，取接近卡片玻璃的值才不突兀
            BackColor = (Theme.LiquidMode || Theme.MillenniumMode) ? Theme.GlassCard : Theme.Card;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            ItemHeight = 64;
            IntegralHeight = false;
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            Graphics g = e.Graphics;
            Theme.EnableQuality(g);
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            if (Theme.LiquidMode || Theme.MillenniumMode)
            {
                // 行底改成「裁父卡片的那一块玻璃」：整列看起来是一片连续的玻璃，
                // 而不是一行行白条。ListBox 本身不支持透明，只能逐行取样。
                GlassCrop.PaintRect(this, g, e.Bounds);
            }
            else
            {
                using (SolidBrush bg = new SolidBrush(Theme.Card))
                {
                    g.FillRectangle(bg, e.Bounds);
                }
            }
            if (RowPaint != null && e.Index < Items.Count)
            {
                Rectangle rect = new Rectangle(e.Bounds.X + 3, e.Bounds.Y + 2, e.Bounds.Width - 6, e.Bounds.Height - 4);
                RowPaint(Items[e.Index], g, rect, selected, e.Index == hoverIndex, e.Index);
            }
            // 行分隔线画在内容之后，才不会被悬停底色盖住。
            // 只有一条极淡的线，作用是给长列表建立节奏；最后一行不画，避免和卡片底边叠成双线。
            if (e.Index < Items.Count - 1)
            {
                using (SolidBrush line = new SolidBrush(Color.FromArgb(110, Theme.Border)))
                {
                    g.FillRectangle(line, e.Bounds.X + 18, e.Bounds.Bottom - 1, Math.Max(1, e.Bounds.Width - 36), 1);
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int index = IndexFromPoint(e.Location);
            if (index != hoverIndex)
            {
                hoverIndex = index;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hoverIndex = -1;
            Invalidate();
        }
    }
}
