# verify-apk-adb.ps1 — 真机装机与取数（Windows/adb 版，一条命令）
#
# ## 为什么有这个脚本
# 手机端那套是 `tools/verify-apk-on-device.sh`（走 Shizuku 的 shz 桥）。迁到电脑后
# 没有 Shizuku，但如果继续"手工敲 adb"，就会重演首包事故里的漏项：
# 只看"装上了"、只看"有进程"、只听"没报错"。所以把判据固化成一条命令，
# **每条判据都参与退出码**，无法判定时判**失败**（本项目最提防"看着绿其实没验"）。
#
# ## 判据（V0~V7）
#   V0  前置：APK 文件存在 + 设备已连接（否则 exit 2/3，明确区分"环境没准备好"与"验收没过"）
#   V1  包名与版本**从 APK 内部读出**（aapt2 dump badging），不依赖文件名
#   V2  安装成功（失败时给出 fuse:s0 / 空间 / 签名 / 降级 四类坑的正解）
#   V3  装到设备上的包名 == 包内包名（包名是 PlayerSettings 真生效的硬证据，不符必须判红）
#   V4  进程存活且 **pid 三次采样一致**（一次采样无法排除"崩了又起"的重启循环）
#   V5  logcat 回收（清缓冲 → 启动 → 抓 Unity/Player/Activity/AndroidRuntime/CRASH）
#   V6  能读到设备与包的关键事实（model / Android 版本 / versionName / firstInstallTime）
#
# ## 用法（本机只有 Windows PowerShell 5.1，没有 pwsh）
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify-apk-adb.ps1 -Apk <路径> `
#       [-Serial <序列号>] [-Observe 8] [-OutDir <证据目录>] [-Replace]
#
#   -Replace  签名冲突时先卸旧包再装（**破坏性**：清掉应用数据，故不默认开启）
#
# ## 已实测的三类装机坑（都写进脚本的错误分支里）
#   1) fuse:s0 —— 手机端从共享存储直装会失败（Windows/adb 侧通常不触发，但保留正解）
#   2) resources.arsc 未压缩对齐 —— INSTALL_PARSE_FAILED，需 zipalign + apksigner
#   3) signatures do not match —— 设备上已有**另一个 keystore**（例如 CI 签名）的同名包
#      → 实测命中：本地 Unity 构建 vs 设备上的 CI 包，必须 -Replace 卸旧装新
#
# ## 本脚本必须存成 **UTF-8 with BOM**
#   PS 5.1 读无 BOM 的 UTF-8 会按 GBK 解码 → 中文串破损 → 解析直接报错
#   （实测：无 BOM 时 Parser 在中文行报"意外的标记 }"；加 BOM 后 0 语法错误）
#
# ## 退出码
#   0 = 全部通过   1 = 有判据失败   2 = APK 不存在   3 = 无设备
#
# ## 与手机端脚本的对应关系
#   shz "pm install"        →  adb install -r（无需 fuse 拷拷弯：adb 自行推流到 /data/local/tmp）
#   shz "pidof"             →  adb shell pidof
#   shz "logcat -d -b all"  →  adb logcat -d -b all
#   shz "screencap"         →  adb exec-out screencap -p（留待「像素判据」小类使用）

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Apk,
    [string]$Serial = '',
    [int]$Observe = 8,
    [string]$OutDir = '',
    [switch]$Replace
)

$ErrorActionPreference = 'Continue'
$script:pass = 0
$script:fail = 0

function Ok  ([string]$m) { Write-Host "  [PASS] $m" -ForegroundColor Green;  $script:pass++ }
function Bad ([string]$m) { Write-Host "  [FAIL] $m" -ForegroundColor Red;    $script:fail++ }
function Info([string]$m) { Write-Host "  · $m" }

# ── 工具定位 ──────────────────────────────────────────────────────────
$adb = Join-Path $env:USERPROFILE '.dsh\tools\platform-tools\adb.exe'
if (-not (Test-Path $adb)) { $c = Get-Command adb -ErrorAction SilentlyContinue; if ($c) { $adb = $c.Source } }
$aapt2Candidates = @(
    (Join-Path $env:USERPROFILE '.dsh\tools\build-tools\aapt2.exe'),
    'D:\Unity\6000.3.25f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\build-tools\36.0.0\aapt2.exe'
)
$aapt2 = $aapt2Candidates | Where-Object { Test-Path $_ } | Select-Object -First 1

Write-Host "`n[verify-apk-adb] 真机装机与取数（Windows/adb）" -ForegroundColor Cyan
Write-Host "  APK   : $Apk"
Write-Host "  adb   : $adb"
Write-Host "  aapt2 : $(if ($aapt2) { $aapt2 } else { '(未找到)' })"

# ── V0-a APK 存在 ────────────────────────────────────────────────────
if (-not (Test-Path -LiteralPath $Apk)) {
    Bad "APK 不存在：$Apk"
    Write-Host "`n结论：环境未准备好（退出码 2）" -ForegroundColor Yellow
    exit 2
}
$apkItem = Get-Item -LiteralPath $Apk
Ok ("APK 存在：" + [math]::Round($apkItem.Length / 1MB, 2) + " MB · 修改于 " + $apkItem.LastWriteTime)

# ── V0-b 设备连接 ────────────────────────────────────────────────────
if (-not (Test-Path $adb)) { Bad "找不到 adb：$adb"; exit 3 }
# 注意：必须用 @() 强制成数组——单条匹配时 $devRaw 是字符串，$devRaw[0] 会取到**首字符**
$devRaw = @(& $adb devices 2>&1 | Select-Object -Skip 1 | Where-Object { $_ -match '\tdevice$' })
if ($devRaw.Count -eq 0) {
    Bad "没有已授权的 adb 设备（先 `adb pair` 配对，并在手机上确认调试授权）"
    Write-Host "`n结论：环境未准备好（退出码 3）" -ForegroundColor Yellow
    exit 3
}
if (-not $Serial) { $Serial = ($devRaw[0] -split '\s+')[0].Trim() }
if ([string]::IsNullOrWhiteSpace($Serial) -or $Serial.Length -lt 4) {
    Bad "设备序列号解析异常（读到 '$Serial'）—— 用错误的序列号继续只会得到假结论"
    exit 3
}
# 【实测坑 4】给一个**不在设备列表里**的序列号时，`adb -s <坏的> shell ...` 会**阻塞等待设备**
#   （实测挂死 >10 分钟），既不返回也不报错 —— 属"无法判定"，必须前置拦停而不是挂着。
$known = @($devRaw | ForEach-Object { ($_ -split '\s+')[0].Trim() })
if ($known -notcontains $Serial) {
    Bad "序列号 '$Serial' 不在已连接设备列表里 → adb 会无限等待设备，已前置拦停"
    Info ("当前可用设备：" + ($known -join ' , '))
    Write-Host "`n结论：环境未准备好（退出码 3）" -ForegroundColor Yellow
    exit 3
}
$A = @('-s', $Serial)
Ok "设备已连接：$Serial"

# ── V1 包名与版本从 APK 内部读出 ─────────────────────────────────────
if (-not $aapt2) {
    Bad "找不到 aapt2 —— 无法从包内读包名/版本（判据无法判定 → 判失败）"
} else {
    $badging = & $aapt2 dump badging $Apk 2>&1
    $pkgLine = $badging | Select-String -Pattern "^package: name='([^']+)' versionCode='([^']*)' versionName='([^']*)'" | Select-Object -First 1
    if (-not $pkgLine) {
        Bad "aapt2 读不出 package 行（APK 损坏或不是有效 Android 包）"
    } else {
        $pkg    = $pkgLine.Matches[0].Groups[1].Value
        $vcode  = $pkgLine.Matches[0].Groups[2].Value
        $vname  = $pkgLine.Matches[0].Groups[3].Value
        $label  = ($badging | Select-String -Pattern "^application-label:'([^']*)'") | Select-Object -First 1
        $sdk    = ($badging | Select-String -Pattern "^targetSdkVersion:'([^']*)'")      | Select-Object -First 1
        Ok "包内包名 = $pkg"
        Ok "包内版本 = versionCode '$vcode' / versionName '$vname'"
        Info ("标签 = " + $(if ($label) { $label.Matches[0].Groups[1].Value } else { '?' }) +
              " · targetSdk = " + $(if ($sdk) { $sdk.Matches[0].Groups[1].Value } else { '?' }))
    }
}

# ── V5-a 清日志缓冲（启动前清，日志才对应本次启动）──────────────────
& $adb @A logcat -c 2>&1 | Out-Null

# ── V2 安装 ──────────────────────────────────────────────────────────
Write-Host "`n  [安装]" -ForegroundColor Cyan
& $adb @A shell am force-stop $pkg 2>&1 | Out-Null
$instOut = (& $adb @A install -r $Apk 2>&1) -join "`n"
# 【实测坑 3】设备上已有 CI 用**另一个 keystore** 签的同名包时，会报
#   INSTALL_FAILED_UPDATE_INCOMPATIBLE: signatures do not match
#   解法是卸旧装新；但卸载是**破坏性动作**（清掉应用数据），所以做成显式开关 -Replace，
#   绝不默认执行——本项目最怕"看起来装上了、其实换了另一条路"。
if (-not ($instOut -match 'Success') -and $Replace -and $instOut -match 'UPDATE_INCOMPATIBLE|signatures do not match') {
    Write-Host "  · 检测到签名冲突 + 已指定 -Replace → 卸载设备上的旧包后重装" -ForegroundColor Yellow
    $un = (& $adb @A uninstall $pkg 2>&1) -join ' '
    Info "卸载：$un"
    $instOut = (& $adb @A install -r $Apk 2>&1) -join "`n"
}
$instOk  = $instOut -match 'Success'
if ($instOk) {
    Ok "安装成功"
} else {
    Bad "安装失败：$instOut"
    Write-Host "  ── 装机坑正解（按错误串对症）──"
    if ($instOut -match 'fuse:s0|Permission denied|no access to read file context') {
        Info "fuse:s0 / 共享存储读不了 →【正解】adb 会自动推流到 /data/local/tmp；若仍失败，先手工推："
        Info "    adb $($A -join ' ') push `"$Apk`" /data/local/tmp/x.apk ; adb $($A -join ' ') shell pm install -r /data/local/tmp/x.apk"
    }
    if ($instOut -match 'INSTALL_FAILED_INSUFFICIENT_STORAGE') { Info "空间不足 → 手机清理后重试；或 adb shell pm uninstall <包名> 先卸旧版" }
    if ($instOut -match 'INSTALL_FAILED_UPDATE_INCOMPATIBLE|signatures do not match') {
        Info "签名不一致（设备上的包来自另一个 keystore，例如 CI 签名）→【正解】加 -Replace 让脚本先卸旧包再装："
        Info "    powershell -File tools/verify-apk-adb.ps1 -Apk `"$Apk`" -Replace"
        Info "    （手工等价：adb $($A -join ' ') uninstall $pkg  →  再装）"
    }
    if ($instOut -match 'INSTALL_FAILED_VERSION_DOWNGRADE') { Info "版本降级被拒 → adb install -r -d $Apk" }
    if ($instOut -match 'INSTALL_PARSE_FAILED_NO_CERTIFICATES|resources\.arsc') { Info "包未对齐/未签名（历史上遇过 resources.arsc 未压缩对齐）→ 用 zipalign + apksigner 重打包" }
    Write-Host "`n结论：装机失败（退出码 1）" -ForegroundColor Yellow
    exit 1
}

# ── V3 设备上的包名必须 == 包内包名 ─────────────────────────────────
$onDevice = (& $adb @A shell pm list packages 2>&1) -replace 'package:', '' | ForEach-Object { $_.Trim() }
if ($onDevice -contains $pkg) {
    Ok "设备上存在包：$pkg（与包内一致 → PlayerSettings 生效）"
} else {
    $near = $onDevice | Where-Object { $_ -match 'whisper|DefaultCompany' }
    Bad "设备上找不到包内包名 $pkg；相近的包：$(if ($near) { $near -join ', ' } else { '无' })"
}

# ── V6 设备与包的关键事实 ───────────────────────────────────────────
$model = (& $adb @A shell getprop ro.product.model 2>&1 | Select-Object -First 1).Trim()
$rel   = (& $adb @A shell getprop ro.build.version.release 2>&1 | Select-Object -First 1).Trim()
$sdkv  = (& $adb @A shell getprop ro.build.version.sdk 2>&1 | Select-Object -First 1).Trim()
Ok "设备：$model · Android $rel (API $sdkv)"
$dumpsys = (& $adb @A shell dumpsys package $pkg 2>&1) -join "`n"
$dvName = ([regex]::Match($dumpsys, 'versionName=([^\s]+)')).Groups[1].Value
$dvCode = ([regex]::Match($dumpsys, 'versionCode=(\d+)')).Groups[1].Value
if ($dvName) { Ok "设备上包的版本：versionName=$dvName versionCode=$dvCode" } else { Bad "读不到设备上包的版本（dumpsys 无输出？）" }

# ── V4 起屏检查（实测坑 5）──────────────────────────────────────────
# 【实测】设备熄屏（mWakefulness=Dozing）时启动，Unity 进程会起来并打 "Starting Game Loop"，
#   但 Activity 一启动就被 PAUSE → **游戏逻辑根本不跑**，于是 [Whisper] 一行都没有。
#   实测证据：熄屏时全量 logcat 894 行、`[Whisper]` 命中 0；唤醒后同一次冷启动 6493 行、
#   出现 `[Whisper] BOOT OK · 29 ms · 房间 11 · 门 20 · 道具 7 · 可走格 405 · 着色器 Whisper/UnlitColor`。
#   → 这是**环境前提**，必须显式检查并自动唤醒；醒不过来就判失败（不许静默当"没输出"）。
$wake = ([regex]::Match(((& $adb @A shell dumpsys power 2>&1) -join "`n"), 'mWakefulness=(\w+)')).Groups[1].Value
if ($wake -ne 'Awake') {
    Info "设备未亮屏（mWakefulness=$wake）→ 发 KEYCODE_WAKEUP 唤醒"
    & $adb @A shell input keyevent KEYCODE_WAKEUP 2>&1 | Out-Null
    Start-Sleep -Seconds 2
    $wake = ([regex]::Match(((& $adb @A shell dumpsys power 2>&1) -join "`n"), 'mWakefulness=(\w+)')).Groups[1].Value
}
if ($wake -eq 'Awake') { Ok "屏幕已点亮（mWakefulness=Awake）——熄屏会造成启动假阴性，已排除" }
else { Bad "屏幕仍未点亮（mWakefulness=$wake）→ 游戏逻辑不会运行，本轮的启动判据不可信" }

# ── V4b 冷启动 + pid 三次采样 ───────────────────────────────────────
Write-Host "`n  [启动与进程稳定性]" -ForegroundColor Cyan
# 用 am start + 显式 Activity（不用 monkey：monkey 在息屏/受限后台启动下会静默失败）
$launch = (& $adb @A shell cmd package resolve-activity --brief $pkg 2>&1 | Select-Object -Last 1).Trim()
if ($launch -match '/') {
    Info "启动 Activity：$launch"
    & $adb @A shell am start -n $launch 2>&1 | Out-Null
} else {
    Info "resolve-activity 未给出 Activity（$launch）→ 退回 monkey 启动"
    & $adb @A shell monkey -p $pkg -c android.intent.category.LAUNCHER 1 2>&1 | Out-Null
}
$pids = @()
for ($i = 1; $i -le 3; $i++) {
    Start-Sleep -Seconds ([math]::Max(2, [int]($Observe / 3)))
    $p = ((& $adb @A shell pidof $pkg 2>&1) -join ' ').Trim() -split '\s+' | Where-Object { $_ -match '^\d+$' } | Select-Object -First 1
    $pids += $(if ($p) { $p } else { 'none' })
    Info ("第 $i 次采样 pid = " + $(if ($p) { $p } else { '(无进程)' }))
}
$uniq = ($pids | Sort-Object -Unique)
if ($pids[0] -eq 'none') {
    Bad "进程不存活（三次采样都没拿到 pid）→ 启动即崩或包名不对"
} elseif ($uniq.Count -eq 1) {
    Ok "进程存活且 pid 稳定：$($pids[0])（三次一致 → 排除崩溃重启循环）"
} else {
    Bad "pid 在三次采样间变化（$($pids -join ' → ')）→ 疑似崩溃重启循环"
}

# ── V5 logcat 回收（全量，避免 tag 过滤误伤 [Whisper]）───────────────
Write-Host "`n  [日志回收]" -ForegroundColor Cyan
# 输出目录：默认工程内 build\verify-evidence（PSScriptRoot=…\whisper\tools → 上跳一级=工程根）
if (-not $OutDir) { $OutDir = Join-Path (Split-Path -Parent $PSScriptRoot) 'build\verify-evidence' }
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Force -Path $OutDir | Out-Null }
$logFile = Join-Path $OutDir ("logcat-" + (Get-Date -Format 'yyyyMMdd-HHmmss') + ".txt")
# 【实测坑 6】早期版本按 tag 过滤（-s Unity:V …）会漏掉 boot 早期日志，
#   导致"BOOT OK 明明在，脚本却说没抓到"。改为**抓全量**再在本地筛。
#   用 cmd 重定向落盘（二进制安全），避免 PS 管道按 GBK 转码 UTF-8 日志造成乱码/丢行。
$redir = "`"$adb`" $($A -join ' ') logcat -d > `"$logFile`""
cmd /c $redir | Out-Null
$logLines = if (Test-Path $logFile) { Get-Content -LiteralPath $logFile -Encoding UTF8 } else { @() }
if ($logLines.Count -gt 0) { Ok "logcat 已回收 $($logLines.Count) 行（全量）→ $logFile" } else { Bad "logcat 没有内容（缓冲区被清且应用没输出？）" }

$bootLine = $logLines | Select-String -Pattern '\[Whisper\] BOOT OK' | Select-Object -Last 1
if ($bootLine) { Ok "boot 完成：" + ($bootLine.Line -replace '.*\[Whisper\]', '[Whisper]') }
else {
    Info "本次抓取未见 [Whisper] BOOT OK 行（本小类不判红；启动关键契约属后续判据）"
    Info "  排查顺序：①屏幕是否亮（本脚本已自动唤醒并判据化）②是否冷启动（热启动不重跑 boot）"
}
$shaderErr = $logLines | Select-String -Pattern 'ArgumentNullException.*shader'
if ($shaderErr) { Bad "出现 shader 为空异常（首包黑屏复现）" } else { Ok "无 shader 为空异常" }

# ── 结论 ─────────────────────────────────────────────────────────────
Write-Host ""
if ($script:fail -eq 0) {
    Write-Host "结论：装机与取数全部通过（通过 $($script:pass) · 失败 0）" -ForegroundColor Green
    exit 0
} else {
    Write-Host "结论：存在失败判据（通过 $($script:pass) · 失败 $($script:fail)）" -ForegroundColor Red
    exit 1
}
