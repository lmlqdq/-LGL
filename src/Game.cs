using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace Liuguang.Core
{
    public class VersionEntry
    {
        public string Id = "";
        public string Type = "release";
        public string Url = "";
        public string ReleaseTime = "";
        public string TypeText
        {
            get
            {
                if (Type == "release") return "正式版";
                if (Type == "snapshot") return "快照";
                if (Type == "old_beta") return "远古 Beta";
                if (Type == "old_alpha") return "远古 Alpha";
                return Type;
            }
        }

        public override string ToString()
        {
            return Id;
        }
    }

    public class InstalledVersion
    {
        public string Id = "";
        public string McVersion = "";
        public string Loader = "原版";
        public string Type = "release";
        public int JavaMajor = 8;
        public bool HasJar = false;
        public int ModCount = 0;

        public override string ToString()
        {
            return Id;
        }
    }

    public static class VersionList
    {
        public const string ManifestUrl = "https://launchermeta.mojang.com/mc/game/version_manifest_v2.json";

        public static List<VersionEntry> All = new List<VersionEntry>();
        public static string LatestRelease = "";
        public static string LatestSnapshot = "";
        public static DateTime LastRefresh = DateTime.MinValue;

        public static void Refresh()
        {
            Json root = Net.GetJson(ManifestUrl);
            List<VersionEntry> list = new List<VersionEntry>();
            foreach (Json item in root["versions"].AsArray())
            {
                VersionEntry e = new VersionEntry();
                e.Id = item["id"].AsString("");
                e.Type = item["type"].AsString("release");
                e.Url = item["url"].AsString("");
                e.ReleaseTime = item["releaseTime"].AsString("");
                if (e.Id.Length > 0) list.Add(e);
            }
            All = list;
            LatestRelease = root["latest"]["release"].AsString("");
            LatestSnapshot = root["latest"]["snapshot"].AsString("");
            LastRefresh = DateTime.Now;
            Log.Write("已获取版本清单，共 " + list.Count + " 个版本，最新正式版 " + LatestRelease);
        }

        public static VersionEntry Find(string id)
        {
            for (int i = 0; i < All.Count; i++)
            {
                if (All[i].Id == id) return All[i];
            }
            return null;
        }

        public static List<VersionEntry> Filter(string keyword, string type)
        {
            List<VersionEntry> result = new List<VersionEntry>();
            string key = (keyword == null ? "" : keyword.Trim().ToLowerInvariant());
            for (int i = 0; i < All.Count; i++)
            {
                VersionEntry e = All[i];
                if (type.Length > 0 && e.Type != type) continue;
                if (key.Length > 0 && e.Id.ToLowerInvariant().IndexOf(key) < 0) continue;
                result.Add(e);
            }
            return result;
        }

        public static List<InstalledVersion> Installed(Config cfg)
        {
            List<InstalledVersion> list = new List<InstalledVersion>();
            if (!Directory.Exists(Paths.VersionsDir)) return list;
            string[] dirs = Directory.GetDirectories(Paths.VersionsDir);
            for (int i = 0; i < dirs.Length; i++)
            {
                string id = Path.GetFileName(dirs[i]);
                string jsonPath = Path.Combine(dirs[i], id + ".json");
                if (!File.Exists(jsonPath)) continue;
                InstalledVersion iv = new InstalledVersion();
                iv.Id = id;
                iv.HasJar = File.Exists(Path.Combine(dirs[i], id + ".jar"));
                try
                {
                    Json root = Json.Parse(File.ReadAllText(jsonPath, Encoding.UTF8));
                    iv.Type = root["type"].AsString("release");
                    string inherit = root["inheritsFrom"].AsString("");
                    if (inherit.Length > 0) iv.McVersion = inherit;
                    else iv.McVersion = id;
                    if (id.ToLowerInvariant().StartsWith("fabric-loader")) iv.Loader = "Fabric";
                    else if (id.ToLowerInvariant().StartsWith("quilt-loader")) iv.Loader = "Quilt";
                    else if (id.ToLowerInvariant().IndexOf("neoforge") >= 0) iv.Loader = "NeoForge";
                    else if (id.ToLowerInvariant().IndexOf("forge") >= 0) iv.Loader = "Forge";
                }
                catch
                {
                    iv.McVersion = id;
                }
                string modsDir = Paths.ModsDir(id, cfg.VersionIsolation);
                if (Directory.Exists(modsDir))
                {
                    iv.ModCount = Directory.GetFiles(modsDir, "*.jar").Length + Directory.GetFiles(modsDir, "*.jar.disabled").Length;
                }
                list.Add(iv);
            }
            list.Sort(delegate(InstalledVersion a, InstalledVersion b)
            {
                return string.Compare(b.McVersion, a.McVersion, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }
    }

    public class GameVersion
    {
        public string Id = "";
        public string InheritsFrom = "";
        public string MainClass = "net.minecraft.client.main.Main";
        public string AssetsId = "legacy";
        public string AssetIndexUrl = "";
        public string AssetIndexSha1 = "";
        public Json ClientDownload = Json.Null;
        public string JarVersion = "";
        public List<Json> Libraries = new List<Json>();
        public List<Json> JvmArgs = new List<Json>();
        public List<Json> GameArgs = new List<Json>();
        public string LegacyArgs = "";
        public int JavaMajor = 8;
        public Json Logging = Json.Null;
        public string Dir = "";
        public string Type = "release";
        public string ReleaseTime = "";
        public bool IsFabric = false;
    }

    public static class VersionMeta
    {
        public static GameVersion Load(string id)
        {
            return Load(id, 0);
        }

        private static GameVersion Load(string id, int depth)
        {
            if (depth > 6) throw new Exception("版本继承层级过深：" + id);
            string jsonPath = Paths.VersionJsonPath(id);
            if (!File.Exists(jsonPath)) throw new Exception("版本 " + id + " 尚未安装，请先在下载页安装");

            Json root;
            try
            {
                root = Json.Parse(File.ReadAllText(jsonPath, Encoding.UTF8));
            }
            catch (Exception ex)
            {
                throw new Exception("版本 " + id + " 的 JSON 已损坏：" + ex.Message);
            }

            GameVersion v = new GameVersion();
            v.Id = id;
            v.Dir = Paths.VersionDir(id);
            v.InheritsFrom = root["inheritsFrom"].AsString("");
            v.Type = root["type"].AsString("release");
            v.ReleaseTime = root["releaseTime"].AsString("");

            if (v.InheritsFrom.Length > 0)
            {
                GameVersion parent = Load(v.InheritsFrom, depth + 1);
                v.MainClass = parent.MainClass;
                v.AssetsId = parent.AssetsId;
                v.AssetIndexUrl = parent.AssetIndexUrl;
                v.AssetIndexSha1 = parent.AssetIndexSha1;
                v.ClientDownload = parent.ClientDownload;
                v.JarVersion = parent.JarVersion;
                v.JavaMajor = parent.JavaMajor;
                v.Logging = parent.Logging;
                v.IsFabric = parent.IsFabric;
                v.Libraries.AddRange(parent.Libraries);
                v.JvmArgs.AddRange(parent.JvmArgs);
                v.GameArgs.AddRange(parent.GameArgs);
                v.LegacyArgs = parent.LegacyArgs;
            }

            string mainClass = root["mainClass"].AsString("");
            if (mainClass.Length > 0) v.MainClass = mainClass;

            Json assetIndex = root["assetIndex"];
            if (assetIndex.IsObject)
            {
                v.AssetsId = assetIndex["id"].AsString(v.AssetsId);
                v.AssetIndexUrl = assetIndex["url"].AsString(v.AssetIndexUrl);
                v.AssetIndexSha1 = assetIndex["sha1"].AsString(v.AssetIndexSha1);
            }
            else
            {
                string assets = root["assets"].AsString("");
                if (assets.Length > 0) v.AssetsId = assets;
            }

            Json client = root["downloads"]["client"];
            if (client.IsObject)
            {
                v.ClientDownload = client;
                v.JarVersion = id;
            }

            Json majorVersion = root["javaVersion"]["majorVersion"];
            if (!majorVersion.IsNull) v.JavaMajor = majorVersion.AsInt(v.JavaMajor);

            Json logging = root["logging"]["client"];
            if (logging.IsObject) v.Logging = logging;

            List<Json> ownLibraries = root["libraries"].AsArray();
            if (ownLibraries.Count > 0) MergeLibraries(v.Libraries, ownLibraries);

            List<Json> jvm = root["arguments"]["jvm"].AsArray();
            if (jvm.Count > 0) v.JvmArgs.AddRange(jvm);
            List<Json> game = root["arguments"]["game"].AsArray();
            if (game.Count > 0) v.GameArgs.AddRange(game);

            string legacy = root["minecraftArguments"].AsString("");
            if (legacy.Length > 0) v.LegacyArgs = legacy;

            if (id.ToLowerInvariant().IndexOf("fabric") >= 0) v.IsFabric = true;
            return v;
        }

        private static void MergeLibraries(List<Json> target, List<Json> own)
        {
            for (int i = 0; i < own.Count; i++)
            {
                Json lib = own[i];
                string key = Key(lib["name"].AsString(""));
                int found = -1;
                for (int k = 0; k < target.Count; k++)
                {
                    if (Key(target[k]["name"].AsString("")) == key)
                    {
                        found = k;
                        break;
                    }
                }
                if (found >= 0) target[found] = lib;
                else target.Add(lib);
            }
        }

        /// <summary>
        /// 库的去重键：group:artifact[:classifier]，**不含版本号**。
        ///
        /// 不含版本号是对的——Fabric 这类子版本要能覆盖父版本里的同名库。
        /// 但必须含 classifier：1.19 之后原版把 native 拆成了独立的库条目
        /// （com.mojang:jtracy:1.14.38 和 com.mojang:jtracy:1.14.38:natives-windows 是两条），
        /// 以前这里只取 group:artifact，结果每一条 native 都把主 jar 顶掉了，
        /// 主 jar 既不下载也不进 classpath —— 启动就是
        /// NoClassDefFoundError: com/mojang/jtracy/TracyClient。
        /// </summary>
        public static string Key(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            string clean = name;
            int at = clean.IndexOf('@');
            if (at >= 0) clean = clean.Substring(0, at);
            string[] parts = clean.Split(':');
            if (parts.Length < 2) return clean;
            string key = parts[0] + ":" + parts[1];
            if (parts.Length > 3 && parts[3].Length > 0) key = key + ":" + parts[3];
            return key;
        }

        /// <summary>取 maven 坐标里的 classifier，没有则返回空串</summary>
        public static string Classifier(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            string clean = name;
            int at = clean.IndexOf('@');
            if (at >= 0) clean = clean.Substring(0, at);
            string[] parts = clean.Split(':');
            return parts.Length > 3 ? parts[3] : "";
        }

        /// <summary>
        /// 规则判定：只模拟 Windows 桌面环境，所有 features 均为关闭。
        ///
        /// 另外补一条原版 JSON 里查不到的约束：arm64 的 native 包在新版清单里
        /// 和 x64 的规则**完全一样**（都只写 os.name=windows），光看 rules 分不出来，
        /// 于是 x64 机器会把两种包一起下、一起塞进 classpath。这里按包名里的
        /// arm64 后缀再筛一道。
        /// </summary>
        public static bool IsAllowed(Json item)
        {
            string classifier = Classifier(item["name"].AsString(""));
            if (classifier.Length > 0 && classifier.IndexOf("arm64") >= 0 && !Util.IsArm64) return false;

            Json rules = item["rules"];
            if (!rules.IsArray) return true;
            bool allowed = false;
            List<Json> list = rules.AsArray();
            for (int i = 0; i < list.Count; i++)
            {
                Json rule = list[i];
                if (!RuleMatches(rule)) continue;
                allowed = rule["action"].AsString("allow") == "allow";
            }
            return allowed;
        }

        private static bool RuleMatches(Json rule)
        {
            Json os = rule["os"];
            if (os.IsObject)
            {
                string name = os["name"].AsString("");
                if (name.Length > 0 && name != "windows") return false;
                string arch = os["arch"].AsString("");
                if (arch.Length > 0)
                {
                    string mine = Util.Is64Bit ? "x86_64" : "x86";
                    if (arch != mine) return false;
                }
                string version = os["version"].AsString("");
                if (version.Length > 0)
                {
                    try
                    {
                        if (!Regex.IsMatch(Environment.OSVersion.Version.ToString(), version)) return false;
                    }
                    catch
                    {
                    }
                }
            }
            Json features = rule["features"];
            if (features.IsObject)
            {
                foreach (KeyValuePair<string, Json> kv in features.Props)
                {
                    if (kv.Value.AsBool(false) != false) return false;
                }
            }
            return true;
        }

        /// <summary>group:artifact:version[:classifier] -> 相对路径</summary>
        public static string LibraryPath(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            string clean = name;
            int at = clean.IndexOf('@');
            if (at >= 0) clean = clean.Substring(0, at);
            string[] parts = clean.Split(':');
            if (parts.Length < 3) return "";
            string group = parts[0];
            string artifact = parts[1];
            string version = parts[2];
            string classifier = parts.Length > 3 ? parts[3] : "";
            string file = artifact + "-" + version + (classifier.Length > 0 ? "-" + classifier : "") + ".jar";
            return group.Replace('.', '/') + "/" + artifact + "/" + version + "/" + file;
        }
    }

    public static class VersionInstaller
    {
        public static void EnsureJson(string id)
        {
            EnsureJson(id, 0);
        }

        private static void EnsureJson(string id, int depth)
        {
            if (depth > 6) throw new Exception("版本继承层级过深：" + id);
            string path = Paths.VersionJsonPath(id);
            if (File.Exists(path))
            {
                try
                {
                    Json root = Json.Parse(File.ReadAllText(path, Encoding.UTF8));
                    string parent = root["inheritsFrom"].AsString("");
                    if (parent.Length > 0) EnsureJson(parent, depth + 1);
                }
                catch
                {
                }
                return;
            }

            if (VersionList.All.Count == 0) VersionList.Refresh();
            VersionEntry entry = VersionList.Find(id);
            if (entry == null) throw new Exception("官方版本清单中不存在 " + id);

            Util.EnsureDir(Paths.VersionDir(id));
            Log.Write("下载版本描述文件：" + id);
            string json = Net.GetString(entry.Url);
            Util.WriteText(path, json);

            try
            {
                Json root = Json.Parse(json);
                string parent = root["inheritsFrom"].AsString("");
                if (parent.Length > 0) EnsureJson(parent, depth + 1);
            }
            catch
            {
            }
        }

        /// <summary>完整安装一个版本（原版 / Fabric 等继承版本），返回可用元数据</summary>
        public static GameVersion Install(string id, Config cfg, ProgressHandler progress)
        {
            Paths.EnsureBaseDirs();
            if (progress != null) progress(0, 1, "解析版本元数据");
            EnsureJson(id);

            GameVersion v = VersionMeta.Load(id);
            List<DownloadItem> tasks = new List<DownloadItem>();
            List<string> nativeJars = new List<string>();
            List<string> nativeExcludes = new List<string>();

            // 1. 客户端 jar
            if (v.ClientDownload.IsObject && v.JarVersion.Length > 0)
            {
                string jarPath = Path.Combine(Paths.VersionDir(v.JarVersion), v.JarVersion + ".jar");
                string url = v.ClientDownload["url"].AsString("");
                if (url.Length > 0)
                {
                    tasks.Add(new DownloadItem(url, jarPath, v.ClientDownload["sha1"].AsString(""), v.ClientDownload["size"].AsLong(0)));
                }
            }

            // 2. 依赖库与 native
            for (int i = 0; i < v.Libraries.Count; i++)
            {
                Json lib = v.Libraries[i];
                if (!VersionMeta.IsAllowed(lib)) continue;
                string name = lib["name"].AsString("");
                if (name.Length == 0) continue;

                string rel = VersionMeta.LibraryPath(name);
                Json natives = lib["natives"];
                if (natives.IsObject)
                {
                    string key = natives["windows"].AsString("");
                    if (key.Length > 0)
                    {
                        key = key.Replace("${arch}", Util.Is64Bit ? "64" : "32");
                        Json classifier = lib["downloads"]["classifiers"][key];
                        string classifierRel = VersionMeta.LibraryPath(name + ":" + key);
                        if (classifier.IsObject && classifierRel.Length > 0)
                        {
                            string dest = Path.Combine(Paths.LibrariesDir, classifierRel.Replace('/', Path.DirectorySeparatorChar));
                            string url = classifier["url"].AsString("");
                            if (url.Length > 0)
                            {
                                tasks.Add(new DownloadItem(url, dest, classifier["sha1"].AsString(""), classifier["size"].AsLong(0)));
                                nativeJars.Add(dest);
                            }
                        }
                        Json excludes = lib["extract"]["exclude"];
                        if (excludes.IsArray)
                        {
                            foreach (Json ex in excludes.AsArray()) nativeExcludes.Add(ex.AsString(""));
                        }
                    }
                }

                if (rel.Length == 0) continue;
                string target = Path.Combine(Paths.LibrariesDir, rel.Replace('/', Path.DirectorySeparatorChar));
                Json artifact = lib["downloads"]["artifact"];
                if (artifact.IsObject)
                {
                    string url = artifact["url"].AsString("");
                    if (url.Length > 0)
                    {
                        tasks.Add(new DownloadItem(url, target, artifact["sha1"].AsString(""), artifact["size"].AsLong(0)));
                    }
                }
                else
                {
                    string baseUrl = lib["url"].AsString("");
                    if (baseUrl.Length > 0)
                    {
                        string full = baseUrl.TrimEnd('/') + "/" + rel;
                        tasks.Add(new DownloadItem(full, target, "", 0));
                    }
                }
            }

            // 3. log4j 配置：这只是日志格式文件，缺了游戏照样能进。
            //    单独下载、失败不阻断安装——以前它算在必装批次里，
            //    镜像站返回 404 就把整个版本的安装判成失败。
            DownloadItem logTask = null;
            if (v.Logging.IsObject)
            {
                Json file = v.Logging["file"];
                string cfgId = file["id"].AsString("");
                string cfgUrl = file["url"].AsString("");
                if (cfgId.Length > 0 && cfgUrl.Length > 0)
                {
                    string logConfigPath = Path.Combine(Paths.AssetsDir, "log_configs", cfgId);
                    logTask = new DownloadItem(cfgUrl, logConfigPath, file["sha1"].AsString(""), file["size"].AsLong(0));
                }
            }

            // 4. 资源索引
            string indexFile = Path.Combine(Paths.AssetsIndexDir, v.AssetsId + ".json");
            if (v.AssetIndexUrl.Length > 0 && !File.Exists(indexFile))
            {
                tasks.Add(new DownloadItem(v.AssetIndexUrl, indexFile, v.AssetIndexSha1, 0));
            }

            if (progress != null) progress(0, tasks.Count, "共 " + tasks.Count + " 个文件待检查");
            Downloader.Run(tasks, cfg.DownloadThreads, progress);

            if (logTask != null)
            {
                try
                {
                    List<DownloadItem> extra = new List<DownloadItem>();
                    extra.Add(logTask);
                    Downloader.Run(extra, 1, null);
                }
                catch (Exception ex)
                {
                    Log.Error("日志配置下载失败（不影响启动）：" + ex.Message);
                }
            }

            // 5. 解压 native
            if (nativeJars.Count > 0)
            {
                if (progress != null) progress(0, 1, "解压本地库");
                ExtractNatives(nativeJars, Path.Combine(v.Dir, "natives"), nativeExcludes);
            }

            // 6. 资源文件
            InstallAssets(v, cfg, indexFile, progress);

            // 7. 写入版本标记，便于 UI 显示
            try
            {
                string markerPath = Path.Combine(v.Dir, ".installed");
                Util.WriteText(markerPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            }
            catch
            {
            }

            if (progress != null) progress(1, 1, "安装完成");
            Log.Write("版本安装完成：" + id);
            return v;
        }

        private static void InstallAssets(GameVersion v, Config cfg, string indexFile, ProgressHandler progress)
        {
            if (!File.Exists(indexFile)) return;
            Json index;
            try
            {
                index = Json.Parse(File.ReadAllText(indexFile, Encoding.UTF8));
            }
            catch (Exception ex)
            {
                Log.Error("资源索引解析失败：" + ex.Message);
                return;
            }

            Json objects = index["objects"];
            if (!objects.IsObject) return;

            List<DownloadItem> assetTasks = new List<DownloadItem>();
            foreach (KeyValuePair<string, Json> kv in objects.Props)
            {
                Json obj = kv.Value;
                string hash = obj["hash"].AsString("");
                if (hash.Length < 4) continue;
                string sub = hash.Substring(0, 2);
                string dest = Path.Combine(Paths.AssetsObjectDir, sub, hash);
                if (File.Exists(dest) && new FileInfo(dest).Length > 0) continue;
                string url = "https://resources.download.minecraft.net/" + sub + "/" + hash;
                assetTasks.Add(new DownloadItem(url, dest, hash, obj["size"].AsLong(0)));
            }

            if (assetTasks.Count > 0)
            {
                if (progress != null) progress(0, assetTasks.Count, "检查游戏资源文件");
                Downloader.Run(assetTasks, cfg.DownloadThreads, progress);
            }

            bool isVirtual = index["virtual"].AsBool(false);
            bool mapToResources = index["map_to_resources"].AsBool(false);
            if (isVirtual || mapToResources)
            {
                string virtualDir = mapToResources
                    ? Path.Combine(Paths.McDir, "resources")
                    : Path.Combine(Paths.AssetsDir, "virtual", v.AssetsId);
                foreach (KeyValuePair<string, Json> kv in objects.Props)
                {
                    try
                    {
                        Json obj = kv.Value;
                        string hash = obj["hash"].AsString("");
                        if (hash.Length < 4) continue;
                        string source = Path.Combine(Paths.AssetsObjectDir, hash.Substring(0, 2), hash);
                        string dest = Path.Combine(virtualDir, kv.Key.Replace('/', Path.DirectorySeparatorChar));
                        if (File.Exists(dest) || !File.Exists(source)) continue;
                        Util.EnsureDir(Path.GetDirectoryName(dest));
                        File.Copy(source, dest, true);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static void ExtractNatives(List<string> jars, string nativesDir, List<string> excludes)
        {
            Util.EnsureDir(nativesDir);
            for (int i = 0; i < jars.Count; i++)
            {
                string jar = jars[i];
                if (!File.Exists(jar)) continue;
                try
                {
                    using (ZipArchive archive = ZipFile.OpenRead(jar))
                    {
                        foreach (ZipArchiveEntry entry in archive.Entries)
                        {
                            string full = entry.FullName;
                            if (full.EndsWith("/")) continue;
                            if (full.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase)) continue;
                            bool skip = false;
                            for (int k = 0; k < excludes.Count; k++)
                            {
                                string pattern = excludes[k];
                                if (pattern.Length > 0 && full.Replace('\\', '/').StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                                {
                                    skip = true;
                                    break;
                                }
                            }
                            if (skip) continue;
                            string target = Path.Combine(nativesDir, full.Replace('/', Path.DirectorySeparatorChar));
                            Util.EnsureDir(Path.GetDirectoryName(target));
                            entry.ExtractToFile(target, true);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("解压本地库失败：" + Path.GetFileName(jar) + " - " + ex.Message);
                }
            }
        }
    }

    public static class FabricInstaller
    {
        /// <summary>安装 Fabric 加载器，返回新版本 id</summary>
        public static string Install(string mcVersion, Config cfg, ProgressHandler progress)
        {
            if (progress != null) progress(0, 2, "安装原版 " + mcVersion);
            VersionInstaller.Install(mcVersion, cfg, progress);

            if (progress != null) progress(1, 2, "获取 Fabric 加载器列表");
            string listUrl = "https://meta.fabricmc.net/v2/versions/loader/" + mcVersion;
            Json list = Net.GetJson(listUrl);
            string loaderVersion = "";
            if (list.IsArray)
            {
                foreach (Json item in list.AsArray())
                {
                    if (item["loader"]["stable"].AsBool(false))
                    {
                        loaderVersion = item["loader"]["version"].AsString("");
                        break;
                    }
                }
                if (loaderVersion.Length == 0 && list.Count > 0)
                {
                    loaderVersion = list[0]["loader"]["version"].AsString("");
                }
            }
            if (loaderVersion.Length == 0) throw new Exception("该版本暂无可用的 Fabric 加载器：" + mcVersion);

            if (progress != null) progress(1, 2, "下载 Fabric 描述文件");
            string profileUrl = "https://meta.fabricmc.net/v2/versions/loader/" + mcVersion + "/" + loaderVersion + "/profile/json";
            Json profile = Net.GetJson(profileUrl);
            string id = profile["id"].AsString("");
            if (id.Length == 0) id = "fabric-loader-" + loaderVersion + "-" + mcVersion;

            Util.EnsureDir(Paths.VersionDir(id));
            Util.WriteText(Paths.VersionJsonPath(id), profile.ToJson());
            Log.Write("已写入 Fabric 版本描述：" + id);

            VersionInstaller.Install(id, cfg, progress);
            return id;
        }
    }
}
