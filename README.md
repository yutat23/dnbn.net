# dnbn.net - TCP独自電文ライブラリ
![image](https://img.shields.io/badge/language-C%23-green?logo)
![image](https://img.shields.io/badge/dotnet-10.0-blue?logo=dotnet)
[![NuGet version (Dnbn.net)](https://img.shields.io/nuget/v/dnbn.net?link=https%3A%2F%2Fwww.nuget.org%2Fpackages%2Fdnbn.net
)](https://www.nuget.org/packages/dnbn.net/)

.NET 10+ 対応のTCPメッセージ送受信ライブラリです。

## 目次

- [主な機能](#主な機能)
- [インストール](#インストール)
- [クイックスタート](#クイックスタート)
- [設定リファレンス](#設定リファレンス)
  - [サーバー設定 (ServerConfig)](#サーバー設定-serverconfig)
  - [クライアント設定 (ClientConfig)](#クライアント設定-clientconfig)
  - [リトライポリシー (RetryPolicy)](#リトライポリシー-retrypolicy)
  - [キープアライブ設定 (KeepAliveConfig)](#キープアライブ設定-keepaliveconfig)
  - [Web UI設定 (WebUIConfig)](#web-ui設定-webuiconfig)
- [メッセージプロトコル](#メッセージプロトコル)
  - [終端文字方式](#終端文字方式)
  - [固定長プロトコル](#固定長プロトコル)
  - [可変長プロトコル](#可変長プロトコル)
  - [プロトコル選択ガイド](#プロトコル選択ガイド)
- [APIリファレンス](#apiリファレンス)
  - [ITcpClient](#itcpclient)
  - [ITcpServer](#itcpserver)
  - [ITcpMessengerFactory](#itcpmessengerfactory)
  - [Message](#message)
  - [SessionInfo](#sessioninfo)
- [使用例](#使用例)
  - [基本的なサーバー/クライアント通信](#基本的なサーバークライアント通信)
  - [Promise的チェーン処理](#promise的チェーン処理)
  - [Observableパターン](#observableパターン)
  - [フィルターパイプライン](#フィルターパイプライン)
  - [接続リトライ機能](#接続リトライ機能)
  - [キープアライブ機能](#キープアライブ機能)
  - [動的設定変更機能](#動的設定変更機能)
  - [セッション管理](#セッション管理)
  - [エラーハンドリング](#エラーハンドリング)
- [Web UI機能](#web-ui機能)
- [ログ機能](#ログ機能)
  - [log4netとの統合](#log4netとの統合)
  - [ログレベル](#ログレベル)
- [トラブルシューティング](#トラブルシューティング)
- [サンプルプロジェクト](#サンプルプロジェクト)

## 主な機能

- 固定長/可変長/終端文字など柔軟な電文プロトコル対応
- サーバーとクライアントを同一アプリ内で併用可能
- 長期接続を前提とした安定したTCP通信
- メッセージ受信イベント（IObservable/イベントデリゲート対応）
- 送信後の応答待ち（Promise的制御）
- リトライポリシー、フィルターパイプライン対応
- 接続リトライ機能（接続失敗時およびNW障害時の自動再接続、無限リトライ対応）
- キープアライブ機能（定期的なメッセージ送信で接続維持）
- 動的設定変更機能（実行時にKeepAlive、タイムアウト、リトライポリシーなどを変更可能）
- 複数クライアントを同一ポートで受信可能
- appsettings.json統合設定
- Web UI機能（TCP Messengerの状態をWebブラウザでリアルタイム表示、SSEによる自動更新）

## インストール

このパッケージは **NuGet.org** から配布されています。以下のコマンドでインストールできます：

```bash
dotnet add package dnbn.net
```

## クイックスタート

### 1. appsettings.json に設定を追加

```json
{
  "dnbn.net": {
    "Servers": [
      {
        "Name": "MainServer",
        "ListenPort": 5000,
        "Encoding": "UTF-8",
        "MessageTerminator": "\r",
        "ClientIdentification": "SourceEndpoint"
      }
    ],
    "Clients": [
      {
        "Name": "ControllerA",
        "RemoteHost": "192.168.1.10",
        "RemotePort": 7000,
        "RetryPolicy": {
          "MaxRetryCount": 3,
          "RetryDelayStrategy": "Exponential",
          "InitialDelayMs": 500,
          "MaxDelayMs": 30000,
          "FailOnTimeout": true,
          "FailOnErrorResponse": true
        },
        "ConnectionRetryPolicy": {
          "MaxRetryCount": -1,
          "RetryDelayStrategy": "Exponential",
          "InitialDelayMs": 1000,
          "MaxDelayMs": 60000
        },
        "TimeoutMilliseconds": 5000,
        "KeepAlive": {
          "Enabled": true,
          "IntervalSeconds": 30,
          "Message": "PING\r"
        }
      }
    ]
  }
}
```

**注**: 設定キー名として `"dnbn.net"` を推奨します。後方互換性のため、`"TcpMessenger"` も引き続きサポートされています。両方のキーが存在する場合、`"dnbn.net"` が優先されます。

### 2. サービスを登録

```csharp
using Dnbn.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .Build();

var services = new ServiceCollection();
services.AddLogging(builder => builder.AddConsole());
services.AddTcpMessenger(configuration);

var serviceProvider = services.BuildServiceProvider();
```

### 3. サーバーを使用

```csharp
using Dnbn.Core;
using Dnbn.Models;

var factory = serviceProvider.GetRequiredService<ITcpMessengerFactory>();

// サーバーを作成して起動
var server = factory.CreateServer("MainServer");
server.OnMessageReceived += (sender, args) =>
{
    var (message, sessionInfo) = args;
    Console.WriteLine($"Received: {message.Text} from {sessionInfo.SessionId}");
    
    // 応答を送信
    var response = Message.FromString("OK\r", System.Text.Encoding.UTF8);
    server.SendAsync(sessionInfo.SessionId, response).Wait();
};
server.OnClientConnected += (sender, sessionInfo) =>
{
    Console.WriteLine($"Client connected: {sessionInfo.SessionId}");
};
server.OnClientDisconnected += (sender, sessionInfo) =>
{
    Console.WriteLine($"Client disconnected: {sessionInfo.SessionId}");
};

await server.StartAsync();
```

### 4. クライアントを使用

```csharp
// クライアントを作成して接続
var client = factory.CreateClient("ControllerA");
client.OnMessageReceived += (sender, message) =>
{
    Console.WriteLine($"Received: {message.Text}");
};
client.OnConnected += (sender, args) =>
{
    Console.WriteLine("Connected to server");
};
client.OnDisconnected += (sender, args) =>
{
    Console.WriteLine("Disconnected from server");
};

// キープアライブ応答イベントの処理
client.OnKeepAliveResponseReceived += (sender, message) =>
{
    Console.WriteLine($"Keep-alive response: {message.Text}");
    // 状態取得コマンドの応答を使用して処理を行う例
};

await client.ConnectAsync();

// メッセージを送信
var msg = Message.FromString("HELLO\r", System.Text.Encoding.UTF8);
await client.SendAsync(msg);

// 応答を待つ
var response = await client.SendAndWaitAsync(
    msg,
    m => m.Code == "OK" || m.Text?.StartsWith("OK") == true,
    TimeSpan.FromSeconds(3)
);

Console.WriteLine($"Response: {response.Text}");
```

## 設定リファレンス

### サーバー設定 (ServerConfig)

サーバー側の設定項目です。`dnbn.net.Servers`配列に設定します（`TcpMessenger.Servers`も引き続きサポートされています）。

| プロパティ | 型 | 必須 | デフォルト値 | 説明 |
|-----------|-----|------|------------|------|
| `Name` | `string` | ✓ | `""` | サーバー名（設定識別用）。`CreateServer()`で使用します |
| `ListenPort` | `int` | ✓ | - | リッスンポート番号 |
| `Encoding` | `string` | - | `"UTF-8"` | 文字エンコーディング（`"UTF-8"`, `"Shift-JIS"`など） |
| `MessageTerminator` | `string?` | - | `null` | メッセージ終端文字（`"\r"`, `"\r\n"`, `"\n"`など）。送信時に使用されます。終端文字方式を使用する場合に設定 |
| `ReceiveMessageTerminator` | `string[]?` | - | `null` | 受信時のメッセージ終端文字の配列（複数の候補をサポート）。未設定の場合は`MessageTerminator`を使用。例：`["#", "?"]`（正常応答「#」、異常応答「?」） |
| `ClientIdentification` | `ClientIdentification` | - | `SourceEndpoint` | クライアント識別方式。`SourceEndpoint`（送信元エンドポイント）または`HeaderBased`（ヘッダベース） |
| `FixedHeaderLength` | `int?` | - | `null` | 固定長ヘッダサイズ（バイト）。固定長/可変長プロトコルで使用 |
| `FixedBodyLength` | `int?` | - | `null` | 固定長ボディサイズ（バイト）。固定長プロトコルで使用 |
| `LengthFieldOffset` | `int?` | - | `null` | 可変長ボディの場合のヘッダ内長さフィールドの位置（バイト）。可変長プロトコルで使用 |
| `LengthFieldLength` | `int?` | - | `null` | 可変長ボディの場合のヘッダ内長さフィールドのサイズ（バイト）。1, 2, 4バイトをサポート |
| `EnableMessageLogging` | `bool` | - | `false` | メッセージ送受信時のログ出力を有効にするかどうか。`true`に設定すると、DEBUGレベルでメッセージの送受信がログ出力されます |

**設定例**:

```json
{
  "Name": "MainServer",
  "ListenPort": 5000,
  "Encoding": "UTF-8",
  "MessageTerminator": "\r\n",
  "ReceiveMessageTerminator": ["\r"],
  "ClientIdentification": "SourceEndpoint",
  "EnableMessageLogging": true
}
```

### クライアント設定 (ClientConfig)

クライアント側の設定項目です。`dnbn.net.Clients`配列に設定します（`TcpMessenger.Clients`も引き続きサポートされています）。

| プロパティ | 型 | 必須 | デフォルト値 | 説明 |
|-----------|-----|------|------------|------|
| `Name` | `string` | ✓ | `""` | クライアント名（設定識別用）。`CreateClient()`で使用します |
| `RemoteHost` | `string` | ✓ | `""` | リモートホスト（IPアドレスまたはホスト名） |
| `RemotePort` | `int` | ✓ | - | リモートポート番号 |
| `Encoding` | `string` | - | `"UTF-8"` | 文字エンコーディング（`"UTF-8"`, `"Shift-JIS"`など） |
| `MessageTerminator` | `string?` | - | `null` | メッセージ終端文字（`"\r"`, `"\r\n"`, `"\n"`など）。送信時に使用されます。終端文字方式を使用する場合に設定 |
| `ReceiveMessageTerminator` | `string[]?` | - | `null` | 受信時のメッセージ終端文字の配列（複数の候補をサポート）。未設定の場合は`MessageTerminator`を使用。例：`["#", "?"]`（正常応答「#」、異常応答「?」） |
| `TimeoutMilliseconds` | `int` | - | `5000` | タイムアウト（ミリ秒）。メッセージ送信時のデフォルトタイムアウト |
| `RetryPolicy` | `RetryPolicy?` | - | `null` | リトライポリシー（メッセージ送信用）。詳細は[リトライポリシー](#リトライポリシー-retrypolicy)を参照 |
| `ConnectionRetryPolicy` | `RetryPolicy?` | - | `null` | 接続リトライポリシー（接続失敗時およびNW障害時の自動再接続用）。詳細は[リトライポリシー](#リトライポリシー-retrypolicy)を参照 |
| `KeepAlive` | `KeepAliveConfig?` | - | `null` | キープアライブ設定。詳細は[キープアライブ設定](#キープアライブ設定-keepaliveconfig)を参照 |
| `FixedHeaderLength` | `int?` | - | `null` | 固定長ヘッダサイズ（バイト）。固定長/可変長プロトコルで使用 |
| `FixedBodyLength` | `int?` | - | `null` | 固定長ボディサイズ（バイト）。固定長プロトコルで使用 |
| `LengthFieldOffset` | `int?` | - | `null` | 可変長ボディの場合のヘッダ内長さフィールドの位置（バイト）。可変長プロトコルで使用 |
| `LengthFieldLength` | `int?` | - | `null` | 可変長ボディの場合のヘッダ内長さフィールドのサイズ（バイト）。1, 2, 4バイトをサポート |
| `EnableMessageLogging` | `bool` | - | `false` | メッセージ送受信時のログ出力を有効にするかどうか。`true`に設定すると、DEBUGレベルでメッセージの送受信がログ出力されます |

**設定例**:

```json
{
  "Name": "ControllerA",
  "RemoteHost": "192.168.1.10",
  "RemotePort": 7000,
  "Encoding": "UTF-8",
  "MessageTerminator": "\r",
  "ReceiveMessageTerminator": ["\r\n"],
  "TimeoutMilliseconds": 5000,
  "EnableMessageLogging": true,
  "RetryPolicy": {
    "MaxRetryCount": 3,
    "RetryDelayStrategy": "Exponential",
    "InitialDelayMs": 500,
    "MaxDelayMs": 30000,
    "FailOnTimeout": true,
    "FailOnErrorResponse": true
  },
  "ConnectionRetryPolicy": {
    "MaxRetryCount": -1,
    "RetryDelayStrategy": "Exponential",
    "InitialDelayMs": 1000,
    "MaxDelayMs": 60000
  },
  "KeepAlive": {
    "Enabled": true,
    "IntervalSeconds": 30,
    "Message": "PING\r"
  }
}
```

### リトライポリシー (RetryPolicy)

メッセージ送信時や接続時のリトライ動作を制御します。

| プロパティ | 型 | 必須 | デフォルト値 | 説明 |
|-----------|-----|------|------------|------|
| `MaxRetryCount` | `int` | - | `3` | 最大リトライ回数。`-1`で無限リトライ（接続リトライのみ） |
| `RetryDelayStrategy` | `RetryDelayStrategy` | - | `Exponential` | リトライ遅延戦略。`Fixed`（固定遅延）または`Exponential`（指数バックオフ） |
| `InitialDelayMs` | `int` | - | `500` | 初期待機時間（ミリ秒） |
| `MaxDelayMs` | `int` | - | `60000` | 最大待機時間（ミリ秒）。指数バックオフ時の上限値 |
| `FailOnTimeout` | `bool` | - | `true` | タイムアウト時に失敗とするか（メッセージ送信リトライのみ） |
| `FailOnErrorResponse` | `bool` | - | `true` | エラー応答時に失敗とするか（メッセージ送信リトライのみ） |

**リトライ遅延の計算**:

- **Fixed（固定遅延）**: 常に`InitialDelayMs`の待機時間
- **Exponential（指数バックオフ）**: `InitialDelayMs * 2^retryCount`で計算され、`MaxDelayMs`を上限として適用

**指数バックオフの例**（`InitialDelayMs = 1000`, `MaxDelayMs = 60000`）:
- 1回目: 1秒
- 2回目: 2秒
- 3回目: 4秒
- 4回目: 8秒
- 5回目: 16秒
- 6回目: 32秒
- 7回目以降: 60秒（`MaxDelayMs`で上限）

**設定例**:

```json
{
  "RetryPolicy": {
    "MaxRetryCount": 3,
    "RetryDelayStrategy": "Exponential",
    "InitialDelayMs": 500,
    "MaxDelayMs": 30000,
    "FailOnTimeout": true,
    "FailOnErrorResponse": true
  },
  "ConnectionRetryPolicy": {
    "MaxRetryCount": -1,
    "RetryDelayStrategy": "Exponential",
    "InitialDelayMs": 1000,
    "MaxDelayMs": 60000
  }
}
```

### キープアライブ設定 (KeepAliveConfig)

定期的なメッセージ送信で接続を維持する設定です。

| プロパティ | 型 | 必須 | デフォルト値 | 説明 |
|-----------|-----|------|------------|------|
| `Enabled` | `bool` | - | `false` | キープアライブを有効にするか |
| `IntervalSeconds` | `int` | - | `30` | キープアライブ間隔（秒） |
| `Message` | `string` | - | `""` | キープアライブメッセージ。終端文字を含める必要があります |

**設定例**:

```json
{
  "KeepAlive": {
    "Enabled": true,
    "IntervalSeconds": 30,
    "Message": "PING\r"
  }
}
```

**動作**:

- キープアライブが有効な場合、指定された間隔で自動的にメッセージを送信します
- キープアライブメッセージの応答は`OnKeepAliveResponseReceived`イベントで受け取れます
- 応答がタイムアウトした場合、WARNレベルのログが出力されます

### Web UI設定 (WebUIConfig)

TCP Messengerの状態をWebブラウザで表示するための設定です。`dnbn.net.WebUI`に設定します（`TcpMessenger.WebUI`も引き続きサポートされています）。

| プロパティ | 型 | 必須 | デフォルト値 | 説明 |
|-----------|-----|------|------------|------|
| `Enabled` | `bool` | - | `false` | Web UIを有効にするか |
| `Port` | `int` | - | `8080` | Web UIのポート番号 |
| `UpdateIntervalSeconds` | `int` | - | `1` | SSEストリームの送信間隔（秒） |
| `BindAddress` | `string` | - | `"localhost"` | バインドアドレス（`"*"`で全アドレス） |
| `EnableLogging` | `bool` | - | `true` | Web UI関連のログ出力の有効化/無効化 |

**設定例**:

```json
{
  "dnbn.net": {
    "WebUI": {
      "Enabled": true,
      "Port": 8080,
      "UpdateIntervalSeconds": 1,
      "BindAddress": "localhost",
      "EnableLogging": true
    }
  }
}
```

**注**: `"TcpMessenger"` キーも引き続きサポートされています。

**動作**:

- Web UIが有効な場合、指定されたポートでHTTPサーバーが起動します
- ブラウザで `http://localhost:8080` にアクセスすると、TCP Messengerの状態が表示されます
- SSE（Server-Sent Events）により、接続状態やメッセージ送受信数などがリアルタイムで更新されます
- REST APIエンドポイント（`/api/status`、`/api/status/client`、`/api/status/server`、`/api/health`）も利用可能です

## メッセージプロトコル

このライブラリは、以下の3つのメッセージプロトコル方式をサポートしています。

### 終端文字方式

メッセージの終端を特定の文字列で識別する方式です。最もシンプルで、テキストベースのプロトコルに適しています。

**設定方法**:

```json
{
  "MessageTerminator": "\r\n"
}
```

**特徴**:
- メッセージ長が可変
- 設定が簡単
- テキストプロトコルに最適
- バイナリデータには不向き（終端文字がデータに含まれる可能性がある）
- 送信時と受信時で異なる終端文字を指定可能
- 受信時に複数の終端文字候補を指定可能

**使用例**:

**基本的な使用例**:

```json
{
  "Name": "TextServer",
  "ListenPort": 5000,
  "Encoding": "UTF-8",
  "MessageTerminator": "\r\n"
}
```

**送信時と受信時で異なる終端文字を使用する例**:

```json
{
  "Name": "TextServer",
  "ListenPort": 5000,
  "Encoding": "UTF-8",
  "MessageTerminator": "\r\n",  // 送信用
  "ReceiveMessageTerminator": ["\r"]  // 受信用
}
```

**複数の終端文字候補を指定する例**（正常応答「#」、異常応答「?」など）:

```json
{
  "Name": "DeviceController",
  "RemoteHost": "192.168.1.10",
  "RemotePort": 7000,
  "Encoding": "UTF-8",
  "MessageTerminator": "\r",  // 送信用
  "ReceiveMessageTerminator": ["#", "?"]  // 受信用：正常応答「#」、異常応答「?」
}
```

### 固定長プロトコル

ヘッダとボディが固定長のプロトコルです。固定長の電文を扱う場合に使用します。

**設定方法**:

```json
{
  "FixedHeaderLength": 10,
  "FixedBodyLength": 100
}
```

**特徴**:
- メッセージ長が固定（ヘッダ + ボディ）
- パースが高速
- 固定長の電文プロトコルに最適
- 可変長データには不向き

**使用例**:

```json
{
  "Name": "FixedLengthServer",
  "ListenPort": 5000,
  "Encoding": "UTF-8",
  "FixedHeaderLength": 10,
  "FixedBodyLength": 100
}
```

この場合、1メッセージは110バイト（10バイトのヘッダ + 100バイトのボディ）として処理されます。

### 可変長プロトコル

ヘッダにボディ長が含まれる可変長プロトコルです。ヘッダは固定長、ボディは可変長です。

**設定方法**:

```json
{
  "FixedHeaderLength": 10,
  "LengthFieldOffset": 2,
  "LengthFieldLength": 4
}
```

**特徴**:
- ヘッダは固定長、ボディは可変長
- 効率的なデータ転送が可能
- 多くのプロトコルで採用されている方式
- 長さフィールドの位置とサイズを正確に設定する必要がある

**パラメータ説明**:
- `FixedHeaderLength`: ヘッダの固定長（バイト）
- `LengthFieldOffset`: ヘッダ内の長さフィールドの位置（バイト、0始まり）
- `LengthFieldLength`: 長さフィールドのサイズ（バイト）。1, 2, 4バイトをサポート

**使用例**:

```json
{
  "Name": "VariableLengthServer",
  "ListenPort": 5000,
  "Encoding": "UTF-8",
  "FixedHeaderLength": 10,
  "LengthFieldOffset": 2,
  "LengthFieldLength": 4
}
```

この場合、ヘッダの2バイト目から4バイト（2-5バイト目）にボディ長が格納されていると解釈されます。長さフィールドは**Big-endian（ネットワークバイトオーダー）**として解釈されます。

**電文構造の例**:

```
[ヘッダ(10バイト)] [ボディ(可変長)]
  ↑
  2-5バイト目にボディ長（4バイト、Big-endian）
```

### プロトコル選択ガイド

| 方式 | 適用ケース | メリット | デメリット |
|------|-----------|---------|-----------|
| 終端文字 | テキストベースのプロトコル、シンプルな通信 | 設定が簡単、可変長対応 | バイナリデータに不向き |
| 固定長 | 固定長の電文プロトコル | パースが高速、シンプル | 可変長データに対応不可 |
| 可変長 | 効率的な可変長データ転送が必要 | 柔軟性が高い、効率的 | 設定がやや複雑 |

## APIリファレンス

### ITcpClient

TCPクライアントのインターフェイスです。

#### プロパティ

| プロパティ | 型 | 説明 |
|-----------|-----|------|
| `Name` | `string` | クライアント名 |
| `IsConnected` | `bool` | 接続状態 |
| `ConnectionInfo` | `ClientConnectionInfo` | 接続状態情報（詳細は[ClientConnectionInfo](#clientconnectioninfo)を参照） |
| `KeepAlive` | `KeepAliveConfig?` | KeepAlive設定の取得・設定（実行時に変更可能） |
| `TimeoutMilliseconds` | `int` | タイムアウト設定の取得・設定（ミリ秒、実行時に変更可能） |
| `RetryPolicy` | `RetryPolicy?` | リトライポリシーの取得・設定（実行時に変更可能） |
| `ConnectionRetryPolicy` | `RetryPolicy?` | 接続リトライポリシーの取得・設定（実行時に変更可能） |

#### イベント

| イベント | 型 | 説明 |
|---------|-----|------|
| `OnMessageReceived` | `EventHandler<Message>` | メッセージ受信イベント |
| `OnConnected` | `EventHandler` | 接続イベント |
| `OnDisconnected` | `EventHandler` | 切断イベント |
| `OnError` | `EventHandler<Exception>` | エラーイベント |
| `OnKeepAliveResponseReceived` | `EventHandler<Message>` | キープアライブ応答受信イベント |

#### メソッド

##### `Task ConnectAsync()`

サーバーに接続します。`ConnectionRetryPolicy`が設定されている場合、接続失敗時に自動的にリトライします。

```csharp
await client.ConnectAsync();
```

##### `Task DisconnectAsync(bool isIntentional = true)`

サーバーから切断します。

- `isIntentional`: 意図的な切断かどうか。`true`の場合、自動再接続は行われません。

```csharp
await client.DisconnectAsync(true);
```

##### `Task SendAsync(Message message)`

メッセージを送信します。応答は待ちません。応答は`OnMessageReceived`イベントで受け取ります。

```csharp
var msg = Message.FromString("HELLO\r", System.Text.Encoding.UTF8);
await client.SendAsync(msg);
```

##### `Task<Message> SendAsync(Message message, TimeSpan timeout)`

メッセージを送信して応答を待ちます。簡易版で、すべての応答を受け入れます。

```csharp
var msg = Message.FromString("HELLO\r", System.Text.Encoding.UTF8);
var response = await client.SendAsync(msg, TimeSpan.FromSeconds(5));
Console.WriteLine($"Response: {response.Text}");
```

**注意**: このメソッドを使用した場合、応答は`OnMessageReceived`イベントでは発行されません（キューイング方式）。

##### `Task<Message> SendAndWaitAsync(Message requestMessage, Func<Message, bool> responsePredicate, TimeSpan timeout)`

メッセージを送信して、条件に一致する応答を待ちます。

- `requestMessage`: 送信するメッセージ
- `responsePredicate`: 応答の条件（`true`を返す応答を受け入れる）
- `timeout`: タイムアウト時間

```csharp
var msg = Message.FromString("HELLO\r", System.Text.Encoding.UTF8);
var response = await client.SendAndWaitAsync(
    msg,
    m => m.Code == "OK" || m.Text?.StartsWith("OK") == true,
    TimeSpan.FromSeconds(3)
);
```

**注意**: このメソッドを使用した場合、応答は`OnMessageReceived`イベントでは発行されません（キューイング方式）。

#### 動的設定変更

実行時に設定を変更できます。接続中でも変更可能です。

**KeepAlive設定の変更**:

```csharp
// KeepAliveを有効化
client.KeepAlive = new KeepAliveConfig
{
    Enabled = true,
    IntervalSeconds = 10,
    Message = "PING\r"
};

// KeepAliveを無効化
client.KeepAlive = new KeepAliveConfig { Enabled = false };
```

**タイムアウト設定の変更**:

```csharp
// タイムアウトを10秒に変更
client.TimeoutMilliseconds = 10000;
```

**リトライポリシーの変更**:

```csharp
// リトライポリシーを設定
client.RetryPolicy = new RetryPolicy
{
    MaxRetryCount = 5,
    RetryDelayStrategy = RetryDelayStrategy.Exponential,
    InitialDelayMs = 1000,
    MaxDelayMs = 30000
};

// リトライポリシーを無効化
client.RetryPolicy = null;
```

**接続リトライポリシーの変更**:

```csharp
// 接続リトライポリシーを設定（無限リトライ）
client.ConnectionRetryPolicy = new RetryPolicy
{
    MaxRetryCount = -1,
    RetryDelayStrategy = RetryDelayStrategy.Exponential,
    InitialDelayMs = 2000,
    MaxDelayMs = 60000
};
```

**現在の設定を取得**:

```csharp
// KeepAlive設定を取得
var keepAlive = client.KeepAlive;
if (keepAlive != null)
{
    Console.WriteLine($"KeepAlive: Enabled={keepAlive.Enabled}, Interval={keepAlive.IntervalSeconds}s");
}

// タイムアウト設定を取得
var timeout = client.TimeoutMilliseconds;
Console.WriteLine($"Timeout: {timeout}ms");
```

**注意事項**:
- KeepAlive設定を変更すると、接続中の場合、既存のタイマーが停止され、新しい設定で再起動されます
- 設定変更はスレッドセーフに実装されています
- プロパティから取得した設定オブジェクトはコピーなので、直接変更しても元の設定には影響しません

#### Observable

`MessageReceived`プロパティで`IObservable<Message>`にアクセスできます。Reactive Extensionsを使用したメッセージ処理が可能です。

```csharp
using System.Reactive.Linq;

client.MessageReceived
    .Where(msg => msg.Text?.StartsWith("ALERT") == true)
    .Subscribe(msg =>
    {
        Console.WriteLine($"Alert: {msg.Text}");
    });
```

### ITcpServer

TCPサーバーのインターフェイスです。

#### プロパティ

| プロパティ | 型 | 説明 |
|-----------|-----|------|
| `Name` | `string` | サーバー名 |
| `IsRunning` | `bool` | サーバーが起動中かどうか |
| `ListenPort` | `int` | リッスンポート |
| `ConnectionInfo` | `ServerConnectionInfo` | 接続状態情報（詳細は[ServerConnectionInfo](#serverconnectioninfo)を参照） |

#### イベント

| イベント | 型 | 説明 |
|---------|-----|------|
| `OnMessageReceived` | `EventHandler<(Message message, SessionInfo sessionInfo)>` | メッセージ受信イベント |
| `OnClientConnected` | `EventHandler<SessionInfo>` | クライアント接続イベント |
| `OnClientDisconnected` | `EventHandler<SessionInfo>` | クライアント切断イベント |
| `OnError` | `EventHandler<(Exception exception, SessionInfo? sessionInfo)>` | エラーイベント |

#### メソッド

##### `Task StartAsync()`

サーバーを起動します。

```csharp
await server.StartAsync();
```

##### `Task StopAsync()`

サーバーを停止します。

```csharp
await server.StopAsync();
```

##### `Task SendAsync(string sessionId, Message message)`

特定のセッションにメッセージを送信します。

- `sessionId`: セッションID（`SessionInfo.SessionId`）

```csharp
var response = Message.FromString("OK\r", System.Text.Encoding.UTF8);
await server.SendAsync(sessionInfo.SessionId, response);
```

##### `Task BroadcastAsync(Message message)`

全セッションにメッセージをブロードキャストします。

```csharp
var announcement = Message.FromString("ANNOUNCEMENT\r", System.Text.Encoding.UTF8);
await server.BroadcastAsync(announcement);
```

##### `SessionInfo? GetSession(string sessionId)`

セッション情報を取得します。

```csharp
var session = server.GetSession(sessionId);
if (session != null)
{
    Console.WriteLine($"Session: {session.SessionId}, Connected at: {session.ConnectedAt}");
}
```

##### `IEnumerable<SessionInfo> GetAllSessions()`

全セッション情報を取得します。

```csharp
var allSessions = server.GetAllSessions();
foreach (var session in allSessions)
{
    Console.WriteLine($"Session: {session.SessionId}");
}
```

#### Observable

`MessageReceived`プロパティで`IObservable<(Message message, SessionInfo sessionInfo)>`にアクセスできます。

```csharp
using System.Reactive.Linq;

server.MessageReceived
    .Where(args => args.message.Code == "ALERT")
    .Subscribe(args =>
    {
        var (message, sessionInfo) = args;
        Console.WriteLine($"Alert from {sessionInfo.SessionId}: {message.Text}");
    });
```

### ITcpMessengerFactory

サーバーとクライアントを作成するファクトリーです。

#### メソッド

##### `ITcpServer CreateServer(string name)`

設定からサーバーを作成します。

- `name`: サーバー名（`ServerConfig.Name`と一致する必要があります）

```csharp
var server = factory.CreateServer("MainServer");
```

##### `ITcpClient CreateClient(string name)`

設定からクライアントを作成します。

- `name`: クライアント名（`ClientConfig.Name`と一致する必要があります）

```csharp
var client = factory.CreateClient("ControllerA");
```

### Message

TCPメッセージを表すクラスです。

#### プロパティ

| プロパティ | 型 | 説明 |
|-----------|-----|------|
| `RawData` | `byte[]` | メッセージの生データ（バイト配列） |
| `Text` | `string?` | メッセージの文字列表現（エンコーディング変換後） |
| `Code` | `string?` | メッセージコード（プロトコルヘッダから抽出可能） |
| `Timestamp` | `DateTime` | メッセージのタイムスタンプ |
| `Metadata` | `Dictionary<string, object>` | 追加のメタデータ |

#### 静的メソッド

##### `Message FromString(string text, Encoding encoding)`

文字列からメッセージを作成します。

```csharp
var msg = Message.FromString("HELLO\r", System.Text.Encoding.UTF8);
```

##### `Message FromBytes(byte[] data, Encoding encoding)`

バイト配列からメッセージを作成します。

```csharp
var bytes = System.Text.Encoding.UTF8.GetBytes("HELLO\r");
var msg = Message.FromBytes(bytes, System.Text.Encoding.UTF8);
```

### SessionInfo

TCPセッション情報を表すクラスです。

#### プロパティ

| プロパティ | 型 | 説明 |
|-----------|-----|------|
| `SessionId` | `string` | セッションID（サーバー側で割り当て） |
| `SourceEndpoint` | `IPEndPoint` | 送信元エンドポイント（IP+Port） |
| `RemoteEndpoint` | `IPEndPoint?` | リモートエンドポイント（接続先） |
| `ConnectedAt` | `DateTime` | 接続開始時刻 |
| `LastMessageReceivedAt` | `DateTime?` | 最後のメッセージ受信時刻 |
| `Metadata` | `Dictionary<string, object>` | 追加のセッションメタデータ |
| `IsActive` | `bool` | セッションが有効かどうか |

### ClientConnectionInfo

TCPクライアントの接続状態情報を表すクラスです。

#### プロパティ

| プロパティ | 型 | 説明 |
|-----------|-----|------|
| `IsConnected` | `bool` | 接続状態 |
| `ConnectedAt` | `DateTime?` | 接続開始時刻 |
| `LastMessageReceivedAt` | `DateTime?` | 最後のメッセージ受信時刻 |
| `RemoteHost` | `string` | リモートホスト（IPアドレスまたはホスト名） |
| `RemotePort` | `int` | リモートポート |
| `IsReconnecting` | `bool` | 再接続試行中かどうか |
| `ConnectionDuration` | `TimeSpan?` | 接続継続時間（接続開始時刻から現在までの経過時間） |
| `MessagesSent` | `long` | 送信メッセージ数 |
| `MessagesReceived` | `long` | 受信メッセージ数 |
| `PendingRequests` | `int` | 待機中のリクエスト数 |
| `LastKeepAliveSentAt` | `DateTime?` | 最後のキープアライブ送信時刻 |
| `LastKeepAliveResponseReceivedAt` | `DateTime?` | 最後のキープアライブ応答受信時刻 |
| `KeepAliveTimeoutCount` | `int` | キープアライブタイムアウト回数 |
| `ErrorCount` | `int` | エラー発生回数 |
| `LastError` | `string?` | 最後のエラーメッセージ |
| `LastErrorAt` | `DateTime?` | 最後のエラー発生時刻 |
| `ConnectionRetryAttempts` | `int` | 接続リトライ試行回数 |
| `LastRetryAttemptAt` | `DateTime?` | 最後のリトライ試行時刻 |

### ServerConnectionInfo

TCPサーバーの接続状態情報を表すクラスです。

#### プロパティ

| プロパティ | 型 | 説明 |
|-----------|-----|------|
| `IsRunning` | `bool` | サーバーが起動中かどうか |
| `StartedAt` | `DateTime?` | 起動時刻 |
| `Uptime` | `TimeSpan?` | 稼働時間（起動時刻から現在までの経過時間） |
| `ListenPort` | `int` | リッスンポート |
| `ConnectionCount` | `int` | 現在の接続数 |
| `TotalConnections` | `long` | 累計接続数 |
| `LastClientConnectedAt` | `DateTime?` | 最後のクライアント接続時刻 |
| `LastClientDisconnectedAt` | `DateTime?` | 最後のクライアント切断時刻 |
| `MessagesSent` | `long` | 送信メッセージ数（全セッション合計） |
| `MessagesReceived` | `long` | 受信メッセージ数（全セッション合計） |

## 使用例

### 基本的なサーバー/クライアント通信

サーバー側:

```csharp
var server = factory.CreateServer("MainServer");
server.OnMessageReceived += async (sender, args) =>
{
    var (message, sessionInfo) = args;
    Console.WriteLine($"Received: {message.Text} from {sessionInfo.SessionId}");
    
    // 応答を送信
    var response = Message.FromString("OK\r", System.Text.Encoding.UTF8);
    await server.SendAsync(sessionInfo.SessionId, response);
};
await server.StartAsync();
```

クライアント側:

```csharp
var client = factory.CreateClient("ControllerA");
client.OnMessageReceived += (sender, message) =>
{
    Console.WriteLine($"Received: {message.Text}");
};
await client.ConnectAsync();

var msg = Message.FromString("HELLO\r", System.Text.Encoding.UTF8);
await client.SendAsync(msg);
```

### Promise的チェーン処理

`SendAndWaitAsync`の戻り値は`Task<Message>`なので、`Then`拡張メソッド（`TaskExtensions`）を使用してチェーン処理が可能です。

```csharp
var initMessage = Message.FromString("INIT\r", System.Text.Encoding.UTF8);

await client
    .SendAndWaitAsync(initMessage, m => m.Code == "OK", TimeSpan.FromSeconds(3))
    .Then(async resp =>
    {
        var next = Message.FromString($"NEXT:{resp.Text}\r", System.Text.Encoding.UTF8);
        return await client.SendAndWaitAsync(next, m => m.Code == "OK", TimeSpan.FromSeconds(3));
    })
    .Then(async final =>
    {
        Console.WriteLine($"Final response: {final?.Text}");
        return (Message?)null;
    });
```

### Observableパターン

Reactive Extensionsを使用したメッセージ処理です。

```csharp
using System.Reactive.Linq;

// サーバー側
server.MessageReceived
    .Where(args => args.message.Code == "ALERT")
    .Subscribe(args =>
    {
        var (message, sessionInfo) = args;
        Console.WriteLine($"Alert from {sessionInfo.SessionId}: {message.Text}");
    });

// クライアント側
client.MessageReceived
    .Where(msg => msg.Text?.StartsWith("ECHO:") == true)
    .Subscribe(msg =>
    {
        Console.WriteLine($"Echo response: {msg.Text}");
    });
```

### フィルターパイプライン

メッセージの送受信時に処理を追加できます。

```csharp
using Dnbn.Filters;
using Dnbn.Models;

public class LoggingFilter : IMessageFilter
{
    public Task<Message> OnSendingAsync(Message msg, IMessageContext ctx)
    {
        Console.WriteLine($"[SEND] {msg.Text}");
        return Task.FromResult(msg);
    }

    public Task<Message> OnReceivedAsync(Message msg, IMessageContext ctx)
    {
        Console.WriteLine($"[RECV] {msg.Text}");
        return Task.FromResult(msg);
    }
}

// フィルターを登録
services.AddSingleton<IMessageFilter, LoggingFilter>();
```

### 接続リトライ機能

接続失敗時やNW障害時に自動的にリトライする機能です。

**設定例**:

```json
{
  "ConnectionRetryPolicy": {
    "MaxRetryCount": -1,
    "RetryDelayStrategy": "Exponential",
    "InitialDelayMs": 1000,
    "MaxDelayMs": 60000
  }
}
```

**動作**:

- **接続時のリトライ**: `ConnectAsync()`呼び出し時に接続に失敗した場合、`ConnectionRetryPolicy`に基づいて自動的にリトライします
- **NW障害時の自動再接続**: 通信中にNW障害が発生した場合、自動的に再接続を試行します
- **無限リトライ**: `MaxRetryCount = -1`に設定すると、接続成功まで永続的にリトライを続けます
- **指数バックオフ**: `RetryDelayStrategy = "Exponential"`の場合、リトライ間隔が指数関数的に増加します（`MaxDelayMs`で上限が設定されます）

**注意事項**:

- 意図的な切断（`DisconnectAsync(true)`）の場合は自動再接続しません
- 無限リトライ（`MaxRetryCount = -1`）の場合、アプリケーション終了まで接続を試行し続けます
- ログにリトライ試行回数とエラー内容が記録されます

### キープアライブ機能

定期的なメッセージ送信で接続を維持します。

**設定例**:

```json
{
  "KeepAlive": {
    "Enabled": true,
    "IntervalSeconds": 30,
    "Message": "PING\r"
  }
}
```

**使用例**:

```csharp
client.OnKeepAliveResponseReceived += (sender, message) =>
{
    Console.WriteLine($"Keep-alive response: {message.Text}");
    // 状態取得コマンドの応答を使用して処理を行う例
};
```

**実行時の設定変更**:

```csharp
// 接続中にKeepAlive設定を変更
client.KeepAlive = new KeepAliveConfig
{
    Enabled = true,
    IntervalSeconds = 10,  // 間隔を10秒に変更
    Message = "STATUS\r"  // メッセージを変更
};

// KeepAliveを無効化
client.KeepAlive = new KeepAliveConfig { Enabled = false };
```

### 動的設定変更機能

実行時にクライアント設定を変更できます。接続中でも変更可能です。

**対応している設定**:
- KeepAlive設定（有効/無効、間隔、メッセージ）
- タイムアウト設定
- リトライポリシー
- 接続リトライポリシー

**使用例**:

```csharp
// KeepAlive設定を変更
client.KeepAlive = new KeepAliveConfig
{
    Enabled = true,
    IntervalSeconds = 15,
    Message = "HEARTBEAT\r"
};

// タイムアウトを変更
client.TimeoutMilliseconds = 10000;

// リトライポリシーを変更
client.RetryPolicy = new RetryPolicy
{
    MaxRetryCount = 5,
    RetryDelayStrategy = RetryDelayStrategy.Exponential,
    InitialDelayMs = 1000,
    MaxDelayMs = 30000
};

// 現在の設定を確認
var currentKeepAlive = client.KeepAlive;
if (currentKeepAlive != null)
{
    Console.WriteLine($"KeepAlive: Enabled={currentKeepAlive.Enabled}, Interval={currentKeepAlive.IntervalSeconds}s");
}
```

**動作**:
- KeepAlive設定を変更すると、接続中の場合、既存のタイマーが停止され、新しい設定で再起動されます
- 設定変更はスレッドセーフに実装されています
- プロパティから取得した設定オブジェクトはコピーなので、直接変更しても元の設定には影響しません

**注意事項**:
- リモートホスト/ポート、エンコーディングなど、接続に影響する設定は実行時に変更できません
- 設定変更は即座に反映されます

### 接続状態情報の取得

クライアントとサーバーの接続状態情報を取得できます。

**クライアント側の使用例**:

```csharp
// 接続状態情報を取得
var info = client.ConnectionInfo;
Console.WriteLine($"接続状態: {info.IsConnected}");
Console.WriteLine($"接続開始時刻: {info.ConnectedAt}");
Console.WriteLine($"接続継続時間: {info.ConnectionDuration}");
Console.WriteLine($"送信メッセージ数: {info.MessagesSent}");
Console.WriteLine($"受信メッセージ数: {info.MessagesReceived}");
Console.WriteLine($"エラー発生回数: {info.ErrorCount}");
if (info.LastError != null)
{
    Console.WriteLine($"最後のエラー: {info.LastError} ({info.LastErrorAt})");
}
```

**サーバー側の使用例**:

```csharp
// 接続状態情報を取得
var info = server.ConnectionInfo;
Console.WriteLine($"サーバー状態: {info.IsRunning}");
Console.WriteLine($"起動時刻: {info.StartedAt}");
Console.WriteLine($"稼働時間: {info.Uptime}");
Console.WriteLine($"現在の接続数: {info.ConnectionCount}");
Console.WriteLine($"累計接続数: {info.TotalConnections}");
Console.WriteLine($"送信メッセージ数: {info.MessagesSent}");
Console.WriteLine($"受信メッセージ数: {info.MessagesReceived}");
```

**複数クライアント/サーバーの一覧表示例**:

```csharp
// 複数のクライアントの状態を一覧表示
var clients = new[] { client1, client2, client3 };
foreach (var client in clients)
{
    var info = client.ConnectionInfo;
    Console.WriteLine($"{client.Name}: " +
        $"接続={info.IsConnected}, " +
        $"送信={info.MessagesSent}, " +
        $"受信={info.MessagesReceived}, " +
        $"エラー={info.ErrorCount}");
}

// 複数のサーバーの状態を一覧表示
var servers = new[] { server1, server2 };
foreach (var server in servers)
{
    var info = server.ConnectionInfo;
    Console.WriteLine($"{server.Name}: " +
        $"起動中={info.IsRunning}, " +
        $"接続数={info.ConnectionCount}, " +
        $"送信={info.MessagesSent}, " +
        $"受信={info.MessagesReceived}");
}
```

**HTTPエンドポイント経由での取得（サンプルプロジェクト）**:

サンプルプロジェクトの統合モードでは、HTTPエンドポイント経由で接続状態情報をJSON形式で取得できます。

```bash
# 全接続状態情報を取得
curl http://localhost:8080/api/status

# クライアント接続状態情報を取得
curl http://localhost:8080/api/status/client

# サーバー接続状態情報を取得
curl http://localhost:8080/api/status/server

# ヘルスチェック
curl http://localhost:8080/api/health
```

**レスポンス例**:

```json
{
  "client": {
    "name": "EchoClient",
    "isConnected": true,
    "connectedAt": "2024-01-01T12:00:00Z",
    "connectionDuration": "00:05:30",
    "remoteHost": "localhost",
    "remotePort": 5000,
    "isReconnecting": false,
    "messagesSent": 100,
    "messagesReceived": 100,
    "pendingRequests": 0,
    "lastMessageReceivedAt": "2024-01-01T12:05:25Z",
    "keepAlive": {
      "lastSentAt": "2024-01-01T12:05:30Z",
      "lastResponseReceivedAt": "2024-01-01T12:05:30Z",
      "timeoutCount": 0
    },
    "error": {
      "count": 0,
      "lastError": null,
      "lastErrorAt": null
    },
    "connectionRetry": {
      "attempts": 0,
      "lastAttemptAt": null
    }
  },
  "server": {
    "name": "EchoServer",
    "isRunning": true,
    "listenPort": 5000,
    "startedAt": "2024-01-01T12:00:00Z",
    "uptime": "00:05:30",
    "connectionCount": 1,
    "totalConnections": 1,
    "lastClientConnectedAt": "2024-01-01T12:00:01Z",
    "lastClientDisconnectedAt": null,
    "messagesSent": 100,
    "messagesReceived": 100,
    "sessions": [
      {
        "sessionId": "127.0.0.1:12345-abc123",
        "sourceEndpoint": "127.0.0.1:12345",
        "connectedAt": "2024-01-01T12:00:01Z",
        "lastMessageReceivedAt": "2024-01-01T12:05:25Z",
        "isActive": true
      }
    ]
  }
}
```

### セッション管理

サーバー側で接続されているクライアントのセッションを管理できます。

```csharp
// 特定のセッション情報を取得
var session = server.GetSession(sessionId);
if (session != null)
{
    Console.WriteLine($"Session: {session.SessionId}");
    Console.WriteLine($"Source: {session.SourceEndpoint}");
    Console.WriteLine($"Connected at: {session.ConnectedAt}");
}

// 全セッション情報を取得
var allSessions = server.GetAllSessions();
foreach (var session in allSessions)
{
    Console.WriteLine($"Session: {session.SessionId}, Active: {session.IsActive}");
}

// 特定のセッションにメッセージを送信
var response = Message.FromString("OK\r", System.Text.Encoding.UTF8);
await server.SendAsync(sessionId, response);

// 全セッションにブロードキャスト
var announcement = Message.FromString("ANNOUNCEMENT\r", System.Text.Encoding.UTF8);
await server.BroadcastAsync(announcement);
```

### エラーハンドリング

エラーはイベントで受け取ることができます。

**クライアント側**:

```csharp
client.OnError += (sender, exception) =>
{
    Console.WriteLine($"Error: {exception.Message}");
    // エラー処理
};
```

**サーバー側**:

```csharp
server.OnError += (sender, args) =>
{
    var (exception, sessionInfo) = args;
    Console.WriteLine($"Error in session {sessionInfo?.SessionId}: {exception.Message}");
    // エラー処理
};
```

**タイムアウト処理**:

```csharp
try
{
    var response = await client.SendAsync(msg, TimeSpan.FromSeconds(5));
    Console.WriteLine($"Response: {response.Text}");
}
catch (TimeoutException ex)
{
    Console.WriteLine($"Timeout: {ex.Message}");
    // タイムアウト処理
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    // その他のエラー処理
}
```

### Web UI機能

TCP Messengerの状態をWebブラウザでリアルタイムに表示する機能です。

#### 基本的な使用方法

**appsettings.jsonに設定を追加**:

```json
{
  "dnbn.net": {
    "WebUI": {
      "Enabled": true,
      "Port": 8080,
      "UpdateIntervalSeconds": 1,
      "BindAddress": "localhost",
      "EnableLogging": true
    }
  }
}
```

**注**: `"TcpMessenger"` キーも引き続きサポートされています。

**サーバー単体でWeb UIを起動**:

```csharp
using Dnbn.Extensions;
using Dnbn.Configuration;

var factory = serviceProvider.GetRequiredService<ITcpMessengerFactory>();
var tcpMessengerConfig = configuration.GetSection("TcpMessenger").Get<TcpMessengerConfig>();

var server = factory.CreateServer("MainServer");
await server.StartAsync();

// Web UIを起動
if (tcpMessengerConfig?.WebUI?.Enabled == true)
{
    var webUIService = await server.StartWebUIAsync(
        tcpMessengerConfig.WebUI,
        logger,
        cancellationToken);
    
    Console.WriteLine($"Web UIが起動しました: http://localhost:{tcpMessengerConfig.WebUI.Port}");
}
```

**クライアント単体でWeb UIを起動**:

```csharp
var client = factory.CreateClient("ControllerA");
await client.ConnectAsync();

// Web UIを起動
if (tcpMessengerConfig?.WebUI?.Enabled == true)
{
    var webUIService = await client.StartWebUIAsync(
        tcpMessengerConfig.WebUI,
        logger,
        cancellationToken);
    
    Console.WriteLine($"Web UIが起動しました: http://localhost:{tcpMessengerConfig.WebUI.Port}");
}
```

**統合モード（複数のサーバーとクライアント）でWeb UIを起動**:

```csharp
var servers = new[] { server1, server2 };
var clients = new[] { client1, client2 };

// Web UIを起動（統合モード）
if (tcpMessengerConfig?.WebUI?.Enabled == true)
{
    var webUIService = await servers.StartWebUIAsync(
        clients,
        tcpMessengerConfig.WebUI,
        logger,
        cancellationToken);
    
    Console.WriteLine($"Web UIが起動しました: http://localhost:{tcpMessengerConfig.WebUI.Port}");
}
```

**手動でWebUIServiceを作成**:

```csharp
using Dnbn.WebUI;
using Dnbn.Configuration;

var webUIConfig = new WebUIConfig
{
    Enabled = true,
    Port = 8080,
    UpdateIntervalSeconds = 1,
    BindAddress = "localhost",
    EnableLogging = true
};

var webUIService = new WebUIService(
    new[] { server1, server2 },  // サーバーリスト
    new[] { client1, client2 },  // クライアントリスト
    webUIConfig,
    logger);

await webUIService.StartAsync(cancellationToken);

// アプリケーション終了時に停止
await webUIService.StopAsync(cancellationToken);
```

#### Web UIの機能

- **リアルタイム状態表示**: SSE（Server-Sent Events）により、接続状態やメッセージ送受信数などが自動更新されます
- **クライアント状態**: 接続状態、接続継続時間、送受信メッセージ数、キープアライブ情報、エラー情報などを表示
- **サーバー状態**: サーバーの稼働状態、接続数、セッション情報、送受信メッセージ数などを表示
- **REST API**: 以下のエンドポイントが利用可能です
  - `GET /api/status` - 全接続状態情報を取得
  - `GET /api/status/client` - クライアント接続状態情報を取得
  - `GET /api/status/server` - サーバー接続状態情報を取得
  - `GET /api/health` - ヘルスチェック
  - `GET /api/status/stream` - SSEストリーム（リアルタイム更新）

#### ブラウザでのアクセス

Web UIが有効な場合、ブラウザで以下のURLにアクセスできます：

```
http://localhost:8080
```

（設定で`Port`や`BindAddress`を変更した場合は、それに合わせてURLを変更してください）

## ログ機能

### log4netとの統合

このライブラリはlog4netと統合できます。アプリ側でlog4netを使用している場合、その設定に合わせてログ出力されます。

#### log4netの設定

アプリ側でlog4netを設定します（例：`log4net.config`）：

```xml
<?xml version="1.0" encoding="utf-8"?>
<log4net>
  <appender name="ConsoleAppender" type="log4net.Appender.ConsoleAppender">
    <layout type="log4net.Layout.PatternLayout">
      <conversionPattern value="%date [%thread] %level %logger - %message%newline" />
    </layout>
  </appender>
  <appender name="FileAppender" type="log4net.Appender.FileAppender">
    <file value="logs/dnbn.log" />
    <appendToFile value="true" />
    <layout type="log4net.Layout.PatternLayout">
      <conversionPattern value="%date [%thread] %level %logger - %message%newline" />
    </layout>
  </appender>
  <root>
    <level value="DEBUG" />
    <appender-ref ref="ConsoleAppender" />
    <appender-ref ref="FileAppender" />
  </root>
  <logger name="Dnbn.Core.TcpClient">
    <level value="DEBUG" />
  </logger>
  <logger name="Dnbn.Core.TcpServer">
    <level value="DEBUG" />
  </logger>
</log4net>
```

#### log4netを使用する場合のサービス登録

```csharp
using Dnbn.Extensions;
using log4net.Config;

// log4net設定を読み込む
XmlConfigurator.Configure(new System.IO.FileInfo("log4net.config"));

var services = new ServiceCollection();

// log4netと共にTCP Messengerサービスを登録
services.AddTcpMessengerWithLog4net(configuration);

var serviceProvider = services.BuildServiceProvider();
```

#### 手動でlog4netアダプターを登録する場合

```csharp
using Dnbn.Logging;
using Microsoft.Extensions.Logging;

// log4net設定を読み込む
XmlConfigurator.Configure(new System.IO.FileInfo("log4net.config"));

var services = new ServiceCollection();

// log4netアダプターを手動で登録
services.AddSingleton(typeof(ILogger<>), typeof(Log4netLoggerAdapter<>));
services.AddSingleton<ILoggerFactory, Log4netLoggerFactoryAdapter>();

// TCP Messengerサービスを登録
services.AddTcpMessenger(configuration);
```

### メッセージ送受信ログ

`EnableMessageLogging`設定を`true`にすることで、メッセージの送受信時にDEBUGレベルのログを出力できます。

**設定方法**:

```json
{
  "dnbn.net": {
    "Servers": [
      {
        "Name": "MainServer",
        "ListenPort": 5000,
        "EnableMessageLogging": true
      }
    ],
    "Clients": [
      {
        "Name": "ControllerA",
        "RemoteHost": "192.168.1.10",
        "RemotePort": 7000,
        "EnableMessageLogging": true
      }
    ]
  }
}
```

**ログ出力例**:

- クライアント送信: `TCP Client 'ControllerA' sending message: HELLO`
- クライアント受信: `TCP Client 'ControllerA' received message: OK`
- サーバー送信: `TCP Server 'MainServer' sending message to session 127.0.0.1:12345-abc123: ECHO: HELLO`
- サーバー受信: `TCP Server 'MainServer' received message from session 127.0.0.1:12345-abc123: HELLO`

**注意事項**:

- ログレベルをDEBUGに設定する必要があります（Microsoft.Extensions.Loggingまたはlog4netの設定で）
- フィルターパイプラインによるログ出力とは独立して動作します
- デフォルト値は`false`のため、明示的に`true`に設定しない限りログは出力されません

### ログレベル

ライブラリは以下のログレベルを使用します：

- **DEBUG**: メッセージ送受信（`EnableMessageLogging`が`true`の場合）、電文受信（メッセージの詳細）
- **INFO**: 接続（CONNECT）、サーバー起動/停止、意図的な切断
- **WARN**: キープアライブタイムアウトなど警告
- **ERROR**: 意図しない切断（NW障害など）、受信エラー、接続エラー

## トラブルシューティング

### よくある問題と解決方法

#### 1. 接続できない

**症状**: `ConnectAsync()`が失敗する、またはタイムアウトする

**確認事項**:
- `RemoteHost`と`RemotePort`が正しいか確認
- ファイアウォール設定を確認
- サーバーが起動しているか確認
- ネットワーク接続を確認

**解決方法**:
- `ConnectionRetryPolicy`を設定して自動再接続を有効にする
- ログレベルを`DEBUG`に設定して詳細なログを確認

#### 2. メッセージが受信できない

**症状**: メッセージを送信したが、受信イベントが発火しない

**確認事項**:
- `MessageTerminator`が正しく設定されているか（終端文字方式の場合）
- `FixedHeaderLength`、`FixedBodyLength`が正しいか（固定長プロトコルの場合）
- `LengthFieldOffset`、`LengthFieldLength`が正しいか（可変長プロトコルの場合）
- `Encoding`が正しいか確認

**解決方法**:
- ログレベルを`DEBUG`に設定して、受信データを確認
- プロトコル設定を見直す
- ネットワークパケットキャプチャツールで実際のデータを確認

#### 3. メッセージが正しくパースされない

**症状**: メッセージが分割されたり、複数のメッセージが結合されたりする

**確認事項**:
- 終端文字が正しく設定されているか
- 固定長プロトコルの場合、実際のメッセージ長が設定と一致しているか
- 可変長プロトコルの場合、長さフィールドの位置とサイズが正しいか
- 長さフィールドのバイトオーダー（Big-endian）が正しいか

**解決方法**:
- プロトコル仕様書を確認
- 実際のバイトデータをログ出力して確認
- テストデータで動作確認

#### 4. タイムアウトが発生する

**症状**: `SendAsync`や`SendAndWaitAsync`でタイムアウトが発生する

**確認事項**:
- `TimeoutMilliseconds`が適切に設定されているか
- サーバー側の応答が返ってきているか
- ネットワーク遅延が大きすぎないか

**解決方法**:
- `TimeoutMilliseconds`を増やす
- `RetryPolicy`を設定してリトライを有効にする
- サーバー側のログを確認

#### 5. 自動再接続が動作しない

**症状**: NW障害時に自動再接続されない

**確認事項**:
- `ConnectionRetryPolicy`が設定されているか
- `MaxRetryCount`が`-1`（無限リトライ）または正の値に設定されているか
- 意図的な切断（`DisconnectAsync(true)`）をしていないか

**解決方法**:
- `ConnectionRetryPolicy`を正しく設定する
- ログを確認して、リトライが実行されているか確認

#### 6. キープアライブが動作しない

**症状**: キープアライブメッセージが送信されない

**確認事項**:
- `KeepAlive.Enabled`が`true`になっているか
- `KeepAlive.Message`が設定されているか
- クライアントが接続されているか

**解決方法**:
- 設定を確認
- ログを確認して、キープアライブメッセージが送信されているか確認

### デバッグのヒント

1. **ログレベルをDEBUGに設定**: 詳細なログを確認できます
2. **メッセージフィルターを使用**: 送受信メッセージをログ出力できます
3. **ネットワークパケットキャプチャ**: Wiresharkなどのツールで実際のデータを確認
4. **単体テスト**: 小さなテストプログラムで動作確認

### ログの見方

- **DEBUG**: メッセージの詳細な内容が出力されます。プロトコルの問題を調査する際に有用です
- **INFO**: 接続/切断の情報が出力されます。接続状態を確認する際に有用です
- **WARN**: キープアライブタイムアウトなど、警告レベルの情報が出力されます
- **ERROR**: エラーが発生した際の詳細情報が出力されます。問題の原因を特定する際に有用です

## サンプルプロジェクト

実際の使用例は `Samples/TcpMessenger.Sample` プロジェクトを参照してください。

### サンプルプロジェクトの実行

```bash
cd Samples/TcpMessenger.Sample
dotnet run
```

サンプルプロジェクトには以下の機能が含まれています：

- **サーバーモード**: ポート5000でリッスンし、クライアントからのメッセージをエコー
- **クライアントモード**: localhost:5000に接続し、メッセージを送信
- **統合モード**: サーバーとクライアントを同時に起動し、Promise的チェーン処理の例を実行
- **動的設定変更**: `config`コマンドで実行時に設定を変更（KeepAlive、タイムアウト、リトライポリシーなど）
- **Web UI**: `appsettings.json`で`WebUI.Enabled`を`true`に設定すると、Web UIが自動的に起動します（デフォルト: `http://localhost:8080`）

詳細は [Samples/TcpMessenger.Sample/README.md](./Samples/TcpMessenger.Sample/README.md) を参照してください。
