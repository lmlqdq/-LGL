using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace Liuguang.Core
{
    /// <summary>一个 Java 下载源：真实地址 + 给用户看的来源名（写日志和进度用）</summary>
    public class JavaSource
    {
        public string Url = "";
        public string Label = "";

        public JavaSource()
        {
        }

        public JavaSource(string url, string label)
        {
            Url = url;
            Label = label;
        }
    }

    /// <summary>
    /// Java 运行时自动准备：先用本机已有的，没有就自动下载一个装进 .minecraft\runtime。
    ///
    /// 【下载源为什么换】
    /// 原来按「Adoptium 官方 → 清华 TUNA → 微软」的顺序试，实测下来这条线在本机几乎不可用：
    ///   华为云 OpenJDK   1.9 ~ 6.9 MB/s   一个 60MB 的包十几秒下完，国内 CDN
    ///   Azul Zulu CDN    0.8 MB/s         覆盖 Java 8 与 32 位系统，官方元数据 API 很稳定
    ///   Microsoft 官方   0.24 MB/s        仅 Java 11+
    ///   Adoptium 官方    ~0.02 MB/s       302 跳到 GitHub Releases，国内等于不可用
    ///   清华 TUNA                          目录页能打开，但 zip 直链无论带不带 Referer、
    ///                                      http 还是 https 一律 403，整条线路已删除
    /// 所以现在华为云排第一，Azul 第二（兜 Java 8 / 32 位），微软第三，Adoptium 只留作最后兜底。
    ///
    /// 【另一类失败：不是源的问题】
    /// 日志里出现过三个不同域名在两秒内全部 NameResolutionFailure，而同一时刻 5000 多个游戏资源
    /// 是下成功的。那是本地 DNS 解析器在高频查询后进入负缓存期的典型表现——已解析过的域名还能用，
    /// 新域名一律失败。这种情况换来换去都没用，所以这里连续两个源 DNS 失败就直接短路，
    /// 给出能操作的提示，而不是让用户对着一个源一个源地超时。
    /// </summary>
    public static class JavaInstaller
    {
        /// <summary>华为云 OpenJDK：国内 CDN，最快。只有 x64，且不含 Java 8</summary>
        private const string Huawei = "https://repo.huaweicloud.com/openjdk/";

        /// <summary>Azul Zulu 的 CDN 目录</summary>
        private const string AzulCdn = "https://cdn.azul.com/zulu/bin/";

        /// <summary>Azul 的元数据 API，能问到「当前最新的 zulu XX 的 jre zip 在哪」</summary>
        private const string AzulApi = "https://api.azul.com/metadata/v1/zulu/packages/?java_version=";

        /// <summary>确保拿到一个满足 major 的 java.exe，返回路径；失败抛异常</summary>
        public static string Ensure(int major, string preferred, ProgressHandler progress)
        {
            if (major <= 0) major = 8;

            // 1. 用户手动指定的优先，但必须是同一个大版本（旧版本用高版本 Java 会直接崩）
            if (!string.IsNullOrEmpty(preferred) && File.Exists(preferred))
            {
                JavaInfo prefer = JavaFinder.Inspect(preferred);
                if (prefer.Major == major || (major > 8 && prefer.Major > major)) return preferred;
                Log.Write("已指定的 Java " + prefer.Major + " 不满足本版本要求的 Java " + major + "，改为自动挑选");
            }

            // 2. 本机已有的
            string best = JavaFinder.FindBest(major, "");
            if (best.Length > 0)
            {
                if (progress != null) progress(0, 0, "使用本机 Java " + major + "：" + best);
                return best;
            }

            // 3. 都没有，自动下载
            return Install(major, progress);
        }

        /// <summary>下载并解压指定大版本的 Java 到 .minecraft\runtime\java-{major}</summary>
        public static string Install(int major, ProgressHandler progress)
        {
            if (major <= 0) major = 8;
            string dest = Path.Combine(Paths.RuntimeDir, "java-" + major);
            string marker = Path.Combine(dest, "java.path");

            string cached = ReadMarker(marker);
            if (cached.Length > 0) return cached;

            if (Directory.Exists(dest))
            {
                string existing = FindJavaExe(dest, 0);
                if (existing.Length > 0)
                {
                    Util.WriteText(marker, existing);
                    JavaFinder.Invalidate();
                    return existing;
                }
            }

            string zipDir = Path.Combine(Paths.RuntimeDir, "downloads");
            Util.EnsureDir(zipDir);
            string zip = Path.Combine(zipDir, "java-" + major + ".zip");
            string arch = Util.Is64Bit ? "x64" : "x86";

            List<JavaSource> sources = DirectSources(major, arch);
            Exception last = null;
            int dnsFails = 0;
            int index = 0;
            bool apiTried = false;

            // 直链跑完还不行，再花时间问 API 要最新地址（顺序即优先级）
            while (index < sources.Count)
            {
                JavaSource src = sources[index];
                index++;

                if (progress != null) progress(0, 0, "正在从 " + src.Label + " 下载 Java " + major +
                    "（第 " + index + "/" + sources.Count + " 个源）");
                Log.Write("开始下载 Java " + major + "（" + src.Label + "）：" + src.Url);

                try
                {
                    return DownloadAndExtract(src, zip, dest, marker, major, progress);
                }
                catch (Exception ex)
                {
                    last = ex;

                    // Downloader 内部已重试过 3 次（每次间隔几百毫秒），对 DNS 来说太短了。
                    // 这里对同一个源再等 4 秒重试一次：DNS 抖动通常几秒就能恢复。
                    if (Net.IsDnsFailure(ex))
                    {
                        Log.Write("Java " + major + " / " + src.Label + " 域名解析失败，4 秒后重试同一源");
                        System.Threading.Thread.Sleep(4000);
                        try
                        {
                            return DownloadAndExtract(src, zip, dest, marker, major, progress);
                        }
                        catch (Exception ex2)
                        {
                            last = ex2;
                        }
                    }

                    Log.Error("Java " + major + " / " + src.Label + " 失败：" + Net.ShortMessage(last));

                    if (Net.IsDnsFailure(last))
                    {
                        dnsFails++;
                        // 连续两个不同的域名都解析不了 → 是本机 DNS，不是源的问题，继续试只是浪费用户时间
                        if (dnsFails >= 2) throw DnsError(major, last);
                    }
                    else
                    {
                        dnsFails = 0;
                    }
                }

                if (index >= sources.Count && !apiTried)
                {
                    apiTried = true;
                    List<JavaSource> more = ApiSources(major, arch);
                    for (int k = 0; k < more.Count; k++) sources.Add(more[k]);
                }
            }

            throw new Exception("自动下载 Java " + major + " 失败（" + Net.ShortMessage(last) + "）。\r\n" +
                "已在 " + (apiTried ? sources.Count : index) + " 个下载源上试过。\r\n" +
                "点「导入本地 Java」可以直接使用已下载好的 JDK 压缩包或已装好的 java.exe，不需要联网。",
                last);
        }

        /// <summary>本机 DNS 出问题时的专用提示——这种时候换源没用，得让用户改 DNS 或走离线导入</summary>
        private static Exception DnsError(int major, Exception last)
        {
            return new Exception(
                "下载 Java " + major + " 失败：本机域名解析（DNS）无响应。\r\n\r\n" +
                "这不是下载源的问题。连续两个不同域名的解析都失败了，说明是本地 DNS 解析器的问题" +
                "（连续下载大量文件后很常见，已解析过的域名还能用，新域名一律失败）。\r\n\r\n" +
                "可以这样解决：\r\n" +
                "1）等 1~2 分钟再点一次「自动下载 Java」，DNS 负缓存会自动过期；\r\n" +
                "2）把网络适配器的 DNS 改成 223.5.5.5 或 119.29.29.29，再重试；\r\n" +
                "3）点「导入本地 Java」，选一个已经下载好的 JDK 压缩包（.zip）或已装好的 java.exe，" +
                "完全不依赖网络。",
                last);
        }

        /// <summary>
        /// 直链源，按实测速度排序。这里不发任何网络请求，纯拼地址。
        /// </summary>
        private static List<JavaSource> DirectSources(int major, string arch)
        {
            List<JavaSource> list = new List<JavaSource>();
            bool x64 = !string.Equals(arch, "x86", StringComparison.OrdinalIgnoreCase);

            // 1) 华为云 OpenJDK：最快，但只有 x64，且 Java 9 以下没有
            if (x64 && major >= 9)
            {
                list.Add(new JavaSource(Huawei + major + "/openjdk-" + major + "_windows-x64_bin.zip", "华为云"));
                list.Add(new JavaSource(Huawei + major + ".0.2/openjdk-" + major + ".0.2_windows-x64_bin.zip", "华为云(旧版目录)"));
            }

            // 2) Azul Zulu 的固定直链：覆盖 8 / 21 / 25，也就是 MC 用得最多的三个大版本
            string zulu = AzulPinned(major, x64);
            if (zulu.Length > 0) list.Add(new JavaSource(zulu, "Azul Zulu"));

            // 3) 微软，11 起才有
            if (x64 && major >= 11)
                list.Add(new JavaSource("https://aka.ms/download-jdk/microsoft-jdk-" + major + "-windows-x64.zip", "Microsoft"));

            return list;
        }

        /// <summary>直链全挂之后才调用的 API 解析源</summary>
        private static List<JavaSource> ApiSources(int major, string arch)
        {
            List<JavaSource> list = new List<JavaSource>();
            string zulu = ResolveAzul(major, arch);
            if (zulu.Length > 0) list.Add(new JavaSource(zulu, "Azul Zulu(API)"));
            list.Add(new JavaSource("https://api.adoptium.net/v3/binary/latest/" + major + "/ga/windows/" + arch +
                "/jre/hotspot/normal/eclipse", "Adoptium"));
            return list;
        }

        /// <summary>Azul 的固定直链。版本号写死是为了省掉一次 API 往返，失败自然会退到 API 解析。</summary>
        private static string AzulPinned(int major, bool x64)
        {
            if (!x64) return "";
            if (major == 8) return AzulCdn + "zulu8.98.0.107-ca-jre8.0.512-win_x64.zip";
            if (major == 21) return AzulCdn + "zulu21.54.109-ca-jre21.0.13.0.3-win_x64.zip";
            if (major == 25) return AzulCdn + "zulu25.38.109-ca-jre25.0.5.0.3-win_x64.zip";
            return "";
        }

        /// <summary>问 Azul 元数据 API 要最新的 JRE zip（优先不带 JavaFX 的普通 JRE，体积小一半）</summary>
        private static string ResolveAzul(int major, string arch)
        {
            try
            {
                string url = AzulApi + major + "&os=windows&arch=" + (string.Equals(arch, "x86", StringComparison.OrdinalIgnoreCase) ? "x86" : "x86_64") +
                    "&archive_type=zip&java_package_type=jre&latest=true&release_status=ga";
                string json = Net.GetString(url, 1);
                MatchCollection matches = Regex.Matches(json, "\"download_url\"\\s*:\\s*\"([^\"]+)\"");
                string withFx = "";
                for (int i = 0; i < matches.Count; i++)
                {
                    string candidate = matches[i].Groups[1].Value.Replace("\\/", "/");
                    if (candidate.Length == 0) continue;
                    // 带 -fx- 的是 JavaFX 完整包，体积大一倍，MC 用不上
                    if (candidate.IndexOf("-fx-", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (withFx.Length == 0) withFx = candidate;
                        continue;
                    }
                    return candidate;
                }
                return withFx;
            }
            catch (Exception ex)
            {
                Log.Write("查询 Azul 最新版本失败：" + ex.Message);
                return "";
            }
        }

        private static string DownloadAndExtract(JavaSource src, string zip, string dest, string marker, int major, ProgressHandler progress)
        {
            // 本地已经有一个像样的安装包就直接用，不再重下。
            // 场景：上次下完没解压完就退出、或者把手动下的包丢进了 runtime\downloads。
            long existing = 0;
            try
            {
                if (File.Exists(zip)) existing = new FileInfo(zip).Length;
            }
            catch
            {
            }

            if (existing >= 1024 * 1024)
            {
                if (progress != null) progress(0, 0, "使用已下载的 Java " + major + " 安装包（" + Util.FormatSize(existing) + "）");
                Log.Write("复用已下载的安装包：" + zip + "（" + Util.FormatSize(existing) + "）");
            }
            else
            {
                if (progress != null) progress(0, 0, "从 " + src.Label + " 下载 Java " + major + "（约 40-60 MB）");
                TryDelete(zip);
                List<DownloadItem> one = new List<DownloadItem>();
                one.Add(new DownloadItem(src.Url, zip, "", 0));
                Downloader.Run(one, 1, null);
            }

            long size = 0;
            try
            {
                size = new FileInfo(zip).Length;
            }
            catch
            {
            }
            if (size < 1024 * 1024) throw new Exception("下载内容不完整（" + size + " 字节）");

            if (progress != null) progress(0, 0, "解压 Java " + major + "，请稍候");
            TryDeleteDir(dest);
            Util.EnsureDir(dest);
            try
            {
                ZipFile.ExtractToDirectory(zip, dest);
            }
            catch (Exception ex)
            {
                // 包坏了就删掉，让下一个源重新下一份，别让坏包一直挡在前面
                TryDelete(zip);
                throw new Exception("压缩包解压失败（可能下载不完整）：" + ex.Message);
            }

            string found = FindJavaExe(dest, 0);
            if (found.Length == 0)
            {
                TryDeleteDir(dest);
                throw new Exception("压缩包内没有 bin\\java.exe");
            }

            Util.WriteText(marker, found);
            JavaFinder.Invalidate();
            TryDelete(zip);
            Log.Write("Java " + major + " 已就绪（来源 " + src.Label + "）：" + found);
            if (progress != null) progress(1, 1, "Java " + major + " 已就绪");
            return found;
        }

        /// <summary>
        /// 离线导入：用户自己下载的 JDK 压缩包，或者已经装好的 java.exe。
        /// 完全不联网，用于下载源全都不通时的兜底。
        /// </summary>
        public static string Import(string path, int major, ProgressHandler progress)
        {
            if (major <= 0) major = 8;
            if (string.IsNullOrEmpty(path)) throw new Exception("没有选择文件");
            if (!File.Exists(path) && !Directory.Exists(path)) throw new Exception("找不到：" + path);

            string dest = Path.Combine(Paths.RuntimeDir, "java-" + major);

            if (progress != null) progress(0, 0, "正在导入 Java " + major + "…");
            TryDeleteDir(dest);
            Util.EnsureDir(dest);

            if (File.Exists(path) && string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    ZipFile.ExtractToDirectory(path, dest);
                }
                catch (Exception ex)
                {
                    TryDeleteDir(dest);
                    throw new Exception("解压失败：" + ex.Message + "\r\n请确认这是 Windows 版的 JDK/JRE 压缩包（..._windows-x64_bin.zip 这种）。");
                }
            }
            else
            {
                // 选的是已解压目录或直接选了 java.exe：定位到 JRE 根目录再整棵复制进来，
                // 这样 JavaFinder 扫描 runtime 时也能看到它，界面上才选得到。
                string root = path;
                if (File.Exists(path))
                {
                    string parent = Path.GetDirectoryName(path);
                    if (parent != null && string.Equals(Path.GetFileName(parent), "bin", StringComparison.OrdinalIgnoreCase))
                        parent = Path.GetDirectoryName(parent);
                    root = parent;
                }
                if (root == null || !Directory.Exists(root))
                {
                    TryDeleteDir(dest);
                    throw new Exception("找不到 Java 目录，请选 java.exe 或整个 JRE 文件夹。");
                }

                if (progress != null) progress(0, 0, "正在复制 Java 文件，约需十几秒…");
                CopyDir(root, dest);
            }

            string found = FindJavaExe(dest, 0);
            if (found.Length == 0)
            {
                TryDeleteDir(dest);
                throw new Exception("这个包里没有 bin\\java.exe，不像是 Windows 版的 JDK/JRE。\r\n" +
                    "请确认下载的是 Windows x64 版本，而不是 linux / macos 的包。");
            }

            // 用户导入的包版本未必等于调用方要的版本（比如需要 25 却导了个 21）。
            // 按实际版本重新归位到 java-{实际大版本}，这样以后要 21 的版本也能直接命中，
            // 而不是把 21 塞进 java-25 里、下次启动才崩。
            int realMajor = 0;
            try
            {
                JavaInfo info = JavaFinder.Inspect(found);
                realMajor = info.Major;
            }
            catch
            {
            }
            if (realMajor > 0 && realMajor != major)
            {
                string realDest = Path.Combine(Paths.RuntimeDir, "java-" + realMajor);
                try
                {
                    TryDeleteDir(realDest);
                    Directory.Move(dest, realDest);
                    dest = realDest;
                    found = FindJavaExe(dest, 0);
                    Log.Write("导入的是 Java " + realMajor + "，已归位到 " + realDest);
                }
                catch (Exception ex)
                {
                    Log.Write("Java 目录改名为 java-" + realMajor + " 失败：" + ex.Message);
                }
            }

            Util.WriteText(Path.Combine(dest, "java.path"), found);
            JavaFinder.Invalidate();
            Log.Write("已导入 Java " + major + "：" + found);
            if (progress != null) progress(1, 1, "Java " + major + " 导入完成");
            return found;
        }

        private static void CopyDir(string from, string to)
        {
            Util.EnsureDir(to);
            string[] files = Directory.GetFiles(from);
            for (int i = 0; i < files.Length; i++)
            {
                try
                {
                    File.Copy(files[i], Path.Combine(to, Path.GetFileName(files[i])), true);
                }
                catch
                {
                    // 个别文件（锁定的 dll / 符号链接）复制不了不算致命，只要 bin\java.exe 在就行
                }
            }
            string[] dirs = Directory.GetDirectories(from);
            for (int i = 0; i < dirs.Length; i++)
            {
                CopyDir(dirs[i], Path.Combine(to, Path.GetFileName(dirs[i])));
            }
        }

        public static string FindJavaExe(string dir, int depth)
        {
            if (depth > 4 || string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return "";
            try
            {
                string exe = Path.Combine(dir, "bin", "java.exe");
                if (File.Exists(exe)) return exe;
                string[] subs = Directory.GetDirectories(dir);
                for (int i = 0; i < subs.Length; i++)
                {
                    string found = FindJavaExe(subs[i], depth + 1);
                    if (found.Length > 0) return found;
                }
            }
            catch
            {
            }
            return "";
        }

        /// <summary>某个大版本是否已经下好（用于 UI 提示）</summary>
        public static bool IsReady(int major)
        {
            if (major <= 0) return false;
            string dest = Path.Combine(Paths.RuntimeDir, "java-" + major);
            if (ReadMarker(Path.Combine(dest, "java.path")).Length > 0) return true;
            return FindJavaExe(dest, 0).Length > 0;
        }

        private static string ReadMarker(string marker)
        {
            try
            {
                if (!File.Exists(marker)) return "";
                string path = File.ReadAllText(marker).Trim();
                if (path.Length > 0 && File.Exists(path)) return path;
            }
            catch
            {
            }
            return "";
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
            }
        }

        private static void TryDeleteDir(string path)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }
            catch
            {
            }
        }
    }
}
