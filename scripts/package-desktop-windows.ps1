param(
    [Parameter(Mandatory = $true)][string] $PublishDirectory,
    [Parameter(Mandatory = $true)][string] $OutputDirectory,
    [Parameter(Mandatory = $true)][string] $Version,
    [string] $Status = "unsigned"
)

$ErrorActionPreference = "Stop"

if ($Version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') {
    throw "Version must have the form 1.2.3."
}

$executable = Join-Path $PublishDirectory "Drover.Native.exe"
if (-not (Test-Path $executable -PathType Leaf)) {
    throw "The publish directory does not contain Drover.Native.exe."
}

$kitRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
$makeAppx = Get-ChildItem $kitRoot -Filter makeappx.exe -Recurse |
    Where-Object { $_.FullName -match '\\x64\\makeappx\.exe$' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1
if ($null -eq $makeAppx) {
    throw "The Windows SDK makeappx tool was not found."
}

$temporary = Join-Path ([System.IO.Path]::GetTempPath()) "drover-msix-$([guid]::NewGuid())"
$stage = Join-Path $temporary "stage"
New-Item $stage -ItemType Directory | Out-Null
try {
    Copy-Item (Join-Path $PublishDirectory "*") $stage -Recurse
    Add-Type -AssemblyName System.Drawing
    $assetDirectory = Join-Path $stage "Assets"
    New-Item $assetDirectory -ItemType Directory -Force | Out-Null
    $sourceImage = [System.Drawing.Image]::FromFile((Resolve-Path "src/Shared/Web/icon-512.png"))
    try {
        foreach ($asset in @(
            @{ Name = "StoreLogo.png"; Size = 50 },
            @{ Name = "Square44x44Logo.png"; Size = 44 },
            @{ Name = "Square150x150Logo.png"; Size = 150 }
        )) {
            $bitmap = New-Object System.Drawing.Bitmap($asset.Size, $asset.Size)
            try {
                $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
                try {
                    $graphics.DrawImage($sourceImage, 0, 0, $asset.Size, $asset.Size)
                }
                finally {
                    $graphics.Dispose()
                }
                $bitmap.Save((Join-Path $assetDirectory $asset.Name), [System.Drawing.Imaging.ImageFormat]::Png)
            }
            finally {
                $bitmap.Dispose()
            }
        }
    }
    finally {
        $sourceImage.Dispose()
    }

    $manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
         xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
         xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
         IgnorableNamespaces="uap rescap">
  <Identity Name="ForrestTechnologies.Drover" Publisher="CN=Forrest Technologies" Version="$Version.0" ProcessorArchitecture="x64" />
  <Properties>
    <DisplayName>Drover</DisplayName>
    <PublisherDisplayName>Forrest Technologies</PublisherDisplayName>
    <Logo>Assets\StoreLogo.png</Logo>
  </Properties>
  <Dependencies>
    <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.17763.0" MaxVersionTested="10.0.26100.0" />
  </Dependencies>
  <Resources><Resource Language="en-us" /></Resources>
  <Applications>
    <Application Id="Drover" Executable="Drover.Native.exe" EntryPoint="Windows.FullTrustApplication">
      <uap:VisualElements DisplayName="Drover" Description="Connect to a self-hosted Drover server"
          BackgroundColor="transparent" Square150x150Logo="Assets\Square150x150Logo.png" Square44x44Logo="Assets\Square44x44Logo.png" />
    </Application>
  </Applications>
  <Capabilities><rescap:Capability Name="runFullTrust" /></Capabilities>
</Package>
"@
    Set-Content (Join-Path $stage "AppxManifest.xml") $manifest -Encoding utf8NoBOM

    New-Item $OutputDirectory -ItemType Directory -Force | Out-Null
    $package = Join-Path $OutputDirectory "drover-windows-x64-$Version-$Status.msix"
    & $makeAppx.FullName pack /d $stage /p $package /o
    if ($LASTEXITCODE -ne 0) {
        throw "makeappx failed with exit code $LASTEXITCODE."
    }
    Write-Output $package
}
finally {
    Remove-Item $temporary -Recurse -Force -ErrorAction SilentlyContinue
}
