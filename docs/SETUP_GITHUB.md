# GitHubリポジトリセットアップガイド

`yutat23/dnbn.net` リポジトリでの管理手順です。

## 1. GitHubリポジトリの作成

### 1.1 リポジトリを作成

1. GitHubにログイン
2. 右上の「+」→「New repository」をクリック
3. 以下の設定で作成：
   - **Repository name**: `dnbn.net`
   - **Owner**: `yutat23`
   - **Visibility**: Private（プライベートリポジトリ）
   - **Initialize this repository with**: チェックを外す（既存のコードがあるため）

### 1.2 ローカルリポジトリを初期化

```bash
cd Z:\mywork\dnbn.net

# Gitリポジトリを初期化（まだ初期化していない場合）
git init

# .gitignoreを作成（必要に応じて）
echo "bin/" > .gitignore
echo "obj/" >> .gitignore
echo "*.user" >> .gitignore
echo ".vs/" >> .gitignore

# ファイルをステージング
git add .

# 初回コミット
git commit -m "Initial commit: TCP Messenger library"

# リモートリポジトリを追加
git remote add origin https://github.com/yutat23/dnbn.net.git

# メインブランチを設定
git branch -M main

# プッシュ
git push -u origin main
```

## 2. Personal Access Token (PAT) の作成

### 2.1 PATを作成

1. GitHub → Settings → Developer settings → Personal access tokens → Tokens (classic)
2. 「Generate new token (classic)」をクリック
3. 以下の設定：
   - **Note**: `dnbn.net NuGet Package Access`
   - **Expiration**: 適切な期間を選択（例：90日、1年）
   - **Select scopes**:
     - ✅ `read:packages` - パッケージを読み取る
     - ✅ `write:packages` - パッケージを公開する
     - ✅ `repo` - プライベートリポジトリにアクセスする場合
4. 「Generate token」をクリック
5. **トークンをコピーして保存**（後で表示できません）

### 2.2 トークンの管理

セキュリティのため、環境変数やシークレット管理ツールを使用することを推奨します。

#### Windows PowerShellの場合

```powershell
# ユーザー環境変数に設定（永続的）
[System.Environment]::SetEnvironmentVariable('GITHUB_TOKEN', 'YOUR_TOKEN', 'User')

# 現在のセッションで使用
$env:GITHUB_TOKEN = 'YOUR_TOKEN'
```

#### Linux/Macの場合

```bash
# ~/.bashrc または ~/.zshrc に追加
export GITHUB_TOKEN='YOUR_TOKEN'
```

## 3. パッケージの公開

### 3.1 リリースタグを作成

```bash
# バージョン1.0.0のタグを作成
git tag v1.0.0

# タグをプッシュ
git push origin v1.0.0
```

これにより、GitHub Actionsが自動的にパッケージを作成してGitHub Packagesに公開します。

### 3.2 手動でパッケージを作成する場合

```bash
# パッケージを作成
dotnet pack -c Release

# GitHub Packagesに公開
dotnet nuget push bin/Release/dnbn.net.1.0.0.nupkg `
  --source https://nuget.pkg.github.com/yutat23/index.json `
  --api-key $env:GITHUB_TOKEN
```

## 4. 他のプロジェクトで使用

### 4.1 nuget.config を作成

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

### 4.2 認証を設定

#### Windows PowerShellの場合

```powershell
dotnet nuget add source https://nuget.pkg.github.com/yutat23/index.json `
  --name github `
  --username yutat23 `
  --password $env:GITHUB_TOKEN `
  --store-password-in-clear-text
```

#### Linux/Macの場合

```bash
dotnet nuget add source https://nuget.pkg.github.com/yutat23/index.json \
  --name github \
  --username yutat23 \
  --password $GITHUB_TOKEN \
  --store-password-in-clear-text
```

**注意**: 環境変数が設定されていない場合は、直接トークンを指定することもできます（セキュリティに注意）。

### 4.3 パッケージを追加

```bash
dotnet add package dnbn.net --version 1.0.0
```

## 5. バージョンアップの手順

### 5.1 バージョンを更新

`dnbn.net.csproj` の `<Version>` を更新：

```xml
<Version>1.0.1</Version>  <!-- パッチバージョンアップ -->
```

### 5.2 コミットとタグ

```bash
git add dnbn.net.csproj
git commit -m "Bump version to 1.0.1"
git tag v1.0.1
git push origin main
git push origin v1.0.1
```

## 6. チームメンバーへの共有

### 6.1 リポジトリへのアクセス権限

1. GitHubリポジトリの Settings → Collaborators
2. 「Add people」をクリック
3. チームメンバーのGitHubユーザー名を追加

### 6.2 パッケージへのアクセス権限

各メンバーが以下を実行：

1. Personal Access Tokenを作成（上記手順2.1を参照）
2. 認証を設定（上記手順4.2を参照）
3. パッケージを使用

## 7. CI/CDでの使用

### 7.1 GitHub Actionsで使用

他のプロジェクトのGitHub Actionsで使用する場合：

```yaml
- name: Setup NuGet authentication
  run: |
    dotnet nuget add source https://nuget.pkg.github.com/yutat23/index.json \
      --name github \
      --username yutat23 \
      --password ${{ secrets.GITHUB_TOKEN }} \
      --store-password-in-clear-text

- name: Restore packages
  run: dotnet restore
```

**注意**: 同じ組織内のリポジトリの場合、`GITHUB_TOKEN`が自動的に使用できます。
他の組織のリポジトリの場合、リポジトリのSecretsにPersonal Access Tokenを設定する必要があります。

## 8. トラブルシューティング

### 認証エラーが発生する場合

1. Personal Access Tokenが正しく設定されているか確認
2. トークンのスコープが適切か確認（`read:packages`、`write:packages`、`repo`）
3. `nuget.config` の設定を確認

```bash
# NuGetソースを確認
dotnet nuget list source
```

### パッケージが見つからない場合

1. パッケージ名が正しいか確認（`dnbn.net`）
2. バージョン番号が正しいか確認
3. リポジトリが正しく公開されているか確認（GitHubのPackagesページで確認）

### 403 Forbiddenエラーが発生する場合

- Personal Access Tokenのスコープが不足している可能性があります
- トークンを再生成して、必要なスコープをすべて選択してください

