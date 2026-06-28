# Pixel Snapper

GUI C# port of [Sprite Fusion Pixel Snapper](https://github.com/Hugo-Dz/spritefusion-pixel-snapper) — snaps messy pixel art onto a clean grid.

The original tool is a Rust script by Hugo Duprez. This repository reimplements the algorithm in C# (.NET 8) with a WPF desktop interface.

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (to build)
- Windows 10+ (WPF)
- [7-Zip](https://www.7-zip.org/) (optional, only for `-SevenZip` archives)

## Project structure

| Path | Description |
|------|-------------|
| `src/PixelSnapper.App` | WPF GUI application |
| `src/PixelSnapper.Core` | Image processing pipeline ([ImageSharp](https://github.com/SixLabors/ImageSharp)) |
| `src/PixelSnapper.Bench` | Simple dev benchmark harness |
| `run.ps1` | Build, publish, and package script |
| `PixelSnapper.sln` | Solution file |

## Build & run

```powershell
.\run.ps1
```

Builds `dist\PixelSnapper.exe` (self-contained, single file) and launches the app.

Skip the build when the EXE already exists:

```powershell
.\run.ps1 -SkipBuild
```

### Distribution archives

```powershell
.\run.ps1 -Zip                  # ~60 MB ZIP, no .NET install required
.\run.ps1 -Zip -Small           # ~2 MB ZIP, requires .NET 8 Desktop Runtime
.\run.ps1 -SevenZip             # ~60 MB 7z, smaller than ZIP
.\run.ps1 -SevenZip -Small      # ~0.6 MB 7z
.\run.ps1 -SevenZip -Small -SkipBuild
```

The `-Small` variant is a framework-dependent Release build (~2 MB EXE, no native self-extract). Recipients need the [.NET 8 Desktop Runtime x64](https://dotnet.microsoft.com/download/dotnet/8.0).

| Variant | Archive | Recipient requirements |
|---------|---------|------------------------|
| `-Zip` | `PixelSnapper-win-x64.zip` | None (self-contained) |
| `-Zip -Small` | `PixelSnapper-win-x64-small.zip` | .NET 8 Desktop Runtime |
| `-SevenZip` | `PixelSnapper-win-x64.7z` | None (self-contained) |
| `-SevenZip -Small` | `PixelSnapper-win-x64-small.7z` | .NET 8 Desktop Runtime |

Small archives include `README.txt` with runtime install instructions.

You can also run `dist\PixelSnapper.exe` or `dist-small\PixelSnapper.exe` directly after building.

## Usage

1. Drag and drop a PNG/JPG image, or click **Browse file…**
2. Adjust **k_colors** (default 16) and optional **pixel_size** (empty = auto-detect)
3. Preview input and result side by side
4. Click **Save PNG** to export the snapped image

## Development

```powershell
dotnet build PixelSnapper.sln
dotnet run --project src/PixelSnapper.App/PixelSnapper.App.csproj
dotnet run --project src/PixelSnapper.Bench/PixelSnapper.Bench.csproj -c Release
```

## Upstream & license

- Original algorithm and Rust implementation: [Hugo-Dz/spritefusion-pixel-snapper](https://github.com/Hugo-Dz/spritefusion-pixel-snapper) by Hugo Duprez
- This project: MIT — see [LICENSE](LICENSE)

## By
Ported by zbigniewcebula