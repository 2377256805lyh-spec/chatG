$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$sourceFolder = Join-Path $PSScriptRoot 'src'
$assetFolder = Join-Path $PSScriptRoot 'assets'
$compiler = Join-Path $framework 'csc.exe'
$gazeCatalog = Get-Content -LiteralPath (Join-Path $assetFolder 'gaze-flow.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ((Get-FileHash -LiteralPath (Join-Path $assetFolder 'gaze-flow.bin') -Algorithm SHA256).Hash -ine $gazeCatalog.sha256) {
    throw '转头位移文件与清单不一致，请重新制作转头位移。'
}
$gestureCatalog = Get-Content -LiteralPath (Join-Path $assetFolder 'artwork/thinking-layers.json') -Raw -Encoding UTF8 | ConvertFrom-Json
foreach ($asset in $gestureCatalog.atlases_sha256.PSObject.Properties) {
    if ((Get-FileHash -LiteralPath (Join-Path (Join-Path $assetFolder 'artwork') $asset.Name) -Algorithm SHA256).Hash -ine $asset.Value) {
        throw '思考分层原图与清单不一致，请重新制作分层。'
    }
}
$arguments = @(
    '/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/codepage:65001',
    ('/out:' + (Join-Path $PSScriptRoot '小G.exe')),
    ('/win32manifest:' + (Join-Path $sourceFolder 'app.manifest')),
    ('/win32icon:' + (Join-Path $assetFolder 'xiaog.ico')),
    ('/resource:' + (Join-Path $assetFolder 'xiaog-atlas.png') + ',xiaog-atlas.png'),
    ('/resource:' + (Join-Path $assetFolder 'xiaog.ico') + ',xiaog.ico'),
    ('/resource:' + (Join-Path $assetFolder 'gaze-flow.bin') + ',gaze-flow.bin'),
    ('/reference:' + (Join-Path $framework 'WPF/PresentationFramework.dll')),
    ('/reference:' + (Join-Path $framework 'WPF/PresentationCore.dll')),
    ('/reference:' + (Join-Path $framework 'WPF/WindowsBase.dll')),
    ('/reference:' + (Join-Path $framework 'System.Xaml.dll')),
    '/reference:System.Runtime.Serialization.dll', '/reference:System.Windows.Forms.dll',
    '/reference:System.Drawing.dll', (Join-Path $sourceFolder 'Main.cs'),
    '/reference:System.IO.Compression.dll',
    (Join-Path $sourceFolder 'AnimationFrames.cs'),
    (Join-Path $sourceFolder 'FrameComposer.cs'), (Join-Path $sourceFolder 'CodexLink.cs'),
    (Join-Path $sourceFolder 'HeadMotion.cs'),
    (Join-Path $sourceFolder 'SourceArtwork.cs'),
    (Join-Path $sourceFolder 'PoseMotion.cs'),
    (Join-Path $sourceFolder 'GestureLayers.cs'),
    (Join-Path $sourceFolder 'BubbleDesign.cs'),
    (Join-Path $sourceFolder 'VoiceClips.cs')
)
$framePack = Join-Path $assetFolder 'animation-frames.zip'
if (Test-Path -LiteralPath $framePack) { $arguments += '/resource:' + $framePack + ',animation-frames.zip' }
Get-ChildItem -LiteralPath (Join-Path $assetFolder 'artwork') -Filter '*.png' | Where-Object { $_.Name -notlike 'waving*' -and $_.Name -notin @('failed.png', 'arm-background.png', 'running.png') } | ForEach-Object {
    $arguments += '/resource:' + $_.FullName + ',artwork.' + $_.Name
}
$voiceFolder = Join-Path $assetFolder 'voice'
$catalog = Get-Content -LiteralPath (Join-Path $voiceFolder 'catalog.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$mainText = Get-Content -LiteralPath (Join-Path $sourceFolder 'Main.cs') -Raw -Encoding UTF8
$phraseBlock = [regex]::Match($mainText, 'string\[\] phrases\s*=\s*\{([\s\S]*?)\};').Groups[1].Value
$phrases = @([regex]::Matches($phraseBlock, '"([^"]*)"') | ForEach-Object { $_.Groups[1].Value })
if ($catalog.clips.Count -ne $phrases.Count) { throw '语录与配音数量不一致，请重新制作配音。' }
for ($i = 0; $i -lt $phrases.Count; $i++) {
    $clip = $catalog.clips[$i]
    if ($clip.text -cne $phrases[$i] -or $clip.file -cne ('{0:00}.wav' -f $i)) { throw '语录与配音顺序不一致，请重新制作配音。' }
    $file = Join-Path $voiceFolder $clip.file
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ine $clip.sha256) { throw '配音文件与清单不一致。' }
    $arguments += '/resource:' + $file + ',voice.' + $clip.file
}
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw '编译失败，请查看上方错误。' }
Write-Output '已生成小G.exe。'
