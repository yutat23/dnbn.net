using System;
using System.Reactive;
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
  Task ConnectAsync();

  /// <summary>
  /// 切断する
  /// </summary>
  /// <param name="isIntentional">意図的な切断かどうか（デフォルト: true）</param>
  Task DisconnectAsync(bool isIntentional = true);

  /// <summary>
  /// メッセージを送信する
  /// </summary>
  Task SendAsync(Message message);

  /// <summary>
  /// メッセージを送信して応答を待つ（簡易版：すべての応答を受け入れる）
  /// </summary>
  Task<Message> SendAsync(Message message, TimeSpan timeout);

  /// <summary>
  /// メッセージを送信して応答を待つ
  /// </summary>
  Task<Message> SendAndWaitAsync(
      Message requestMessage,
      Func<Message, bool> responsePredicate,
      TimeSpan timeout);
}



