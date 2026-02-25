module FSharp.ATProto.CodeGen.Program

open System
open System.IO
open FSharp.ATProto.Lexicon
open FSharp.ATProto.CodeGen.NamespaceGen

let private parseArgs (args: string array) : Result<string * string, string> =
    let mutable lexdir = None
    let mutable outdir = None
    let mutable i = 0

    while i < args.Length do
        match args.[i] with
        | "--lexdir" when i + 1 < args.Length ->
            lexdir <- Some args.[i + 1]
            i <- i + 2
        | "--outdir" when i + 1 < args.Length ->
            outdir <- Some args.[i + 1]
            i <- i + 2
        | other ->
            i <- i + 1

    match lexdir, outdir with
    | Some l, Some o -> Ok(l, o)
    | None, _ -> Error "Missing required argument: --lexdir <path>"
    | _, None -> Error "Missing required argument: --outdir <path>"

[<EntryPoint>]
let main args =
    match parseArgs args with
    | Error msg ->
        eprintfn "Error: %s" msg
        eprintfn "Usage: FSharp.ATProto.CodeGen --lexdir <path> --outdir <path>"
        1
    | Ok(lexdir, outdir) ->
        // Find all *.json files recursively
        let jsonFiles =
            Directory.GetFiles(lexdir, "*.json", SearchOption.AllDirectories)
            |> Array.sort

        printfn "Found %d lexicon JSON files in %s" jsonFiles.Length lexdir

        // Parse each file
        let mutable parsed = []
        let mutable failures = 0

        for file in jsonFiles do
            let json = File.ReadAllText(file)

            match LexiconParser.parse json with
            | Ok doc -> parsed <- doc :: parsed
            | Error err ->
                failures <- failures + 1
                eprintfn "  WARNING: Failed to parse %s: %s" (Path.GetRelativePath(lexdir, file)) err

        let docs = List.rev parsed
        printfn "Successfully parsed %d lexicons (%d failures)" docs.Length failures

        // Generate all namespace files
        let files = generateAll docs
        printfn "Generated %d namespace files" files.Length

        // Ensure output directory exists
        Directory.CreateDirectory(outdir) |> ignore

        // Write each file
        for (fileName, content) in files do
            let filePath = Path.Combine(outdir, fileName)
            File.WriteAllText(filePath, content)
            printfn "  Wrote %s" fileName

        // Print Compile entries for .fsproj
        printfn ""
        printfn "Add these <Compile Include> entries to your .fsproj:"
        printfn ""

        for (fileName, _) in files do
            printfn "    <Compile Include=\"Generated/%s\" />" fileName

        printfn ""
        printfn "Done."
        0
