function Write-ModZip([string]$Destination, [System.Collections.IDictionary]$Files) {
    $parent = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Destination))
    [IO.Directory]::CreateDirectory($parent) | Out-Null
    $temp = $Destination + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
    try {
        $zip = [IO.Compression.ZipFile]::Open($temp, [IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($name in @($Files.Keys | Sort-Object -CaseSensitive)) {
                if ($name -match '\\|(^|/)\.\.(/|$)|^/') { throw "Unsafe archive name: $name" }
                $entry = $zip.CreateEntry($name, [IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = [datetimeoffset]'2000-01-01T00:00:00Z'
                $inputFile = [IO.File]::OpenRead($Files[$name])
                try {
                    $output = $entry.Open()
                    try { $inputFile.CopyTo($output) } finally { $output.Dispose() }
                } finally { $inputFile.Dispose() }
            }
        } finally { $zip.Dispose() }
        [IO.File]::Move($temp, $Destination, $true)
    } finally { if ([IO.File]::Exists($temp)) { [IO.File]::Delete($temp) } }
}
