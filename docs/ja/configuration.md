# 設定リファレンス

[English](../configuration.md) | 日本語

`appsettings.json` では `dnbn.net` セクションを使います。`TcpMessenger` セクションも後方互換性のために読み込まれますが、新しいコードでは `dnbn.net` を使ってください。

```json
{
  "dnbn.net": {
    "Servers": [],
    "Clients": [],
    "WebUI": {
      "Enabled": false
    }
  }
}
```

## フル設定サンプル

JSON/XML設定で指定できるすべてのプロパティを記載したサンプルです。各プロパティの意味は後続のリファレンスを参照してください。

メッセージの区切り方式は「終端文字方式」（`MessageTerminator` / `ReceiveMessageTerminator`）と「固定長/長さフィールド方式」（`FixedHeaderLength` / `FixedBodyLength` / `LengthFieldOffset` / `LengthFieldLength`）のどちらか一方を使います。以下では前者を `TerminatorServer` / `TerminatorClient`、後者を `LengthFieldServer` / `LengthFieldClient` として示します。

```json
{
  "dnbn.net": {
    "Servers": [
      {
        "Name": "TerminatorServer",
        "ListenPort": 5000,
        "BindAddress": "127.0.0.1",
        "Encoding": "UTF-8",
        "MessageTerminator": "\r\n",
        "ReceiveMessageTerminator": [ "\r\n", "\n" ],
        "ClientIdentification": "SourceEndpoint",
        "EnableMessageLogging": true,
        "MaxReceiveBufferBytes": 1048576,
        "TcpKeepAlive": {
          "Enabled": true,
          "TimeSeconds": 60,
          "IntervalSeconds": 10,
          "RetryCount": 5
        }
      },
      {
        "Name": "LengthFieldServer",
        "ListenPort": 5001,
        "BindAddress": "0.0.0.0",
        "Encoding": "Shift-JIS",
        "ClientIdentification": "SourceEndpoint",
        "FixedHeaderLength": 8,
        "LengthFieldOffset": 4,
        "LengthFieldLength": 4,
        "EnableMessageLogging": false,
        "MaxReceiveBufferBytes": 1048576
      }
    ],
    "Clients": [
      {
        "Name": "TerminatorClient",
        "RemoteHost": "192.168.1.10",
        "RemotePort": 5000,
        "Encoding": "UTF-8",
        "MessageTerminator": "\r\n",
        "ReceiveMessageTerminator": [ "\r\n", "\n" ],
        "TimeoutMilliseconds": 5000,
        "SendQueueCapacity": 1000,
        "MaxConcurrentResponseWaits": 1,
        "IncompleteRequestRecovery": "Reconnect",
        "WaitForConnectionOnSend": true,
        "WaitForConnectionTimeoutMilliseconds": 10000,
        "EnableMessageLogging": true,
        "MaxReceiveBufferBytes": 1048576,
        "RetryPolicy": {
          "MaxRetryCount": 3,
          "RetryDelayStrategy": "Exponential",
          "InitialDelayMs": 500,
          "MaxDelayMs": 60000,
          "FailOnTimeout": true,
          "FailOnErrorResponse": true
        },
        "ConnectionRetryPolicy": {
          "MaxRetryCount": -1,
          "RetryDelayStrategy": "Exponential",
          "InitialDelayMs": 1000,
          "MaxDelayMs": 30000
        },
        "KeepAlive": {
          "Enabled": true,
          "IntervalSeconds": 30,
          "Message": "PING",
          "DisconnectOnTimeout": true
        },
        "TcpKeepAlive": {
          "Enabled": true,
          "TimeSeconds": 60,
          "IntervalSeconds": 10,
          "RetryCount": 5
        }
      },
      {
        "Name": "LengthFieldClient",
        "RemoteHost": "192.168.1.20",
        "RemotePort": 5001,
        "Encoding": "Shift-JIS",
        "FixedHeaderLength": 8,
        "FixedBodyLength": 128,
        "TimeoutMilliseconds": 3000
      }
    ],
    "WebUI": {
      "Enabled": true,
      "Port": 8080,
      "UpdateIntervalSeconds": 1,
      "BindAddress": "localhost",
      "EnableLogging": true,
      "EventTimelineCapacity": 200,
      "EnableMessageHistory": true,
      "MessageHistoryCapacity": 200,
      "MessageHistoryMaxPayloadBytes": 512,
      "AllowSendFromUI": true,
      "SendAuthToken": "your-secret-token"
    }
  }
}
```

`ClientConfig.NotificationPredicate` と `KeepAliveConfig.ResponsePredicate` はコード専用のプロパティのため、このサンプルには含まれていません。

## ServerConfig

`dnbn.net.Servers` に設定します。`Name` は `ITcpMessengerFactory.CreateServer(name)` で使う識別子です。

| プロパティ | 型 | 既定値 | 説明 |
|---|---:|---:|---|
| `Name` | `string` | `""` | サーバー名 |
| `ListenPort` | `int` | `0` | 待ち受けポート |
| `BindAddress` | `string` | `"0.0.0.0"` | 待ち受けIPアドレス |
| `Encoding` | `string` | `"UTF-8"` | 文字エンコーディング |
| `MessageTerminator` | `string?` | `null` | 送信時、および受信時の既定終端文字 |
| `ReceiveMessageTerminator` | `string[]?` | `null` | 受信時の終端文字候補 |
| `ClientIdentification` | `ClientIdentification` | `SourceEndpoint` | クライアント識別方式。`SourceEndpoint`のみ実装済み。`HeaderBased`は未実装で、設定すると検証時にエラー |
| `FixedHeaderLength` | `int?` | `null` | 固定長/長さフィールド方式のヘッダ長 |
| `FixedBodyLength` | `int?` | `null` | 固定長方式のボディ長 |
| `LengthFieldOffset` | `int?` | `null` | ヘッダ内の長さフィールド開始位置 |
| `LengthFieldLength` | `int?` | `null` | 長さフィールドのバイト数。1、2、4のいずれか |
| `EnableMessageLogging` | `bool` | `false` | メッセージ送受信ログ（`true`: Information、`false`: Debug レベルで出力） |
| `MaxReceiveBufferBytes` | `int?` | `null` | 受信バッファ上限。指定時は1以上 |
| `TcpKeepAlive` | `TcpKeepAliveConfig?` | `null` | TCPレベルのキープアライブ設定（接続を受け付けたクライアントソケットに適用） |

## ClientConfig

`dnbn.net.Clients` に設定します。`Name` は `ITcpMessengerFactory.CreateClient(name)` で使う識別子です。

| プロパティ | 型 | 既定値 | 説明 |
|---|---:|---:|---|
| `Name` | `string` | `""` | クライアント名 |
| `RemoteHost` | `string` | `""` | 接続先ホスト |
| `RemotePort` | `int` | `0` | 接続先ポート |
| `Encoding` | `string` | `"UTF-8"` | 文字エンコーディング |
| `MessageTerminator` | `string?` | `null` | 送信時、および受信時の既定終端文字 |
| `ReceiveMessageTerminator` | `string[]?` | `null` | 受信時の終端文字候補 |
| `RetryPolicy` | `RetryPolicy?` | `null` | メッセージ送信リトライ |
| `ConnectionRetryPolicy` | `RetryPolicy?` | `null` | 接続失敗/切断時の再接続リトライ |
| `TimeoutMilliseconds` | `int` | `5000` | `SendAsync` の既定タイムアウト |
| `SendQueueCapacity` | `int` | `1000` | 送信キューの最大サイズ。満杯時は送信呼び出しが空き待ちになる |
| `MaxConcurrentResponseWaits` | `int?` | `null` | 同時に応答待ちにできる要求数。`SendOneWayAsync`は対象外。nullは従来どおり無制限 |
| `IncompleteRequestRecovery` | `IncompleteRequestRecovery` | `KeepConnection` | wire書き込み開始後のtimeout/cancel時の回復方法。`Reconnect`を推奨 |
| `WaitForConnectionOnSend` | `bool` | `false` | 未接続時の送信で接続確立を待つ。既定値では従来どおり即座に `InvalidOperationException` |
| `WaitForConnectionTimeoutMilliseconds` | `int` | `10000` | 接続待ち送信の最大待機時間。タイムアウト時は `TimeoutException` |
| `KeepAlive` | `KeepAliveConfig?` | `null` | KeepAlive設定（アプリケーションレベル：電文送信による死活監視） |
| `TcpKeepAlive` | `TcpKeepAliveConfig?` | `null` | TCPレベルのキープアライブ設定 |
| `FixedHeaderLength` | `int?` | `null` | 固定長/長さフィールド方式のヘッダ長 |
| `FixedBodyLength` | `int?` | `null` | 固定長方式のボディ長 |
| `LengthFieldOffset` | `int?` | `null` | ヘッダ内の長さフィールド開始位置 |
| `LengthFieldLength` | `int?` | `null` | 長さフィールドのバイト数。1、2、4のいずれか |
| `EnableMessageLogging` | `bool` | `false` | メッセージ送受信ログ（`true`: Information、`false`: Debug レベルで出力） |
| `MaxReceiveBufferBytes` | `int?` | `null` | 受信バッファ上限。指定時は1以上 |

`NotificationPredicate` はコードから設定するプロパティです。JSON/XML設定には含められません。

`SendAsync` / `SendAndWaitAsync` は応答必須、`SendOneWayAsync` は応答なしの契約です。FIFO以外の相関IDを持たないプロトコルでは、同時応答待ちを1件に制限し、送信済み要求が未完了になった接続を再確立することで遅延応答の誤相関を防ぎます。`Reconnect`時の接続待ち送信には`WaitForConnectionOnSend`も有効にしてください。

## RetryPolicy

| プロパティ | 型 | 既定値 | 説明 |
|---|---:|---:|---|
| `MaxRetryCount` | `int` | `3` | 最大リトライ回数。接続リトライでは `-1` で無限リトライ |
| `RetryDelayStrategy` | `RetryDelayStrategy` | `Exponential` | `Fixed` または `Exponential` |
| `InitialDelayMs` | `int` | `500` | 初期待機時間 |
| `MaxDelayMs` | `int` | `60000` | 最大待機時間 |
| `FailOnTimeout` | `bool` | `true` | タイムアウトを失敗として扱う |
| `FailOnErrorResponse` | `bool` | `true` | エラー応答を失敗として扱う |

`RetryPolicy`を設定すると要求電文が再送されます。二重実行できないコマンドには設定しないでください。接続確立の再試行だけが必要な場合は`ConnectionRetryPolicy`を使用します。

## KeepAliveConfig

| プロパティ | 型 | 既定値 | 説明 |
|---|---:|---:|---|
| `Enabled` | `bool` | `false` | KeepAliveを有効にする |
| `IntervalSeconds` | `int` | `30` | 送信間隔（応答タイムアウトも同じ値） |
| `Message` | `string` | `""` | 送信するKeepAliveメッセージ |
| `DisconnectOnTimeout` | `bool` | `true` | 応答タイムアウト時に切断する。NW障害扱いとなり、`ConnectionRetryPolicy` 設定時は自動再接続する |

`ResponsePredicate` はコードから設定するプロパティです。JSON/XML設定には含められません。

KeepAliveの応答は通常要求と同じFIFO順で相関され、通常要求が応答待ちの間はKeepAlive送信自体が延期されます。`DisconnectOnTimeout` の既定値は `true` です。応答がない接続はFIFO相関を信頼できないため切断し、遅延したKeepAlive応答が後続の通常要求へ誤配されるのを防ぎます。従来どおり接続を維持する必要がある場合だけ `false` を明示してください。

## TcpKeepAliveConfig

OSのTCPスタックが行うキープアライブ（ソケットオプション `SO_KEEPALIVE`）の設定です。無通信状態が続いてもNW障害・タイムアウトによる切断をOSレベルで検知できるようになり、`SendAsync` 時に初めてエラーになるのを防ぎやすくなります。電文を送信する `KeepAliveConfig`（アプリケーションレベル）とは独立しており、併用もできます。

| プロパティ | 型 | 既定値 | 説明 |
|---|---:|---:|---|
| `Enabled` | `bool` | `false` | TCPキープアライブを有効にする |
| `TimeSeconds` | `int` | `60` | 無通信状態から最初のプローブ送信までの時間（秒） |
| `IntervalSeconds` | `int` | `10` | プローブの再送間隔（秒） |
| `RetryCount` | `int` | `5` | 接続断と判定するまでのプローブ再送回数 |

未設定（`null`）の場合は従来どおりOSの既定動作です。`TimeSeconds` / `IntervalSeconds` / `RetryCount` の細かい制御がOSでサポートされていない環境では、基本のキープアライブ有効化のみが行われます。.NET Framework では、これらの詳細パラメータは Windows 10 1709 以降で有効です。

```json
{
  "dnbn.net": {
    "Clients": [
      {
        "Name": "MyClient",
        "RemoteHost": "192.168.1.10",
        "RemotePort": 5000,
        "TcpKeepAlive": {
          "Enabled": true,
          "TimeSeconds": 60,
          "IntervalSeconds": 10,
          "RetryCount": 5
        }
      }
    ]
  }
}
```

## WebUIConfig

Web UI は `dnbn.net.WebUI` パッケージ側で使います。

| プロパティ | 型 | 既定値 | 説明 |
|---|---:|---:|---|
| `Enabled` | `bool` | `false` | Web UIを有効にする |
| `Port` | `int` | `8080` | HTTPポート |
| `UpdateIntervalSeconds` | `int` | `1` | SSE更新間隔 |
| `BindAddress` | `string` | `"localhost"` | バインドアドレス。`"*"` で全アドレス |
| `EnableLogging` | `bool` | `true` | Web UIログ |
| `EventTimelineCapacity` | `int` | `200` | 接続・切断・状態遷移・エラー履歴の最大件数 |
| `EnableMessageHistory` | `bool` | `false` | 送受信メッセージ履歴を有効にする。ペイロードを扱うため既定OFF |
| `MessageHistoryCapacity` | `int` | `200` | メッセージ履歴の最大件数 |
| `MessageHistoryMaxPayloadBytes` | `int` | `512` | 履歴1件に保持するペイロードの最大バイト数 |
| `AllowSendFromUI` | `bool` | `false` | Web UIからの送信を有効にする。既定OFF |
| `SendAuthToken` | `string?` | `null` | 送信APIが `X-Dnbn-Send-Token` に要求するトークン |

`EventTimelineCapacity` とメッセージ履歴の各上限はリングバッファとして機能し、上限を超えると古い項目から破棄されます。`AllowSendFromUI` を有効にする場合は `SendAuthToken` を設定し、Web UIのポートを信頼できるネットワークだけに公開してください。
