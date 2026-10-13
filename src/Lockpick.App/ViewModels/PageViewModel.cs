using CommunityToolkit.Mvvm.ComponentModel;

namespace Lockpick.App.ViewModels;

/// <summary>A top-level navigation page.</summary>
public abstract class PageViewModel(string title, string iconKey) : ObservableObject
{
    public string Title { get; } = title;

    /// <summary>Resource key of the page's Lucide icon geometry (see Assets/Icons.axaml).</summary>
    public string IconKey { get; } = iconKey;

    /// <summary>Called when the page becomes visible or hidden, so pages can pause periodic work.</summary>
    public virtual void OnVisibilityChanged(bool visible)
    {
    }
}
