using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;

namespace Siem.Api.Services;

/// <summary>
/// Helper extension methods for C#-to-F# interop.
/// </summary>
public static class FSharpInteropExtensions
{
    /// <summary>
    /// Converts an IEnumerable to an F# list.
    /// </summary>
    public static FSharpList<T> ToFSharpList<T>(this IEnumerable<T> source)
    {
        return ListModule.OfSeq(source);
    }

    /// <summary>
    /// Converts a nullable reference type to an F# Option.
    /// </summary>
    public static FSharpOption<T> ToFSharpOption<T>(this T? value) where T : class
    {
        return value is null
            ? FSharpOption<T>.None
            : FSharpOption<T>.Some(value);
    }

    /// <summary>
    /// Converts a Nullable&lt;T&gt; value type to an F# Option.
    /// </summary>
    public static FSharpOption<T> ToFSharpOptionValue<T>(this T? value) where T : struct
    {
        return value.HasValue
            ? FSharpOption<T>.Some(value.Value)
            : FSharpOption<T>.None;
    }
}
