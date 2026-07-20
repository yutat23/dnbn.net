using System;
using Dnbn.Models;

namespace Dnbn.Core;

/// <summary>TCPサーバーが受信したメッセージを非同期に処理するハンドラ。</summary>
public delegate Task TcpServerMessageHandler(
    Message message,
    SessionInfo sessionInfo,
    CancellationToken cancellationToken);

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
  /// セッションごとの受信順序を保ってawaitされる非同期メッセージハンドラ。
  /// 既存の独自ITcpServer実装とのソース互換性のため、既定実装は何もしない。
  /// </summary>
  event TcpServerMessageHandler? OnMessageReceivedAsync
#if NETSTANDARD2_0
  // netstandard2.0 はインターフェイスの既定実装を利用できないため、実装側で定義が必要
  ;
#else
  {
    add { }
    remove { }
  }
#endif

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
