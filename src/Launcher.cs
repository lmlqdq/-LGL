using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Liuguang.Core
{
    public class LaunchPlan
    {
        public string JavaPath = "";
        public string Arguments = "";
        public string WorkDir = "";
        public string CommandLine = "";
    }

    public static class GameLauncher
    {
        public static LaunchPlan Build(GameVersion v, Config cfg, string javaPath)
        {
            if (string.IsNullOrEmpty(cfg.PlayerName) || !Util.IsValidPlayerName(cfg.PlayerName))
                throw new Exception("玩家名不合法（3-16 位字母、数字或下划线）");

            string gameDir = Paths.GameDir(v.Id, cfg.VersionIsolation);
            Util.EnsureDir(gameDir);
            string nativesDir = Path.Combine(v.Dir, "natives");
            Util.EnsureDir(nativesDir);

            List<string> classpath = new List<string>();
            for (int i = 0; i < v.Libraries.Count; i++)
            {
                Json lib = v.Libraries[i];
                if (!VersionMeta.IsAllowed(lib)) continue;
                string rel = VersionMeta.LibraryPath(lib["name"].AsString(""));
                if (rel.Length == 0) continue;
                string path = Path.Combine(Paths.LibrariesDir, rel.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(path)) classpath.Add(path);
            }
            if (v.JarVersion.Length > 0)
            {
                string jar = Path.Combine(Paths.VersionDir(v.JarVersion), v.JarVersion + ".jar");
                if (File.Exists(jar)) classpath.Add(jar);
            }
            if (classpath.Count == 0) throw new Exception("缺少游戏核心文件，请先在下载页重新安装该版本");
            string cp = string.Join(";", classpath.ToArray());

            string logConfig = "";
            if (v.Logging.IsObject)
            {
                string cfgId = v.Logging["file"]["id"].AsString("");
                if (cfgId.Length > 0)
                {
                    string candidate = Path.Combine(Paths.AssetsDir, "log_configs", cfgId);
                    if (File.Exists(candidate)) logConfig = candidate;
                }
            }

            Dictionary<string, string> map = new Dictionary<string, string>();
            map["auth_player_name"] = cfg.PlayerName;
            map["version_name"] = v.Id;
            map["game_directory"] = gameDir;
            map["assets_root"] = Paths.AssetsDir;
            map["assets_index_name"] = v.AssetsId;
            map["auth_uuid"] = Hashing.OfflineUuid(cfg.PlayerName);
            map["auth_access_token"] = "0";
            map["auth_session"] = "-";
            map["user_type"] = "legacy";
            map["version_type"] = v.Type;
            map["natives_directory"] = nativesDir;
            map["launcher_name"] = "Liuguang";
            map["launcher_version"] = Paths.Version;
            map["classpath"] = cp;
            map["classpath_separator"] = ";";
            map["library_directory"] = Paths.LibrariesDir;
            map["game_assets"] = Path.Combine(Paths.AssetsDir, "virtual", v.AssetsId);
            map["user_properties"] = "{}";
            map["clientid"] = "";
            map["auth_xuid"] = "0";
            map["resolution_width"] = cfg.WindowWidth.ToString();
            map["resolution_height"] = cfg.WindowHeight.ToString();

            List<string> jvm = new List<string>();
            jvm.Add("-Xmx" + cfg.MemoryMb + "M");
            jvm.Add("-Xms" + Math.Min(cfg.MemoryMb, 1024) + "M");
            jvm.Add("-Dfile.encoding=UTF-8");
            jvm.Add("-Djava.library.path=" + nativesDir);
            jvm.Add("-Dminecraft.launcher.brand=Liuguang");
            jvm.Add("-Dminecraft.launcher.version=" + Paths.Version);
            if (logConfig.Length > 0) jvm.Add("-Dlog4j.configurationFile=" + logConfig);
            if (cfg.JvmArgs != null && cfg.JvmArgs.Trim().Length > 0)
            {
                string[] extra = cfg.JvmArgs.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < extra.Length; i++) jvm.Add(extra[i]);
            }

            bool skipNext = false;
            for (int i = 0; i < v.JvmArgs.Count; i++)
            {
                Json item = v.JvmArgs[i];
                string value;
                if (item.Kind == JsonKind.String)
                {
                    value = item.StringValue;
                }
                else if (item.IsObject)
                {
                    if (!VersionMeta.IsAllowed(item)) continue;
                    value = item["value"].AsString("");
                }
                else
                {
                    continue;
                }
                if (value.Length == 0) continue;
                value = Replace(value, map);
                if (skipNext)
                {
                    skipNext = false;
                    continue;
                }
                if (value == "-cp" || value == "-classpath")
                {
                    skipNext = true;
                    continue;
                }
                if (value == cp) continue;
                if (value.IndexOf("${classpath}") >= 0) continue;
                jvm.Add(value);
            }
            jvm.Add("-cp");
            jvm.Add(cp);

            List<string> game = new List<string>();
            if (v.GameArgs.Count > 0)
            {
                for (int i = 0; i < v.GameArgs.Count; i++)
                {
                    Json item = v.GameArgs[i];
                    string value;
                    if (item.Kind == JsonKind.String)
                    {
                        value = item.StringValue;
                    }
                    else if (item.IsObject)
                    {
                        if (!VersionMeta.IsAllowed(item)) continue;
                        value = item["value"].AsString("");
                    }
                    else
                    {
                        continue;
                    }
                    if (value.Length == 0) continue;
                    game.Add(Replace(value, map));
                }
            }
            else if (v.LegacyArgs.Length > 0)
            {
                string[] tokens = v.LegacyArgs.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < tokens.Length; i++) game.Add(Replace(tokens[i], map));
            }

            if (cfg.Fullscreen)
            {
                if (!ContainsToken(game, "--fullscreen")) game.Add("--fullscreen");
            }
            else if (!ContainsToken(game, "--width"))
            {
                game.Add("--width");
                game.Add(cfg.WindowWidth.ToString());
                game.Add("--height");
                game.Add(cfg.WindowHeight.ToString());
            }

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < jvm.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(Quote(jvm[i]));
            }
            sb.Append(' ');
            sb.Append(Quote(v.MainClass));
            for (int i = 0; i < game.Count; i++)
            {
                sb.Append(' ');
                sb.Append(Quote(game[i]));
            }

            LaunchPlan plan = new LaunchPlan();
            plan.JavaPath = javaPath;
            plan.Arguments = sb.ToString();
            plan.WorkDir = gameDir;
            plan.CommandLine = "\"" + javaPath + "\" " + plan.Arguments;
            return plan;
        }

        public static Process Launch(LaunchPlan plan)
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = plan.JavaPath;
            psi.Arguments = plan.Arguments;
            psi.WorkingDirectory = plan.WorkDir;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            try
            {
                psi.StandardOutputEncoding = Encoding.UTF8;
                psi.StandardErrorEncoding = Encoding.UTF8;
            }
            catch
            {
            }

            Process process = new Process();
            process.StartInfo = psi;
            process.EnableRaisingEvents = true;
            process.OutputDataReceived += new DataReceivedEventHandler(OnGameOutput);
            process.ErrorDataReceived += new DataReceivedEventHandler(OnGameOutput);
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            Log.Write("游戏进程已启动，PID=" + process.Id);
            return process;
        }

        private static void OnGameOutput(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            Log.Write("[游戏] " + e.Data);
        }

        private static bool ContainsToken(List<string> list, string token)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], token, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public static string Replace(string text, Dictionary<string, string> map)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (text.IndexOf("${", StringComparison.Ordinal) < 0) return text;
            StringBuilder sb = new StringBuilder(text);
            foreach (KeyValuePair<string, string> kv in map)
            {
                sb.Replace("${" + kv.Key + "}", kv.Value == null ? "" : kv.Value);
            }
            return sb.ToString();
        }

        public static string Quote(string value)
        {
            if (value == null) return "\"\"";
            if (value.Length == 0) return "\"\"";
            if (value.IndexOf(' ') < 0 && value.IndexOf('"') < 0 && value.IndexOf('\t') < 0) return value;
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }

    public class JavaInfo
    {
        public string Path = "";
        public int Major = 0;
        public bool Is64Bit = true;
        public string VersionText = "";
    }

    public static class JavaFinder
    {
        private static List<JavaInfo> cached;
        private static DateTime cachedAt = DateTime.MinValue;

        /// <summary>新装了 Java 之后必须清缓存，否则 60 秒内仍然扫不到</summary>
        public static void Invalidate()
        {
            cached = null;
            cachedAt = DateTime.MinValue;
        }

        public static List<JavaInfo> Scan(bool force)
        {
            if (!force && cached != null && (DateTime.Now - cachedAt).TotalSeconds < 60) return cached;
            List<JavaInfo> result = new List<JavaInfo>();
            List<string> candidates = new List<string>();

            string javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
            if (!string.IsNullOrEmpty(javaHome)) candidates.Add(Path.Combine(javaHome, "bin", "java.exe"));

            string pathVar = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathVar))
            {
                string[] parts = pathVar.Split(';');
                for (int i = 0; i < parts.Length; i++)
                {
                    if (parts[i].Trim().Length == 0) continue;
                    try
                    {
                        candidates.Add(Path.Combine(parts[i].Trim().Trim('"'), "java.exe"));
                    }
                    catch
                    {
                    }
                }
            }

            // 注册表
            try
            {
                RegistryView[] views = new RegistryView[] { RegistryView.Registry64, RegistryView.Registry32 };
                string[] roots = new string[]
                {
                    "SOFTWARE\\JavaSoft\\Java Runtime Environment",
                    "SOFTWARE\\JavaSoft\\Java Development Kit",
                    "SOFTWARE\\JavaSoft\\JRE",
                    "SOFTWARE\\JavaSoft\\JDK"
                };
                for (int v = 0; v < views.Length; v++)
                {
                    using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, views[v]))
                    {
                        for (int r = 0; r < roots.Length; r++)
                        {
                            using (RegistryKey root = baseKey.OpenSubKey(roots[r]))
                            {
                                if (root == null) continue;
                                string current = root.GetValue("CurrentVersion") as string;
                                if (!string.IsNullOrEmpty(current))
                                {
                                    using (RegistryKey sub = root.OpenSubKey(current))
                                    {
                                        if (sub != null)
                                        {
                                            string home = sub.GetValue("JavaHome") as string;
                                            if (!string.IsNullOrEmpty(home)) candidates.Add(Path.Combine(home, "bin", "java.exe"));
                                        }
                                    }
                                }
                                string[] names = root.GetSubKeyNames();
                                for (int n = 0; n < names.Length; n++)
                                {
                                    using (RegistryKey sub = root.OpenSubKey(names[n]))
                                    {
                                        if (sub == null) continue;
                                        string home = sub.GetValue("JavaHome") as string;
                                        if (!string.IsNullOrEmpty(home)) candidates.Add(Path.Combine(home, "bin", "java.exe"));
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            // 常见安装目录
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string[] bases = new string[]
            {
                Path.Combine(pf, "Java"),
                Path.Combine(pf86, "Java"),
                Path.Combine(pf, "Eclipse Adoptium"),
                Path.Combine(pf, "Eclipse Foundation"),
                Path.Combine(pf, "Microsoft"),
                Path.Combine(pf, "Zulu"),
                Path.Combine(pf, "BellSoft"),
                Path.Combine(pf, "Amazon Corretto"),
                Path.Combine(pf, "Semeru"),
                Path.Combine(pf, "AdoptOpenJDK"),
                Path.Combine(local, "Programs", "Eclipse Adoptium"),
                Path.Combine(local, "Programs", "Java")
            };
            for (int i = 0; i < bases.Length; i++)
            {
                try
                {
                    if (!Directory.Exists(bases[i])) continue;
                    string[] dirs = Directory.GetDirectories(bases[i]);
                    for (int d = 0; d < dirs.Length; d++)
                    {
                        candidates.Add(Path.Combine(dirs[d], "bin", "java.exe"));
                    }
                }
                catch
                {
                }
            }

            // 官方启动器内置运行时
            try
            {
                string runtime = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft", "runtime");
                if (Directory.Exists(runtime))
                {
                    string[] kinds = Directory.GetDirectories(runtime);
                    for (int i = 0; i < kinds.Length; i++)
                    {
                        string[] versions = Directory.GetDirectories(kinds[i]);
                        for (int k = 0; k < versions.Length; k++)
                        {
                            candidates.Add(Path.Combine(versions[k], "bin", "java.exe"));
                            string[] platform = Directory.GetDirectories(versions[k]);
                            for (int p = 0; p < platform.Length; p++)
                            {
                                candidates.Add(Path.Combine(platform[p], "bin", "java.exe"));
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            // 启动器自己下载的运行时（.minecraft\runtime\java-17\...\bin\java.exe）
            try
            {
                ScanJavaDir(candidates, Paths.RuntimeDir, 0);
            }
            catch
            {
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                string p = candidates[i];
                if (string.IsNullOrEmpty(p)) continue;
                bool exists = false;
                try
                {
                    exists = File.Exists(p);
                }
                catch
                {
                }
                if (!exists) continue;
                bool duplicate = false;
                for (int k = 0; k < result.Count; k++)
                {
                    if (string.Equals(result[k].Path, p, StringComparison.OrdinalIgnoreCase))
                    {
                        duplicate = true;
                        break;
                    }
                }
                if (duplicate) continue;
                JavaInfo info = Inspect(p);
                if (info.Major > 0) result.Add(info);
            }

            result.Sort(delegate(JavaInfo a, JavaInfo b)
            {
                if (a.Major != b.Major) return b.Major.CompareTo(a.Major);
                return string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
            });
            cached = result;
            cachedAt = DateTime.Now;
            return result;
        }

        /// <summary>递归查找某个目录下的 bin\java.exe（自动下载的 JDK 压缩包有一层版本目录）</summary>
        private static void ScanJavaDir(List<string> candidates, string dir, int depth)
        {
            if (depth > 4 || string.IsNullOrEmpty(dir)) return;
            if (!Directory.Exists(dir)) return;
            try
            {
                string exe = Path.Combine(dir, "bin", "java.exe");
                if (File.Exists(exe)) candidates.Add(exe);
                string[] subs = Directory.GetDirectories(dir);
                for (int i = 0; i < subs.Length; i++) ScanJavaDir(candidates, subs[i], depth + 1);
            }
            catch
            {
            }
        }

        public static JavaInfo Inspect(string javaPath)
        {
            JavaInfo info = new JavaInfo();
            info.Path = javaPath;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = javaPath;
                psi.Arguments = "-version";
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardError = true;
                psi.RedirectStandardOutput = true;
                using (Process process = Process.Start(psi))
                {
                    string text = process.StandardError.ReadToEnd() + "\n" + process.StandardOutput.ReadToEnd();
                    process.WaitForExit(5000);
                    info.VersionText = FirstLine(text);
                    Match m = Regex.Match(text, "\"([^\"]+)\"");
                    string version = m.Success ? m.Groups[1].Value : "";
                    info.Major = ParseMajor(version);
                    info.Is64Bit = text.IndexOf("64-Bit", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch
            {
                info.Major = 0;
            }
            return info;
        }

        private static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string[] lines = text.Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim().Length > 0) return lines[i].Trim();
            }
            return "";
        }

        public static int ParseMajor(string version)
        {
            if (string.IsNullOrEmpty(version)) return 0;
            if (version.StartsWith("1."))
            {
                string[] parts = version.Split('.');
                if (parts.Length >= 2)
                {
                    int minor;
                    if (int.TryParse(parts[1], out minor)) return minor;
                }
                return 0;
            }
            Match m = Regex.Match(version, "^\\d+");
            if (m.Success)
            {
                int major;
                if (int.TryParse(m.Value, out major)) return major;
            }
            return 0;
        }

        /// <summary>挑选最合适的 Java：优先精确匹配所需大版本，其次更高的版本</summary>
        public static string FindBest(int requiredMajor, string preferred)
        {
            if (requiredMajor <= 0) requiredMajor = 8;
            if (!string.IsNullOrEmpty(preferred) && File.Exists(preferred))
            {
                JavaInfo prefer = Inspect(preferred);
                if (prefer.Major == requiredMajor) return preferred;
                // 只有目标版本本身需要 9+ 时，才允许用更高版本的 Java 顶替；
                // 旧版（1.12-）强行用 Java 17 会直接起不来。
                if (requiredMajor > 8 && prefer.Major > requiredMajor) return preferred;
            }

            List<JavaInfo> all = Scan(false);
            JavaInfo best = null;
            int bestScore = int.MaxValue;
            for (int i = 0; i < all.Count; i++)
            {
                JavaInfo info = all[i];
                int score;
                if (info.Major == requiredMajor) score = 0;
                // 1.12 及以前必须 Java 8：高版本 Java 会因为 LWJGL2 / 字节码版本直接崩，
                // 所以这里不拿 Java 17 去凑 Java 8 的版本，宁可返回空让上层自动下载。
                else if (requiredMajor <= 8) continue;
                else if (info.Major > requiredMajor) score = 1 + (info.Major - requiredMajor);
                else continue;
                if (!info.Is64Bit && Util.Is64Bit) score += 5;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = info;
                }
            }
            if (best != null) return best.Path;
            return "";
        }
    }
}
