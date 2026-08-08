# BatchFileProcessor

A .NET 8 worker service that ingests large, sequential **batch files** — fixed-width or delimited — and
publishes their records to upstream systems as confirmed messages.

The engine is a **generic raw slicer**. It reads the layouts a folder may receive and asks only five
questions:

1. Which of these layouts is this file — the one whose framing it is actually made of?
2. How is it framed — fixed-width records, or rows separated by a delimiter?
3. What are the fields, and where does each one start?
4. Can I slice each field out and hand back its text exactly as it appeared?
5. Which fields does the layout say to encrypt, and which does it say to publish?

It answers nothing else. It does not know what any field *means*, does not interpret values (types, scale,
sign, dates), and has no notion of any particular business domain. Every field name, every position, every
`encrypt`/`required`/`skip` flag lives in the layout YAML. Swap the layout and you have a new format, with
zero code changes.

Layouts under [`docs/layouts/`](docs/layouts) are worked examples of the format, not part of the engine.

## What it guarantees

- **A file is read by the layout it belongs to, or by none.** A folder may receive several versions of a
  format. Each file is matched to exactly one of the layouts its profile declares, using what those layouts
  already say about their own framing — nothing extra is configured and no code knows the format. A file
  matching none, or more than one, is quarantined rather than run through whichever was declared first.
- **Nothing ships from a structurally broken file.** The whole file is framed in a first pass before a
  single record is published, so a wrong trailer marker, a short file, or a row the layout cannot classify
  stops the run with no partial publish behind it.
- **At-least-once delivery.** A batch's watermark advances only *after* the broker confirms the publish, so
  an interrupted run resumes the contiguous confirmed prefix — a record is never lost.
- **Every line is processed or rejected.** A structurally valid record is published; a record that fails
  field validation is encrypted and routed to the reject queue. A single bad line never fails the batch. A
  file is archived to `done/` only after every line has been delivered-or-rejected.
- **Deterministic, dedup-friendly identity.** Each message carries a content-derived id
  (`{FileId}-{BatchSeq}` for batches, `{FileId}-{RecordSeq}-reject` for rejects, where `FileId` is the
  file's SHA-256). It is stamped onto the transport envelope as a deterministic `MessageId`, so a replay
  carries the same id and brokers / consumers can deduplicate it. Delivery is *at-least-once*; combined with
  an idempotent consumer this is **effectively-once**.
- **Encryption from the layout.** Fields the layout flags `encrypt: true` are encrypted (AES-256-GCM,
  self-describing envelope) before publish; a rejected record's raw content is encrypted too, because
  nothing classified it. A field is encrypted or it is carried in clear — there is no partially-revealed
  form, and the engine has no opinion on which fields deserve which.
- **The file cannot change under the run.** The second pass recomputes the SHA-256 and compares it to the
  first; a mismatch aborts rather than publishing a blend of two versions.
- **Per-profile isolation.** One worker + pipeline is built per profile and run concurrently, so a backlog
  or a slow file in one folder does not stall another's processing. (The broker connection, checkpoint
  store, and host resources are shared.)
- **Fail-closed.** Missing config or an unknown format/transport fails fast at **startup**. At **runtime**,
  a structural fault or an exhausted publish retry quarantines the affected file to `failed/` with its
  watermark preserved for a clean re-drive, never proceeding on ambiguous state.

> Completeness reconciliation (e.g. trailer control totals) and duplicate suppression are **downstream**
> responsibilities: the trailer record is published like any other with its raw counts, and every message
> carries a deterministic dedup key. The engine deliberately performs no value interpretation.

## Workflow

```text
                         ┌──────────────────────────────────────────────┐
                         │  layout.yaml     (what the file IS)          │
                         │  profiles.yaml   (which folder → which layout)│
                         └───────────────────────┬──────────────────────┘
                                                 │ read once at startup
                                                 ▼
  incoming/                                ┌───────────┐
     │  file lands                         │  PROFILE  │  one worker + pipeline per profile,
     │                                     │  WORKER   │  all running concurrently
     ▼                                     └─────┬─────┘
  ┌───────────────────┐  size stable?            │ poll
  │ completion guard  │──── no ──▶ leave it      │
  └─────────┬─────────┘                          │
            │ yes                                │
            ▼                                    │
  ┌───────────────────┐                          │
  │ claim: atomic     │◀─────────────────────────┘
  │ move to processing│   (crash recovery re-offers anything left in processing/)
  └─────────┬─────────┘
            │
            ▼
  ┌──────────────────────────────────────────────────────────────────────────────────────┐
  │ WHICH LAYOUT IS THIS FILE?   ask each layout the profile declares: could you frame it?│
  │   · fixed-width answers from its own record length — is the file a whole number       │
  │     of records?   (1200-byte records and 2400-byte records cannot both be right)      │
  │   · exactly one must say yes                                                          │
  └───────────────────┬──────────────────────────────────────┬───────────────────────────┘
                      │ none, or more than one               │ exactly one
                      ▼                                      │
                  failed/                                    │  its version is what
            (unattributable — never                          │  provenance will carry
             guessed at, never read)                         │
                                                             ▼
  ══════════════════════════════ PASS 1 — validate whole file ══════════════════════════════
  ┌──────────────────────────────────────────────────────────────────────────────────────┐
  │ stream the file → frame every record → classify every row → discard the content      │
  │   · fixed-width: fixed stride from the layout                                        │
  │   · delimited:   scan to the declared terminator                                     │
  │ computes FileId (SHA-256) on the way through                                         │
  └────────────────────────────────────┬─────────────────────────────────────────────────┘
                                       │
                 structural fault ◀─────┴─────▶ file is well-formed
                 (bad marker, short file,             │
                  unclassifiable row)                 │  nothing has been published yet
                        │                             │
                        ▼                             ▼
                    failed/                 ┌───────────────────┐
                  (watermark kept)          │ load watermark    │ resume from the last
                                            │ from checkpoint   │ confirmed batch
                                            └─────────┬─────────┘
                                                      │
  ══════════════════════════════ PASS 2 — slice and publish ══════════════════════════════
                                                      ▼
  ┌──────────────────────────────────────────────────────────────────────────────────────┐
  │ READER            frame record ──▶ resolve its row/record type from the layout        │
  └────────────────────────────────────┬─────────────────────────────────────────────────┘
                                       ▼
  ┌──────────────────────────────────────────────────────────────────────────────────────┐
  │ PARSER            slice each field by position/index — raw text, spaces preserved     │
  └───────┬───────────────────────┬──────────────────────────────┬───────────────────────┘
          │ type marked skip      │ field count wrong,           │ valid
          │                       │ or required field blank      │
          ▼                       ▼                              ▼
      consumed,            ┌─────────────┐            ┌────────────────────┐
      never emitted        │ encrypt the │            │ encrypt the fields │
      (control row)        │ whole raw   │            │ the layout flags   │
                           │ record      │            └─────────┬──────────┘
                           └──────┬──────┘                      ▼
                                  ▼                       ┌───────────┐
                            reject queue                  │  batcher  │ seal on count/bytes
                            (confirmed)                   └─────┬─────┘
                                  │                             ▼
                                  │                   ┌───────────────────┐
                                  │                   │ bounded channel   │ caps memory —
                                  │                   │                   │ O(1) in file size
                                  │                   └─────────┬─────────┘
                                  │                             ▼
                                  │                   ┌───────────────────┐
                                  │                   │ N publishers      │ fan-out; confirms
                                  │                   │ (parallel)        │ may arrive out of order
                                  │                   └─────────┬─────────┘
                                  │                             │ broker confirms
                                  │                             ▼
                                  │                   ┌───────────────────┐
                                  │                   │ advance watermark │ contiguous confirmed
                                  │                   │ + checkpoint      │ prefix only
                                  │                   └─────────┬─────────┘
                                  │                             │
                                  └──────────────┬──────────────┘
                                                 ▼
                                    ┌─────────────────────────┐
                                    │ re-check SHA-256 ==     │ mismatch → the file changed
                                    │ PASS 1 FileId           │ mid-run → abort to failed/
                                    └────────────┬────────────┘
                                                 ▼
                                    ┌─────────────────────────┐
                                    │ clear checkpoint        │
                                    │ move to done/           │
                                    └─────────────────────────┘
```

Memory is O(1) in file size — records stream through a bounded buffer — so multi-gigabyte files are handled
sequentially. Publishing is the only network-bound stage, so it is the only one fanned out.

## What a layout can declare

Everything below is a YAML edit and never a code change.

| | Fixed-width | Delimited |
|---|---|---|
| Framing | `recordLength`, `terminator` length | `delimiter`, `terminator` character |
| Which file is which | record length: a file is a whole number of records | not yet distinguishable — a profile declares one delimited layout |
| Separator | — | any text: a character, a hex escape (`\x1F`), several characters (`~\|~`), or the aliases `tab`/`space`/`lf`/`cr` |
| Encoding | declared per layout (any the platform supplies, incl. code pages) | same |
| Record/row types | identified by a discriminator at a byte position | header/trailer by position; body types by a marker column |
| Several body types | yes, by discriminator | yes — each names itself with a `match`, all in the same column |
| Unrecognised body row | rejected as an unknown record type | resolves to the type that declares no `match`, if the layout declares one; otherwise the file fails closed |
| Per field | `name`, `start`, `length` | `name`, `index` |
| Per-field flags | `encrypt`, `required`, `skip` | `encrypt`, `required`, `skip` |
| Coverage rule | fields must tile the record with no gaps | field indexes must cover `0..n-1` with no gaps |

Field types, scale, sign, and date formats are deliberately absent — those are the consumer's concern.

## Projects

Variability axes (source / format / transport / checkpoint) sit behind ports wired only at the host;
single-implementation support libraries are concrete references (no speculative ports).

| Project | Responsibility |
|---|---|
| `Common.FileIngestion.Abstractions` | Ports (`IFileSource`, `IRecordParser`, `ICheckpointStore`, `ICompletionGuard`) + primitives |
| `Common.FileIngestion.Layouts` | Layout model + YAML loaders for both framings; field-boundary splitting |
| `Common.FileIngestion.Reading` | Stream framers (fixed-width + delimited) and single-pass SHA-256 (`FileId`) |
| `Common.FileIngestion.Parsing` | The raw slicers: `FixedLengthRecordParser`, `DelimitedRecordParser` |
| `Common.FileIngestion.Protection` | Record protector (field + payload encryption) |
| `Common.FileIngestion.Sources.Folder` | Folder file source + stable-size completion guard |
| `Common.FileIngestion.Checkpointing` | File-based watermark store (same-volume resume) |
| `Common.FileIngestion.Checkpointing.Redis` | Redis watermark store (cross-instance resume) |
| `Common.FileIngestion.Telemetry` | Ingestion metrics + tracing |
| `Common.FileIngestion` | The engine: pipeline, batching, lineage, health, rejecting |
| `Common.Messaging.Contracts` | Message contracts + `IMessagePublisher` port |
| `Common.Messaging.MassTransit` | MassTransit adapter, send-retry, deterministic envelope ids |
| `Common.Security.DataProtection` | AES-256-GCM crypto, field/payload protectors, key providers |
| `Common.Observability` | OpenTelemetry wiring, run/correlation context, log redaction |
| `Ingestion.Worker` | Composition root: loads profiles, builds one worker + pipeline per profile |

Each `src` project has a matching mocked-unit-test project under `src/tests/`.

## Configuration — three layers, three owners

| Layer | Owns |
|---|---|
| **Layout YAML** | What a file *is*: framing, record/row types, fields, and the `encrypt`/`required`/`skip` flags |
| **`profiles.yaml`** | Operational routing: folders → layouts/format/completion/destinations/batch limits. `layout` for one, `layouts` for several. One profile = one concurrent worker. See [`docs/profiles.yaml`](docs/profiles.yaml) |
| **`appsettings` / Helm / Key Vault** | Shared infra & **secrets**: broker + checkpoint connection strings, tuning, observability |

Broker and checkpoint connection strings live in `appsettings`/Key Vault and **never** in `profiles.yaml`.
Adding a folder is a `profiles.yaml` edit; a new format of either kind is a layout YAML edit — both
zero-code.

Which log keys get redacted is derived from the layouts themselves: every field any loaded layout flags
`encrypt` becomes a redacted structured-log key. No field name is hardcoded anywhere.

**Currently supported:** `fixed-length` and `delimited` formats, RabbitMQ transport, File/Redis checkpoint,
stable-size completion. RFC 4180 quoting is not implemented — a row that does not split into exactly the
declared number of fields is rejected, so its absence can reject data but never mis-map it. Other transports
(Kafka/Azure Service Bus) plug in at their existing seams when a concrete case arrives.

## Build, test, run

```bash
# Build
dotnet build BatchFileProcessor.sln

# Test (mocked unit tests + 90% line-coverage gate per project)
dotnet test BatchFileProcessor.sln

# Run the worker (expects config + a reachable broker/checkpoint)
dotnet run --project src/Ingestion.Worker
```

The worker exposes `GET /health/live` (heartbeat staleness) and `GET /health/ready` (publish-outcome gate).
The default `appsettings.json` uses container paths (`/config`, `/data`); point `Ingestion:ProfilesPath`,
the profile folders, and the layout path at local directories for a local run.

## Quality gates & conventions

- **Central Package Management** — versions in `Directory.Packages.props`; shared settings in
  `Directory.Build.props`: `net8.0`, nullable + implicit usings, `TreatWarningsAsErrors`, .NET analyzers
  (`latest-recommended`), NuGet audit (direct + transitive), deterministic builds — a violation fails
  `dotnet build`.
- **Coverage** — 90% line coverage per test project (Coverlet threshold), enforced on `dotnet test`.
- **Static analysis** — developed against a SonarQube gate (zero new issues on changed code), run locally.
  **No CI pipeline is committed in this repo**; the build- and coverage-gates above run via `dotnet build`
  and `dotnet test`.
- **Testing model** — committed tests are **mocked unit tests** only (they carry the coverage gate).
  Integration tests run locally against **real** infrastructure and are **never committed**.
- **Tests are generic.** They exercise the engine against synthetic layouts they declare themselves. No test
  loads a shipped layout file or asserts on any particular format's field names — a test that broke when a
  layout changed would be testing the layout, not the code.
- **Test data is never committed** — sample production-shaped files stay local (see `.gitignore`).

## Design reference

See [`docs/stage1-ingestion-design.md`](docs/stage1-ingestion-design.md) for the full design rationale —
boundary and non-negotiables, the generic ingestion model and variability seams, memory/scale invariants,
delivery guarantees and resilience, security/field protection, component decomposition, and test strategy.
