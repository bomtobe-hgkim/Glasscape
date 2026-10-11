# Microsoft Store package

Create the unsigned, self-contained x64 Store upload bundle from the repository root:

```powershell
./scripts/package-msix.ps1 `
  -IdentityName 'BOMTOBE.Glasscape' `
  -Publisher 'CN=20298E27-3AA6-4580-9D83-5FC1AF024E55' `
  -PublisherDisplayName 'BOMTOBE' `
  -StoreId '9NNNHKLJW3J1' `
  -Version '2.1.0.0'
./tests/Glasscape.Packaging.Tests/VerifyBundle.ps1
```

These identity values were observed on this product's Partner Center identity page. The script requires explicit inputs so a fork cannot silently claim this product's identity. It requires .NET 10 and the x64 Windows SDK MakeAppx/MakePri tools. It writes only below `dist/store` by default. `-SkipBuild` repackages the existing MSIX staging directory; use a normal run for a release after source changes.

The manifest targets Windows desktop 10.0.19041.0 and later, declares English and Korean, uses normal-user `packagedClassicApp` execution with `runFullTrust`, and registers `GlasscapeStartup` disabled by default. Startup passes `--background`. Application code uses `Windows.ApplicationModel.StartupTask` asynchronously when package identity is present. Portable execution retains its quoted HKCU Run command. The packaged UI hides the portable self-copy installer.

The package contains .NET 10 Windows Desktop, ExplorerTAP and the pinned mpv DLLs. Publishing uses individual runtime files so it does not extract binaries into the read-only installation directory. Settings and logs use LocalAppData. Only the Aurora Flow and Ocean Drift MP4/poster pairs are included. Package icons are deterministic size conversions of the existing Glasscape artwork.

The script preserves the application license, native dependency licenses/notices, .NET runtime licenses, SDK/CsWinRT license texts, and available source archives and build provenance. `third_party/mpv/NOTICE.md` documents the remaining uncertainty about exact sources for some unpinned upstream binary dependencies; including a date snapshot is not a claim that it is their exact corresponding source.

MakeAppx runs schema and semantic validation without `/nv`. `VerifyBundle.ps1` opens the delivered bundle and nested application/source packages to verify identity, startup, architecture, languages, runtime payload, license/source inclusion and excluded-media absence. `dist/store/build-info.json` records the result and SHA-256.

Store upload packages do not need a developer signature; Microsoft signs them during submission. This script never changes certificate trust or Windows security settings. Local installation and full WACK execution require an installable package. The Windows 11 unsigned testing mechanism changes the publisher identity and requires administrator/all-users installation for executable content; the Store bundle intentionally retains its observed publisher. See [Microsoft signing guidance](https://learn.microsoft.com/en-us/windows/msix/package/sign-msix-package-guide) and [unsigned package testing](https://learn.microsoft.com/en-us/windows/msix/package/unsigned-package).

License provenance: `Licenses/CsWinRT-LICENSE.txt` was retrieved from the [Microsoft CsWinRT repository](https://github.com/microsoft/CsWinRT/blob/master/LICENSE). `Licenses/WindowsSDK-NET-Ref-LICENSE.rtf` was retrieved from the license URL in the restored `Microsoft.Windows.SDK.NET.Ref` package, [aka.ms/WinSDKLicenseURL](https://aka.ms/WinSDKLicenseURL). Runtime license texts are copied from the exact restored runtime packs selected by the application's runtime configuration.
