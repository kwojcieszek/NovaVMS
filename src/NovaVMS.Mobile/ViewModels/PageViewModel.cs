using NovaVMS.Mobile.Models;
using NovaVMS.Presentation.ViewModels;

namespace NovaVMS.Mobile.ViewModels;

/// <summary>Page shown in the shell content area.</summary>
public abstract class PageViewModel : ViewModelBase
{
    /// <summary>Bottom navigation tab highlighted while the page is shown.</summary>
    public abstract MobileTab Tab { get; }

    /// <summary>The camera list is the root page; the back button leaves the application there.</summary>
    public virtual bool IsRoot => false;

    /// <summary>
    /// Shows the back button above the page. iOS has no system back button, so every page below a tab offers one;
    /// the first page of a tab does not.
    /// </summary>
    public virtual bool HasBackButton => !IsRoot;

    /// <summary>Shows the page the back button returns to.</summary>
    public virtual void NavigateBack(ShellViewModel shell)
    {
        shell.ShowCameras();
    }
}
