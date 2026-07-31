$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

function Get-Keys([string]$path) {
    [xml]$document = Get-Content -Raw -Encoding UTF8 -LiteralPath $path
    $namespaces = New-Object System.Xml.XmlNamespaceManager($document.NameTable)
    $namespaces.AddNamespace('x', 'http://schemas.microsoft.com/winfx/2006/xaml')
    return @($document.SelectNodes('//*[@x:Key]', $namespaces) | ForEach-Object {
        $_.GetAttribute('Key', 'http://schemas.microsoft.com/winfx/2006/xaml')
    })
}

$english = Get-Keys (Join-Path $root 'Resources\Strings.en.xaml')
$russian = Get-Keys (Join-Path $root 'Resources\Strings.ru.xaml')
$difference = Compare-Object $english $russian
if ($difference) { throw "Localization key mismatch:`n$($difference | Out-String)" }

[xml]$mainWindow = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $root 'MainWindow.xaml')
$allowedLiterals = @('Borderus')
foreach ($node in $mainWindow.SelectNodes('//*')) {
    foreach ($attribute in $node.Attributes) {
        if ($attribute.LocalName -notin @('Text', 'Content', 'Header', 'ToolTip', 'Title')) { continue }
        $value = $attribute.Value
        if ($value -match '^\{DynamicResource ([^}]+)\}$') {
            if ($matches[1] -notin $english) { throw "Missing localization key: $($matches[1])" }
            continue
        }
        if ($value -in $allowedLiterals -or $value -eq [string][char]0x21BA -or $value.EndsWith(' RU') -or
            $value -match '^-?\d+(?:\.\d+)?(?: px|%)?$') { continue }
        throw "Unlocalized UI text: $value"
    }
}

[pscustomobject]@{ Languages = 2; Keys = $english.Count; MainWindowText = 'localized' }
