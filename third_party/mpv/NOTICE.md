# mpv 0.41.0 Windows x64 runtime provenance

This directory contains the unchanged Windows x64 playback payload supplied by the mpv project. The application launches it as a separate process; the fetch script does not register file associations, modify the registry, or start an interactive player window.

## Pinned release and runtime

- First-party release: https://github.com/mpv-player/mpv/releases/tag/v0.41.0
- Download: https://github.com/mpv-player/mpv/releases/download/v0.41.0/mpv-v0.41.0-x86_64-w64-mingw32.zip
- Download SHA-256: `a49811c0752c108b8260636f9c6f6fcb97406641c98b30f1e7b500dfb20177de`
- The release asset is an outer ZIP containing one nested ZIP, `mpv-git-2025-12-21-41f6a64-x86_64.zip`.
- Nested ZIP SHA-256: `443afe07e61e3f742743349d01d8dac95f0c5390a766a9d3d5b1b5f05911bae5`
- Runtime directory: `win-x64/`. Keep `mpv.exe` and **all DLLs in that directory together**. `mpv.com` is the upstream console wrapper. Upstream register/unregister batch files are preserved but are not executed.
- `runtime.sha256` records each unchanged runtime file's SHA-256.
- Refetch: `powershell -File scripts/fetch-mpv.ps1`. Downloads are pinned and checked before extraction; extraction rejects paths outside the intended directory. It is safe to rerun and does not recursively delete any directory.

Actual console-wrapper `--version` output is preserved as `provenance/runtime-version.txt`:

```text
mpv v0.41.0-dev-g41f6a6450
built on Dec 21 2025 19:19:52
libplacebo version: v7.358.0
FFmpeg version: git-2025-12-21-1fcb201
libavcodec 62.23.102; libavdevice 62.2.100; libavfilter 11.10.101
libavformat 62.8.100; libavutil 60.20.100
libswresample 6.2.100; libswscale 9.3.100
```

The first-party release says these CI binary assets omit some optional features, including encoding. Samples are generated with a separate FFmpeg development tool, not with this player.

## Sources and build instructions

- Exact mpv tag source: https://github.com/mpv-player/mpv/archive/refs/tags/v0.41.0.zip
- Included as `mpv-v0.41.0-source.zip`, SHA-256 `7a37ded4ae1655528525c008736e3abd2f0f360e94140107d62185d056a20c46`.
- Release commit: `41f6a6450`, as reported by the binary.
- First-party CI build run: https://github.com/mpv-player/mpv/actions/runs/20414542745
- Exact tag CI workflow: https://github.com/mpv-player/mpv/blob/v0.41.0/.github/workflows/build.yml
- Exact tag build recipe: https://github.com/mpv-player/mpv/blob/v0.41.0/ci/build-mingw64.sh
- Original copies of the workflow, `build-mingw64.sh`, and `build-common.sh` are preserved without edits in `provenance/` and also exist in the source ZIP.
- Exact FFmpeg commit reported by the DLLs: `1fcb201ab10cbc98d931e340da6ace292e82b1e1`; source https://github.com/FFmpeg/FFmpeg/archive/1fcb201.zip is included as `source/FFmpeg-1fcb201.zip`, SHA-256 `2358b01dd8ec76fa8fe2a07ce8c96b01a471ef1c224221ee77afe545687fb9da`.
- libiconv 1.18 source named in the exact CI recipe: https://ftp.gnu.org/gnu/libiconv/libiconv-1.18.tar.gz, included as `source/libiconv-1.18.tar.gz`, SHA-256 `3b08f5f4f9b4eb82f151a7040bfd6fe6c6fb922efe4b1659c66ea933276965e8`.

The CI recipe shallow-clones several dependencies without commit pins and can reuse its dependency cache. The release ZIP contains no dependency source/version manifest or license files. Accordingly, this directory **does not claim to be a proven complete corresponding-source bundle for every DLL**. The reported libplacebo `v7.358.0` has no published tag. `source/libplacebo-build-date-snapshot.zip` is the first-party GitHub mirror's last commit before the binary build time (`47587a2d47299f7261a01b77e989b74f80a99769`); it is a documented date snapshot, **not proof of the exact binary dependency commit**. Its archive SHA-256 is `7f27922f607f108b80339a9387d3a92dd2d8723965d24ac777bea4544240df89`. The retrieved commit metadata is preserved in `provenance/libplacebo-build-date-snapshot.json`.

## Preserved license texts and component origins

The mpv source's exact `Copyright`, `LICENSE.GPL`, and `LICENSE.LGPL` are preserved in `licenses/`. mpv's Copyright file describes GPL version 2 or later as the default, and LGPL builds only when GPL-only source is excluded. The exact CI recipe enables GPL in FFmpeg. Consult each preserved upstream notice and source file for its terms.

Dependency license texts are preserved in `licenses/`; `provenance/license-sources.json` records retrieval URLs, source-archive entries and SHA-256. Texts taken from tagged versions are labeled by their URLs. Texts taken from a project's current branch document its license and are **not assertions that the released binary used that current source commit**.

| Runtime / build component | Upstream origin | Version evidence |
| --- | --- | --- |
| FFmpeg / av*.dll / sw*.dll | https://github.com/FFmpeg/FFmpeg | Exact runtime commit above |
| libplacebo | https://code.videolan.org/videolan/libplacebo and its https://github.com/haasn/libplacebo mirror | Runtime reports v7.358.0; exact commit not recorded |
| libiconv | https://www.gnu.org/software/libiconv/ | Recipe pins 1.18 |
| zlib | https://zlib.net/ | Recipe pins 1.3.1 |
| FreeType | https://freetype.org/ | Recipe pins 2.14.1; preserved FTL and GPL alternatives |
| FriBidi | https://github.com/fribidi/fribidi | Recipe pins 1.0.16 |
| HarfBuzz | https://github.com/harfbuzz/harfbuzz | Recipe pins 12.2.0 |
| libass | https://github.com/libass/libass | Unpinned clone in recipe |
| dav1d | https://code.videolan.org/videolan/dav1d / https://github.com/videolan/dav1d | Unpinned clone in recipe |
| Little CMS | https://github.com/mm2/Little-CMS | Unpinned clone in recipe |
| shaderc | https://github.com/google/shaderc | Unpinned clone in recipe |
| SPIRV-Cross | https://github.com/KhronosGroup/SPIRV-Cross | Unpinned clone in recipe |
| Vulkan loader | https://github.com/KhronosGroup/Vulkan-Loader | Unpinned clone in recipe |
| LuaJIT (static) | https://github.com/LuaJIT/LuaJIT | Unpinned clone in recipe |
| MuJS (static) | https://github.com/ArtifexSoftware/mujs | Meson wrap installed by workflow |
| GCC runtime / libstdc++ / libgcc / libssp | https://gcc.gnu.org/ | Compiler runtime copied by recipe; exact compiler version not in release ZIP |
| MinGW / winpthreads | https://www.mingw-w64.org/ | Toolchain runtime copied by recipe; exact version not in release ZIP |

## Sample-generation tools, not player runtime

`generation-tools/` contains FFmpeg 8.1.2 from Gyan's Windows builds, linked by https://ffmpeg.org/download.html. It is used only to create and verify this project's original sample MP4s. Its package SHA-256 `db580001caa24ac104c8cb856cd113a87b0a443f7bdf47d8c12b1d740584a2ec` was checked against the builder's published `.sha256`. Original `README.txt` and `LICENSE` are preserved. Do not add this directory or its archive to the application runtime solely for playback; only `win-x64/` is needed.

Generated sample footage and posters have their own CC0-1.0 notice in `assets/samples/README.md`; player/tool licenses do not describe those original assets.
