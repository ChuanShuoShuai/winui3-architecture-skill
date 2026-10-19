using System;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace MyApp;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        App.LogStartup("MainWindow ctor begin");
        InitializeComponent();

        // Custom title bar: the strip must be a UIElement with a non-null background.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarArea);

        AppWindow.Resize(new Windows.Graphics.SizeInt32(AppSettings.WindowWidth, AppSettings.WindowHeight));

        ApplyTheme();
        ApplyBackdrop(AppSettings.BackdropMode);
        RestoreLastPage();

        App.LogStartup("MainWindow ctor done");
    }

    // ---- theme ------------------------------------------------------------

    public void ApplyTheme() => ThemeHelper.ApplyTheme(RootGrid, AppSettings.ThemeIndex, AppBackdrop);

    // ---- backdrop ---------------------------------------------------------

    /// <summary>
    /// Apply the requested backdrop. When the resolved kind is None there is
    /// nothing behind the window, so an opaque colour must be painted here —
    /// forgetting this shows the desktop through the window.
    /// </summary>
    public void ApplyBackdrop(int mode)
    {
        var requested = mode switch
        {
            1 => BackdropKind.Acrylic,
            2 => BackdropKind.Mica,
            _ => BackdropKind.None
        };

        AppBackdrop.SetKind(requested);

        bool hasMaterial = AppBackdrop.EffectiveKind != BackdropKind.None;
        RootGrid.Background = hasMaterial
            ? null
            : new SolidColorBrush(ThemeHelper.CurrentTheme == ApplicationTheme.Dark
                ? Color.FromArgb(255, 32, 32, 32)
                : Color.FromArgb(255, 243, 243, 243));
    }

    // ---- navigation -------------------------------------------------------

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item) return;
        if (item.Tag is not string tag) return;

        NavigateTo(tag);
    }

    /// <summary>
    /// Map a Tag to a page type. Add your pages here; keep the guard on
    /// CurrentSourcePageType so re-selecting the same item does not rebuild
    /// the visual tree and throw away scroll position.
    /// </summary>
    private void NavigateTo(string tag)
    {
        Type? page = tag switch
        {
            "home"     => null,   // typeof(HomePage)
            "second"   => null,   // typeof(SecondPage)
            "settings" => null,   // typeof(SettingsPage)
            _ => null
        };

        if (page is null) return;
        if (ContentFrame.CurrentSourcePageType == page) return;

        AppSettings.LastPage = tag;
        AppSettings.Save();
        ContentFrame.Navigate(page, null, new EntranceNavigationTransitionInfo());
    }

    /// <summary>Select the saved item so navigation happens through the normal path.</summary>
    private void RestoreLastPage()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            foreach (var obj in NavView.MenuItems)
            {
                if (obj is NavigationViewItem i && (i.Tag as string) == AppSettings.LastPage)
                {
                    NavView.SelectedItem = i;
                    return;
                }
            }

            foreach (var obj in NavView.FooterMenuItems)
            {
                if (obj is NavigationViewItem i && (i.Tag as string) == AppSettings.LastPage)
                {
                    NavView.SelectedItem = i;
                    return;
                }
            }

            NavView.SelectedItem = NavView.MenuItems.Count > 0 ? NavView.MenuItems[0] : null;
        });
    }

    // ---- state persistence ------------------------------------------------

    public void PersistWindowState()
    {
        AppSettings.WindowWidth = AppWindow.Size.Width;
        AppSettings.WindowHeight = AppWindow.Size.Height;
        AppSettings.Save();
    }
}
