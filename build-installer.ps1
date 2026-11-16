$ErrorActionPreference='Stop'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$dependencyNames=@('NAudio.Core.dll','NAudio.Wasapi.dll','OpenCvSharp.dll','OpenCvSharpExtern.dll','System.Memory.dll','System.Buffers.dll','System.Numerics.Vectors.dll','System.Runtime.CompilerServices.Unsafe.dll','haarcascade_frontalface_default.xml','THIRD-PARTY.txt','SharpDX.dll','SharpDX.DXGI.dll','SharpDX.Direct3D11.dll')
$dependencyFiles=$dependencyNames|ForEach-Object{Join-Path $PSScriptRoot "dist\$_"}
Compress-Archive -LiteralPath $dependencyFiles -DestinationPath "$PSScriptRoot\assets\dependencies.zip" -Force
& $compiler /nologo /target:winexe /optimize+ /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll /win32icon:"$PSScriptRoot\assets\Priint.ico" "/resource:$PSScriptRoot\assets\Priint.ico,Priint.ico" "/resource:$PSScriptRoot\dist\Priint.exe,Priint.app" "/resource:$PSScriptRoot\assets\dependencies.zip,Priint.dependencies" "/resource:$PSScriptRoot\assets\tools.zip,Priint.tools" /out:"$PSScriptRoot\dist\Priint-Instalador-6.0.1.exe" "$PSScriptRoot\Installer.cs" "$PSScriptRoot\AssemblyInfo.cs"
if($LASTEXITCODE -ne 0){throw 'Falha ao compilar instalador'}




