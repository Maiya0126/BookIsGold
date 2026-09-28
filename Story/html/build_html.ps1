# 书语·HTML 构建脚本
# 用法：在 Story 目录下执行  powershell -ExecutionPolicy Bypass -File .\html\build_html.ps1
# 将 Story/*.md 按序合并为一部完整阅读版 HTML

$ErrorActionPreference = "Stop"
$storyDir = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$outFile  = Join-Path $storyDir "html\劝学诗里的千年灵_环世界颜氏书灵物语_全本.html"

$files = Get-ChildItem -Path $storyDir -Filter "*.md" | Where-Object { $_.Name -ne "人物设定.md" } | Sort-Object Name

function Convert-MdToHtml([string]$text) {
    $lines = $text -split "`r?`n"
    $sb = New-Object System.Text.StringBuilder
    $inList = $false
    foreach ($line in $lines) {
        $t = $line.TrimEnd()
        if ($t -match '^# (.+)')        { if ($inList) { [void]$sb.Append("</ul>`n"); $inList=$false }; [void]$sb.AppendLine("<h1>$($Matches[1])</h1>") }
        elseif ($t -match '^## (.+)')   { if ($inList) { [void]$sb.Append("</ul>`n"); $inList=$false }; [void]$sb.AppendLine("<h2>$($Matches[1])</h2>") }
        elseif ($t -match '^> ?(.*)')   { if ($inList) { [void]$sb.Append("</ul>`n"); $inList=$false }; [void]$sb.AppendLine("<blockquote>$($Matches[1])</blockquote>") }
        elseif ($t -match '^- (.+)')    { if (-not $inList) { [void]$sb.Append("<ul>`n"); $inList=$true }; [void]$sb.AppendLine("<li>$($Matches[1])</li>") }
        elseif ($t -match '^\*\*(.+)')  { if ($inList) { [void]$sb.Append("</ul>`n"); $inList=$false }; [void]$sb.AppendLine("<p class='verse'>$($Matches[1] -replace '\*\*','')</p>") }
        elseif ($t.Trim() -eq '')       { if ($inList) { [void]$sb.Append("</ul>`n"); $inList=$false } }
        else                            { if ($inList) { [void]$sb.Append("</ul>`n"); $inList=$false }; $escaped = $t -replace '\*\*(.+?)\*\*','<strong>$1</strong>'; [void]$sb.AppendLine("<p>$escaped</p>") }
    }
    if ($inList) { [void]$sb.Append("</ul>`n") }
    return $sb.ToString()
}

$body = New-Object System.Text.StringBuilder
foreach ($f in $files) {
    $md = Get-Content -Path $f.FullName -Raw -Encoding UTF8
    [void]$body.AppendLine((Convert-MdToHtml $md))
    [void]$body.AppendLine("<hr>")
}

$html = @"
<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>劝学诗里的千年灵——环世界·颜氏书灵物语（全本）</title>
<style>
body{background:#f7f2e7;color:#3b2f1e;font-family:"Noto Serif SC","Songti SC",serif;line-height:2;max-width:820px;margin:0 auto;padding:32px 20px}
h1{color:#8a6d3b;border-bottom:2px solid #c9a96e;padding-bottom:10px;font-size:1.6em;margin-top:2em}
h2{color:#7a5c30;font-size:1.2em;margin-top:1.6em}
blockquote{border-left:4px solid #c9a96e;margin:1em 0;padding:2px 16px;color:#7a5c30;background:#f0e8d5;border-radius:0 8px 8px 0}
p.verse{text-align:center;font-weight:bold;color:#8a6d3b;letter-spacing:2px}
hr{border:0;border-top:1px dashed #c9a96e80;margin:3em 0}
strong{color:#8a4a1e}
.footer{text-align:center;color:#a08c60;font-size:.9em;margin-top:3em}
</style>
</head>
<body>
$body
<div class="footer">《劝学诗里的千年灵》 · Golden Books「书中自有黄金屋」同作者小说 · by Maiya0126</div>
</body>
</html>
"@

[System.IO.File]::WriteAllText($outFile, $html, (New-Object System.Text.UTF8Encoding $true))
Write-Host "[书语] 构建完成: $outFile" -ForegroundColor Green
Write-Host "[书语] 共合并 $($files.Count) 个章节文件" -ForegroundColor Green
