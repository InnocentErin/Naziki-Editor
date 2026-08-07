param(
    [string]$RuntimePath = (Join-Path $PSScriptRoot '..\Runtime\OriginalPlayer\NazikiOriginalPlayer.exe'),
    [string]$VfsRoot,
    [switch]$RequireStoryboard,
    [switch]$TestStoryboardFailureRetention
)

$ErrorActionPreference = 'Stop'
$connectionId = [Guid]::NewGuid().ToString('N')
$sessionId = [Guid]::NewGuid().ToString('N')
$nonce = [Guid]::NewGuid().ToString('N')
$pipeName = "naziki-preview-smoke-$([Guid]::NewGuid().ToString('N'))"
$generation = 1
$pipe = [System.IO.Pipes.NamedPipeServerStream]::new(
    $pipeName,
    [System.IO.Pipes.PipeDirection]::InOut,
    1,
    [System.IO.Pipes.PipeTransmissionMode]::Byte,
    [System.IO.Pipes.PipeOptions]::Asynchronous)
$process = $null

function Read-Exact([System.IO.Stream]$Stream, [byte[]]$Buffer, [int]$TimeoutMs) {
    $offset = 0
    while ($offset -lt $Buffer.Length) {
        $task = $Stream.ReadAsync($Buffer, $offset, $Buffer.Length - $offset)
        if (-not $task.Wait($TimeoutMs)) { throw 'Timed out reading Preview protocol frame.' }
        if ($task.Result -eq 0) { throw 'Preview protocol pipe closed.' }
        $offset += $task.Result
    }
}

function Read-Frame([System.IO.Stream]$Stream, [int]$TimeoutMs) {
    $header = [byte[]]::new(4)
    Read-Exact $Stream $header $TimeoutMs
    $length = [BitConverter]::ToInt32($header, 0)
    if ($length -le 0 -or $length -gt 67108864) { throw "Invalid frame length: $length" }
    $payload = [byte[]]::new($length)
    Read-Exact $Stream $payload $TimeoutMs
    return ([Text.Encoding]::UTF8.GetString($payload) | ConvertFrom-Json)
}

function Write-Frame([System.IO.Stream]$Stream, $Envelope) {
    $payload = [Text.Encoding]::UTF8.GetBytes(($Envelope | ConvertTo-Json -Depth 12 -Compress))
    $header = [BitConverter]::GetBytes([int]$payload.Length)
    $Stream.Write($header, 0, $header.Length)
    $Stream.Write($payload, 0, $payload.Length)
    $Stream.Flush()
}

try {
    $arguments = @(
        '-screen-width', '640', '-screen-height', '360', '-force-d3d11',
        '--naziki-preview-session', $sessionId,
        '--naziki-preview-connection', $connectionId,
        '--naziki-preview-generation', "$generation",
        '--naziki-preview-pipe', $pipeName,
        '--naziki-preview-nonce', $nonce
    )
    $process = Start-Process -FilePath $RuntimePath -ArgumentList $arguments -PassThru -WindowStyle Hidden
    $connection = $pipe.WaitForConnectionAsync()
    if (-not $connection.Wait(15000)) { throw 'Unity did not connect to the Preview pipe within 15 seconds.' }

    $hello = Read-Frame $pipe 5000
    if ($hello.protocol -ne 'naziki.editor-preview.v2' -or $hello.type -ne 'host.hello') {
        throw "Unexpected first envelope: $($hello | ConvertTo-Json -Compress)"
    }
    if ($hello.connectionId -ne $connectionId -or $hello.generation -ne $generation -or
        $hello.sessionId -ne $sessionId -or $hello.payload.authenticationNonce -ne $nonce -or
        $hello.payload.hostRevision -lt 5) {
        throw 'Unity host.hello identity or revision did not match the launch contract.'
    }

    Write-Frame $pipe ([ordered]@{
        protocol = 'naziki.editor-preview.v2'
        connectionId = $connectionId
        generation = $generation
        sessionId = $sessionId
        type = 'host.accept'
        requestId = $hello.requestId
        editorVersion = 0
        basePreviewVersion = 0
        targetPreviewVersion = 0
        payload = [ordered]@{ authenticationNonce = $nonce; hostRevision = 5 }
    })
    $ready = Read-Frame $pipe 5000
    if ($ready.type -ne 'host.ready' -or $ready.requestId -ne $hello.requestId) {
        throw "Unexpected handshake completion: $($ready | ConvertTo-Json -Compress)"
    }

    if ($VfsRoot) {
        $resolvedVfsRoot = (Resolve-Path -LiteralPath $VfsRoot).Path
        $loadRequestId = [Guid]::NewGuid().ToString('N')
        Write-Frame $pipe ([ordered]@{
            protocol = 'naziki.editor-preview.v2'
            connectionId = $connectionId
            generation = $generation
            sessionId = $sessionId
            type = 'preview.open'
            requestId = $loadRequestId
            editorVersion = 1
            basePreviewVersion = 0
            targetPreviewVersion = 1
            payload = [ordered]@{
                vfsRoot = $resolvedVfsRoot
                level = 'level.json'
                time = 0
                settings = @{}
                authenticationNonce = $nonce
            }
        })

        $loadDeadline = [DateTime]::UtcNow.AddSeconds(120)
        $progressCount = 0
        $loadReady = $null
        while ([DateTime]::UtcNow -lt $loadDeadline) {
            $message = Read-Frame $pipe 30000
            if ($message.requestId -ne $loadRequestId) { continue }
            if ($message.connectionId -ne $connectionId -or $message.generation -ne $generation -or
                $message.sessionId -ne $sessionId -or $message.targetPreviewVersion -ne 1) {
                throw "Content response identity did not match preview.open: $($message | ConvertTo-Json -Compress)"
            }
            if ($message.type -eq 'preview.load.progress') {
                $progressCount++
                continue
            }
            if ($message.type -eq 'preview.load.failed' -or $message.type -eq 'command.rejected') {
                throw "Unity content load failed: $($message | ConvertTo-Json -Depth 12 -Compress)"
            }
            if ($message.type -eq 'preview.load.ready') {
                $loadReady = $message
                break
            }
        }
        if (-not $loadReady) { throw 'Unity did not report preview.load.ready within 120 seconds.' }
        if ($RequireStoryboard -and $loadReady.payload.storyboardLoaded -ne $true) {
            $diagnostics = $loadReady.payload.diagnostics | ConvertTo-Json -Depth 12 -Compress
            throw "Unity loaded the chart but did not initialize the storyboard: $diagnostics"
        }
        Write-Output "Preview content load succeeded (requestId=$loadRequestId, progressEvents=$progressCount, duration=$($loadReady.payload.duration), storyboardLoaded=$($loadReady.payload.storyboardLoaded))."

        $seekRequestId = [Guid]::NewGuid().ToString('N')
        $playRequestId = [Guid]::NewGuid().ToString('N')
        $seekTime = [Math]::Min(1.0, [Math]::Max(0.0, [double]$loadReady.payload.duration / 4.0))
        foreach ($command in @(
            [ordered]@{ type = 'preview.seek'; requestId = $seekRequestId; payload = @{ time = $seekTime } },
            [ordered]@{ type = 'preview.play'; requestId = $playRequestId; payload = @{} }
        )) {
            Write-Frame $pipe ([ordered]@{
                protocol = 'naziki.editor-preview.v2'
                connectionId = $connectionId
                generation = $generation
                sessionId = $sessionId
                type = $command.type
                requestId = $command.requestId
                editorVersion = 1
                basePreviewVersion = 1
                targetPreviewVersion = 1
                payload = $command.payload
            })
        }

        $seekAcknowledged = $false
        $playingState = $null
        $controlDeadline = [DateTime]::UtcNow.AddSeconds(10)
        while ([DateTime]::UtcNow -lt $controlDeadline -and -not $playingState) {
            $message = Read-Frame $pipe 5000
            if ($message.requestId -eq $seekRequestId -and $message.type -eq 'preview.ack') {
                $seekAcknowledged = $true
                continue
            }
            if ($message.requestId -eq $playRequestId -and $message.type -eq 'preview.rejected') {
                throw "Unity rejected preview.play: $($message | ConvertTo-Json -Depth 12 -Compress)"
            }
            if ($message.requestId -eq $playRequestId -and $message.type -eq 'preview.state' -and
                $message.payload.state -eq 'Playing') {
                if (-not $seekAcknowledged) {
                    throw 'Unity reported Playing before the preceding seek completed.'
                }
                $playingState = $message
            }
        }
        if (-not $playingState) { throw 'Unity did not confirm preview.play within 10 seconds.' }

        $advancedTime = $null
        $advanceDeadline = [DateTime]::UtcNow.AddSeconds(5)
        while ([DateTime]::UtcNow -lt $advanceDeadline -and -not $advancedTime) {
            $message = Read-Frame $pipe 5000
            if ($message.type -eq 'preview.time' -and
                [double]$message.payload.time -ge ($seekTime + 0.25)) {
                $advancedTime = [double]$message.payload.time
            }
        }
        if (-not $advancedTime) { throw 'Unity playback time did not advance after preview.play.' }

        $pauseTimes = @()
        foreach ($pauseIndex in 1..2) {
            if ($pauseIndex -eq 2) { Start-Sleep -Milliseconds 600 }
            $pauseRequestId = [Guid]::NewGuid().ToString('N')
            Write-Frame $pipe ([ordered]@{
                protocol = 'naziki.editor-preview.v2'
                connectionId = $connectionId
                generation = $generation
                sessionId = $sessionId
                type = 'preview.pause'
                requestId = $pauseRequestId
                editorVersion = 1
                basePreviewVersion = 1
                targetPreviewVersion = 1
                payload = @{}
            })
            while ($true) {
                $message = Read-Frame $pipe 5000
                if ($message.requestId -eq $pauseRequestId -and $message.type -eq 'preview.rejected') {
                    throw "Unity rejected preview.pause: $($message | ConvertTo-Json -Depth 12 -Compress)"
                }
                if ($message.requestId -eq $pauseRequestId -and $message.type -eq 'preview.state' -and
                    $message.payload.state -eq 'Paused') {
                    $pauseTimes += [double]$message.payload.time
                    break
                }
            }
        }
        if ([Math]::Abs($pauseTimes[1] - $pauseTimes[0]) -gt 0.05) {
            throw "Unity playback time continued after pause ($($pauseTimes[0]) -> $($pauseTimes[1]))."
        }
        Write-Output "Preview seek/play/pause sequencing succeeded (seek=$seekTime, advanced=$advancedTime)."

        if ($TestStoryboardFailureRetention) {
            $retentionRoot = Join-Path ([System.IO.Path]::GetTempPath()) `
                "naziki-storyboard-retention-$([Guid]::NewGuid().ToString('N'))"
            try {
                Copy-Item -LiteralPath $resolvedVfsRoot -Destination $retentionRoot -Recurse
                [System.IO.File]::WriteAllBytes(
                    (Join-Path $retentionRoot 'retention-broken.png'),
                    [byte[]](0x89, 0x50, 0x4e, 0x47, 0x00))
                [System.IO.File]::WriteAllText(
                    (Join-Path $retentionRoot 'storyboard.json'),
                    '{"sprites":[{"id":"retention-probe","time":0,"path":"retention-broken.png"}]}',
                    [Text.UTF8Encoding]::new($false))

                $replaceRequestId = [Guid]::NewGuid().ToString('N')
                Write-Frame $pipe ([ordered]@{
                    protocol = 'naziki.editor-preview.v2'
                    connectionId = $connectionId
                    generation = $generation
                    sessionId = $sessionId
                    type = 'preview.replaceSnapshot'
                    requestId = $replaceRequestId
                    editorVersion = 2
                    basePreviewVersion = 1
                    targetPreviewVersion = 2
                    payload = [ordered]@{
                        vfsRoot = $retentionRoot
                        level = 'level.json'
                        time = $pauseTimes[1]
                        settings = @{}
                    }
                })

                $replaceReady = $null
                $replaceDeadline = [DateTime]::UtcNow.AddSeconds(30)
                while ([DateTime]::UtcNow -lt $replaceDeadline) {
                    $message = Read-Frame $pipe 10000
                    if ($message.requestId -ne $replaceRequestId) { continue }
                    if ($message.type -eq 'preview.load.failed' -or $message.type -eq 'command.rejected') {
                        throw "Unity rejected the transactional storyboard probe: $($message | ConvertTo-Json -Depth 12 -Compress)"
                    }
                    if ($message.type -eq 'preview.load.ready') {
                        $replaceReady = $message
                        break
                    }
                }
                if (-not $replaceReady) {
                    throw 'Unity did not report the failed storyboard hot update within 30 seconds.'
                }
                $retentionDiagnostics = @($replaceReady.payload.diagnostics)
                if ($replaceReady.payload.storyboardLoaded -ne $true -or
                    $replaceReady.payload.storyboardRetained -ne $true) {
                    throw "Unity did not retain the previous storyboard: $($replaceReady.payload | ConvertTo-Json -Depth 12 -Compress)"
                }
                $decodeDiagnostic = @($retentionDiagnostics | Where-Object {
                    $_.code -eq 'PREVIEW_ASSET_DECODE_FAILED' -and $_.fatal -eq $false
                })
                if ($decodeDiagnostic.Count -ne 1) {
                    throw "Unity did not return the expected nonfatal asset-decode diagnostic: $($retentionDiagnostics | ConvertTo-Json -Depth 12 -Compress)"
                }

                $retainedSeekRequestId = [Guid]::NewGuid().ToString('N')
                Write-Frame $pipe ([ordered]@{
                    protocol = 'naziki.editor-preview.v2'
                    connectionId = $connectionId
                    generation = $generation
                    sessionId = $sessionId
                    type = 'preview.seek'
                    requestId = $retainedSeekRequestId
                    editorVersion = 2
                    basePreviewVersion = 2
                    targetPreviewVersion = 2
                    payload = @{ time = 0.5 }
                })
                while ($true) {
                    $message = Read-Frame $pipe 10000
                    if ($message.requestId -ne $retainedSeekRequestId) { continue }
                    if ($message.type -ne 'preview.ack') {
                        throw "Retained storyboard failed the follow-up seek: $($message | ConvertTo-Json -Depth 12 -Compress)"
                    }
                    break
                }
                Write-Output 'Corrupt-image hot update returned PREVIEW_ASSET_DECODE_FAILED and retained the previous initialized storyboard.'
            }
            finally {
                $resolvedRetentionRoot = [System.IO.Path]::GetFullPath($retentionRoot)
                $resolvedTempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
                if ($resolvedRetentionRoot.StartsWith($resolvedTempRoot,
                        [System.StringComparison]::OrdinalIgnoreCase) -and
                    (Split-Path -Leaf $resolvedRetentionRoot).StartsWith(
                        'naziki-storyboard-retention-',
                        [System.StringComparison]::Ordinal) -and
                    (Test-Path -LiteralPath $resolvedRetentionRoot)) {
                    Remove-Item -LiteralPath $resolvedRetentionRoot -Recurse -Force
                }
            }
        }
    }

    Write-Frame $pipe ([ordered]@{
        protocol = 'naziki.editor-preview.v2'
        connectionId = $connectionId
        generation = $generation
        sessionId = $sessionId
        type = 'host.shutdown'
        requestId = [Guid]::NewGuid().ToString('N')
        editorVersion = 0
        basePreviewVersion = 0
        targetPreviewVersion = 0
        payload = @{}
    })
    Write-Output "Preview v2 handshake succeeded (hostRevision=$($hello.payload.hostRevision))."
}
finally {
    $pipe.Dispose()
    if ($process -and -not $process.HasExited) {
        if (-not $process.WaitForExit(3000)) { $process.Kill() }
    }
    if ($process) { $process.Dispose() }
}
