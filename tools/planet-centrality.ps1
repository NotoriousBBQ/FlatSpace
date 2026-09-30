<#
.SYNOPSIS
  Ranks the planets of a board config by shortest-path betweenness: how many planet pairs have a shortest route that
  passes THROUGH the planet. A high rank means a chokepoint (a crossroads such as Industrial 4 on 4p.json); connection
  count alone misses this (Verdant 4 has 4 connections but sits on almost no shortest routes).

.DESCRIPTION
  Builds the same graph the game uses for an explicit-connection board (Planet.Connections, symmetrized, edge cost =
  straight-line distance, see PathingSystem.BuildExplicitConnections), runs Dijkstra from every planet and counts, for each
  planet, the pairs whose shortest path passes through it (endpoints excluded). One shortest path per pair is used, so
  ties are resolved arbitrarily. Read-only: it changes nothing.

  Written for the "weight planet value by connectivity" idea in FUTURE_FEATURES.md. Findings that motivated it: on
  4p.json the lasting blockade occupations in three tuning runs were Industrial 4 (rank 4 of 100), Normal 9 (rank 14) and
  Verdant 4 (rank 78: not a chokepoint, but a specialised food producer).

.EXAMPLE
  ./tools/planet-centrality.ps1
  ./tools/planet-centrality.ps1 -Board Assets\Flatspace\BoardConfigs\test2.json -Highlight "Normal 0","Ocean 1" -Top 20
#>
param(
  [string]$Board = "Assets\Flatspace\BoardConfigs\4p.json",   # relative to the repo root, or an absolute path
  [string[]]$Highlight = @("Normal 9", "Industrial 4", "Verdant 4"),
  [int]$Top = 12
)

$root = Split-Path -Parent $PSScriptRoot
$path = if ([System.IO.Path]::IsPathRooted($Board)) { $Board } else { Join-Path $root $Board }
$b = Get-Content $path -Raw | ConvertFrom-Json

$pos = @{}; $type = @{}; $adj = @{}
foreach ($p in $b.planetEntries) {
  $pos[$p.name] = @($p.position.x, $p.position.y)
  $type[$p.name] = $p.planetType
  $adj[$p.name] = New-Object System.Collections.ArrayList
}
foreach ($p in $b.planetEntries) {
  foreach ($c in $p.connections) {
    if ($pos.ContainsKey($c)) {
      if (-not $adj[$p.name].Contains($c)) { [void]$adj[$p.name].Add($c) }
      if (-not $adj[$c].Contains($p.name)) { [void]$adj[$c].Add($p.name) }
    }
  }
}

function Dist($a, $b) {
  $dx = $pos[$a][0] - $pos[$b][0]; $dy = $pos[$a][1] - $pos[$b][1]
  [math]::Sqrt($dx * $dx + $dy * $dy)
}

$names = @($pos.Keys)
$between = @{}; foreach ($n in $names) { $between[$n] = 0 }
foreach ($src in $names) {
  $d = @{}; $prev = @{}; $done = @{}
  foreach ($n in $names) { $d[$n] = [double]::MaxValue }
  $d[$src] = 0
  while ($true) {
    $cur = $null; $best = [double]::MaxValue
    foreach ($n in $names) { if (-not $done.ContainsKey($n) -and $d[$n] -lt $best) { $best = $d[$n]; $cur = $n } }
    if ($null -eq $cur) { break }
    $done[$cur] = $true
    foreach ($nb in $adj[$cur]) {
      $nd = $d[$cur] + (Dist $cur $nb)
      if ($nd -lt $d[$nb]) { $d[$nb] = $nd; $prev[$nb] = $cur }
    }
  }
  foreach ($t in $names) {
    if ($t -eq $src -or -not $prev.ContainsKey($t)) { continue }
    $at = $prev[$t]
    while ($at -ne $src) { $between[$at]++; $at = $prev[$at] }
  }
}

$rows = foreach ($n in $names) {
  [pscustomobject]@{ Planet = $n; Connections = $adj[$n].Count; Betweenness = $between[$n]; X = [int]$pos[$n][0]; Y = [int]$pos[$n][1] }
}
$rank = $rows | Sort-Object Betweenness -Descending
$i = 0; foreach ($r in $rank) { $i++; $r | Add-Member -NotePropertyName Rank -NotePropertyValue $i }

"board: $path ($($names.Count) planets)"
"--- top $Top by betweenness"
$rank | Select-Object -First $Top | Format-Table Rank, Planet, Connections, Betweenness, X, Y -AutoSize | Out-String
"--- highlighted planets"
$rank | Where-Object { $_.Planet -in $Highlight } | Format-Table Rank, Planet, Connections, Betweenness, X, Y -AutoSize | Out-String
"--- Prime (home) planets"
foreach ($n in $names) { if ($type[$n] -eq 0) { "$n at ($([int]$pos[$n][0]), $([int]$pos[$n][1]))" } }
