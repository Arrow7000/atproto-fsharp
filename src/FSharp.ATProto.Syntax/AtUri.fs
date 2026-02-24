namespace FSharp.ATProto.Syntax

type AtUri = private AtUri of string

module AtUri =
    let value (AtUri s) = s

    let parse (s: string) : Result<AtUri, string> =
        if isNull s then Error "AT-URI cannot be null"
        elif s.Length > 8192 then Error "AT-URI exceeds max length of 8KB"
        elif not (s.StartsWith("at://")) then Error "AT-URI must start with 'at://'"
        elif s.Contains('?') || s.Contains('#') then Error "AT-URI must not contain query or fragment"
        else
            let rest = s.Substring(5)
            if rest.Length = 0 then Error "AT-URI must have an authority"
            elif rest.EndsWith("/") then Error "AT-URI must not have a trailing slash"
            else
                let parts = rest.Split('/', 3)
                let authorityStr = parts.[0]
                let authorityResult =
                    if authorityStr.StartsWith("did:") then Did.parse authorityStr |> Result.map ignore
                    else Handle.parse authorityStr |> Result.map ignore
                match authorityResult with
                | Error e -> Error (sprintf "Invalid AT-URI authority: %s" e)
                | Ok _ ->
                    if parts.Length >= 2 then
                        match Nsid.parse parts.[1] with
                        | Error e -> Error (sprintf "Invalid AT-URI collection: %s" e)
                        | Ok _ ->
                            if parts.Length >= 3 then
                                match RecordKey.parse parts.[2] with
                                | Error e -> Error (sprintf "Invalid AT-URI record key: %s" e)
                                | Ok _ -> Ok (AtUri s)
                            else Ok (AtUri s)
                    else Ok (AtUri s)
