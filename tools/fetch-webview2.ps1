# Downloads the Microsoft Edge WebView2 SDK from NuGet and copies the three DLLs the app
# needs into .\lib. Called by build.cmd when .\lib is missing.
#
# Supply-chain checks: the package version is pinned, the download must match the SHA-256
# below (cross-checked against the SHA-512 published in the NuGet catalog), and every DLL
# must carry a valid Authenticode signature from Microsoft Corporation.
$ErrorActionPreference = 'Stop'
$version = '1.0.2903.40'
$sha256 = 'EF128016DD1E51C59178C827ED5B8AA3322C57AFA8675D930F8109505542AD74'

$root = Split-Path $PSScriptRoot -Parent
$lib = Join-Path $root 'lib'
$tmp = Join-Path ([IO.Path]::GetTempPath()) ("wv2-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $tmp | Out-Null
try {
    $zip = Join-Path $tmp 'wv2.zip'
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -UseBasicParsing "https://www.nuget.org/api/v2/package/Microsoft.Web.WebView2/$version" -OutFile $zip

    $actual = (Get-FileHash $zip -Algorithm SHA256).Hash
    if ($actual -ne $sha256) { throw "WebView2 package hash mismatch. Expected $sha256, got $actual." }

    $pkg = Join-Path $tmp 'pkg'
    Expand-Archive $zip -DestinationPath $pkg
    $files = @(
        "$pkg\lib\net462\Microsoft.Web.WebView2.Core.dll",
        "$pkg\lib\net462\Microsoft.Web.WebView2.WinForms.dll",
        "$pkg\runtimes\win-x64\native\WebView2Loader.dll"
    )
    foreach ($f in $files) {
        $sig = Get-AuthenticodeSignature $f
        if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') {
            throw "Unexpected signature on $(Split-Path $f -Leaf): $($sig.Status) $($sig.SignerCertificate.Subject)"
        }
    }

    New-Item -ItemType Directory -Force $lib | Out-Null
    foreach ($f in $files) { Copy-Item $f $lib -Force; Write-Output ("{0,-40} signature OK" -f (Split-Path $f -Leaf)) }
}
finally {
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
