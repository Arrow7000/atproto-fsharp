# Phase 2: DRISL/CBOR Encoding Layer -- Design Document

Date: 2026-02-24

## Goal

Implement the `FSharp.ATProto.DRISL` library: encode/decode AT Protocol data to/from canonical DRISL-CBOR binary format, compute CIDs, and convert between JSON and the internal data model.

## Data Model

```fsharp
[<RequireQualifiedAccess>]
type AtpValue =
    | Null
    | Bool of bool
    | Integer of int64
    | String of string
    | Bytes of byte[]
    | Link of Cid
    | Array of AtpValue list
    | Object of Map<string, AtpValue>
```

No special BlobRef case. Blobs are Objects with convention fields (`$type: "blob"`, `ref`, `mimeType`, `size`). This keeps the core DU clean and matches how CBOR actually encodes them.

## Module Structure

```
src/FSharp.ATProto.DRISL/
    Varint.fs        -- Unsigned varint encoding/decoding (for CID binary format)
    CidBinary.fs     -- Binary CID parsing, construction, base32 encoding
    Drisl.fs         -- CBOR encode/decode via System.Formats.Cbor
    AtpJson.fs       -- JSON <-> AtpValue conversion ($link, $bytes, $type)
```

Dependency: `FSharp.ATProto.Syntax` (for the `Cid` type) + `System.Formats.Cbor` (NuGet).

## DRISL Encoding Rules

DRISL is a deterministic subset of CBOR (RFC 8949). Built on `System.Formats.Cbor` with `CborConformanceMode.Canonical`, which gives us shortest-form integers/lengths, definite-length encoding, and sorted keys on write for free.

Additional rules we enforce:

| Rule | Read | Write |
|------|------|-------|
| Reject all floats | Yes | Yes |
| String-only map keys | Yes | Yes |
| Only Tag 42 allowed | Yes | Yes |
| Validate key sort order | Yes | Automatic |
| No bignum tags (2, 3) | Yes | N/A |
| No extra simple values | Yes | N/A |
| All bytes consumed (no trailing data) | Yes | N/A |
| CID validation inside Tag 42 | Yes | Yes |
| No indefinite-length encoding | Yes (Canonical) | Yes (Canonical) |
| No duplicate map keys | Yes (Canonical) | Yes |

### Key Sort Order

DRISL sorts map keys by the byte-wise lexicographic order of their CBOR-encoded forms. Since all keys are UTF-8 strings with shortest-form length encoding, this is equivalent to: sort by byte-length first, then lexicographically by UTF-8 bytes. `CborConformanceMode.Canonical` (RFC 8949 deterministic) produces the same ordering.

### Tag 42 (CID Links)

Encoded as: CBOR tag 42 (`0xD8 0x2A`) wrapping a byte string containing `0x00` prefix + raw binary CID bytes. On decode, the `0x00` prefix is stripped and the remaining bytes are parsed as a CIDv1.

## CID Implementation

Minimal, covering only the ATProto subset:

- CIDv1 only (version byte `0x01`)
- Codecs: `0x71` (dag-cbor/DRISL) for data, `0x55` (raw) for blobs
- Hash: SHA-256 only (multihash: `0x12 0x20` + 32 bytes)
- String encoding: Base32-lower with `b` multibase prefix
- Binary format: `<varint version><varint codec><varint hash-fn><varint hash-len><hash-bytes>`
- Varint: unsigned LEB128 encoding (used for version, codec, hash function code, hash length)

Functions:
- `CidBinary.compute : byte[] -> Cid` -- SHA-256 hash bytes, construct CIDv1 with dag-cbor codec
- `CidBinary.toBytes : Cid -> byte[]` -- CID string to raw binary
- `CidBinary.fromBytes : byte[] -> Result<Cid, string>` -- raw binary to CID string

## JSON Conversion

`AtpJson` module handles AT Protocol JSON conventions:

| JSON | AtpValue |
|------|----------|
| `{"$link": "bafy..."}` | `Link of Cid` |
| `{"$bytes": "base64..."}` | `Bytes of byte[]` |
| `null` | `Null` |
| `true`/`false` | `Bool` |
| integer | `Integer` |
| `123.0` (integer-like float) | `Integer` |
| string | `String` |
| array | `Array` |
| object | `Object` |

Validation rules:
- `$link` objects must have exactly one key, value must be a valid CID string
- `$bytes` objects must have exactly one key, value must be a base64 string
- `$type` if present must be a non-empty string
- True floating-point values (e.g., `123.456`) are rejected
- Top-level value must be an object (for record validation, checked separately)

## Testing

### Interop test vectors (19 cases)

- 3 fixture roundtrips: JSON -> encode -> CBOR bytes match expected, CID matches expected
- 5 valid JSON cases: accepted without error
- 11 invalid JSON cases: rejected with appropriate error

### FsCheck properties

- `decode(encode(x)) = Ok x` for generated AtpValue trees
- `fromJson(toJson(x)) = Ok x` for generated AtpValue trees
- Encoded output never contains float bytes
- Map keys always in DRISL sort order
- All computed CIDs are CIDv1 with SHA-256

### Unit tests

- Varint encoding/decoding edge cases (0, 1, 127, 128, large values)
- CID string <-> binary roundtrip
- Known CID computation (hash known input, compare to expected CID)
- CBOR edge cases: empty map, nested structures, unicode strings, large integers
