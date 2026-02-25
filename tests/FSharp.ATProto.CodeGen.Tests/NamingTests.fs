module NamingTests

open Expecto

[<Tests>]
let tests =
    testList "Naming" [
        testCase "placeholder" <| fun () ->
            Expect.isTrue true "placeholder"
    ]
