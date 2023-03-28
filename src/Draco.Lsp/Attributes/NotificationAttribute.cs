using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Draco.Lsp.Attributes;

/// <summary>
/// Annotates a JSON-RPC notification.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class NotificationAttribute : Attribute
{
    /// <summary>
    /// The method being called.
    /// </summary>
    public string Method { get; set; }

    public NotificationAttribute(string method)
    {
        this.Method = method;
    }
}
