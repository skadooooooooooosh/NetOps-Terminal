[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$agreementPath = Join-Path $repositoryRoot 'docs\USER_AGREEMENT.md'
$licensePath = Join-Path $repositoryRoot 'installer\NetOpsTerminal.Installer\License.rtf'
$source = [System.IO.File]::ReadAllText($agreementPath, [System.Text.Encoding]::UTF8)
if ($source -match '[^\x00-\x7F]') {
    throw 'The installer license generator requires ASCII agreement text.'
}
$rtfLines = [System.Collections.Generic.List[string]]::new()
foreach ($line in ($source -split '\r?\n')) {
    if ($line.Length -eq 0) {
        $rtfLines.Add('\par')
        continue
    }
    $prefix = ''
    $suffix = '\par'
    if ($line.StartsWith('# ')) {
        $line = $line.Substring(2)
        $prefix = '\b\fs26 '
        $suffix = '\b0\fs20\par'
    } elseif ($line.StartsWith('## ')) {
        $line = $line.Substring(3)
        $prefix = '\b\fs22 '
        $suffix = '\b0\fs20\par'
    }
    $line = $line.Replace('**', '').Replace('\', '\\').Replace('{', '\{').Replace('}', '\}')
    $rtfLines.Add($prefix + $line + $suffix)
}
$rtf = '{\rtf1\ansi\deff0{\fonttbl{\f0 Segoe UI;}}\f0\fs20' + "`n" +
    ($rtfLines -join "`n") + "`n}"
[System.IO.File]::WriteAllText($licensePath, $rtf, [System.Text.Encoding]::ASCII)
