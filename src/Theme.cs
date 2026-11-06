using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace Liuguang.UI
{
    public static class Theme
    {
        // ==================== 设计令牌 ====================
        // 圆角、间距、字号全部收在这里。页面里不再出现魔法数字，
        // 改一处全站生效——这是让界面「看起来是一个设计系统」而不是拼凑的前提。

        public const int Pad = 24;          // 页面外边距：内容一律不贴窗口边
        public const int Gap = 16;          // 卡片 / 区块之间的间距
        public const int RadiusCard = 16;   // 卡片
        public const int RadiusBtn = 10;    // 按钮
        public const int RadiusInput = 10;  // 输入框 / 下拉框
        public const int RadiusRow = 10;    // 列表行

        // ---- 青色主题（默认）----
        // 中性色带一点青调（不是纯灰），和主色同源，界面才不会「白底 + 彩色按钮」两张皮。
        public static Color Bg = Color.FromArgb(244, 247, 249);        // 页面底
        public static Color Sidebar = Color.FromArgb(251, 252, 253);   // 侧栏
        public static Color Card = Color.FromArgb(255, 255, 255);      // 卡片实底（对话框等不透明场景）
        public static Color CardAlt = Color.FromArgb(247, 250, 251);   // 次级填充
        public static Color CardHover = Color.FromArgb(233, 245, 245); // 悬停/选中：带青调
        /// <summary>
        /// 玻璃卡片下面那一层「近似色」。卡片本身是半透明玻璃，
        /// 这个值只在拿不到壁纸时兜底、以及判断「这个底色是实色还是玻璃」时使用；
        /// 不能再等于纯白，否则等于告诉子控件「我是不透明的白板」。
        /// </summary>
        public static Color GlassCard = Color.FromArgb(246, 252, 253);
        public static Color Border = Color.FromArgb(224, 232, 236);    // 描边
        public static Color BorderStrong = Color.FromArgb(206, 218, 224);
        public static Color Text = Color.FromArgb(20, 32, 42);         // 主文字：深蓝黑，比纯黑柔和
        public static Color SubText = Color.FromArgb(105, 117, 127);   // 次要文字

        public static Color Accent = Color.FromArgb(15, 163, 163);     // 主色：青
        public static Color Accent2 = Color.FromArgb(35, 188, 203);    // 副色：青蓝
        public static Color Accent3 = Color.FromArgb(79, 214, 192);    // 点缀：薄荷
        public static Color Good = Color.FromArgb(16, 155, 102);
        public static Color Warn = Color.FromArgb(196, 128, 24);
        public static Color Bad = Color.FromArgb(216, 66, 82);

        public static Color Track = Color.FromArgb(233, 239, 243);     // 进度条轨道 / 开关关闭态
        public static Color InputBg = Color.FromArgb(251, 253, 253);   // 输入框内部
        // 日志区底：三套皮肤统一用深色。终端就该是深的，
        // 而且 TextBox 是自绘不了的实心矩形，浅色时它就是一块贴上去的白纸。
        public static Color LogBg = Color.FromArgb(17, 23, 41);
        public static Color LogText = Color.FromArgb(186, 202, 232);   // 日志正文
        public static Color LogTextDim = Color.FromArgb(122, 140, 172); // 日志辅助信息
        public static Color Disabled = Color.FromArgb(238, 242, 245);  // 禁用填充
        public static Color Shadow = Color.FromArgb(20, 32, 42);       // 阴影基色（配低透明度）
        public static Color RowHover = Color.FromArgb(246, 250, 251);  // 列表行悬停

        // ---- 字体 ----
        // YaHei UI 是 Win10 起自带的 UI 版雅黑，字重比老雅黑细腻；没有就退回雅黑。
        public static readonly string UiFamily = PickFamily(
            new string[] { "Microsoft YaHei UI", "Microsoft YaHei", "微软雅黑" }, "Microsoft Sans Serif");
        public static readonly string MonoFamily = PickFamily(
            new string[] { "Cascadia Mono", "Consolas", "Courier New" }, "Courier New");
        // 导航图标用 MDL2（Win10/11 自带），字形规整、粗细一致；老系统退回 Segoe UI Symbol
        public static readonly string IconFamily = PickFamily(
            new string[] { "Segoe MDL2 Assets", "Segoe UI Symbol" }, "Segoe UI Symbol");
        // MDL2 用的是私用区码点，字体不存在时会渲染成方块，所以导航图标要按这个开关切换字符集
        public static readonly bool HasMdl2 = string.Equals(IconFamily, "Segoe MDL2 Assets", StringComparison.OrdinalIgnoreCase);

        // H1 从 22 Bold 改成 19 Regular：22pt 粗体在 96DPI 下是 29px 的黑体块，
        // 那是「对话框标题」的形态。现代界面的页面标题靠「大 + 细」拉层级，
        // 不靠加粗 —— 加粗反而显得吵。
        public static Font H1 = MakeFont(19f, FontStyle.Regular);
        public static Font H2 = MakeFont(15f, FontStyle.Bold);
        public static Font H3 = MakeFont(11.5f, FontStyle.Bold);
        public static Font RowTitle = MakeFont(10.5f, FontStyle.Bold);
        public static Font Body = MakeFont(10f, FontStyle.Regular);
        public static Font BodyBold = MakeFont(10f, FontStyle.Bold);
        public static Font Small = MakeFont(9f, FontStyle.Regular);
        public static Font Tiny = MakeFont(8f, FontStyle.Regular);
        public static Font Mono = new Font(MonoFamily, 9.5f, FontStyle.Regular);
        public static Font Icon = new Font(IconFamily, 12f, FontStyle.Regular);
        // 侧栏图标轨用的大号图标。12f 放在 92px 宽的竖排导航里像个句点，
        // 16f 才站得住（导航项本身只有 76px 宽，图标是唯一的视觉主体）。
        public static Font IconNav = new Font(IconFamily, 16f, FontStyle.Regular);

        public static Font MakeFont(float size, FontStyle style)
        {
            try
            {
                return new Font(UiFamily, size, style);
            }
            catch
            {
                return new Font("Microsoft Sans Serif", size, style);
            }
        }

        /// <summary>按优先级挑一个系统里真实存在的字体族，避免 new Font 静默回退成宋体</summary>
        private static string PickFamily(string[] candidates, string fallback)
        {
            try
            {
                InstalledFontCollection fonts = new InstalledFontCollection();
                FontFamily[] families = fonts.Families;
                for (int c = 0; c < candidates.Length; c++)
                {
                    for (int i = 0; i < families.Length; i++)
                    {
                        if (string.Equals(families[i].Name, candidates[c], StringComparison.OrdinalIgnoreCase))
                        {
                            return families[i].Name;
                        }
                    }
                }
            }
            catch
            {
            }
            return fallback;
        }

        public static void ApplyAccent(string hex)
        {
            // 玻璃/千禧年皮肤的主色由皮肤本身决定，不再让用户配置的 hex 覆盖：
            // 否则「蓝条纹 + 用户青绿色按钮」这种撞色永远存在。
            // 只有纯色皮肤真正尊重用户挑的主题色。
            if (LiquidMode) hex = "#2F6BFF";
            else if (MillenniumMode) hex = "#1C8FD6";

            Color c = ParseColor(hex, Accent);
            // 白底上高亮色要压暗一点，否则渐变尾段在白卡片上几乎看不清
            if (Luma(c) > 190) c = Darken(c, 0.22f);
            Accent = c;
            if (LiquidMode)
            {
                // 液态玻璃主题的副色往「更亮的蓝 / 冰蓝」推，和条纹背景同一家族；
                // 沿用青绿目标色会让蓝色主色推导出发灰的蓝绿渐变。
                Accent2 = Lighten(Mix(c, Color.FromArgb(96, 156, 255), 0.58f), 0.04f);
                Accent3 = Lighten(Mix(c, Color.FromArgb(168, 210, 255), 0.60f), 0.06f);
            }
            else if (MillenniumMode)
            {
                // 千禧年的副色要往「青 / 湖蓝」推，而且要更亮：
                // 那个年代的渐变一律是高饱和到高亮度的双色，中间不夹灰。
                Accent2 = Lighten(Mix(c, Color.FromArgb(0, 186, 255), 0.55f), 0.16f);
                Accent3 = Lighten(Mix(c, Color.FromArgb(140, 240, 255), 0.60f), 0.20f);
            }
            else
            {
                // 纯色主题不要花：副色只是主色的略浅版，用来做渐变尾巴
                Accent2 = Lighten(Mix(c, Color.FromArgb(120, 140, 160), 0.22f), 0.10f);
                Accent3 = Lighten(c, 0.26f);
            }
            InvalidateWallpaper();   // 换主题色后壁纸要重画，否则光斑还是旧颜色
        }

        // ==================== 主题皮肤 ====================
        // 三套皮肤共用同一份绘制代码，只在这里换「设计令牌 + 背景图像 + 玻璃参数」。
        // 也就是说它们不是一个界面做三份，而是同一套界面换三层材质：
        //   液态玻璃 = 蓝白斜条纹 + 真折射（厚玻璃）
        //   千禧年   = 青蓝天光 + 玻璃泡泡 + 强顶光（Aqua 那一套）
        //   纯色     = 平涂底色 + 白卡细边（不要玻璃，要干净）

        public enum Skin
        {
            Solid = 0,
            Liquid = 1,
            Millennium = 2
        }

        public static Skin CurrentSkin = Skin.Liquid;

        /// <summary>true = 液态玻璃主题</summary>
        public static bool LiquidMode
        {
            get { return CurrentSkin == Skin.Liquid; }
        }

        /// <summary>true = 千禧年主题</summary>
        public static bool MillenniumMode
        {
            get { return CurrentSkin == Skin.Millennium; }
        }

        /// <summary>true = 纯色主题（平涂，不跑折射）</summary>
        public static bool SolidMode
        {
            get { return CurrentSkin == Skin.Solid; }
        }

        /// <summary>配置里存的皮肤键</summary>
        public static string SkinKey(Skin skin)
        {
            if (skin == Skin.Liquid) return "liquid";
            if (skin == Skin.Millennium) return "millennium";
            return "solid";
        }

        /// <summary>
        /// 解析配置里的皮肤键。认不出来的按液态玻璃处理 ——
        /// 老配置（1.4.0 之前的 Theme="" 或 "classic"）都归到这里，
        /// 那时界面上唯一的皮肤就是玻璃，迁到"看不出区别"比迁到"看起来坏了"安全。
        /// </summary>
        public static Skin ParseSkin(string key)
        {
            if (string.Equals(key, "solid", StringComparison.OrdinalIgnoreCase)) return Skin.Solid;
            if (string.Equals(key, "millennium", StringComparison.OrdinalIgnoreCase)) return Skin.Millennium;
            if (string.Equals(key, "liquid", StringComparison.OrdinalIgnoreCase)) return Skin.Liquid;
            return Skin.Liquid;
        }

        /// <summary>
        /// 切换皮肤。只改令牌和壁纸；已经创建的控件靠 <see cref="ApplySkin"/> 同步底色，
        /// 靠 <see cref="Revision"/> 让所有离屏玻璃表面重建。
        /// </summary>
        public static void SetSkin(Skin skin)
        {
            CurrentSkin = skin;
            if (skin == Skin.Liquid)
            {
                // 液态玻璃的中性色全部带蓝调，且必须比纯白「深一档」，
                // 因为底下压着的是蓝白条纹，纯白卡片会像贴纸。
                Bg = Color.FromArgb(226, 238, 255);
                Sidebar = Color.FromArgb(238, 245, 255);
                Card = Color.FromArgb(240, 247, 255);
                CardAlt = Color.FromArgb(228, 239, 255);
                CardHover = Color.FromArgb(214, 231, 255);
                GlassCard = Color.FromArgb(234, 243, 255);
                Border = Color.FromArgb(120, 255, 255, 255);
                BorderStrong = Color.FromArgb(190, 255, 255, 255);
                Text = Color.FromArgb(10, 26, 56);
                SubText = Color.FromArgb(62, 86, 126);
                Track = Color.FromArgb(120, 255, 255, 255);
                InputBg = Color.FromArgb(244, 249, 255);
                // 日志区深色：跟卡片里的深色玻璃板（LgCard.Dark）配套
                LogBg = Color.FromArgb(17, 23, 41);
                Disabled = Color.FromArgb(228, 237, 250);
                Shadow = Color.FromArgb(10, 34, 78);
                RowHover = Color.FromArgb(96, 255, 255, 255);
                // 玻璃薄膜的白色雾度。用户要「70% 透明」，意味着卡片要让背景
                // 透出七成，只有约三成不透明。雾度必须压到很低，否则又是磨砂白板。
                // 14 已经是接近通透的下限，再低深色正文会压在条纹上发花。
                GlassAlpha = 14;

                Liquid.Strength = 2.10f;
                Liquid.Band = 0.155f;
                Liquid.Dispersion = 0.20f;
                Liquid.Saturation = 1.12f;
                // 噪点归零：切向液化波（swirl）在照片壁纸上就是彩色噪点。
                // 液态玻璃要的是干净的边缘折射，不是满屏颗粒。
                Liquid.Noise = 0f;
                // 模糊 12 -> 16：噪点去掉后，玻璃的「透而不清」完全靠 box blur 提供。
                // 模糊更足、白雾更薄（34 -> 24），才读作通透的玻璃而不是磨砂白板。
                Liquid.Blur = 16;
            }
            else if (skin == Skin.Millennium)
            {
                // 千禧年的颜色要「饱和、发亮、偏青」，像 2000 年代的系统主题：
                // 天光是高饱和的湖蓝，卡片是奶白玻璃，描边是可见的灰蓝而不是白色。
                // 关键区别在 GlassAlpha：118 已经是「半透明塑料」而不是玻璃，
                // 泡泡和顶光叠上去才是那个年代的味道。
                Bg = Color.FromArgb(206, 236, 253);
                Sidebar = Color.FromArgb(232, 246, 255);
                Card = Color.FromArgb(240, 250, 255);
                CardAlt = Color.FromArgb(222, 242, 255);
                CardHover = Color.FromArgb(203, 232, 255);
                GlassCard = Color.FromArgb(232, 246, 255);
                Border = Color.FromArgb(150, 190, 225);
                BorderStrong = Color.FromArgb(108, 164, 212);
                Text = Color.FromArgb(12, 42, 78);
                SubText = Color.FromArgb(62, 104, 148);
                Track = Color.FromArgb(176, 214, 244);
                InputBg = Color.FromArgb(250, 253, 255);
                LogBg = Color.FromArgb(10, 24, 40);   // 千禧年：略带青的深色
                Disabled = Color.FromArgb(226, 236, 246);
                Shadow = Color.FromArgb(12, 52, 96);
                RowHover = Color.FromArgb(206, 234, 255);
                GlassAlpha = 118;

                // 折射只留一点点：千禧年的玻璃是「亮」，不是「弯」。
                // 强度给到 2.10 会把泡泡拉成哈哈镜，反而不像那个年代。
                Liquid.Strength = 0.95f;
                Liquid.Band = 0.100f;
                Liquid.Dispersion = 0.06f;
                Liquid.Saturation = 1.06f;
                Liquid.Noise = 1.00f;
                Liquid.Blur = 2;
            }
            else
            {
                // 纯色：令牌全部走中性灰，卡片就是白纸 + 一根细边，不跑任何玻璃。
                Bg = Color.FromArgb(243, 245, 247);
                Sidebar = Color.FromArgb(250, 251, 252);
                Card = Color.FromArgb(255, 255, 255);
                CardAlt = Color.FromArgb(246, 248, 250);
                CardHover = Color.FromArgb(236, 241, 245);
                GlassCard = Color.FromArgb(255, 255, 255);
                Border = Color.FromArgb(222, 228, 234);
                BorderStrong = Color.FromArgb(202, 211, 219);
                Text = Color.FromArgb(24, 32, 42);
                SubText = Color.FromArgb(104, 116, 128);
                Track = Color.FromArgb(230, 235, 240);
                InputBg = Color.FromArgb(250, 251, 252);
                LogBg = Color.FromArgb(20, 24, 30);   // 纯色：中性深灰
                Disabled = Color.FromArgb(238, 241, 244);
                Shadow = Color.FromArgb(20, 32, 42);
                RowHover = Color.FromArgb(244, 247, 250);
                GlassAlpha = 0;

                Liquid.Strength = 0f;
                Liquid.Band = 0.10f;
                Liquid.Dispersion = 0f;
                Liquid.Saturation = 1.00f;
                Liquid.Noise = 1.00f;
                Liquid.Blur = 2;
            }
            // 主题色跟着皮肤走，保证「蓝条纹 + 蓝按钮」不撞色：
            // 玻璃皮肤固定用蓝，千禧年固定用亮青，纯色才保留用户自己挑的色。
            // 之前这段逻辑散在 Program.cs 和 Pages2.cs 两处，只在「切肤 / 空值」时触发，
            // 用户改过主题色再切回玻璃皮肤就会得到「蓝底 + 青绿按钮」的撞色界面。
            string accent = "";
            if (skin == Skin.Liquid) accent = "#2F6BFF";
            else if (skin == Skin.Millennium) accent = "#1C8FD6";
            if (accent.Length > 0)
            {
                Accent = ParseColor(accent, Accent);
                Accent2 = Lighten(Mix(Accent, Color.FromArgb(96, 156, 255), 0.58f), 0.04f);
                Accent3 = Lighten(Mix(Accent, Color.FromArgb(168, 210, 255), 0.60f), 0.06f);
            }
            InvalidateWallpaper();
        }

        /// <summary>兼容旧调用点：true = 液态玻璃，false = 纯色</summary>
        public static void SetMode(bool liquid)
        {
            SetSkin(liquid ? Skin.Liquid : Skin.Solid);
        }

        /// <summary>
        /// 主题切换后同步整棵控件树里那些「构造时把 Theme.X 缓存进 BackColor」的控件。
        /// 自绘控件画满整个矩形，缓存旧色看不出来；但 TextBox / ListBox 这类原生控件
        /// 是拿 BackColor 直接刷背景的，不换就会在蓝色页面里留一块旧的白色。
        /// </summary>
        public static void ApplySkin(Control root)
        {
            if (root == null) return;
            try
            {
                // 页面底色是「圆角外清底」的兜底色：按钮、下拉框的四个角
                // 就是拿它去填的，不跟着换会在蓝条纹上留一小块旧的白青色。
                if (root is PageBase) root.BackColor = Bg;

                LgCard card = root as LgCard;
                if (card != null)
                {
                    if (card.Dark)
                    {
                        // 深色卡不参与玻璃绘制，Fill / BorderColor 用不上。
                        // 这里只同步 BackColor（圆角外露出的那圈兜底色），
                        // 不要把浅色 Fill 写回去 —— 以后若有人让 PaintDarkSurface
                        // 用上 Fill，就会拿到一个浅色值。
                        card.BackColor = LogBg;
                    }
                    else
                    {
                        card.BackColor = GlassCard;
                        // Fill / BorderColor 是纯色皮肤画白卡用的（见 LgCard.PaintSurface）。
                        // 它们是在构造时缓存下来的，不跟着换的话，切到纯色会得到
                        // 「上一套皮肤颜色的卡片」压在平涂背景上。
                        card.Fill = GlassCard;
                        card.BorderColor = Border;
                    }
                }
                else
                {
                    LgList list = root as LgList;
                    if (list != null)
                    {
                        list.BackColor = (LiquidMode || MillenniumMode) ? GlassCard : Card;
                    }
                    else
                    {
                        LgInput input = root as LgInput;
                        if (input != null)
                        {
                            input.BackColor = Card;
                            input.ApplySkin();
                        }
                        else
                        {
                            LgTextBox box = root as LgTextBox;
                            if (box != null) box.ApplySkin();
                            else if (root is LgCombo) root.BackColor = Bg;
                        }
                    }
                }
                for (int i = 0; i < root.Controls.Count; i++)
                {
                    ApplySkin(root.Controls[i]);
                }
            }
            catch
            {
            }
        }

        private static bool refreshing;

        /// <summary>
        /// 通知整个窗口「背景换了」（换主题色 / 窗口尺寸变了）。
        /// 卡片的离屏表面靠 Theme.Revision 判断是否重建，栏靠重绘，
        /// 所以换色后必须整棵可视树失效一次，否则旧壁纸的光斑会留在屏幕上。
        /// </summary>
        public static void RefreshAll(Control root)
        {
            if (root == null || refreshing) return;
            refreshing = true;
            try
            {
                root.Invalidate(true);
                root.Update();
            }
            catch
            {
            }
            finally
            {
                refreshing = false;
            }
        }

        /// <summary>感知亮度，用于判断颜色放在白底上是否需要压暗</summary>
        public static int Luma(Color c)
        {
            return (int)(0.299 * c.R + 0.587 * c.G + 0.114 * c.B);
        }

        public static Color ParseColor(string hex, Color fallback)
        {
            try
            {
                if (string.IsNullOrEmpty(hex)) return fallback;
                string s = hex.Trim().TrimStart('#');
                if (s.Length != 6) return fallback;
                int r = Convert.ToInt32(s.Substring(0, 2), 16);
                int g = Convert.ToInt32(s.Substring(2, 2), 16);
                int b = Convert.ToInt32(s.Substring(4, 2), 16);
                return Color.FromArgb(r, g, b);
            }
            catch
            {
                return fallback;
            }
        }

        public static string ToHex(Color c)
        {
            return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
        }

        public static Color Mix(Color a, Color b, float ratio)
        {
            if (ratio < 0) ratio = 0;
            if (ratio > 1) ratio = 1;
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * ratio),
                (int)(a.G + (b.G - a.G) * ratio),
                (int)(a.B + (b.B - a.B) * ratio));
        }

        public static Color Lighten(Color c, float ratio)
        {
            return Mix(c, Color.White, ratio);
        }

        public static Color Darken(Color c, float ratio)
        {
            return Mix(c, Color.Black, ratio);
        }

        public static Color Alpha(Color c, int alpha)
        {
            if (alpha < 0) alpha = 0;
            if (alpha > 255) alpha = 255;
            return Color.FromArgb(alpha, c);
        }

        public static GraphicsPath Round(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                path.AddRectangle(new Rectangle(rect.X, rect.Y, Math.Max(1, rect.Width), Math.Max(1, rect.Height)));
                return path;
            }
            int max = Math.Min(rect.Width, rect.Height) / 2;
            if (radius > max) radius = max;
            if (radius <= 0)
            {
                path.AddRectangle(rect);
                return path;
            }
            int d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d - 1, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d - 1, rect.Bottom - d - 1, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d - 1, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRound(Graphics g, Rectangle rect, int radius, Color color)
        {
            using (GraphicsPath path = Round(rect, radius))
            {
                using (SolidBrush brush = new SolidBrush(color))
                {
                    g.FillPath(brush, path);
                }
            }
        }

        public static void DrawRound(Graphics g, Rectangle rect, int radius, Color color, float width)
        {
            using (GraphicsPath path = Round(rect, radius))
            {
                using (Pen pen = new Pen(color, width))
                {
                    g.DrawPath(pen, path);
                }
            }
        }

        public static LinearGradientBrush Gradient(Rectangle rect, Color a, Color b)
        {
            return Gradient(rect, a, b, 0f);
        }

        public static LinearGradientBrush Gradient(Rectangle rect, Color a, Color b, float angle)
        {
            Rectangle safe = rect;
            if (safe.Width <= 0) safe.Width = 1;
            if (safe.Height <= 0) safe.Height = 1;
            LinearGradientBrush brush = new LinearGradientBrush(safe, a, b, angle);
            return brush;
        }

        public static void DrawGlowBar(Graphics g, Rectangle rect, double phase)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return;
            using (LinearGradientBrush brush = Gradient(rect, Accent, Accent2, 0f))
            {
                ColorBlend blend = new ColorBlend(4);
                blend.Colors = new Color[] { Alpha(Accent, 0), Accent, Accent2, Alpha(Accent2, 0) };
                blend.Positions = new float[] { 0f, 0.35f, 0.65f, 1f };
                brush.InterpolationColors = blend;
                g.FillRectangle(brush, rect);
            }
        }

        public static void EnableQuality(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        }

        // ==================== 液态玻璃 ====================
        // WinForms 的子控件都是独立 HWND，没法像 CSS 的 backdrop-filter 那样采样桌面再模糊。
        // 这里用等效做法：整窗共用一张「柔光壁纸」位图，每个玻璃控件按自己在窗口里的位置，
        // 把壁纸对应区域原样画一遍，再叠加半透明白 + 顶部高光 + 底部积光 + 玻璃边。
        // 好处是跨卡片的光斑天然连续，看起来就是一块块磨砂玻璃压在同一张壁纸上。

        /// <summary>
        /// 玻璃薄膜的白色叠加浓度（0-255）。1.3.0 起降到 16（6%）。
        ///
        /// 这不是「再调淡一点」，而是性质变了：真正的折射由 <see cref="Liquid"/> 在下面一层做完，
        /// 边缘已经弯了；这一层如果还有 26% 白，会把刚做出来的折射形变重新糊平，
        /// 等于自己抵消自己。真实的玻璃中间几乎是透明的 —— 能看清背后的东西，
        /// 只是被压上一层极淡的雾。之前所有的「白板感」都来自这层雾太厚。
        /// </summary>
        public static int GlassAlpha = 16;

        /// <summary>主题版本号。换色 / 换壁纸时自增，卡片的离屏表面据此判断是否需要重建。</summary>
        public static int Revision = 0;

        private static Bitmap wallpaper;
        private static Size wallpaperSize;
        private static DateTime wallpaperAt = DateTime.MinValue;
        private static Bitmap frost;

        /// <summary>
        /// 壁纸重建的最小间隔（毫秒）。只在「尺寸变了」这条路径上生效，
        /// 拖拽缩放时 WM_SIZE 会连发几十次，每次都重画整张壁纸会明显卡顿。
        /// </summary>
        private const int WallpaperThrottleMs = 150;

        /// <summary>丢弃当前壁纸，下次绘制时重建。换主题色 / 换尺寸 / 换背景都走这里。</summary>
        public static void InvalidateWallpaper()
        {
            Revision++;
            if (wallpaper != null)
            {
                wallpaper.Dispose();
                wallpaper = null;
            }
            wallpaperSize = Size.Empty;
            // 时间戳归零：否则调用方紧接着请求同尺寸壁纸时，
            // 会命中下面的 150ms 节流分支、返回 null，界面在启动初期整片纯色。
            wallpaperAt = DateTime.MinValue;
        }

        // ==================== 自定义背景 ====================
        // 用户选的图片**顶替**生成出来的壁纸，三套皮肤共用同一张 ——
        // 也就是说「自定义背景」不是第四种皮肤，而是一层可替换的底。
        // 玻璃/泡泡/条纹照旧画在它上面（纯色皮肤除外，那边卡片本来是实底）。

        private static string customBackground = "";

        public static string CustomBackground
        {
            get { return customBackground; }
        }

        public static bool HasCustomBackground
        {
            get { return customBackground.Length > 0 && File.Exists(customBackground); }
        }

        /// <summary>设置自定义背景；传空串 = 回到皮肤自带的背景。会立刻重建壁纸。</summary>
        public static void SetCustomBackground(string path)
        {
            string want = path == null ? "" : path;
            if (string.Equals(want, customBackground, StringComparison.OrdinalIgnoreCase)) return;
            customBackground = want;
            InvalidateWallpaper();
        }

        private static Bitmap Wallpaper(Size size)
        {
            if (size.Width < 4 || size.Height < 4) return wallpaper;
            if (wallpaper != null && wallpaperSize == size) return wallpaper;
            // 节流只约束「已有壁纸但尺寸变了」这条路径；壁纸为 null 时必须立刻生成，
            // 否则第一帧和换主题后的一段时间里玻璃下面什么都没有。
            if (wallpaper != null && (DateTime.Now - wallpaperAt).TotalMilliseconds < WallpaperThrottleMs)
            {
                return wallpaper;
            }
            wallpaperAt = DateTime.Now;
            Revision++;

            Bitmap bmp = new Bitmap(size.Width, size.Height);
            bool ok = false;
            try
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    EnableQuality(g);
                    Rectangle rect = new Rectangle(0, 0, size.Width, size.Height);

                    if (HasCustomBackground)
                    {
                        CustomBackdrop(g, size);
                        ok = true;
                    }
                    else if (LiquidMode)
                    {
                        // 液态玻璃的默认背景：优先用内嵌的日落草原壁纸，
                        // 读不到（资源被打包工具剥离 / 异常）才退回程序画的蓝白条纹。
                        if (!PaintDefaultWallpaper(g, size))
                        {
                            LiquidBackdrop(g, size);
                        }
                        ok = true;
                    }
                    else if (MillenniumMode)
                    {
                        MillenniumBackdrop(g, size);
                        ok = true;
                    }
                    else
                    {
                        // 纯色：一张平涂的底。不渐变、不光斑、不纹理 ——
                        // 「纯色」这个皮肤的全部意思就是「什么都别加」。
                        using (SolidBrush brush = new SolidBrush(Bg))
                        {
                            g.FillRectangle(brush, rect);
                        }
                        ok = true;
                    }
                }
                ok = true;
            }
            catch
            {
                ok = false;
            }

            // 生成失败时不要留下半张空白位图：宁可返回 null 让调用方走纯色兜底，
            // 也不要拿着一块「全白」的壁纸去叠玻璃——那正是玻璃看不见的样子。
            if (!ok)
            {
                bmp.Dispose();
                return null;
            }

            if (wallpaper != null)
            {
                wallpaper.Dispose();
            }
            wallpaper = bmp;
            wallpaperSize = size;
            return wallpaper;
        }

        /// <summary>
        /// 液态玻璃主题的背景。
        ///
        /// 参考图的信息量几乎全在背景上：一半纯白、一半**蓝白斜条纹**，
        /// 胶囊玻璃压在条纹上，条纹在边缘被推开、在中间被压成淡蓝。
        /// 所以这里不是把经典主题的青色光斑换个颜色，而是真的画一层结构：
        ///   1. 冰蓝底（上浅下深，仍有 50 个色阶落差）
        ///   2. 45° 蓝白斜条纹 —— 折射要有东西可弯，条纹就是那东西
        ///   3. 几团大光斑，让条纹不是均匀铺满，玻璃压上去才有明暗变化
        ///   4. 左上角一层白，保证标题和侧栏的深色文字压得住
        /// </summary>
        private static void LiquidBackdrop(Graphics g, Size size)
        {
            Rectangle rect = new Rectangle(0, 0, size.Width, size.Height);

            // 底色从「近白 → 淡蓝」改成「中亮 → 饱和蓝」。
            // 原来上端是 250,253,255，等于一张白纸，条纹压上去只剩几个色阶，
            // 整屏被读成「淡蓝渐变」而不是参考图里的蓝白条纹布。
            using (LinearGradientBrush brush = Gradient(rect,
                Color.FromArgb(238, 247, 255), Color.FromArgb(142, 180, 255), 112f))
            {
                g.FillRectangle(brush, rect);
            }

            // 条纹周期按窗口宽度取（1/8，上限 168）：参考图的条纹更细密，
            // 1/7 + 240 上限在 1130 宽的窗口上得到 161px 的粗条，像色块不像布纹。
            int period = Math.Max(88, Math.Min(168, size.Width / 8));
            int band = period / 2;
            // 浓度 150/78 → 190/96：截图实测这个值下条纹才有「布」的清晰边缘，
            // 150 的时候它读起来是一层云雾。底色上端也抬回 250，让亮条更白。
            StripeField(g, size, Color.FromArgb(28, 84, 236), 190, period, band, 45f, 0f);
            // 第二遍错位半个周期、更淡：得到粗细相间的条纹，比等宽条纹更像参考图里的布纹
            StripeField(g, size, Color.FromArgb(96, 152, 255), 96, period, band, 45f, period * 0.5f);

            Blob(g, size, 0.10f, 0.02f, 0.80f, 205, Color.FromArgb(120, 255, 255, 255));
            Blob(g, size, 1.02f, 0.28f, 0.72f, 205, Alpha(Accent, 120));
            Blob(g, size, 0.62f, 1.06f, 0.66f, 180, Alpha(Accent3, 110));

            // 左上角压白：标题、侧栏文字都在这一带，直接在蓝条纹上放深色字会花。
            // 用径向光斑而不是整幅线性渐变 —— 线性会把右侧的条纹一起洗掉，
            // 卡片压上去就没有结构可弯了（这是前几版「玻璃看不见」的老毛病）。
            // 132 → 96：压太狠会把左上角的条纹也抹平，那里正是侧栏和标题栏所在。
            Blob(g, size, 0.06f, 0.00f, 0.92f, 205, Color.FromArgb(96, 255, 255, 255));
            // 整幅白纱 46 → 22：这层是「洗掉条纹」的主力，留一点点只为压噪点。
            using (LinearGradientBrush wash = new LinearGradientBrush(
                new Rectangle(0, 0, Math.Max(2, size.Width), Math.Max(2, size.Height)),
                Color.FromArgb(22, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 62f))
            {
                g.FillRectangle(wash, rect);
            }

            // 不叠 MicroTexture：那层 19px、32° 的细纹和 45° 条纹不是同方向，
            // 叠在一起会互相干涉，截图里条纹「糊成云雾」有一半是它造成的。
            // 条纹本身就是折射所需要的结构，不必再加噪点。

            // 四角轻轻压深：玻璃卡片要「浮」起来，背景不能是一张平板
            using (LinearGradientBrush shade = new LinearGradientBrush(
                new Rectangle(0, 0, Math.Max(2, size.Width), Math.Max(2, size.Height)),
                Color.FromArgb(18, 18, 54, 120), Color.FromArgb(46, 12, 40, 100), LinearGradientMode.ForwardDiagonal))
            {
                g.FillRectangle(shade, rect);
            }
        }

        /// <summary>
        /// 斜条纹场。用 TextureBrush 旋转而不是手画平行四边形：
        /// 平铺无缝、任意角度都连续，也不受窗口尺寸影响。
        /// </summary>
        private static void StripeField(Graphics g, Size size, Color color, int alpha, int period, int width, float angle, float shift)
        {
            if (period < 8 || width < 1 || alpha <= 0) return;
            try
            {
                using (Bitmap tile = new Bitmap(period, period))
                {
                    using (Graphics tg = Graphics.FromImage(tile))
                    {
                        tg.Clear(Color.Transparent);
                        using (SolidBrush brush = new SolidBrush(Color.FromArgb(alpha, color)))
                        {
                            tg.FillRectangle(brush, 0, 0, period, width);
                        }
                    }
                    using (TextureBrush brush = new TextureBrush(tile, WrapMode.Tile))
                    {
                        brush.RotateTransform(angle);
                        brush.TranslateTransform(-size.Width * 0.12f + shift, -size.Height * 0.12f + shift);
                        g.FillRectangle(brush, new Rectangle(0, 0, size.Width, size.Height));
                    }
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// 千禧年主题的背景。
        ///
        /// 那个年代的界面语言（Win XP 的 Luna、Vista 的 Aero、Mac 的 Aqua）有三个硬指标：
        /// 高饱和天光渐变、成群的半透明玻璃泡泡、一道横贯画面的镜面反光。
        /// 所以这里不是把条纹换个颜色，而是把这三样都画出来：
        ///   1. 上亮下深的湖蓝天光（底边压到 96,168,232，深色状态栏文字才压得住）
        ///   2. 一道 18° 的斜向镜面光带 —— 千禧年的「塑料感」一半来自它
        ///   3. 四颗大小不一的玻璃泡泡（内部发白、边缘亮圈、左上一点高光）
        ///   4. 磨砂颗粒，让渐变不至于像一整片纯色
        /// </summary>
        private static void MillenniumBackdrop(Graphics g, Size size)
        {
            Rectangle rect = new Rectangle(0, 0, size.Width, size.Height);

            using (LinearGradientBrush brush = Gradient(rect,
                Color.FromArgb(250, 254, 255), Color.FromArgb(96, 168, 232), 96f))
            {
                ColorBlend blend = new ColorBlend(3);
                blend.Colors = new Color[]
                {
                    Color.FromArgb(252, 254, 255),
                    Color.FromArgb(176, 224, 252),
                    Color.FromArgb(96, 168, 232)
                };
                blend.Positions = new float[] { 0f, 0.44f, 1f };
                brush.InterpolationColors = blend;
                g.FillRectangle(brush, rect);
            }

            // 镜面光带：从左上到右下的一条宽带，两层叠出「芯亮边淡」
            try
            {
                using (LinearGradientBrush shine = new LinearGradientBrush(
                    new Rectangle(0, 0, Math.Max(2, size.Width), Math.Max(2, size.Height)),
                    Color.FromArgb(150, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 18f))
                {
                    ColorBlend blend = new ColorBlend(3);
                    blend.Colors = new Color[]
                    {
                        Color.FromArgb(0, 255, 255, 255),
                        Color.FromArgb(132, 255, 255, 255),
                        Color.FromArgb(0, 255, 255, 255)
                    };
                    blend.Positions = new float[] { 0.18f, 0.42f, 0.72f };
                    shine.InterpolationColors = blend;
                    g.FillRectangle(shine, rect);
                }
                // 一道更细更亮的芯
                using (LinearGradientBrush core = new LinearGradientBrush(
                    new Rectangle(0, 0, Math.Max(2, size.Width), Math.Max(2, size.Height)),
                    Color.FromArgb(0, 255, 255, 255), Color.FromArgb(84, 255, 255, 255), 18f))
                {
                    ColorBlend blend = new ColorBlend(3);
                    blend.Colors = new Color[]
                    {
                        Color.FromArgb(0, 255, 255, 255),
                        Color.FromArgb(78, 255, 255, 255),
                        Color.FromArgb(0, 255, 255, 255)
                    };
                    blend.Positions = new float[] { 0.34f, 0.46f, 0.60f };
                    core.InterpolationColors = blend;
                    g.FillRectangle(core, rect);
                }
            }
            catch
            {
                // 光带画不出来不影响底色，绝不能让它打断整张壁纸
            }

            // 玻璃泡泡：位置刻意避开左上（标题、侧栏文字都在那一带）
            Bubble(g, size, 0.78f, 0.20f, 0.150f, 150);
            Bubble(g, size, 0.93f, 0.52f, 0.088f, 120);
            Bubble(g, size, 0.62f, 0.80f, 0.115f, 108);
            Bubble(g, size, 0.40f, 0.36f, 0.060f, 96);

            // 顶部再压一层白：标题栏是玻璃条，底下太蓝会显得脏
            using (LinearGradientBrush top = new LinearGradientBrush(
                new Rectangle(0, 0, Math.Max(2, size.Width), Math.Max(2, size.Height / 3)),
                Color.FromArgb(96, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Vertical))
            {
                g.FillRectangle(top, new Rectangle(0, 0, size.Width, Math.Max(2, size.Height / 3)));
            }

            MicroTexture(g, size);

            // 四角压深，玻璃卡才浮得起来
            using (LinearGradientBrush shade = new LinearGradientBrush(
                new Rectangle(0, 0, Math.Max(2, size.Width), Math.Max(2, size.Height)),
                Color.FromArgb(16, 12, 52, 96), Color.FromArgb(48, 8, 40, 84), LinearGradientMode.ForwardDiagonal))
            {
                g.FillRectangle(shade, rect);
            }
        }

        /// <summary>
        /// 一颗玻璃泡泡。千禧年背景的灵魂，画法只有三步：
        /// 内部径向发白（PathGradientBrush，中心亮四周透明）、
        /// 边缘一圈亮线、左上角一点硬高光。
        /// </summary>
        private static void Bubble(Graphics g, Size size, float cx, float cy, float radiusRatio, int alpha)
        {
            double diag = Math.Sqrt((double)size.Width * size.Width + (double)size.Height * size.Height);
            int r = (int)(diag * radiusRatio);
            if (r < 8 || alpha <= 0) return;
            Rectangle box = new Rectangle((int)(size.Width * cx) - r, (int)(size.Height * cy) - r, r * 2, r * 2);
            try
            {
                using (GraphicsPath path = new GraphicsPath())
                {
                    path.AddEllipse(box);
                    using (PathGradientBrush brush = new PathGradientBrush(path))
                    {
                        brush.CenterColor = Color.FromArgb(Math.Min(255, alpha), 255, 255, 255);
                        PointF[] points = path.PathPoints;
                        Color[] surround = new Color[points.Length];
                        for (int i = 0; i < surround.Length; i++) surround[i] = Color.FromArgb(0, 255, 255, 255);
                        brush.SurroundColors = surround;
                        try
                        {
                            brush.FocusScales = new PointF(0.62f, 0.62f);
                        }
                        catch
                        {
                        }
                        g.FillPath(brush, path);
                    }
                }

                float penWidth = Math.Max(1f, r * 0.045f);
                using (Pen edge = new Pen(Color.FromArgb(Math.Min(255, alpha * 3 / 2), 255, 255, 255), penWidth))
                {
                    g.DrawEllipse(edge, box);
                }
                // 左上高光：一颗小椭圆，不是圆 —— 圆点像贴纸，椭圆才像反光
                int hw = (int)(r * 0.42f);
                int hh = (int)(r * 0.26f);
                if (hw > 2 && hh > 2)
                {
                    using (SolidBrush spec = new SolidBrush(Color.FromArgb(Math.Min(255, alpha + 60), 255, 255, 255)))
                    {
                        g.FillEllipse(spec, box.X + (int)(r * 0.42f), box.Y + (int)(r * 0.30f), hw, hh);
                    }
                }
            }
            catch
            {
                // 单颗泡泡失败就跳过，背景整体不受影响
            }
        }

        /// <summary>
        /// 用户自选的背景图。
        ///
        /// 按「铺满并裁切」（cover）缩放：宁可裁掉两侧，也不留黑边、不拉伸变形 ——
        /// 拉伸过的照片一眼就能看出来，比裁切难看得多。
        /// 上面再压一层白纱：用户可能选深色照片，而三套皮肤的文字都是深色，
        /// 不压白纱就会出现「换了张好看的壁纸，字全看不见了」。
        /// </summary>
        private static void CustomBackdrop(Graphics g, Size size)
        {
            Rectangle rect = new Rectangle(0, 0, size.Width, size.Height);
            bool drawn = false;
            try
            {
                // 用 FileStream 读，读完立刻释放：直接 Image.FromFile 会把文件锁住，
                // 用户之后就删不掉自己刚选的那张图了。
                using (FileStream fs = new FileStream(customBackground, FileMode.Open, FileAccess.Read))
                {
                    using (Image img = Image.FromStream(fs))
                    {
                        float sw = (float)img.Width;
                        float sh = (float)img.Height;
                        if (sw > 0f && sh > 0f)
                        {
                            float scale = Math.Max(size.Width / sw, size.Height / sh);
                            int dw = (int)(sw * scale + 0.5f);
                            int dh = (int)(sh * scale + 0.5f);
                            int dx = (size.Width - dw) / 2;
                            int dy = (size.Height - dh) / 2;
                            g.DrawImage(img, new Rectangle(dx, dy, dw, dh));
                            drawn = true;
                        }
                    }
                }
            }
            catch
            {
                drawn = false;
            }
            if (!drawn)
            {
                using (SolidBrush brush = new SolidBrush(Bg))
                {
                    g.FillRectangle(brush, rect);
                }
            }

            // 整体白纱 + 顶部加厚：保证深色文字在任何照片上都读得出来
            using (SolidBrush scrim = new SolidBrush(Color.FromArgb(46, 255, 255, 255)))
            {
                g.FillRectangle(scrim, rect);
            }
            Rectangle topArea = new Rectangle(0, 0, size.Width, Math.Max(2, size.Height * 2 / 5));
            using (LinearGradientBrush top = new LinearGradientBrush(topArea,
                Color.FromArgb(120, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Vertical))
            {
                g.FillRectangle(top, topArea);
            }
        }

        /// <summary>
        /// 液态玻璃默认背景：内嵌的日落草原壁纸。
        ///
        /// 这张图和「自定义背景」走同一套处理 —— cover 缩放 + 白纱 + 顶部渐隐，
        /// 保证深色文字在任何亮度上都读得出来。区别只在于图片来源是内嵌资源，
        /// 而不是用户磁盘上的文件。
        /// </summary>
        private static bool PaintDefaultWallpaper(Graphics g, Size size)
        {
            Image img = null;
            try
            {
                using (Stream stream = LoadDefaultWallpaper())
                {
                    if (stream == null) return false;
                    img = Image.FromStream(stream);
                }
                if (img == null) return false;
                using (img)
                {
                    DrawCover(g, size, img);
                }
                PaintScrim(g, size);
                return true;
            }
            catch
            {
                if (img != null) img.Dispose();
                return false;
            }
        }

        /// <summary>
        /// 从 exe 内嵌资源里读默认壁纸。资源名要和 build.bat 的 /resource 参数里
        /// 指定的逻辑名一致。读不到返回 null，调用方退回程序画的条纹背景。
        /// </summary>
        private static Stream LoadDefaultWallpaper()
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                // 先按固定资源名找，找不到再扫一遍「名字以 default_wallpaper 结尾」的资源，
                // 这样哪怕打包工具改了命名空间前缀也能命中。
                Stream s = asm.GetManifestResourceStream(DefaultWallpaperResource);
                if (s != null) return s;

                string[] names = asm.GetManifestResourceNames();
                for (int i = 0; i < names.Length; i++)
                {
                    if (names[i].EndsWith("default_wallpaper.jpg", StringComparison.OrdinalIgnoreCase))
                    {
                        return asm.GetManifestResourceStream(names[i]);
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        private const string DefaultWallpaperResource = "Liuguang.default_wallpaper.jpg";

        /// <summary>cover 缩放：铺满并裁切，不留黑边、不拉伸变形。</summary>
        private static void DrawCover(Graphics g, Size size, Image img)
        {
            Rectangle rect = new Rectangle(0, 0, size.Width, size.Height);
            float sw = (float)img.Width;
            float sh = (float)img.Height;
            if (sw <= 0f || sh <= 0f)
            {
                g.DrawImage(img, rect);
                return;
            }
            float scale = Math.Max(size.Width / sw, size.Height / sh);
            int dw = (int)(sw * scale + 0.5f);
            int dh = (int)(sh * scale + 0.5f);
            int dx = (size.Width - dw) / 2;
            int dy = (size.Height - dh) / 2;
            g.DrawImage(img, new Rectangle(dx, dy, dw, dh));
        }

        /// <summary>白纱 + 顶部加厚渐隐：深色文字在任何照片上都读得出来。</summary>
        private static void PaintScrim(Graphics g, Size size)
        {
            Rectangle rect = new Rectangle(0, 0, size.Width, size.Height);
            using (SolidBrush scrim = new SolidBrush(Color.FromArgb(46, 255, 255, 255)))
            {
                g.FillRectangle(scrim, rect);
            }
            Rectangle topArea = new Rectangle(0, 0, size.Width, Math.Max(2, size.Height * 2 / 5));
            using (LinearGradientBrush top = new LinearGradientBrush(topArea,
                Color.FromArgb(120, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Vertical))
            {
                g.FillRectangle(top, topArea);
            }
        }

        /// <summary>
        /// 一团径向光斑。<paramref name="radius"/> 是相对位图对角线的半径比例
        /// （不是长边，这样宽窗口上不会被拉成横条）；<paramref name="focus"/> 是衰减指数，
        /// 越大中心越集中、越像一团「光」，越小越像一层均匀的雾。
        /// </summary>
        private static void Blob(Graphics g, Size size, float cx, float cy, float radius, float focus, Color color)
        {
            double diag = Math.Sqrt((double)size.Width * size.Width + (double)size.Height * size.Height);
            int r = (int)(diag * radius);
            if (r < 8 || color.A <= 0) return;
            Rectangle rect = new Rectangle((int)(size.Width * cx) - r, (int)(size.Height * cy) - r, r * 2, r * 2);
            try
            {
                using (GraphicsPath path = new GraphicsPath())
                {
                    path.AddEllipse(rect);
                    using (PathGradientBrush brush = new PathGradientBrush(path))
                    {
                        brush.CenterColor = color;
                        // SurroundColors 的数组长度必须与路径点数一致，否则 GDI+ 会抛参数异常
                        PointF[] points = path.PathPoints;
                        Color[] surround = new Color[points.Length];
                        for (int i = 0; i < surround.Length; i++) surround[i] = Color.FromArgb(0, color);
                        brush.SurroundColors = surround;
                        if (focus > 0f)
                        {
                            // 默认的线性衰减会让光斑边界明显，用指数去「收」一下中心，
                            // 边缘才会像光一样自然散开。
                            try
                            {
                                brush.FocusScales = new PointF(Math.Max(0f, Math.Min(1f, 1f - 1f / focus)),
                                                               Math.Max(0f, Math.Min(1f, 1f - 1f / focus)));
                            }
                            catch
                            {
                                // FocusScales 对部分 GDI+ 版本可能不接受极端值，退化为默认衰减
                            }
                        }
                        g.FillPath(brush, path);
                    }
                }
            }
            catch
            {
                // 单团光斑画失败不该影响整张壁纸
            }
        }

        /// <summary>折射引擎从这里取整窗壁纸（内部使用）</summary>
        public static Bitmap BackdropImage(Control c)
        {
            if (c == null) return null;
            return Wallpaper(FormSize(c));
        }

        /// <summary>
        /// 壁纸的微观结构：一层 19px 周期的斜向宽带。
        ///
        /// 这是液态玻璃能被「看见」的前提。折射是纯几何位移，它不改变对比度，
        /// 所以如果背后是一片平滑渐变，位移多少都不产生视觉差异——
        /// 这正是之前几版调白度、调光斑全都无效的深层原因：没有可被弯折的结构。
        /// 加上这层宽带之后，边缘 30px 的位移会把纹路错开一个多周期，
        /// 出现明显的「水波状扭曲」，人眼立刻读成「透过一层弯曲的介质在看背景」。
        ///
        /// 两个数值是实测出来的，不是猜的：
        ///  - 周期必须够大（19px）。7px 的细线在模糊之后会被完全抹平，位移就白做了。
        ///  - 幅度必须够淡（±6 色阶）。11 色阶时整页看起来像条纹墙纸，
        ///    6 色阶在页面上只是「一点亚麻质感」，但在玻璃里被扭曲之后仍然清楚可辨。
        /// 用 TextureBrush + RotateTransform 而不是手画斜线，纹路才是连续的、没有接缝。
        /// </summary>
        private static void MicroTexture(Graphics g, Size size)
        {
            try
            {
                using (Bitmap tile = new Bitmap(19, 19))
                {
                    using (Graphics tg = Graphics.FromImage(tile))
                    {
                        tg.Clear(Color.Transparent);
                        using (SolidBrush light = new SolidBrush(Color.FromArgb(6, 255, 255, 255)))
                        {
                            tg.FillRectangle(light, 0, 5, 19, 7);
                        }
                        using (SolidBrush dark = new SolidBrush(Color.FromArgb(5, 24, 74, 92)))
                        {
                            tg.FillRectangle(dark, 0, 14, 19, 4);
                        }
                    }
                    using (TextureBrush brush = new TextureBrush(tile, WrapMode.Tile))
                    {
                        // 斜 32°：斜纹在竖直边缘和水平边缘上都有足够的切向分量，
                        // 两个方向的折射都能把纹路错开，效果比横平竖直明显得多。
                        brush.RotateTransform(32f);
                        brush.TranslateTransform(-size.Width * 0.1f, -size.Height * 0.1f);
                        g.FillRectangle(brush, new Rectangle(0, 0, size.Width, size.Height));
                    }
                }
            }
            catch
            {
                // 纹理只是增强项，画不出来不该影响整张壁纸
            }
        }

        /// <summary>控件左上角在窗体客户区里的坐标（用于对齐同一张壁纸）</summary>
        public static Point FormOrigin(Control c, Point clientPoint)
        {
            if (c == null) return clientPoint;
            Point screen = c.PointToScreen(clientPoint);
            Control root = c;
            while (root.Parent != null) root = root.Parent;
            Point zero = root.PointToScreen(Point.Empty);
            return new Point(screen.X - zero.X, screen.Y - zero.Y);
        }

        public static Size FormSize(Control c)
        {
            if (c == null) return Size.Empty;
            Control root = c;
            while (root.Parent != null) root = root.Parent;
            return root.ClientSize;
        }

        /// <summary>把壁纸对应区域铺到控件上（不透明，但跨控件是连续的）</summary>
        public static void PaintBackdrop(Control c, Graphics g, Rectangle rect)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return;
            Bitmap bmp = Wallpaper(FormSize(c));
            // 壁纸取不到（尺寸异常 / 生成失败）时才退回纯色；
            // 以前是无条件先铺一层 Bg，等于玻璃底下永远是同一个颜色。
            if (bmp == null)
            {
                using (SolidBrush fill = new SolidBrush(Bg))
                {
                    g.FillRectangle(fill, rect);
                }
                return;
            }

            Point origin = FormOrigin(c, rect.Location);
            int sx = Math.Max(0, origin.X);
            int sy = Math.Max(0, origin.Y);
            int sw = Math.Min(bmp.Width - sx, rect.Width - (sx - origin.X));
            int sh = Math.Min(bmp.Height - sy, rect.Height - (sy - origin.Y));
            if (sw <= 0 || sh <= 0) return;

            Rectangle dest = new Rectangle(rect.X + (sx - origin.X), rect.Y + (sy - origin.Y), sw, sh);
            try
            {
                g.DrawImage(bmp, dest, new Rectangle(sx, sy, sw, sh), GraphicsUnit.Pixel);
            }
            catch
            {
                using (SolidBrush fill = new SolidBrush(Bg))
                {
                    g.FillRectangle(fill, rect);
                }
            }
        }

        /// <summary>
        /// 磨砂颗粒。真玻璃后面的东西是「糊」的，纯白渐变是「平」的，
        /// 这一层 1px 噪点就是两者观感的差别——成本只有一张 64×64 的图，铺满整窗。
        ///
        /// 原来 alpha 只有 5~18，压在 255 的透明白下面，肉眼几乎看不见，等于没画。
        /// 现在白点提到 26~54、暗点提到 16~34，并且铺两层（2×2 平铺 + 错位半周期），
        /// 才是一次真正看得见的「磨砂」。
        /// </summary>
        private static Bitmap Frost()
        {
            if (frost != null) return frost;
            Bitmap bmp = new Bitmap(64, 64);
            Random rnd = new Random(20260215);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                for (int i = 0; i < 2600; i++)
                {
                    int x = rnd.Next(64);
                    int y = rnd.Next(64);
                    bool light = rnd.Next(3) != 0;
                    int a = light ? 12 + rnd.Next(17) : 9 + rnd.Next(12);
                    using (SolidBrush brush = new SolidBrush(light
                        ? Color.FromArgb(a, 255, 255, 255)
                        : Color.FromArgb(a, 22, 60, 70)))
                    {
                        g.FillRectangle(brush, x, y, 1, 1);
                    }
                }
            }
            frost = bmp;
            return frost;
        }

        /// <summary>
        /// 在折射层之上叠玻璃薄膜。
        ///
        /// 注意这里已经不是「液态玻璃」的主体了 —— 主体是 <see cref="Liquid"/> 在下面做的
        /// 边缘折射位移。这一层只负责三件玻璃本体的事：
        ///   1. 一层极淡的雾（16%），表明「这是玻璃，不是空气」
        ///   2. 表面质感：磨砂颗粒 + 斜向反光带
        ///   3. 边缘：外轮廓暗边（厚度）+ 内侧亮环（边缘聚光）+ 顶光亮线（光从上来）
        ///
        /// 1.3.0 删掉了之前的「折射暗区」（一段中下部压暗的渐变）。
        /// 那是为了在没有折射的情况下伪造「玻璃有暗面」，属于用颜色冒充几何。
        /// 现在边缘真的在弯，背景自己会产生明暗偏移，再压一层暗只会把折射糊掉。
        /// </summary>
        public static void PaintGlass(Graphics g, Rectangle rect, int radius, int alpha)
        {
            if (rect.Width <= 2 || rect.Height <= 2) return;
            bool liq = LiquidMode;
            // 千禧年的玻璃在「厚度」上和液态玻璃同档（都是厚玻璃），
            // 只是颜色更青、折射更弱，所以高光类参数直接复用 liq 的那一档。
            bool mil = MillenniumMode;
            bool thick = liq || mil;
            using (GraphicsPath path = Round(rect, radius))
            {
                // 1) 雾：玻璃本体的乳白。经典主题只有 6%，液态玻璃主题 29%——
                //    后者底下压着高对比条纹，雾厚一点才像「一块厚玻璃」而不是「一张透明膜」。
                if (alpha > 0)
                {
                    using (SolidBrush brush = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255)))
                    {
                        g.FillPath(brush, path);
                    }
                }

                // 2) 冷色薄膜：纯色主题不要，液态玻璃偏蓝，千禧年偏青蓝
                //    ——三种玻璃的「色温」不一样，这是它们看起来是三种材质的关键。
                //    透明化改造：冷色膜从 30 降到 16，否则这层蓝会把背景色温整个盖掉，
                //    卡片看起来像蒙了一层蓝纱而不是透亮玻璃。
                if (thick)
                {
                    using (SolidBrush tint = new SolidBrush(mil
                        ? Color.FromArgb(22, 150, 214, 255)
                        : Color.FromArgb(16, 138, 182, 255)))
                    {
                        g.FillPath(tint, path);
                    }
                }

                Region old = g.Clip;
                try
                {
                    g.SetClip(path, CombineMode.Intersect);

                    // 3) 磨砂颗粒已移除。之前的做法是在玻璃上铺两层 64×64 的随机噪点，
                    //    配合折射引擎的液化波，叠在照片壁纸上就是用户说的「满屏噪点」。
                    //    真玻璃的「糊」应该由折射引擎里的 box blur 提供，而不是噪点；
                    //    噪点是磨砂塑料的手法，不是液态玻璃。

                    // 4) 斜向光带：静态的液面反光。少了这一笔，玻璃看起来是平的
                    using (LinearGradientBrush streak = new LinearGradientBrush(
                        new Rectangle(rect.X, rect.Y, Math.Max(2, rect.Width), Math.Max(2, rect.Height)),
                        Color.FromArgb(0, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 52f))
                    {
                        ColorBlend blend = new ColorBlend(4);
                        blend.Colors = new Color[]
                        {
                            Color.FromArgb(0, 255, 255, 255),
                            Color.FromArgb(thick ? 42 : 44, 255, 255, 255),
                            Color.FromArgb(thick ? 12 : 12, 255, 255, 255),
                            Color.FromArgb(0, 255, 255, 255)
                        };
                        blend.Positions = new float[] { 0f, 0.30f, 0.52f, 1f };
                        try
                        {
                            streak.InterpolationColors = blend;
                        }
                        catch
                        {
                        }
                        g.FillRectangle(streak, rect);
                    }

                    // 5) 顶部高光：光从上方来。透明化改造：从 46 压到 24，
                    //    高光太重会把玻璃中间整个洗白，读作「白板」而不是「透亮」。
                    int half = Math.Max(2, rect.Height / 2);
                    Rectangle top = new Rectangle(rect.X, rect.Y, rect.Width, half);
                    using (LinearGradientBrush brush = new LinearGradientBrush(top,
                        Color.FromArgb(liq ? 24 : 28, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Vertical))
                    {
                        g.FillRectangle(brush, top);
                    }

                    // 6) 底部积光：液体在底部会积一层更亮的光。透明化改造：58 压到 30，
                    //    底部积光太重会让卡片下半截发白，失去「透」的观感。
                    int lowHeight = Math.Max(8, rect.Height / 5);
                    Rectangle low = new Rectangle(rect.X, rect.Bottom - lowHeight, rect.Width, lowHeight);
                    using (LinearGradientBrush brush = new LinearGradientBrush(low,
                        Color.FromArgb(0, 255, 255, 255), Color.FromArgb(liq ? 30 : 50, 255, 255, 255), LinearGradientMode.Vertical))
                    {
                        g.FillRectangle(brush, low);
                    }

                    // 7) 内侧冷色边缘：光穿过玻璃边缘时会被染上一点青 / 蓝
                    int rimHeight = Math.Max(4, rect.Height / 8);
                    Rectangle rim = new Rectangle(rect.X, rect.Y, rect.Width, rimHeight);
                    using (LinearGradientBrush brush = new LinearGradientBrush(rim,
                        liq ? Color.FromArgb(38, 84, 142, 255) : Color.FromArgb(26, 92, 178, 190),
                        liq ? Color.FromArgb(0, 84, 142, 255) : Color.FromArgb(0, 92, 178, 190),
                        LinearGradientMode.Vertical))
                    {
                        g.FillRectangle(brush, rim);
                    }
                }
                finally
                {
                    // 必须放回原来的裁剪区：裁剪没恢复，后续所有绘制都会被切在这条路径里，
                    // 表现为「卡片以外的内容整块消失」。
                    g.Clip = old;
                    if (old != null) old.Dispose();
                }

                // 8) 边缘聚光环：iOS 液态玻璃最显眼的特征。
                //    用 4px 宽的笔沿圆角路径画、再裁掉外侧，只留指向内侧的 2px。
                //    视觉上是「玻璃边缘把光挤成了更亮的一圈」，比单纯描一条线厚得多。
                //    液态玻璃主题把这一圈加宽到 5px、提到 100/255：参考图里胶囊两端那两道
                //    亮白弧线，就是这一层 + 折射共同挤出来的。
                Region rimOld = g.Clip;
                try
                {
                    g.SetClip(path, CombineMode.Intersect);
                    // 聚光环同样改成上亮下暗：整圈同亮度会让四条边等重，
                    // 看上去像一个白框。光是从上面来的，下沿只该有一点环境反射。
                    using (LinearGradientBrush rimBrush = new LinearGradientBrush(
                        new Rectangle(rect.X, rect.Y, Math.Max(1, rect.Width), Math.Max(2, rect.Height)),
                        Color.FromArgb(liq ? 84 : 56, 255, 255, 255),
                        Color.FromArgb(liq ? 20 : 16, 255, 255, 255), LinearGradientMode.Vertical))
                    {
                        using (Pen glow = new Pen(rimBrush, liq ? 5f : 4f))
                        {
                            g.DrawPath(glow, path);
                        }
                    }
                }
                finally
                {
                    g.Clip = rimOld;
                    if (rimOld != null) rimOld.Dispose();
                }

                // 9) 外轮廓：暗边 = 玻璃的厚度边界。液态玻璃是蓝底，暗边要带蓝，
                //    纯黑描边在蓝白条纹上会显得脏。
                using (Pen outer = new Pen(liq ? Color.FromArgb(72, 26, 62, 140) : Color.FromArgb(88, Shadow), 1f))
                {
                    g.DrawPath(outer, path);
                }

                Rectangle inside = new Rectangle(rect.X + 1, rect.Y + 1, rect.Width - 2, rect.Height - 2);
                if (inside.Width > 6 && inside.Height > 6)
                {
                    // 内侧白线：边缘反光。
                    // 原来是整圈同一个 215 —— 在浅蓝背景上读起来就是「描了一圈白边」，
                    // 像贴纸的裁切线，把折射挤出来的弯折全盖住了。
                    // 真实玻璃的边缘是「上亮下暗」：光从上方来，下沿只能反射一点环境光。
                    // 所以这里改用垂直渐变笔，顶部 240、底部 70。
                    using (GraphicsPath innerPath = Round(inside, Math.Max(2, radius - 1)))
                    {
                        Color edgeTop = liq ? Color.FromArgb(170, 255, 255, 255) : Color.FromArgb(225, 255, 255, 255);
                        Color edgeBottom = liq ? Color.FromArgb(46, 255, 255, 255) : Color.FromArgb(120, 255, 255, 255);
                        using (LinearGradientBrush edge = new LinearGradientBrush(
                            new Rectangle(inside.X, inside.Y, Math.Max(1, inside.Width), Math.Max(2, inside.Height)),
                            edgeTop, edgeBottom, LinearGradientMode.Vertical))
                        {
                            using (Pen highlight = new Pen(edge, liq ? 1.1f : 1f))
                            {
                                g.DrawPath(highlight, innerPath);
                            }
                        }
                    }
                    // 单一方向顶光：上沿最亮，两端收进去，才像光打在弧面上。
                    // 透明化改造：245 压到 190，亮线太重会把卡片上沿读成一条白边。
                    int inset = Math.Max(5, radius);
                    using (Pen bright = new Pen(Color.FromArgb(liq ? 190 : 245, 255, 255, 255), 2f))
                    {
                        g.DrawLine(bright, rect.X + inset, rect.Y + 1, rect.Right - inset - 1, rect.Y + 1);
                    }
                }
            }
        }

        /// <summary>
        /// 一块「小玻璃」：按钮、开关、导航胶囊、进度条轨道都用它。
        ///
        /// 与 <see cref="PaintGlass"/> 的分工：
        ///  - 卡片底下是整窗壁纸，所以卡片要跑折射（<see cref="Liquid.Build"/>）；
        ///  - 小控件多数躺在卡片里，卡片内部本来就是平滑的玻璃面，
        ///    对平滑面做位移是看不出任何变化的（折射只改变结构的位置，不改变对比度）。
        ///    所以这里直接裁父卡片的表面当底，省掉一次折射计算，接缝还是零。
        ///  - 只有直接压在页面上的小控件（导航胶囊、状态栏进度条）才真的跑折射。
        /// </summary>
        public static void PaintPill(Control c, Graphics g, Rectangle rect, int radius, int fog, Color tint, int tintAlpha)
        {
            if (c == null || g == null) return;
            if (rect.Width < 4 || rect.Height < 4) return;
            bool liq = LiquidMode;
            using (GraphicsPath path = Round(rect, radius))
            {
                Region old = g.Clip;
                try
                {
                    g.SetClip(path, CombineMode.Intersect);

                    LgCard card = c.Parent as LgCard;
                    if (card != null && card.Surface != null)
                    {
                        GlassCrop.PaintRect(c, g, rect);
                    }
                    else if (rect.Height >= 16)
                    {
                        // 直接压在页面上：跑一次边缘折射，胶囊两端才会把条纹「挤」弯
                        Bitmap refr = Liquid.Build(c, rect.X, rect.Y, rect.Width, rect.Height, radius);
                        if (refr != null) g.DrawImageUnscaled(refr, rect.X, rect.Y);
                        else PaintBackdrop(c, g, rect);
                    }
                    else
                    {
                        // 太薄的控件（进度条）没有可弯折的厚度，折射算出来只会糊成一片
                        PaintBackdrop(c, g, rect);
                    }

                    if (tintAlpha > 0 && tint.A > 0)
                    {
                        using (SolidBrush brush = new SolidBrush(Alpha(tint, tintAlpha)))
                        {
                            g.FillPath(brush, path);
                        }
                    }
                    if (fog > 0)
                    {
                        using (SolidBrush brush = new SolidBrush(Color.FromArgb(fog, 255, 255, 255)))
                        {
                            g.FillPath(brush, path);
                        }
                    }

                    int half = Math.Max(2, rect.Height / 2);
                    Rectangle top = new Rectangle(rect.X, rect.Y, rect.Width, half);
                    using (LinearGradientBrush brush = new LinearGradientBrush(top,
                        Color.FromArgb(liq ? 76 : 44, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Vertical))
                    {
                        g.FillRectangle(brush, top);
                    }
                    int bh = Math.Max(3, rect.Height / 4);
                    Rectangle bottom = new Rectangle(rect.X, rect.Bottom - bh, rect.Width, bh);
                    using (LinearGradientBrush brush = new LinearGradientBrush(bottom,
                        Color.FromArgb(0, 255, 255, 255), Color.FromArgb(liq ? 54 : 24, 255, 255, 255), LinearGradientMode.Vertical))
                    {
                        g.FillRectangle(brush, bottom);
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

                // 边缘：内侧白环 + 外侧薄暗线。小控件不做 5px 聚光，
                // 那个厚度只属于大块玻璃，套在 40px 高的按钮上会显得脏。
                using (Pen inner = new Pen(Color.FromArgb(liq ? 196 : 140, 255, 255, 255), 1f))
                {
                    g.DrawPath(inner, path);
                }
                using (Pen outer = new Pen(liq ? Color.FromArgb(46, 24, 60, 136) : Color.FromArgb(38, Shadow), 1f))
                {
                    g.DrawPath(outer, path);
                }
            }
        }

        /// <summary>
        /// Aqua 顶光：千禧年主题的招牌。
        ///
        /// 那个年代的按钮/卡片上永远有一道「上半个是亮的、下半个是本色」的硬边过渡 ——
        /// 这个过渡不是渐变出来的，是一条**明确的分界**：高斯渐变看起来像玻璃，
        /// 而 2000 年代的塑料是「刷了一层清漆」。
        /// 所以这里分两段画：先一整块柔光，再在 46% 处压一条更亮的边。
        /// </summary>
        public static void PaintGloss(Graphics g, Rectangle rect, int radius, int alpha)
        {
            if (alpha <= 0 || rect.Width <= 4 || rect.Height <= 8) return;
            using (GraphicsPath path = Round(rect, radius))
            {
                Region old = g.Clip;
                try
                {
                    g.SetClip(path, CombineMode.Intersect);

                    int half = Math.Max(2, rect.Height * 46 / 100);
                    Rectangle upper = new Rectangle(rect.X, rect.Y, rect.Width, half);
                    using (LinearGradientBrush brush = new LinearGradientBrush(upper,
                        Color.FromArgb(alpha, 255, 255, 255), Color.FromArgb(alpha / 5, 255, 255, 255),
                        LinearGradientMode.Vertical))
                    {
                        g.FillRectangle(brush, upper);
                    }
                    // 分界线：一条 1px 的高亮窄带，就是「清漆边缘」的读法
                    if (half < rect.Height - 2)
                    {
                        using (SolidBrush edge = new SolidBrush(Color.FromArgb(Math.Min(255, alpha + 40), 255, 255, 255)))
                        {
                            g.FillRectangle(edge, rect.X, rect.Y + half, rect.Width, 1);
                        }
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
        }

        /// <summary>
        /// 「控件底」的统一入口：输入框、下拉框、开关轨道、进度条轨道都走这里。
        ///
        /// 三种皮肤在这里分叉，而不是在每个控件里各写一遍 if：
        ///  - 纯色：白底 + 1px 边，不透明、不折射；
        ///  - 千禧年：奶白玻璃（雾 = liquidFog + 56）+ Aqua 顶光 + 灰蓝细边；
        ///  - 液态玻璃：原来的玻璃胶囊。
        ///
        /// <paramref name="liquidFog"/> 只对两种玻璃皮肤有意义，纯色皮肤会忽略它。
        /// </summary>
        public static void PaintWell(Control c, Graphics g, Rectangle rect, int radius, int liquidFog, Color tint, int tintAlpha)
        {
            if (c == null || g == null || rect.Width < 4 || rect.Height < 4) return;

            if (SolidMode)
            {
                FillRound(g, rect, radius, Card);
                DrawRound(g, rect, radius, Border, 1f);
                if (tintAlpha > 0 && tint.A > 0)
                {
                    FillRound(g, rect, radius, Alpha(tint, tintAlpha));
                }
                return;
            }

            if (MillenniumMode)
            {
                int fog = liquidFog + 56;
                if (fog > 170) fog = 170;
                PaintPill(c, g, rect, radius, fog, tint, tintAlpha);
                // 顶光要压在玻璃之上、描边之下，所以两者都画在 PaintPill 之后
                if (rect.Height >= 18) PaintGloss(g, rect, radius, 96);
                DrawRound(g, rect, radius, Alpha(BorderStrong, 190), 1f);
                return;
            }

            PaintPill(c, g, rect, radius, liquidFog, tint, tintAlpha);
        }

        public static void PaintWell(Control c, Graphics g, Rectangle rect, int radius, int liquidFog)
        {
            PaintWell(c, g, rect, radius, liquidFog, Color.Empty, 0);
        }

        /// <summary>
        /// 标题栏 / 侧栏 / 状态栏那层白的浓度。
        ///
        /// 单独提成常量，是因为栏里还有子控件（图标按钮、导航项、进度条）要自己铺底。
        /// 子控件如果只铺壁纸、栏铺「壁纸 + 白」，两者会差整整一档 ——
        /// 在蓝白条纹上就是几块看得出来的暗方块。子控件必须拿同一个值重铺一遍。
        /// </summary>
        // 透明化改造：标题栏/侧栏/状态栏的白雾整体下调。
        // 原 92/104 在液态玻璃下（乘 7/10 后 64/73）仍是明显的白色带，
        // 用户要「70% 透明」，栏也要透出背景。
        public const int TitleBarAlpha = 72;
        public const int ChromeBarAlpha = 80;   // 侧栏 + 状态栏

        /// <summary>
        /// 只铺「栏底」：壁纸 + 白，不画顶光。
        ///
        /// 栏内的子控件（导航项、图标按钮、进度条）应当调这个而不是 PaintGlassBar ——
        /// 顶光是按控件自身高度算的（顶部 40%），小控件画出来会得到一道
        /// 横条上根本不存在的高光，五个导航项就是五道假高光。
        /// </summary>
        public static void PaintBarFlat(Control c, Graphics g, Rectangle rect, int alpha)
        {
            PaintBackdrop(c, g, rect);
            if (rect.Width <= 0 || rect.Height <= 0) return;

            int a = alpha;
            // 液态玻璃的栏要更透（蓝白条纹得透上来）；千禧年的栏要更白 ——
            // 那个年代的工具栏是奶白色的，不是透明的。
            // 透明化改造：液态玻璃乘 7/10 改成 5/10，让栏真正透出背景。
            if (LiquidMode) a = a * 5 / 10;
            else if (MillenniumMode) a = a * 11 / 10;
            if (a < 0) a = 0;
            if (a > 255) a = 255;
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(a, 255, 255, 255)))
            {
                g.FillRectangle(brush, rect);
            }
        }

        /// <summary>
        /// 整条玻璃栏（标题栏 / 侧栏 / 状态栏）：栏底 + 一道内高光。
        /// 栏和卡片用的是同一张壁纸，所以光斑在栏、内容区、卡片之间是连续过渡的。
        /// </summary>
        public static void PaintGlassBar(Control c, Graphics g, Rectangle rect, int alpha)
        {
            PaintBarFlat(c, g, rect, alpha);
            if (rect.Width <= 0 || rect.Height <= 0) return;

            // 栏也要有「从上方打光」的质感，否则就是一条平的浅色带。
            // 只铺顶部 40% 且很淡，纵向高度大（侧栏）时才看得出来是玻璃。
            // 纯色皮肤不要这道光：它是渐变，而「纯色」的整个意思就是不要渐变。
            if (SolidMode) return;
            int sheen = Math.Max(6, rect.Height * 2 / 5);
            Rectangle area = new Rectangle(rect.X, rect.Y, rect.Width, Math.Min(rect.Height, sheen));
            if (area.Height > 2)
            {
                using (LinearGradientBrush brush = new LinearGradientBrush(area,
                    Color.FromArgb(LiquidMode ? 40 : 96, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Vertical))
                {
                    g.FillRectangle(brush, area);
                }
            }
        }

        /// <summary>
        /// 把圆角矩形之外的像素重铺一遍背后那张壁纸。
        ///
        /// 玻璃控件的圆角一定是在「已经铺满整块矩形」之后才画的，
        /// 于是四个角会残留一块方形的壁纸 + 之后的玻璃填充，
        /// 视觉上卡片就像没切圆角。这里用 Exclude 得到圆角外的区域，
        /// 重新从同一张壁纸取样铺一次，弧形边界才是干净的。
        /// </summary>
        public static void ClearOutsideRound(Control c, Graphics g, Rectangle rect, int radius)
        {
            if (rect.Width <= 4 || rect.Height <= 4) return;
            Region old = g.Clip;
            try
            {
                using (GraphicsPath path = Round(rect, radius))
                {
                    Region outside = new Region(rect);
                    using (Region inside = new Region(path))
                    {
                        outside.Exclude(inside);
                        g.SetClip(outside, CombineMode.Replace);
                        PaintBackdrop(c, g, rect);
                    }
                }
            }
            catch
            {
                // 圆角修正失败不该影响整张卡片
            }
            finally
            {
                g.Clip = old;
                if (old != null) old.Dispose();
            }
        }

        /// <summary>
        /// 卡片的接触阴影：贴着玻璃内侧的底缘画一条渐隐暗带。
        ///
        /// 为什么是「内侧」而不是「外投影」：
        /// 卡片本身就是玻璃的边界，控件位图的范围就是 (0,0,W,H)。
        /// 任何画在 x&lt;0 / y&lt;0 的像素都会被这张位图直接裁掉——所以
        /// 「往外扩一圈画柔和外投影」在 WinForms 的独立 HWND + 离屏位图模型下
        /// 是画不出来的（试过，投影全部落在位图外，等于没画）。
        /// 唯一能挤出「厚度感」的地方是玻璃内侧底缘：那里本来就该比顶部更暗，
        /// 光又是从上方来的，空气光遮蔽（AO）也正好落在这一圈。
        ///
        /// 所以这里改成：沿圆角路径内侧、由外向内的一圈渐隐暗边，
        /// 越贴边越暗、往里 10px 内衰减到 0，卡片就会从背景上「浮」起来，
        /// 而且完全不侵占任何子控件的空间。
        /// </summary>
        public static void CardShade(Graphics g, Rectangle rect, int radius)
        {
            if (rect.Width < 8 || rect.Height < 8) return;

            // ---- AO 内环：贴着圆角边的一圈暗边，顶部最淡、底部最重 ----
            // 分两层是因为 GDI+ 做不了径向衰减描边：
            // 外层 1.8px 稍实，内层 5px 很淡，两者叠加后近似一条 6px 的软边。
            Rectangle[] rings = new Rectangle[]
            {
                new Rectangle(rect.X + 1, rect.Y + 1, Math.Max(1, rect.Width - 2), Math.Max(1, rect.Height - 2)),
                new Rectangle(rect.X + 3, rect.Y + 3, Math.Max(1, rect.Width - 6), Math.Max(1, rect.Height - 6))
            };
            int[] ringAlpha = new int[] { LiquidMode ? 20 : 30, LiquidMode ? 9 : 14 };
            float[] ringWidth = new float[] { 2.2f, 4f };

            Region old = g.Clip;
            try
            {
                using (GraphicsPath path = Round(rect, radius))
                {
                    g.SetClip(path, CombineMode.Intersect);
                    for (int i = 0; i < rings.Length; i++)
                    {
                        Rectangle r = rings[i];
                        if (r.Width < 6 || r.Height < 6) continue;
                        int rad = Math.Max(1, radius - i * 2);
                        try
                        {
                            using (GraphicsPath ring = Round(r, rad))
                            {
                                // 上沿压淡、下沿压实：光和 AO 的方向必须一致，
                                // 否则会读成「卡片上下都被描了一圈边」，反而更假。
                                using (LinearGradientBrush brush = new LinearGradientBrush(
                                    new Rectangle(r.X, r.Y, Math.Max(1, r.Width), Math.Max(1, r.Height)),
                                    Color.FromArgb(ringAlpha[i] / 3, Shadow),
                                    Color.FromArgb(ringAlpha[i], Shadow),
                                    LinearGradientMode.Vertical))
                                {
                                    using (Pen pen = new Pen(brush, ringWidth[i]))
                                    {
                                        g.DrawPath(pen, ring);
                                    }
                                }
                            }
                        }
                        catch
                        {
                        }
                    }

                    // ---- 接触阴影：贴着底缘的一条更实的暗带 ----
                    int h = Math.Max(8, rect.Height / 10);
                    Rectangle contact = new Rectangle(rect.X, rect.Bottom - h, rect.Width, h);
                    using (LinearGradientBrush brush = new LinearGradientBrush(contact,
                        Color.FromArgb(0, Shadow), Color.FromArgb(LiquidMode ? 20 : 34, Shadow), LinearGradientMode.Vertical))
                    {
                        g.FillRectangle(brush, contact);
                    }
                }
            }
            finally
            {
                g.Clip = old;
                if (old != null) old.Dispose();
            }
        }

        /// <summary>页面背景柔光：顶部一团极淡的主色光晕，避免大片留白显得空</summary>
        public static void PaintPageGlow(Graphics g, Rectangle rect)
        {
            if (rect.Width < 60 || rect.Height < 60) return;
            int r = Math.Max(140, rect.Width / 2);
            Rectangle area = new Rectangle(rect.X + rect.Width / 2 - r, rect.Y - (int)(r * 0.62f), r * 2, r);
            try
            {
                using (GraphicsPath path = new GraphicsPath())
                {
                    path.AddEllipse(area);
                    using (PathGradientBrush brush = new PathGradientBrush(path))
                    {
                        brush.CenterColor = Alpha(Accent, 18);
                        // SurroundColors 长度必须等于路径点数，否则 GDI+ 抛参数异常
                        PointF[] points = path.PathPoints;
                        Color[] surround = new Color[points.Length];
                        for (int i = 0; i < surround.Length; i++) surround[i] = Color.FromArgb(0, Accent);
                        brush.SurroundColors = surround;
                        g.FillPath(brush, path);
                    }
                }
            }
            catch
            {
            }
        }

        /// <summary>列表行的选中 / 悬停底色，统一成一种画法（含左侧 3px 主色指示条）</summary>
        public static void RowSelection(Graphics g, Rectangle rect, int radius, bool selected, bool hover)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return;
            if (selected)
            {
                if (LiquidMode || MillenniumMode)
                {
                    // 玻璃皮肤的选中态是「一块被点亮的玻璃」：半透明主色 + 内侧白环，
                    // 不用实色，否则会在透出来的背景上压出一块死板的长方形。
                    FillRound(g, rect, radius, Alpha(Accent, 64));
                    DrawRound(g, rect, radius, Color.FromArgb(120, 255, 255, 255), 1f);
                }
                else
                {
                    FillRound(g, rect, radius, CardHover);
                }
                FillRound(g, new Rectangle(rect.X + 6, rect.Y + rect.Height / 2 - 8, 3, 16), 2, Accent);
            }
            else if (hover)
            {
                FillRound(g, rect, radius, LiquidMode || MillenniumMode ? Alpha(Accent, 26) : RowHover);
                if (LiquidMode || MillenniumMode) DrawRound(g, rect, radius, Color.FromArgb(80, 255, 255, 255), 1f);
            }
        }
    }
}
