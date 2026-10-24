$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$loader = Join-Path $dir 'CsInternalLoader.exe'
if (-not (Test-Path -LiteralPath $loader)) { throw "未找到 $loader" }
if (-not (Get-Process hl -ErrorAction SilentlyContinue)) { throw '请先从 cs1.6.exe 启动游戏并进入地图。' }
& $loader
if ($LASTEXITCODE -ne 0) { throw "内部模块加载失败，错误码 $LASTEXITCODE" }
