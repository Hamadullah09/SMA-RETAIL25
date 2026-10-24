#!/usr/bin/env bash
# Builds the X# 3.0.1 runtime (XSharp.Core, XSharp.RT, XSharp.Rdd, XSharp.MacroCompiler for .NET 8) from the
# public source on GitHub, without Visual Studio and without an xsharp.eu account. Optionally applies the
# local patches in ./patches.
#
# Usage (Git Bash):  spikes/xsharp3/build-runtime.sh [--patched] [Release|Debug]
# Output:            $XSB/runtime-3.0.1[-patched][-debug]/  (4 DLLs)
#
# Needs ~2 GB of disk and network access (GitHub, dot.net, NuGet). Takes about 10 minutes on first run.
# Everything goes under $XSB (default C:\Users\<you>\xsb); nothing is installed system-wide.
# A short base path is required: the Roslyn sources and the .NET SDK exceed Windows' 260-character path limit
# under deeper folders.
set -euo pipefail

PATCHED=0; CONFIG=Release
for a in "$@"; do case "$a" in --patched) PATCHED=1;; Release|Debug) CONFIG=$a;; esac; done
HERE="$(cd "$(dirname "$0")" && pwd)"
XSB="${XSB:-$HOME/xsb}"
SRC="$XSB/src"
WIN() { cygpath -w "$1"; }

mkdir -p "$XSB"

# 1. Source: the v3.0.1.0 tag (June 2026), with long paths enabled for this clone only.
if [ ! -d "$SRC/.git" ]; then
  git -c core.longpaths=true -c advice.detachedHead=false clone --depth 1 --branch v3.0.1.0 \
      https://github.com/X-Sharp/XSharpPublic.git "$SRC"
  git -C "$SRC" config core.longpaths true
fi

# 2. .NET SDK 9.0.310, which the repo's global.json pins. Installed into $XSB only (Microsoft-signed script).
if [ ! -x "$XSB/dotnet9/dotnet.exe" ]; then
  curl -sSL -o "$XSB/dotnet-install.ps1" https://dot.net/v1/dotnet-install.ps1
  powershell -NoProfile -ExecutionPolicy Bypass -Command \
    "if ((Get-AuthenticodeSignature '$(WIN "$XSB/dotnet-install.ps1")').Status -ne 'Valid') { throw 'bad signature' }; \
     & '$(WIN "$XSB/dotnet-install.ps1")' -Version 9.0.310 -InstallDir '$(WIN "$XSB/dotnet9")' -NoPath"
fi

# 3. Stand-in for sn.exe. The build re-signs assemblies with the strong-name tool from the .NET Framework SDK;
#    .NET (Core) does not verify strong-name signatures, so a no-op is enough for these test builds.
if [ ! -x "$XSB/tools/sn.exe" ]; then
  mkdir -p "$XSB/snstub"
  cat > "$XSB/snstub/snstub.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><AssemblyName>sn</AssemblyName></PropertyGroup>
</Project>
EOF
  echo 'System.Console.WriteLine("sn stub: skipped " + string.Join(" ", args));' > "$XSB/snstub/Program.cs"
  DOTNET_ROOT="$(WIN "$XSB/dotnet9")" "$XSB/dotnet9/dotnet.exe" build "$XSB/snstub" -c Release -o "$XSB/tools" -v q -nologo
fi

export DOTNET_ROOT="$(WIN "$XSB/dotnet9")" PATH="$XSB/tools:$XSB/dotnet9:$PATH" DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
SD="-p:SolutionDir=$(WIN "$SRC/src")\\"
cd "$SRC/src"

# 4. Compiler: X#'s bootstrap C# compiler, then xsc (with an app host so the build task can start it), then the MSBuild task.
dotnet build Compiler/src/Tools/csc/csc.csproj -c Release -v m -nologo "$SD"
dotnet build Compiler/src/Compiler/xsc/xsc.csproj -c Release -f net9.0 -v m -nologo "$SD" -p:UseAppHost=true
dotnet build Compiler/src/Compiler/XSharpBuildTask/XSharp.Build.csproj -c Release -f net9.0 -v m -nologo "$SD"

# 5. MSBuild support folder: X# props/targets plus the task assembly.
mkdir -p "$XSB/msbuild"
cp -f Compiler/src/Compiler/XSharpBuildTask/*.props Compiler/src/Compiler/XSharpBuildTask/*.Props \
      Compiler/src/Compiler/XSharpBuildTask/*.targets Compiler/src/Compiler/XSharpBuildTask/rc*.* "$XSB/msbuild/" 2>/dev/null || true
cp -f "$SRC/Artifacts/bin/XSharp.Build/Release/net9.0/"*.dll "$XSB/msbuild/"

# 6. Optional local patches (see README.md).
if [ "$PATCHED" = 1 ]; then
  for p in "$HERE"/patches/*.patch; do
    git -C "$SRC" apply --check "$p" 2>/dev/null && git -C "$SRC" apply "$p" || echo "already applied: $(basename "$p")"
  done
else
  git -C "$SRC" checkout -- src/Runtime
fi

# 7. Runtime for .NET 8.
export XSHARPDEV="$(WIN "$SRC/Artifacts/bin/xsc/Release/net9.0")"
MSB="-p:XSharpMsBuildDir=$(WIN "$XSB/msbuild")\\"
BEFORE="-p:CustomBeforeMicrosoftCommonProps=$(WIN "$XSB/msbuild/XSharp.BeforeCommon.Props")"
for p in Runtime/XSharp.Rdd/XSharp.RDD.Next.xsproj Runtime/XSharp.RT/XSharp.RT.Next.xsproj Runtime/MacroCompiler/XSharp.MacroCompiler.Next.csproj; do
  dotnet build "$p" -c "$CONFIG" -v m -nologo "$MSB" "$BEFORE"
done

# 8. Collect.
LABEL="runtime-3.0.1"; [ "$PATCHED" = 1 ] && LABEL="$LABEL-patched"; [ "$CONFIG" = Debug ] && LABEL="$LABEL-debug"
OUT="$XSB/$LABEL"; rm -rf "$OUT"; mkdir -p "$OUT"
cp "$SRC/Artifacts/$CONFIG/net8.0/XSharp."{Core,Rdd,RT}.dll "$OUT/"
cp "$SRC/Artifacts/$CONFIG/net8.0-windows7.0/XSharp.MacroCompiler.dll" "$OUT/"
echo "Runtime DLLs in $(WIN "$OUT")"
echo "Build the spike against them:  dotnet build spikes/RddSpike -p:XSharpRuntimeDir=$(WIN "$OUT")"
