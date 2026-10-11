# Glasscape source and third-party notices

Glasscape uses mixed source licenses. Copyright (c) 2026 BOMTOBE applies to
project-authored contributions, not to upstream authors' work. The MIT grant in
[LICENSE-MIT.txt](LICENSE-MIT.txt) covers independently authored Glasscape code,
tests, build scripts and documentation, subject to the exclusions below.

## App source scope

The following complete files are designated **GPL-3.0-only** for this release and
are excluded from the MIT grant. Their Glasscape changes retain GPL terms; see
[LICENSE-GPL-3.0.md](LICENSE-GPL-3.0.md).
This selects GPL version 3 for this release and does not remove any broader
rights that an upstream author may have granted over their own work.

| File | Provenance and scope |
| --- | --- |
| `src/Glasscape/NativeMethods.cs` | C# interop declaration translation of the TranslucentTB 2026.2 ExplorerTAP API, including its COM interface GUID and method order. Also contains Windows API declarations. |
| `src/Glasscape/WindowsTaskbarBackend.cs` | Taskbar detection/classification and restoration adapted from TranslucentTB 2026.2, with Glasscape-specific .NET module enumeration, lifecycle and error handling. |
| `src/Glasscape/Wallpaper/DesktopLayer.cs` | Desktop layer discovery and parenting adapted from Lively Wallpaper's Progman/WorkerW implementation, with Glasscape-specific validation, timeout, host and positioning changes. |
| `src/Glasscape/Wallpaper/WallpaperNativeMethods.cs` | Companion interop/helper file conservatively included in the GPL desktop integration scope. Windows API declarations and constants are identified as interoperability information; this designation does not claim ownership of Windows APIs. |

Other BOMTOBE-authored app source files, tests, build scripts and documentation
use MIT unless a file or its directory carries another notice. The declarations
and short calls needed for interoperability are recorded here without asserting
that each declaration or constant is independently copyrightable. This scope is
not a claim that all app code was independently developed without references.
Third-party source, licenses and provenance under `third_party/` are excluded
from the MIT grant and keep their original terms.

## TranslucentTB and ExplorerTAP

Upstream: [TranslucentTB 2026.2](https://github.com/TranslucentTB/TranslucentTB/tree/2026.2),
release commit `d4636e4`. Upstream declares GPLv3 in its
[README](https://github.com/TranslucentTB/TranslucentTB/blob/2026.2/README.md#license).
Credits include @ethanhs, Charles Milette (@sylveon),
@MrAksel, @denosawr and the other
[TranslucentTB contributors](https://github.com/TranslucentTB/TranslucentTB/graphs/contributors).

Relevant source:

- [ITaskbarAppearanceService.idl](https://github.com/TranslucentTB/TranslucentTB/blob/2026.2/ExplorerTAP/ITaskbarAppearanceService.idl)
- [api.hpp](https://github.com/TranslucentTB/TranslucentTB/blob/2026.2/ExplorerTAP/api.hpp)
- [taskbarattributeworker.cpp](https://github.com/TranslucentTB/TranslucentTB/blob/2026.2/TranslucentTB/taskbar/taskbarattributeworker.cpp), particularly taskbar classification and default appearance restoration.

The unchanged ExplorerTAP.dll remains GPL-covered. Its exact source archive,
original license, hashes and dependency credits are preserved in
`third_party/TranslucentTB/`. See its [NOTICE.md](third_party/TranslucentTB/NOTICE.md).
No upstream icon is included. The reused TranslucentTB mutex value prevents
conflicting simultaneous clients; that constant does not change `Program.cs`'s
MIT designation.

## Lively Wallpaper desktop integration

Upstream: [Lively Wallpaper](https://github.com/lively-community/lively), by
Dani John (@rocksdanister) and
[Lively contributors](https://github.com/lively-community/lively/graphs/contributors).
The referenced revision is `25a0a5f4ed1988ff42117d3a17feb321062237fe`:

- [WinDesktopCore.cs](https://github.com/lively-community/lively/blob/25a0a5f4ed1988ff42117d3a17feb321062237fe/src/Lively/Lively/Core/WinDesktopCore.cs), including `SetupDesktopLayer` and raised desktop handling.
- [Upstream GPLv3 license](https://github.com/lively-community/lively/blob/25a0a5f4ed1988ff42117d3a17feb321062237fe/LICENSE).

The two desktop integration files listed above conservatively retain GPL scope.
Glasscape uses its own mpv process/session management and does not redistribute
a Lively executable, UI or media library. The GPL designation preserves the
upstream-derived integration rather than granting MIT rights over it.

## mpv and its dependencies

Glasscape launches the unchanged mpv player as a separate process. The player
and dependency sources retain their own GPL, LGPL and other upstream terms;
none are relicensed under MIT. See
[third_party/mpv/NOTICE.md](third_party/mpv/NOTICE.md) and its preserved
`licenses/`, `source/` and `provenance/` material for the exact known versions,
sources and documented provenance limits. This notice does not claim a fully
verified corresponding-source match for every DLL in the upstream binary.

## Samples and distributed application

The project-created `aurora-flow` and `ocean-drift` videos, posters and generation
script use [CC0-1.0](assets/samples/LICENSE-CC0.txt), as described in
[assets/samples/README.md](assets/samples/README.md). The private `rain`, `rain2`
and `town` source media are excluded from public and release archives; no rights
to them are granted by the MIT or GPL source notices.

The distributed Glasscape executable combines the designated GPL integration
with the other app code and relies on GPL-covered ExplorerTAP. Distribution of
that combined application follows **GPL version 3 conditions**, including
preservation of applicable notices and provision of corresponding source and
build information. It is **not an MIT-only executable**. MIT-covered original
portions remain separately reusable under MIT; their permissive grant does not
relicense any GPL portions or dependencies. Portable and Store distributions
must retain the license texts and source/provenance materials.

Support and source/license questions: [bomtobe.sw@gmail.com](mailto:bomtobe.sw@gmail.com).
