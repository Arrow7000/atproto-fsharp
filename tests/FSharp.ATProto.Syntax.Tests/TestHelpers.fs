module TestHelpers

open System.IO

/// Load non-empty lines from an interop test file
let loadTestLines (relativePath: string) =
    let basePath =
        let rec findRoot (dir: DirectoryInfo) =
            if File.Exists(Path.Combine(dir.FullName, "FSharp.ATProto.sln")) then
                dir.FullName
            elif dir.Parent <> null then
                findRoot dir.Parent
            else
                failwith "Could not find solution root"
        findRoot (DirectoryInfo(Directory.GetCurrentDirectory()))
    let fullPath = Path.Combine(basePath, "extern", "atproto-interop-tests", relativePath)
    File.ReadAllLines(fullPath)
    |> Array.filter (fun line -> line.Length > 0 && not (line.StartsWith("#")))
    |> Array.distinct
