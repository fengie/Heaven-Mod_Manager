# Application branding

The canonical app icon is:

`src/MhwModManager.App/Assets/MHWModManager.ico`

It is generated from the approved MHW Manual Mod Manager dragon + mod-card artwork and contains Windows icon sizes 16, 24, 32, and 48 px.

## How it is wired

- `MhwModManager.App.csproj` sets `ApplicationIcon` so the built/published `MHW Mod Manager.exe` carries the icon.
- The same file is included as a WPF `Resource`.
- `App.xaml` sets the global `Window.Icon` through the shared Window style so main, startup, profile-editor, and future ordinary WPF windows inherit it unless they explicitly override the icon.

## Verification

A branding-only icon/project/XAML change does not justify manually changing function verification booleans. Run the normal Windows verifier/build so project/XAML fingerprints invalidate and rerun where appropriate. After publishing, visually confirm File Explorer, taskbar/Alt+Tab, and window-title-bar branding.
