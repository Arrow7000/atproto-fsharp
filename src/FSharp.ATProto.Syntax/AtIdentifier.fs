namespace FSharp.ATProto.Syntax

/// <summary>
/// A union type representing an AT Protocol identifier, which can be either a <see cref="Did"/>
/// or a <see cref="Handle"/>. Used in contexts where either identifier type is accepted,
/// such as the authority component of an <see cref="AtUri"/>.
/// </summary>
type AtIdentifier =
    /// <summary>A DID-based identifier.</summary>
    | AtDid of Did
    /// <summary>A handle-based identifier.</summary>
    | AtHandle of Handle

/// <summary>
/// Functions for creating, validating, and extracting data from <see cref="AtIdentifier"/> values.
/// </summary>
module AtIdentifier =
    /// <summary>
    /// Extract the string representation of an AT identifier.
    /// </summary>
    /// <param name="identifier">The AT identifier to extract the value from.</param>
    /// <returns>The identifier string, whether it is a DID or handle.</returns>
    let value = function
        | AtDid d -> Did.value d
        | AtHandle h -> Handle.value h

    /// <summary>
    /// Parse a string as an AT identifier, trying DID first and then handle.
    /// </summary>
    /// <param name="s">
    /// A string that is either a valid DID (e.g. <c>"did:plc:z72i7hdynmk6r22z27h6tvur"</c>)
    /// or a valid handle (e.g. <c>"my-handle.bsky.social"</c>).
    /// </param>
    /// <returns>
    /// <c>Ok</c> with an <see cref="AtIdentifier"/> wrapping either <c>AtDid</c> or <c>AtHandle</c>,
    /// or <c>Error</c> if the string is neither a valid DID nor a valid handle.
    /// </returns>
    let parse (s: string) : Result<AtIdentifier, string> =
        match Did.parse s with
        | Ok d -> Ok (AtDid d)
        | Error _ ->
            match Handle.parse s with
            | Ok h -> Ok (AtHandle h)
            | Error _ -> Error (sprintf "Invalid AT Identifier: %s" s)
