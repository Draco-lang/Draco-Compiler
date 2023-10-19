using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Draco.JsonRpc;

/// <summary>
/// A message adapter to query info about messages.
/// </summary>
/// <typeparam name="TMessage">The message type this is an adapter for.</typeparam>
public interface IJsonRpcMessageAdapter<TMessage>
{
    /// <summary>
    /// Checks, if <paramref name="message"/> is a request.
    /// </summary>
    /// <param name="message">The message to check.</param>
    /// <returns>True, if <paramref name="message"/> is a request, false otherwise.</returns>
    public static abstract bool IsRequest(TMessage message);

    /// <summary>
    /// Checks, if <paramref name="message"/> is a response.
    /// </summary>
    /// <param name="message">The message to check.</param>
    /// <returns>True, if <paramref name="message"/> is a response, false otherwise.</returns>
    public static abstract bool IsResponse(TMessage message);

    /// <summary>
    /// Checks, if <paramref name="message"/> is a notification.
    /// </summary>
    /// <param name="message">The message to check.</param>
    /// <returns>True, if <paramref name="message"/> is a notification, false otherwise.</returns>
    public static abstract bool IsNotification(TMessage message);

    /// <summary>
    /// Retrieves the ID for <paramref name="message"/>, in case there is one.
    /// </summary>
    /// <param name="message">The message to retrieve the ID for.</param>
    /// <returns>The ID of <paramref name="message"/>, or null if it has none.</returns>
    public static abstract object? GetId(TMessage message);

    /// <summary>
    /// Retrieves the method name for <paramref name="message"/>.
    /// </summary>
    /// <param name="message">The message to retrieve the method name for.</param>
    /// <returns>The method name <paramref name="message"/> is invoking.</returns>
    public static abstract string GetMethodName(TMessage message);

    /// <summary>
    /// Checks, if <paramref name="message"/> is a cancellation message.
    /// </summary>
    /// <param name="message">The message to check.</param>
    /// <returns>True, if <paramref name="message"/> is a request cancellation, false otherwise.</returns>
    public static abstract bool IsCancellation(TMessage message);
}
