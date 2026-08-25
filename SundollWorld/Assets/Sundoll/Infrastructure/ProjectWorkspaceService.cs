using System;
using System.Collections.Generic;
using System.IO;
using Sundoll.Application;
using Sundoll.Core;
using UnityEngine;

namespace Sundoll.Infrastructure
{
    [Serializable]
    internal sealed class ProjectWorkspaceRecentDocument
    {
        public int formatVersion = 1;
        public List<ProjectWorkspaceEntry> entries = new List<ProjectWorkspaceEntry>();
    }

    [Serializable]
    public sealed class ProjectWorkspaceEntry
    {
        public string projectRoot;
        public string projectId;
        public string displayName;
        public string lastOpenedUtc;

        public ProjectWorkspaceEntry DeepClone()
        {
            return new ProjectWorkspaceEntry
            {
                projectRoot = projectRoot,
                projectId = projectId,
                displayName = displayName,
                lastOpenedUtc = lastOpenedUtc
            };
        }
    }

    public sealed class ProjectWorkspaceOpenResult
    {
        public string projectRoot;
        public M2SaveSession saveSession;
        public bool created;
        public string diagnostic;
    }

    /// <summary>
    /// Owns the desktop-level project catalogue. World mutations remain in the
    /// command bus and project bytes remain owned by the M2 persistence layer.
    /// </summary>
    public sealed class ProjectWorkspaceService
    {
        private const int CurrentRecentFormatVersion = 1;
        private const int MaxRecentProjects = 12;
        private readonly string recentProjectsPath;

        public ProjectWorkspaceService(string workspaceRoot, string settingsRoot)
        {
            if (string.IsNullOrWhiteSpace(workspaceRoot))
            {
                throw new ArgumentException("Workspace root is required.", nameof(workspaceRoot));
            }

            if (string.IsNullOrWhiteSpace(settingsRoot))
            {
                throw new ArgumentException("Workspace settings root is required.", nameof(settingsRoot));
            }

            WorkspaceRoot = Path.GetFullPath(workspaceRoot);
            SettingsRoot = Path.GetFullPath(settingsRoot);
            M2FileIO.EnsureDirectory(WorkspaceRoot);
            M2FileIO.EnsureDirectory(SettingsRoot);
            recentProjectsPath = Path.Combine(SettingsRoot, "recent-projects.json");
        }

        public string WorkspaceRoot { get; }
        public string SettingsRoot { get; }
        public string RecentProjectsPath => recentProjectsPath;

        public ProjectWorkspaceOpenResult Create(string displayName)
        {
            displayName = NormalizeDisplayName(displayName);
            var projectRoot = CreateUniqueProjectRoot(displayName);
            var idSuffix = Guid.NewGuid().ToString("N");
            var bus = new M1CommandBus(
                M1WorldState.CreateEmpty(),
                new M1LocalAuthority(new AllowAllRulePolicy()));
            var receipt = bus.Execute(new M1CreateProjectCommand(
                "project-create-" + idSuffix,
                bus.State.revision,
                "project-" + idSuffix,
                displayName,
                "map-" + idSuffix));
            if (!receipt.accepted)
            {
                throw new InvalidOperationException("Could not create project state: " + receipt.message);
            }

            try
            {
                var session = M2SaveSession.Open(projectRoot, bus.State);
                // Adopt the exact validated shape read from the first immutable
                // Revision so subsequent export/hash comparisons use disk truth.
                session.Reload();
                Remember(session.State, projectRoot);
                return new ProjectWorkspaceOpenResult
                {
                    projectRoot = projectRoot,
                    saveSession = session,
                    created = true,
                    diagnostic = "已创建项目：" + displayName
                };
            }
            catch
            {
                // Creation never targets an existing directory. Removing this
                // incomplete root cannot erase a prior user project.
                if (Directory.Exists(projectRoot))
                {
                    Directory.Delete(projectRoot, true);
                }

                throw;
            }
        }

        public ProjectWorkspaceOpenResult Open(string projectRoot)
        {
            var fullRoot = ValidateExistingProjectRoot(projectRoot);
            var store = new M2ProjectStore(fullRoot);
            var loaded = store.LoadBestAvailable();
            if (loaded == null || loaded.state == null || loaded.state.project == null)
            {
                throw new InvalidDataException("所选目录不包含可恢复的 SundollWorld 项目。");
            }

            var session = M2SaveSession.Open(fullRoot, loaded.state);
            Remember(session.State, fullRoot);
            return new ProjectWorkspaceOpenResult
            {
                projectRoot = fullRoot,
                saveSession = session,
                created = false,
                diagnostic = string.IsNullOrWhiteSpace(loaded.diagnostic)
                    ? "已打开项目：" + loaded.state.project.displayName
                    : "已打开项目；" + loaded.diagnostic
            };
        }

        public ProjectWorkspaceOpenResult Import(string packagePath, string displayName)
        {
            if (string.IsNullOrWhiteSpace(packagePath))
            {
                throw new ArgumentException("Package path is required.", nameof(packagePath));
            }

            displayName = NormalizeDisplayName(displayName);
            var destinationRoot = CreateUniqueProjectRoot(displayName);
            M2PackageArchive.Import(Path.GetFullPath(packagePath), destinationRoot);
            var opened = Open(destinationRoot);
            opened.created = true;
            opened.diagnostic = "已导入项目包：" + displayName;
            return opened;
        }

        public string Export(M2SaveSession session, string packagePath)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            if (string.IsNullOrWhiteSpace(packagePath))
            {
                throw new ArgumentException("Package path is required.", nameof(packagePath));
            }

            session.WaitForSave();
            return session.ExportPackage(Path.GetFullPath(packagePath));
        }

        public IReadOnlyList<ProjectWorkspaceEntry> GetRecentProjects()
        {
            var document = ReadRecentDocument();
            var result = new List<ProjectWorkspaceEntry>();
            foreach (var entry in document.entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.projectRoot))
                {
                    continue;
                }

                result.Add(entry.DeepClone());
            }

            return result;
        }

        private void Remember(M1WorldState state, string projectRoot)
        {
            if (state == null || state.project == null)
            {
                return;
            }

            projectRoot = Path.GetFullPath(projectRoot);
            var document = ReadRecentDocument();
            document.entries.RemoveAll(entry =>
                entry == null ||
                SameProjectRoot(entry.projectRoot, projectRoot));
            document.entries.Insert(0, new ProjectWorkspaceEntry
            {
                projectRoot = projectRoot,
                projectId = state.project.id,
                displayName = state.project.displayName,
                lastOpenedUtc = DateTime.UtcNow.ToString("O")
            });
            if (document.entries.Count > MaxRecentProjects)
            {
                document.entries.RemoveRange(MaxRecentProjects, document.entries.Count - MaxRecentProjects);
            }

            M2FileIO.WriteUtf8Atomic(recentProjectsPath, JsonUtility.ToJson(document, true));
        }

        private ProjectWorkspaceRecentDocument ReadRecentDocument()
        {
            if (!File.Exists(recentProjectsPath))
            {
                return NewRecentDocument();
            }

            try
            {
                var document = JsonUtility.FromJson<ProjectWorkspaceRecentDocument>(
                    File.ReadAllText(recentProjectsPath));
                return document != null && document.formatVersion == CurrentRecentFormatVersion && document.entries != null
                    ? document
                    : NewRecentDocument();
            }
            catch (Exception)
            {
                // Recent projects are local convenience state. A damaged list
                // must not prevent opening the authoritative project itself.
                return NewRecentDocument();
            }
        }

        private static ProjectWorkspaceRecentDocument NewRecentDocument()
        {
            return new ProjectWorkspaceRecentDocument
            {
                formatVersion = CurrentRecentFormatVersion,
                entries = new List<ProjectWorkspaceEntry>()
            };
        }

        private string CreateUniqueProjectRoot(string displayName)
        {
            var slug = ToSafeDirectoryName(displayName);
            var candidate = Path.Combine(WorkspaceRoot, slug);
            var suffix = 2;
            while (Directory.Exists(candidate) || File.Exists(candidate))
            {
                candidate = Path.Combine(WorkspaceRoot, slug + "-" + suffix++);
            }

            return candidate;
        }

        private static string ValidateExistingProjectRoot(string projectRoot)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                throw new ArgumentException("Project root is required.", nameof(projectRoot));
            }

            var fullRoot = Path.GetFullPath(projectRoot);
            if (!Directory.Exists(fullRoot))
            {
                throw new DirectoryNotFoundException("项目目录不存在：" + fullRoot);
            }

            var hasHead = File.Exists(Path.Combine(fullRoot, "HEAD.json"));
            var hasRevisions = Directory.Exists(Path.Combine(fullRoot, "revisions"));
            if (!hasHead && !hasRevisions)
            {
                throw new InvalidDataException("所选目录不是 SundollWorld 项目。");
            }

            return fullRoot;
        }

        private static bool SameProjectRoot(string candidate, string expectedFullRoot)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }

            try
            {
                return string.Equals(Path.GetFullPath(candidate), expectedFullRoot, PathComparison);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string NormalizeDisplayName(string displayName)
        {
            displayName = string.IsNullOrWhiteSpace(displayName) ? "未命名项目" : displayName.Trim();
            if (displayName.Length > 80)
            {
                displayName = displayName.Substring(0, 80).Trim();
            }

            return displayName;
        }

        private static string ToSafeDirectoryName(string displayName)
        {
            var invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
            var characters = displayName.ToCharArray();
            for (var index = 0; index < characters.Length; index++)
            {
                if (invalid.Contains(characters[index]) || characters[index] == Path.DirectorySeparatorChar ||
                    characters[index] == Path.AltDirectorySeparatorChar)
                {
                    characters[index] = '-';
                }
            }

            var result = new string(characters).Trim().Trim('.');
            return string.IsNullOrWhiteSpace(result) ? "SundollWorld-Project" : result;
        }

        private static StringComparison PathComparison =>
            Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }
}
