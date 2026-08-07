param(
    [string]$RuntimePath = (Join-Path $PSScriptRoot '..\Runtime\OriginalPlayer\NazikiOriginalPlayer.exe'),
    [string]$VfsRoot
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
        Write-Output "Preview content load succeeded (requestId=$loadRequestId, progressEvents=$progressCount, duration=$($loadReady.payload.duration))."
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
