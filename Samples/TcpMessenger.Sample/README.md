# TcpMessenger.Sample

dnbn.net（TCPメッセージ送受信ライブラリ）の機能を**シナリオ別に実演する**サンプル集です。
1シナリオ＝1ファイルで自己完結しており、コードはそのままコピーして使える形になっています。

## 実行方法

```bash
# メニューから選択
dotnet run --project Samples/TcpMessenger.Sample

# シナリオ番号を直接指定（例: シナリオ3）
dotnet run --project Samples/TcpMessenger.Sample -- 3
```

シナリオ1〜6は人の操作なしで自動実行されます。7はWeb UI確認のためEnter入力で終了、8は対話モードです。

## シナリオ一覧

| # | シナリオ | 実演する機能 |
|---|---|---|
| 1 | クイックスタート | 最小構成のサーバー/クライアント、`SendAsync`によるリクエスト/レスポンス |
| 2 | チャット／ブロードキャスト | 複数セッション管理、`BroadcastAsync`、`OnMessageReceived`（プッシュ受信）、Rx購読 |
| 3 | 障害と自動再接続 | `ConnectionRetryPolicy`（無限リトライ）、`IsReconnecting`監視、`InterruptReconnectDelay`、`WaitForConnectionAsync` |
| 4 | KeepAliveと死活監視 | `KeepAliveConfig`、`ResponsePredicate`による応答判定、`KeepAliveTimeoutCount`での無応答検出 |
| 5 | レガシープロトコル | 終端文字方式＋Shift-JIS、固定長方式、長さフィールド方式のフレーミング |
| 6 | リクエスト制御 | タイムアウト、`RetryPolicy`による自動再送、`SendAndWaitAsync`の述語マッチング、FIFOパイプライン |
| 7 | 運用監視 | `IMessageFilter`（チェックサム付与/検証）、`ConnectionInfo`統計、Web UIダッシュボード |
| 8 | 対話プレイグラウンド | appsettings.json＋DI（`AddDnbnNet`）構成、実行時の設定変更（KeepAlive/タイムアウト） |

## 構成ファイル

- `appsettings.json` — シナリオ8（プレイグラウンド）で使用するDI構成の例
- それ以外のシナリオは設定をコード内に直接記述しています（コピーして使いやすくするため）

## 受信経路の使い分け（重要）

dnbn.net のクライアントには受信メッセージの経路が3つあります。サンプル全体を通して、この使い分けを意識して読んでください。

1. **`SendAsync` の戻り値** — 自分が送ったリクエストへの応答（`OnMessageReceived`は発火しない）
2. **`OnMessageReceived` / `MessageReceived`(Rx)** — リクエストと無関係にサーバーから届くプッシュ通知
3. **`OnKeepAliveResponseReceived`** — KeepAliveメッセージへの応答（`ResponsePredicate`で判定）
