using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Liuguang.Core
{
    public class ModProject
    {
        public string Id = "";
        public string Slug = "";
        public string Title = "";
        public string Description = "";
        public string IconUrl = "";
        public string Author = "";
        public string ProjectType = "mod";
        public long Downloads = 0;
        public List<string> Categories = new List<string>();
        public List<string> GameVersions = new List<string>();

        public override string ToString()
        {
            return Title;
        }
    }

    public class ModDependency
    {
        public string ProjectId = "";
        public string VersionId = "";
        public string DependencyType = "required";
    }

    public class ModFileInfo
    {
        public string Url = "";
        public string FileName = "";
        public long Size = 0;
        public bool Primary = false;
    }

    public class ModVersion
    {
        public string Id = "";
        public string ProjectId = "";
        public string Name = "";
        public string VersionNumber = "";
        public string VersionType = "release";
        public List<string> GameVersions = new List<string>();
        public List<string> Loaders = new List<string>();
        public List<ModFileInfo> Files = new List<ModFileInfo>();
        public List<ModDependency> Dependencies = new List<ModDependency>();

        public ModFileInfo PrimaryFile()
        {
            for (int i = 0; i < Files.Count; i++)
            {
                if (Files[i].Primary) return Files[i];
            }
            if (Files.Count > 0) return Files[0];
            return null;
        }
    }

    public static class Modrinth
    {
        public const string ApiBase = "https://api.modrinth.com/v2";

        public static List<ModProject> Search(string query, string gameVersion, string loader, string projectType, int offset, int limit, out int total)
        {
            total = 0;

            // 搜索时**只按「版本 + 类型」过滤，不按加载器分类过滤**。
            // 原因（实测踩坑）：Modrinth 的 categories 里 forge/neoforge/fabric 是并列分类，
            // 一个同时支持多种加载器的 mod 可能只挂在其中一两个分类下；更重要的是，
            // 新版本（如 1.21.x / 26.x）里 Forge 还没跟上，`versions:26.3` + `categories:forge`
            // 两个条件一交集就是 0 结果，用户搜任何词都「搜不到模组」。
            // 加载器的精确匹配留到「安装」那一步（GetVersions 里按 loaders 过滤），
            // 搜索阶段只负责「把人带到这里」，宁可多显示、不要漏显示。
            List<string> facets = new List<string>();
            facets.Add("[\"project_type:" + (string.IsNullOrEmpty(projectType) ? "mod" : projectType) + "\"]");
            bool hasVersion = !string.IsNullOrEmpty(gameVersion);
            if (hasVersion) facets.Add("[\"versions:" + gameVersion + "\"]");

            List<ModProject> result = DoSearch(query, facets, offset, limit, out total);

            // 兜底：按版本过滤搜不到任何东西（新版本 mod 还没发布，或版本号写法不匹配），
            // 去掉版本条件再搜一次，至少让用户看到「热门 mod」，而不是空列表 + 一脸懵。
            // 结果里每项都带着自己的 GameVersions，用户能自己判断适不适用。
            if (hasVersion && total == 0 && result.Count == 0)
            {
                List<string> broad = new List<string>();
                broad.Add(facets[0]);
                result = DoSearch(query, broad, offset, limit, out total);
            }
            return result;
        }

        private static List<ModProject> DoSearch(string query, List<string> facets, int offset, int limit, out int total)
        {
            total = 0;
            string facetText = "[" + string.Join(",", facets.ToArray()) + "]";

            StringBuilder url = new StringBuilder();
            url.Append(ApiBase).Append("/search?query=");
            url.Append(Uri.EscapeDataString(query == null ? "" : query));
            url.Append("&limit=").Append(limit);
            url.Append("&offset=").Append(offset);
            url.Append("&index=relevance");
            url.Append("&facets=").Append(Uri.EscapeDataString(facetText));

            Json root = Net.GetJson(url.ToString());
            total = root["total_hits"].AsInt(0);
            List<ModProject> result = new List<ModProject>();
            foreach (Json hit in root["hits"].AsArray())
            {
                ModProject p = new ModProject();
                p.Id = hit["project_id"].AsString("");
                p.Slug = hit["slug"].AsString("");
                p.Title = hit["title"].AsString("");
                p.Description = hit["description"].AsString("");
                p.IconUrl = hit["icon_url"].AsString("");
                p.Author = hit["author"].AsString("");
                p.ProjectType = hit["project_type"].AsString("mod");
                p.Downloads = hit["downloads"].AsLong(0);
                foreach (Json c in hit["categories"].AsArray()) p.Categories.Add(c.AsString(""));
                foreach (Json c in hit["versions"].AsArray()) p.GameVersions.Add(c.AsString(""));
                result.Add(p);
            }
            return result;
        }

        public static List<ModVersion> GetVersions(string projectId, string gameVersion, string loader)
        {
            StringBuilder url = new StringBuilder();
            url.Append(ApiBase).Append("/project/").Append(Uri.EscapeDataString(projectId)).Append("/version");
            bool hasParam = false;
            if (!string.IsNullOrEmpty(gameVersion))
            {
                url.Append("?game_versions=").Append(Uri.EscapeDataString("[\"" + gameVersion + "\"]"));
                hasParam = true;
            }
            if (!string.IsNullOrEmpty(loader))
            {
                url.Append(hasParam ? "&" : "?").Append("loaders=").Append(Uri.EscapeDataString("[\"" + loader + "\"]"));
            }

            Json list = Net.GetJson(url.ToString());
            List<ModVersion> result = new List<ModVersion>();
            foreach (Json item in list.AsArray())
            {
                ModVersion v = new ModVersion();
                v.Id = item["id"].AsString("");
                v.ProjectId = item["project_id"].AsString("");
                v.Name = item["name"].AsString("");
                v.VersionNumber = item["version_number"].AsString("");
                v.VersionType = item["version_type"].AsString("release");
                foreach (Json c in item["game_versions"].AsArray()) v.GameVersions.Add(c.AsString(""));
                foreach (Json c in item["loaders"].AsArray()) v.Loaders.Add(c.AsString(""));
                foreach (Json f in item["files"].AsArray())
                {
                    ModFileInfo info = new ModFileInfo();
                    info.Url = f["url"].AsString("");
                    info.FileName = f["filename"].AsString("");
                    info.Size = f["size"].AsLong(0);
                    info.Primary = f["primary"].AsBool(false);
                    if (info.Url.Length > 0) v.Files.Add(info);
                }
                foreach (Json d in item["dependencies"].AsArray())
                {
                    ModDependency dep = new ModDependency();
                    dep.ProjectId = d["project_id"].AsString("");
                    dep.VersionId = d["version_id"].AsString("");
                    dep.DependencyType = d["dependency_type"].AsString("required");
                    if (dep.ProjectId.Length > 0) v.Dependencies.Add(dep);
                }
                result.Add(v);
            }
            return result;
        }

        public static ModProject GetProject(string projectId)
        {
            Json root = Net.GetJson(ApiBase + "/project/" + Uri.EscapeDataString(projectId));
            ModProject p = new ModProject();
            p.Id = root["id"].AsString(projectId);
            p.Slug = root["slug"].AsString("");
            p.Title = root["title"].AsString("");
            p.Description = root["description"].AsString("");
            p.IconUrl = root["icon_url"].AsString("");
            p.Downloads = root["downloads"].AsLong(0);
            return p;
        }
    }

    public static class ModInstaller
    {
        /// <summary>下载模组及其必需前置，返回新安装的文件名列表</summary>
        public static List<string> Install(string projectId, string gameVersion, string loader, string modsDir, ProgressHandler progress)
        {
            List<string> installed = new List<string>();
            InstallInternal(projectId, gameVersion, loader, modsDir, progress, installed, 0);
            return installed;
        }

        private static void InstallInternal(string projectId, string gameVersion, string loader, string modsDir,
            ProgressHandler progress, List<string> installed, int depth)
        {
            if (depth > 3) return;
            Util.EnsureDir(modsDir);

            if (progress != null) progress(0, 0, "查找适配版本…");
            List<ModVersion> versions = Modrinth.GetVersions(projectId, gameVersion, loader);
            if (versions.Count == 0)
            {
                if (depth == 0) throw new Exception("没有找到适配 " + gameVersion + " / " + loader + " 的文件版本");
                return;
            }
            ModVersion chosen = versions[0];
            ModFileInfo file = chosen.PrimaryFile();
            if (file == null) throw new Exception("该版本没有可下载文件");

            string dest = Path.Combine(modsDir, Util.SafeFileName(file.FileName));
            if (File.Exists(dest))
            {
                Log.Write("已存在，跳过：" + file.FileName);
                installed.Add(file.FileName);
            }
            else
            {
                if (progress != null) progress(0, 0, "下载 " + chosen.Name);
                List<DownloadItem> items = new List<DownloadItem>();
                items.Add(new DownloadItem(file.Url, dest, "", file.Size));
                Downloader.Run(items, 1, null);
                installed.Add(file.FileName);
                Log.Write("已安装模组：" + file.FileName);
            }

            for (int i = 0; i < chosen.Dependencies.Count; i++)
            {
                ModDependency dep = chosen.Dependencies[i];
                if (dep.DependencyType != "required") continue;
                if (dep.ProjectId.Length == 0) continue;
                string depId = dep.ProjectId;
                if (installed.Count > 0)
                {
                    bool skip = false;
                    for (int k = 0; k < installed.Count; k++)
                    {
                        if (installed[k].ToLowerInvariant().IndexOf(depId.ToLowerInvariant().Substring(0, Math.Min(8, depId.Length))) >= 0) skip = true;
                    }
                    if (skip) continue;
                }
                if (progress != null) progress(0, 0, "安装前置依赖…");
                InstallInternal(depId, gameVersion, loader, modsDir, progress, installed, depth + 1);
            }
        }
    }

    public class ModFile
    {
        public string FilePath = "";
        public string FileName = "";
        public bool Enabled = true;
        public long Size = 0;
        public string Title = "";
        public string Version = "";
        public string Loader = "";

        public override string ToString()
        {
            return FileName;
        }
    }

    public static class ModManager
    {
        public static List<ModFile> Scan(string modsDir)
        {
            List<ModFile> result = new List<ModFile>();
            if (!Directory.Exists(modsDir)) return result;
            string[] files;
            try
            {
                files = Directory.GetFiles(modsDir);
            }
            catch
            {
                return result;
            }
            for (int i = 0; i < files.Length; i++)
            {
                string path = files[i];
                string name = Path.GetFileName(path);
                bool enabled;
                string lower = name.ToLowerInvariant();
                if (lower.EndsWith(".jar"))
                {
                    enabled = true;
                }
                else if (lower.EndsWith(".jar.disabled"))
                {
                    enabled = false;
                }
                else
                {
                    continue;
                }
                ModFile mod = new ModFile();
                mod.FilePath = path;
                mod.FileName = name;
                mod.Enabled = enabled;
                try
                {
                    mod.Size = new FileInfo(path).Length;
                }
                catch
                {
                }
                ReadMeta(mod);
                result.Add(mod);
            }
            result.Sort(delegate(ModFile a, ModFile b)
            {
                return string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
            });
            return result;
        }

        private static void ReadMeta(ModFile mod)
        {
            string fallback = mod.FileName;
            if (fallback.ToLowerInvariant().EndsWith(".disabled"))
                fallback = fallback.Substring(0, fallback.Length - ".disabled".Length);
            mod.Title = Path.GetFileNameWithoutExtension(fallback);
            mod.Version = "";
            try
            {
                using (ZipArchive zip = ZipFile.OpenRead(mod.FilePath))
                {
                    ZipArchiveEntry entry = zip.GetEntry("fabric.mod.json");
                    if (entry == null) entry = zip.GetEntry("quilt.mod.json");
                    if (entry != null)
                    {
                        using (StreamReader reader = new StreamReader(entry.Open(), Encoding.UTF8))
                        {
                            string text = reader.ReadToEnd();
                            Json root = Json.TryParse(text, Json.Null);
                            if (root.IsObject)
                            {
                                string name = root["name"].AsString("");
                                string version = root["version"].AsString("");
                                if (name.Length > 0) mod.Title = name;
                                mod.Version = version;
                                mod.Loader = "Fabric";
                            }
                            else
                            {
                                mod.Loader = "Fabric";
                            }
                        }
                        return;
                    }

                    entry = zip.GetEntry("META-INF/mods.toml");
                    if (entry == null) entry = zip.GetEntry("META-INF/neoforge.mods.toml");
                    if (entry != null)
                    {
                        using (StreamReader reader = new StreamReader(entry.Open(), Encoding.UTF8))
                        {
                            string text = reader.ReadToEnd();
                            string displayName = TomlValue(text, "displayName");
                            string version = TomlValue(text, "version");
                            if (displayName.Length > 0) mod.Title = displayName;
                            mod.Version = version;
                            mod.Loader = "Forge";
                        }
                        return;
                    }

                    entry = zip.GetEntry("mcmod.info");
                    if (entry != null)
                    {
                        using (StreamReader reader = new StreamReader(entry.Open(), Encoding.UTF8))
                        {
                            string text = reader.ReadToEnd();
                            Json root = Json.TryParse(text, Json.Null);
                            Json first = root.IsArray ? root[0] : root;
                            if (first.IsObject)
                            {
                                string name = first["name"].AsString("");
                                if (name.Length > 0) mod.Title = name;
                                mod.Version = first["version"].AsString("");
                            }
                            mod.Loader = "Forge";
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private static string TomlValue(string text, string key)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string[] lines = text.Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (!line.StartsWith(key, StringComparison.OrdinalIgnoreCase)) continue;
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string value = line.Substring(eq + 1).Trim().Trim('"');
                return value;
            }
            return "";
        }

        public static bool SetEnabled(ModFile mod, bool enabled)
        {
            try
            {
                if (mod.Enabled == enabled) return true;
                string target;
                if (enabled)
                {
                    if (!mod.FilePath.ToLowerInvariant().EndsWith(".jar.disabled")) return false;
                    target = mod.FilePath.Substring(0, mod.FilePath.Length - ".disabled".Length);
                }
                else
                {
                    target = mod.FilePath + ".disabled";
                }
                if (File.Exists(target)) File.Delete(target);
                File.Move(mod.FilePath, target);
                mod.FilePath = target;
                mod.FileName = Path.GetFileName(target);
                mod.Enabled = enabled;
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("切换模组状态失败：" + ex.Message);
                return false;
            }
        }

        public static bool Delete(ModFile mod)
        {
            try
            {
                if (File.Exists(mod.FilePath)) File.Delete(mod.FilePath);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("删除模组失败：" + ex.Message);
                return false;
            }
        }
    }
}
