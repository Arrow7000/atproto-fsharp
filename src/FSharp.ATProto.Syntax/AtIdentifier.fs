namespace FSharp.ATProto.Syntax

type AtIdentifier =
    | AtDid of Did
    | AtHandle of Handle

module AtIdentifier =
    let value = function
        | AtDid d -> Did.value d
        | AtHandle h -> Handle.value h

    let parse (s: string) : Result<AtIdentifier, string> =
        match Did.parse s with
        | Ok d -> Ok (AtDid d)
        | Error _ ->
            match Handle.parse s with
            | Ok h -> Ok (AtHandle h)
            | Error _ -> Error (sprintf "Invalid AT Identifier: %s" s)
