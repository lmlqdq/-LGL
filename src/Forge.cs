using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Liuguang.Core
{
    /// <summary>
    /// Forge 加载器安装器。
    ///
    /// 对标 PCL2 / HMCL 的完整安装流程，而不是「只下载一个 universal jar 糊弄过去」：
    ///   1. 从官方 promotions_slim.json 拿到目标 MC 版本对应的 Forge 版本号
    ///   2. 下载 forge-{mc}-{ver}-installer.jar
    ///   3. 解压其中的 version.json（最终版本描述）和 install_profile.json（补丁流水线）
    ///   4. 把 version.json 写成继承原版的版本描述，复用 VersionInstaller 下齐所有库
    ///   5. 执行 install_profile.json 里的 client 侧 processor —— 用系统 Java 跑
    ///      jarsplitter / ForgeAutoRenamingTool / binarypatcher，把原版 client.jar
    ///      打成带 Forge 补丁的最终 client.jar
    ///
    /// 为什么必须跑 processor：Forge 1.13+ 的最终 client.jar 不在 maven 上（404），
    /// 它是把 installer 里的 data/client.lzma 与原版 jar 做二进制补丁实时生成的。
    /// 只下 libraries 不跑 processor，启动时 classpath 里会少 net.minecraftforge:forge，
    /// 游戏直接起不来。
    /// </summary>
    public static class ForgeInstaller
    {
        public static string Install(string mcVersion, Config cfg, ProgressHandler progress)
        {
            Paths.EnsureBaseDirs();

            // 1. 原版基础
            if (progress != null) progress(0, 3, "安装原版 " + mcVersion);
            VersionInstaller.Install(mcVersion, cfg, progress);

            // 2. 拿 Forge 版本号
            if (progress != null) progress(1, 3, "获取 Forge 版本列表");
            string forgeVersion = ResolveForgeVersion(mcVersion);

            // 3. 下载 installer jar
            string installerUrl = "https://maven.minecraftforge.net/net/minecraftforge/forge/"
                + mcVersion + "-" + forgeVersion + "/forge-" + mcVersion + "-" + forgeVersion + "-installer.jar";
            string installerPath = Path.Combine(Paths.McDir, "cache", "forge-" + mcVersion + "-" + forgeVersion + "-installer.jar");
            Util.EnsureDir(Path.GetDirectoryName(installerPath));
            if (!File.Exists(installerPath))
            {
                if (progress != null) progress(1, 3, "下载 Forge 安装器");
                List<DownloadItem> dl = new List<DownloadItem>();
                dl.Add(new DownloadItem(installerUrl, installerPath, "", 0));
                Downloader.Run(dl, 1, progress);
            }

            // 4. 解压 version.json + install_profile.json
            if (progress != null) progress(2, 3, "解析安装描述");
            string versionJson, installProfile;
            ExtractInstaller(installerPath, out versionJson, out installProfile);

            Json version = Json.Parse(versionJson);
            string id = version["id"].AsString("");
            if (id.Length == 0) id = mcVersion + "-forge-" + forgeVersion;

            // 5. 写版本描述（复用 VersionInstaller 下载所有 libraries + 原版继承）
            Util.EnsureDir(Paths.VersionDir(id));
            Util.WriteText(Paths.VersionJsonPath(id), versionJson);
            Log.Write("已写入 Forge 版本描述：" + id);
            VersionInstaller.Install(id, cfg, progress);

            // 6. 执行 client processor 流水线，生成最终的 client.jar
            if (progress != null) progress(2, 3, "应用 Forge 补丁（二进制补丁）");
            Json profile = Json.Parse(installProfile);
            ProcessorRunner.Run(profile, version, installerPath, cfg, mcVersion, progress);

            return id;
        }

        private static string ResolveForgeVersion(string mcVersion)
        {
            string url = "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json";
            Json root = Net.GetJson(url);
            string key = mcVersion + "-latest";
            string v = root["promos"][key].AsString("");
            if (v.Length == 0) key = mcVersion + "-recommended";
            if (v.Length == 0) v = root["promos"][key].AsString("");
            if (v.Length == 0)
                throw new Exception("Forge 暂不支持 Minecraft " + mcVersion + "，请换一个版本");
            return v;
        }

        private static void ExtractInstaller(string installerPath, out string versionJson, out string installProfile)
        {
            versionJson = "";
            installProfile = "";
            using (ZipArchive zip = ZipFile.OpenRead(installerPath))
            {
                ZipArchiveEntry v = zip.GetEntry("version.json");
                if (v == null) throw new Exception("安装器缺少 version.json");
                using (StreamReader reader = new StreamReader(v.Open(), Encoding.UTF8))
                    versionJson = reader.ReadToEnd();

                ZipArchiveEntry p = zip.GetEntry("install_profile.json");
                if (p == null) throw new Exception("安装器缺少 install_profile.json");
                using (StreamReader reader = new StreamReader(p.Open(), Encoding.UTF8))
                    installProfile = reader.ReadToEnd();
            }
        }
    }

    /// <summary>
    /// 执行 Forge install_profile.json 里的 processor 流水线。
    ///
    /// 每个 processor 是一个「用 Java 运行某个工具 jar，带一串参数」的任务，
    /// 参数里的 {XXX} 是占位符，要先替换成实际路径 / maven 坐标。
    /// 这是 Forge 安装最难也最关键的一环 —— 缺了它最终 client.jar 就不会生成。
    /// </summary>
    internal static class ProcessorRunner
    {
        public static void Run(Json profile, Json version, string installerPath, Config cfg, string mcVersion, ProgressHandler progress)
        {
            string java = ResolveJava(cfg);
            string root = Paths.McDir;
            string libsDir = Paths.LibrariesDir;
            string minecraftJar = Path.Combine(Paths.VersionDir(mcVersion), mcVersion + ".jar");

            // 数据映射：占位符 -> 实际值。client 侧下载 maven 坐标 / 解压 installer 内文件
            Dictionary<string, string> data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Json dataObj = profile["data"];
            if (dataObj.IsObject)
            {
                foreach (KeyValuePair<string, Json> kv in dataObj.Props)
                {
                    string clientVal = kv.Value["client"].AsString("");
                    if (clientVal.Length > 0) data[kv.Key] = clientVal;
                }
            }

            List<Json> processors = profile["processors"].AsArray();
            for (int i = 0; i < processors.Count; i++)
            {
                Json p = processors[i];
                if (!NeedsClient(p)) continue;

                string jar = p["jar"].AsString("");
                List<string> args = ResolveArgs(p, data, root, libsDir, installerPath, minecraftJar, java);

                if (progress != null)
                    progress(2, 3, "补丁 " + (i + 1) + "/" + processors.Count + "：" + ShortJar(jar));

                RunJava(java, jar, p["classpath"].AsArray(), args, libsDir);
            }
        }

        private static bool NeedsClient(Json p)
        {
            List<Json> sides = p["sides"].AsArray();
            if (sides.Count == 0) return true; // sides 缺省 = 两侧都要
            for (int i = 0; i < sides.Count; i++)
            {
                if (sides[i].AsString("") == "client") return true;
            }
            return false;
        }

        private static List<string> ResolveArgs(Json p, Dictionary<string, string> data,
            string root, string libsDir, string installerPath, string minecraftJar, string java)
        {
            List<Json> raw = p["args"].AsArray();
            List<string> result = new List<string>();
            for (int i = 0; i < raw.Count; i++)
            {
                string a = raw[i].AsString("");
                result.Add(Resolve(a, data, root, libsDir, installerPath, minecraftJar, java));
            }
            return result;
        }

        /// <summary>把一个参数里的占位符全部替换成实际值。</summary>
        private static string Resolve(string s, Dictionary<string, string> data,
            string root, string libsDir, string installerPath, string minecraftJar, string java)
        {
            if (s == null) return s;

            // {INSTALLER} / {ROOT} / {MINECRAFT_JAR} / {LIBRARIES_DIR}
            s = s.Replace("{INSTALLER}", installerPath);
            s = s.Replace("{ROOT}", root);
            s = s.Replace("{MINECRAFT_JAR}", minecraftJar);
            s = s.Replace("{LIBRARIES_DIR}", libsDir);

            // {SIDE} = client（客户端安装）
            s = s.Replace("{SIDE}", "client");

            // {KEY} -> data["KEY"]（可能又是 maven 坐标或 installer 内路径）
            int open = s.IndexOf('{');
            while (open >= 0)
            {
                int close = s.IndexOf('}', open + 1);
                if (close < 0) break;
                string key = s.Substring(open + 1, close - open - 1);
                string val;
                if (data.TryGetValue(key, out val))
                {
                    val = ResolveData(key, val, data, libsDir, installerPath);
                }
                else
                {
                    val = "{" + key + "}";
                }
                s = s.Substring(0, open) + val + s.Substring(close + 1);
                open = s.IndexOf('{', open + val.Length);
            }

            // 参数里直接写的 maven 坐标（如 processor[3] 的 --input [de.oceanlabs.mcp:mcp_config:...@zip]）
            // 是「下载源」，要真正下载并替换成本地路径。
            if (s.Length > 0 && s[0] == '[' && s[s.Length - 1] == ']')
            {
                string coord = s.Substring(1, s.Length - 2);
                s = ResolveMaven(coord, libsDir, true);
            }

            return s;
        }

        /// <summary>
        /// data 里的值有三种：
        ///   - "[net.minecraftforge:forge:1.20.1-47.3.0:client]" 这类 maven 坐标 ——
        ///     大多是 processor 之间的「输出路径 / 中间产物」，不在这里下载，
        ///     只把坐标解析成本地路径；真正需要下载的输入（mcp_config zip 等）
        ///     是在 args 里以 --input [坐标] 形式出现的，由 Resolve 单独下载。
        ///   - "/data/client.lzma" 这类 installer 内部路径 → 从 installer jar 解压到缓存，
        ///     返回真实文件路径（binarypatcher 的 --apply 需要真实文件）。
        ///   - "'literal'" → 去掉引号。
        /// </summary>
        private static string ResolveData(string key, string val, Dictionary<string, string> data, string libsDir, string installerPath)
        {
            if (val.StartsWith("[") && val.EndsWith("]"))
            {
                string coord = val.Substring(1, val.Length - 2);
                return MavenLocalPath(coord, libsDir);
            }
            if (val.StartsWith("'") && val.EndsWith("'"))
            {
                return val.Substring(1, val.Length - 2);
            }
            if (val.StartsWith("/"))
            {
                // installer 内部路径（如 /data/client.lzma）→ 解压到缓存目录
                return ExtractInstallerFile(installerPath, val.TrimStart('/'));
            }
            return val;
        }

        /// <summary>从 installer jar 里解压一个文件到缓存目录，返回本地路径。</summary>
        private static string ExtractInstallerFile(string installerPath, string entryName)
        {
            string cacheDir = Path.Combine(Paths.McDir, "cache", "forge-extract");
            Util.EnsureDir(cacheDir);
            string dest = Path.Combine(cacheDir, entryName.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(dest)) return dest;

            Util.EnsureDir(Path.GetDirectoryName(dest));
            using (ZipArchive zip = ZipFile.OpenRead(installerPath))
            {
                ZipArchiveEntry entry = zip.GetEntry(entryName);
                if (entry == null) return dest;
                using (Stream input = entry.Open())
                using (FileStream output = File.Create(dest))
                {
                    input.CopyTo(output);
                }
            }
            return dest;
        }

        /// <summary>maven 坐标 → 本地库文件路径（仅算路径，不下载）。</summary>
        private static string MavenLocalPath(string coord, string libsDir)
        {
            string ext = "jar";
            int at = coord.IndexOf('@');
            if (at >= 0)
            {
                ext = coord.Substring(at + 1);
                coord = coord.Substring(0, at);
            }
            string[] parts = coord.Split(':');
            if (parts.Length < 3) return coord;
            string group = parts[0];
            string artifact = parts[1];
            string version = parts[2];
            string classifier = parts.Length > 3 ? parts[3] : "";

            string rel = group.Replace('.', '/') + "/" + artifact + "/" + version + "/"
                + artifact + "-" + version + (classifier.Length > 0 ? "-" + classifier : "") + "." + ext;
            return Path.Combine(libsDir, rel.Replace('/', Path.DirectorySeparatorChar));
        }

        /// <summary>
        /// maven 坐标 group:artifact:version[:classifier][@ext] → 本地库文件路径（不存在则下载）。
        /// Forge 的 net.minecraft:client 中间产物不在 maven 上，所以只有显式需要下载的
        /// （processor 工具 jar、classpath 依赖、mcp_config zip 等）才走这里。
        /// </summary>
        private static string ResolveMaven(string coord, string libsDir, bool download)
        {
            string local = MavenLocalPath(coord, libsDir);
            if (!download) return local;
            if (File.Exists(local)) return local;

            string ext = "jar";
            int at = coord.IndexOf('@');
            if (at >= 0)
            {
                ext = coord.Substring(at + 1);
                coord = coord.Substring(0, at);
            }
            string[] parts = coord.Split(':');
            if (parts.Length < 3) return coord;
            string rel = parts[0].Replace('.', '/') + "/" + parts[1] + "/" + parts[2] + "/"
                + parts[1] + "-" + parts[2] + (parts.Length > 3 ? "-" + parts[3] : "") + "." + ext;

            string url = "https://maven.minecraftforge.net/" + rel;
            Util.EnsureDir(Path.GetDirectoryName(local));
            List<DownloadItem> dl = new List<DownloadItem>();
            dl.Add(new DownloadItem(url, local, "", 0));
            Downloader.Run(dl, 1, null);
            return local;
        }

        /// <summary>执行一个 processor：java -cp <classpath> <main> <args></summary>
        private static void RunJava(string java, string jar, List<Json> classpath, List<string> args, string libsDir)
        {
            List<string> cp = new List<string>();
            cp.Add(ResolveMaven(jar, libsDir, true));
            for (int i = 0; i < classpath.Count; i++)
            {
                cp.Add(ResolveMaven(classpath[i].AsString(""), libsDir, true));
            }
            string cpStr = string.Join(";", cp.ToArray());

            string argLine = "";
            for (int i = 0; i < args.Count; i++)
            {
                argLine += " \"" + args[i].Replace("\"", "\\\"") + "\"";
            }

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = java;
            psi.Arguments = "-cp \"" + cpStr + "\" " + MainClass(jar) + argLine;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.WorkingDirectory = Paths.McDir;

            Log.Write("执行补丁：" + ShortJar(jar));
            using (Process process = Process.Start(psi))
            {
                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    throw new Exception("Forge 补丁执行失败（" + ShortJar(jar) + "，退出码 " + process.ExitCode + "）："
                        + FirstNonEmpty(stderr, stdout));
                }
                string tail = FirstNonEmpty(stderr, stdout);
                if (tail.Length > 0) Log.Write("补丁输出：" + tail.Substring(0, Math.Min(200, tail.Length)));
            }
        }

        /// <summary>
        /// processor jar 的主类。用 -cp 运行时 Java 不会读 MANIFEST 的 Main-Class，
        /// 必须显式指定。这里按 Forge 官方工具的已知主类映射，未知的退回从 MANIFEST 读。
        /// </summary>
        private static string MainClass(string jar)
        {
            string lower = (jar == null ? "" : jar).ToLowerInvariant();
            if (lower.IndexOf("installertools") >= 0) return "net.minecraftforge.installertools.ConsoleTool";
            if (lower.IndexOf("jarsplitter") >= 0) return "net.minecraftforge.jarsplitter.ConsoleTool";
            if (lower.IndexOf("autorenamingtool") >= 0 || lower.IndexOf("fart") >= 0) return "net.minecraftforge.fart.Main";
            if (lower.IndexOf("binarypatcher") >= 0) return "net.minecraftforge.binarypatcher.ConsoleTool";
            return ReadMainClass(jar);
        }

        /// <summary>从 jar 的 MANIFEST.MF 读 Main-Class，作为未知工具的兜底。</summary>
        private static string ReadMainClass(string jarCoord)
        {
            string jarPath = ResolveMaven(jarCoord, Paths.LibrariesDir, true);
            try
            {
                using (ZipArchive zip = ZipFile.OpenRead(jarPath))
                {
                    ZipArchiveEntry mf = zip.GetEntry("META-INF/MANIFEST.MF");
                    if (mf == null) return "";
                    using (StreamReader reader = new StreamReader(mf.Open(), Encoding.UTF8))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            if (line.StartsWith("Main-Class:", StringComparison.OrdinalIgnoreCase))
                            {
                                return line.Substring("Main-Class:".Length).Trim();
                            }
                        }
                    }
                }
            }
            catch
            {
            }
            return "";
        }

        private static string ShortJar(string jar)
        {
            if (jar == null) return "";
            int colon = jar.LastIndexOf(':');
            if (colon >= 0 && colon + 1 < jar.Length) return jar.Substring(colon + 1);
            return jar;
        }

        private static string FirstNonEmpty(string a, string b)
        {
            if (!string.IsNullOrEmpty(a)) return a.Trim();
            if (!string.IsNullOrEmpty(b)) return b.Trim();
            return "";
        }

        private static string ResolveJava(Config cfg)
        {
            // 优先用户配置的 Java，其次系统 Java（Forge 1.17+ 需要 Java 17，1.20+ 需要 Java 17+）
            if (!string.IsNullOrEmpty(cfg.JavaPath) && File.Exists(cfg.JavaPath)) return cfg.JavaPath;
            string best = JavaFinder.FindBest(17, "");
            if (best.Length > 0 && File.Exists(best)) return best;
            best = JavaFinder.FindBest(8, "");
            if (best.Length > 0 && File.Exists(best)) return best;
            throw new Exception("未找到可用的 Java，请先在设置里配置 Java 或安装 Java 运行时");
        }
    }
}
