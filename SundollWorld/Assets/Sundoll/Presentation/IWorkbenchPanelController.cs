using System;

namespace Sundoll.Presentation
{
    /// <summary>
    /// Common lifetime contract for Workbench panels. Controllers may read a
    /// session and invoke its facades, but never become an authority themselves.
    /// </summary>
    public interface IWorkbenchPanelController : IDisposable
    {
        void Bind(WorkbenchSession session);
        void Refresh();
    }
}
