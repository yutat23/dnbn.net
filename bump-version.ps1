# バージョンを+1して、タグを作成してプッシュするスクリプト
param(
    [Parameter(Mandatory=$false)]
    [ValidateSet("major", "minor", "patch")]
    [string]$BumpType = "patch"
)

# エラー時に停止
$ErrorActionPreference = "Stop"

# プロジェクトファイルのパス
$MainProject = "dnbn.net.csproj"
$WebUIProject = "Extensions\Dnbn.WebUI\Dnbn.WebUI.csproj"

# 現在のバージョンを取得
function Get-Version {
    param([string]$ProjectFile)
    
    $content = Get-Content $ProjectFile -Raw
    if ($content -match '<Version>([\d.]+)</Version>') {
        return $matches[1]
    }
    throw "バージョンが見つかりません: $ProjectFile"
}

# バージョンを更新
function Update-Version {
    param(
        [string]$ProjectFile,
        [string]$NewVersion
    )
    
    $content = Get-Content $ProjectFile -Raw
    $content = $content -replace '<Version>([\d.]+)</Version>', "<Version>$NewVersion</Version>"
    Set-Content $ProjectFile -Value $content -NoNewline
    Write-Host "✓ $ProjectFile のバージョンを $NewVersion に更新しました" -ForegroundColor Green
}

# バージョンを+1する
function Increment-Version {
    param(
        [string]$Version,
        [string]$Type
    )
    
    $parts = $Version -split '\.'
    $major = [int]$parts[0]
    $minor = [int]$parts[1]
    $patch = [int]$parts[2]
    
    switch ($Type) {
        "major" {
            $major++
            $minor = 0
            $patch = 0
        }
        "minor" {
            $minor++
            $patch = 0
        }
        "patch" {
            $patch++
        }
    }
    
    return "$major.$minor.$patch"
}

# メイン処理
try {
    Write-Host "バージョンアップ処理を開始します..." -ForegroundColor Cyan
    Write-Host ""
    
    # 現在のバージョンを取得
    $currentVersion = Get-Version -ProjectFile $MainProject
    Write-Host "現在のバージョン: $currentVersion" -ForegroundColor Yellow
    
    # 新しいバージョンを計算
    $newVersion = Increment-Version -Version $currentVersion -Type $BumpType
    Write-Host "新しいバージョン: $newVersion ($BumpType bump)" -ForegroundColor Yellow
    Write-Host ""
    
    # 確認
    $confirm = Read-Host "このバージョンで続行しますか? (y/N)"
    if ($confirm -ne "y" -and $confirm -ne "Y") {
        Write-Host "処理をキャンセルしました" -ForegroundColor Red
        exit 0
    }
    
    # バージョンを更新
    Update-Version -ProjectFile $MainProject -NewVersion $newVersion
    Update-Version -ProjectFile $WebUIProject -NewVersion $newVersion
    
    Write-Host ""
    
    # Gitの状態を確認
    $gitStatus = git status --porcelain
    if ($gitStatus) {
        Write-Host "変更をコミットします..." -ForegroundColor Cyan
        git add $MainProject $WebUIProject
        git commit -m "Bump version to $newVersion"
        Write-Host "✓ コミット完了" -ForegroundColor Green
    }
    
    # タグを作成
    $tagName = "v$newVersion"
    Write-Host ""
    Write-Host "タグを作成します: $tagName" -ForegroundColor Cyan
    git tag -a $tagName -m "Release version $newVersion"
    Write-Host "✓ タグ作成完了" -ForegroundColor Green
    
    # プッシュ
    Write-Host ""
    $pushConfirm = Read-Host "コミットとタグをプッシュしますか? (y/N)"
    if ($pushConfirm -eq "y" -or $pushConfirm -eq "Y") {
        Write-Host "プッシュ中..." -ForegroundColor Cyan
        git push origin HEAD
        git push origin $tagName
        Write-Host "✓ プッシュ完了" -ForegroundColor Green
        Write-Host ""
        Write-Host "バージョン $newVersion のリリース準備が完了しました！" -ForegroundColor Green
    } else {
        Write-Host ""
        Write-Host "手動でプッシュしてください:" -ForegroundColor Yellow
        Write-Host "  git push origin HEAD" -ForegroundColor Yellow
        Write-Host "  git push origin $tagName" -ForegroundColor Yellow
    }
    
} catch {
    Write-Host ""
    Write-Host "エラーが発生しました: $_" -ForegroundColor Red
    exit 1
}
