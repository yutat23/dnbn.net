# dnbn.net ライブラリの使用方法

このライブラリを他のプロジェクトで使用する方法を説明します。

## 方法1: ローカルプロジェクト参照（開発中）

同じソリューション内や、ローカルのプロジェクト参照として使用する場合：

```xml
<ItemGroup>
  <ProjectReference Include="..\path\to\dnbn.net\dnbn.net.csproj" />
</ItemGroup>
```

または、dotnet CLIで：

```bash
dotnet add reference ../path/to/dnbn.net/dnbn.net.csproj
```

## 方法2: ローカルNuGetパッケージ

### 2.1 パッケージを作成

```bash
cd Z:\mywork\dnbn.net
dotnet pack -c Release
```

これにより、`bin/Release/dnbn.net.1.0.0.nupkg` が作成されます。

### 2.2 ローカルNuGetソースに追加

```bash
# ローカルNuGetソースを作成（初回のみ）
nuget sources add -Name "LocalPackages" -Source "Z:\mywork\packages"

# パッケージをコピー
mkdir Z:\mywork\packages
copy bin\Release\dnbn.net.1.0.0.nupkg Z:\mywork\packages\
```

### 2.3 プロジェクトで参照

プロジェクトの `.csproj` ファイルに追加：

```xml
<ItemGroup>
  <PackageReference Include="dnbn.net" Version="1.0.0" />
</ItemGroup>
```

または、`nuget.config` を作成：

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="LocalPackages" value="Z:\mywork\packages" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
```

## 方法3: GitHub Packages（プライベートリポジトリ対応・推奨）

### 3.1 GitHubにリポジトリを作成

1. GitHubで新しいリポジトリを作成（**プライベートでも可**）
2. コードをプッシュ

### 3.2 GitHub Actionsで自動パッケージ化

`.github/workflows/publish.yml` が既に作成されています。

### 3.3 Personal Access Token (PAT) の作成（プライベートリポジトリの場合）

プライベートリポジトリのパッケージにアクセスするには、PATが必要です：

1. GitHub → Settings → Developer settings → Personal access tokens → Tokens (classic)
2. 「Generate new token (classic)」をクリック
3. 以下のスコープを選択：
   - `read:packages` - パッケージを読み取る
   - `write:packages` - パッケージを公開する
   - `repo` - プライベートリポジトリにアクセスする場合（プライベートリポジトリの場合）
4. トークンを生成して保存

### 3.4 リリースタグを作成

```bash
git tag v1.0.0
git push origin v1.0.0
```

これにより、GitHub Actionsが自動的にパッケージを作成してGitHub Packagesに公開します。

### 3.5 他のプロジェクトで使用

`nuget.config` を作成：

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

**認証を設定**（プライベートパッケージの場合、必須）：

```bash
dotnet nuget add source https://nuget.pkg.github.com/YOUR_USERNAME/index.json `
  --name github `
  --username YOUR_USERNAME `
  --password YOUR_GITHUB_TOKEN `
  --store-password-in-clear-text
```

**注意**: `YOUR_GITHUB_TOKEN` は、上記で作成したPersonal Access Tokenです。

その後、パッケージを追加：

```bash
dotnet add package dnbn.net --version 1.0.0
```

詳細は [docs/PRIVATE_REPO.md](./docs/PRIVATE_REPO.md) を参照してください。

## 方法4: NuGet.orgに公開（一般公開）

### 4.1 NuGet.orgアカウントを作成

https://www.nuget.org/ でアカウントを作成

### 4.2 パッケージを作成

```bash
dotnet pack -c Release
```

### 4.3 公開

```bash
dotnet nuget push bin/Release/dnbn.net.1.0.0.nupkg --api-key YOUR_API_KEY --source https://api.nuget.org/v3/index.json
```

または、NuGet.orgのWebサイトからアップロード

### 4.4 他のプロジェクトで使用

```bash
dotnet add package dnbn.net
```

## 推奨される方法

- **開発中**: 方法1（プロジェクト参照）
- **チーム内共有**: 方法2（ローカルNuGetパッケージ）または方法3（GitHub Packages）
- **一般公開**: 方法4（NuGet.org）

## 注意事項

- GitHub Packagesを使用する場合、認証が必要です
- NuGet.orgに公開する場合、パッケージ名が一意である必要があります
- バージョン番号はセマンティックバージョニングに従うことを推奨します

