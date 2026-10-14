# Hashes the Winland.Ipc contract the same way ProtocolStamp.Hash does.
# Handshake.cs, JsonLine.cs, LineCodec.cs, ShellPush.cs, Wire.cs, WmPicture.cs, in that order.
$ErrorActionPreference = 'Stop'
$names = @(
    'Handshake.cs',
    'JsonLine.cs',
    'LineCodec.cs',
    'ShellPush.cs',
    'Wire.cs',
    'WmPicture.cs'
)
$builder = [System.Text.StringBuilder]::new()
foreach ($name in $names) {
    $text = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot $name))
    $text = $text.Replace("`r`n", "`n").Replace("`r", "`n")
    [void]$builder.Append($text)
    [void]$builder.Append("`n")
}
$bytes = [System.Text.Encoding]::UTF8.GetBytes($builder.ToString())
$sha = [System.Security.Cryptography.SHA256]::Create()
try {
    $hash = $sha.ComputeHash($bytes)
}
finally {
    $sha.Dispose()
}
-join ($hash | ForEach-Object { $_.ToString('x2') })
