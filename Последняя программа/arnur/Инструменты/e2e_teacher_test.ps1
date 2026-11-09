param([string]$StationExe, [string]$StationId, [string]$SettingsDir)
$ErrorActionPreference = 'Stop'
$srv = Join-Path $PSScriptRoot '..\готовая_программа\Сервер\PromServer.exe'   # [ИЗМЕНЕНО 20.10.2026] путь от папки скрипта
$cfg = Join-Path $env:LOCALAPPDATA "$SettingsDir\server.txt"
$bak = "$cfg.e2ebak"
if (Test-Path $cfg) { Copy-Item $cfg $bak -Force }
New-Item -ItemType Directory -Force (Split-Path $cfg) | Out-Null
Set-Content $cfg "server=ws://127.0.0.1:8090/ws`non=1" -Encoding utf8

$ps = Start-Process $srv -PassThru -WindowStyle Hidden
Start-Sleep 1
$st = Start-Process $StationExe -PassThru
$out = New-Object System.Collections.Generic.List[string]
try {
  $ws = [System.Net.WebSockets.ClientWebSocket]::new()
  $ws.ConnectAsync([Uri]'ws://127.0.0.1:8090/ws', [Threading.CancellationToken]::None).Wait()
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
  Send @{ type = 'hello'; station = 'teacher'; name = 'Преподаватель (тест)' }

  $lesson = @{ id = 'L1'; name = 'Тест'; description = 'проверка'; receive = 1; depart = 0; maxCritical = 0; passScore = 70; autoTrains = 1; everyMin = 5; minutes = 0; startClock = '08:00' }
  $stud = @{ id = 's1'; name = 'Иванов Иван'; group = 'ДС-41'; pwd = $false }
  $deadline = (Get-Date).AddSeconds(40); $hello = $false; $progress = 0; $screen = $null; $result = $null; $phase = 0; $t0 = $null
  while ((Get-Date) -lt $deadline -and -not $result) {
    $m = Recv 500
    if ($m) {
      if ($m.type -eq 'event' -and $m.from -eq $StationId) {
        switch ($m.data.kind) {
          's_hello'    { $hello = $true; $out.Add('s_hello OK')
                         Ev @{ kind = 't_hello'; allowAdd = $true; students = @($stud) }
                         Ev @{ kind = 't_session'; active = $true; gameId = 'g1'; lesson = $lesson; student = $stud; clock = 28800; running = $true; speed = 10; take = $false }
                         $t0 = Get-Date }
          's_progress' { $progress++; $out.Add("s_progress: recv=$($m.data.received) crit=$($m.data.critical) wrong=$($m.data.wrong) score=$($m.data.score)") }
          's_screen'   { $screen = $m.data; $out.Add("s_screen OK: $($m.data.w)x$($m.data.h), base64 $($m.data.jpg.Length) симв.") }
          's_result'   { $result = $m.data }
        }
      } elseif ($m.type -eq 'state') { $out.Add("state: $($m.station) online=$($m.online)") }
      elseif ($m.type -eq 'error') { $out.Add("error: $($m.message)") }
    }
    if ($t0 -and $phase -eq 0 -and ((Get-Date) - $t0).TotalSeconds -gt 5) { $phase = 1; Ev @{ kind = 't_screen'; w = 1200 } }
    if ($t0 -and $phase -eq 1 -and ((Get-Date) - $t0).TotalSeconds -gt 25) { $phase = 2; Ev @{ kind = 't_stop' } }
  }
  if ($result) {
    $out.Add("s_result: student=$($result.student.Name) lesson=$($result.lessonName) recv=$($result.received) score=$($result.score) passed=$($result.passed) events=$($result.events.Count) journal=$($result.journal.Count) clock=$([math]::Round($result.clockEnd - $result.clockStart))s")
    $result.events | ForEach-Object { $out.Add("  event: $($_.Text) -$($_.Penalty)") }
    $result.journal | Select-Object -First 12 | ForEach-Object { $out.Add("  j: $_") }
  } else { $out.Add('НЕТ s_result') }
}
finally {
  if (-not $st.HasExited) { Stop-Process $st.Id -Force }
  if (-not $ps.HasExited) { Stop-Process $ps.Id -Force }
  if (Test-Path $bak) { Move-Item $bak $cfg -Force } else { Remove-Item $cfg -Force }
}
$out
