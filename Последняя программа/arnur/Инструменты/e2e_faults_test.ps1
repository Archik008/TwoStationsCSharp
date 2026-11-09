# [ДОБАВЛЕНО 20.10.2026, этап 3] Сквозной тест неисправностей: скрипт играет роль преподавателя на сервере.
# Запуск: powershell -ExecutionPolicy Bypass -File e2e_faults_test.ps1 -StationExe <путь к exe> -StationId algabas|akzht -SettingsDir StationAlgabas|StationAKZHT
# Что делает: урок ×10 → t_objects → неисправности (ложная занятость, потеря контроля стрелки, светофор «при переходе в открыт»,
# двигатель у поезда урока) → через 250 игровых секунд снимает ложную занятость → t_stop → печатает s_faults и s_result.
# -Live: только потеря контроля стрелки, урок идёт $Seconds секунд — за это время действия студента делаются руками в окне станции.
param([string]$StationExe, [string]$StationId, [string]$SettingsDir, [int]$Seconds = 34, [switch]$Live, [int]$Speed = 10, [int]$Port = 8091)
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
  function Fault($kind, $obj, $cond = 'none', $condState = '', $tf = 'none') {
    Ev @{ kind = 't_fault_add'; fault = @{ kind = $kind; obj = $obj; cond = $cond; condState = $condState; trainFilter = $tf; trainNumber = 0; start = -1 } }
    $out.Add("t_fault_add: $kind на $obj ($cond $condState)")
  }
  Send @{ type = 'hello'; station = 'teacher'; name = 'Преподаватель (тест)' }

  $lesson = @{ id = 'L1'; name = 'Тест неисправностей'; description = 'проверка'; receive = 0; depart = 0; maxCritical = 0; passScore = 70; autoTrains = 1; everyMin = 5; minutes = 0; startClock = '08:00' }
  $stud = @{ id = 's1'; name = 'Иванов Иван'; group = 'ДС-41'; pwd = $false }
  $deadline = (Get-Date).AddSeconds($Seconds + 20); $result = $null; $phase = 0; $t0 = $null; $objs = $null; $faults = $null
  while ((Get-Date) -lt $deadline -and -not $result) {
    $m = Recv 300
    if ($m -and $m.type -eq 'event' -and $m.from -eq $StationId) {
      switch ($m.data.kind) {
        's_hello'   { $out.Add('s_hello OK')
                      Ev @{ kind = 't_hello'; allowAdd = $true; students = @($stud) }
                      Ev @{ kind = 't_session'; active = $true; gameId = 'g1'; lesson = $lesson; student = $stud; clock = 28800; running = $true; speed = $Speed; take = $false }
                      $t0 = Get-Date }
        's_objects' { $objs = $m.data
                      $out.Add("s_objects: светофоров $($objs.signals.Count), стрелок $($objs.switches.Count), участков $($objs.sections.Count), перегонов $($objs.lines.Count), поездов $($objs.trains.Count), видов $($objs.kinds.Count)")
                      if ($phase -eq 1 -and $Live) {
                        Fault 'sw_nocontrol' $objs.switches[0].id
                        $phase = 5
                      } elseif ($phase -eq 1) {
                        Fault 'sec_occ' $objs.sections[0].id
                        Fault 'sw_nocontrol' $objs.switches[0].id
                        Fault 'sig_noopen' $objs.signals[0].id 'to' 'открыт'
                        $phase = 2
                      } elseif ($phase -eq 3) {
                        if ($objs.trains.Count -gt 0) { Fault 'tr_engine' $objs.trains[0].id } else { $out.Add('поездов на станции нет — tr_engine не задана') }
                        $phase = 4
                      } }
        's_faults'  { $faults = $m.data.list }
        's_result'  { $result = $m.data }
      }
    }
    if (-not $t0) { continue }
    $el = ((Get-Date) - $t0).TotalSeconds
    if ($phase -eq 0 -and $el -gt 2)  { $phase = 1; Ev @{ kind = 't_objects' } }
    if ($phase -eq 2 -and $el -gt 7)  { $phase = 3; Ev @{ kind = 't_objects' } }
    if ($phase -eq 4 -and $el -gt 30) { $phase = 5; Ev @{ kind = 't_fault_remove'; id = 'f1' }; $out.Add('t_fault_remove f1') }
    if ($phase -eq 5 -and $el -gt $Seconds) {
      $phase = 6
      $faults | ForEach-Object { $out.Add("  s_faults: $($_.id) $($_.name) | $($_.label) | $($_.stateText)") }
      Ev @{ kind = 't_stop' }
    }
  }
  if ($result) {
    $out.Add("s_result: score=$($result.score) passed=$($result.passed) events=$($result.events.Count) faults=$($result.faults.Count) du46=$($result.du46.Count) docs=$(@($result.docs).Count) journal=$($result.journal.Count)")   # [этап 4] docs — журналы, бланки
    $result.events | ForEach-Object { $out.Add("  event: $($_.Text) -$($_.Penalty)") }
    $result.faults | ForEach-Object { $out.Add("  fault: $($_.name) | $($_.label) | $($_.state)"); $_.log | ForEach-Object { $out.Add("     $_") } }
    $result.journal | Where-Object { $_ -match 'Неисправн|ДУ-46|Радиосвязь|стрелк|Стрелк' } | Select-Object -First 25 | ForEach-Object { $out.Add("  j: $_") }
  } else { $out.Add('НЕТ s_result') }
}
finally {
  if (-not $st.HasExited) { Stop-Process $st.Id -Force }
  if (-not $ps.HasExited) { Stop-Process $ps.Id -Force }
  if (Test-Path $bak) { Move-Item $bak $cfg -Force } else { Remove-Item $cfg -Force }
}
$out
