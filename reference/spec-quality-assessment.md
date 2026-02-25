# Spec Quality & Testability Assessment

Per-layer assessment of how well-specified each component is, how testable, and what
the risks are. This guides implementation confidence and strategy.

## Layer: Identifiers (Syntax)

**Verdict: Paved highway.**

- 6 of 7 types have exact regex from spec
- 200+ interop test vectors
- TypeScript and Go implementations produce identical results
- Strategy: "implement regex, run tests, done"

| Type | Regex? | Vectors | Complexity | TS/Go agree? |
|------|--------|---------|------------|-------------|
| TID | Yes | 14 | Trivial | Yes |
| Handle | Yes | 105+ | Trivial | Yes |
| NSID | Yes | ~50 | Low | Yes |
| DID | Yes | ~38 | Low | Yes (minor % edge) |
| RecordKey | Yes | ~28 | Trivial | Yes |
| AT-URI | Compositional | ~40+ | Moderate | Minor divergence |
| CID | Shallow only | 16 | Varies | Different approaches |

**Only risk:** CID is underspecified at syntax level. Shallow regex for syntax; deep validation in DRISL layer.

## Layer: DRISL/CBOR

**Verdict: Well-fenced garden. No dragons.**

- DRISL is a restrictive subset of CBOR -- spec says what's NOT allowed
- Go reference implementation is ~30 lines of config options on a CBOR library
- System.Formats.Cbor Canonical mode gives 70-80% for free
- 126 test vectors (106 DASL + 20 atproto)
- String-only keys eliminate the CBOR sort-order ambiguity

**What System.Formats.Cbor gives free:**
Minimal integers, sorted keys (write), no indefinite-length, no dup keys, UTF-8

**What we add (~10 rules):**
Reject floats, validate sort on READ, string-only keys, only Tag 42, no bignums,
all bytes consumed, CID validation inside Tag 42

**Risks:**
- JSON $link/$bytes layer has only 3 roundtrip fixtures (supplement with FsCheck)
- Must validate key sort order during decode (CborReader doesn't do this)
- CID parsing needs varint decoding (small, well-specified)

## Layer: MST (Merkle Search Tree)

**Verdict: Data structure clear, algorithms unspecified.**

- Key-depth algorithm: fully deterministic, zero ambiguity
- Node CBOR structure: fully specified field by field
- Tree is deterministic: same keys -> same root CID regardless of insertion order
- Correctness verifiable by root CID comparison alone

**BUT:**
- Insertion/deletion algorithms NOT specified -- must port from TypeScript (~850 lines)
- Diff algorithm NOT specified -- TS and Go use different approaches
- Historical bugs in prefix compression, empty tree trimming

**For firehose (read-only):** ~300 lines, well-specified, safe territory
**For repo creation (write):** ~850 lines, port-from-TypeScript strategy

**Test vectors:**
- key_heights.json (9 cases), common_prefix.json (13 cases)
- Hardcoded root CIDs in both TS and Go for known tree configs

## Layer: Lexicon Parser & Code Generator

**Verdict: 75% mechanical, 25% design judgment.**

- Spec is prose-only (no JSON Schema meta-schema)
- Only 10 test vectors for document validation (very thin!)
- 59 record-data validation vectors (decent)
- 25+ documented corner cases where spec is ambiguous (Discussion #4343)
- TypeScript types.ts serves as de facto formal definition

**Mechanical (75%):** JSON parsing, 18 type kinds, ref resolution, HTTP mapping, type generation
**Judgment (25%):** Open union representation, nullable vs optional, unknown type, defaults, tokens

**Known divergences between TS and Go:**
- Named ref/union top-level defs
- null type support
- Open unions with no refs
- Token magic in knownValues

**Strategy:** Follow TypeScript implementation behavior as de facto standard.

## Summary Table

| Layer | Spec Quality | Test Vectors | Verify Correctness? | Confidence |
|-------|-------------|-------------|---------------------|-----------|
| Identifiers | Regex-grade | 200+ | Yes | Very High |
| DRISL/CBOR | Tight subset | 126 | Yes (roundtrip) | High |
| MST (read) | Crystal clear | Root CIDs | Yes (CID compare) | High |
| MST (write) | Shape-only | Root CIDs | Yes, port-not-invent | Medium-High |
| Lexicon parser | Prose, gaps | 10 (thin) | Partially | Medium |
| Lexicon codegen | N/A (design) | 59 data tests | Behavioral testing | Medium |
