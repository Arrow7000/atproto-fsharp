namespace FSharp.ATProto.Syntax

open System.Text.Json
open System.Text.Json.Serialization

/// <summary>JSON converter for <see cref="Did"/> values. Reads/writes as a JSON string.</summary>
type DidConverter() =
    inherit JsonConverter<Did>()

    override _.Read(reader, _, _) =
        let s = reader.GetString()

        match Did.parse s with
        | Ok v -> v
        | Error msg -> raise (JsonException(sprintf "Invalid DID '%s': %s" s msg))

    override _.Write(writer, v, _) =
        writer.WriteStringValue(Did.value v)

/// <summary>JSON converter for <see cref="Handle"/> values. Reads/writes as a JSON string.</summary>
type HandleConverter() =
    inherit JsonConverter<Handle>()

    override _.Read(reader, _, _) =
        let s = reader.GetString()

        match Handle.parse s with
        | Ok v -> v
        | Error msg -> raise (JsonException(sprintf "Invalid Handle '%s': %s" s msg))

    override _.Write(writer, v, _) =
        writer.WriteStringValue(Handle.value v)

/// <summary>JSON converter for <see cref="AtUri"/> values. Reads/writes as a JSON string.</summary>
type AtUriConverter() =
    inherit JsonConverter<AtUri>()

    override _.Read(reader, _, _) =
        let s = reader.GetString()

        match AtUri.parse s with
        | Ok v -> v
        | Error msg -> raise (JsonException(sprintf "Invalid AT-URI '%s': %s" s msg))

    override _.Write(writer, v, _) =
        writer.WriteStringValue(AtUri.value v)

/// <summary>JSON converter for <see cref="Cid"/> values. Reads/writes as a JSON string.</summary>
type CidConverter() =
    inherit JsonConverter<Cid>()

    override _.Read(reader, _, _) =
        let s = reader.GetString()

        match Cid.parse s with
        | Ok v -> v
        | Error msg -> raise (JsonException(sprintf "Invalid CID '%s': %s" s msg))

    override _.Write(writer, v, _) =
        writer.WriteStringValue(Cid.value v)

/// <summary>JSON converter for <see cref="Nsid"/> values. Reads/writes as a JSON string.</summary>
type NsidConverter() =
    inherit JsonConverter<Nsid>()

    override _.Read(reader, _, _) =
        let s = reader.GetString()

        match Nsid.parse s with
        | Ok v -> v
        | Error msg -> raise (JsonException(sprintf "Invalid NSID '%s': %s" s msg))

    override _.Write(writer, v, _) =
        writer.WriteStringValue(Nsid.value v)

/// <summary>JSON converter for <see cref="Tid"/> values. Reads/writes as a JSON string.</summary>
type TidConverter() =
    inherit JsonConverter<Tid>()

    override _.Read(reader, _, _) =
        let s = reader.GetString()

        match Tid.parse s with
        | Ok v -> v
        | Error msg -> raise (JsonException(sprintf "Invalid TID '%s': %s" s msg))

    override _.Write(writer, v, _) =
        writer.WriteStringValue(Tid.value v)

/// <summary>JSON converter for <see cref="RecordKey"/> values. Reads/writes as a JSON string.</summary>
type RecordKeyConverter() =
    inherit JsonConverter<RecordKey>()

    override _.Read(reader, _, _) =
        let s = reader.GetString()

        match RecordKey.parse s with
        | Ok v -> v
        | Error msg -> raise (JsonException(sprintf "Invalid RecordKey '%s': %s" s msg))

    override _.Write(writer, v, _) =
        writer.WriteStringValue(RecordKey.value v)

/// <summary>JSON converter for <see cref="AtDateTime"/> values. Reads/writes as a JSON string.</summary>
type AtDateTimeConverter() =
    inherit JsonConverter<AtDateTime>()

    override _.Read(reader, _, _) =
        let s = reader.GetString()

        match AtDateTime.parse s with
        | Ok v -> v
        | Error msg -> raise (JsonException(sprintf "Invalid AtDateTime '%s': %s" s msg))

    override _.Write(writer, v, _) =
        writer.WriteStringValue(AtDateTime.value v)

/// <summary>JSON converter for <see cref="Language"/> values. Reads/writes as a JSON string.</summary>
type LanguageConverter() =
    inherit JsonConverter<Language>()

    override _.Read(reader, _, _) =
        let s = reader.GetString()

        match Language.parse s with
        | Ok v -> v
        | Error msg -> raise (JsonException(sprintf "Invalid Language '%s': %s" s msg))

    override _.Write(writer, v, _) =
        writer.WriteStringValue(Language.value v)

/// <summary>
/// JSON converter for <see cref="Uri"/> values. Reads/writes as a JSON string.
/// Named <c>SyntaxUriConverter</c> to avoid collision with <c>System.UriConverter</c>.
/// </summary>
type SyntaxUriConverter() =
    inherit JsonConverter<Uri>()

    override _.Read(reader, _, _) =
        let s = reader.GetString()

        match Uri.parse s with
        | Ok v -> v
        | Error msg -> raise (JsonException(sprintf "Invalid URI '%s': %s" s msg))

    override _.Write(writer, v, _) =
        writer.WriteStringValue(Uri.value v)
