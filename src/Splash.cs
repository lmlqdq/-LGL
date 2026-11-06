using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Liuguang.Core;

namespace Liuguang.UI
{
    /// <summary>
    /// 启动动画：用 Windows 自带的 Media Player（WMPlayer.OCX ActiveX 控件）播放一段
    /// 开场视频，任意键 / 任意点击立即跳过。
    ///
    /// 为什么用 AxHost 而不是 WMPLib Interop：
    ///   - 项目用系统自带 csc.exe 编译、没有 tlbimp 工具，生成不了 Interop.WMPLib.dll；
    ///   - AxHost 是 System.Windows.Forms 内置类型，子类只传一个 ProgID，
    ///     .NET 会通过 OLE 接口在运行时动态实例化 ActiveX 控件 —— 零额外程序集、零依赖。
    ///
    /// 视频文件约定：<see cref="SplashVideoPath"/>（启动器目录下 splash.mp4）。
    /// 文件不存在 / 加载失败 / 系统没装 WMP 时，不弹任何错误、直接跳过动画 ——
    /// 片头是锦上添花，不该挡住启动器本身。
    /// </summary>
    public class SplashForm : Form
    {
        /// <summary>视频文件名。放在启动器目录下，跟着 exe 走。</summary>
        public const string SplashVideoName = "splash.mp4";

        public static string SplashVideoPath
        {
            get { return Path.Combine(Paths.Root, SplashVideoName); }
        }

        public static bool VideoAvailable
        {
            get { return File.Exists(SplashVideoPath); }
        }

        private WmpPlayer player;
        private bool skip;
        private bool started;            // 是否已经真正开始播放（playState==3 或播放进度 > 0）
        private int grace;               // 加载宽限轮数：给 WMP 解码器留出起播时间
        private readonly Timer watchdog;

        public SplashForm()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;

            Text = Paths.AppName;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            // 和主窗口同尺寸：片头结束时主窗口在同一个位置出现，过渡不跳
            ClientSize = new Size(1130, 706);
            BackColor = Color.Black;
            ShowInTaskbar = true;
            TopMost = true;
            KeyPreview = true;

            watchdog = new Timer();
            watchdog.Interval = 100;
            watchdog.Tick += new EventHandler(OnWatchdog);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
            Focus();
            try
            {
                player = new WmpPlayer();
                player.Dock = DockStyle.Fill;
                Controls.Add(player);
                player.Play(SplashVideoPath);
                Log.Write("启动动画：播放 " + SplashVideoName + "（任意键跳过）");
            }
            catch (Exception ex)
            {
                // WMP 没装 / ActiveX 创建失败 —— 直接跳过，不阻塞
                Log.Error("启动动画初始化失败：" + ex.Message);
                Close();
                return;
            }
            watchdog.Start();
        }

        private void OnWatchdog(object sender, EventArgs e)
        {
            if (player == null) return;
            try
            {
                // 跳过键直接关。
                if (skip)
                {
                    watchdog.Stop();
                    Log.Write("启动动画：跳过");
                    Close();
                    return;
                }

                // 关键：只有「真正开始播放」之后才允许用 playState==1(停止) 判定结束。
                // WMP 在 URL 刚设置、解码器还在起播时，playState 会短暂停在 0(未定义)/
                // 10(就绪)/1(停止)，此时还没出画面。若拿 1 当「播完」，窗口会在 100ms 内
                // 被误关，动画一闪而过甚至看不见 —— 这就是「启动动画没了」的根因。
                if (!started)
                {
                    int state = player.GetPlayState();
                    // 3 = 播放中；2 = 已暂停（说明已加载出第一帧，可视为已起播）
                    if (state == 3 || state == 2 || player.GetPosition() > 0.05)
                    {
                        started = true;
                        Log.Write("启动动画：开始播放");
                    }
                    else
                    {
                        // 给解码器宽限期（约 5 秒）：这期间还没起播可能是 MP4 编码格式
                        // 需要 WMP 装解码器 / 或文件较大加载慢，别急着关。
                        grace++;
                        if (grace > 50)
                        {
                            watchdog.Stop();
                            Log.Write("启动动画：长时间未起播，跳过");
                            Close();
                        }
                        return;
                    }
                }

                // 已起播：播到头（8=媒体结束，或回到 1=停止）才算真正结束。
                if (player.IsFinished())
                {
                    watchdog.Stop();
                    Log.Write("启动动画：结束");
                    Close();
                }
            }
            catch
            {
                watchdog.Stop();
                Close();
            }
        }

        /// <summary>任意键跳过。ProcessCmdKey 能收到 Tab/方向键/Enter 等导航键。</summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Skip();
            return true;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Skip();
            base.OnMouseDown(e);
        }

        private void Skip()
        {
            if (skip) return;
            skip = true;
            Log.Write("启动动画：跳过");
            try
            {
                if (player != null) player.Stop();
            }
            catch
            {
            }
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (watchdog != null) watchdog.Stop();
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (watchdog != null) watchdog.Dispose();
                if (player != null)
                {
                    try
                    {
                        player.Dispose();
                    }
                    catch
                    {
                    }
                }
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// 用 AxHost 承载 Windows Media Player 的 ActiveX 控件（WMPlayer.OCX.7）。
    ///
    /// AxHost 是 .NET 原生提供「承载任意 COM ActiveX 控件」的基类：
    /// 子类构造时把 CLSID 传给它，运行时 .NET 通过 IOleObject/IOleInPlaceObject
    /// 这套 OLE 接口把控件真正创建出来并嵌进 WinForms 窗口。
    ///
    /// 视频控制走「晚绑定」：OCX 暴露的 COM 自动化接口（URL / controls / playState）
    /// 通过反射拿，不依赖任何 Interop 程序集，也就不需要 tlbimp。
    /// </summary>
    public class WmpPlayer : AxHost
    {
        private object ocx;                 // WMPlayer.OCX 自动化对象
        private object wmpControls;         // controls
        private object wmpSettings;         // settings

        private static readonly object[] NoArgs = new object[] { };

        public WmpPlayer()
            : base("6BF52A52-394A-11D3-B153-00C04F79FAA6")
        {
            // CLSID 是 WMPlayer.OCX.7 的类标识（WMP 9+ 的 ActiveX 控件）
        }

        /// <summary>AxHost 创建好底层 COM 控件后调用，拿到自动化接口。</summary>
        protected override void AttachInterfaces()
        {
            try
            {
                ocx = GetOcx();
                if (ocx != null)
                {
                    wmpControls = InvokeGet(ocx, "controls");
                    wmpSettings = InvokeGet(ocx, "settings");
                    // uiMode = "none"：不显示播放/暂停控制条，视频全屏无边框播放。
                    // 默认的 "full" 会在底部露出一条「正在播放 splash … 千比特/秒」，
                    // 那会瞬间戳穿「这是个启动动画」的错觉。
                    try
                    {
                        InvokeSet(ocx, "uiMode", "none");
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
                ocx = null;
                wmpControls = null;
                wmpSettings = null;
            }
            base.AttachInterfaces();
        }

        public void Play(string path)
        {
            if (ocx == null) throw new InvalidOperationException("WMP 控件未创建");
            // 设为「自动播放」：URL 一给，WMP 就自己开始播
            if (wmpSettings != null)
            {
                try
                {
                    InvokeSet(wmpSettings, "autoStart", true);
                }
                catch
                {
                }
            }
            InvokeSet(ocx, "URL", path);
            if (wmpControls != null)
            {
                try
                {
                    Invoke(wmpControls, "play", NoArgs);
                }
                catch
                {
                }
            }
        }

        public void Stop()
        {
            if (wmpControls == null) return;
            try
            {
                Invoke(wmpControls, "stop", NoArgs);
            }
            catch
            {
            }
        }

        /// <summary>
        /// 视频是否已经播完。playState 枚举：0 未定义 / 1 停止 / 2 暂停 /
        /// 3 播放中 / 8 媒体结束 / 10 准备就绪。到 8 就是播完。
        /// </summary>
        public bool IsFinished()
        {
            if (ocx == null) return true;
            try
            {
                object state = InvokeGet(ocx, "playState");
                if (state == null) return true;
                int value = Convert.ToInt32(state);
                // 8 = 媒体结束；某些版本播完回到 1(停止)，统一当结束处理
                return value == 8 || value == 1;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>返回当前 playState 原始值（用于区分「未起播」和「已播完」）。</summary>
        public int GetPlayState()
        {
            if (ocx == null) return 0;
            try
            {
                object state = InvokeGet(ocx, "playState");
                return state == null ? 0 : Convert.ToInt32(state);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>返回当前播放位置（秒）。拿不到就返回 0。</summary>
        public double GetPosition()
        {
            if (wmpControls == null) return 0;
            try
            {
                object pos = InvokeGet(wmpControls, "currentPosition");
                return pos == null ? 0 : Convert.ToDouble(pos);
            }
            catch
            {
                return 0;
            }
        }

        // ==================== 晚绑定反射 ====================
        // COM 自动化对象在 .NET 里表现为 __ComObject，它的成员要等运行时才解析，
        // 所以用 Type.InvokeMember 走 IDispatch 调用。

        private static object InvokeGet(object target, string name)
        {
            Type t = target.GetType();
            return t.InvokeMember(name,
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.GetProperty,
                null, target, null);
        }

        private static void InvokeSet(object target, string name, object value)
        {
            Type t = target.GetType();
            t.InvokeMember(name,
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.SetProperty,
                null, target, new object[] { value });
        }

        private static object Invoke(object target, string name, object[] args)
        {
            Type t = target.GetType();
            return t.InvokeMember(name,
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.InvokeMethod,
                null, target, args);
        }
    }
}
