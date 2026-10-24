# X# 3.0.1 runtime, built from source

The NuGet X# runtime stops at 2.24. The X# 3 installers on xsharp.eu need a registered account, and the free public edition leaves out the VO dialect. Neither is needed for what the rewrite uses: `XSharp.Core`, `XSharp.RT`, `XSharp.Rdd` and `XSharp.MacroCompiler`, called from C#. This folder builds those four DLLs for .NET 8 straight from the Apache-2.0 source tag `v3.0.1.0`.

- `build-runtime.sh`: the build, with no Visual Studio or account needed. See the header for usage. Assembled from the exact commands used on 2026-10-24; the script as a whole has not been re-run from an empty folder.
- `patches/0001-win32-filestream-position.patch`: fixes shared-mode opens on .NET 6+ (verified). See the spike report, section 7.
- `patches/0002-cdx-tag-refresh-root-page.patch`: makes a tag pick up a root page moved by another process. A real stale-state bug, but **not enough** to stop the multi-writer index corruption.

Both patches are local and unpublished. Reporting them to the X# project means posting publicly, so that waits for an explicit go-ahead.

Build output and the working folder (about 1.9 GB) live in `%USERPROFILE%\xsb`, outside this repo. Delete that folder to remove everything, including the private .NET 9 SDK.
