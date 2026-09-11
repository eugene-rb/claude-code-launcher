<#
.SYNOPSIS
    ランチャーの音声通知アセット (src/Smooth-Coder.App/Assets/Voice/*.wav) を生成する。

.DESCRIPTION
    Aivis Cloud API (https://api.aivis-project.com/v1/tts/synthesize) で秘書口調のアナウンス音声を
    合成し、WAV としてリポジトリの Assets/Voice へ書き出す。

    アプリは実行時に音声合成を一切行わない。生成済み WAV をリソースとして同梱し、それを再生する。
    無人運転中にネットワーク障害で通知が鳴らなくなるのを避けるためと、API キーを配布物に持ち込まない
    ためである。声を差し替えたくなったときだけ、このスクリプトを手で一度流し直す。

.PARAMETER ApiKey
    Aivis Cloud API のキー。省略時は環境変数 AIVIS_API_KEY を読む。
    キーはこのスクリプトにもリポジトリにも書かないこと。

.PARAMETER ModelUuid
    音声合成モデルの UUID。既定は Aivis 公式ティアの「まお」。

.PARAMETER StyleName
    話者スタイル名。既定は落ち着いた読み上げ向けの「おちつき」。

.EXAMPLE
    ./scripts/generate-voice.ps1 -ApiKey 'aivis_xxxxxxxx'
#>
[CmdletBinding()]
param(
    [string]$ApiKey = $env:AIVIS_API_KEY,
    [string]$ModelUuid = 'a59cb814-0083-4369-8542-f51a29e72af7',
    [string]$StyleName = 'おちつき',
    [double]$SpeakingRate = 1.0,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    throw 'API キーが未指定です。-ApiKey を渡すか、環境変数 AIVIS_API_KEY を設定してください。'
}

# 台詞は VoiceNotificationService.VoiceCue と 1:1 で対応する。片方だけ増やすと、
# アプリ側はアセット欠損としてビープに退避する（無音にはならない）。
$phrases = [ordered]@{
    'limit-detected'    = '利用上限を検知いたしました。引き継ぎの準備をいたします。'
    'handoff-to-codex'  = 'コーデックスへ作業を引き継ぎます。'
    'handoff-to-claude' = 'クロードコードへ作業を引き継ぎます。'
    'auto-resume'       = '作業を再開いたします。'
    'waiting-for-reset' = '両方の利用上限に達しました。リセットまでお待ちください。'
    'awaiting-approval' = '承認をお待ちしております。'
    'resume-failed'     = '自動再開に失敗いたしました。ご確認をお願いいたします。'
    # 1 ターン終わるたびに鳴るので、他より短く切ってある。
    'turn-complete'     = '作業が完了いたしました。'
}

$outputDirectory = Join-Path $PSScriptRoot '..\src\Smooth-Coder.App\Assets\Voice'
if (-not (Test-Path $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}
$outputDirectory = (Resolve-Path $outputDirectory).Path

Write-Host "出力先: $outputDirectory"
Write-Host "モデル: $ModelUuid / スタイル: $StyleName"
Write-Host ''

foreach ($name in $phrases.Keys) {
    $destination = Join-Path $outputDirectory "$name.wav"

    if ((Test-Path $destination) -and -not $Force) {
        Write-Host "skip  $name.wav (既存。上書きするには -Force)"
        continue
    }

    $body = @{
        model_uuid              = $ModelUuid
        style_name              = $StyleName
        text                    = $phrases[$name]
        output_format           = 'wav'
        output_audio_channels   = 'mono'
        output_sampling_rate    = 44100
        speaking_rate           = $SpeakingRate
        # 通知音として鳴らすので、前後の無音は詰めて即座に聞こえ始めるようにする。
        leading_silence_seconds = 0.0
        trailing_silence_seconds = 0.05
    } | ConvertTo-Json -Compress

    # ConvertTo-Json は非 ASCII を \uXXXX へエスケープするため、そのまま UTF-8 バイト列にして送れる。
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($body)

    Invoke-WebRequest -Uri 'https://api.aivis-project.com/v1/tts/synthesize' `
        -Method Post `
        -Headers @{ Authorization = "Bearer $ApiKey" } `
        -ContentType 'application/json' `
        -Body $bytes `
        -OutFile $destination

    $size = (Get-Item $destination).Length
    Write-Host ("ok    {0}.wav  ({1:N0} bytes)  {2}" -f $name, $size, $phrases[$name])
}

Write-Host ''
Write-Host '完了しました。csproj の <Resource Include="Assets\Voice\*.wav" /> で同梱されます。'
