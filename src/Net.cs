using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Liuguang.Core
{
    /// <summary>进度回调：done=已完成数量，total=总数量，message=当前动作描述</summary>
    public delegate void ProgressHandler(int done, int total, string message);

    public static class Log
    {
        private static readonly object Gate = new object();

        public static string FilePath = "";

        public static event Action<string> Message;

        public static void Write(string text)
        {
            string line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + text;
            lock (Gate)
            {
                try
                {
                    if (FilePath.Length > 0) File.AppendAllText(FilePath, line + Environment.NewLine, Encoding.UTF8);
                }
                catch
                {
                }
            }
            Action<string> handler = Message;
            if (handler != null)
            {
                try
                {
                    handler(line);
                }
                catch
                {
                }
            }
        }

        public static void Error(string text)
        {
            Write("[错误] " + text);
        }
    }

    public static class Hashing
    {
        public static string Sha1File(string path)
        {
            using (FileStream fs = File.OpenRead(path))
            {
                using (SHA1 sha = SHA1.Create())
                {
                    byte[] hash = sha.ComputeHash(fs);
                    return ToHex(hash);
                }
            }
        }

        public static string ToHex(byte[] data)
        {
            StringBuilder sb = new StringBuilder(data.Length * 2);
            for (int i = 0; i < data.Length; i++) sb.Append(data[i].ToString("x2"));
            return sb.ToString();
        }

        /// <summary>与 Java 的 UUID.nameUUIDFromBytes(("OfflinePlayer:" + name)) 完全一致</summary>
        public static string OfflineUuid(string playerName)
        {
            byte[] input = Encoding.UTF8.GetBytes("OfflinePlayer:" + playerName);
            using (MD5 md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(input);
                hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
                hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
                StringBuilder sb = new StringBuilder(36);
                for (int i = 0; i < 16; i++)
                {
                    if (i == 4 || i == 6 || i == 8 || i == 10) sb.Append('-');
                    sb.Append(hash[i].ToString("x2"));
                }
                return sb.ToString();
            }
        }
    }

    public static class Net
    {
        public const string DefaultUserAgent = "LiuguangLauncher/1.1.1 (Minecraft Java Launcher; +https://github.com/liuguang-launcher)";

        public static string UserAgent = DefaultUserAgent;
        public static bool UseMirror = false;
        public static int TimeoutMs = 30000;

        private static bool initialized = false;

        public static void Init()
        {
            if (initialized) return;
            initialized = true;
            try
            {
                ServicePointManager.SecurityProtocol =
                    SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            }
            catch
            {
            }
            try
            {
                ServicePointManager.DefaultConnectionLimit = 64;
                ServicePointManager.Expect100Continue = false;
            }
            catch
            {
            }
        }

        public const string MirrorHost = "https://bmclapi2.bangbang93.com";

        /// <summary>
        /// 把官方下载地址改写为国内镜像（BMCLAPI）。
        ///
        /// 注意 piston-data 不能整站替换：镜像站只收录了 v1/objects 下的 jar 大文件，
        /// log4j 配置（client-1.21.2.xml）这类几百字节的小文件在镜像上是 404。
        /// 之前无条件替换域名，就导致安装 1.21.2 时卡在 client-1.21.2.xml 报 404。
        /// 所以这里按文件类型判断，非 jar 保持官方地址；即便判断错了，
        /// 下载器还有「镜像失败自动回落官方源」的兜底。
        /// </summary>
        public static string FixUrl(string url)
        {
            if (!UseMirror || url == null || url.Length == 0) return url;
            string result = url;
            result = result.Replace("https://launchermeta.mojang.com", MirrorHost);
            result = result.Replace("https://piston-meta.mojang.com", MirrorHost);
            result = result.Replace("https://resources.download.minecraft.net", MirrorHost + "/assets");
            result = result.Replace("https://libraries.minecraft.net", MirrorHost + "/maven");
            result = result.Replace("https://meta.fabricmc.net", MirrorHost + "/fabric-meta");
            result = result.Replace("https://maven.fabricmc.net", MirrorHost + "/maven");
            result = result.Replace("https://maven.minecraftforge.net", MirrorHost + "/maven");
            result = result.Replace("https://files.minecraftforge.net", MirrorHost + "/forge");
            // 两个 objects 域名（launcher./piston-data.）只镜 jar：1.8.9 的 client-1.7.xml
            // 和 1.21.2 的 client-1.21.2.xml 在镜像上都是 404，实测确认过。
            if (IsMirroredObject(url))
            {
                result = result.Replace("https://piston-data.mojang.com", MirrorHost);
                result = result.Replace("https://launcher.mojang.com", MirrorHost);
            }
            return result;
        }

        /// <summary>objects 域名上的 jar 镜像站有收录，其它扩展名（xml/txt/json）没有</summary>
        private static bool IsMirroredObject(string url)
        {
            bool objectsHost = url.IndexOf("piston-data.mojang.com", StringComparison.OrdinalIgnoreCase) >= 0
                || url.IndexOf("launcher.mojang.com", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!objectsHost) return false;
            int slash = url.LastIndexOf('/');
            if (slash < 0) return false;
            string name = url.Substring(slash + 1).ToLowerInvariant();
            int query = name.IndexOf('?');
            if (query >= 0) name = name.Substring(0, query);
            return name.EndsWith(".jar");
        }

        /// <summary>候选地址：镜像优先，官方兜底（镜像缺档时不会再整批失败）</summary>
        public static string[] Candidates(string url)
        {
            string primary = FixUrl(url);
            if (string.Equals(primary, url, StringComparison.OrdinalIgnoreCase)) return new string[] { url };
            return new string[] { primary, url };
        }

        /// <summary>404/403/410 属于永久失败，重试没有意义</summary>
        public static bool IsPermanent(Exception ex)
        {
            if (ex == null) return false;
            WebException we = ex as WebException;
            if (we == null) return false;
            HttpWebResponse resp = we.Response as HttpWebResponse;
            if (resp == null) return false;
            int code = (int)resp.StatusCode;
            return code == 404 || code == 403 || code == 410;
        }

        /// <summary>
        /// 是否是域名解析失败。要顺着 InnerException 一层层找：Downloader 会在外面包一层
        /// 「有 1 个文件下载失败：……」，真正的原因藏在里面。
        /// 这类失败换源是没用的，调用方应该换策略（等一会 / 让用户改 DNS / 走离线导入）。
        /// </summary>
        public static bool IsDnsFailure(Exception ex)
        {
            for (Exception e = ex; e != null; e = e.InnerException)
            {
                WebException we = e as WebException;
                if (we != null && we.Status == WebExceptionStatus.NameResolutionFailure) return true;
                if (e.Message != null &&
                    e.Message.IndexOf("NameResolutionFailure", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        public static HttpWebRequest CreateRequest(string url, bool keepAlive)
        {
            Init();
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = UserAgent;
            req.Timeout = TimeoutMs;
            req.ReadWriteTimeout = TimeoutMs;
            req.Accept = "application/json, text/plain, */*";
            req.KeepAlive = keepAlive;
            req.AllowAutoRedirect = true;
            try
            {
                req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            }
            catch
            {
            }
            return req;
        }

        public static string GetString(string url)
        {
            return GetString(url, 3);
        }

        public static string GetString(string url, int retries)
        {
            string[] urls = Candidates(url);
            Exception last = null;
            for (int attempt = 0; attempt < retries; attempt++)
            {
                bool allPermanent = true;
                for (int u = 0; u < urls.Length; u++)
                {
                    try
                    {
                        HttpWebRequest req = CreateRequest(urls[u], true);
                        using (WebResponse resp = req.GetResponse())
                        {
                            using (Stream stream = resp.GetResponseStream())
                            {
                                if (stream == null) throw new Exception("服务器未返回内容");
                                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                                {
                                    return reader.ReadToEnd();
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        last = ex;
                        if (!IsPermanent(ex)) allPermanent = false;
                    }
                }
                if (allPermanent) break;
                Thread.Sleep(400 * (attempt + 1));
            }
            throw new Exception("网络请求失败：" + ShortMessage(last), last);
        }

        public static Json GetJson(string url)
        {
            return Json.Parse(GetString(url));
        }

        public static string ShortMessage(Exception ex)
        {
            if (ex == null) return "未知错误";
            WebException we = ex as WebException;
            if (we != null)
            {
                HttpWebResponse resp = we.Response as HttpWebResponse;
                if (resp != null) return "HTTP " + (int)resp.StatusCode + " " + resp.StatusDescription;
                // 保留英文关键词，IsDnsFailure 靠它兜底判断；同时给出人话，日志里才好读
                switch (we.Status)
                {
                    case WebExceptionStatus.NameResolutionFailure:
                        return "域名解析失败（NameResolutionFailure）";
                    case WebExceptionStatus.ConnectFailure:
                        return "服务器连不上（ConnectFailure）";
                    case WebExceptionStatus.Timeout:
                        return "连接超时（Timeout）";
                    case WebExceptionStatus.ConnectionClosed:
                        return "连接被中断（ConnectionClosed）";
                    case WebExceptionStatus.TrustFailure:
                        return "证书校验失败（TrustFailure）";
                    case WebExceptionStatus.SecureChannelFailure:
                        return "TLS 握手失败（SecureChannelFailure）";
                }
                return we.Status.ToString();
            }
            return ex.Message;
        }
    }

    public class DownloadItem
    {
        public string Url = "";
        public string Dest = "";
        public string Sha1 = "";
        public long Size = 0;
        public string Name = "";

        public DownloadItem()
        {
        }

        public DownloadItem(string url, string dest, string sha1, long size)
        {
            Url = url;
            Dest = dest;
            Sha1 = sha1 == null ? "" : sha1;
            Size = size;
            Name = Path.GetFileName(dest);
        }

        /// <summary>详情面板里显示的名字。资源文件的 dest 是哈希串，这里优先取 URL 末段。</summary>
        public string Desc()
        {
            if (Name != null && Name.Length > 0) return Name;
            try
            {
                int q = Url.IndexOf('?');
                string clean = q >= 0 ? Url.Substring(0, q) : Url;
                int slash = clean.LastIndexOf('/');
                if (slash >= 0 && slash + 1 < clean.Length) return clean.Substring(slash + 1);
            }
            catch
            {
            }
            return "未命名文件";
        }
    }

    public static class Downloader
    {
        /// <summary>多线程下载一组文件，已存在且校验通过的文件会自动跳过</summary>
        public static void Run(List<DownloadItem> items, int threads, ProgressHandler progress)
        {
            if (items == null || items.Count == 0)
            {
                if (progress != null) progress(0, 0, "无需下载");
                return;
            }

            // 同一个目标文件可能被登记多次（不同资源名指向同一个 hash）。
            // 多线程同时写同一个 dest 会让 File.Move 抛 IOException，整批下载被判失败。
            List<DownloadItem> unique = new List<DownloadItem>();
            Dictionary<string, bool> seen = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < items.Count; i++)
            {
                string key = items[i].Dest == null ? "" : items[i].Dest;
                if (key.Length > 0)
                {
                    if (seen.ContainsKey(key)) continue;
                    seen[key] = true;
                }
                unique.Add(items[i]);
            }
            items = unique;

            int total = items.Count;
            int done = 0;
            int cursor = -1;
            object gate = new object();
            List<string> failures = new List<string>();

            if (threads < 1) threads = 1;
            if (threads > 16) threads = 16;
            if (threads > total) threads = total;

            // 明细面板靠它拿到「总量 / 在下的文件 / 失败项」，状态栏只显示一个百分比
            ProgressFeed.Plan(total);

            if (progress != null) progress(0, total, "开始下载 " + total + " 个文件");

            Thread[] workers = new Thread[threads];
            for (int w = 0; w < threads; w++)
            {
                Thread t = new Thread(delegate()
                {
                    while (true)
                    {
                        DownloadItem item;
                        lock (gate)
                        {
                            cursor++;
                            if (cursor >= total) return;
                            item = items[cursor];
                        }
                        ProgressFeed.StartFile(item.Desc());
                        bool ok = false;
                        try
                        {
                            Fetch(item);
                            ok = true;
                        }
                        catch (Exception ex)
                        {
                            // 单个文件失败不再中断整批：其余文件继续下完，
                            // 下次重试时已完成的文件会被 SHA1 校验直接跳过。
                            lock (gate)
                            {
                                // ex.Message 本身已含文件名，不再重复拼 item.Name
                                if (failures.Count < 200) failures.Add(ex.Message);
                            }
                            ProgressFeed.Fail(item.Desc(), Net.ShortMessage(ex));
                        }
                        // 失败也算「处理过」，否则进度永远到不了 100%，看起来像卡死
                        ProgressFeed.EndFile(item.Desc(), ok);
                        if (!ok) continue;

                        int current;
                        lock (gate)
                        {
                            done++;
                            current = done;
                        }
                        if (progress != null) progress(current, total, item.Name);
                    }
                });
                t.IsBackground = true;
                workers[w] = t;
                t.Start();
            }
            for (int i = 0; i < workers.Length; i++) workers[i].Join();

            if (failures.Count > 0)
            {
                string sample = failures[0];
                string more = failures.Count > 1 ? "（共 " + failures.Count + " 项，首项如上）" : "";
                throw new Exception("有 " + failures.Count + " 个文件下载失败：" + sample + more +
                    "\r\n已完成的部分不会重复下载，重试只会补缺失的文件。", new Exception(sample));
            }
        }

        /// <summary>
        /// 下载单个文件。先试镜像，镜像 404/超时就换官方源再试；
        /// 两个源都永久失败（404）时立即放弃，不做无意义的退避重试。
        /// </summary>
        private static void Fetch(DownloadItem item)
        {
            if (File.Exists(item.Dest))
            {
                bool ok = true;
                if (item.Sha1.Length == 32)
                {
                    try
                    {
                        ok = Hashing.Sha1File(item.Dest) == item.Sha1.ToLowerInvariant();
                    }
                    catch
                    {
                        ok = false;
                    }
                }
                else if (item.Size > 0)
                {
                    try
                    {
                        ok = new FileInfo(item.Dest).Length == item.Size;
                    }
                    catch
                    {
                        ok = false;
                    }
                }
                if (ok) return;
                try
                {
                    File.Delete(item.Dest);
                }
                catch
                {
                }
            }

            string dir = Path.GetDirectoryName(item.Dest);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            string[] urls = Net.Candidates(item.Url);
            Exception last = null;

            for (int attempt = 0; attempt < 3; attempt++)
            {
                bool allPermanent = true;
                for (int u = 0; u < urls.Length; u++)
                {
                    try
                    {
                        SaveTo(urls[u], item.Dest);
                        return;
                    }
                    catch (Exception ex)
                    {
                        last = ex;
                        if (!Net.IsPermanent(ex)) allPermanent = false;
                    }
                }
                if (allPermanent) break;
                Thread.Sleep(300 * (attempt + 1));
            }

            string reason = Net.ShortMessage(last);
            if (urls.Length > 1) reason = reason + "，镜像与官方源均失败";
            throw new Exception(Path.GetFileName(item.Dest) + " 下载失败（" + reason + "）", last);
        }

        private static void SaveTo(string url, string dest)
        {
            string temp = dest + ".tmp";
            try
            {
                HttpWebRequest req = Net.CreateRequest(url, false);
                using (WebResponse resp = req.GetResponse())
                {
                    using (Stream input = resp.GetResponseStream())
                    {
                        if (input == null) throw new Exception("服务器未返回数据");
                        using (FileStream output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 16384))
                        {
                            byte[] buffer = new byte[16384];
                            while (true)
                            {
                                int read = input.Read(buffer, 0, buffer.Length);
                                if (read <= 0) break;
                                output.Write(buffer, 0, read);
                                ProgressFeed.AddBytes(read);   // 速度统计用
                            }
                        }
                    }
                }
                try
                {
                    if (File.Exists(dest)) File.Delete(dest);
                    File.Move(temp, dest);
                }
                catch (IOException)
                {
                    // 另一个线程刚好把同一个文件写好了，直接采用，不算失败
                    if (File.Exists(dest))
                    {
                        TryDelete(temp);
                        return;
                    }
                    throw;
                }
            }
            catch
            {
                TryDelete(temp);
                throw;
            }
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
    }
}
