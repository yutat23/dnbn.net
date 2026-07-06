# 設定リファレンス

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

## ServerConfig

`dnbn.net.Servers` に設定します。`Name` は `ITcpMessengerFactory.CreateServer(name)` で使う識別子です。

| プロパティ | 型 | 既定値 | 説明 |
|---|---:|---:|---|
| `Name` | `string` | `""` | サーバー名 |
| `ListenPort` | `int` | `0` | 待ち受けポート |
| `Encoding` | `string` | `"UTF-8"` | 文字エンコーディング |
| `MessageTerminator` | `string?` | `null` | 送信時、および受信時の既定終端文字 |
| `ReceiveMessageTerminator` | `string[]?` | `null` | 受信時の終端文字候補 |
| `ClientIdentification` | `ClientIdentification` | `SourceEndpoint` | クライアント識別方式 |
| `FixedHeaderLength` | `int?` | `null` | 固定長/長さフィールド方式のヘッダ長 |
| `FixedBodyLength` | `int?` | `null` | 固定長方式のボディ長 |
| `LengthFieldOffset` | `int?` | `null` | ヘッダ内の長さフィールド開始位置 |
| `LengthFieldLength` | `int?` | `null` | 長さフィールドのバイト数 |
| `EnableMessageLogging` | `bool` | `false` | メッセージ送受信ログ（`true`: Information、`false`: Debug レベルで出力） |
| `MaxReceiveBufferBytes` | `int?` | `null` | 受信バッファ上限。未設定または0以下は無制限 |
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
| `WaitForConnectionOnSend` | `bool` | `false` | 未接続時の送信で接続確立を待つ。既定値では従来どおり即座に `InvalidOperationException` |
| `WaitForConnectionTimeoutMilliseconds` | `int` | `10000` | 接続待ち送信の最大待機時間。タイムアウト時は `TimeoutException` |
| `KeepAlive` | `KeepAliveConfig?` | `null` | KeepAlive設定（アプリケーションレベル：電文送信による死活監視） |
| `TcpKeepAlive` | `TcpKeepAliveConfig?` | `null` | TCPレベルのキープアライブ設定 |
| `FixedHeaderLength` | `int?` | `null` | 固定長/長さフィールド方式のヘッダ長 |
| `FixedBodyLength` | `int?` | `null` | 固定長方式のボディ長 |
| `LengthFieldOffset` | `int?` | `null` | ヘッダ内の長さフィールド開始位置 |
| `LengthFieldLength` | `int?` | `null` | 長さフィールドのバイト数 |
| `EnableMessageLogging` | `bool` | `false` | メッセージ送受信ログ（`true`: Information、`false`: Debug レベルで出力） |
| `MaxReceiveBufferBytes` | `int?` | `null` | 受信バッファ上限。未設定または0以下は無制限 |

`NotificationPredicate` はコードから設定するプロパティです。JSON/XML設定には含められません。

## RetryPolicy

| プロパティ | 型 | 既定値 | 説明 |
|---|---:|---:|---|
| `MaxRetryCount` | `int` | `3` | 最大リトライ回数。接続リトライでは `-1` で無限リトライ |
| `RetryDelayStrategy` | `RetryDelayStrategy` | `Exponential` | `Fixed` または `Exponential` |
| `InitialDelayMs` | `int` | `500` | 初期待機時間 |
| `MaxDelayMs` | `int` | `60000` | 最大待機時間 |
| `FailOnTimeout` | `bool` | `true` | タイムアウトを失敗として扱う |
| `FailOnErrorResponse` | `bool` | `true` | エラー応答を失敗として扱う |

## KeepAliveConfig

| プロパティ | 型 | 既定値 | 説明 |
|---|---:|---:|---|
| `Enabled` | `bool` | `false` | KeepAliveを有効にする |
| `IntervalSeconds` | `int` | `30` | 送信間隔 |
| `Message` | `string` | `""` | 送信するKeepAliveメッセージ |

`ResponsePredicate` はコードから設定するプロパティです。JSON/XML設定には含められません。

## TcpKeepAliveConfig

OSのTCPスタックが行うキープアライブ（ソケットオプション `SO_KEEPALIVE`）の設定です。無通信状態が続いてもNW障害・タイムアウトによる切断をOSレベルで検知できるようになり、`SendAsync` 時に初めてエラーになるのを防ぎやすくなります。電文を送信する `KeepAliveConfig`（アプリケーションレベル）とは独立しており、併用もできます。

| プロパティ | 型 | 既定値 | 説明 |
|---|---:|---:|---|
| `Enabled` | `bool` | `false` | TCPキープアライブを有効にする |
| `TimeSeconds` | `int` | `60` | 無通信状態から最初のプローブ送信までの時間（秒） |
| `IntervalSeconds` | `int` | `10` | プローブの再送間隔（秒） |
| `RetryCount` | `int` | `5` | 接続断と判定するまでのプローブ再送回数 |

未設定（`null`）の場合は従来どおりOSの既定動作です。`TimeSeconds` / `IntervalSeconds` / `RetryCount` の細かい制御がOSでサポートされていない環境では、基本のキープアライブ有効化のみが行われます。

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
