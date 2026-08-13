# Dnbn.Sample.NetFramework

[English](./README.md) | 日本語

.NET Framework 4.8 のコンソールアプリから dnbn.net（`netstandard2.0` ビルド）を使うサンプルです。

エコーサーバーとクライアントを同一プロセスで起動し、次の流れを実演します。

1. `TcpServer` の起動と `OnMessageReceivedAsync` によるエコー応答
2. `TcpClient` の接続(`ConnectionRetryPolicy` による自動再接続設定付き)
3. `SendAsync` によるリクエスト/レスポンス
4. サーバープッシュ電文の受信(`OnMessageReceived`)
5. アプリケーションレベル KeepAlive(2秒間隔の PING と応答観測)

コードは C# 7.3(.NET Framework プロジェクトの既定コンパイラ設定)の範囲で書かれているため、既存のレガシープロジェクトへそのまま持ち込めます。`await using` が使えないため、後片付けは `DisconnectAsync`/`StopAsync` の後に `Dispose` を呼ぶ形になります。

## 実行方法(Windows)

```bash
cd Samples/Dnbn.Sample.NetFramework
dotnet run
```

Visual Studio からは `Dnbn.Sample.NetFramework` をスタートアッププロジェクトに設定して実行してください。

Windows 以外の OS ではビルド(コンパイル検証)のみ可能です(`Microsoft.NETFramework.ReferenceAssemblies` パッケージ利用)。

## 自分のプロジェクトで使う場合

- SDK スタイル / PackageReference 形式のプロジェクトで `dotnet add package dnbn.net` を実行してください(net461 以降なら `netstandard2.0` ビルドが自動選択されます。4.7.2 以降を推奨)。
- 旧形式(packages.config)のプロジェクトの場合は、PackageReference 形式への移行を推奨します。
- 依存パッケージのバージョン差異解決のため、`AutoGenerateBindingRedirects` を有効にしてください(本サンプルの csproj を参照)。
