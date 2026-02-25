module IntegrationTests

open Expecto

[<Tests>]
let tests =
    testList "Integration" [
        testCase "placeholder" <| fun () ->
            Expect.isTrue true "placeholder"
    ]
