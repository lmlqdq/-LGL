using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Liuguang.Core;

namespace Liuguang.UI
{
    public abstract class PageBase : Panel
    {
        protected MainForm Host;

        protected PageBase(MainForm host)
        {
            Host = host;
            BackColor = Theme.Bg;
            Dock = DockStyle.Fill;
            // 页面要自己铺壁纸（液态玻璃的光斑底），所以必须开 UserPaint，
            // 并在 OnPaint 里真正画出背景，否则切页会残留上一帧。
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
            DoubleBuffered = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.PaintBackdrop(this, e.Graphics, ClientRectangle);
            // 顶部一团极淡的主色光晕：整块留白完全平涂会显得空，这是「空」和「干净」的分界
            Theme.PaintPageGlow(e.Graphics, ClientRectangle);
            base.OnPaint(e);
        }

        public virtual void Reload()
        {
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (ClientSize.Width > 0 && ClientSize.Height > 0) LayoutPage();
        }

        protected virtual void LayoutPage()
        {
        }

        /// <summary>
        /// 页面上的所有文字都走这里。返回 LgLabel：它自己裁父卡片那块玻璃当背景，
        /// 所以不会在玻璃上压出一块纯白方块（bg 参数只是保留调用点兼容，已不使用）。
        /// </summary>
        protected Label MakeLabel(string text, Font font, Color color, Color bg)
        {
            LgLabel label = new LgLabel();
            label.Text = text;
            label.Font = font;
            label.ForeColor = color;
            label.AutoSize = false;
            label.TextAlign = ContentAlignment.MiddleLeft;
            return label;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            // 页面铺满内容区，窗口右/下边的可拖拽边缘就在这里，不接管的话拉不动
            if (WindowDrag.TryStart(this, e, false)) return;
            base.OnMouseDown(e);
        }

        protected static string Clip(string text, int max)
        {
            if (text == null) return "";
            if (text.Length <= max) return text;
            return text.Substring(0, max) + "…";
        }

        protected void Tip(string message)
        {
            Host.Notify(message);
        }
    }

    public class HomePage : PageBase
    {
        private readonly LgCard hero;
        private readonly LgCard logCard;
        private readonly LgCombo versionCombo;
        private readonly LgButton launchButton;
        private readonly LgTextBox logBox;
        /// <summary>日志为空时的占位提示。有日志就隐藏。</summary>
        private readonly LgLabel logPlaceholder;
        private readonly Label welcome;
        private readonly Label subLabel;
        private readonly Label instanceLabel;
        private readonly Label javaLabel;
        private List<InstalledVersion> versions = new List<InstalledVersion>();
        private bool javaScanned;
        private readonly StringBuilder logBuffer = new StringBuilder();
        private readonly System.Windows.Forms.Timer logFlushTimer;

        public HomePage(MainForm host) : base(host)
        {
            hero = new LgCard();
            hero.Fill = Theme.GlassCard;
            Controls.Add(hero);

            welcome = MakeLabel("欢迎回来", Theme.H1, Theme.Text, hero.Fill);
            hero.Controls.Add(welcome);

            subLabel = MakeLabel("离线模式 · 存档与模组均保存在本机", Theme.Small, Theme.SubText, hero.Fill);
            hero.Controls.Add(subLabel);

            instanceLabel = MakeLabel("当前实例", Theme.Small, Theme.SubText, hero.Fill);
            hero.Controls.Add(instanceLabel);

            versionCombo = new LgCombo();
            hero.Controls.Add(versionCombo);

            launchButton = new LgButton("启 动 游 戏");
            launchButton.Font = new Font("Microsoft YaHei", 12f, FontStyle.Bold);
            launchButton.Click += new EventHandler(OnLaunchClick);
            hero.Controls.Add(launchButton);

            javaLabel = MakeLabel("正在检测 Java…", Theme.Small, Theme.SubText, hero.Fill);
            hero.Controls.Add(javaLabel);

            logCard = new LgCard();
            logCard.Fill = Theme.GlassCard;
            // 日志卡整块做成深色玻璃板：白色 TextBox 是这块界面上最廉价的东西，
            // 换成深色之后它同时解决了「白底刺眼」和「系统滚动条突兀」两个问题。
            logCard.Dark = true;
            Controls.Add(logCard);

            Label logTitle = MakeLabel("运行日志", Theme.H3, Theme.LogText, logCard.Fill);
            logTitle.Name = "logTitle";
            logCard.Controls.Add(logTitle);

            logBox = new LgTextBox();
            logBox.LogStyle = true;
            logBox.Multiline = true;
            logBox.ReadOnly = true;
            // 不要系统滚动条：它是原生控件画的，颜色改不了，
            // 在深色板上就是一条刺眼的白条。日志自动跟到底部，回看用滚轮。
            logBox.ScrollBars = ScrollBars.None;
            logBox.BorderStyle = BorderStyle.None;
            logBox.ApplySkin();
            logBox.ForeColor = Theme.LogText;
            logBox.Font = Theme.Mono;
            // 折行：去掉滚动条之后，长命令行的后半段会被右边直接裁掉，
            // 又没法横向滚动，等于看不见。日志宁可多占一行也不能丢内容。
            logBox.WordWrap = true;
            logCard.Controls.Add(logBox);

            // 空的日志区是一块纯黑的洞，占屏面积还很大。
            // 一句占位提示同时解决「这里是什么」和「为什么是空的」两个问题。
            logPlaceholder = new LgLabel();
            logPlaceholder.Text = "启动游戏后，运行日志会显示在这里";
            logPlaceholder.Font = Theme.Small;
            logPlaceholder.ForeColor = Theme.LogTextDim;
            logPlaceholder.TextAlign = ContentAlignment.MiddleCenter;
            logCard.Controls.Add(logPlaceholder);
            // TextBox 是不透明的，占位块必须压在它上面才看得见
            logPlaceholder.BringToFront();

            // 日志按 200ms 批量刷入文本框。游戏启动时会喷出上千行 stdout，
            // 逐行 AppendText 会让界面卡到点不动。
            logFlushTimer = new System.Windows.Forms.Timer();
            logFlushTimer.Interval = 200;
            logFlushTimer.Tick += new EventHandler(delegate(object s, EventArgs e) { FlushLog(); });
            logFlushTimer.Start();
        }

        protected override void LayoutPage()
        {
            int w = ClientSize.Width;
            int h = ClientSize.Height;
            if (w <= 0 || h <= 0) return;

            // 全站统一外边距：卡片不贴窗口边缘。
            // 原来 hero 是 SetBounds(0,0,w,...)，圆角被窗口裁掉、左边紧贴侧栏，
            // 这是界面「廉价感」最直接的来源。
            int pad = Theme.Pad;
            int innerW = w - pad * 2;
            if (innerW < 320)
            {
                pad = 12;
                innerW = w - 24;
            }

            // 196 -> 172：原来卡片里从「欢迎」到「已检测到 Java」之间有几处空档，
            // 卡片高度又是按最大内容撑的，视觉上就是「上下都虚」。
            // 收紧到 172 之后，标题、下拉、底部说明三段间距接近均匀。
            int heroHeight = 172;
            hero.SetBounds(pad, 20, innerW, heroHeight);

            int textW = Math.Max(160, innerW - 240);
            welcome.SetBounds(28, 20, textW, 34);
            subLabel.SetBounds(30, 54, textW, 18);
            instanceLabel.SetBounds(30, 86, 200, 18);
            versionCombo.SetBounds(30, 104, Math.Min(320, Math.Max(160, innerW - 260)), 32);
            // 按钮在整卡垂直居中：172 高、52 高，y=(172-52)/2=60
            launchButton.SetBounds(innerW - 196, 60, 172, 52);
            javaLabel.SetBounds(30, 146, textW, 18);

            int logTop = 20 + heroHeight + Theme.Gap;
            int logHeight = Math.Max(120, h - logTop - pad);
            logCard.SetBounds(pad, logTop, innerW, logHeight);

            Label logTitle = logCard.Controls["logTitle"] as Label;
            if (logTitle != null) logTitle.SetBounds(24, 16, 200, 22);
            int boxW = logCard.Width - 48;
            int boxH = logCard.Height - 80;
            if (boxW < 40) boxW = 40;
            if (boxH < 40) boxH = 40;
            // 和卡片同色，所以这圈留白就是「内边距」，不是缝
            logBox.SetBounds(24, 56, boxW, boxH);
            // 占位要落在整块深色板的垂直中心，不是日志区的顶部 ——
            // 贴在顶上会读成「一行提示」，居中也才像「这里暂时是空的」。
            if (logPlaceholder != null)
            {
                int ph = Math.Min(40, boxH);
                logPlaceholder.SetBounds(24, 56 + (boxH - ph) / 2, boxW, ph);
            }
        }

        public override void Reload()
        {
            RefreshVersions();
            if (!javaScanned)
            {
                javaScanned = true;
                System.Threading.Thread thread = new System.Threading.Thread(delegate()
                {
                    // 必须包 try/catch：后台线程里未捕获的异常会直接终止整个进程，
                    // 用户看到的现象就是「启动器自己消失了」。
                    List<JavaInfo> list = new List<JavaInfo>();
                    try
                    {
                        list = JavaFinder.Scan(false);
                    }
                    catch (Exception ex)
                    {
                        Log.Write("Java 探测失败：" + ex.Message);
                    }
                    Host.Ui(delegate
                    {
                        if (list.Count == 0)
                        {
                            javaLabel.Text = "未检测到 Java，启动游戏时会自动下载对应版本（约 40-60 MB，只需一次）";
                            javaLabel.ForeColor = Theme.Warn;
                        }
                        else
                        {
                            StringBuilder sb = new StringBuilder();
                            sb.Append("已检测到 ").Append(list.Count).Append(" 个 Java：");
                            for (int i = 0; i < list.Count && i < 3; i++)
                            {
                                if (i > 0) sb.Append(" / ");
                                sb.Append("Java ").Append(list[i].Major);
                            }
                            javaLabel.Text = sb.ToString();
                            javaLabel.ForeColor = Theme.SubText;
                        }
                    });
                });
                thread.IsBackground = true;
                thread.Start();
            }
        }

        private void RefreshVersions()
        {
            versions = VersionList.Installed(Host.Cfg);
            versionCombo.Items.Clear();
            for (int i = 0; i < versions.Count; i++)
            {
                InstalledVersion iv = versions[i];
                string name = iv.McVersion;
                if (iv.Loader != "原版") name += "  ·  " + iv.Loader;
                if (iv.ModCount > 0) name += "  (" + iv.ModCount + " 模组)";
                versionCombo.Items.Add(name);
            }

            if (versions.Count == 0)
            {
                welcome.Text = "还没有安装游戏版本";
                subLabel.Text = "前往「下载」页安装原版或 Fabric，之后就能在这里启动";
                launchButton.Enabled = false;
                javaLabel.Text = "提示：安装版本需要联网，可在设置中切换国内镜像";
                return;
            }

            launchButton.Enabled = true;
            welcome.Text = "欢迎回来，" + Host.Cfg.PlayerName;
            subLabel.Text = "离线登录 · 版本隔离已" + (Host.Cfg.VersionIsolation ? "开启" : "关闭");

            int selected = 0;
            for (int i = 0; i < versions.Count; i++)
            {
                if (versions[i].Id == Host.Cfg.LastVersion)
                {
                    selected = i;
                    break;
                }
            }
            versionCombo.SelectedIndex = selected;
        }

        private void OnLaunchClick(object sender, EventArgs e)
        {
            int index = versionCombo.SelectedIndex;
            if (index < 0 || index >= versions.Count)
            {
                Tip("请先选择一个游戏版本");
                return;
            }
            Host.LaunchVersion(versions[index].Id);
        }

        public void AppendLog(string line)
        {
            // 这里可能来自后台线程（游戏 stdout），只写缓冲区，不碰控件。
            lock (logBuffer)
            {
                logBuffer.Append(line).Append("\r\n");
                if (logBuffer.Length > 400000) logBuffer.Length = 0;
            }
        }

        private void FlushLog()
        {
            string text;
            lock (logBuffer)
            {
                if (logBuffer.Length == 0) return;
                text = logBuffer.ToString();
                logBuffer.Length = 0;
            }
            if (logBox == null || logBox.IsDisposed) return;
            try
            {
                if (logBox.TextLength > 200000) logBox.Text = "";
                if (logPlaceholder != null && !logPlaceholder.IsDisposed) logPlaceholder.Visible = false;
                logBox.AppendText(text);
                logBox.SelectionStart = logBox.TextLength;
                logBox.ScrollToCaret();
            }
            catch
            {
            }
        }
    }

    public class VersionsPage : PageBase
    {
        private readonly LgCard listCard;
        private readonly LgList list;
        private readonly LgCard detail;
        private readonly Label titleLabel;
        private readonly Label infoLabel;
        private readonly Label pathLabel;
        private readonly LgButton launchBtn;
        private readonly LgButton folderBtn;
        private readonly LgButton deleteBtn;
        private readonly LgButton refreshBtn;
        private List<InstalledVersion> versions = new List<InstalledVersion>();
        private InstalledVersion selected;

        public VersionsPage(MainForm host) : base(host)
        {
            titleLabel = MakeLabel("版本管理", Theme.H1, Theme.Text, Theme.Bg);
            Controls.Add(titleLabel);

            refreshBtn = new LgButton("刷新列表");
            refreshBtn.Ghost = true;
            refreshBtn.Font = Theme.Small;
            refreshBtn.Click += new EventHandler(delegate(object s, EventArgs e) { Reload(); });
            Controls.Add(refreshBtn);

            // 列表放进卡片里：LgList 是矩形控件，直接放在玻璃页面上会是一块生硬的白方块，
            // 边缘还会切掉页面的光斑。套一层卡片后，圆角与描边由卡片统一提供。
            listCard = new LgCard();
            Controls.Add(listCard);

            list = new LgList();
            list.RowPaint = new LgList.RowPaintHandler(PaintRow);
            list.SelectedIndexChanged += new EventHandler(OnSelect);
            listCard.Controls.Add(list);

            detail = new LgCard();
            detail.Fill = Theme.GlassCard;
            Controls.Add(detail);

            Label detailTitle = MakeLabel("实例详情", Theme.H3, Theme.Text, detail.Fill);
            detailTitle.Name = "detailTitle";
            detail.Controls.Add(detailTitle);

            infoLabel = MakeLabel("在左侧选择一个实例", Theme.Body, Theme.SubText, detail.Fill);
            infoLabel.Name = "info";
            // 多行说明必须顶端对齐：MakeLabel 默认 MiddleLeft，多行文本会被垂直居中、
            // 行距按控件高度摊开，看着像散架的表格。
            infoLabel.TextAlign = ContentAlignment.TopLeft;
            detail.Controls.Add(infoLabel);

            pathLabel = MakeLabel("", Theme.Small, Theme.SubText, detail.Fill);
            pathLabel.Name = "path";
            pathLabel.TextAlign = ContentAlignment.TopLeft;
            detail.Controls.Add(pathLabel);

            launchBtn = new LgButton("启动");
            launchBtn.Click += new EventHandler(delegate(object s, EventArgs e)
            {
                if (selected != null) Host.LaunchVersion(selected.Id);
            });
            detail.Controls.Add(launchBtn);

            folderBtn = new LgButton("打开目录");
            folderBtn.Ghost = true;
            folderBtn.Click += new EventHandler(delegate(object s, EventArgs e)
            {
                if (selected != null) Util.RevealInExplorer(Paths.VersionDir(selected.Id));
            });
            detail.Controls.Add(folderBtn);

            deleteBtn = new LgButton("删除实例");
            deleteBtn.Ghost = true;
            deleteBtn.FollowTheme = false;
            deleteBtn.ColorA = Theme.Bad;
            deleteBtn.ColorB = Theme.Bad;
            deleteBtn.Click += new EventHandler(OnDeleteClick);
            detail.Controls.Add(deleteBtn);
        }

        protected override void LayoutPage()
        {
            int w = ClientSize.Width;
            int h = ClientSize.Height;
            if (w <= 0 || h <= 0) return;

            int pad = Theme.Pad;
            int innerW = w - pad * 2;
            if (innerW < 320)
            {
                pad = 12;
                innerW = w - 24;
            }

            titleLabel.SetBounds(pad, 14, 400, 36);
            refreshBtn.SetBounds(pad + innerW - 116, 18, 116, 34);

            int top = 72;
            int bottom = Math.Max(top + 120, h - pad);
            int listW = (int)((innerW - Theme.Gap) * 0.42f);
            if (listW < 280) listW = Math.Min(280, innerW / 2);

            listCard.SetBounds(pad, top, listW, bottom - top);
            int cw = Math.Max(40, listCard.Width - 20);
            int ch = Math.Max(40, listCard.Height - 24);
            list.SetBounds(10, 12, cw, ch);

            int detailX = pad + listW + Theme.Gap;
            int detailW = Math.Max(240, innerW - listW - Theme.Gap);
            detail.SetBounds(detailX, top, detailW, bottom - top);

            Label detailTitle = detail.Controls["detailTitle"] as Label;
            if (detailTitle != null) detailTitle.SetBounds(26, 20, 200, 22);
            Label info = detail.Controls["info"] as Label;
            if (info != null) info.SetBounds(26, 56, Math.Max(80, detail.Width - 52), 136);
            Label path = detail.Controls["path"] as Label;
            if (path != null) path.SetBounds(26, 200, Math.Max(80, detail.Width - 52), 36);

            int buttonTop = Math.Max(240, detail.Height - 58);
            launchBtn.SetBounds(26, buttonTop, 104, 38);
            folderBtn.SetBounds(140, buttonTop, 104, 38);
            deleteBtn.SetBounds(254, buttonTop, 104, 38);
        }

        public override void Reload()
        {
            versions = VersionList.Installed(Host.Cfg);
            list.Items.Clear();
            for (int i = 0; i < versions.Count; i++) list.Items.Add(versions[i]);
            if (versions.Count > 0 && list.SelectedIndex < 0) list.SelectedIndex = 0;
            if (versions.Count == 0)
            {
                infoLabel.Text = "还没有实例。\r\n\r\n前往「下载」页安装原版或 Fabric 版本，安装完成后会自动出现在这里。";
                pathLabel.Text = "";
            }
        }

        private void PaintRow(object item, Graphics g, Rectangle rect, bool selectedItem, bool hover, int index)
        {
            InstalledVersion iv = item as InstalledVersion;
            if (iv == null) return;
            Theme.RowSelection(g, rect, Theme.RadiusRow, selectedItem, hover);

            string title = iv.McVersion;
            TextRenderer.DrawText(g, title, Theme.RowTitle, new Rectangle(rect.X + 22, rect.Y + 10, rect.Width - 100, 22), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

            string sub = iv.Id;
            TextRenderer.DrawText(g, sub, Theme.Small, new Rectangle(rect.X + 22, rect.Y + 34, rect.Width - 34, 20), Theme.SubText,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

            string badge = iv.Loader;
            Color badgeColor = iv.Loader == "原版" ? Theme.SubText : Theme.Accent;
            TextRenderer.DrawText(g, badge, Theme.Tiny, new Rectangle(rect.Right - 84, rect.Y + 12, 70, 18), badgeColor,
                TextFormatFlags.Right | TextFormatFlags.EndEllipsis);
        }

        private void OnSelect(object sender, EventArgs e)
        {
            selected = list.SelectedItem as InstalledVersion;
            if (selected == null)
            {
                infoLabel.Text = "在左侧选择一个实例";
                pathLabel.Text = "";
                return;
            }
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("实例 ID：" + selected.Id);
            sb.AppendLine("原版版本：" + selected.McVersion);
            sb.AppendLine("加载器：" + selected.Loader);
            sb.AppendLine("模组数量：" + selected.ModCount);
            sb.AppendLine("游戏目录：" + Paths.GameDir(selected.Id, Host.Cfg.VersionIsolation));
            infoLabel.Text = sb.ToString();
            pathLabel.Text = "客户端目录：" + Paths.VersionDir(selected.Id);
        }

        private void OnDeleteClick(object sender, EventArgs e)
        {
            if (selected == null) return;
            DialogResult result = MessageBox.Show(this,
                "确定删除实例「" + selected.Id + "」吗？\r\n该实例目录下的存档与模组会一并删除，且无法恢复。",
                "删除实例", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
            if (result != DialogResult.OK) return;
            try
            {
                Directory.Delete(Paths.VersionDir(selected.Id), true);
                Log.Write("已删除实例：" + selected.Id);
                Reload();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "删除失败：" + ex.Message, "流光启动器", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    public class DownloadPage : PageBase
    {
        private readonly LgCard listCard;
        private readonly LgList list;
        private readonly LgCombo typeCombo;
        private readonly LgInput searchInput;
        private readonly LgButton searchBtn;
        private readonly LgButton installVanillaBtn;
        private readonly LgButton installFabricBtn;
        private readonly LgButton installForgeBtn;
        private List<VersionEntry> shown = new List<VersionEntry>();
        private bool loading;

        public DownloadPage(MainForm host) : base(host)
        {
            Label title = MakeLabel("下载中心", Theme.H1, Theme.Text, Theme.Bg);
            title.Name = "title";
            Controls.Add(title);

            typeCombo = new LgCombo();
            typeCombo.Items.Add("正式版");
            typeCombo.Items.Add("快照");
            typeCombo.Items.Add("全部");
            typeCombo.SelectedIndex = 0;
            typeCombo.SelectedIndexChanged += new EventHandler(delegate(object s, EventArgs e) { Reload(); });
            Controls.Add(typeCombo);

            searchInput = new LgInput();
            searchInput.Inner.KeyDown += new KeyEventHandler(delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) FillList();
            });
            Controls.Add(searchInput);

            searchBtn = new LgButton("搜索");
            searchBtn.Ghost = true;
            searchBtn.Click += new EventHandler(delegate(object s, EventArgs e) { FillList(); });
            Controls.Add(searchBtn);

            listCard = new LgCard();
            Controls.Add(listCard);

            list = new LgList();
            list.RowPaint = new LgList.RowPaintHandler(PaintRow);
            list.DoubleClick += new EventHandler(delegate(object s, EventArgs e) { InstallVanilla(); });
            listCard.Controls.Add(list);

            installVanillaBtn = new LgButton("安装原版");
            installVanillaBtn.Click += new EventHandler(delegate(object s, EventArgs e) { InstallVanilla(); });
            Controls.Add(installVanillaBtn);

            installFabricBtn = new LgButton("安装 Fabric");
            installFabricBtn.Ghost = true;
            installFabricBtn.Click += new EventHandler(delegate(object s, EventArgs e) { InstallFabric(); });
            Controls.Add(installFabricBtn);

            installForgeBtn = new LgButton("安装 Forge");
            installForgeBtn.Ghost = true;
            installForgeBtn.Click += new EventHandler(delegate(object s, EventArgs e) { InstallForge(); });
            Controls.Add(installForgeBtn);
        }

        protected override void LayoutPage()
        {
            int w = ClientSize.Width;
            int h = ClientSize.Height;
            if (w <= 0 || h <= 0) return;

            int pad = Theme.Pad;
            int innerW = w - pad * 2;
            if (innerW < 320)
            {
                pad = 12;
                innerW = w - 24;
            }

            Label title = Controls["title"] as Label;
            if (title != null) title.SetBounds(pad, 14, 300, 36);

            typeCombo.SetBounds(pad, 70, 110, 34);
            int searchLeft = pad + 122;
            int searchW = Math.Max(150, pad + innerW - searchLeft - 100);
            searchInput.SetBounds(searchLeft, 68, searchW, 38);
            searchBtn.SetBounds(searchLeft + searchW + 10, 68, 90, 38);

            int listTop = 124;
            int btnTop = h - pad - 42;
            listCard.SetBounds(pad, listTop, innerW, Math.Max(120, btnTop - 16 - listTop));
            int cw = Math.Max(40, listCard.Width - 20);
            int ch = Math.Max(40, listCard.Height - 24);
            list.SetBounds(10, 12, cw, ch);

            installVanillaBtn.SetBounds(pad, btnTop, 140, 42);
            installFabricBtn.SetBounds(pad + 150, btnTop, 140, 42);
            installForgeBtn.SetBounds(pad + 300, btnTop, 140, 42);
        }

        public override void Reload()
        {
            if (VersionList.All.Count > 0)
            {
                FillList();
                return;
            }
            if (loading) return;
            loading = true;
            Host.RunBackground("获取官方版本清单", delegate
            {
                VersionList.Refresh();
            }, delegate
            {
                loading = false;
                FillList();
            });
        }

        private void FillList()
        {
            if (VersionList.All.Count == 0) return;
            string type = "";
            if (typeCombo.SelectedIndex == 0) type = "release";
            else if (typeCombo.SelectedIndex == 1) type = "snapshot";
            shown = VersionList.Filter(searchInput.Text, type);
            list.BeginUpdate();
            list.Items.Clear();
            for (int i = 0; i < shown.Count; i++) list.Items.Add(shown[i]);
            list.EndUpdate();
            if (shown.Count > 0) list.SelectedIndex = 0;
            Tip("共 " + shown.Count + " 个版本，最新正式版 " + VersionList.LatestRelease);
        }

        private void PaintRow(object item, Graphics g, Rectangle rect, bool selected, bool hover, int index)
        {
            VersionEntry entry = item as VersionEntry;
            if (entry == null) return;
            Theme.RowSelection(g, rect, Theme.RadiusRow, selected, hover);

            TextRenderer.DrawText(g, entry.Id, Theme.RowTitle, new Rectangle(rect.X + 22, rect.Y + 10, rect.Width - 130, 22), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, entry.TypeText + " · " + entry.ReleaseTime.Replace("T", " ").Replace("Z", ""), Theme.Small,
                new Rectangle(rect.X + 22, rect.Y + 34, rect.Width - 34, 20), Theme.SubText,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

            if (entry.Id == VersionList.LatestRelease)
            {
                TextRenderer.DrawText(g, "最新", Theme.Tiny, new Rectangle(rect.Right - 70, rect.Y + 12, 56, 18), Theme.Accent,
                    TextFormatFlags.Right);
            }
        }

        private void InstallVanilla()
        {
            VersionEntry entry = list.SelectedItem as VersionEntry;
            if (entry == null)
            {
                Tip("请先在列表中选择一个版本");
                return;
            }
            string id = entry.Id;
            Host.RunBackground("安装 " + id, delegate
            {
                VersionInstaller.Install(id, Host.Cfg, new ProgressHandler(Host.Report));
            }, delegate
            {
                Tip(id + " 安装完成，可以回主页启动了");
            });
        }

        private void InstallFabric()
        {
            VersionEntry entry = list.SelectedItem as VersionEntry;
            if (entry == null)
            {
                Tip("请先选择一个原版版本作为基础");
                return;
            }
            string baseVersion = entry.Id;
            Host.RunBackground("安装 Fabric（" + baseVersion + "）", delegate
            {
                string id = FabricInstaller.Install(baseVersion, Host.Cfg, new ProgressHandler(Host.Report));
                Host.Cfg.LastVersion = id;
                Host.Cfg.Save();
            }, delegate
            {
                Tip("Fabric 安装完成，可在主页选择实例启动");
            });
        }

        private void InstallForge()
        {
            VersionEntry entry = list.SelectedItem as VersionEntry;
            if (entry == null)
            {
                Tip("请先选择一个原版版本作为基础");
                return;
            }
            string baseVersion = entry.Id;
            Host.RunBackground("安装 Forge（" + baseVersion + "）", delegate
            {
                string id = ForgeInstaller.Install(baseVersion, Host.Cfg, new ProgressHandler(Host.Report));
                Host.Cfg.LastVersion = id;
                Host.Cfg.Save();
            }, delegate
            {
                Tip("Forge 安装完成，可在主页选择实例启动");
            });
        }
    }
}
