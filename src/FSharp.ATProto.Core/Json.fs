namespace FSharp.ATProto.Core

open System.Text.Json
open System.Text.Json.Serialization
open FSharp.ATProto.Syntax

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
        let opts = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
        opts.DefaultIgnoreCondition <- JsonIgnoreCondition.WhenWritingNull
        opts.Converters.Add(JsonFSharpConverter(JsonFSharpOptions.Default().WithUnionInternalTag().WithUnionNamedFields()))
        opts.Converters.Add(DidConverter())
        opts.Converters.Add(HandleConverter())
        opts.Converters.Add(AtUriConverter())
        opts.Converters.Add(CidConverter())
        opts.Converters.Add(NsidConverter())
        opts.Converters.Add(TidConverter())
        opts.Converters.Add(RecordKeyConverter())
        opts.Converters.Add(AtDateTimeConverter())
        opts.Converters.Add(LanguageConverter())
        opts.Converters.Add(SyntaxUriConverter())
        opts
