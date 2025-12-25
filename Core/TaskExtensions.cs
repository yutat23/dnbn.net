using Dnbn.Models;

namespace Dnbn.Core;

/// <summary>
/// Promise的なチェーン処理のための拡張メソッド
/// </summary>
public static class TaskExtensions
{
    /// <summary>
    /// タスクの結果に対して次の処理をチェーンする
    /// </summary>
    public static async Task<TResult?> Then<T, TResult>(
        this Task<T> task,
        Func<T, Task<TResult?>> continuation)
    {
        var result = await task;
        return await continuation(result);
    }

    /// <summary>
    /// メッセージタスクの結果に対して次の処理をチェーンする
    /// </summary>
    public static async Task<Message?> Then(
        this Task<Message> task,
        Func<Message, Task<Message?>> continuation)
    {
        var result = await task;
        return await continuation(result);
    }
}

