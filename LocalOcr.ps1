$ErrorActionPreference='Stop'
[Console]::InputEncoding=[Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$null=[Windows.Media.Ocr.OcrEngine,Windows.Foundation,ContentType=WindowsRuntime]
$null=[Windows.Globalization.Language,Windows.Globalization,ContentType=WindowsRuntime]
$null=[Windows.Storage.Streams.InMemoryRandomAccessStream,Windows.Storage.Streams,ContentType=WindowsRuntime]
$null=[Windows.Storage.Streams.DataWriter,Windows.Storage.Streams,ContentType=WindowsRuntime]
$null=[Windows.Graphics.Imaging.BitmapDecoder,Windows.Graphics.Imaging,ContentType=WindowsRuntime]
$null=[Windows.Graphics.Imaging.SoftwareBitmap,Windows.Graphics.Imaging,ContentType=WindowsRuntime]
$null=[Windows.Media.Ocr.OcrResult,Windows.Foundation,ContentType=WindowsRuntime]
$asTask=[System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.IsGenericMethod -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' } | Select-Object -First 1
function Await($operation,$type) { $task=$asTask.MakeGenericMethod($type).Invoke($null,@($operation)); $task.GetAwaiter().GetResult() }
try {
    $language=[Windows.Globalization.Language]::new('zh-Hans')
    $engine=[Windows.Media.Ocr.OcrEngine]::TryCreateFromLanguage($language)
    if($null -eq $engine) { throw '缺少 Windows 简体中文 OCR 语言包，请安装中文语言的基本输入/OCR组件后重试。' }
    [Console]::WriteLine('READY')
    while($null -ne ($line=[Console]::ReadLine())) {
        $stream=$null; $writer=$null; $bitmap=$null
        try {
            $bytes=[Convert]::FromBase64String($line)
            $stream=[Windows.Storage.Streams.InMemoryRandomAccessStream]::new()
            $writer=[Windows.Storage.Streams.DataWriter]::new($stream)
            $writer.WriteBytes($bytes)
            $null=Await ($writer.StoreAsync()) ([UInt32])
            $writer.DetachStream() | Out-Null
            $stream.Seek(0)
            $decoder=Await ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])
            $bitmap=Await ($decoder.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])
            $result=Await ($engine.RecognizeAsync($bitmap)) ([Windows.Media.Ocr.OcrResult])
            $lines=@($result.Lines | ForEach-Object { $_.Text })
            $boxes=@($result.Lines | ForEach-Object {
                $words=@($_.Words)
                if($words.Count -gt 0) {
                    $left=($words | ForEach-Object {$_.BoundingRect.X} | Measure-Object -Minimum).Minimum
                    $top=($words | ForEach-Object {$_.BoundingRect.Y} | Measure-Object -Minimum).Minimum
                    $right=($words | ForEach-Object {$_.BoundingRect.X+$_.BoundingRect.Width} | Measure-Object -Maximum).Maximum
                    $bottom=($words | ForEach-Object {$_.BoundingRect.Y+$_.BoundingRect.Height} | Measure-Object -Maximum).Maximum
                    @{text=$_.Text;x=$left;y=$top;width=($right-$left);height=($bottom-$top)}
                }
            })
            [Console]::WriteLine((@{text=($lines -join "`n");lines=$boxes} | ConvertTo-Json -Depth 5 -Compress))
        } catch { [Console]::WriteLine('{"error":"本地 OCR 失败，请重新选择清晰的聊天消息区域。"}') }
        finally { if($bitmap){$bitmap.Dispose()}; if($writer){$writer.Dispose()}; if($stream){$stream.Dispose()} }
    }
} catch { [Console]::WriteLine((@{error=$_.Exception.Message} | ConvertTo-Json -Compress)); exit 1 }
