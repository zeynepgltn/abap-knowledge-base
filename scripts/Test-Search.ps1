# Arama kabul testi: tests\arama-testleri.json'daki her sorguyu calisan uygulamada arar ve
# beklenen notun ilk 5 sonuc icinde olup olmadigini olcer (hedef: 30 sorgudan en az 27'si).
# "beklenen": sonuc basliginda gecmesi yeterli olan ifadeler (biri yeterli, buyuk/kucuk harf fark etmez).
#
# Kullanim:  powershell -ExecutionPolicy Bypass -File scripts\Test-Search.ps1 [-BaseUrl http://localhost:5168]
param(
    [string]$BaseUrl = 'http://localhost:5168',
    [string]$TestFile = (Join-Path $PSScriptRoot '..\tests\arama-testleri.json'),
    [int]$Top = 5
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Web

$tests = [IO.File]::ReadAllText((Resolve-Path $TestFile), [Text.Encoding]::UTF8) | ConvertFrom-Json
$passed = 0
$rows = foreach ($t in $tests) {
    $url = "$BaseUrl/?q=" + [uri]::EscapeDataString($t.q)
    $html = (Invoke-WebRequest $url -UseBasicParsing).Content
    $titles = @([regex]::Matches($html, '<div class="note-title">(.*?)</div>', 'Singleline') |
        ForEach-Object { [Web.HttpUtility]::HtmlDecode(($_.Groups[1].Value -replace '<[^>]+>', '')).Trim() })
    $rank = 0
    for ($i = 0; $i -lt $titles.Count; $i++) {
        foreach ($e in $t.beklenen) { if ($titles[$i].IndexOf($e, [StringComparison]::OrdinalIgnoreCase) -ge 0) { $rank = $i + 1; break } }
        if ($rank) { break }
    }
    $ok = $rank -ge 1 -and $rank -le $Top
    if ($ok) { $passed++ }
    [pscustomobject]@{
        Sonuc   = if ($ok) { 'OK' } elseif ($rank) { "SIRA $rank" } else { 'YOK' }
        Sorgu   = $t.q
        Toplam  = $titles.Count
        Ilk     = if ($titles.Count) { $titles[0] } else { '' }
    }
}
$rows | Format-Table -AutoSize | Out-String -Width 200
"Ilk $Top icinde bulunan: $passed / $($tests.Count)"
