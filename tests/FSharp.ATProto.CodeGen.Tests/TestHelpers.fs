module TestHelpers

open System.IO
open System.Text.Json

let solutionRoot =
    let rec findRoot (dir: DirectoryInfo) =
        if File.Exists(Path.Combine(dir.FullName, "FSharp.ATProto.sln")) then
            dir.FullName
        elif dir.Parent <> null then
            findRoot dir.Parent
        else
            failwith "Could not find solution root"
    findRoot (DirectoryInfo(Directory.GetCurrentDirectory()))

let lexiconDir =
    Path.Combine(solutionRoot, "extern", "atproto", "lexicons")

let loadInteropJson (relativePath: string) : JsonDocument =
    let fullPath = Path.Combine(solutionRoot, "extern", "atproto-interop-tests", relativePath)
    JsonDocument.Parse(File.ReadAllText(fullPath))
