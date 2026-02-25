# Testing Strategy Reference

## Test Data Sources

### atproto-interop-tests (github.com/bluesky-social/atproto-interop-tests)
- `syntax/` (22 files): Valid/invalid for TID, Handle, DID, NSID, AT-URI, CID, DateTime, RecordKey, Language, URI, AtIdentifier
- `data-model/` (3 files): JSON-CBOR-CID triplets, valid/invalid data model values
- `mst/` (4 files): key_heights.json, common_prefix.json, example_keys.txt
- `crypto/` (3 files): signature-fixtures.json, w3c_didkey_K256.json, w3c_didkey_P256.json
- `lexicon/` (5+ files): Valid/invalid schemas, record validation data (56 invalid + 3 valid)
- `firehose/` (1 file): commit-proof-fixtures.json

### DASL Testing Fixtures (github.com/hyphacoop/dasl-testing)
- 106 CBOR test vectors: 32 roundtrip, 66 invalid_in, 8 invalid_out
- Coverage: CID/Tag42, floats, minimality, map keys, simple values, tags, indefinite length, integers, UTF-8

### Hardcoded Root CIDs (from TS/Go implementations)
- Empty tree: bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm
- Single entry: bafyreibj4lsc3aqnrvphp5xmrnfoorvru4wynt6lwidqbm2623a6tatzdu
- Single layer-2: bafyreih7wfei65pxzhauoibu3ls7jgmkju4bspy4t2ha2qdjnzqvoy33ai
- 5-entry tree: bafyreicmahysq4n6wfuxo522m6dpiy7z7qzym3dzs756t5n7nfdgccwq7m

## FsCheck Property-Based Tests

### Roundtrip Properties
- CBOR: decode(encode(value)) = Ok value
- JSON: fromJson(toJson(x)) = Ok x
- TID/CID/identifiers: parse(format(x)) = Ok x

### Ordering/Determinism
- TID monotonicity: sequential next() produces strictly increasing values
- MST determinism: same key-value set -> same root CID regardless of insertion order
- CBOR map keys: always sorted bytewise lexicographic

### Structural Invariants
- MST: height(key) = countLeadingZeroPairs(SHA256(key))
- MST: no empty leaf nodes after any operation sequence
- CID: always CIDv1, SHA-256, correct codec
- No-float: no floating-point anywhere in any AtpValue

### Idempotence
- MST insert-then-delete returns to original tree
- Signature verify(sign(msg, key), pubkey) = true

## Integration Testing

### Local PDS (Docker)
- Image: ghcr.io/bluesky-social/pds:0.4
- Create account, create post, read back, verify content
- Test session auth (createSession/refreshSession)
- Test pagination, error handling, rate limiting

### Behavioral Testing
- Compare output against FishyFlip/idunno.Bluesky for same endpoints
- No runtime dependency, just verification
