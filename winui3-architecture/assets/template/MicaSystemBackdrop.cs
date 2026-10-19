using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace MyApp;

public enum BackdropKind
{
    /// <summary>No material. The window must paint an opaque colour itself.</summary>
    None = 0,
    /// <summary>System acrylic: translucent + blurred.</summary>
    Acrylic = 1,
    /// <summary>Mica: takes its tint from the desktop wallpaper. Windows 11 only.</summary>
    Mica = 2
}

/// <summary>
/// Theme-following, switchable window backdrop.
///
/// Two responsibilities:
///   1. Resolve the user's intent against what this machine supports
///      (Mica -> Acrylic -> None).
///   2. Rebuild the controller when the kind changes, without flicker.
///
/// The short-circuit in SetKind is load-bearing: theme switching calls it
/// repeatedly, and rebuilding the controller each time makes the window flash.
/// </summary>
public sealed class MicaSystemBackdrop : SystemBackdrop
{
    private MicaController? _mica;
    private DesktopAcrylicController? _acrylic;
    private SystemBackdropConfiguration? _configuration;
    private SystemBackdropTheme _theme = SystemBackdropTheme.Default;
    private BackdropKind _kind = BackdropKind.Mica;
    private ICompositionSupportsSystemBackdrop? _target;

    /// <summary>Kind actually applied on this machine (may be lower than requested).</summary>
    public BackdropKind EffectiveKind { get; private set; } = BackdropKind.Mica;

    public void SetKind(BackdropKind kind)
    {
        if (_kind == kind) return;

        _kind = kind;
        if (_target != null) ApplyControllers(_target);
        else EffectiveKind = Resolve(kind);
    }

    public void SetTheme(SystemBackdropTheme theme)
    {
        _theme = theme;
        if (_configuration != null) _configuration.Theme = theme;
    }

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);

        _target = connectedTarget;
        _configuration = new SystemBackdropConfiguration
        {
            IsInputActive = true,
            Theme = _theme
        };
        ApplyControllers(connectedTarget);
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);

        ReleaseControllers(disconnectedTarget);
        _target = null;
        _configuration = null;
    }

    private static BackdropKind Resolve(BackdropKind kind)
    {
        if (kind == BackdropKind.Mica && !MicaController.IsSupported()) kind = BackdropKind.Acrylic;
        if (kind == BackdropKind.Acrylic && !DesktopAcrylicController.IsSupported()) kind = BackdropKind.None;
        return kind;
    }

    private void ApplyControllers(ICompositionSupportsSystemBackdrop target)
    {
        ReleaseControllers(target);
        if (_configuration == null) return;

        EffectiveKind = Resolve(_kind);
        if (EffectiveKind == BackdropKind.Mica)
        {
            _mica = new MicaController();
            _mica.SetSystemBackdropConfiguration(_configuration);
            _mica.AddSystemBackdropTarget(target);
        }
        else if (EffectiveKind == BackdropKind.Acrylic)
        {
            _acrylic = new DesktopAcrylicController();
            _acrylic.SetSystemBackdropConfiguration(_configuration);
            _acrylic.AddSystemBackdropTarget(target);
        }
        // None: attach nothing. The window must paint its own background.
    }

    private void ReleaseControllers(ICompositionSupportsSystemBackdrop target)
    {
        _mica?.RemoveSystemBackdropTarget(target);
        _mica?.Dispose();
        _mica = null;

        _acrylic?.RemoveSystemBackdropTarget(target);
        _acrylic?.Dispose();
        _acrylic = null;
    }
}
