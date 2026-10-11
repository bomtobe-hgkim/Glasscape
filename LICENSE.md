# Glasscape license scope

Copyright (c) 2026 BOMTOBE applies to BOMTOBE's original contributions.
Glasscape has mixed source licenses; the distributed application is not MIT-only.

- Independently authored Glasscape app code, tests, build scripts and documentation
  are available under the [MIT License](LICENSE-MIT.txt), except for the exclusions below.
- `NativeMethods.cs`, `WindowsTaskbarBackend.cs`, `Wallpaper/DesktopLayer.cs`
  and `Wallpaper/WallpaperNativeMethods.cs`, relative to
  `src/TransparentTaskbar/`, are designated GPL-3.0-only.
  They preserve upstream-derived taskbar/desktop integration and are excluded
  from the MIT grant. The full terms are in [LICENSE-GPL-3.0.md](LICENSE-GPL-3.0.md).
- Third-party source, binaries and notices under `third_party/` retain their own
  licenses. The original `aurora-flow` and `ocean-drift` sample videos, posters
  and generation script use [CC0-1.0](assets/samples/LICENSE-CC0.txt).

The combined Glasscape application, including its GPL integration and required
ExplorerTAP component, is distributed subject to GPL version 3 conditions,
including applicable notices and corresponding source/build information.
MIT-covered original portions can be reused separately under MIT; that grant
does not relicense GPL portions or third-party components.

See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for precise file scope,
upstream attribution, provenance limits and distribution information.

Support and license questions: [bomtobe.sw@gmail.com](mailto:bomtobe.sw@gmail.com).
