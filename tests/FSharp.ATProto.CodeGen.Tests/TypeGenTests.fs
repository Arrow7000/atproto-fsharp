module TypeGenTests

open Expecto

[<Tests>]
let tests =
    testList "TypeGen" [
        testCase "placeholder" <| fun () ->
            Expect.isTrue true "placeholder"
    ]
