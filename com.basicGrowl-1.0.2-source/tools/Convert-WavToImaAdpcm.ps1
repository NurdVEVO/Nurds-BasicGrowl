param(
    [Parameter(Mandatory = $true)]
    [string]$InputDirectory,
    [string]$OutputDirectory = "EmbeddedAudio\SeekerNoises"
)

$ErrorActionPreference = "Stop"

$indexTable = @(
    -1, -1, -1, -1, 2, 4, 6, 8,
    -1, -1, -1, -1, 2, 4, 6, 8
)

$stepTable = @(
    7, 8, 9, 10, 11, 12, 13, 14, 16, 17,
    19, 21, 23, 25, 28, 31, 34, 37, 41, 45,
    50, 55, 60, 66, 73, 80, 88, 97, 107, 118,
    130, 143, 157, 173, 190, 209, 230, 253, 279, 307,
    337, 371, 408, 449, 494, 544, 598, 658, 724, 796,
    876, 963, 1060, 1166, 1282, 1411, 1552, 1707, 1878, 2066,
    2272, 2499, 2749, 3024, 3327, 3660, 4026, 4428, 4871, 5358,
    5894, 6484, 7132, 7845, 8630, 9493, 10442, 11487, 12635, 13899,
    15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767
)

function Clamp-Int([int]$value, [int]$min, [int]$max) {
    if ($value -lt $min) { return $min }
    if ($value -gt $max) { return $max }
    return $value
}

function Get-WavSampleRate([string]$path) {
    $rateText = & ffprobe -v error -select_streams a:0 -show_entries stream=sample_rate -of default=noprint_wrappers=1:nokey=1 $path
    $sampleRate = 0
    if ($LASTEXITCODE -ne 0 -or -not [int]::TryParse(($rateText | Select-Object -First 1), [ref]$sampleRate)) {
        throw "Could not determine sample rate for '$path'."
    }

    return $sampleRate
}

function Convert-WavToPcm16Mono([string]$path, [int]$sampleRate) {
    $tempPath = [System.IO.Path]::GetTempFileName()
    try {
        & ffmpeg -hide_banner -loglevel error -y -i $path -ac 1 -ar $sampleRate -f s16le -acodec pcm_s16le $tempPath
        if ($LASTEXITCODE -ne 0) {
            throw "ffmpeg failed for '$path'."
        }

        return [System.IO.File]::ReadAllBytes($tempPath)
    }
    finally {
        if (Test-Path -LiteralPath $tempPath) {
            Remove-Item -LiteralPath $tempPath -Force
        }
    }
}

function Convert-Pcm16ToSamples([byte[]]$bytes) {
    $sampleCount = [Math]::Floor($bytes.Length / 2)
    $samples = [int[]]::new($sampleCount)
    for ($i = 0; $i -lt $sampleCount; $i++) {
        $samples[$i] = [System.BitConverter]::ToInt16($bytes, $i * 2)
    }

    return $samples
}

function Encode-ImaNibble([int]$sample, [ref]$predictor, [ref]$stepIndex) {
    $step = $script:stepTable[$stepIndex.Value]
    $diff = $sample - $predictor.Value
    $code = 0
    if ($diff -lt 0) {
        $code = 8
        $diff = -$diff
    }

    $vpdiff = $step -shr 3
    if ($diff -ge $step) {
        $code = $code -bor 4
        $diff -= $step
        $vpdiff += $step
    }

    $step = $step -shr 1
    if ($diff -ge $step) {
        $code = $code -bor 2
        $diff -= $step
        $vpdiff += $step
    }

    $step = $step -shr 1
    if ($diff -ge $step) {
        $code = $code -bor 1
        $vpdiff += $step
    }

    if (($code -band 8) -ne 0) {
        $predictor.Value -= $vpdiff
    }
    else {
        $predictor.Value += $vpdiff
    }

    $predictor.Value = Clamp-Int $predictor.Value -32768 32767
    $stepIndex.Value = Clamp-Int ($stepIndex.Value + $script:indexTable[$code]) 0 88
    return $code -band 15
}

function Write-ImaAdpcm([string]$path, [int[]]$samples, [int]$sampleRate) {
    if ($samples.Length -eq 0) {
        throw "No samples to encode for '$path'."
    }

    $initialPredictor = $samples[0]
    $predictor = $initialPredictor
    $stepIndex = 0
    $encodedBytes = [System.Collections.Generic.List[byte]]::new()
    $pendingNibble = -1

    for ($i = 1; $i -lt $samples.Length; $i++) {
        $code = Encode-ImaNibble $samples[$i] ([ref]$predictor) ([ref]$stepIndex)
        if ($pendingNibble -lt 0) {
            $pendingNibble = $code
        }
        else {
            $encodedBytes.Add([byte](($code -shl 4) -bor $pendingNibble))
            $pendingNibble = -1
        }
    }

    if ($pendingNibble -ge 0) {
        $encodedBytes.Add([byte]$pendingNibble)
    }

    $file = [System.IO.File]::Create($path)
    try {
        $writer = [System.IO.BinaryWriter]::new($file)
        try {
            $writer.Write([byte[]](0x49, 0x41, 0x44, 0x50))
            $writer.Write([int]$sampleRate)
            $writer.Write([int]1)
            $writer.Write([int]$samples.Length)
            $writer.Write([int16]$initialPredictor)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write($encodedBytes.ToArray())
        }
        finally {
            $writer.Dispose()
        }
    }
    finally {
        $file.Dispose()
    }
}

if (-not (Get-Command ffmpeg -ErrorAction SilentlyContinue)) {
    throw "ffmpeg is required and was not found on PATH."
}

if (-not (Get-Command ffprobe -ErrorAction SilentlyContinue)) {
    throw "ffprobe is required and was not found on PATH."
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$files = Get-ChildItem -LiteralPath $InputDirectory -Filter "*.wav" -File | Sort-Object Name
foreach ($file in $files) {
    $sampleRate = Get-WavSampleRate $file.FullName
    $pcmBytes = Convert-WavToPcm16Mono $file.FullName $sampleRate
    $samples = Convert-Pcm16ToSamples $pcmBytes
    $outputPath = Join-Path $OutputDirectory ($file.BaseName + ".imaadpcm")
    Write-ImaAdpcm $outputPath $samples $sampleRate
    $output = Get-Item -LiteralPath $outputPath
    "{0} ({1} Hz) -> {2} bytes ({3:P1} of WAV)" -f $file.Name, $sampleRate, $output.Length, ($output.Length / [double]$file.Length)
}
