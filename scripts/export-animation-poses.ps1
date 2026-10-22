param(
    [Parameter(Mandatory=$true)][string]$AssemblyPath,
    [Parameter(Mandatory=$true)][string]$Output
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
$constructor = $type.GetConstructor($flags, $null, [type[]]@([Windows.Media.Imaging.BitmapSource]), $null)
$composer = $constructor.Invoke([object[]]@($sheet.PSObject.BaseObject))
$frames = $type.GetField('Frames', $flags).GetValue($composer)
[IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($Output)) | Out-Null
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
