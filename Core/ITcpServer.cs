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
    Task StartAsync();

    /// <summary>
    /// サーバーを停止する
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// 特定セッションにメッセージを送信
    /// </summary>
    Task SendAsync(string sessionId, Message message);

    /// <summary>
    /// 全セッションにメッセージを送信
    /// </summary>
    Task BroadcastAsync(Message message);

    /// <summary>
    /// セッション情報を取得
    /// </summary>
    SessionInfo? GetSession(string sessionId);

    /// <summary>
    /// 全セッション情報を取得
    /// </summary>
    IEnumerable<SessionInfo> GetAllSessions();
}



