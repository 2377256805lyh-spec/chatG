param(
    [Parameter(Mandatory=$true)][string]$AssemblyPath,
    [Parameter(Mandatory=$true)][string]$Output,
    [ValidateRange(1,120)][int]$FramesPerSecond = 60
)
# 素材制作入口：使用程序本身的动作采样与矢量绘制函数导出透明 PNG 序列。
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore,PresentationFramework,WindowsBase
$assembly = [Reflection.Assembly]::LoadFile((Resolve-Path -LiteralPath $AssemblyPath).Path)
$flags = [Reflection.BindingFlags]'Instance,Static,Public,NonPublic'
$sheet = New-Object Windows.Media.Imaging.BitmapImage
$stream = $assembly.GetManifestResourceStream('xiaog-atlas.png')
try {
    $sheet.BeginInit()
    $sheet.CacheOption = [Windows.Media.Imaging.BitmapCacheOption]::OnLoad
    $sheet.StreamSource = $stream
    $sheet.EndInit()
} finally { $stream.Dispose() }
$composerType = $assembly.GetType('XiaoGDesktop.FrameComposer', $true)
$constructor = $composerType.GetConstructor($flags, $null, [type[]]@([Windows.Media.Imaging.BitmapSource],[bool]), $null)
$composer = $constructor.Invoke([object[]]@($sheet.PSObject.BaseObject,$true))
$bodyMethod = $composerType.GetMethod('ExtraBody', $flags)
$armMethod = $composerType.GetMethod('ExtraArm', $flags)
$actions = $assembly.GetType('XiaoGDesktop.ExtraActions', $true)
$durationMethod = $actions.GetMethod('Duration', $flags)
$sampleMethod = $actions.GetMethod('Sample', $flags)
$drawMethod = $actions.GetMethod('Draw', $flags)
$poseType = $assembly.GetType('XiaoGDesktop.ExtraActionPose', $true)
$eyeField = $poseType.GetField('Eye', $flags)
$angleField = $poseType.GetField('Angle', $flags)
$yField = $poseType.GetField('OffsetY', $flags)
$result = @{}
foreach ($name in @('sleepy','idea')) {
    $folder = Join-Path ([IO.Path]::GetFullPath($Output)) $name
    [IO.Directory]::CreateDirectory($folder) | Out-Null
    $duration = $durationMethod.Invoke($null, [object[]]@($name))
    $count = [int][Math]::Round($duration * $FramesPerSecond)
    for ($index=0; $index -lt $count; $index++) {
        $pose = $sampleMethod.Invoke($null, [object[]]@($name, [double]($index / $FramesPerSecond)))
        $visual = New-Object Windows.Media.DrawingVisual
        $context = $visual.RenderOpen()
        try {
            $context.PushTransform((New-Object Windows.Media.ScaleTransform(3,3)))
            $context.PushTransform((New-Object Windows.Media.TranslateTransform(0, $yField.GetValue($pose))))
            $context.PushTransform((New-Object Windows.Media.RotateTransform($angleField.GetValue($pose),96,198)))
            $eye = [int]$eyeField.GetValue($pose)
            $body = $bodyMethod.Invoke($composer, [object[]]@($name, $eye))
            $arm = $armMethod.Invoke($composer, [object[]]@($name, [double]($index / $FramesPerSecond)))
            $context.DrawImage($body, (New-Object Windows.Rect(0,0,192,208)))
            $context.DrawImage($arm, (New-Object Windows.Rect(0,0,192,208)))
            $drawMethod.Invoke($null, [object[]]@($context, $pose)) | Out-Null
            $context.Pop(); $context.Pop(); $context.Pop()
        } finally { $context.Close() }
        $bitmap = New-Object Windows.Media.Imaging.RenderTargetBitmap(576,624,96,96,([Windows.Media.PixelFormats]::Pbgra32))
        $bitmap.Render($visual)
        $encoder = New-Object Windows.Media.Imaging.PngBitmapEncoder
        $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
        $file = [IO.File]::Create((Join-Path $folder ('{0:0000}.png' -f $index)))
        try { $encoder.Save($file) } finally { $file.Dispose() }
    }
    $result[$name] = @{ seconds=$duration; frames=$count; rate=$FramesPerSecond }
    Write-Output ($name + ': ' + $count + ' PNG')
}
$manifest = @{ assembly_sha256=(Get-FileHash -LiteralPath $AssemblyPath -Algorithm SHA256).Hash.ToLowerInvariant(); clips=$result }
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Output 'manifest.json') -Encoding UTF8
