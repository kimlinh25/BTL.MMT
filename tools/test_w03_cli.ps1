$ErrorActionPreference = 'Stop'

$repo = Split-Path $PSScriptRoot -Parent
$cli = Join-Path $repo 'src/ET4230.Cli/bin/Release/net10.0/ET4230.Cli.dll'
if (-not (Test-Path -LiteralPath $cli)) {
    throw 'Chạy dotnet build ET4230.P2P.sln -c Release trước.'
}

$evidence = (New-Item -ItemType Directory -Path (Join-Path $repo "tmp/w03-cli-$([Guid]::NewGuid().ToString('N'))")).FullName
$processes = [System.Collections.Generic.List[System.Diagnostics.Process]]::new()

function Get-LoopbackPort {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    try {
        $listener.Start()
        return $listener.LocalEndpoint.Port
    }
    finally {
        $listener.Stop()
    }
}

function Start-Cli($Mode, $Config, $Log) {
    $process = Start-Process -FilePath 'dotnet' -ArgumentList @(
        ('"{0}"' -f $cli), $Mode, '--config', ('"{0}"' -f $Config)
    ) -WindowStyle Hidden -PassThru -RedirectStandardOutput "$Log.out" -RedirectStandardError "$Log.err"
    $processes.Add($process)
    return $process
}

function Wait-Ready($Process, $Log, $Marker) {
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    while ($clock.Elapsed.TotalSeconds -lt 15 -and -not $Process.HasExited) {
        if (Select-String -LiteralPath "$Log.out" -SimpleMatch -Pattern $Marker -Quiet) { return }
        Start-Sleep -Milliseconds 100
    }
    throw "Không thấy $Marker. Log: $Log.out / $Log.err"
}

function Wait-Exit($Process, $Expected, $Log) {
    if (-not $Process.WaitForExit(15000) -or $Process.ExitCode -ne $Expected) {
        throw "Mong exit $Expected. Log: $Log.out / $Log.err"
    }
}

$trackerPort = Get-LoopbackPort
$trackerConfig = Join-Path $evidence 'tracker.json'
@{ schemaVersion = 1; tracker = @{ port = $trackerPort; ttlSeconds = 4 } } |
    ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $trackerConfig -Encoding utf8

try {
    $trackerLog = Join-Path $evidence 'tracker'
    $tracker = Start-Cli 'tracker' $trackerConfig $trackerLog
    Wait-Ready $tracker $trackerLog 'TRACKER_READY'

    foreach ($size in @(196611, 0)) {
        $case = (New-Item -ItemType Directory -Path (Join-Path $evidence "bytes-$size")).FullName
        do { $seedPort = Get-LoopbackPort } while ($seedPort -eq $trackerPort)
        $config = Join-Path $case 'peer.json'
        @{
            schemaVersion = 1
            tracker = @{ port = $trackerPort; heartbeatSeconds = 1; ttlSeconds = 4 }
            peer = @{ port = $seedPort }
            transfer = @{ pieceSizeBytes = 65536; blockSizeBytes = 4096; requestTimeoutSeconds = 5 }
            file = @{ sourcePath = 'source.bin'; manifestPath = 'manifest.json'; outputPath = 'received.bin' }
        } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $config -Encoding utf8

        $source = Join-Path $case 'source.bin'
        $output = Join-Path $case 'received.bin'
        $bytes = [byte[]]::new($size)
        for ($index = 0; $index -lt $size; $index++) { $bytes[$index] = $index % 251 }
        [System.IO.File]::WriteAllBytes($source, $bytes)

        $seedLog = Join-Path $case 'seed'
        $seed = Start-Cli 'seed' $config $seedLog
        Wait-Ready $seed $seedLog 'SEED_READY'
        if ($size -gt 0) {
            Start-Sleep -Seconds 5 # Bắt buộc discovery vẫn tìm được seed sau quá một TTL.
        }

        $downloadLog = Join-Path $case 'download'
        $download = Start-Cli 'download' $config $downloadLog
        Wait-Exit $download 0 $downloadLog
        $sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()
        $outputHash = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToLowerInvariant()
        $manifest = Get-Content -LiteralPath (Join-Path $case 'manifest.json') -Raw | ConvertFrom-Json
        if ($sourceHash -ne $outputHash -or $outputHash -ne $manifest.fileId -or
            -not (Select-String -LiteralPath "$downloadLog.out" -SimpleMatch -Pattern 'DOWNLOAD_OK' -Quiet)) {
            throw "SHA-256 hoặc log hoàn tất sai cho tệp $size byte."
        }
        Write-Host "PASS TCP loopback bytes=$size SHA256=$outputHash"

        $collisionLog = Join-Path $case 'download-existing'
        $collision = Start-Cli 'download' $config $collisionLog
        Wait-Exit $collision 1 $collisionLog
        if ((Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToLowerInvariant() -ne $outputHash) {
            throw 'Lần tải thứ hai đã thay đổi tệp đích có sẵn.'
        }
    }
}
finally {
    foreach ($process in $processes) {
        if (-not $process.HasExited) {
            $process.Kill()
            $process.WaitForExit()
        }
        $process.Dispose()
    }
    Write-Host "Log và cấu hình: $evidence"
}
