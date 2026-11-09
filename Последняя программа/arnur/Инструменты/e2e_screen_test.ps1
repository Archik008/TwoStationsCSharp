# [ДОБАВЛЕНО 21.10.2026] Тест большого экрана станции: кадр всего окна, части окна (у АКЖТ — «АКЖТ» / «Путевой пост»), метка кадра.
# Запуск: powershell -ExecutionPolicy Bypass -File e2e_screen_test.ps1 -StationExe <exe> -StationId akzht -SettingsDir StationAKZHT [-OutDir папка для jpg]
param([string]$StationExe, [string]$StationId, [string]$SettingsDir, [string]$OutDir = $env:TEMP, [int]$W = 3840, [int]$Port = 8091)
$ErrorActionPreference = 'Stop'
$srv = Join-Path $PSScriptRoot '..\готовая_программа\Сервер\PromServer.exe'
$cfg = Join-Path $env:LOCALAPPDATA "$SettingsDir\server.txt"
$bak = "$cfg.e2ebak"
if (Test-Path $cfg) { Copy-Item $cfg $bak -Force }
New-Item -ItemType Directory -Force (Split-Path $cfg) | Out-Null
Set-Content $cfg "server=ws://127.0.0.1:$($Port)/ws`non=1" -Encoding utf8
$ps = Start-Process $srv -ArgumentList "-addr :$Port" -PassThru -WindowStyle Hidden -RedirectStandardOutput (Join-Path $OutDir "srv_out.txt") -RedirectStandardError (Join-Path $OutDir "srv_err.txt")
Start-Sleep 1
$st = Start-Process $StationExe -PassThru
$out = New-Object System.Collections.Generic.List[string]
try {
  $ws = [System.Net.WebSockets.ClientWebSocket]::new()
  $ws.ConnectAsync([Uri]"ws://127.0.0.1:$($Port)/ws", [Threading.CancellationToken]::None).Wait()
  function Send($obj) { $j = $obj | ConvertTo-Json -Depth 8 -Compress; $b = [Text.Encoding]::UTF8.GetBytes($j)
    $ws.SendAsync([ArraySegment[byte]]::new($b), 'Text', $true, [Threading.CancellationToken]::None).Wait() }
  $script:pending = $null; $script:acc = New-Object IO.MemoryStream; $script:rbuf = New-Object byte[] 65536
  function Recv([int]$ms) {
    while ($true) {
      if (-not $script:pending) { $script:pending = $ws.ReceiveAsync([ArraySegment[byte]]::new($script:rbuf), [Threading.CancellationToken]::None) }
      if (-not $script:pending.Wait($ms)) { return $null }
      $r = $script:pending.Result; $script:pending = $null
      $script:acc.Write($script:rbuf, 0, $r.Count)
      if ($r.EndOfMessage) { $t = [Text.Encoding]::UTF8.GetString($script:acc.ToArray()); $script:acc.SetLength(0); return ($t | ConvertFrom-Json) }
    }
  }
  function Ev($data) { Send @{ type = 'event'; to = $StationId; data = $data } }
  Send @{ type = 'hello'; station = 'teacher'; name = 'Преподаватель (тест экрана)' }
  $deadline = (Get-Date).AddSeconds(40); $step = 0; $parts = $null
  while ((Get-Date) -lt $deadline -and $step -lt 3) {
    $m = Recv 300
    if (-not $m -or $m.type -ne 'event' -or $m.from -ne $StationId) { continue }
    switch ($m.data.kind) {
      's_hello' { Start-Sleep 3; Ev @{ kind = 't_screen'; w = $W; tag = 'whole' } }
      's_screen' {
        $d = $m.data
        $file = Join-Path $OutDir "screen_$($d.tag).jpg"
        [IO.File]::WriteAllBytes($file, [Convert]::FromBase64String($d.jpg))
        $out.Add("s_screen tag=$($d.tag) $($d.w)x$($d.h) частей: $(@($d.parts).Count) $((@($d.parts) | ForEach-Object { "$($_.name)[$([math]::Round($_.x,3)),$([math]::Round($_.w,3))]" }) -join ' ') → $file")
        $step++
        if ($step -eq 1 -and @($d.parts).Count -gt 0) { $parts = @($d.parts); $p = $parts[0]
          Ev @{ kind = 't_screen'; w = $W; rx = $p.x; ry = $p.y; rw = $p.w; rh = $p.h; tag = 'part1' } }
        elseif ($step -eq 2 -and $parts.Count -gt 1) { $p = $parts[1]
          Ev @{ kind = 't_screen'; w = 1200; rx = $p.x; ry = $p.y; rw = $p.w; rh = $p.h; tag = 'part2small' } }
        else { $step = 3 }
      }
    }
  }
}
catch { $out.Add("ОШИБКА связи: " + $_.Exception.GetBaseException().Message) }
finally {
  if (-not $st.HasExited) { Stop-Process $st.Id -Force }
  if (-not $ps.HasExited) { Stop-Process $ps.Id -Force }
  if (Test-Path $bak) { Move-Item $bak $cfg -Force } else { Remove-Item $cfg -Force }
}
$out
