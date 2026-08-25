using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Sundoll.Infrastructure;

namespace Sundoll.Tests.EditMode
{
    public sealed class ProjectWorkspaceServiceTests
    {
        private readonly List<M2SaveSession> sessions = new List<M2SaveSession>();
        private string testRoot;

        [SetUp]
        public void SetUp()
        {
            testRoot = Path.Combine(Path.GetTempPath(), "Sundoll-Workspace-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var session in sessions)
            {
                session?.Dispose();
            }

            sessions.Clear();
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, true);
            }
        }

        [Test]
        public void CreateAndOpen_UsesDistinctProjectRootAndRecentCatalogue()
        {
            var service = CreateService();
            var created = service.Create("中文测试项目");
            sessions.Add(created.saveSession);

            Assert.That(created.created, Is.True);
            Assert.That(created.saveSession.State.project.displayName, Is.EqualTo("中文测试项目"));
            Assert.That(created.saveSession.State.map, Is.Not.Null);
            Assert.That(File.Exists(Path.Combine(created.projectRoot, "HEAD.json")), Is.True);

            created.saveSession.Dispose();
            sessions.Remove(created.saveSession);
            var opened = service.Open(created.projectRoot);
            sessions.Add(opened.saveSession);

            Assert.That(opened.created, Is.False);
            Assert.That(opened.saveSession.State.project.displayName, Is.EqualTo("中文测试项目"));
            Assert.That(service.GetRecentProjects(), Has.Count.EqualTo(1));
            Assert.That(service.GetRecentProjects()[0].projectRoot, Is.EqualTo(created.projectRoot));
        }

        [Test]
        public void Create_WithSameName_DoesNotOverwriteExistingProject()
        {
            var service = CreateService();
            var first = service.Create("重复名称");
            var second = service.Create("重复名称");
            sessions.Add(first.saveSession);
            sessions.Add(second.saveSession);

            Assert.That(second.projectRoot, Is.Not.EqualTo(first.projectRoot));
            Assert.That(Directory.Exists(first.projectRoot), Is.True);
            Assert.That(Directory.Exists(second.projectRoot), Is.True);
            Assert.That(service.GetRecentProjects(), Has.Count.EqualTo(2));
        }

        [Test]
        public void ExportAndImport_PreservesCanonicalWorldState()
        {
            var service = CreateService();
            var created = service.Create("打包源项目");
            sessions.Add(created.saveSession);
            var packagePath = Path.Combine(testRoot, "exports", "portable.sundollpkg");

            service.Export(created.saveSession, packagePath);
            var imported = service.Import(packagePath, "导入项目");
            sessions.Add(imported.saveSession);

            Assert.That(File.Exists(packagePath), Is.True);
            Assert.That(imported.created, Is.True);
            Assert.That(imported.projectRoot, Is.Not.EqualTo(created.projectRoot));
            Assert.That(
                M2CanonicalStateHasher.Compute(imported.saveSession.State),
                Is.EqualTo(M2CanonicalStateHasher.Compute(created.saveSession.State)));
        }

        [Test]
        public void DamagedRecentCatalogue_DoesNotBlockProjectCreation()
        {
            var service = CreateService();
            File.WriteAllText(service.RecentProjectsPath, "{ damaged json");

            Assert.That(service.GetRecentProjects(), Is.Empty);
            var created = service.Create("恢复后的项目");
            sessions.Add(created.saveSession);

            Assert.That(created.saveSession.State.project.displayName, Is.EqualTo("恢复后的项目"));
            Assert.That(service.GetRecentProjects(), Has.Count.EqualTo(1));
        }

        private ProjectWorkspaceService CreateService()
        {
            return new ProjectWorkspaceService(
                Path.Combine(testRoot, "projects"),
                Path.Combine(testRoot, "settings"));
        }
    }
}
