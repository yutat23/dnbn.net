# yutat23/dnbn.net リポジトリのセットアップ

## 初回セットアップ

### 1. GitHubリポジトリを作成

1. https://github.com/new にアクセス
2. 設定：
   - **Repository name**: `dnbn.net`
   - **Owner**: `yutat23`
   - **Visibility**: ✅ **Private**
   - **Initialize this repository with**: すべてチェックを外す
3. 「Create repository」をクリック

### 2. ローカルリポジトリを初期化

```powershell
# プロジェクトディレクトリに移動
cd Z:\mywork\dnbn.net

# Gitリポジトリは既に初期化済み
# リモートリポジトリを追加
git remote add origin https://github.com/yutat23/dnbn.net.git

# ファイルをステージング
git add .

# 初回コミット
git commit -m "Initial commit: TCP Messenger library"

# メインブランチを設定
git branch -M main

# プッシュ
git push -u origin main
```

### 3. Personal Access Tokenの作成

1. GitHub → Settings → Developer settings → Personal access tokens → Tokens (classic)
2. 「Generate new token (classic)」をクリック
3. 設定：
   - **Note**: `dnbn.net NuGet Package Access`
   - **Expiration**: 適切な期間を選択
   - **Select scopes**:
     - ✅ `read:packages`
     - ✅ `write:packages`
     - ✅ `repo`（プライベートリポジトリの場合）
4. 「Generate token」をクリック
5. **トークンをコピーして保存**

### 4. パッケージを公開

```powershell
# リリースタグを作成
git tag v1.0.0

# タグをプッシュ
git push origin v1.0.0
```

これで、GitHub Actionsが自動的にパッケージを作成してGitHub Packagesに公開します。

## 他のプロジェクトで使用

### 1. nuget.config を作成

プロジェクトのルートに `nuget.config` を作成：

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="github" value="https://nuget.pkg.github.com/yutat23/index.json" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
```

### 2. 認証を設定

```powershell
# 環境変数にトークンを設定（推奨）
$env:GITHUB_TOKEN = "YOUR_GITHUB_TOKEN"

# NuGetソースに追加
dotnet nuget add source https://nuget.pkg.github.com/yutat23/index.json `
  --name github `
  --username yutat23 `
  --password $env:GITHUB_TOKEN `
  --store-password-in-clear-text
```

### 3. パッケージを追加

```bash
dotnet add package dnbn.net --version 1.0.0
```

## バージョンアップ

### 1. バージョンを更新

`dnbn.net.csproj` の `<Version>` を更新：

```xml
<Version>1.0.1</Version>
```

### 2. コミットとタグ

```powershell
git add dnbn.net.csproj
git commit -m "Bump version to 1.0.1"
git tag v1.0.1
git push origin main
git push origin v1.0.1
```

## トラブルシューティング

### 認証エラー

- Personal Access Tokenが正しく設定されているか確認
- トークンのスコープが適切か確認（`read:packages`、`write:packages`、`repo`）

### パッケージが見つからない

```powershell
# NuGetソースを確認
dotnet nuget list source
```

