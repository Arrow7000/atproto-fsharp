namespace FSharp.ATProto.Pds

open System
open System.Collections.Concurrent
open System.Security.Cryptography
open System.Text.Json
open FSharp.ATProto.Syntax
open FSharp.ATProto.Crypto
open Microsoft.AspNetCore.Builder

/// Fired when a new account is created on the PDS.
type AccountCreatedEvent =
    { Did : Did
      Handle : Handle }

/// Fired when a record is created in a repository.
type RecordCreatedEvent =
    { Did : Did
      Collection : string
      Rkey : string
      Uri : AtUri }

/// Fired when a record is deleted from a repository.
type RecordDeletedEvent =
    { Did : Did
      Collection : string
      Rkey : string }

type PdsAccount =
    { Did : Did
      Handle : Handle
      PasswordHash : byte[]
      PasswordSalt : byte[]
      SigningKey : KeyPair
      CreatedAt : DateTimeOffset }

type SessionInfo =
    { AccessToken : string
      RefreshToken : string
      Did : Did
      Handle : Handle
      AccessExpiresAt : DateTimeOffset
      RefreshExpiresAt : DateTimeOffset }

type StoredRecord =
    { Uri : AtUri
      Cid : string
      Collection : string
      Rkey : string
      Value : JsonElement }

type PdsState =
    { Accounts : ConcurrentDictionary<string, PdsAccount>
      AccountsByDid : ConcurrentDictionary<string, PdsAccount>
      Sessions : ConcurrentDictionary<string, SessionInfo>
      RefreshIndex : ConcurrentDictionary<string, string>
      Records : ConcurrentDictionary<string, StoredRecord>
      Config : PdsBuilder
      SigningKey : KeyPair
      ServiceDid : Did }

and PdsBuilder =
    { Hostname : string
      Port : int
      SigningKey : KeyPair option
      AdminPassword : string option
      InviteRequired : bool
      InviteCode : string option
      AccessTokenLifetime : TimeSpan
      RefreshTokenLifetime : TimeSpan
      OnAccountCreated : (AccountCreatedEvent -> unit) option
      OnRecordCreated : (RecordCreatedEvent -> unit) option
      OnRecordDeleted : (RecordDeletedEvent -> unit) option }

/// A running PDS instance that can be interacted with programmatically.
type RunningPds =
    { App : WebApplication
      Url : string
      State : PdsState }

module PdsBuilder =

    let create (hostname : string) : PdsBuilder =
        { Hostname = hostname
          Port = 2583
          SigningKey = None
          AdminPassword = None
          InviteRequired = false
          InviteCode = None
          AccessTokenLifetime = TimeSpan.FromHours 2.0
          RefreshTokenLifetime = TimeSpan.FromDays 90.0
          OnAccountCreated = None
          OnRecordCreated = None
          OnRecordDeleted = None }

module internal Passwords =

    let hash (password : string) : byte[] * byte[] =
        let salt = RandomNumberGenerator.GetBytes 16

        let hash =
            Rfc2898DeriveBytes.Pbkdf2 (
                password,
                salt,
                iterations = 100_000,
                hashAlgorithm = HashAlgorithmName.SHA256,
                outputLength = 32
            )

        hash, salt

    let verify (password : string) (storedHash : byte[]) (salt : byte[]) : bool =
        let computed =
            Rfc2898DeriveBytes.Pbkdf2 (
                password,
                salt,
                iterations = 100_000,
                hashAlgorithm = HashAlgorithmName.SHA256,
                outputLength = 32
            )

        CryptographicOperations.FixedTimeEquals (ReadOnlySpan computed, ReadOnlySpan storedHash)

module internal Tokens =

    let generate () : string =
        let bytes = RandomNumberGenerator.GetBytes 32
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')

module internal RecordKeys =

    let generate () : string =
        let chars = "234567abcdefghijklmnopqrstuvwxyz"
        let bytes = RandomNumberGenerator.GetBytes 13
        String [| for b in bytes -> chars.[int b % chars.Length] |]

    let recordKey (did : Did) (collection : string) (rkey : string) : string =
        sprintf "%s/%s/%s" (Did.value did) collection rkey

    let computeCid (json : JsonElement) : string =
        let bytes = System.Text.Encoding.UTF8.GetBytes (json.GetRawText ())
        let hash = SHA256.HashData bytes
        let b32 = Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_')
        sprintf "bafyrei%s" (b32.ToLowerInvariant().Substring (0, 22))
