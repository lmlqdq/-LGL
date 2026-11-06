using System;
using System.IO;
using System.Windows.Forms;
using Liuguang.Core;
using Liuguang.UI;

namespace Liuguang
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 兜住所有未处理异常：UI 线程异常不再弹系统崩溃框直接退出，
            // 后台线程异常也不会静默终止进程，统一写日志并给出可回传的提示。
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += new System.Threading.ThreadExceptionEventHandler(OnThreadException);
            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(OnDomainException);

            try
            {
                Net.Init();
                Paths.EnsureBaseDirs();
                Log.FilePath = Path.Combine(Paths.LogDir, "launcher.log");
                Log.Write("==== " + Paths.AppName + " " + Paths.Version + " 启动 ====");
                Log.Write("程序目录：" + Paths.Root);

                Config config = Config.Load();
                Net.UseMirror = config.UseMirror;

                // 1.4.0 起默认皮肤是液态玻璃：老配置里没有 Theme 字段，按新默认走。
                // 用户的青色主题色在这个皮肤下会显得脏（蓝白条纹里插进一块青绿），
                // 所以只在「还是默认青」时顺手换成蓝，用户自己挑过的其它色不动。
                Theme.Skin skin = Theme.ParseSkin(config.Theme);
                if (skin != Theme.Skin.Solid && (config.Accent.Length == 0
                    || string.Equals(config.Accent, "#0FA3A3", StringComparison.OrdinalIgnoreCase)))
                {
                    config.Accent = "#2F6BFF";
                }
                config.Theme = Theme.SkinKey(skin);
                Theme.SetSkin(skin);
                Theme.SetCustomBackground(config.Background);
                Theme.ApplyAccent(config.Accent);
                Log.Write("界面皮肤：" + config.Theme
                    + (Theme.HasCustomBackground ? "，自定义背景：" + config.Background : ""));

                // 先把主窗口建好再放片头：
                // 建界面（建五个页面、扫 Java、读壁纸）在冷启动时要几百毫秒，
                // 这段时间正好用来播动画。片头结束时主窗口已经就绪，
                // 关掉片头的下一帧主窗口就出来了，中间不会出现「动画没了、窗口还没来」的空档。
                MainForm main = new MainForm(config);
                // 视频动画不在这里播 —— 它改到「点击启动游戏」时才播，
                // 作为启动游戏前的过场。启动器本身要秒开，不能被片头拖慢。
                Application.Run(main);
            }
            catch (Exception ex)
            {
                Report(ex);
            }
        }

        private static void OnThreadException(object sender, System.Threading.ThreadExceptionEventArgs e)
        {
            Report(e.Exception);
        }

        private static void OnDomainException(object sender, UnhandledExceptionEventArgs e)
        {
            Report(e.ExceptionObject as Exception);
        }

        private static void Report(Exception ex)
        {
            string message = ex == null ? "未知异常" : ex.Message;
            string logPath = "";
            try
            {
                Log.Error("启动器异常：" + (ex == null ? "未知异常" : ex.ToString()));
                logPath = Log.FilePath;
            }
            catch
            {
            }
            try
            {
                string tip = "流光启动器遇到错误：\r\n\r\n" + message;
                if (!string.IsNullOrEmpty(logPath)) tip += "\r\n\r\n日志文件：" + logPath;
                MessageBox.Show(tip, "流光启动器", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch
            {
            }
        }
    }
}
