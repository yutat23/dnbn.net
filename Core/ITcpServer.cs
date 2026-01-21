using System;
using System.Reactive;
using Dnbn.Models;

namespace Dnbn.Core;

/// <summary>
/// TCPサーバーインターフェイス
/// </summary>
public interface ITcpServer : IDisposable
{
  /// <summary>
  /// サーバー名
  /// </summary>
  string Name { get; }

  /// <summary>
  /// サーバーが起動中かどうか
  /// </summary>
  bool IsRunning { get; }

  /// <summary>
  /// メッセージ受信イベント
  /// </summary>
  event EventHandler<(Message message, SessionInfo sessionInfo)>? OnMessageReceived;

  /// <summary>
  /// クライアント接続イベント
  /// </summary>
  event EventHandler<SessionInfo>? OnClientConnected;

  /// <summary>
  /// クライアント切断イベント
  /// </summary>
  event EventHandler<SessionInfo>? OnClientDisconnected;

  /// <summary>
  /// エラーイベント
  /// </summary>
  event EventHandler<(Exception exception, SessionInfo? sessionInfo)>? OnError;

  /// <summary>
  /// メッセージ受信のObservable
  /// </summary>
  IObservable<(Message message, SessionInfo sessionInfo)> MessageReceived { get; }

  /// <summary>
  /// サーバーを起動する
  /// </summary>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  Task StartAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// サーバーを停止する
  /// </summary>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  Task StopAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// 特定セッションにメッセージを送信
  /// </summary>
  /// <param name="sessionId">セッションID（SessionInfo.SessionId）</param>
  /// <param name="message">送信するメッセージ</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  Task SendAsync(string sessionId, Message message, CancellationToken cancellationToken = default);

  /// <summary>
  /// 全セッションにメッセージを送信
  /// </summary>
  /// <param name="message">送信するメッセージ</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  Task BroadcastAsync(Message message, CancellationToken cancellationToken = default);

  /// <summary>
  /// 特定セッションに文字列を送信（設定のEncodingを使用）
  /// </summary>
  /// <param name="sessionId">セッションID（SessionInfo.SessionId）</param>
  /// <param name="text">送信する文字列</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  Task SendAsync(string sessionId, string text, CancellationToken cancellationToken = default);

  /// <summary>
  /// 全セッションに文字列を送信（設定のEncodingを使用）
  /// </summary>
  /// <param name="text">送信する文字列</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  Task BroadcastAsync(string text, CancellationToken cancellationToken = default);

  /// <summary>
  /// セッション情報を取得
  /// </summary>
  SessionInfo? GetSession(string sessionId);

  /// <summary>
  /// 全セッション情報を取得
  /// </summary>
  IEnumerable<SessionInfo> GetAllSessions();

  /// <summary>
  /// リッスンポート
  /// </summary>
  int ListenPort { get; }

  /// <summary>
  /// 接続状態情報の取得
  /// </summary>
  ServerConnectionInfo ConnectionInfo { get; }
}



