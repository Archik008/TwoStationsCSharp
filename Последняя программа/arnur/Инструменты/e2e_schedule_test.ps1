# [ДОБАВЛЕНО 22.10.2026, этап 5] Сквозной тест расписания: скрипт играет роль преподавателя на сервере.
# Запуск: powershell -ExecutionPolicy Bypass -File e2e_schedule_test.ps1 -StationExe <путь к exe> -StationId algabas|akzht -SettingsDir StationAlgabas|StationAKZHT
# Что делает: урок ×10 с расписанием из одного поезда №2002 (из тоннеля к Ч АКЖТ 08:02, пост 08:05, Алгабас 08:07) →
# через ~20 с спрашивает t_objects: поезд №2002 должен быть на схеме (у АКЖТ — у Ч, у Алгабаса — на НАУ, потому что АКЖТ нет на связи) →
# t_stop → печатает s_result (scheduleName, trains, журнал расписания) и все s_train_event.
param([string]$StationExe, [string]$StationId, [string]$SettingsDir, [int]$Speed = 10, [int]$Port = 8091)
$ErrorActionPreference = 'Stop'
$srv = Join-Path $PSScriptRoot '..\готовая_программа\Сервер\PromServer.exe'
$cfg = Join-Path $env:LOCALAPPDATA "$SettingsDir\server.txt"
$bak = "$cfg.e2ebak"
if (Test-Path $cfg) { Copy-Item $cfg $bak -Force }
New-Item -ItemType Directory -Force (Split-Path $cfg) | Out-Null
Set-Content $cfg "server=ws://127.0.0.1:$($Port)/ws`non=1" -Encoding utf8

$ps = Start-Process $srv -ArgumentList "-addr :$Port" -PassThru -WindowStyle Hidden
Start-Sleep 1
$st = Start-Process $StationExe -PassThru
$out = New-Object System.Collections.Generic.List[string]
try {
  $ws = [System.Net.WebSockets.ClientWebSocket]::new()
  $ws.ConnectAsync([Uri]"ws://127.0.0.1:$($Port)/ws", [Threading.CancellationToken]::None).Wait()
  function Send($obj) { $j = $obj | ConvertTo-Json -Depth 10 -Compress; $b = [Text.Encoding]::UTF8.GetBytes($j)
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

  $sched = @{ id = 'e2e'; name = 'Тестовое расписание'; trains = @(
      @{ number = 2002; category = 'грузовой'; origin = 'tun_akzht'; turnFrom = 0; note = ''; stops = @(
          @{ station = 'akzht'; arr = '08:02'; dep = '08:03'; track = '4' },
          @{ station = 'post'; arr = '08:05'; dep = '08:05'; track = '' },
          @{ station = 'algabas'; arr = '08:07'; dep = ''; track = '3' }) }) }
  $lesson = @{ id = 'L1'; name = 'Тест расписания'; description = 'проверка'; receive = 0; depart = 0; maxCritical = 0; passScore = 70;
               autoTrains = 0; everyMin = 5; minutes = 0; startClock = '08:00'; schedule = $sched; scheduleLeadMin = 1; trainStepSec = 12 }
  $stud = @{ id = 's1'; name = 'Иванов Иван'; group = 'ДС-41'; pwd = $false }
  $deadline = (Get-Date).AddSeconds(70); $result = $null; $phase = 0; $t0 = $null
  while ((Get-Date) -lt $deadline -and -not $result) {
    $m = Recv 300
    if ($m -and $m.type -eq 'event' -and $m.from -eq $StationId) {
      switch ($m.data.kind) {
        's_hello'       { $out.Add('s_hello OK')
                          Ev @{ kind = 't_hello'; allowAdd = $true; students = @($stud) }
                          Ev @{ kind = 't_session'; active = $true; gameId = 'g5'; lesson = $lesson; student = $stud; clock = 28800; running = $true; speed = $Speed; take = $false }
                          $t0 = Get-Date }
        's_objects'     { $tr = @($m.data.trains | ForEach-Object { $_.label })
                          $out.Add("s_objects: поезда на схеме: $($tr -join '; ')")
                          if ($tr -match '2002') { $out.Add('OK: поезд №2002 выпущен по расписанию') } else { $out.Add('ОШИБКА: поезда №2002 нет') } }
        's_train_event' { $e = $m.data.ev; $out.Add("s_train_event: №$($e.Number) $($e.Station) $($e.Kind) $($e.Clock) путь $($e.Track)") }
        's_result'      { $result = $m.data }
      }
    }
    if (-not $t0) { continue }
    $el = ((Get-Date) - $t0).TotalSeconds
    if ($phase -eq 0 -and $el -gt 40) { $phase = 1; Ev @{ kind = 't_objects' } }
    if ($phase -eq 1 -and $el -gt 44) { $phase = 2; Ev @{ kind = 't_stop' } }
  }
  if ($result) {
    $out.Add("s_result: scheduleName=$($result.scheduleName) trains=$(@($result.trains).Count) score=$($result.score) events=$(@($result.events).Count)")
    $result.journal | Where-Object { $_ -match 'Расписание|2002' } | Select-Object -First 12 | ForEach-Object { $out.Add("  j: $_") }
  } else { $out.Add('НЕТ s_result') }
}
finally {
  if (-not $st.HasExited) { Stop-Process $st.Id -Force }
  if (-not $ps.HasExited) { Stop-Process $ps.Id -Force }
  if (Test-Path $bak) { Move-Item $bak $cfg -Force } else { Remove-Item $cfg -Force }
}
$out
