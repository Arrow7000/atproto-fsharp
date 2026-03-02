// TestDrive.fs — Live integration test that exercises the FSharp.ATProto library
// step by step, with pauses between actions so you can verify each one on Bluesky.
//
// After all steps complete, the program waits for you to quit (Ctrl+C, SIGTERM,
// or closing the terminal). On exit it cleans up: deletes the posts and likes,
// leaving only the re-follow of @adler.dev intact.
//
// To run:
//   dotnet run --project examples/BskyBotExample
//
// Expects .env in the project directory (or environment variables):
//   BSKY_HANDLE=alice-bot-yay.bsky.social
//   BSKY_PASSWORD=your-app-password

open System
open System.IO
open System.Runtime.InteropServices
open System.Threading
open System.Threading.Tasks
open FSharp.ATProto.Core
open FSharp.ATProto.Bluesky
open FSharp.ATProto.Syntax

// ── Helpers ──────────────────────────────────────────────────────────

let loadEnv (path : string) =
    if File.Exists path then
        for line in File.ReadAllLines path do
            let trimmed = line.Trim ()

            if not (String.IsNullOrEmpty trimmed) && not (trimmed.StartsWith "#") then
                match trimmed.IndexOf '=' with
                | -1 -> ()
                | i ->
                    let key = trimmed.[.. i - 1].Trim ()
                    let value = trimmed.[i + 1 ..].Trim ()
                    Environment.SetEnvironmentVariable (key, value)

let env key =
    match Environment.GetEnvironmentVariable key with
    | null -> failwithf "Missing %s — set it in .env or environment" key
    | v -> v

let step number title =
    printfn ""
    printfn "── Step %d: %s ──" number title

let pause (seconds : int) =
    task {
        for i in seconds .. -1 .. 1 do
            printf "\r   Waiting %d seconds...  " i
            do! Task.Delay (1000)

        printf "\r                           \r"
    }

let unwrap label result =
    match result with
    | Ok v ->
        printfn "   OK: %s" label
        v
    | Error (e : XrpcError) ->
        failwithf "   FAIL: %s — %d %s" label e.StatusCode (e.Message |> Option.defaultValue "(no message)")

// ── Main ─────────────────────────────────────────────────────────────

[<EntryPoint>]
let main _ =
    loadEnv (Path.Combine (__SOURCE_DIRECTORY__, ".env"))

    let handle = env "BSKY_HANDLE"
    let password = env "BSKY_PASSWORD"

    printfn ""
    printfn "FSharp.ATProto Test Drive"
    printfn "========================"
    printfn "Account: %s" handle
    printfn ""

    task {
        // ── 1. Login ──
        step 1 "Logging in"

        let! loginResult = Bluesky.login "https://bsky.social" handle password
        let agent = loginResult |> unwrap "Logged in"
        let session = agent.Session.Value
        printfn "   DID: %s" (Did.value session.Did)
        printfn "   Handle: @%s" (Handle.value session.Handle)

        do! pause 5

        // ── 2. Create a post ──
        step 2 "Creating a post"

        let postText =
            sprintf "Test drive from FSharp.ATProto! [%s]" (DateTime.UtcNow.ToString ("yyyy-MM-dd HH:mm:ss"))

        let! postResult = Bluesky.post agent postText
        let post1 = postResult |> unwrap (sprintf "Posted: \"%s\"" postText)
        printfn "   URI: %s" (AtUri.value post1.Uri)

        do! pause 30

        // ── 3. Reply to it ──
        step 3 "Replying to the post"

        let replyText = "And this is a reply to my own post!"
        let! replyResult = Bluesky.replyTo agent replyText post1
        let reply1 = replyResult |> unwrap (sprintf "Replied: \"%s\"" replyText)
        printfn "   URI: %s" (AtUri.value reply1.Uri)

        do! pause 30

        // ── 4. Like the original post ──
        step 4 "Liking the original post"

        let! like1Result = Bluesky.like agent post1
        let like1 = like1Result |> unwrap "Liked original post"
        printfn "   Like URI: %s" (AtUri.value like1.Uri)

        do! pause 30

        // ── 5. Like the reply ──
        step 5 "Liking the reply"

        let! like2Result = Bluesky.like agent reply1
        let like2 = like2Result |> unwrap "Liked reply"
        printfn "   Like URI: %s" (AtUri.value like2.Uri)

        do! pause 30

        // ── 6. Unlike the reply (keep the like on the original) ──
        step 6 "Unliking the reply"

        let! unlikeResult = Bluesky.undo agent like2

        match unlikeResult with
        | Ok Undone -> printfn "   OK: Unliked reply"
        | Ok WasNotPresent -> printfn "   Note: Reply wasn't liked (already undone?)"
        | Error e -> printfn "   FAIL: Unlike — %A" e

        do! pause 30

        // ── 7. Look up @adler.dev ──
        step 7 "Looking up @adler.dev"

        let! profileResult = Bluesky.getProfile agent "adler.dev"
        let adlerProfile = profileResult |> unwrap "Got profile for @adler.dev"

        printfn "   Display name: %s" (adlerProfile.DisplayName |> Option.defaultValue "(none)")

        printfn "   DID: %s" (Did.value adlerProfile.Did)

        let isFollowing =
            adlerProfile.Viewer |> Option.bind (fun v -> v.Following) |> Option.isSome

        printfn "   Currently following: %b" isFollowing

        do! pause 30

        // ── 8. Unfollow @adler.dev ──
        step 8 "Unfollowing @adler.dev"

        match adlerProfile.Viewer |> Option.bind (fun v -> v.Following) with
        | Some followUri ->
            let followRef : FollowRef = { Uri = followUri }
            let! unfollowResult = Bluesky.unfollow agent followRef

            match unfollowResult with
            | Ok () -> printfn "   OK: Unfollowed @adler.dev"
            | Error e -> printfn "   FAIL: Unfollow — %A" e
        | None -> printfn "   Note: Not currently following @adler.dev — skipping"

        do! pause 30

        // ── 9. Re-follow @adler.dev ──
        step 9 "Re-following @adler.dev"

        let! refollowResult = Bluesky.follow agent adlerProfile.Did

        match refollowResult with
        | Ok followRef ->
            printfn "   OK: Re-followed @adler.dev"
            printfn "   Follow URI: %s" (AtUri.value followRef.Uri)
        | Error e -> printfn "   FAIL: Re-follow — %A" e

        // ── All steps done — wait for quit, then clean up ──
        printfn ""
        printfn "── All steps complete! ──"
        printfn ""
        printfn "Everything is live on Bluesky. Take a look around!"
        printfn "Press Ctrl+C (or close terminal) to clean up and exit."
        printfn ""

        // Cleanup deletes the posts and the remaining like, restoring the
        // account to its pre-test-drive state. The @adler.dev follow stays.
        let mutable cleanedUp = 0

        let cleanup () =
            if Interlocked.CompareExchange (&cleanedUp, 1, 0) = 0 then
                printfn ""
                printfn "── Cleaning up ──"

                let work =
                    task {
                        let! r1 = Bluesky.undo agent like1

                        match r1 with
                        | Ok Undone -> printfn "   Unliked original post"
                        | Ok WasNotPresent -> printfn "   Original post like already gone"
                        | Error e -> printfn "   Unlike failed (non-fatal): %A" e

                        let! r2 = Bluesky.deleteRecord agent reply1.Uri

                        match r2 with
                        | Ok () -> printfn "   Deleted reply"
                        | Error e -> printfn "   Delete reply failed (non-fatal): %A" e

                        let! r3 = Bluesky.deleteRecord agent post1.Uri

                        match r3 with
                        | Ok () -> printfn "   Deleted post"
                        | Error e -> printfn "   Delete post failed (non-fatal): %A" e
                    }

                work.GetAwaiter().GetResult ()
                printfn ""
                printfn "   Done — back to initial state (still following @adler.dev)"
                printfn ""

        let exitSignal = new TaskCompletionSource<unit> ()

        // Ctrl+C (SIGINT)
        Console.CancelKeyPress.Add (fun args ->
            args.Cancel <- true
            cleanup ()
            exitSignal.TrySetResult (()) |> ignore)

        // Normal process exit / SIGTERM via .NET
        AppDomain.CurrentDomain.ProcessExit.Add (fun _ -> cleanup ())

        // SIGTERM (explicit — covers `kill <pid>` and docker stop)
        PosixSignalRegistration.Create (
            PosixSignal.SIGTERM,
            fun ctx ->
                ctx.Cancel <- true
                cleanup ()
                exitSignal.TrySetResult (()) |> ignore
        )
        |> ignore

        // SIGHUP (terminal closed)
        PosixSignalRegistration.Create (
            PosixSignal.SIGHUP,
            fun ctx ->
                ctx.Cancel <- true
                cleanup ()
                exitSignal.TrySetResult (()) |> ignore
        )
        |> ignore

        do! exitSignal.Task

        return 0
    }
    |> fun t -> t.GetAwaiter().GetResult ()
