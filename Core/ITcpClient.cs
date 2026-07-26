using System;
using Dnbn.Configuration;
using Dnbn.Models;

namespace Dnbn.Core;

/// <summary>
/// TCPクライアントインターフェイス
/// </summary>
public interface ITcpClient : IDisposable
{
  /// <summary>
  /// クライアント名
  /// </summary>
  string Name { get; }

  /// <summary>
  /// 接続状態
  /// </summary>
  bool IsConnected { get; }

  /// <summary>
  /// 詳細な接続状態（未接続/接続中/接続済み/自動再接続中）
  /// </summary>
  ConnectionState State { get; }

  /// <summary>
  /// メッセージ受信イベント
  /// </summary>
  event EventHandler<Message>? OnMessageReceived;

  /// <summary>
  /// 接続イベント
  /// </summary>
  event EventHandler? OnConnected;

  /// <summary>
  /// 切断イベント
  /// </summary>
  event EventHandler? OnDisconnected;

  /// <summary>
  /// エラーイベント
  /// </summary>
  event EventHandler<Exception>? OnError;

  /// <summary>
  /// キープアライブ応答受信イベント
  /// </summary>
  event EventHandler<Message>? OnKeepAliveResponseReceived;

  /// <summary>
  /// 接続状態変化イベント。状態が実際に変化したときのみ発火する。
  /// OnConnected / OnDisconnected と異なり、自動再接続の開始（Reconnecting への遷移）も観測できる。
  /// </summary>
  event EventHandler<(ConnectionState previous, ConnectionState current)>? OnConnectionStateChanged;

  /// <summary>
  /// メッセージトレースイベント（診断用）。
  /// OnMessageReceived と異なり、SendAsync の応答・KeepAlive を含む全送受信を観測できる。
  /// ハンドラ未登録時のオーバーヘッドはほぼゼロ。
  /// </summary>
  event EventHandler<MessageTraceEvent>? OnMessageTrace
#if NETSTANDARD2_0
  // netstandard2.0 はインターフェイスの既定実装を利用できないため、実装側で定義が必要
  ;
#else
  {
    // 既存の独自 ITcpClient 実装を壊さないための既定実装。
    // TcpClient はこのイベントを独自に実装して実際のトレースを発行する。
    add { }
    remove { }
  }
#endif

  /// <summary>
  /// メッセージ受信のObservable
  /// </summary>
  IObservable<Message> MessageReceived { get; }

  /// <summary>
  /// 接続する
  /// </summary>
  /// <param name="cancellationToken">切断処理を開始する前のキャンセルに使用するトークン。後片付けの開始後は中断しない</param>
  Task ConnectAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// 切断する
  /// </summary>
  /// <param name="isIntentional">意図的な切断かどうか（デフォルト: true）</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  Task DisconnectAsync(bool isIntentional = true, CancellationToken cancellationToken = default);

  /// <summary>
  /// メッセージをキューに追加して送信し、応答を待つ（HTTPクライアントのように）
  /// MaxConcurrentResponseWaitsが未設定の場合、複数要求を送信して応答をFIFOで待機できる。
  /// 1の場合は先行要求の完了まで次の応答必須要求を送信しない。
  /// 応答メッセージはOnMessageReceivedイベントを発行しない
  /// </summary>
  /// <param name="message">送信するメッセージ</param>
  /// <param name="timeout">タイムアウト時間。指定しない場合はClientConfigのTimeoutMillisecondsを使用</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  /// <returns>応答メッセージ</returns>
  Task<Message> SendAsync(Message message, TimeSpan? timeout = null, CancellationToken cancellationToken = default);

  /// <summary>
  /// メッセージを送信して応答を待つ
  /// </summary>
  /// <param name="requestMessage">送信するメッセージ</param>
  /// <param name="responsePredicate">応答の条件（trueを返す応答を受け入れる）</param>
  /// <param name="timeout">タイムアウト時間</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  Task<Message> SendAndWaitAsync(
      Message requestMessage,
      Func<Message, bool> responsePredicate,
      TimeSpan timeout,
      CancellationToken cancellationToken = default);

  /// <summary>
  /// 文字列を送信して応答を待つ（設定のEncodingを使用）
  /// MaxConcurrentResponseWaitsが未設定の場合、複数要求を送信して応答をFIFOで待機できる。
  /// 1の場合は先行要求の完了まで次の応答必須要求を送信しない。
  /// 応答メッセージはOnMessageReceivedイベントを発行しない
  /// </summary>
  /// <param name="text">送信する文字列</param>
  /// <param name="timeout">タイムアウト時間。指定しない場合はClientConfigのTimeoutMillisecondsを使用</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  /// <returns>応答メッセージ</returns>
  Task<Message> SendAsync(string text, TimeSpan? timeout = null, CancellationToken cancellationToken = default);

  /// <summary>
  /// 文字列を送信して応答を待つ（後方互換性のためのオーバーロード）
  /// </summary>
  /// <param name="text">送信する文字列</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  /// <returns>応答メッセージ</returns>
  Task<Message> SendAsync(string text, CancellationToken cancellationToken);

  /// <summary>
  /// 文字列を送信して応答を待つ（設定のEncodingを使用）
  /// </summary>
  /// <param name="text">送信する文字列</param>
  /// <param name="responsePredicate">応答の条件（trueを返す応答を受け入れる）</param>
  /// <param name="timeout">タイムアウト時間</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  Task<Message> SendAndWaitAsync(
      string text,
      Func<Message, bool> responsePredicate,
      TimeSpan timeout,
      CancellationToken cancellationToken = default);

  /// <summary>
  /// メッセージを送信する（応答を待たない通知電文用）。
  /// 送信キューを経由するため、SendAsync との送信順序は保証される。
  /// 戻りのTaskはソケットへの書き込み完了時に完了する（応答の有無は関知しない）。
  /// リトライポリシーは適用されない。
  /// </summary>
  /// <param name="message">送信するメッセージ</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  Task SendOneWayAsync(Message message, CancellationToken cancellationToken = default);

  /// <summary>
  /// 文字列を送信する（応答を待たない通知電文用、設定のEncodingを使用）
  /// </summary>
  /// <param name="text">送信する文字列</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  Task SendOneWayAsync(string text, CancellationToken cancellationToken = default);

  /// <summary>
  /// 通知電文の判定述語の取得・設定。
  /// マッチした受信メッセージは応答マッチングをスキップして OnMessageReceived に直接配信される。
  /// </summary>
  Func<Message, bool>? NotificationPredicate { get; set; }

  /// <summary>
  /// KeepAlive設定の取得・設定
  /// </summary>
  KeepAliveConfig? KeepAlive { get; set; }

  /// <summary>
  /// タイムアウト設定の取得・設定（ミリ秒）
  /// </summary>
  int TimeoutMilliseconds { get; set; }

  /// <summary>
  /// リトライポリシーの取得・設定
  /// </summary>
  RetryPolicy? RetryPolicy { get; set; }

  /// <summary>
  /// 接続リトライポリシーの取得・設定
  /// </summary>
  RetryPolicy? ConnectionRetryPolicy { get; set; }

  /// <summary>
  /// 接続状態情報の取得
  /// </summary>
  ClientConnectionInfo ConnectionInfo { get; }

  /// <summary>
  /// 接続リトライのバックオフ待機を中断し、次の接続試行を即座に実行させる。
  /// リトライループ自体はキャンセルせず、待機時間のみスキップする。
  /// </summary>
  void InterruptReconnectDelay();

  /// <summary>
  /// 接続が確立されるまで待機する。既に接続済みの場合は即座に返る。
  /// </summary>
  /// <param name="timeout">待機のタイムアウト</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  /// <exception cref="TimeoutException">タイムアウト時間内に接続が確立されなかった場合</exception>
  Task WaitForConnectionAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}
