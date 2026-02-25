namespace FSharp.ATProto.Core

open System
open System.Reflection
open Microsoft.FSharp.Reflection

/// Serializes F# records to URL query strings for XRPC queries.
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

    /// Convert an F# record to a URL query string.
    /// Option fields are omitted when None.
    /// List fields are emitted as repeated parameters.
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
