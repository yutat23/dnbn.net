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
| `KeepAlive` | `KeepAliveConfig?` | `null` | KeepAlive設定 |
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

## WebUIConfig

Web UI は `dnbn.net.WebUI` パッケージ側で使います。

| プロパティ | 型 | 既定値 | 説明 |
|---|---:|---:|---|
| `Enabled` | `bool` | `false` | Web UIを有効にする |
| `Port` | `int` | `8080` | HTTPポート |
| `UpdateIntervalSeconds` | `int` | `1` | SSE更新間隔 |
| `BindAddress` | `string` | `"localhost"` | バインドアドレス。`"*"` で全アドレス |
| `EnableLogging` | `bool` | `true` | Web UIログ |

