# TranslucentTB native dependency

This directory redistributes the original, unmodified x64 ExplorerTAP.dll from
TranslucentTB **2026.2**, licensed under the GNU General Public License version 3.
The complete upstream GPL text is in LICENSE.md.

Upstream project: https://github.com/TranslucentTB/TranslucentTB
Release: https://github.com/TranslucentTB/TranslucentTB/releases/tag/2026.2
Release tag commit: d4636e4

Credits follow the upstream README: @ethanhs, @sylveon (Charles Milette),
@MrAksel, @denosawr, and all TranslucentTB contributors:
https://github.com/TranslucentTB/TranslucentTB/graphs/contributors
The upstream icon credit is @dAKirby309; no upstream icon is included here.
ExplorerTAP uses Microsoft Detours, Windows Implementation Library (WIL), and
C++/WinRT through the build dependencies declared in the upstream source.

## Files and SHA256 provenance

| Artifact | SHA256 |
| --- | --- |
| TranslucentTB-portable-x64.zip (downloaded during restore) | 0dbe8e0255c20e131cde536dcd0ae490d45989a7d360e26d0150dff1922ac420 |
| win-x64/ExplorerTAP.dll | c48f1a06168acd7839269dde0d15ccf5d09c702512b94dd6ba9a05df1b153dc2 |
| TranslucentTB-2026.2-source.zip | 73ac414e17710de5765f2305bbbc1aaa4d88628c49d9b47a3159795d05f2f257 |
| LICENSE.md | 982b4aa12f1eeb169feae72ae5ada1443e608343f29ccee0ce5ee58f21b1a851 |

Release archive: https://github.com/TranslucentTB/TranslucentTB/releases/download/2026.2/TranslucentTB-portable-x64.zip
Source archive: https://github.com/TranslucentTB/TranslucentTB/archive/refs/tags/2026.2.zip
License: https://raw.githubusercontent.com/TranslucentTB/TranslucentTB/2026.2/LICENSE.md

The source archive is distributed beside the DLL. It contains the upstream
ExplorerTAP source, build projects, dependency manifests, and CONTRIBUTING.md
with build instructions. The DLL has not been rebuilt or modified.

## Native dependency inspection

PE machine: AMD64 (0x8664). Exported functions: DllGetClassObject (ordinal 1)
and InjectExplorerTAP (ordinal 2). There are no delay imports.

Imports: ole32.dll, OLEAUT32.dll, RPCRT4.dll, USER32.dll, KERNEL32.dll,
api-ms-win-core-winrt-error-l1-1-1.dll, api-ms-win-core-winrt-l1-1-0.dll,
api-ms-win-core-winrt-error-l1-1-0.dll. These are Windows components.
The source also loads Windows.UI.Xaml.dll from System32. The release project
uses a static CRT; no Visual C++ redistributable DLL or other portable-release
DLL is required for ExplorerTAP.dll.

Restore this exact dependency and its accompanying license/source with
powershell -ExecutionPolicy Bypass -File scripts/fetch-native.ps1.
