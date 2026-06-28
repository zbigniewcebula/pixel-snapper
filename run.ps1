# Pixel Snapper — build + run
param(
	[switch]$SkipBuild,
	[switch]$Zip,
	[switch]$SevenZip,
	[switch]$Small
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

function Get-SevenZipPath {
	$candidates = @(
		(Join-Path ${env:ProgramFiles} "7-Zip\7z.exe"),
		(Join-Path ${env:ProgramFiles(x86)} "7-Zip\7z.exe")
	)
	foreach ($path in $candidates) {
		if (Test-Path $path) { return $path }
	}
	$cmd = Get-Command 7z -ErrorAction SilentlyContinue
	if ($cmd) { return $cmd.Source }
	return $null
}

$distSubdir = if ($Small) { "dist-small" } else { "dist" }
$exe = Join-Path $PSScriptRoot "$distSubdir\PixelSnapper.exe"

if (-not $SkipBuild) {
	if ($Small) {
		Write-Host "Building Pixel Snapper (small, framework-dependent)..." -ForegroundColor Cyan
	} else {
		Write-Host "Building Pixel Snapper EXE..." -ForegroundColor Cyan
	}

	$publishDir = Join-Path $env:TEMP "pixelsnapper_build_$(Get-Random)"
	$publishArgs = @(
		"publish", "src/PixelSnapper.App/PixelSnapper.App.csproj",
		"-c", "Release",
		"-r", "win-x64",
		"-p:PublishSingleFile=true",
		"-o", $publishDir
	)

	if ($Small) {
		$publishArgs += @(
			"--self-contained", "false",
			"-p:EnableCompressionInSingleFile=false",
			"-p:SmallPublish=true"
		)
	} else {
		$publishArgs += @(
			"--self-contained", "true",
			"-p:IncludeNativeLibrariesForSelfExtract=true"
		)
	}

	& dotnet @publishArgs

	if ($LASTEXITCODE -ne 0) {
		Write-Error "Build failed (exit $LASTEXITCODE)"
	}

	$src = Join-Path $publishDir "PixelSnapper.exe"
	$distDir = Join-Path $PSScriptRoot $distSubdir
	New-Item -ItemType Directory -Force -Path $distDir | Out-Null

	if (-not (Test-Path $src)) {
		Write-Error "Expected $src not found"
	}

	Copy-Item -Force $src $exe
	Remove-Item -Recurse -Force $publishDir -ErrorAction SilentlyContinue

	$sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 2)
	Write-Host "Built: $distSubdir\PixelSnapper.exe ($sizeMb MB)" -ForegroundColor Green
}

if (-not (Test-Path $exe)) {
	Write-Error "Missing $distSubdir\PixelSnapper.exe. Run without -SkipBuild."
}

if ($Zip -or $SevenZip) {
	if ($SevenZip) {
		$sevenZipExe = Get-SevenZipPath
		if (-not $sevenZipExe) {
			Write-Error "7-Zip not found. Install from https://www.7-zip.org/ or add 7z.exe to PATH."
		}
		$archiveName = if ($Small) { "PixelSnapper-win-x64-small.7z" } else { "PixelSnapper-win-x64.7z" }
	} else {
		$archiveName = if ($Small) { "PixelSnapper-win-x64-small.zip" } else { "PixelSnapper-win-x64.zip" }
	}
	$archivePath = Join-Path $PSScriptRoot $archiveName
	$stagingDir = Join-Path $PSScriptRoot "out\zip-staging"

	if (Test-Path $stagingDir) {
		Remove-Item -Recurse -Force $stagingDir
	}
	New-Item -ItemType Directory -Force -Path $stagingDir | Out-Null

	Copy-Item -Force $exe (Join-Path $stagingDir "PixelSnapper.exe")

	if ($Small) {
		@"
Pixel Snapper — lightweight build (~2 MB)

Requires .NET 8 Desktop Runtime (x64), one-time install:
https://dotnet.microsoft.com/download/dotnet/8.0
-> download ".NET Desktop Runtime 8.x" for Windows x64

After installing the runtime, run PixelSnapper.exe.
"@ | Set-Content -Path (Join-Path $stagingDir "README.txt") -Encoding UTF8
	}

	if (Test-Path $archivePath) {
		Remove-Item -Force $archivePath
	}

	if ($SevenZip) {
		Push-Location $stagingDir
		try {
			& $sevenZipExe a -t7z -mx=9 -bb0 $archivePath *
			if ($LASTEXITCODE -ne 0) {
				Write-Error "7-Zip failed (exit $LASTEXITCODE)"
			}
		} finally {
			Pop-Location
		}
	} else {
		Compress-Archive -Path (Join-Path $stagingDir "*") -DestinationPath $archivePath -CompressionLevel Optimal
	}
	Remove-Item -Recurse -Force $stagingDir

	$sizeMb = [math]::Round((Get-Item $archivePath).Length / 1MB, 2)
	$via = if ($SevenZip) { "7-Zip" } else { "ZIP" }
	Write-Host "Done ($via): $archiveName ($sizeMb MB)" -ForegroundColor Green
	if ($Small) {
		Write-Host "Note: recipients need .NET 8 Desktop Runtime (see README.txt in the archive)." -ForegroundColor Yellow
	}
	return
}

Write-Host "Launching: $exe" -ForegroundColor Green
& $exe

