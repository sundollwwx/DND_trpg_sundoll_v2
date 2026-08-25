using System;
using Sundoll.Application;
using Sundoll.Core;
using Sundoll.Infrastructure;

namespace Sundoll.Presentation
{
    /// <summary>
    /// Explicit lifetime boundary for one open project. It centralizes runtime
    /// composition without moving authority out of the command bus or save data
    /// into Unity objects.
    /// </summary>
    public sealed class WorkbenchSession : IDisposable
    {
        private bool disposed;

        public WorkbenchSession(M2SaveSession saveSession)
        {
            SaveSession = saveSession ?? throw new ArgumentNullException(nameof(saveSession));
            CommandBus = new M1CommandBus(
                saveSession.State,
                new M1LocalAuthority(new AllowAllRulePolicy()));
            MapEditor = new M3MapEditorFacade(CommandBus);
            PieceLibrary = new M4PieceLibraryFacade(CommandBus);
            PieceAssetCatalog = new M4PieceAssetCatalog(saveSession.ProjectRoot);
            Console = new M5ConsoleFacade(CommandBus);
            M5ConsoleQueries.Ensure(CommandBus.State);
            WorkspaceStateStore = new M3WorkspaceStateStore(saveSession.ProjectRoot);
        }

        public M2SaveSession SaveSession { get; }
        public M1CommandBus CommandBus { get; }
        public M3MapEditorFacade MapEditor { get; }
        public M4PieceLibraryFacade PieceLibrary { get; }
        public M4PieceAssetCatalog PieceAssetCatalog { get; }
        public M5ConsoleFacade Console { get; }
        public M3WorkspaceStateStore WorkspaceStateStore { get; }
        public string ProjectRoot => SaveSession.ProjectRoot;
        public string ProjectDisplayName => CommandBus.State.project == null
            ? "未命名项目"
            : CommandBus.State.project.displayName;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            SaveSession.Dispose();
        }
    }
}
