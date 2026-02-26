namespace FSharp.ATProto.Bluesky

open System.Text.Json
open System.Text.Json.Serialization
open FSharp.ATProto.Syntax

/// Extension properties on PostView for convenient access to post content.
[<AutoOpen>]
module PostExtensions =

    /// JSON options configured for deserializing AT Protocol record types.
    /// Includes UnwrapRecordCases which is required for union types with internal $type tags.
    let private recordOptions =
        let opts = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
        opts.DefaultIgnoreCondition <- JsonIgnoreCondition.WhenWritingNull

        opts.Converters.Add(
            JsonFSharpConverter(
                JsonFSharpOptions
                    .Default()
                    .WithUnionInternalTag()
                    .WithUnionUnwrapSingleFieldCases()
                    .WithUnionUnwrapRecordCases()
                    .WithUnionTagName("$type")
            )
        )

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

    type AppBskyFeed.Defs.PostView with

        /// Deserialize the raw record into a typed Bluesky post. None if not a post record.
        member this.AsPost: AppBskyFeed.Post.Post option =
            match this.Record.TryGetProperty("$type") with
            | true, v when v.GetString() = AppBskyFeed.Post.TypeId ->
                try
                    JsonSerializer.Deserialize<AppBskyFeed.Post.Post>(this.Record, recordOptions)
                    |> Some
                with
                | _ -> None
            | _ -> None

        /// Post text (empty string if not a post record).
        member this.Text: string =
            match this.Record.TryGetProperty("text") with
            | true, v when v.ValueKind = JsonValueKind.String -> v.GetString()
            | _ -> ""

        /// Post facets (empty list if none).
        member this.Facets: AppBskyRichtext.Facet.Facet list =
            match this.AsPost with
            | Some post ->
                match post.Facets with
                | Some facets -> facets
                | None -> []
            | None -> []
