# プライベートリポジトリでの管理ガイド

このライブラリをプライベートリポジトリで管理し、他のプロジェクトで使用する方法を説明します。

## 方法1: GitHub Packages（推奨）

GitHub Packagesはプライベートリポジトリでも使用できます。

### セットアップ手順

#### 1. プライベートリポジトリを作成

GitHubで新しいプライベートリポジトリを作成し、コードをプッシュします。

#### 2. Personal Access Token (PAT) を作成

1. GitHub → Settings → Developer settings → Personal access tokens → Tokens (classic)
2. 「Generate new token (classic)」をクリック
3. 以下のスコープを選択：
   - `read:packages` - パッケージを読み取る
   - `write:packages` - パッケージを公開する
   - `repo` - プライベートリポジトリにアクセスする場合（プライベートリポジトリの場合）
4. トークンを生成して保存

#### 3. パッケージを公開

リリースタグを作成すると、GitHub Actionsが自動的にパッケージを公開します：

```bash
git tag v1.0.0
git push origin v1.0.0
```

#### 4. 他のプロジェクトで使用

##### 4.1 nuget.config を作成

プロジェクトのルートに `nuget.config` を作成：

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="github" value="https://nuget.pkg.github.com/YOUR_USERNAME/index.json" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
```

##### 4.2 認証を設定

```bash
dotnet nuget add source https://nuget.pkg.github.com/YOUR_USERNAME/index.json `
  --name github `
  --username YOUR_USERNAME `
  --password YOUR_GITHUB_TOKEN `
  --store-password-in-clear-text
```

**注意**: 
- `YOUR_USERNAME` はGitHubのユーザー名
- `YOUR_GITHUB_TOKEN` は上記で作成したPersonal Access Token

##### 4.3 パッケージを追加

```bash
dotnet add package dnbn.net --version 1.0.0
```

## 方法2: ローカルNuGetパッケージ

プライベートリポジトリから直接パッケージを配布する方法です。

### セットアップ手順

#### 1. パッケージを作成

```bash
dotnet pack -c Release
```

#### 2. 共有方法

##### オプションA: ファイル共有

- ネットワーク共有フォルダに配置
- ファイルサーバーに配置
- メールやチャットで配布

##### オプションB: 内部NuGetサーバー

組織内にNuGetサーバーを構築する場合：

```bash
# NuGet.Serverをセットアップ
# または、Azure Artifacts、JFrog Artifactoryなどを使用
```

#### 3. 他のプロジェクトで使用

```bash
# ローカルNuGetソースに追加
dotnet nuget add source \\server\packages --name InternalPackages

# パッケージを追加
dotnet add package dnbn.net --version 1.0.0
```

## 方法3: Git Submodule（開発中のみ）

同じソリューション内で開発する場合：

```bash
# サブモジュールとして追加
git submodule add https://github.com/YOUR_USERNAME/dnbn.net.git libs/dnbn.net

# プロジェクト参照を追加
dotnet add reference libs/dnbn.net/dnbn.net.csproj
```

## 方法4: Azure Artifacts（組織向け）

Azure DevOpsを使用している場合：

1. Azure Artifactsフィードを作成
2. パッケージを公開
3. 他のプロジェクトで参照

詳細は [Azure Artifacts のドキュメント](https://docs.microsoft.com/azure/devops/artifacts/) を参照してください。

## セキュリティの考慮事項

### Personal Access Tokenの管理

- **推奨**: 環境変数やシークレット管理ツール（Azure Key Vault、AWS Secrets Managerなど）を使用
- **非推奨**: コードに直接書き込む、Gitにコミットする

### 例: 環境変数を使用

```bash
# Windows PowerShell
$env:NUGET_AUTH_TOKEN = "YOUR_GITHUB_TOKEN"
dotnet nuget add source https://nuget.pkg.github.com/YOUR_USERNAME/index.json `
  --name github `
  --username YOUR_USERNAME `
  --password $env:NUGET_AUTH_TOKEN `
  --store-password-in-clear-text
```

```bash
# Linux/Mac
export NUGET_AUTH_TOKEN="YOUR_GITHUB_TOKEN"
dotnet nuget add source https://nuget.pkg.github.com/YOUR_USERNAME/index.json \
  --name github \
  --username YOUR_USERNAME \
  --password $NUGET_AUTH_TOKEN \
  --store-password-in-clear-text
```

## チームメンバーへの共有

### GitHub Packagesの場合

1. 各メンバーがPersonal Access Tokenを作成
2. 各メンバーが認証を設定
3. パッケージを参照

### 組織アカウントの場合

GitHub Organizationを使用している場合、組織レベルのパッケージ管理が可能です。

## トラブルシューティング

### 認証エラーが発生する場合

1. Personal Access Tokenが正しく設定されているか確認
2. トークンのスコープが適切か確認（`read:packages`、`write:packages`、`repo`）
3. `nuget.config` の設定を確認

### パッケージが見つからない場合

1. パッケージ名が正しいか確認（`dnbn.net`）
2. バージョン番号が正しいか確認
3. NuGetソースが正しく設定されているか確認

```bash
# NuGetソースを確認
dotnet nuget list source
```

