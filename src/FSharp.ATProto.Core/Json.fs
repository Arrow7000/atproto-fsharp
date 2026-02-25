namespace FSharp.ATProto.Core

open System.Text.Json
open System.Text.Json.Serialization

/// Shared JSON configuration for AT Protocol serialization.
module Json =
    /// JsonSerializerOptions configured for AT Protocol.
    let options =
        let opts = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
        opts.DefaultIgnoreCondition <- JsonIgnoreCondition.WhenWritingNull
        opts.Converters.Add(JsonFSharpConverter(JsonFSharpOptions.Default().WithUnionInternalTag().WithUnionNamedFields()))
        opts
