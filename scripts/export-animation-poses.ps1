param(
    [Parameter(Mandatory=$true)][string]$AssemblyPath,
    [Parameter(Mandatory=$true)][string]$Output,
    [switch]$ShakeOnly,
    [switch]$AllActions,
    [ValidateRange(1,144)][int]$FramesPerSecond = 60
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore,PresentationFramework,WindowsBase
$assembly = [Reflection.Assembly]::LoadFile((Resolve-Path -LiteralPath $AssemblyPath).Path)
$sheet = New-Object Windows.Media.Imaging.BitmapImage
$stream = $assembly.GetManifestResourceStream('xiaog-atlas.png')
try {
    $sheet.BeginInit()
    $sheet.CacheOption = [Windows.Media.Imaging.BitmapCacheOption]::OnLoad
    $sheet.StreamSource = $stream
    $sheet.EndInit()
} finally { $stream.Dispose() }
$flags = [Reflection.BindingFlags]'Instance,Public,NonPublic'
$type = $assembly.GetType('XiaoGDesktop.FrameComposer', $true)
if ($AllActions) {
    $constructor = $type.GetConstructor($flags, $null, [type[]]@([Windows.Media.Imaging.BitmapSource],[bool]), $null)
    $composer = $constructor.Invoke([object[]]@($sheet.PSObject.BaseObject,$true))
} else {
    $constructor = $type.GetConstructor($flags, $null, [type[]]@([Windows.Media.Imaging.BitmapSource]), $null)
    $composer = $constructor.Invoke([object[]]@($sheet.PSObject.BaseObject))
}
$frames = $type.GetField('Frames', $flags).GetValue($composer)
[IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($Output)) | Out-Null
if ($AllActions) {
    if ($ShakeOnly) { throw 'AllActions不能与ShakeOnly同时使用。' }
    $method = $type.GetMethod('Animation', $flags)
    foreach ($clip in @(@('jump',.95),@('tiptoe',1.8),@('thinking',4.4),@('failed',4.1),@('waiting',4.84),@('review',5.06),@('shake',2.6),@('run-right',.68),@('run-left',.68))) {
        $folder = Join-Path $Output $clip[0]
        [IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($folder)) | Out-Null
        $count = [int][Math]::Ceiling($clip[1]*$FramesPerSecond)
        for ($i=0; $i -lt $count; $i++) {
            $image = $method.Invoke($composer, [object[]]@($clip[0],[double]($i/$FramesPerSecond)))
            $encoder = New-Object Windows.Media.Imaging.PngBitmapEncoder
            $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($image))
            $file = [IO.File]::Create((Join-Path $folder ('{0:0000}.png' -f $i)))
            try { $encoder.Save($file) } finally { $file.Dispose() }
        }
        Write-Output ($clip[0]+': '+$count+' PNG')
    }
    return
}
if ($ShakeOnly) {
    $shake = $type.GetMethod('Shake', $flags)
    for ($i=0; $i -le 312; $i++) {
        $image = $shake.Invoke($composer, [object[]]@([double]($i/120)))
        $encoder = New-Object Windows.Media.Imaging.PngBitmapEncoder
        $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($image))
        $file = [IO.File]::Create((Join-Path $Output ('{0:0000}.png' -f $i)))
        try { $encoder.Save($file) } finally { $file.Dispose() }
    }
    Write-Output '已导出摇头的313张完整画面（2.6秒，120帧/秒）。'
    return
}
foreach ($sequence in @(@('neutral',0,1),@('run-right',1,8),@('jump',4,5),@('failed',5,8),@('waiting',6,6),@('review',8,6))) {
    for ($i=0; $i -lt $sequence[2]; $i++) {
        $image = $frames.GetValue([int]$sequence[1], $i)
        $encoder = New-Object Windows.Media.Imaging.PngBitmapEncoder
        $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($image))
        $path = Join-Path $Output ('{0}-{1:00}.png' -f $sequence[0], $i)
        $file = [IO.File]::Create($path)
        try { $encoder.Save($file) } finally { $file.Dispose() }
    }
}
Write-Output '已导出与程序位置、大小一致的动作原画。'
