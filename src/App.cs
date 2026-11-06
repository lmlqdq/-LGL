using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Liuguang.Core
{
    public static class Paths
    {
        public static string Root = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        public static string AppName = "流光启动器";
        public static string Version = "1.0.0";

        public static string McDir
        {
            get { return Path.Combine(Root, ".minecraft"); }
        }

        public static string VersionsDir
        {
            get { return Path.Combine(McDir, "versions"); }
        }

        public static string LibrariesDir
        {
            get { return Path.Combine(McDir, "libraries"); }
        }

        public static string AssetsDir
        {
            get { return Path.Combine(McDir, "assets"); }
        }

        /// <summary>启动器自带的 Java 运行时目录（自动下载的 JDK 装在这里）</summary>
        public static string RuntimeDir
        {
            get { return Path.Combine(McDir, "runtime"); }
        }

        public static string AssetsIndexDir
        {
            get { return Path.Combine(AssetsDir, "indexes"); }
        }

        public static string AssetsObjectDir
        {
            get { return Path.Combine(AssetsDir, "objects"); }
        }

        public static string LogDir
        {
            get { return Path.Combine(Root, "日志"); }
        }

        public static string ConfigPath
        {
            get { return Path.Combine(Root, "config.json"); }
        }

        public static string VersionDir(string id)
        {
            return Path.Combine(VersionsDir, id);
        }

        public static string VersionJsonPath(string id)
        {
            return Path.Combine(VersionDir(id), id + ".json");
        }

        /// <summary>版本隔离时，游戏目录为该版本独立目录；否则为 .minecraft</summary>
        public static string GameDir(string versionId, bool isolation)
        {
            if (isolation) return VersionDir(versionId);
            return McDir;
        }

        public static string ModsDir(string versionId, bool isolation)
        {
            return Path.Combine(GameDir(versionId, isolation), "mods");
        }

        public static void EnsureBaseDirs()
        {
            Util.EnsureDir(McDir);
            Util.EnsureDir(VersionsDir);
            Util.EnsureDir(LibrariesDir);
            Util.EnsureDir(AssetsDir);
            Util.EnsureDir(AssetsIndexDir);
            Util.EnsureDir(AssetsObjectDir);
            Util.EnsureDir(LogDir);
        }
    }

    public class Config
    {
        public string PlayerName = "Steve";
        public string JavaPath = "";
        public int MemoryMb = 2048;
        public bool UseMirror = true;
        public bool VersionIsolation = true;
        public string LastVersion = "";
        public int WindowWidth = 854;
        public int WindowHeight = 480;
        public bool Fullscreen = false;
        public bool CloseOnLaunch = false;
        public string Accent = "#0FA3A3";
        /// <summary>
        /// 界面皮肤：liquid = 液态玻璃，millennium = 千禧年，solid = 纯色。
        /// 空值 / "classic" 表示老配置，按液态玻璃处理。
        /// </summary>
        public string Theme = "";
        /// <summary>自定义背景图路径；空值 = 用皮肤自带的背景</summary>
        public string Background = "";
        /// <summary>启动时播放片头动画（任意键可跳过）。config.json 里删掉这一项 = 回到默认开启。</summary>
        public bool Splash = true;
        public int DownloadThreads = 8;
        public int ExtraJvmArgs = 0;
        public string JvmArgs = "";
        /// <summary>主题版本：用于把老配置的默认紫色迁到青色，同时不覆盖用户自己选的色</summary>
        public int ThemeVersion = 2;

        public static Config Load()
        {
            Config cfg = new Config();
            try
            {
                if (!File.Exists(Paths.ConfigPath)) return cfg;
                Json root = Json.Parse(File.ReadAllText(Paths.ConfigPath, Encoding.UTF8));
                cfg.PlayerName = root["PlayerName"].AsString(cfg.PlayerName);
                cfg.JavaPath = root["JavaPath"].AsString("");
                cfg.MemoryMb = root["MemoryMb"].AsInt(cfg.MemoryMb);
                cfg.UseMirror = root["UseMirror"].AsBool(cfg.UseMirror);
                cfg.VersionIsolation = root["VersionIsolation"].AsBool(cfg.VersionIsolation);
                cfg.LastVersion = root["LastVersion"].AsString("");
                cfg.WindowWidth = root["WindowWidth"].AsInt(cfg.WindowWidth);
                cfg.WindowHeight = root["WindowHeight"].AsInt(cfg.WindowHeight);
                cfg.Fullscreen = root["Fullscreen"].AsBool(cfg.Fullscreen);
                cfg.CloseOnLaunch = root["CloseOnLaunch"].AsBool(cfg.CloseOnLaunch);
                cfg.Accent = root["Accent"].AsString(cfg.Accent);
                cfg.Theme = root["Theme"].AsString("");
                cfg.Background = root["Background"].AsString("");
                cfg.Splash = root["Splash"].AsBool(cfg.Splash);
                cfg.DownloadThreads = root["DownloadThreads"].AsInt(cfg.DownloadThreads);
                cfg.JvmArgs = root["JvmArgs"].AsString("");
                cfg.ThemeVersion = root["ThemeVersion"].AsInt(0);
            }
            catch (Exception ex)
            {
                Log.Error("读取配置失败：" + ex.Message);
            }
            // 1.1.2 起默认主题换成青色。老配置里存的是当年的默认紫，
            // 用户没主动换过色就跟着迁到青色；自己选过别的颜色则原样保留。
            if (cfg.ThemeVersion < 2)
            {
                if (string.IsNullOrEmpty(cfg.Accent)
                    || string.Equals(cfg.Accent, "#7C5CFF", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(cfg.Accent, "#6A4CF0", StringComparison.OrdinalIgnoreCase))
                {
                    cfg.Accent = "#0FA3A3";
                }
                cfg.ThemeVersion = 2;
            }
            if (cfg.MemoryMb < 512) cfg.MemoryMb = 512;
            if (cfg.DownloadThreads < 1) cfg.DownloadThreads = 1;
            if (cfg.DownloadThreads > 16) cfg.DownloadThreads = 16;
            return cfg;
        }

        public void Save()
        {
            try
            {
                Json root = Json.NewObject();
                root.Set("PlayerName", Json.Str(PlayerName));
                root.Set("JavaPath", Json.Str(JavaPath));
                root.Set("MemoryMb", Json.Num(MemoryMb));
                root.Set("UseMirror", Json.Bool(UseMirror));
                root.Set("VersionIsolation", Json.Bool(VersionIsolation));
                root.Set("LastVersion", Json.Str(LastVersion));
                root.Set("WindowWidth", Json.Num(WindowWidth));
                root.Set("WindowHeight", Json.Num(WindowHeight));
                root.Set("Fullscreen", Json.Bool(Fullscreen));
                root.Set("CloseOnLaunch", Json.Bool(CloseOnLaunch));
                root.Set("Accent", Json.Str(Accent));
                root.Set("Theme", Json.Str(Theme));
                root.Set("Background", Json.Str(Background));
                root.Set("Splash", Json.Bool(Splash));
                root.Set("DownloadThreads", Json.Num(DownloadThreads));
                root.Set("JvmArgs", Json.Str(JvmArgs));
                root.Set("ThemeVersion", Json.Num(ThemeVersion));
                File.WriteAllText(Paths.ConfigPath, root.ToJson(), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Log.Error("保存配置失败：" + ex.Message);
            }
        }
    }

    public static class Util
    {
        public static bool Is64Bit
        {
            get { return Environment.Is64BitOperatingSystem; }
        }

        private static int archFlag = -1;

        /// <summary>
        /// 是否 Windows on ARM。.NET Framework 没有直接 API，只能看环境变量
        /// PROCESSOR_ARCHITECTURE / PROCESSOR_ARCHITEW6432（32 位进程看到的是 x86）。
        /// 首次调用后缓存，避免反复读环境变量。
        /// </summary>
        public static bool IsArm64
        {
            get
            {
                if (archFlag < 0)
                {
                    string a = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE");
                    string b = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432");
                    bool arm = Contains(a, "ARM64") || Contains(b, "ARM64");
                    archFlag = arm ? 1 : 0;
                }
                return archFlag == 1;
            }
        }

        private static bool Contains(string text, string part)
        {
            if (text == null || text.Length == 0) return false;
            return text.ToUpperInvariant().IndexOf(part) >= 0;
        }

        public static void EnsureDir(string path)
        {
            try
            {
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            }
            catch
            {
            }
        }

        public static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.0") + " KB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / 1024.0 / 1024.0).ToString("0.0") + " MB";
            return (bytes / 1024.0 / 1024.0 / 1024.0).ToString("0.00") + " GB";
        }

        public static string FormatCount(long count)
        {
            if (count >= 100000000) return (count / 100000000.0).ToString("0.0") + " 亿";
            if (count >= 10000) return (count / 10000.0).ToString("0.0") + " 万";
            return count.ToString();
        }

        public static string SafeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "unknown";
            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder sb = new StringBuilder(name.Length);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                bool bad = false;
                for (int k = 0; k < invalid.Length; k++)
                {
                    if (invalid[k] == c)
                    {
                        bad = true;
                        break;
                    }
                }
                sb.Append(bad ? '_' : c);
            }
            return sb.ToString();
        }

        public static bool IsValidPlayerName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (name.Length < 3 || name.Length > 16) return false;
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_';
                if (!ok) return false;
            }
            return true;
        }

        public static string ReadText(string path)
        {
            return File.ReadAllText(path, Encoding.UTF8);
        }

        public static void WriteText(string path, string text)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) EnsureDir(dir);
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }

        /// <summary>在资源管理器中定位文件或文件夹</summary>
        public static void RevealInExplorer(string path)
        {
            try
            {
                if (Directory.Exists(path)) System.Diagnostics.Process.Start("explorer.exe", "\"" + path + "\"");
                else if (File.Exists(path)) System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + path + "\"");
            }
            catch (Exception ex)
            {
                Log.Error("打开文件夹失败：" + ex.Message);
            }
        }
    }
}
