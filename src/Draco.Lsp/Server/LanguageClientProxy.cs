using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Draco.Lsp.Server;

/// <summary>
/// Intercepts language client calls.
/// </summary>
// NOTE: Not sealed since DispatchProxy generates a class derived from this at runtime
internal class LanguageClientProxy : DispatchProxy
{
    internal LanguageServerConnection Connection { get; set; } = null!;

    private readonly ConcurrentDictionary<MethodInfo, LanguageServerMethodHandler> handlers = new();

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(method, nameof(method));
        ArgumentNullException.ThrowIfNull(args, nameof(args));

        if (method.Name == $"get_{nameof(ILanguageClient.Connection)}")
        {
            return this.Connection;
        }
        else
        {
            return this.ProxyRpc(method, args);
        }
    }

    private static readonly MethodInfo SendRequestMethod = typeof(LanguageServerConnection)
        .GetMethods()
        .First(m => m.Name == nameof(LanguageServerConnection.SendRequestAsync)
                 && m.GetParameters().Length == 3);

    private object? ProxyRpc(MethodInfo method, object?[] arguments)
    {
        var handler = this.handlers.GetOrAdd(method, m => new(m, this));

        if (handler.IsRequest)
        {
            // Build up args
            var args = new List<object?>();
            // Method name
            args.Add(handler.MethodName);
            // Parameter
            if (handler.AcceptsParams)
            {
                args.Add(arguments[0]);
            }
            else
            {
                args.Add(null);
            }
            // CT
            if (handler.SupportsCancellation)
            {
                args.Add(arguments[1]!);
            }
            else
            {
                args.Add(CancellationToken.None);
            }

            // Extract return type
            var returnType = method.ReturnType;

            if (returnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                returnType = returnType.GetGenericArguments()[0];
            }

            return SendRequestMethod
                .MakeGenericMethod(returnType)
                .Invoke(this.Connection, args.ToArray());
        }
        else
        {
            // It's a notification
            return this.Connection.SendNotificationAsync(handler.MethodName, arguments.SingleOrDefault());
        }
    }
}
