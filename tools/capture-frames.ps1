# capture-frames.ps1 — 真机逐帧截屏（导出可分析的 PNG 序列 + 可选驱动视角）
#
# ## 为什么有这个脚本
# 像素判据要能回归"氛围/能不能看出场景"，就必须先有**可分析的帧序列**。
# 手机端用 shz 抓图；电脑端用 adb，但 adb 的 screencap 走 exec-out 时会把 CRLF 混进 PNG，
# 直接落盘会得到"看起来像 PNG、其实解不开"的文件 —— 所以本脚本固定用
# `screencap -p <远端路径>` + `adb pull`（二进制安全），并且**每帧校验 PNG magic**。
#
# ## 用法（本机只有 Windows PowerShell 5.1）
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools/capture-frames.ps1 `
#       -OutDir <证据目录> [-Frames 4] [-IntervalMs 1500] [-Swipe] [-Serial <序列号>] [-Tag 前缀]
#
#   -Swipe  每帧之间在屏幕右侧做一次纵向滑动（转视角）——用于「视角可转」的证据
#
# ## 退出码
#   0 = 每帧都拿到且 PNG magic 正确   1 = 有帧缺失/损坏   3 = 无设备

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$OutDir,
    [int]$Frames = 4,
    [int]$IntervalMs = 1500,
    [switch]$Swipe,
    [string]$Serial = '',
    [string]$Tag = 'frame'
)

$adb = Join-Path $env:USERPROFILE '.dsh\tools\platform-tools\adb.exe'
if (-not (Test-Path $adb)) { $c = Get-Command adb -ErrorAction SilentlyContinue; if ($c) { $adb = $c.Source } }

$devRaw = @(& $adb devices 2>&1 | Select-Object -Skip 1 | Where-Object { $_ -match '\tdevice$' })
if ($devRaw.Count -eq 0) { Write-Host "[FAIL] 没有已授权的 adb 设备（先 adb pair 配对）" -ForegroundColor Red; exit 3 }
if (-not $Serial) { $Serial = ($devRaw[0] -split '\s+')[0].Trim() }
$A = @('-s', $Serial)

if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Force -Path $OutDir | Out-Null }
$remote = '/data/local/tmp/whisper-shot.png'

Write-Host "`n[capture-frames] 设备 $Serial → $OutDir（$Frames 帧 · 间隔 ${IntervalMs}ms$(if ($Swipe) { ' · 每帧间转视角' }))" -ForegroundColor Cyan

# 屏幕尺寸（滑动坐标要用）
$sizeLine = (& $adb @A shell wm size 2>&1 | Select-Object -First 1)
$W = 1280; $H = 2800
if ($sizeLine -match '(\d+)x(\d+)') { $W = [int]$Matches[1]; $H = [int]$Matches[2] }
Write-Host "  · 屏幕 ${W}x${H}"

$bad = 0
for ($i = 1; $i -le $Frames; $i++) {
    $name = "{0}-{1:d2}.png" -f $Tag, $i
    $local = Join-Path $OutDir $name
    # 远端抓图 → pull（二进制安全，避免 exec-out 的 CRLF 污染）
    & $adb @A shell screencap -p $remote 2>&1 | Out-Null
    & $adb @A pull $remote $local 2>&1 | Out-Null
    if (-not (Test-Path $local)) { Write-Host "  [FAIL] 第 $i 帧没有拉到：$name" -ForegroundColor Red; $bad++; continue }
    $b = [System.IO.File]::ReadAllBytes($local)
    $isPng = ($b.Length -gt 8 -and $b[0] -eq 0x89 -and $b[1] -eq 0x50 -and $b[2] -eq 0x4E -and $b[3] -eq 0x47)
    if (-not $isPng) { Write-Host "  [FAIL] 第 $i 帧不是有效 PNG（magic 不对，$($b.Length) B）：$name" -ForegroundColor Red; $bad++; continue }
    Write-Host ("  [PASS] {0}  {1} KB" -f $name, [math]::Round($b.Length / 1KB, 1))
    if ($Swipe -and $i -lt $Frames) {
        # 在屏幕右侧 3/4 处纵向滑动：模拟"右半屏拖拽转视角"
        $x = [int]($W * 0.75); $y1 = [int]($H * 0.62); $y2 = [int]($H * 0.42)
        & $adb @A shell input swipe $x $y1 $x $y2 320 2>&1 | Out-Null
        Write-Host "         · 已做一次转视角滑动 (x=$x, $y1→$y2)"
    }
    if ($i -lt $Frames) { Start-Sleep -Milliseconds $IntervalMs }
}
& $adb @A shell rm -f $remote 2>&1 | Out-Null

Write-Host ""
if ($bad -gt 0) { Write-Host "结论：$bad 帧缺失或损坏 → 判失败（退出码 1）" -ForegroundColor Red; exit 1 }
Write-Host "结论：$Frames 帧全部拉取成功且 PNG 有效（退出码 0）" -ForegroundColor Green
exit 0
