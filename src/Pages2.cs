using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Liuguang.Core;

namespace Liuguang.UI
{
    public class ModsPage : PageBase
    {
        private readonly LgCombo instanceCombo;
        private readonly LgCombo typeCombo;
        private readonly LgInput searchInput;
        private readonly LgButton searchBtn;
        private readonly LgCard resultCard;
        private readonly LgList resultList;
        private readonly LgButton installBtn;
        private readonly LgCard installedCard;
        private readonly LgList modList;
        private readonly LgButton toggleBtn;
        private readonly LgButton deleteBtn;
        private readonly LgButton folderBtn;
        private List<InstalledVersion> instances = new List<InstalledVersion>();
        private List<ModProject> results = new List<ModProject>();
        private List<ModFile> mods = new List<ModFile>();

        public ModsPage(MainForm host) : base(host)
        {
            Label title = MakeLabel("资源中心", Theme.H1, Theme.Text, Theme.Bg);
            title.Name = "title";
            Controls.Add(title);

            instanceCombo = new LgCombo();
            Controls.Add(instanceCombo);

            typeCombo = new LgCombo();
            typeCombo.Items.Add("模组");
            typeCombo.Items.Add("资源包");
            typeCombo.Items.Add("光影");
            typeCombo.SelectedIndex = 0;
            typeCombo.SelectedIndexChanged += new EventHandler(delegate(object s, EventArgs e) { RefreshInstalled(); });
            Controls.Add(typeCombo);

            searchInput = new LgInput();
            searchInput.Inner.KeyDown += new KeyEventHandler(delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) DoSearch();
            });
            Controls.Add(searchInput);

            searchBtn = new LgButton("搜索");
            searchBtn.Click += new EventHandler(delegate(object s, EventArgs e) { DoSearch(); });
            Controls.Add(searchBtn);

            resultCard = new LgCard();
            Controls.Add(resultCard);

            resultList = new LgList();
            resultList.RowPaint = new LgList.RowPaintHandler(PaintResultRow);
            resultCard.Controls.Add(resultList);

            installBtn = new LgButton("安装选中资源");
            installBtn.Click += new EventHandler(delegate(object s, EventArgs e) { InstallSelected(); });
            Controls.Add(installBtn);

            installedCard = new LgCard();
            installedCard.Fill = Theme.GlassCard;
            Controls.Add(installedCard);

            Label installedTitle = MakeLabel("已安装", Theme.H3, Theme.Text, installedCard.Fill);
            installedTitle.Name = "installedTitle";
            installedCard.Controls.Add(installedTitle);

            modList = new LgList();
            modList.RowPaint = new LgList.RowPaintHandler(PaintModRow);
            modList.ItemHeight = 54;
            installedCard.Controls.Add(modList);

            toggleBtn = new LgButton("启用/禁用");
            toggleBtn.Ghost = true;
            toggleBtn.Font = Theme.Small;
            toggleBtn.Click += new EventHandler(delegate(object s, EventArgs e) { ToggleSelected(); });
            installedCard.Controls.Add(toggleBtn);

            deleteBtn = new LgButton("删除");
            deleteBtn.Ghost = true;
            deleteBtn.Font = Theme.Small;
            deleteBtn.Click += new EventHandler(delegate(object s, EventArgs e) { DeleteSelected(); });
            installedCard.Controls.Add(deleteBtn);

            folderBtn = new LgButton("打开目录");
            folderBtn.Ghost = true;
            folderBtn.Font = Theme.Small;
            folderBtn.Click += new EventHandler(delegate(object s, EventArgs e)
            {
                string dir = CurrentModsDir();
                Util.EnsureDir(dir);
                Util.RevealInExplorer(dir);
            });
            installedCard.Controls.Add(folderBtn);
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

            instanceCombo.SetBounds(pad, 70, 200, 34);
            typeCombo.SetBounds(pad + 210, 70, 100, 34);
            int searchLeft = pad + 320;
            int searchW = Math.Max(140, pad + innerW - searchLeft - 100);
            searchInput.SetBounds(searchLeft, 68, searchW, 38);
            searchBtn.SetBounds(searchLeft + searchW + 10, 68, 90, 38);

            int top = 124;
            int btnTop = h - pad - 42;
            int leftW = (int)((innerW - Theme.Gap) * 0.56f);
            if (leftW < 320) leftW = Math.Max(240, innerW / 2);

            resultCard.SetBounds(pad, top, leftW, Math.Max(120, btnTop - 16 - top));
            int cw = Math.Max(40, resultCard.Width - 20);
            int ch = Math.Max(40, resultCard.Height - 24);
            resultList.SetBounds(10, 12, cw, ch);

            installBtn.SetBounds(pad, btnTop, 150, 42);

            int cardX = pad + leftW + Theme.Gap;
            int cardW = Math.Max(220, innerW - leftW - Theme.Gap);
            installedCard.SetBounds(cardX, top, cardW, Math.Max(120, h - pad - top));

            Label installedTitle = installedCard.Controls["installedTitle"] as Label;
            if (installedTitle != null) installedTitle.SetBounds(24, 18, 200, 22);

            int listW2 = Math.Max(40, installedCard.Width - 24);
            int listH = Math.Max(40, installedCard.Height - 108);
            modList.SetBounds(12, 48, listW2, listH);

            int buttonTop = Math.Max(60, installedCard.Height - 50);
            toggleBtn.SetBounds(16, buttonTop, 86, 32);
            deleteBtn.SetBounds(110, buttonTop, 62, 32);
            folderBtn.SetBounds(180, buttonTop, 86, 32);
        }

        public override void Reload()
        {
            instances = VersionList.Installed(Host.Cfg);
            instanceCombo.Items.Clear();
            for (int i = 0; i < instances.Count; i++)
            {
                InstalledVersion iv = instances[i];
                instanceCombo.Items.Add(iv.McVersion + (iv.Loader != "原版" ? " · " + iv.Loader : " · 原版"));
            }
            if (instances.Count > 0)
            {
                int selected = 0;
                for (int i = 0; i < instances.Count; i++)
                {
                    if (instances[i].Id == Host.Cfg.LastVersion) selected = i;
                }
                instanceCombo.SelectedIndex = selected;
            }
            RefreshInstalled();
        }

        private InstalledVersion CurrentInstance()
        {
            int index = instanceCombo.SelectedIndex;
            if (index < 0 || index >= instances.Count) return null;
            return instances[index];
        }

        private string LoaderName()
        {
            InstalledVersion iv = CurrentInstance();
            if (iv == null) return "";
            if (iv.Loader == "Fabric") return "fabric";
            if (iv.Loader == "Quilt") return "quilt";
            if (iv.Loader == "Forge") return "forge";
            if (iv.Loader == "NeoForge") return "neoforge";
            return "";
        }

        private string ProjectType()
        {
            if (typeCombo.SelectedIndex == 1) return "resourcepack";
            if (typeCombo.SelectedIndex == 2) return "shader";
            return "mod";
        }

        private string CurrentModsDir()
        {
            InstalledVersion iv = CurrentInstance();
            if (iv == null) return Paths.ModsDir("", Host.Cfg.VersionIsolation);
            string type = ProjectType();
            string gameDir = Paths.GameDir(iv.Id, Host.Cfg.VersionIsolation);
            if (type == "resourcepack") return Path.Combine(gameDir, "resourcepacks");
            if (type == "shader") return Path.Combine(gameDir, "shaderpacks");
            return Path.Combine(gameDir, "mods");
        }

        private void RefreshInstalled()
        {
            mods = ModManager.Scan(CurrentModsDir());
            modList.BeginUpdate();
            modList.Items.Clear();
            for (int i = 0; i < mods.Count; i++) modList.Items.Add(mods[i]);
            modList.EndUpdate();
            Label installedTitle = installedCard.Controls["installedTitle"] as Label;
            if (installedTitle != null) installedTitle.Text = "已安装（" + mods.Count + "）";
        }

        private void DoSearch()
        {
            InstalledVersion iv = CurrentInstance();
            if (iv == null)
            {
                Tip("请先安装一个游戏版本");
                return;
            }
            string query = searchInput.Text.Trim();
            string type = ProjectType();
            string loader = type == "mod" ? LoaderName() : "";
            // 原版实例也能搜（结果会混入需要加载器的，用户自己判断），
            // 不再硬挡死：之前「原版 → 直接 return」导致用户以为「mod 下不了」。
            string version = iv.McVersion;
            Host.RunBackground("搜索资源：" + (query.Length == 0 ? "热门" : query), delegate
            {
                int total;
                List<ModProject> list = Modrinth.Search(query, version, loader, type, 0, 30, out total);
                Host.Ui(delegate
                {
                    results = list;
                    resultList.BeginUpdate();
                    resultList.Items.Clear();
                    for (int i = 0; i < results.Count; i++) resultList.Items.Add(results[i]);
                    resultList.EndUpdate();
                    if (results.Count == 0)
                    {
                        Tip("没有找到相关结果，换个关键词试试");
                    }
                    else if (list.Count > 0 && !list[0].GameVersions.Contains(version))
                    {
                        // 兜底搜索：按版本没搜到，去掉了版本条件，结果里可能没有当前版本。
                        Tip("当前版本 " + version + " 暂无可用的模组，已显示全部相关结果（共 " + total + " 个）");
                    }
                    else
                    {
                        Tip("找到 " + total + " 个结果，显示前 " + results.Count + " 个");
                    }
                });
            }, null);
        }

        private void PaintResultRow(object item, Graphics g, Rectangle rect, bool selected, bool hover, int index)
        {
            ModProject project = item as ModProject;
            if (project == null) return;
            Theme.RowSelection(g, rect, Theme.RadiusRow, selected, hover);

            TextRenderer.DrawText(g, project.Title, Theme.RowTitle, new Rectangle(rect.X + 22, rect.Y + 9, rect.Width - 140, 22), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, Clip(project.Description, 48), Theme.Small, new Rectangle(rect.X + 22, rect.Y + 33, rect.Width - 140, 20),
                Theme.SubText, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, Util.FormatCount(project.Downloads), Theme.Tiny, new Rectangle(rect.Right - 100, rect.Y + 12, 86, 18),
                Theme.Accent, TextFormatFlags.Right);
        }

        private void PaintModRow(object item, Graphics g, Rectangle rect, bool selected, bool hover, int index)
        {
            ModFile mod = item as ModFile;
            if (mod == null) return;
            Theme.RowSelection(g, rect, 8, selected, hover);

            Color color = mod.Enabled ? Theme.Text : Theme.SubText;
            string title = mod.Title;
            if (mod.Version.Length > 0) title += "  " + mod.Version;
            TextRenderer.DrawText(g, Clip(title, 30), Theme.Body, new Rectangle(rect.X + 18, rect.Y + 7, rect.Width - 30, 20), color,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, Clip(mod.FileName, 34), Theme.Tiny, new Rectangle(rect.X + 18, rect.Y + 28, rect.Width - 30, 16),
                Theme.SubText, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            if (!mod.Enabled)
            {
                TextRenderer.DrawText(g, "已禁用", Theme.Tiny, new Rectangle(rect.Right - 56, rect.Y + 8, 44, 16), Theme.Warn,
                    TextFormatFlags.Right);
            }
        }

        private void InstallSelected()
        {
            ModProject project = resultList.SelectedItem as ModProject;
            if (project == null)
            {
                Tip("请先在搜索结果中选择一个资源");
                return;
            }
            InstalledVersion iv = CurrentInstance();
            if (iv == null)
            {
                Tip("请先安装一个游戏版本");
                return;
            }
            string type = ProjectType();
            string loader = type == "mod" ? LoaderName() : "";
            string dir = CurrentModsDir();
            string version = iv.McVersion;
            Util.EnsureDir(dir);
            Host.RunBackground("安装 " + project.Title, delegate
            {
                ModInstaller.Install(project.Id, version, loader, dir, new ProgressHandler(Host.Report));
            }, delegate
            {
                RefreshInstalled();
                Tip(project.Title + " 安装完成");
            });
        }

        private void ToggleSelected()
        {
            ModFile mod = modList.SelectedItem as ModFile;
            if (mod == null)
            {
                Tip("请先选择一个已安装的文件");
                return;
            }
            if (ModManager.SetEnabled(mod, !mod.Enabled))
            {
                RefreshInstalled();
                Tip(mod.Enabled ? "已启用 " + mod.FileName : "已禁用 " + mod.FileName);
            }
        }

        private void DeleteSelected()
        {
            ModFile mod = modList.SelectedItem as ModFile;
            if (mod == null)
            {
                Tip("请先选择一个已安装的文件");
                return;
            }
            if (MessageBox.Show(this, "确定删除「" + mod.FileName + "」吗？", "删除文件",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            if (ModManager.Delete(mod))
            {
                RefreshInstalled();
                Tip("已删除 " + mod.FileName);
            }
        }
    }

    public class SettingsPage : PageBase
    {
        private readonly LgCard appearanceCard;
        private readonly ThemePreview solidTheme;
        private readonly ThemePreview liquidTheme;
        private readonly ThemePreview millenniumTheme;
        private readonly LgButton bgPickBtn;
        private readonly LgButton bgClearBtn;
        private readonly Label bgHint;
        private readonly LgCard javaCard;
        private readonly LgCard runCard;
        private readonly LgCard netCard;
        private readonly LgButton saveBtn;
        private readonly LgCombo javaCombo;
        private readonly LgButton browseBtn;
        private readonly LgButton rescanBtn;
        private readonly LgButton installJavaBtn;
        private readonly LgButton importJavaBtn;
        private readonly LgCombo memoryCombo;
        private readonly LgInput widthInput;
        private readonly LgInput heightInput;
        private readonly LgInput nameInput;
        private readonly LgInput jvmInput;
        private readonly LgSwitch mirrorSwitch;
        private readonly LgSwitch isolationSwitch;
        private readonly LgSwitch fullscreenSwitch;
        private readonly LgSwitch closeSwitch;
        private readonly LgCombo threadCombo;
        private readonly Label javaHint;
        private readonly Label javaHint2;
        private readonly List<LgSwatch> swatchLabels = new List<LgSwatch>();
        // 第一格青、第二格蓝，后面按色相铺开，都是能在浅底上读得清的中深色
        private readonly string[] accents = new string[]
        {
            "#0FA3A3", "#2F6BFF", "#1C8FD6", "#2FB98A", "#4C63D2", "#7C5CFF", "#D2497E"
        };
        private string pendingAccent;
        private bool javaScanning;
        private List<JavaInfo> javaList = new List<JavaInfo>();

        public SettingsPage(MainForm host) : base(host)
        {
            AutoScroll = true;
            pendingAccent = Host.Cfg.Accent;

            Label title = MakeLabel("设置", Theme.H1, Theme.Text, Theme.Bg);
            title.Name = "title";
            Controls.Add(title);

            // 主题放在最上面：这是「一眼能看出区别」的设置项，藏在最底下没人会发现
            appearanceCard = new LgCard();
            Controls.Add(appearanceCard);

            Label appearanceTitle = MakeLabel("界面主题", Theme.H3, Theme.Text, appearanceCard.Fill);
            appearanceTitle.Name = "appearanceTitle";
            appearanceCard.Controls.Add(appearanceTitle);

            Label appearanceHint = MakeLabel("点击缩略图立即切换；下面的背景可以换成自己的图片，三套皮肤通用",
                Theme.Small, Theme.SubText, appearanceCard.Fill);
            appearanceHint.Name = "appearanceHint";
            appearanceCard.Controls.Add(appearanceHint);

            liquidTheme = new ThemePreview();
            liquidTheme.Kind = 1;
            liquidTheme.Click += new EventHandler(delegate(object s, EventArgs e) { PickSkin(1); });
            appearanceCard.Controls.Add(liquidTheme);

            millenniumTheme = new ThemePreview();
            millenniumTheme.Kind = 2;
            millenniumTheme.Click += new EventHandler(delegate(object s, EventArgs e) { PickSkin(2); });
            appearanceCard.Controls.Add(millenniumTheme);

            solidTheme = new ThemePreview();
            solidTheme.Kind = 0;
            solidTheme.Click += new EventHandler(delegate(object s, EventArgs e) { PickSkin(0); });
            appearanceCard.Controls.Add(solidTheme);

            // ---- 背景行：状态文字 + 两个按钮 ----
            bgHint = MakeLabel("背景：跟随皮肤", Theme.Small, Theme.SubText, appearanceCard.Fill);
            bgHint.Name = "bgHint";
            appearanceCard.Controls.Add(bgHint);

            bgPickBtn = new LgButton("选择图片");
            bgPickBtn.Ghost = true;
            bgPickBtn.Font = Theme.Small;
            bgPickBtn.Click += new EventHandler(delegate(object s, EventArgs e)
            {
                OpenFileDialog dialog = new OpenFileDialog();
                dialog.Title = "选择背景图片";
                dialog.Filter = "图片 (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|所有文件 (*.*)|*.*";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                Host.Cfg.Background = dialog.FileName;
                Host.Cfg.Save();
                Theme.SetCustomBackground(dialog.FileName);
                UpdateBackgroundHint();
                // 换背景 = 换壁纸，所有离屏玻璃表面都要重建
                Theme.RefreshAll(Host);
                Tip("已更换背景");
            });
            appearanceCard.Controls.Add(bgPickBtn);

            bgClearBtn = new LgButton("跟随皮肤");
            bgClearBtn.Ghost = true;
            bgClearBtn.Font = Theme.Small;
            bgClearBtn.Click += new EventHandler(delegate(object s, EventArgs e)
            {
                if (Host.Cfg.Background.Length == 0)
                {
                    Tip("当前就是皮肤自带的背景");
                    return;
                }
                Host.Cfg.Background = "";
                Host.Cfg.Save();
                Theme.SetCustomBackground("");
                UpdateBackgroundHint();
                Theme.RefreshAll(Host);
                Tip("已恢复皮肤自带背景");
            });
            appearanceCard.Controls.Add(bgClearBtn);

            javaCard = new LgCard();
            Controls.Add(javaCard);
            Label javaTitle = MakeLabel("Java 运行环境", Theme.H3, Theme.Text, javaCard.Fill);
            javaTitle.Name = "javaTitle";
            javaCard.Controls.Add(javaTitle);

            javaCombo = new LgCombo();
            javaCard.Controls.Add(javaCombo);

            browseBtn = new LgButton("浏览");
            browseBtn.Ghost = true;
            browseBtn.Font = Theme.Small;
            browseBtn.Click += new EventHandler(delegate(object s, EventArgs e)
            {
                OpenFileDialog dialog = new OpenFileDialog();
                dialog.Filter = "Java 程序 (java.exe)|java.exe|所有文件 (*.*)|*.*";
                dialog.Title = "选择 java.exe";
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    Host.Cfg.JavaPath = dialog.FileName;
                    Host.Cfg.Save();
                    Reload();
                }
            });
            javaCard.Controls.Add(browseBtn);

            rescanBtn = new LgButton("重新检测");
            rescanBtn.Ghost = true;
            rescanBtn.Font = Theme.Small;
            rescanBtn.Click += new EventHandler(delegate(object s, EventArgs e)
            {
                Host.RunBackground("检测本机 Java", delegate
                {
                    JavaFinder.Scan(true);
                }, delegate { Reload(); });
            });
            javaCard.Controls.Add(rescanBtn);

            installJavaBtn = new LgButton("自动下载 Java");
            installJavaBtn.Font = Theme.Small;
            installJavaBtn.Click += new EventHandler(OnInstallJava);
            javaCard.Controls.Add(installJavaBtn);

            // 离线兜底：下载源全都不通时，用户自己下个 JDK 包导入就行，完全不走网络
            importJavaBtn = new LgButton("导入本地");
            importJavaBtn.Ghost = true;
            importJavaBtn.Font = Theme.Small;
            importJavaBtn.Click += new EventHandler(OnImportJava);
            javaCard.Controls.Add(importJavaBtn);

            javaHint = MakeLabel("启动器会按版本需求自动挑选合适的 Java", Theme.Small, Theme.SubText, javaCard.Fill);
            javaCard.Controls.Add(javaHint);

            javaHint2 = MakeLabel("1.17+ 需要 Java 17，1.20.5+ 需要 Java 21；下载不动时可点「导入本地」离线安装", Theme.Small, Theme.SubText, javaCard.Fill);
            javaCard.Controls.Add(javaHint2);

            runCard = new LgCard();
            Controls.Add(runCard);
            Label runTitle = MakeLabel("游戏与运行", Theme.H3, Theme.Text, runCard.Fill);
            runTitle.Name = "runTitle";
            runCard.Controls.Add(runTitle);

            memoryCombo = new LgCombo();
            string[] memories = new string[] { "1024", "2048", "3072", "4096", "6144", "8192", "12288", "16384" };
            for (int i = 0; i < memories.Length; i++) memoryCombo.Items.Add(memories[i] + " MB");
            runCard.Controls.Add(memoryCombo);

            Label memLabel = MakeLabel("最大内存", Theme.Small, Theme.SubText, runCard.Fill);
            memLabel.Name = "memLabel";
            runCard.Controls.Add(memLabel);

            Label nameLabel = MakeLabel("玩家名（离线）", Theme.Small, Theme.SubText, runCard.Fill);
            nameLabel.Name = "nameLabel";
            runCard.Controls.Add(nameLabel);

            nameInput = new LgInput();
            runCard.Controls.Add(nameInput);

            Label sizeLabel = MakeLabel("窗口尺寸", Theme.Small, Theme.SubText, runCard.Fill);
            sizeLabel.Name = "sizeLabel";
            runCard.Controls.Add(sizeLabel);

            widthInput = new LgInput();
            runCard.Controls.Add(widthInput);
            heightInput = new LgInput();
            runCard.Controls.Add(heightInput);

            isolationSwitch = new LgSwitch();
            runCard.Controls.Add(isolationSwitch);
            Label isolationLabel = MakeLabel("版本隔离（每个实例独立的 mods / 存档）", Theme.Body, Theme.Text, runCard.Fill);
            isolationLabel.Name = "isoLabel";
            runCard.Controls.Add(isolationLabel);

            fullscreenSwitch = new LgSwitch();
            runCard.Controls.Add(fullscreenSwitch);
            Label fsLabel = MakeLabel("全屏启动", Theme.Body, Theme.Text, runCard.Fill);
            fsLabel.Name = "fsLabel";
            runCard.Controls.Add(fsLabel);

            closeSwitch = new LgSwitch();
            runCard.Controls.Add(closeSwitch);
            Label closeLabel = MakeLabel("启动后关闭启动器", Theme.Body, Theme.Text, runCard.Fill);
            closeLabel.Name = "closeLabel";
            runCard.Controls.Add(closeLabel);

            netCard = new LgCard();
            Controls.Add(netCard);
            Label netTitle = MakeLabel("下载与外观", Theme.H3, Theme.Text, netCard.Fill);
            netTitle.Name = "netTitle";
            netCard.Controls.Add(netTitle);

            mirrorSwitch = new LgSwitch();
            netCard.Controls.Add(mirrorSwitch);
            Label mirrorLabel = MakeLabel("使用国内镜像下载游戏文件（BMCLAPI）", Theme.Body, Theme.Text, netCard.Fill);
            mirrorLabel.Name = "mirrorLabel";
            netCard.Controls.Add(mirrorLabel);

            Label threadLabel = MakeLabel("下载线程数", Theme.Small, Theme.SubText, netCard.Fill);
            threadLabel.Name = "threadLabel";
            netCard.Controls.Add(threadLabel);

            threadCombo = new LgCombo();
            threadCombo.Items.Add("1");
            threadCombo.Items.Add("4");
            threadCombo.Items.Add("8");
            threadCombo.Items.Add("16");
            netCard.Controls.Add(threadCombo);

            Label accentLabel = MakeLabel("主题色", Theme.Small, Theme.SubText, netCard.Fill);
            accentLabel.Name = "accentLabel";
            netCard.Controls.Add(accentLabel);

            for (int i = 0; i < accents.Length; i++)
            {
                LgSwatch swatch = new LgSwatch(Theme.ParseColor(accents[i], Theme.Accent));
                swatch.Tag = accents[i];
                swatch.Click += new EventHandler(delegate(object s, EventArgs e)
                {
                    LgSwatch picked = s as LgSwatch;
                    if (picked == null) return;
                    pendingAccent = (string)picked.Tag;
                    Theme.ApplyAccent(pendingAccent);
                    for (int k = 0; k < swatchLabels.Count; k++)
                    {
                        swatchLabels[k].Selected = swatchLabels[k] == picked;
                        swatchLabels[k].Invalidate();
                    }
                    // 换色 = 换整窗壁纸（光斑颜色变了），所以整棵可视树都要重画：
                    // 只 Invalidate 窗体的话，子控件的离屏表面会留着旧色。
                    Theme.RefreshAll(Host);
                });
                netCard.Controls.Add(swatch);
                swatchLabels.Add(swatch);
            }

            saveBtn = new LgButton("保存设置");
            saveBtn.Click += new EventHandler(OnSave);
            Controls.Add(saveBtn);

            Label jvmLabel = MakeLabel("自定义 JVM 参数（可选，空格分隔）", Theme.Small, Theme.SubText, Theme.Bg);
            jvmLabel.Name = "jvmLabel";
            Controls.Add(jvmLabel);

            jvmInput = new LgInput();
            Controls.Add(jvmInput);
        }

        protected override void LayoutPage()
        {
            // 减 20 是给右侧滚动条预留宽度，否则卡片会被滚动条压住一截
            int w = ClientSize.Width - 20;
            int h = ClientSize.Height;
            if (w <= 0 || h <= 0) return;

            int pad = Theme.Pad;
            int cardW = w - pad * 2;
            if (cardW < 320)
            {
                pad = 12;
                cardW = w - 24;
            }

            Label title = Controls["title"] as Label;
            if (title != null) title.SetBounds(pad, 14, 300, 36);

            // 主题卡：三张缩略图 + 一行背景选择。
            // 高度 = 68(顶部标题/提示) + 98(缩略图) + 48(背景行) + 余量
            if (appearanceCard != null) appearanceCard.SetBounds(pad, 68, cardW, 214);
            Label appearanceTitle = appearanceCard == null ? null : appearanceCard.Controls["appearanceTitle"] as Label;
            if (appearanceTitle != null) appearanceTitle.SetBounds(26, 14, 240, 22);
            Label appearanceHint = appearanceCard == null ? null : appearanceCard.Controls["appearanceHint"] as Label;
            if (appearanceHint != null) appearanceHint.SetBounds(26, 38, Math.Max(200, cardW - 52), 18);

            // 三张缩略图平分一行，间距 12；窄窗时允许压到 130 宽
            int tileY = 62;
            int tileH = 98;
            int gap = 12;
            int tileW = (cardW - 52 - gap * 2) / 3;
            if (tileW > 240) tileW = 240;
            if (tileW < 130)
            {
                tileW = 130;
                tileH = 90;
            }
            if (liquidTheme != null) liquidTheme.SetBounds(26, tileY, tileW, tileH);
            if (millenniumTheme != null) millenniumTheme.SetBounds(26 + tileW + gap, tileY, tileW, tileH);
            if (solidTheme != null) solidTheme.SetBounds(26 + (tileW + gap) * 2, tileY, tileW, tileH);

            // 背景行：左边状态文字，右边两个按钮
            if (bgPickBtn != null) bgPickBtn.SetBounds(Math.Max(150, cardW - 26 - 208), 170, 100, 30);
            if (bgClearBtn != null) bgClearBtn.SetBounds(Math.Max(150, cardW - 26 - 104), 170, 100, 30);
            if (bgHint != null)
            {
                int hintW = cardW - 26 - 216 - 12;
                bgHint.SetBounds(26, 175, Math.Max(120, hintW), 20);
            }

            // 后面所有卡片跟着下移（主题卡从 176 长到 214，多出 38 + 间距）
            javaCard.SetBounds(pad, 298, cardW, 216);
            Label javaTitle = javaCard.Controls["javaTitle"] as Label;
            if (javaTitle != null) javaTitle.SetBounds(26, 18, 240, 22);
            javaCombo.SetBounds(26, 52, Math.Max(160, cardW - 52), 34);
            browseBtn.SetBounds(26, 94, 86, 34);
            rescanBtn.SetBounds(120, 94, 96, 34);
            importJavaBtn.SetBounds(224, 94, 112, 34);
            // 窗口窄的时候让「自动下载 Java」贴住前面的按钮并压缩宽度，不要顶出卡片右边缘
            int javaActX = cardW - 26 - 140;
            int javaActW = 140;
            if (javaActX < 344)
            {
                javaActX = 344;
                javaActW = Math.Max(80, cardW - 26 - javaActX);
            }
            installJavaBtn.SetBounds(javaActX, 94, javaActW, 34);
            javaHint.SetBounds(26, 140, cardW - 52, 20);
            javaHint2.SetBounds(26, 164, cardW - 52, 20);

            runCard.SetBounds(pad, 530, cardW, 280);
            Label runTitle = runCard.Controls["runTitle"] as Label;
            if (runTitle != null) runTitle.SetBounds(26, 18, 240, 22);

            Label memLabel = runCard.Controls["memLabel"] as Label;
            if (memLabel != null) memLabel.SetBounds(26, 54, 100, 20);
            memoryCombo.SetBounds(26, 76, 150, 34);

            Label nameLabel = runCard.Controls["nameLabel"] as Label;
            if (nameLabel != null) nameLabel.SetBounds(200, 54, 160, 20);
            nameInput.SetBounds(200, 76, 190, 38);

            Label sizeLabel = runCard.Controls["sizeLabel"] as Label;
            if (sizeLabel != null) sizeLabel.SetBounds(420, 54, 100, 20);
            widthInput.SetBounds(420, 76, 96, 38);
            heightInput.SetBounds(526, 76, 96, 38);

            isolationSwitch.SetBounds(26, 136, 46, 26);
            Label isoLabel = runCard.Controls["isoLabel"] as Label;
            if (isoLabel != null) isoLabel.SetBounds(86, 136, Math.Max(120, cardW - 112), 26);
            fullscreenSwitch.SetBounds(26, 176, 46, 26);
            Label fsLabel = runCard.Controls["fsLabel"] as Label;
            if (fsLabel != null) fsLabel.SetBounds(86, 176, 300, 26);
            closeSwitch.SetBounds(26, 216, 46, 26);
            Label closeLabel = runCard.Controls["closeLabel"] as Label;
            if (closeLabel != null) closeLabel.SetBounds(86, 216, 300, 26);

            netCard.SetBounds(pad, 826, cardW, 180);
            Label netTitle = netCard.Controls["netTitle"] as Label;
            if (netTitle != null) netTitle.SetBounds(26, 18, 240, 22);
            mirrorSwitch.SetBounds(26, 54, 46, 26);
            Label mirrorLabel = netCard.Controls["mirrorLabel"] as Label;
            if (mirrorLabel != null) mirrorLabel.SetBounds(86, 54, Math.Max(200, cardW - 112), 26);

            Label threadLabel = netCard.Controls["threadLabel"] as Label;
            if (threadLabel != null) threadLabel.SetBounds(26, 100, 90, 20);
            threadCombo.SetBounds(122, 94, 96, 34);

            Label accentLabel = netCard.Controls["accentLabel"] as Label;
            if (accentLabel != null) accentLabel.SetBounds(250, 100, 50, 20);
            for (int i = 0; i < swatchLabels.Count; i++)
            {
                swatchLabels[i].SetBounds(310 + i * 40, 90, 32, 32);
            }

            Label jvmLabel = Controls["jvmLabel"] as Label;
            if (jvmLabel != null) jvmLabel.SetBounds(pad, 1026, 320, 20);
            jvmInput.SetBounds(pad, 1048, cardW, 38);

            saveBtn.SetBounds(pad, 1104, 150, 42);
        }

        public override void Reload()
        {
            ApplyFields();
            if (javaScanning) return;
            javaScanning = true;
            javaCombo.Items.Clear();
            javaCombo.Items.Add("正在检测本机 Java…");
            javaCombo.SelectedIndex = 0;
            javaHint.Text = "正在检测本机 Java…";

            // 原来在 UI 线程上同步扫 Java（注册表 + 多个安装目录 + 官方启动器 runtime），
            // 会造成界面假死。改为后台线程扫，扫完再回填。
            System.Threading.Thread thread = new System.Threading.Thread(delegate()
            {
                List<JavaInfo> found = new List<JavaInfo>();
                try
                {
                    found = JavaFinder.Scan(false);
                }
                catch (Exception ex)
                {
                    Log.Write("Java 探测失败：" + ex.Message);
                }
                Host.Ui(delegate()
                {
                    javaScanning = false;
                    if (IsDisposed) return;
                    FillJavaCombo(found);
                });
            });
            thread.IsBackground = true;
            thread.Start();
        }

        /// <summary>当前选中实例需要哪个大版本的 Java；没有实例时给 21（1.20.5+ 的要求）</summary>
        private int RequiredJavaMajor()
        {
            try
            {
                if (Host.Cfg.LastVersion.Length > 0)
                {
                    GameVersion v = VersionMeta.Load(Host.Cfg.LastVersion);
                    if (v.JavaMajor > 0) return v.JavaMajor;
                }
            }
            catch
            {
            }
            return 21;
        }

        private void OnInstallJava(object sender, EventArgs e)
        {
            int major = RequiredJavaMajor();
            if (JavaInstaller.IsReady(major))
            {
                JavaFinder.Invalidate();
                Reload();
                Tip("Java " + major + " 已在本机，无需重复下载");
                return;
            }
            Host.RunBackground("自动下载 Java " + major, delegate
            {
                JavaInstaller.Install(major, new ProgressHandler(Host.Report));
            }, delegate
            {
                JavaFinder.Invalidate();
                Reload();
                Tip("Java " + major + " 已就绪，现在可以直接启动游戏");
            });
        }

        /// <summary>
        /// 离线导入：用户自己下好的 JDK 压缩包，或者已经装在别处的 java.exe。
        /// 这是「所有下载源都不通」时的兜底，不依赖任何网络。
        /// </summary>
        private void OnImportJava(object sender, EventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "选择 JDK/JRE 压缩包，或已装好的 java.exe";
            dialog.Filter = "Java 压缩包 (*.zip)|*.zip|java.exe|java.exe|所有文件 (*.*)|*.*";
            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            int major = RequiredJavaMajor();
            string file = dialog.FileName;
            Host.RunBackground("导入 Java " + major, delegate
            {
                JavaInstaller.Import(file, major, new ProgressHandler(Host.Report));
            }, delegate
            {
                JavaFinder.Invalidate();
                Reload();
                Tip("Java " + major + " 已导入，现在可以直接启动游戏");
            });
        }

        private void FillJavaCombo(List<JavaInfo> found)
        {
            javaList = found;
            javaCombo.Items.Clear();
            int selected = -1;
            for (int i = 0; i < javaList.Count; i++)
            {
                JavaInfo info = javaList[i];
                javaCombo.Items.Add("Java " + info.Major + (info.Is64Bit ? " (64 位)" : " (32 位)") + "  ·  " + info.Path);
                if (Host.Cfg.JavaPath.Length > 0 && string.Equals(info.Path, Host.Cfg.JavaPath, StringComparison.OrdinalIgnoreCase))
                    selected = i;
            }
            if (Host.Cfg.JavaPath.Length > 0 && selected < 0)
            {
                javaCombo.Items.Add("自定义  ·  " + Host.Cfg.JavaPath);
                selected = javaCombo.Items.Count - 1;
            }
            if (selected >= 0) javaCombo.SelectedIndex = selected;
            else if (javaCombo.Items.Count > 0) javaCombo.SelectedIndex = 0;

            javaHint.Text = javaList.Count == 0
                ? "未检测到 Java。点「自动下载 Java」或在主页直接启动，启动器会自动装好"
                : "已检测到 " + javaList.Count + " 个 Java。留空则按游戏版本自动挑选";
        }

        private void ApplyFields()
        {
            int memoryIndex = 1;
            int[] memories = new int[] { 1024, 2048, 3072, 4096, 6144, 8192, 12288, 16384 };
            for (int i = 0; i < memories.Length; i++)
            {
                if (memories[i] == Host.Cfg.MemoryMb) memoryIndex = i;
            }
            memoryCombo.SelectedIndex = memoryIndex;

            nameInput.Text = Host.Cfg.PlayerName;
            widthInput.Text = Host.Cfg.WindowWidth.ToString();
            heightInput.Text = Host.Cfg.WindowHeight.ToString();
            jvmInput.Text = Host.Cfg.JvmArgs;
            mirrorSwitch.Checked = Host.Cfg.UseMirror;
            isolationSwitch.Checked = Host.Cfg.VersionIsolation;
            fullscreenSwitch.Checked = Host.Cfg.Fullscreen;
            closeSwitch.Checked = Host.Cfg.CloseOnLaunch;

            int[] threads = new int[] { 1, 4, 8, 16 };
            int threadIndex = 2;
            for (int i = 0; i < threads.Length; i++)
            {
                if (threads[i] == Host.Cfg.DownloadThreads) threadIndex = i;
            }
            threadCombo.SelectedIndex = threadIndex;
            pendingAccent = Host.Cfg.Accent;

            // 当前主题色对应的圆点打勾，否则用户看不出「现在用的是哪个」
            for (int i = 0; i < swatchLabels.Count; i++)
            {
                bool on = string.Equals((string)swatchLabels[i].Tag, pendingAccent, StringComparison.OrdinalIgnoreCase);
                if (swatchLabels[i].Selected != on)
                {
                    swatchLabels[i].Selected = on;
                    swatchLabels[i].Invalidate();
                }
            }
            UpdateThemeSelection();
            UpdateBackgroundHint();
        }

        /// <summary>当前皮肤在主题预览卡上打勾</summary>
        private void UpdateThemeSelection()
        {
            int kind = (int)Theme.ParseSkin(Host.Cfg.Theme);
            // ParseSkin 返回的是 Theme.Skin，枚举值和 ThemePreview.Kind 是同一套编码：
            // 0 = 纯色，1 = 液态玻璃，2 = 千禧年。两边必须一直对齐。
            if (liquidTheme != null)
            {
                liquidTheme.Selected = kind == 1;
                liquidTheme.Invalidate();
            }
            if (millenniumTheme != null)
            {
                millenniumTheme.Selected = kind == 2;
                millenniumTheme.Invalidate();
            }
            if (solidTheme != null)
            {
                solidTheme.Selected = kind == 0;
                solidTheme.Invalidate();
            }
        }

        /// <summary>背景那一行的状态文字</summary>
        private void UpdateBackgroundHint()
        {
            if (bgHint == null) return;
            if (Theme.HasCustomBackground)
            {
                string name = "";
                try
                {
                    name = System.IO.Path.GetFileName(Host.Cfg.Background);
                }
                catch
                {
                    name = Host.Cfg.Background;
                }
                bgHint.Text = "背景：自定义 · " + name;
            }
            else if (Host.Cfg.Background.Length > 0)
            {
                bgHint.Text = "背景：文件已丢失，正在使用皮肤自带背景";
            }
            else
            {
                bgHint.Text = "背景：跟随皮肤";
            }
            bgHint.Invalidate();
        }

        /// <summary>
        /// 切换皮肤：立即生效 + 立即写盘（不需要再点「保存设置」）。
        ///
        /// 换皮肤不只是换壁纸：每个控件的 BackColor 里都缓存着旧主题的底色，
        /// 所以要走三步 —— SetSkin 换令牌与壁纸、ApplySkin 同步原生控件底色、
        /// RefreshAll 让整棵可视树重画（否则会看到一半新皮肤一半旧皮肤的界面）。
        /// </summary>
        private void PickSkin(int kind)
        {
            Theme.Skin skin = Theme.ParseSkin(kind == 2 ? "millennium" : (kind == 0 ? "solid" : "liquid"));
            string want = Theme.SkinKey(skin);
            if (string.Equals(Host.Cfg.Theme, want, StringComparison.OrdinalIgnoreCase))
            {
                Tip("当前已经是这个主题");
                return;
            }

            // 主色跟着皮肤走：液态玻璃用蓝、千禧年用亮青、纯色保留用户自己的色。
            // 不带过去的话，切到液态玻璃会得到「蓝条纹 + 青绿按钮」这种撞色。
            Host.Cfg.Theme = want;
            if (skin == Theme.Skin.Liquid) Host.Cfg.Accent = "#2F6BFF";
            else if (skin == Theme.Skin.Millennium) Host.Cfg.Accent = "#1C8FD6";
            pendingAccent = Host.Cfg.Accent;
            Host.Cfg.Save();

            Theme.SetSkin(skin);
            Theme.ApplyAccent(Host.Cfg.Accent);
            Theme.ApplySkin(Host);

            for (int i = 0; i < swatchLabels.Count; i++)
            {
                bool on = string.Equals((string)swatchLabels[i].Tag, pendingAccent, StringComparison.OrdinalIgnoreCase);
                swatchLabels[i].Selected = on;
                swatchLabels[i].Invalidate();
            }
            UpdateThemeSelection();
            Theme.RefreshAll(Host);
            Tip("已切换到「" + (skin == Theme.Skin.Liquid ? "玻璃" : (skin == Theme.Skin.Millennium ? "千禧年" : "纯色")) + "」");
        }

        private void OnSave(object sender, EventArgs e)
        {
            Config cfg = Host.Cfg;
            string name = nameInput.Text.Trim();
            if (!Util.IsValidPlayerName(name))
            {
                MessageBox.Show(this, "玩家名需要 3-16 位，只能包含字母、数字和下划线", "流光启动器",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            cfg.PlayerName = name;

            if (javaCombo.SelectedIndex >= 0 && javaCombo.SelectedIndex < javaList.Count)
                cfg.JavaPath = javaList[javaCombo.SelectedIndex].Path;

            int[] memories = new int[] { 1024, 2048, 3072, 4096, 6144, 8192, 12288, 16384 };
            if (memoryCombo.SelectedIndex >= 0 && memoryCombo.SelectedIndex < memories.Length)
                cfg.MemoryMb = memories[memoryCombo.SelectedIndex];

            int value;
            if (int.TryParse(widthInput.Text.Trim(), out value) && value >= 320) cfg.WindowWidth = value;
            if (int.TryParse(heightInput.Text.Trim(), out value) && value >= 240) cfg.WindowHeight = value;

            int[] threads = new int[] { 1, 4, 8, 16 };
            if (threadCombo.SelectedIndex >= 0 && threadCombo.SelectedIndex < threads.Length)
                cfg.DownloadThreads = threads[threadCombo.SelectedIndex];

            cfg.UseMirror = mirrorSwitch.Checked;
            cfg.VersionIsolation = isolationSwitch.Checked;
            cfg.Fullscreen = fullscreenSwitch.Checked;
            cfg.CloseOnLaunch = closeSwitch.Checked;
            cfg.JvmArgs = jvmInput.Text.Trim();
            cfg.Accent = pendingAccent;
            cfg.Theme = Host.Cfg.Theme;
            cfg.Background = Host.Cfg.Background;
            cfg.Save();

            Net.UseMirror = cfg.UseMirror;
            Theme.ApplyAccent(cfg.Accent);
            Theme.RefreshAll(Host);
            Tip("设置已保存");
        }
    }
}
