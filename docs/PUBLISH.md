# パッケージ公開ガイド

## ローカルNuGetパッケージの作成

### 1. パッケージを作成

```bash
dotnet pack -c Release
```

これにより、`bin/Release/dnbn.net.1.0.0.nupkg` が作成されます。

### 2. ローカルNuGetソースに追加

#### Windowsの場合

```powershell
# NuGetソースを追加（初回のみ）
nuget sources add -Name "LocalPackages" -Source "Z:\mywork\nuget-packages"

# パッケージをコピー
New-Item -ItemType Directory -Force -Path "Z:\mywork\nuget-packages"
Copy-Item "bin\Release\dnbn.net.1.0.0.nupkg" "Z:\mywork\nuget-packages\"
```

#### Linux/Macの場合

```bash
# NuGetソースを追加（初回のみ）
dotnet nuget add source ~/nuget-packages --name LocalPackages

# パッケージをコピー
mkdir -p ~/nuget-packages
cp bin/Release/dnbn.net.1.0.0.nupkg ~/nuget-packages/
```

### 3. プロジェクトで使用

プロジェクトのルートに `nuget.config` を作成：

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="LocalPackages" value="Z:\mywork\nuget-packages" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
```

その後、プロジェクトで参照：

```bash
dotnet add package dnbn.net --version 1.0.0
```

## GitHub Packagesに公開（プライベートリポジトリ対応）

### 1. GitHubリポジトリを作成

GitHubで新しい**プライベートリポジトリ**を作成し、コードをプッシュします。

**重要**: プライベートリポジトリでもGitHub Packagesは使用できます。

### 2. GitHub Actionsの設定

`.github/workflows/publish.yml` が既に作成されています。プライベートリポジトリでもそのまま使用できます。

### 3. Personal Access Token (PAT) の作成

プライベートリポジトリのパッケージにアクセスするには、PATが必要です：

1. GitHub → Settings → Developer settings → Personal access tokens → Tokens (classic)
2. 「Generate new token (classic)」をクリック
3. 以下のスコープを選択：
   - `read:packages` - パッケージを読み取る
   - `write:packages` - パッケージを公開する
   - `repo` - プライベートリポジトリにアクセスする場合
4. トークンを生成して保存（後で表示できません）

### 4. リリースタグを作成

```bash
git tag v1.0.0
git push origin v1.0.0
```

これにより、GitHub Actionsが自動的にパッケージを作成してGitHub Packagesに公開します。

### 5. 他のプロジェクトで使用（プライベートパッケージ）

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

**認証を設定**（プライベートパッケージの場合、必須）：

```bash
# Windowsの場合
dotnet nuget add source https://nuget.pkg.github.com/YOUR_USERNAME/index.json `
  --name github `
  --username YOUR_USERNAME `
  --password YOUR_GITHUB_TOKEN `
  --store-password-in-clear-text

# Linux/Macの場合
dotnet nuget add source https://nuget.pkg.github.com/YOUR_USERNAME/index.json \
  --name github \
  --username YOUR_USERNAME \
  --password YOUR_GITHUB_TOKEN \
  --store-password-in-clear-text
```

**注意**: `YOUR_GITHUB_TOKEN` は、上記で作成したPersonal Access Tokenです。

その後、パッケージを追加：

```bash
dotnet add package dnbn.net --version 1.0.0
```

### 6. CI/CDでの使用（GitHub Actions）

他のプロジェクトのGitHub Actionsで使用する場合、`GITHUB_TOKEN`を使用できます：

```yaml
- name: Setup NuGet authentication
  run: |
    dotnet nuget add source https://nuget.pkg.github.com/${{ github.repository_owner }}/index.json \
      --name github \
      --username ${{ github.repository_owner }} \
      --password ${{ secrets.GITHUB_TOKEN }} \
      --store-password-in-clear-text
```

### 7. チームメンバーへの共有

チームメンバーがプライベートパッケージを使用する場合：

1. 各メンバーがPersonal Access Tokenを作成
2. 上記の手順5で認証を設定
3. パッケージを参照

または、組織のGitHub Packagesを使用する場合、組織レベルの設定で管理できます。

## NuGet.orgに公開

### 1. NuGet.orgアカウントを作成

https://www.nuget.org/ でアカウントを作成します。

### 2. APIキーを取得

NuGet.orgのアカウント設定からAPIキーを生成します。

### 3. パッケージを作成

```bash
dotnet pack -c Release
```

### 4. 公開

```bash
dotnet nuget push bin/Release/dnbn.net.1.0.0.nupkg \
  --api-key YOUR_API_KEY \
  --source https://api.nuget.org/v3/index.json
```

### 5. 他のプロジェクトで使用

```bash
dotnet add package dnbn.net
```

## バージョン管理

セマンティックバージョニング（SemVer）に従ってバージョンを管理します：

- **MAJOR**: 互換性のない変更
- **MINOR**: 後方互換性のある機能追加
- **PATCH**: 後方互換性のあるバグ修正

例：`1.0.0` → `1.0.1` (パッチ) → `1.1.0` (マイナー) → `2.0.0` (メジャー)

