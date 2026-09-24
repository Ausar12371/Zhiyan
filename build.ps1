$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$sourcePath = Join-Path $PSScriptRoot 'source'
$exePath = Join-Path $PSScriptRoot 'JevChatAssistant.exe'
$wpfPath = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/WPF'
& $compilerPath /nologo /target:winexe /platform:anycpu /optimize+ /utf8output "/out:$exePath" /r:Accessibility.dll /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Net.Http.dll /r:System.Web.Extensions.dll /r:System.Security.dll "/r:$wpfPath/UIAutomationClient.dll" "/r:$wpfPath/UIAutomationTypes.dll" "/r:$wpfPath/WindowsBase.dll" (Join-Path $sourcePath 'Core.cs') (Join-Path $sourcePath 'App.cs') (Join-Path $sourcePath 'ChatWindows.cs') (Join-Path $sourcePath 'Theme.cs') (Join-Path $sourcePath 'SelfTests.cs') (Join-Path $sourcePath 'LiveMonitor.cs') (Join-Path $sourcePath 'LiveUi.cs') (Join-Path $sourcePath 'ChatPanels.cs') (Join-Path $sourcePath 'ProfileTests.cs') (Join-Path $sourcePath 'Automatic.cs') (Join-Path $sourcePath 'FollowLayout.cs') (Join-Path $sourcePath 'ChatBinding.cs') (Join-Path $sourcePath 'WechatVision.cs') (Join-Path $sourcePath 'MessageGeometry.cs') (Join-Path $sourcePath 'StartupOptions.cs') (Join-Path $sourcePath 'QqAccessible.cs')
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Write-Output "Built: $exePath"
