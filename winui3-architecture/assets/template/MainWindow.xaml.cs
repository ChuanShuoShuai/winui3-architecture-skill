using System;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI;

namespace MyApp;

public sealed partial class MainWindow : Window
{
    // Pane backdrop: docked or collapsed = transparent (the window backdrop shows
    // through); floating over the page while open = acrylic blur.
    // See UpdatePaneBackdrop — the theme keys stay Transparent in App.xaml.
    private readonly SolidColorBrush _paneInlineBrush = new(Microsoft.UI.Colors.Transparent);
    private SplitView? _paneSplitView;
    private AcrylicBrush? _paneOverlayAcrylic;

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
        UpdateContentInset(NavView.DisplayMode);
        RestoreLastPage();

        // Pane backdrop: every state change that can move the pane between
        // docked and overlay must re-evaluate it.
        NavView.Loaded += (_, _) => UpdatePaneBackdrop();
        NavView.PaneOpening += (_, _) => UpdatePaneBackdrop();
        NavView.PaneOpened += (_, _) => UpdatePaneBackdrop();
        NavView.PaneClosed += (_, _) => UpdatePaneBackdrop();
        SizeChanged += (_, _) => UpdatePaneBackdrop();

        // The pane toggle button takes keyboard focus on launch, which paints a
        // heavy focus rectangle over the hamburger. Hand focus to the content
        // area instead — FocusState.Pointer moves focus without the visual.
        RootGrid.Loaded += (_, _) => RootGrid.Focus(FocusState.Pointer);

        App.LogStartup("MainWindow ctor done");
    }

    /// <summary>
    /// In LeftMinimal the pane floats over the content and the hamburger button
    /// sits at the content's top-left corner. Give the frame a top inset in that
    /// mode only, so page titles clear the button; docked modes need no inset.
    /// </summary>
    private void NavView_DisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
    {
        UpdateContentInset(args.DisplayMode);
    }

    private void UpdateContentInset(NavigationViewDisplayMode mode)
    {
        ContentFrame.Margin = mode == NavigationViewDisplayMode.Minimal
            ? new Thickness(0, 48, 0, 0)
            : new Thickness(0);
    }

    /// <summary>
    /// Sync the navigation pane background: only when the pane floats OVER the
    /// page (inner SplitView Overlay/CompactOverlay AND IsPaneOpen) is it acrylic;
    /// docked or collapsed stays transparent so the window backdrop (Mica on
    /// Win11, system acrylic on Win10) shows through. Theme resource keys stay
    /// Transparent in App.xaml — the reliable switch is the template's inner
    /// SplitView, reached by its template name "RootSplitView".
    /// </summary>
    private void UpdatePaneBackdrop()
    {
        _paneSplitView ??= FindChildByName(NavView, "RootSplitView") as SplitView;
        if (_paneSplitView is not SplitView sv) return;

        bool overlay = sv.DisplayMode is SplitViewDisplayMode.Overlay or SplitViewDisplayMode.CompactOverlay;
        sv.PaneBackground = overlay && sv.IsPaneOpen ? PaneOverlayAcrylic() : _paneInlineBrush;
    }

    private AcrylicBrush PaneOverlayAcrylic()
    {
        bool dark = RootGrid.ActualTheme == ElementTheme.Dark;
        var tint = dark
            ? Windows.UI.Color.FromArgb(0xFF, 0x20, 0x20, 0x20)
            : Windows.UI.Color.FromArgb(0xFF, 0xF9, 0xF9, 0xF9);

        _paneOverlayAcrylic ??= new AcrylicBrush { TintOpacity = 0.6 };
        _paneOverlayAcrylic.TintColor = tint;
        _paneOverlayAcrylic.FallbackColor = tint;
        return _paneOverlayAcrylic;
    }

    private static FrameworkElement? FindChildByName(DependencyObject root, string name)
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement fe && fe.Name == name) return fe;
            var found = FindChildByName(child, name);
            if (found != null) return found;
        }
        return null;
    }

    // ---- theme ------------------------------------------------------------

    public void ApplyTheme()
    {
        ThemeHelper.ApplyTheme(RootGrid, AppSettings.ThemeIndex, AppBackdrop);
        UpdatePaneBackdrop();   // re-tint the overlay acrylic for the new theme
    }

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
