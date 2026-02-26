namespace FSharp.ATProto.Core

open System
open System.Reflection
open Microsoft.FSharp.Reflection

/// <summary>
/// Serializes F# records to URL query strings for XRPC query parameters.
/// Used internally by <see cref="Xrpc.query"/> to convert typed parameter records
/// into query-string form.
/// </summary>
module QueryParams =

    let private isOptionType (t: Type) =
        t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<option<_>>

    let private isListType (t: Type) =
        t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<list<_>>

    let private formatValue (value: obj) : string =
        match value with
        | :? string as s -> s
        | :? int64 as i -> string i
        | :? int as i -> string i
        | :? bool as b -> if b then "true" else "false"
        | _ -> string value

    let private toCamelCase (name: string) =
        if String.IsNullOrEmpty(name) then name
        else string (Char.ToLowerInvariant(name.[0])) + name.[1..]

    /// <summary>
    /// Converts an F# record to a URL query string (including the leading <c>?</c>).
    /// </summary>
    /// <param name="record">An F# record whose fields map to query-string parameters.</param>
    /// <typeparam name="T">The record type. Must be an F# record.</typeparam>
    /// <returns>
    /// A query string such as <c>"?actor=my-handle.bsky.social&amp;limit=50"</c>,
    /// or an empty string if the record produces no parameters.
    /// </returns>
    /// <remarks>
    /// Field names are converted to camelCase. The following field types are supported:
    /// <list type="bullet">
    ///   <item><description><c>option</c> fields: omitted when <c>None</c>; the inner value is emitted when <c>Some</c>.</description></item>
    ///   <item><description><c>list</c> fields: emitted as repeated query parameters (e.g. <c>uris=a&amp;uris=b</c>).</description></item>
    ///   <item><description>Scalar fields (<c>string</c>, <c>int</c>, <c>int64</c>, <c>bool</c>): emitted directly. Booleans serialize as <c>"true"</c>/<c>"false"</c>.</description></item>
    /// </list>
    /// All values are URI-escaped via <see cref="Uri.EscapeDataString"/>.
    /// </remarks>
    /// <example>
    /// <code>
    /// type Params = { Actor: string; Limit: int option }
    /// QueryParams.toQueryString { Actor = "my-handle.bsky.social"; Limit = Some 25 }
    /// // returns "?actor=my-handle.bsky.social&amp;limit=25"
    /// </code>
    /// </example>
    let toQueryString<'T> (record: 'T) : string =
        let fields = FSharpType.GetRecordFields(typeof<'T>)
        let pairs =
            fields
            |> Array.collect (fun (prop: PropertyInfo) ->
                let value = prop.GetValue(record)
                let name = toCamelCase prop.Name

                if isOptionType prop.PropertyType then
                    let tag = FSharpValue.PreComputeUnionTagReader(prop.PropertyType)
                    if tag value = 0 then // None
                        [||]
                    else // Some
                        let cases = FSharpType.GetUnionCases(prop.PropertyType)
                        let fields = FSharpValue.PreComputeUnionReader(cases.[1])
                        let inner = (fields value).[0]
                        [| (name, Uri.EscapeDataString(formatValue inner)) |]
                elif isListType prop.PropertyType then
                    let items = value :?> System.Collections.IEnumerable
                    [| for item in items -> (name, Uri.EscapeDataString(formatValue item)) |]
                else
                    [| (name, Uri.EscapeDataString(formatValue value)) |])

        if Array.isEmpty pairs then ""
        else "?" + (pairs |> Array.map (fun (k, v) -> $"{k}={v}") |> String.concat "&")
