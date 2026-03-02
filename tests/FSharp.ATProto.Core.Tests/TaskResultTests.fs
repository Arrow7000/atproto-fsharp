module TaskResultTests

open Expecto
open System.Threading.Tasks
open FSharp.ATProto.Core

[<Tests>]
let tests =
    testList
        "TaskResult CE"
        [ testCase "return wraps value in Ok"
          <| fun () ->
              let result = (taskResult { return 42 }).GetAwaiter().GetResult ()
              Expect.equal result (Ok 42) "should wrap in Ok"

          testCase "bind Ok continues"
          <| fun () ->
              let result =
                  (taskResult {
                      let! x = Task.FromResult (Ok 1)
                      let! y = Task.FromResult (Ok 2)
                      return x + y
                  })
                      .GetAwaiter()
                      .GetResult ()

              Expect.equal result (Ok 3) "should sum"

          testCase "bind Error short-circuits"
          <| fun () ->
              let mutable reached = false

              let result =
                  (taskResult {
                      let! _ = Task.FromResult (Error "boom")
                      reached <- true
                      return 42
                  })
                      .GetAwaiter()
                      .GetResult ()

              Expect.equal result (Error "boom") "should be Error"
              Expect.isFalse reached "should not reach after Error"

          testCase "returnFrom passes through"
          <| fun () ->
              let result =
                  (taskResult { return! Task.FromResult (Ok "hello") }).GetAwaiter().GetResult ()

              Expect.equal result (Ok "hello") "should pass through"

          testCase "returnFrom Error passes through"
          <| fun () ->
              let result =
                  (taskResult { return! Task.FromResult (Error "fail") }).GetAwaiter().GetResult ()

              Expect.equal result (Error "fail") "should pass through Error"

          testCase "nested taskResult works"
          <| fun () ->
              let inner () = taskResult { return 10 }

              let result =
                  (taskResult {
                      let! x = inner ()
                      return x * 2
                  })
                      .GetAwaiter()
                      .GetResult ()

              Expect.equal result (Ok 20) "nested should work"

          testCase "try-with catches exceptions"
          <| fun () ->
              let result =
                  (taskResult {
                      try
                          failwith "test error"
                          return 42
                      with ex ->
                          return -1
                  })
                      .GetAwaiter()
                      .GetResult ()

              Expect.equal result (Ok -1) "should catch and recover"

          testCase "using disposes resource"
          <| fun () ->
              let disposed = ref false

              let result =
                  (taskResult {
                      use _ =
                          { new System.IDisposable with
                              member _.Dispose () = disposed.Value <- true }

                      return 42
                  })
                      .GetAwaiter()
                      .GetResult ()

              Expect.equal result (Ok 42) "should return value"
              Expect.isTrue disposed.Value "should have disposed" ]
