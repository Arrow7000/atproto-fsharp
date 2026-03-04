namespace FSharp.ATProto.Core

open System.Text.Json
open System.Text.Json.Serialization
open FSharp.ATProto.Syntax
open Microsoft.FSharp.Reflection

/// <summary>
/// Generic JSON converter for "known values" discriminated unions.
/// Reads/writes string values, mapping known strings to fieldless DU cases
/// (via <c>[&lt;JsonName&gt;]</c> attributes) and unknown strings to the <c>Unknown of string</c> fallback case.
/// </summary>
type KnownValueConverter<'T when 'T : equality> () =
    inherit JsonConverter<'T> ()

    static let cases = FSharpType.GetUnionCases (typeof<'T>)

    static let knownCases =
        cases
        |> Array.choose (fun case ->
            if case.GetFields().Length = 0 then
                let jsonName =
                    case.GetCustomAttributesData ()
                    |> Seq.tryFind (fun a -> a.AttributeType.Name.Contains ("JsonName"))
                    |> Option.bind (fun a ->
                        a.ConstructorArguments
                        |> Seq.tryHead
                        |> Option.map (fun arg -> arg.Value :?> string))
                    |> Option.defaultValue case.Name

                Some (jsonName, FSharpValue.MakeUnion (case, [||]) :?> 'T)
            else
                None)

    static let unknownCase =
        cases
        |> Array.tryFind (fun case ->
            case.Name = "Unknown"
            && case.GetFields().Length = 1
            && case.GetFields().[0].PropertyType = typeof<string>)

    static let stringToCase = knownCases |> dict

    static let caseToString = knownCases |> Array.map (fun (s, v) -> (v, s)) |> dict

    override _.Read (reader, _typeToConvert, _options) =
        let s = reader.GetString ()

        match stringToCase.TryGetValue (s) with
        | true, value -> value
        | false, _ ->
            match unknownCase with
            | Some case -> FSharpValue.MakeUnion (case, [| box s |]) :?> 'T
            | None -> failwithf "Unknown value '%s' for type %s" s typeof<'T>.Name

    override _.Write (writer, value, _options) =
        match caseToString.TryGetValue (value) with
        | true, s -> writer.WriteStringValue (s)
        | false, _ ->
            let (_case, fields) = FSharpValue.GetUnionFields (value, typeof<'T>)

            if fields.Length = 1 then
                writer.WriteStringValue (fields.[0] :?> string)
            else
                writer.WriteStringValue (string value)

/// <summary>
/// Factory that intercepts types annotated with <c>[&lt;JsonConverter(typeof&lt;KnownValueConverter&lt;_&gt;&gt;)&gt;]</c>
/// and creates the appropriate <see cref="KnownValueConverter{T}"/>. Must be registered before
/// <c>JsonFSharpConverter</c> in options to take precedence (System.Text.Json checks
/// Options.Converters before type-level attributes).
/// </summary>
type KnownValueConverterFactory () =
    inherit JsonConverterFactory ()

    override _.CanConvert (typeToConvert : System.Type) =
        typeToConvert.GetCustomAttributes (typeof<JsonConverterAttribute>, false)
        |> Array.tryHead
        |> Option.bind (fun attr ->
            let a = attr :?> JsonConverterAttribute
            let ct = a.ConverterType

            if ct <> null && ct.IsGenericType && ct.GetGenericTypeDefinition () = typedefof<KnownValueConverter<_>> then
                Some true
            else
                None)
        |> Option.defaultValue false

    override _.CreateConverter (typeToConvert, _options) =
        let converterType = typedefof<KnownValueConverter<_>>.MakeGenericType ([| typeToConvert |])
        System.Activator.CreateInstance (converterType) :?> JsonConverter

/// <summary>
/// Shared JSON serialization configuration for the AT Protocol.
/// </summary>
module Json =
    /// <summary>
    /// Pre-configured <see cref="JsonSerializerOptions"/> for AT Protocol JSON serialization.
    /// </summary>
    /// <remarks>
    /// The options are configured with:
    /// <list type="bullet">
    ///   <item><description>camelCase property naming (e.g. F# <c>AccessJwt</c> serializes as <c>"accessJwt"</c>).</description></item>
    ///   <item><description>Null fields are omitted when writing (<c>JsonIgnoreCondition.WhenWritingNull</c>).</description></item>
    ///   <item><description>F# discriminated unions use internal <c>$type</c> tag discrimination with named fields
    ///   via <c>FSharp.SystemTextJson</c>.</description></item>
    ///   <item><description>Syntax identifier types (Did, Handle, etc.) serialize as validated JSON strings.</description></item>
    /// </list>
    /// This singleton instance is shared across the <see cref="Xrpc"/> module and all serialization in the library.
    /// </remarks>
    let options =
        let opts = JsonSerializerOptions (PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
        opts.DefaultIgnoreCondition <- JsonIgnoreCondition.WhenWritingNull

        // KnownValueConverterFactory must come before JsonFSharpConverter so that
        // types with [<JsonConverter(typeof<KnownValueConverter<_>>)>] are handled
        // correctly (Options.Converters has higher priority than type-level attributes).
        opts.Converters.Add (KnownValueConverterFactory ())

        opts.Converters.Add (
            JsonFSharpConverter (JsonFSharpOptions.Default().WithUnionInternalTag().WithUnionNamedFields ())
        )

        opts.Converters.Add (DidConverter ())
        opts.Converters.Add (HandleConverter ())
        opts.Converters.Add (AtUriConverter ())
        opts.Converters.Add (CidConverter ())
        opts.Converters.Add (NsidConverter ())
        opts.Converters.Add (TidConverter ())
        opts.Converters.Add (RecordKeyConverter ())
        opts.Converters.Add (AtDateTimeConverter ())
        opts.Converters.Add (LanguageConverter ())
        opts.Converters.Add (SyntaxUriConverter ())
        opts
