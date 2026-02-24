module TidTests

open Expecto
open FSharp.ATProto.Syntax

[<Tests>]
let tests =
    testList "Tid" [
        testCase "placeholder" <| fun () ->
            Expect.isError (Tid.parse "test") "should not be implemented yet"
    ]
