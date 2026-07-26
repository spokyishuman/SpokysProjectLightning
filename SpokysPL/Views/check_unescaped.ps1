$bytes = [IO.File]::ReadAllBytes('ToolsPage.xaml')
$str = [Text.Encoding]::UTF8.GetString($bytes)
$count = 0
for ($i = 0; $i -lt $str.Length - 1; $i++) {
    if ($str[$i] -eq '&' -and $str[$i+1] -ne '#' -and $str[$i+1] -ne 'a' -and $str[$i+1] -ne 'l' -and $str[$i+1] -ne 'g' -and $str[$i+1] -ne 'q' -and $str[$i+1] -ne 'n') {
        $count++
        if ($count -le 10) {
            $start = [Math]::Max(0, $i-30)
            $ctx = $str.Substring($start, 60)
            [Console]::WriteLine("Found at " + $i + ": " + $ctx)
        }
    }
}
[Console]::WriteLine("Total unescaped: " + $count)