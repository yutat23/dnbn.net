# クイックスタート: yutat23/dnbn.net リポジトリのセットアップ

## 1. GitHubリポジトリを作成

1. https://github.com/new にアクセス
2. 以下の設定で作成：
   - **Repository name**: `dnbn.net`
   - **Owner**: `yutat23`
   - **Visibility**: ✅ Private
   - **Initialize this repository with**: すべてチェックを外す
3. 「Create repository」をクリック

## 2. ローカルリポジトリを初期化してプッシュ

```powershell
# プロジェクトディレクトリに移動
cd Z:\mywork\dnbn.net

# Gitリポジトリを初期化
git init

# すべてのファイルを追加
git add .

# 初回コミット
git commit -m "Initial commit: TCP Messenger library"

# メインブランチを設定
git branch -M main

# リモートリポジトリを追加
git remote add origin https://github.com/yutat23/dnbn.net.git

# プッシュ
git push -u origin main
```

## 3. Personal Access Tokenの作成

1. GitHub → Settings → Developer settings → Personal access tokens → Tokens (classic)
2. 「Generate new token (classic)」をクリック
3. スコープを選択：
   - ✅ `read:packages`
   - ✅ `write:packages`
   - ✅ `repo`（プライベートリポジトリの場合）
4. トークンを生成して保存

## 4. パッケージを公開

```powershell
# リリースタグを作成
git tag v1.0.0
git push origin v1.0.0
```

これで、GitHub Actionsが自動的にパッケージを作成してGitHub Packagesに公開します。

## 5. 他のプロジェクトで使用

### nuget.config を作成

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

### 認証を設定

```powershell
$env:GITHUB_TOKEN = "YOUR_GITHUB_TOKEN"
dotnet nuget add source https://nuget.pkg.github.com/yutat23/index.json `
  --name github `
  --username yutat23 `
  --password $env:GITHUB_TOKEN `
  --store-password-in-clear-text
```

### パッケージを追加

```bash
dotnet add package dnbn.net --version 1.0.0
```

詳細は [docs/SETUP_GITHUB.md](./docs/SETUP_GITHUB.md) を参照してください。

