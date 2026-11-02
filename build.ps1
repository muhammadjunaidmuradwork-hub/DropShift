param([string]$RuntimeRoot)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$out=Join-Path $root 'dist'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if(!(Test-Path -LiteralPath $compiler)){throw 'Install .NET Framework 4.8 on 64-bit Windows.'}
$names=@('DropShift.cs','ImageConverter.cs','Theme.cs','Formats.cs','PdfEngine.cs','MediaEngine.cs','OfficeEngine.cs','OfficeWorker.cs','OcrEngine.cs')
$sources=$names | ForEach-Object {Join-Path $root $_}
$refs=@('/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Core.dll','/r:System.Web.Extensions.dll','/r:System.Xml.Linq.dll','/r:System.IO.Compression.dll','/r:System.IO.Compression.FileSystem.dll')
& $compiler /nologo /target:winexe /optimize+ /platform:x64 $refs "/win32manifest:$root\app.manifest" "/out:$out\DropShift.exe" $sources
if($LASTEXITCODE -ne 0){throw 'Application compilation failed'}
Copy-Item "$root\office_worker.py","$root\LICENSE","$root\README.md","$root\THIRD-PARTY.md" $out -Force
if($RuntimeRoot){
 $runtime=(Resolve-Path -LiteralPath $RuntimeRoot).Path
 if($runtime.TrimEnd('\') -eq $out.TrimEnd('\')){throw 'RuntimeRoot must differ from the build output directory'}
 foreach($file in @('pdfium.dll','Tesseract.dll','THIRD-PARTY-NOTICES.txt')){
  if(Test-Path -LiteralPath "$runtime\$file"){Copy-Item -LiteralPath "$runtime\$file" -Destination $out -Force}
 }
 foreach($folder in @('Codecs','Office','x64','tessdata','PDFium-Licenses','OCR-Licenses')){
  if(Test-Path -LiteralPath "$runtime\$folder"){
   & robocopy "$runtime\$folder" "$out\$folder" /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
   if($LASTEXITCODE -ge 8){throw "Engine copy failed: $folder"}
  }
 }
}
if(Test-Path "$out\Tesseract.dll"){
 & $compiler /nologo /target:exe /optimize+ /platform:x64 "/r:$out\Tesseract.dll" /r:System.Web.Extensions.dll "/out:$out\DropShift.Ocr.exe" "$root\OcrWorker.cs"
 if($LASTEXITCODE -ne 0){throw 'OCR helper compilation failed'}
}else{Write-Warning 'Core build only: external conversion engines and OCR are not installed. See RUNTIME.md.'}
Write-Output "Built $out\DropShift.exe"
