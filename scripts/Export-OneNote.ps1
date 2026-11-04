# OneNote masaustu uygulamasindaki acik not defterlerini ham XML olarak disa aktarir.
# Her sayfa resimleriyle birlikte (base64) ayri bir dosyaya yazilir; manifest.json sayfa listesini tutar.
# Uygulamanin --import-onenote komutu bu klasoru okur.
#
# Kullanim:  powershell -ExecutionPolicy Bypass -File scripts\Export-OneNote.ps1
#            powershell -ExecutionPolicy Bypass -File scripts\Export-OneNote.ps1 -Notebooks abap-notlarim,hatalar
param(
    [string]$OutDir = (Join-Path $PSScriptRoot '..\ImportData\onenote'),
    [string[]]$Notebooks = @()
)
$ErrorActionPreference = 'Stop'

$on = New-Object -ComObject OneNote.Application
[string]$hierarchy = ''
$on.GetHierarchy('', 4, [ref]$hierarchy)   # 4 = hsPages
[xml]$doc = $hierarchy
$ns = New-Object Xml.XmlNamespaceManager $doc.NameTable
$ns.AddNamespace('one', $doc.DocumentElement.NamespaceURI)

$OutDir = [IO.Path]::GetFullPath($OutDir)
$pagesDir = Join-Path $OutDir 'pages'
if (Test-Path $pagesDir) { Remove-Item $pagesDir -Recurse -Force }
New-Item -ItemType Directory -Force $pagesDir | Out-Null

$utf8 = New-Object Text.UTF8Encoding $false
$manifest = New-Object System.Collections.ArrayList
$i = 0
foreach ($nb in $doc.SelectNodes('//one:Notebook', $ns)) {
    if ($Notebooks.Count -gt 0 -and $Notebooks -notcontains $nb.name) { continue }
    foreach ($section in $nb.SelectNodes('.//one:Section', $ns)) {
        if ($section.isInRecycleBin -eq 'true') { continue }
        # Bolum gruplari varsa yol olarak tutulur: "Ciktilar/Smart Form"
        $groups = @()
        $parent = $section.ParentNode
        while ($parent.LocalName -eq 'SectionGroup') { $groups = @($parent.name) + $groups; $parent = $parent.ParentNode }
        if ($groups -contains 'OneNote_RecycleBin') { continue }
        $order = 0
        foreach ($page in $section.SelectNodes('one:Page', $ns)) {
            if ($page.isInRecycleBin -eq 'true') { continue }
            $i++; $order++
            [string]$content = ''
            $on.GetPageContent($page.ID, [ref]$content, 1)   # 1 = piBinaryData: resimler dahil
            $file = 'pages/{0:D4}.xml' -f $i
            [IO.File]::WriteAllText((Join-Path $OutDir $file), $content, $utf8)
            [void]$manifest.Add([ordered]@{
                File          = $file
                Notebook      = $nb.name
                SectionGroups = ($groups -join '/')
                Section       = $section.name
                Order         = $order
                Level         = [int]$page.pageLevel
                Title         = $page.name
                Created       = $page.dateTime
                Modified      = $page.lastModifiedTime
            })
            Write-Host ("{0,4}  {1} / {2} / {3}" -f $i, $nb.name, $section.name, $page.name)
        }
    }
}
[IO.File]::WriteAllText((Join-Path $OutDir 'manifest.json'), (ConvertTo-Json @($manifest) -Depth 3), $utf8)
Write-Host "Bitti: $i sayfa -> $OutDir"
