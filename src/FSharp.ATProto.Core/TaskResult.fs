namespace FSharp.ATProto.Core

open System.Threading.Tasks

[<AutoOpen>]
module TaskResultCE =

    type TaskResultBuilder () =
        member _.Return (value : 'T) : Task<Result<'T, 'E>> = Task.FromResult (Ok value)

        member _.ReturnFrom (taskResult : Task<Result<'T, 'E>>) : Task<Result<'T, 'E>> = taskResult

        member _.Bind (taskResult : Task<Result<'T, 'E>>, f : 'T -> Task<Result<'U, 'E>>) : Task<Result<'U, 'E>> =
            task {
                match! taskResult with
                | Ok value -> return! f value
                | Error err -> return Error err
            }

        member _.Zero () : Task<Result<unit, 'E>> = Task.FromResult (Ok ())

        member _.Delay (f : unit -> Task<Result<'T, 'E>>) : unit -> Task<Result<'T, 'E>> = f

        member _.Run (f : unit -> Task<Result<'T, 'E>>) : Task<Result<'T, 'E>> = f ()

        member _.Combine
            (taskResult : Task<Result<unit, 'E>>, f : unit -> Task<Result<'T, 'E>>)
            : Task<Result<'T, 'E>> =
            task {
                match! taskResult with
                | Ok () -> return! f ()
                | Error err -> return Error err
            }

        member _.TryWith
            (f : unit -> Task<Result<'T, 'E>>, handler : exn -> Task<Result<'T, 'E>>)
            : Task<Result<'T, 'E>> =
            task {
                try
                    return! f ()
                with ex ->
                    return! handler ex
            }

        member _.TryFinally (f : unit -> Task<Result<'T, 'E>>, compensation : unit -> unit) : Task<Result<'T, 'E>> =
            task {
                try
                    return! f ()
                finally
                    compensation ()
            }

        member _.Using (resource : 'T :> System.IDisposable, f : 'T -> Task<Result<'U, 'E>>) : Task<Result<'U, 'E>> =
            task {
                try
                    return! f resource
                finally
                    resource.Dispose ()
            }

        member this.For (sequence : 'T seq, body : 'T -> Task<Result<unit, 'E>>) : Task<Result<unit, 'E>> =
            task {
                let mutable result = Ok ()
                let enumerator = sequence.GetEnumerator ()

                while enumerator.MoveNext () && Result.isOk result do
                    match! body enumerator.Current with
                    | Ok () -> ()
                    | Error err -> result <- Error err

                return result
            }

        member this.While (guard : unit -> bool, body : unit -> Task<Result<unit, 'E>>) : Task<Result<unit, 'E>> =
            task {
                let mutable result = Ok ()

                while guard () && Result.isOk result do
                    match! body () with
                    | Ok () -> ()
                    | Error err -> result <- Error err

                return result
            }

    let taskResult = TaskResultBuilder ()
