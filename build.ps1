$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
New-Item -ItemType Directory -Force -Path (Join-Path $PSScriptRoot 'dist'),(Join-Path $PSScriptRoot 'assets') | Out-Null
$runtimeSources=@('deps\naudio.core\lib\netstandard2.0\NAudio.Core.dll','deps\naudio.wasapi\lib\netstandard2.0\NAudio.Wasapi.dll','deps\opencv\lib\net48\OpenCvSharp.dll','deps\opencv-native\runtimes\win-x64\native\OpenCvSharpExtern.dll','deps\system.memory\lib\net461\System.Memory.dll','deps\system.buffers\lib\net461\System.Buffers.dll','deps\system.runtime.compilerservices.unsafe\lib\net461\System.Runtime.CompilerServices.Unsafe.dll','deps\system.numerics.vectors\lib\net46\System.Numerics.Vectors.dll','assets\haarcascade_frontalface_default.xml','assets\THIRD-PARTY.txt','deps\sharpdx\lib\net45\SharpDX.dll','deps\sharpdx.dxgi\lib\net45\SharpDX.DXGI.dll','deps\sharpdx.direct3d11\lib\net45\SharpDX.Direct3D11.dll')
foreach($runtime in $runtimeSources){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $runtime) -Destination (Join-Path $PSScriptRoot 'dist') -Force}
& $compiler /nologo /target:exe /reference:System.Drawing.dll /out:"$PSScriptRoot\assets\IconBuild.exe" "$PSScriptRoot\IconBuild.cs"
if ($LASTEXITCODE -ne 0) { throw 'Falha no construtor de ícone.' }
& "$PSScriptRoot\assets\IconBuild.exe" "$PSScriptRoot\assets"
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Xml.dll /reference:"$PSScriptRoot\dist\SharpDX.dll" /reference:"$PSScriptRoot\dist\SharpDX.DXGI.dll" /reference:"$PSScriptRoot\dist\SharpDX.Direct3D11.dll" /reference:"$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\netstandard.dll" /reference:"$PSScriptRoot\dist\NAudio.Core.dll" /reference:"$PSScriptRoot\dist\NAudio.Wasapi.dll" /reference:"$PSScriptRoot\dist\OpenCvSharp.dll" /win32manifest:"$PSScriptRoot\app.manifest" /win32icon:"$PSScriptRoot\assets\Priint.ico" "/resource:$PSScriptRoot\assets\Priint.ico,Priint.ico" /out:"$PSScriptRoot\dist\Priint.exe" "$PSScriptRoot\Captura.cs" "$PSScriptRoot\Product.cs" "$PSScriptRoot\RecordingEngine.cs" "$PSScriptRoot\RecorderUI.cs" "$PSScriptRoot\RecordingTests.cs" "$PSScriptRoot\UiRegression.cs" "$PSScriptRoot\MediaServices.cs" "$PSScriptRoot\AvUI.cs" "$PSScriptRoot\AvTests.cs" "$PSScriptRoot\DesktopFrames.cs" "$PSScriptRoot\VideoProfiles.cs" "$PSScriptRoot\RecordingLibrary.cs" "$PSScriptRoot\FrameExchange.cs" "$PSScriptRoot\ProfileTests.cs" "$PSScriptRoot\AssemblyInfo.cs"
if ($LASTEXITCODE -ne 0) { throw 'Compilação falhou.' }
Get-Item -LiteralPath (Join-Path $PSScriptRoot 'dist\Priint.exe') | Select-Object FullName,Length




