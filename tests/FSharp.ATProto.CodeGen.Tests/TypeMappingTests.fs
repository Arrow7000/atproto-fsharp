module TypeMappingTests

open Expecto

[<Tests>]
let tests =
    testList "TypeMapping" [
        testCase "placeholder" <| fun () ->
            Expect.isTrue true "placeholder"
    ]
