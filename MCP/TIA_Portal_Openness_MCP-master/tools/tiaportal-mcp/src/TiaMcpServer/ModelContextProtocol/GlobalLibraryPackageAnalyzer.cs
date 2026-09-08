using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace TiaMcpServer.ModelContextProtocol
{
    public static class GlobalLibraryPackageAnalyzer
    {
        public static JsonObject Analyze(string libraryPath)
        {
            var rootDir = Directory.Exists(libraryPath)
                ? libraryPath
                : Path.GetDirectoryName(libraryPath) ?? libraryPath;
            var info = new JsonObject
            {
                ["inputPath"] = libraryPath,
                ["rootDir"] = rootDir,
                ["exists"] = Directory.Exists(rootDir)
            };

            if (!Directory.Exists(rootDir))
            {
                info["ok"] = false;
                info["error"] = "Global library directory not found.";
                info["recommendations"] = new JsonArray("确认路径指向全局库目录，或传入包含 System/XRef 子目录的库根目录。");
                return info;
            }

            var alFiles = Directory.EnumerateFiles(rootDir, "*.al*", SearchOption.TopDirectoryOnly)
                .Select(x => new FileInfo(x))
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var systemDir = Path.Combine(rootDir, "System");
            var xrefDir = Path.Combine(rootDir, "XRef");
            var peDataPlf = Path.Combine(systemDir, "PEData.plf");
            var peDataIdx = Path.Combine(systemDir, "PEData.idx");
            var xrefDb = Path.Combine(xrefDir, "XRef.db");

            info["libraryFiles"] = new JsonArray(alFiles.Select(f => FileInfoToJson(rootDir, f, includeSha256: true)).ToArray());
            info["topLevel"] = AnalyzeDirectorySummary(rootDir, 40);
            info["sections"] = new JsonObject
            {
                ["System"] = AnalyzeDirectorySummary(systemDir, 20),
                ["XRef"] = AnalyzeDirectorySummary(xrefDir, 20),
                ["src"] = AnalyzeDirectorySummary(Path.Combine(rootDir, "src"), 20),
                ["UserFiles"] = AnalyzeDirectorySummary(Path.Combine(rootDir, "UserFiles"), 20),
                ["AdditionalFiles"] = AnalyzeDirectorySummary(Path.Combine(rootDir, "AdditionalFiles"), 20)
            };

            info["requiredFiles"] = new JsonObject
            {
                ["PEData.plf"] = File.Exists(peDataPlf) ? FileInfoToJson(rootDir, new FileInfo(peDataPlf), includeSha256: false) : new JsonObject { ["exists"] = false, ["path"] = peDataPlf },
                ["PEData.idx"] = File.Exists(peDataIdx) ? FileInfoToJson(rootDir, new FileInfo(peDataIdx), includeSha256: false) : new JsonObject { ["exists"] = false, ["path"] = peDataIdx },
                ["XRef.db"] = File.Exists(xrefDb) ? FileInfoToJson(rootDir, new FileInfo(xrefDb), includeSha256: false) : new JsonObject { ["exists"] = false, ["path"] = xrefDb }
            };

            info["xref"] = AnalyzeSqliteDatabaseHeader(xrefDb);
            info["stringHints"] = AnalyzeLibraryStringHints(rootDir, 150);

            var hasCoreFiles = File.Exists(peDataPlf) && File.Exists(peDataIdx) && File.Exists(xrefDb);
            info["ok"] = hasCoreFiles;
            info["recommendations"] = BuildRecommendations(info);
            return info;
        }

        public static string BuildMarkdown(JsonObject root, string jsonPath)
        {
            var md = new StringBuilder();
            md.AppendLine("# Global Library Package Analysis");
            md.AppendLine();
            md.AppendLine("Generated: " + root["timestamp"]?.ToString());
            md.AppendLine("JSON: " + jsonPath);
            md.AppendLine();
            md.AppendLine("## Safety");
            md.AppendLine("- Offline file-system analysis only.");
            md.AppendLine("- TIA Portal is not connected or opened.");
            md.AppendLine("- No global library content is imported, modified, or written.");
            md.AppendLine();
            md.AppendLine("## Summary");
            md.AppendLine("- Input: " + root["inputPath"]);
            md.AppendLine("- Root: " + root["rootDir"]);
            md.AppendLine("- Exists: " + root["exists"]);
            md.AppendLine("- OK: " + root["ok"]);
            md.AppendLine();

            md.AppendLine("## Library Files");
            if (root["libraryFiles"] is JsonArray libraryFiles && libraryFiles.Count > 0)
            {
                foreach (var item in libraryFiles)
                {
                    var obj = item as JsonObject;
                    var hashText = obj?["sha256Status"]?.ToString() == "ok"
                        ? "sha256=" + obj?["sha256"]
                        : "sha256 unavailable: " + obj?["sha256Error"];
                    md.AppendLine("- " + obj?["relativePath"] + " (" + obj?["bytes"] + " bytes, " + hashText + ")");
                }
            }
            else
            {
                md.AppendLine("- No `.al*` file found at top level.");
            }
            md.AppendLine();

            md.AppendLine("## Required Files");
            if (root["requiredFiles"] is JsonObject requiredFiles)
            {
                foreach (var kv in requiredFiles)
                {
                    var obj = kv.Value as JsonObject;
                    md.AppendLine("- " + kv.Key + ": exists=" + obj?["exists"] + ", bytes=" + obj?["bytes"] + ", path=" + obj?["relativePath"]);
                }
            }
            md.AppendLine();

            md.AppendLine("## Sections");
            AppendSectionSummary(md, root, "System", "System");
            AppendSectionSummary(md, root, "XRef", "XRef");
            AppendSectionSummary(md, root, "src", "src");
            AppendSectionSummary(md, root, "UserFiles", "UserFiles");
            AppendSectionSummary(md, root, "AdditionalFiles", "AdditionalFiles");
            md.AppendLine();

            md.AppendLine("## XRef");
            var xref = root["xref"] as JsonObject;
            md.AppendLine("- Exists: " + xref?["exists"]);
            md.AppendLine("- SQLite: " + xref?["isSqlite"]);
            md.AppendLine("- Header: `" + xref?["header"] + "`");
            md.AppendLine();

            md.AppendLine("## Recommendations");
            if (root["recommendations"] is JsonArray recs)
            {
                foreach (var rec in recs)
                {
                    md.AppendLine("- " + rec);
                }
            }
            return md.ToString();
        }

        private static JsonObject FileInfoToJson(string rootDir, FileInfo file, bool includeSha256)
        {
            var obj = new JsonObject
            {
                ["exists"] = file.Exists,
                ["name"] = file.Name,
                ["path"] = file.FullName,
                ["relativePath"] = MakeRelativePath(rootDir, file.FullName),
                ["bytes"] = file.Exists ? file.Length : 0,
                ["lastWriteTime"] = file.Exists ? file.LastWriteTime.ToString("O") : ""
            };
            if (includeSha256 && file.Exists)
            {
                var hash = TryComputeSha256(file.FullName);
                obj["sha256Status"] = hash.Ok ? "ok" : "unavailable";
                obj["sha256"] = hash.Sha256 ?? "";
                obj["sha256Error"] = hash.Error ?? "";
            }
            return obj;
        }

        private static (bool Ok, string? Sha256, string? Error) TryComputeSha256(string file)
        {
            try
            {
                using var sha = SHA256.Create();
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return (true, BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(), null);
            }
            catch (Exception ex)
            {
                return (false, null, ex.Message);
            }
        }

        private static JsonObject AnalyzeDirectorySummary(string dir, int sampleLimit)
        {
            var obj = new JsonObject
            {
                ["path"] = dir,
                ["exists"] = Directory.Exists(dir)
            };

            if (!Directory.Exists(dir))
            {
                obj["fileCount"] = 0;
                obj["totalBytes"] = 0;
                obj["samples"] = new JsonArray();
                return obj;
            }

            var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.Length)
                .ToList();

            obj["fileCount"] = files.Count;
            obj["totalBytes"] = files.Sum(f => f.Length);
            obj["samples"] = new JsonArray(files.Take(sampleLimit).Select(f => new JsonObject
            {
                ["name"] = f.Name,
                ["relativePath"] = MakeRelativePath(dir, f.FullName),
                ["bytes"] = f.Length
            }).ToArray());
            return obj;
        }

        private static JsonObject AnalyzeSqliteDatabaseHeader(string dbPath)
        {
            var info = new JsonObject
            {
                ["path"] = dbPath,
                ["exists"] = File.Exists(dbPath),
                ["isSqlite"] = false
            };
            if (!File.Exists(dbPath)) return info;

            try
            {
                var header = new byte[16];
                using (var stream = new FileStream(dbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    _ = stream.Read(header, 0, header.Length);
                }
                var headerText = Encoding.ASCII.GetString(header);
                info["header"] = headerText.TrimEnd('\0');
                info["isSqlite"] = headerText.StartsWith("SQLite format 3", StringComparison.Ordinal);

                var bytes = ReadAllBytesShared(dbPath);
                var text = ExtractPrintableAscii(bytes, 4, 80);
                info["stringSamples"] = new JsonArray(text.Take(80).Select(x => JsonValue.Create(x)).ToArray());
                info["tableNameHints"] = new JsonArray(text
                    .Where(x => x.IndexOf("sqlite_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                x.IndexOf("xref", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                x.IndexOf("object", StringComparison.OrdinalIgnoreCase) >= 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(40)
                    .Select(x => JsonValue.Create(x))
                    .ToArray());
            }
            catch (Exception ex)
            {
                info["error"] = ex.Message;
            }

            return info;
        }

        private static JsonObject AnalyzeLibraryStringHints(string rootDir, int limit)
        {
            var patterns = new[] { "MasterCopy", "Library", "Faceplate", "Screen", "Template", "Unified", "WinCC", "Type", "HMI", "Siemens" };
            var hits = new JsonObject();
            foreach (var pattern in patterns) hits[pattern] = 0;

            var examples = new JsonArray();
            var files = Directory.EnumerateFiles(rootDir, "*", SearchOption.AllDirectories)
                .Select(x => new FileInfo(x))
                .Where(x => x.Length <= 20 * 1024 * 1024 || x.Name.Equals("XRef.db", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.Length)
                .Take(200);

            foreach (var file in files)
            {
                try
                {
                    var text = Encoding.UTF8.GetString(ReadAllBytesShared(file.FullName));
                    foreach (var pattern in patterns)
                    {
                        var count = CountOccurrences(text, pattern);
                        if (count <= 0) continue;
                        hits[pattern] = (int)(hits[pattern]?.GetValue<int>() ?? 0) + count;
                        if (examples.Count < limit)
                        {
                            examples.Add(new JsonObject
                            {
                                ["file"] = MakeRelativePath(rootDir, file.FullName),
                                ["pattern"] = pattern,
                                ["count"] = count
                            });
                        }
                    }
                }
                catch
                {
                    // 全局库大多是二进制，字符串提示只做尽力扫描。
                }
            }

            return new JsonObject { ["patternCounts"] = hits, ["examples"] = examples };
        }

        private static byte[] ReadAllBytesShared(string file)
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }

        private static int CountOccurrences(string text, string pattern)
        {
            var count = 0;
            var index = 0;
            while ((index = text.IndexOf(pattern, index, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                count++;
                index += pattern.Length;
            }
            return count;
        }

        private static List<string> ExtractPrintableAscii(byte[] bytes, int minLength, int maxItems)
        {
            var result = new List<string>();
            var sb = new StringBuilder();
            foreach (var b in bytes)
            {
                if (b >= 32 && b <= 126)
                {
                    sb.Append((char)b);
                    continue;
                }

                Flush();
                if (result.Count >= maxItems) break;
            }
            Flush();
            return result;

            void Flush()
            {
                if (sb.Length >= minLength) result.Add(sb.ToString());
                sb.Clear();
            }
        }

        private static JsonArray BuildRecommendations(JsonObject info)
        {
            var list = new JsonArray
            {
                "离线扫描只能证明库包结构和核心文件存在，不能证明库对象可被 TIA Openness 枚举或导入。",
                "下一步应在已连接 TIA 的前提下运行 ProbeGlobalLibrary，只读列出 MasterCopies/Types/Folders。",
                "在 ProbeGlobalLibrary 未读回对象层级前，不要承诺从全局库批量导入 HMI 模板或面板对象。",
                "导入类功能必须先在临时项目验证：导入、读回、编译/校验、保存报告，然后再开放给真实项目使用。"
            };
            if (info["ok"]?.GetValue<bool>() != true)
            {
                list.Add("当前库包缺少 PEData.plf、PEData.idx 或 XRef.db，优先检查参考库路径是否完整。");
            }
            return list;
        }

        private static void AppendSectionSummary(StringBuilder md, JsonObject? parent, string key, string title)
        {
            var sections = parent?["sections"] as JsonObject;
            var section = sections?[key] as JsonObject;
            if (section == null) return;

            md.AppendLine();
            md.AppendLine("### " + title);
            md.AppendLine("- Exists: " + section["exists"]);
            md.AppendLine("- File count: " + section["fileCount"]);
            md.AppendLine("- Total bytes: " + section["totalBytes"]);
            if (section["samples"] is JsonArray samples && samples.Count > 0)
            {
                md.AppendLine("- Largest samples:");
                foreach (var item in samples.Take(5))
                {
                    var obj = item as JsonObject;
                    md.AppendLine("  - " + obj?["relativePath"] + " (" + obj?["bytes"] + " bytes)");
                }
            }
        }

        private static string MakeRelativePath(string baseDir, string fullPath)
        {
            try
            {
                var baseUri = new Uri(AppendDirectorySeparatorChar(Path.GetFullPath(baseDir)));
                var fileUri = new Uri(Path.GetFullPath(fullPath));
                var relative = Uri.UnescapeDataString(baseUri.MakeRelativeUri(fileUri).ToString());
                return relative.Replace('/', Path.DirectorySeparatorChar);
            }
            catch
            {
                return fullPath;
            }
        }

        private static string AppendDirectorySeparatorChar(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            return path.EndsWith(Path.DirectorySeparatorChar.ToString()) || path.EndsWith(Path.AltDirectorySeparatorChar.ToString())
                ? path
                : path + Path.DirectorySeparatorChar;
        }
    }
}
