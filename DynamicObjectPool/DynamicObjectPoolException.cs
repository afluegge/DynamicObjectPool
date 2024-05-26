using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace Haisl.Utils;

[ExcludeFromCodeCoverage]
[PublicAPI]
public class DynamicObjectPoolException : Exception
{
    public DynamicObjectPoolException()
    {
    }

    public DynamicObjectPoolException(string message) : base(message)
    {
    }

    public DynamicObjectPoolException(string message, Exception inner) : base(message, inner)
    {
    }
}
