using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Liuguang.Core
{
    /// <summary>
    /// 启动前环境自检。逐个核对启动真正需要的文件：客户端 jar、依赖库、本地库(native)、
    /// 资源索引与资源文件、log4j 配置。缺什么就让安装器补什么（已存在且校验通过的文件会自动跳过）。
    ///
    /// 这样做的意义：以前缺一个库就抛「缺少游戏核心文件，请重新安装」，用户只能猜；
    /// 现在直接把缺的补上，补不上才报出具体缺哪几项。
    /// </summary>
    public static class EnvironmentRepair
    {
        public static void Ensure(GameVersion v, Config cfg, ProgressHandler progress)
        {
            List<string> problems = new List<string>();
            CheckJar(v, problems);
            CheckLibraries(v, problems);
            CheckNatives(v, problems);
            CheckAssets(v, problems);
            CheckLogging(v);

            if (problems.Count == 0)
            {
                Log.Write("环境自检通过：" + v.Id + "（依赖库 " + v.Libraries.Count + " 项）");
                return;
            }

            string summary = Join(problems);
            Log.Write("环境自检发现缺失：" + summary + "，开始自动补全");
            if (progress != null) progress(0, 0, "补全缺失文件：" + summary);

            VersionInstaller.Install(v.Id, cfg, progress);

            // 补完只复核关键项：资源文件即使还差几个也能进游戏，jar 和库缺了必崩
            List<string> after = new List<string>();
            CheckJar(v, after);
            CheckLibraries(v, after);
            CheckNatives(v, after);
            if (after.Count > 0)
            {
                throw new Exception("游戏文件补全失败，仍缺少：" + Join(after) +
                    "。\r\n请检查网络后重试，或在「下载」页重新安装该版本。");
            }
            Log.Write("环境补全完成：" + v.Id);
        }

        /// <summary>客户端主程序 jar（Fabric 等继承版本则检查父版本的 jar）</summary>
        private static void CheckJar(GameVersion v, List<string> problems)
        {
            if (v.JarVersion.Length == 0) return;
            string jar = Path.Combine(Paths.VersionDir(v.JarVersion), v.JarVersion + ".jar");
            // 用官方声明的字节数判断，不能拍一个「小于 xx KB 就算坏」的阈值：
            // 远古版本（rd-132211 之类）的客户端 jar 只有十几 KB，用阈值会误判成缺失而反复重下。
            long expected = v.ClientDownload["size"].AsLong(0);
            try
            {
                if (!File.Exists(jar))
                {
                    problems.Add("客户端主程序 " + v.JarVersion + ".jar");
                    return;
                }
                long actual = new FileInfo(jar).Length;
                if (expected > 0 ? actual != expected : actual == 0)
                {
                    problems.Add("客户端主程序 " + v.JarVersion + ".jar（大小不符）");
                }
            }
            catch
            {
                problems.Add("客户端主程序 " + v.JarVersion + ".jar");
            }
        }

        private static void CheckLibraries(GameVersion v, List<string> problems)
        {
            int missing = 0;
            string sample = "";
            for (int i = 0; i < v.Libraries.Count; i++)
            {
                Json lib = v.Libraries[i];
                if (!VersionMeta.IsAllowed(lib)) continue;
                string name = lib["name"].AsString("");
                if (name.Length == 0) continue;

                // 只有声明了 artifact 或给了仓库地址的库才会有对应文件。
                // 像 1.8.9 的 jinput-platform 只有 classifiers，不能算作缺失，否则会去下 404 地址。
                Json artifact = lib["downloads"]["artifact"];
                string baseUrl = lib["url"].AsString("");
                if (!artifact.IsObject && baseUrl.Length == 0) continue;

                string rel = VersionMeta.LibraryPath(name);
                if (rel.Length == 0) continue;
                string path = Path.Combine(Paths.LibrariesDir, rel.Replace('/', Path.DirectorySeparatorChar));
                try
                {
                    if (File.Exists(path)) continue;
                }
                catch
                {
                }
                missing++;
                if (sample.Length == 0) sample = Path.GetFileName(path);
            }
            if (missing > 0) problems.Add(missing + " 个依赖库（如 " + sample + "）");
        }

        /// <summary>老版本（1.12 及以前）必须把 natives 解压到版本目录，否则 LWJGL 直接崩</summary>
        private static void CheckNatives(GameVersion v, List<string> problems)
        {
            bool needsNatives = false;
            for (int i = 0; i < v.Libraries.Count; i++)
            {
                Json lib = v.Libraries[i];
                if (!VersionMeta.IsAllowed(lib)) continue;
                if (lib["natives"].IsObject) needsNatives = true;
            }
            if (!needsNatives) return;

            string dir = Path.Combine(v.Dir, "natives");
            try
            {
                if (Directory.Exists(dir) && Directory.GetFiles(dir, "*.dll").Length > 0) return;
            }
            catch
            {
            }
            problems.Add("本地库文件（natives）");
        }

        private static void CheckAssets(GameVersion v, List<string> problems)
        {
            string indexFile = Path.Combine(Paths.AssetsIndexDir, v.AssetsId + ".json");
            if (!File.Exists(indexFile))
            {
                if (v.AssetIndexUrl.Length > 0) problems.Add("资源索引 " + v.AssetsId + ".json");
                return;
            }

            Json index;
            try
            {
                index = Json.Parse(File.ReadAllText(indexFile, Encoding.UTF8));
            }
            catch
            {
                problems.Add("资源索引（文件已损坏）");
                return;
            }

            Json objects = index["objects"];
            if (!objects.IsObject) return;

            int total = 0;
            int missing = 0;
            foreach (KeyValuePair<string, Json> kv in objects.Props)
            {
                total++;
                if (total > 30000) break;   // 防御：异常索引不阻塞启动
                string hash = kv.Value["hash"].AsString("");
                if (hash.Length < 4) continue;
                string file = Path.Combine(Paths.AssetsObjectDir, hash.Substring(0, 2), hash);
                bool exists = false;
                try
                {
                    exists = File.Exists(file);
                }
                catch
                {
                }
                if (!exists) missing++;
            }
            if (missing > 0) problems.Add(missing + " 个游戏资源文件（共 " + total + "）");
        }

        /// <summary>
        /// log4j 配置缺失不影响启动（Launcher 只在文件存在时才传 -Dlog4j.configurationFile），
        /// 所以这里只记日志、不算作「需要补全」，避免因为一个格式文件把整版重装一遍。
        /// </summary>
        private static void CheckLogging(GameVersion v)
        {
            if (!v.Logging.IsObject) return;
            string id = v.Logging["file"]["id"].AsString("");
            if (id.Length == 0) return;
            string path = Path.Combine(Paths.AssetsDir, "log_configs", id);
            if (!File.Exists(path))
            {
                Log.Write("缺少日志配置 " + id + "（不影响启动，安装版本时会顺带补上）");
            }
        }

        private static string Join(List<string> items)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) sb.Append("、");
                sb.Append(items[i]);
            }
            return sb.ToString();
        }
    }
}
