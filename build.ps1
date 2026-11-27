# Builds LegionRgb.exe (case + GPU) and LGPU.exe (GPU only) with the C# compiler that ships with Windows
# (.NET Framework 4.x). No SDK needed.
$ErrorActionPreference = 'Stop'
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = "$root\src"
$refs = '/r:System.Runtime.Serialization.dll', '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll'
$shared = "$src\Model.cs", "$src\Interop.cs", "$src\GpuBackend.cs", "$src\Applier.cs", "$src\Ui.cs"
New-Item -ItemType Directory -Force "$root\bin" | Out-Null

& $csc /nologo /target:winexe /platform:x64 /optimize+ /out:"$root\bin\LegionRgb.exe" @refs @shared "$src\CaseBackend.cs" "$src\MainForm.cs" "$src\Program.cs"
if ($LASTEXITCODE -ne 0) { throw "LegionRgb build failed" }
& $csc /nologo /target:winexe /platform:x64 /optimize+ /out:"$root\bin\LGPU.exe" @refs @shared "$src\lgpu\LgpuForm.cs" "$src\lgpu\LgpuProgram.cs"
if ($LASTEXITCODE -ne 0) { throw "LGPU build failed" }
Write-Host "Built $root\bin\LegionRgb.exe and $root\bin\LGPU.exe"
