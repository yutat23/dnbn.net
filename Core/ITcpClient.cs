using System;
using System.Reactive;
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
  /// メッセージ受信のObservable
  /// </summary>
  IObservable<Message> MessageReceived { get; }

  /// <summary>
  /// 接続する
  /// </summary>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  Task ConnectAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// 切断する
  /// </summary>
  /// <param name="isIntentional">意図的な切断かどうか（デフォルト: true）</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  Task DisconnectAsync(bool isIntentional = true, CancellationToken cancellationToken = default);

  /// <summary>
  /// メッセージを送信する
  /// </summary>
  /// <param name="message">送信するメッセージ</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  Task SendAsync(Message message, CancellationToken cancellationToken = default);

  /// <summary>
  /// メッセージを送信して応答を待つ（簡易版：すべての応答を受け入れる）
  /// </summary>
  /// <param name="message">送信するメッセージ</param>
  /// <param name="timeout">タイムアウト時間</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  Task<Message> SendAsync(Message message, TimeSpan timeout, CancellationToken cancellationToken = default);

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
  /// 文字列を送信する（設定のEncodingを使用）
  /// </summary>
  /// <param name="text">送信する文字列</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  Task SendAsync(string text, CancellationToken cancellationToken = default);

  /// <summary>
  /// 文字列を送信して応答を待つ（簡易版：すべての応答を受け入れる、設定のEncodingを使用）
  /// </summary>
  /// <param name="text">送信する文字列</param>
  /// <param name="timeout">タイムアウト時間</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  Task<Message> SendAsync(string text, TimeSpan timeout, CancellationToken cancellationToken = default);

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
}



