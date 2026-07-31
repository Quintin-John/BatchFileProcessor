# Stage 1 — G266 Ingestion Pump — Design

**Status:** design locked; **Stage-1 implemented and hardened** (single-profile G266 — see §14). The §12 open questions are business/spec decisions, not implementation blockers.
**Component scope (single responsibility):** *Turn a dropped file — fixed-length or delimited — into confirmed broker messages, as fast as possible.*
**G266 is the first configured *profile*, not the product** (see §1.2). Everything downstream — the record state machine, matching ("against what"), lifecycle/reconciliation — is **out of scope** and is a separate bounded context that consumes the messages this component emits.

---

## 1. Purpose and boundary

| | |
|---|---|
| **In scope** | Read one G266 file (streaming), map each fixed-width record via a soft-coded layout, batch records into messages, publish to RabbitMQ with publisher confirms (fail-closed), emit full per-record lineage and distributed traces. |
| **Out of scope** | Record state machine, matching against reference data, dollar/date reconciliation outcomes, HEAD/TRAI control-total policy beyond what §7 decides, message consumption. |
| **Deployment unit** | .NET 8 Worker Service, run **per file as a batch job** (K8s Job / ECS task), exits `0` on full confirmation, non-zero on any failure. |

**Dependency direction:** downstream consumers depend on this component's *message contract*; this component depends on nothing downstream. It does not know the state machine exists.

---

## 1.1 Engineering non-negotiables (SOLID · DRY · zero hardcoding)

Binding constraints on all code in this component. Enforced at review and by tests, not aspirational.

**Zero hardcoding — everything below is configuration/data, never a literal in code:**
record layout (offsets/lengths/types/sign/terminator → `layout.yaml`), file source path & completion-guard mode, RabbitMQ URI/exchange/routing-key/delivery-mode, batch size `N` and payload-byte cap, publisher `Count`, outstanding-confirms window `W`, channel capacities `C1`/`C2`, poll interval, reject/abort threshold, Datadog OTLP endpoint + `DD_ENV/SERVICE/VERSION` + sampling, encoding. **The strongest expression of this rule is that the record layout itself is data (YAML), not code** — no field offset is ever compiled in. Any magic number or string in a code path is a defect.

**SOLID — realised structurally, not by comment:**
- **S** — one responsibility per unit; each unit in §10 has a one-sentence purpose with no "and".
- **O** — extension seams added only where a second case is *real*: `IFileSource` (folder now, Azure Blob later), `IMessagePublisher` (MassTransit adapter → RabbitMQ now, Azure Service Bus via config later). The field-type set is **closed** — a new type is a deliberate, tested code change, not an open plugin surface. No speculative strategy/factory patterns.
- **L** — every `IFileSource`/`IMessagePublisher` implementation honours the full contract (complete stream + lifecycle; confirmed-or-throw). No `NotSupported`, no narrowed guarantees.
- **I** — interfaces stay narrow: `IMessagePublisher.PublishBatchAsync` only; `ILineageEmitter` only emits. Consumers depend solely on what they call.
- **D** — high-level orchestration depends on abstractions (`IFileSource`, `IMessagePublisher`, `ILineageEmitter`), wired by DI at the composition root; the pump never imports a concrete transport/source. Dependency direction is one-way, no cycles.

**Testing — 90% line coverage floor, build-gated, no shortcuts:**
- **Write code → add a test that proves it.** Non-negotiable, per unit.
- **≥ 90% line coverage by default**, enforced by the build (coverlet threshold; the build **fails** under 90%) — not a manual aspiration.
- **Coverage is a floor, not the target.** Tests assert *behaviour* (correct values, correct failure handling), not line-execution — coverage-gaming tests are a shortcut and are rejected. Mutation testing (Stryker.NET) is recommended to keep assertions honest.
- **Deterministic** — time is injected (`TimeProvider`) so backoff/heartbeat/stable-size timing is tested without wall-clock flakiness; no `DateTime.Now` in logic.
- Enabled **by** the architecture: every seam (`IFileSource`/`IRecordParser`/`IMessagePublisher`/`ICheckpointStore`/`ILineageEmitter`) is mockable; composition-over-inheritance keeps units isolated. See §11.

**DRY — no duplicated *logic*, and no premature abstraction (both are tech debt):**
- One mapping engine drives all record types from the layout — HEAD/TRAN/TRAI are **not** three parsers.
- Identity, lineage, and config are each single-sourced.
- **But:** coincidental similarity is not duplication. Two occurrences stay duplicated; extraction happens on the third **only when shape and reason-to-change coincide**. We do not couple unrelated call sites to satisfy a DRY metric — that is the failure mode this clause explicitly forbids.

---

## 1.2 Generic ingestion model & design patterns

**Reframing:** this component is a **generic fixed-length/delimited file-ingestion engine**. **G266 is the first *profile*, not the product.** Other files — different drop locations, fixed-length or delimited, routed to different queues on RabbitMQ *or* Azure Service Bus — are additional profiles. The pipeline logic is identical for all of them; only three things vary, each isolated behind a seam.

### The three variability axes (each a narrow seam, each with real named cases → all justified now)

| Axis | Seam | Real cases | Pattern |
|---|---|---|---|
| **Where the file comes from** | `IFileSource` | Windows/Mac folder · Azure Blob | **Adapter** |
| **How a record is framed & extracted** | `IRecordParser` | fixed-length · delimited | **Strategy** |
| **Where messages go** | `IMessagePublisher` (adapter = **MassTransit**) | RabbitMQ · Azure Service Bus | **Strategy / Adapter** |

All three are selected **per profile**; the pipeline core (`read → frame → parse → map → batch → publish → confirm → trace`) is generic and identical across profiles.

### The binding mechanism — Profile + config-driven Router

```yaml
# profiles.yaml  (soft-coded; a dropped file's path/location resolves its profile)
profiles:
  - id: g266
    match:       { glob: "**/g266*" }        # path/location → profile
    source:      folder                        # -> IFileSource
    format:      fixed-length                  # -> IRecordParser
    layout:      g266-layout.yaml              # field defs (data, not code)
    destination:                               # -> IMessagePublisher
      transport: rabbitmq                      # | azure-service-bus
      target:    { exchange: "...", routingKey: "..." }
    batch:       { size: ..., maxBytes: ... }
```

`IProfileResolver` maps `filePath → Profile` via ordered config rules; keyed factories then resolve the `IFileSource` / `IRecordParser` / `IMessagePublisher` the profile names. **Adding a file type = a new profile (config) + at most one new Strategy implementation — never a change to the pipeline.**

### The pattern we follow (macro → micro)

- **Pipes-and-Filters** — the pipeline is the macro-architecture; stages are composable filters over bounded channels.
- **Strategy** — each pluggable stage (source / parse / publish) is a strategy chosen per profile.
- **Adapter** — external systems (file origins, brokers) are adapted to uniform internal contracts.
- **Keyed Factory / keyed DI** (.NET 8) — select the implementation for a profile's `source`/`format`/`transport`.
- **Config-driven Router (Profile resolver)** — `filePath → Profile`; data-driven, no hardcoded routing.
- **Options + DI** — composition root wiring; zero hardcoding.
- **Composition over inheritance** — a generic `IngestOrchestrator` composes injected strategies; **no Template-Method base-class hierarchy** (keeps the algorithm testable and each strategy independently swappable).

### DRY boundary (correctly drawn — varies vs shared)

- **Varies (Strategy):** how a record's raw field slices are *extracted* — by fixed offset (`FixedLengthRecordParser`) vs by delimiter index (`DelimitedRecordParser`).
- **Not done at all:** the pump does **not** convert or validate values. Every field is emitted as its **raw text** with its name — no typed-value coercion (no `decimal`/`date`/`scale`/`sign`); interpreting a value is a downstream concern. The only per-field policy is **data in the layout** (`encrypt`, `required`), applied uniformly by the generic slicer, so there is nothing to duplicate across parsers.

### Explicitly rejected (premature — would add debt)

- **Abstract Factory hierarchy** — keyed DI lookup suffices for the small closed set per axis.
- **Chain of Responsibility** for profile resolution — ordered config rules are simpler and data-driven.
- **Open plugin/registry** for formats/transports — each axis is a **closed** set; adding a member is a deliberate, tested code change, not an open extension surface (OCP honoured without an open plugin API).
- **Specification pattern** for validation, **Builder** for the envelope, **Mediator** — no real win; needless indirection.

### Build scope (no speculative implementation)

- **Now:** generic pipeline + all three seams + `FolderFileSource` + `FixedLengthRecordParser` + `MassTransitPublisher` (RabbitMQ transport) + `ICheckpointStore` (watermark) + reject sink + profile/resolver + keyed factories. This runs G266 end-to-end.
- **Second implementations are added on first concrete need, same seams, zero rework:** `AzureBlobFileSource`, `DelimitedRecordParser` (needs delimiter/quote/escape/header/index-or-name schema), and the **Azure Service Bus transport** (MassTransit config/MultiBus — not a new publisher class). Building any of these against no concrete spec would be speculative and untested — deferred by design, not designed-out.

---

## 2. Input: the G266 file

Empirically confirmed from the sample `g266tpeo_T000001_20221107`:

- ASCII text, **fixed-length records of exactly 1200 bytes** + newline terminator.
- Record type discriminator at **offset 0, length 4**. Per the V4.8 spec the record types are **FHR / PER / AER / FTR** (File Header / Posting Extract / Authorisation Extract / File Trailer); the discriminator **values** map as `HEAD`→FHR, `TRAN`→PER, `AUTH`→AER, `TRAI`→FTR. See [`docs/layouts/g266-v4.8.yaml`](layouts/g266-v4.8.yaml) and §12/Q1.
- Sample structure: 1 × `HEAD` (FHR), 20,296 × `TRAN` (PER), 1 × `TRAI` (FTR) (single outer frame; the expected ~1k-record intermediate framing is **absent** in the sample — see §12/Q6).
- Trailer format (now known, §12/Q6): `TRAI` + `TLR-PTG-NBR` (count of PER/TRAN records) + `TLR-ATH-NBR` (count of AER/AUTH records) + `LAST`. The sample's `...000000043...000000000...` does not match its 20,296-record TRAN body, so that particular sample's trailer is inconsistent (a partial/test extract); the format itself is captured in the layout.
- **Files may be hundreds of GB** → tens/hundreds of millions of records; a single run may last hours. Design target is **constant memory in file size** and sustained streaming throughput, never in-memory load. See §3.1.

**Authoritative field layout:** ACI Issuer 4.8 — *Interfaces* / *Batch Guide* (product = "EPS-ISSUER"; **CMM** = Card Management Module). The record-level offsets are **not** reverse-engineered from the sample; they are transcribed 1:1 from that spec into the layout YAML. **This document does not assert field offsets** (see §12/Q1).

---

## 3. Architecture

```
                          per-file batch job (exit 0 / non-zero)
 ┌──────────────────────────────────────────────────────────────────────────┐
 │  FileStream                                                                │
 │     │ (streaming, constant memory)                                        │
 │  PipeReader ──1200B frames──▶ Layout Mapper ──▶ bounded Channel<Batch>    │
 │     │ SHA-256 (same pass)         │ (span, zero-alloc, YAML-driven)   │    │
 │     ▼                             ▼                                   ▼    │
 │  FileId                    accept / reject                    N Publishers │
 │                                                        (own IChannel each) │
 │                                                        confirm-select,     │
 │                                                        batched, fail-closed│
 │                                                                            │
 │  Cross-cutting: OpenTelemetry (traces+metrics+logs) ──OTLP──▶ Datadog Agent│
 │  Lineage: async bounded log pipeline, block-on-overflow (never drop)       │
 └──────────────────────────────────────────────────────────────────────────┘
                                     │ confirmed messages
                                     ▼
                              RabbitMQ exchange  ──▶  (downstream: state machine, etc.)
```

**Concurrency model (justified win, per partial-failure tests):**
- **One reader** — sequential file I/O is fastest single-threaded; framing + hashing on the read thread.
- **One mapper stage** — span-based parse is ~1000× faster than the transport; not a bottleneck, not parallelized.
- **Bounded channel** — decouples produce/publish, applies backpressure, caps in-flight memory regardless of file size.
- **N publishers** — RabbitMQ publish is network I/O and the sole bottleneck → fan out. RabbitMQ `IChannel` is **not** thread-safe → one channel per publisher task.
- **No ordering guarantee** across publishers (locked decision).

### 3.1 Memory & scale invariants (files up to hundreds of GB)

**Hard rule: peak memory is O(1) in file size** — a 300 GB file uses the same working set as a 24 MB one. Enforced at every point where memory could accidentally scale with input:

- **No whole-file materialisation** — no `ReadAllText`/`ReadAllLines`, no `List<record>`, no full buffering. `PipeReader` streams; segments are released on `AdvanceTo`.
- **Framing across buffer boundaries** holds at most **one** partial record (≤1200 B), never accumulated input.
- **Bounded ingest channel** (capacity `C1`) and **bounded lineage channel** (capacity `C2`, block-on-overflow).
- **Bounded outstanding-confirms window** (`W`) — the publisher blocks once `W` unconfirmed messages are in flight, so the pending-confirm tracking set **cannot grow with file size**. *(Critical: naïve publisher confirms over a hundreds-of-GB file would otherwise grow the unconfirmed map unbounded — this window is what makes confirms safe at scale.)*
- **Streaming SHA-256** — O(1) state.
- **Pooled serialisation** — `Utf8JsonWriter` over `IBufferWriter`/`ArrayPool`, no per-message `byte[]` churn.

Total bound ≈ `pipeBuffer + C1·batchBytes + W·msgBytes + C2·eventBytes + fixed` — every term configured, **none file-dependent**.

**Sustained-run discipline (runs may last hours):** span-based zero-alloc mapping + `ArrayPool` + pooled serialisation keep Gen0 GC pressure flat across the whole file; no Large-Object-Heap churn. Throughput must not degrade from GC over a multi-hour run.

### 3.2 Trigger model (host lifecycle) — decoupled from the pump

The pump (read → map → publish) is **identical regardless of trigger**. The trigger is a thin adapter behind an owned `IFileSource`; choosing one does not touch the pipeline, and a second mode is a drop-in without pump changes.

| Mode | How it starts | Trade-offs |
|---|---|---|
| **A. Per-file batch job** | external trigger launches a container for **one** file; pump runs once; exits `0`/non-zero | Per-file isolation, horizontal scale (N files = N jobs), natural fail-closed boundary, no idle daemon, no long-lived memory. Strong fit for hundreds-of-GB + deterministic exit code. |
| **B. Long-running worker (folder watch)** | `BackgroundService` watches a folder (`FileSystemWatcher`/poll), claims a **complete** file, runs pump, moves to `processed/failed`, loops | Simple for continuous arrival, no per-file cold start. **Must** solve: partial/in-transfer detection (never read a file still being written — require a completion signal: atomic rename, `.done` sentinel, or stable-size poll), single-flight concurrency control, in-progress recovery on restart. Long-lived → sustained GC discipline over days. |
| **C. Event/queue-triggered job** | file-ready event (object-storage event, SFTP-complete hook, or a "file ready" control message) launches a per-file job | Combines B's automation with A's isolation + complete-file guarantee. |

All three sit behind the same `IFileSource`; the pump is unchanged.

#### 3.2.1 Selected source: Windows folder drop (+ Azure flexibility) — **Mode B**

Known requirement: files are **dropped into a Windows folder**; must stay flexible to Azure later. Resolution:

- **Now — folder-watch worker (Mode B) over a configured path.** The same worker serves a **local Windows folder** and an **Azure Files (SMB) share** with no code change — only the mounted path differs.
- **Later — Azure Blob Storage** is a *different* trigger (Event Grid → Mode C), so it becomes a **second `IFileSource` implementation**, not a modification of the folder worker. Built only when that case is real; the pump and contract are untouched. This is the open/closed seam added because a second case is genuinely named — but the Azure Blob adapter is **not** built speculatively now.

**`IFileSource` contract:** yields a *claimed, verified-complete, readable stream* plus a `complete`/`fail` lifecycle. Folder impl returns a `FileStream`; a future Blob impl returns a blob read stream. The pump consumes a `Stream`/`PipeReader` and is blind to origin.

**Folder-watch mechanics (correctness-critical for hundreds-of-GB drops):**
- **Discovery = periodic directory poll**, not `FileSystemWatcher` alone. FSW on Windows drops events under load, fires `Created` before the write completes, and overflows its buffer on large files; it is at best a wake-up hint. An authoritative poll sweep is the deterministic mechanism.
- **Completion guard (mandatory)** — never read a mid-transfer file. Preferred, in order of determinism: (1) producer **atomic-renames** `*.tmp → *.g266` on completion; (2) producer drops a **`.done` sentinel**; (3) fallback **stable-size + exclusive-lock probe** (size and last-write unchanged for T seconds *and* the file opens with no sharing). (1)/(2) require producer cooperation and are deterministic; (3) is heuristic and used only if we do not control the producer. **Which is available is an open sub-question (§12/Q2).**
- **Claim = atomic move** into a `processing/` directory (single-flight; prevents double pickup). On success → `processed/`; on failure → `failed/`.
- **Restart recovery** — any file left in `processing/` on startup was interrupted; reconciled per the restart policy (§12/Q8).
- **Concurrency** — one file at a time by default (each hundreds-of-GB run already saturates broker + Datadog); parallel-file processing is a config knob, not the default.

#### 3.2.2 Container access to the drop folder (bind mount / volume)

The container reaches the drop folder via a **bind mount / volume** at a config-driven container path (e.g. `-v <host>:/data/in`). Caveats specific to a **Windows-host folder in a Linux container** (default Docker Desktop / WSL2):

- **File-change events do not cross the boundary** — `inotify`/`FileSystemWatcher` do not fire reliably over WSL2 (9P/virtiofs). This makes the poll-based discovery of §3.2.1 **mandatory**, not merely preferred.
- **Cross-OS bind-mount I/O is slow** — a throughput concern for sequential reads of hundreds-of-GB files. If it bites, place the folder on a **native Linux filesystem** or an **Azure Files/CIFS volume**, not a Windows-host bind mount.
- **Lock-probe completion guard is unreliable across the boundary** — prefer atomic-rename / `.done` sentinel guards (metadata-based, portable) over the exclusive-lock probe (§12/Q2).
- **Permissions** — container UID/GID needs read on the mount; for CIFS/Azure Files set `uid`/`gid`/`file_mode`.

**Consequence:** an **Azure Files (SMB) CIFS mount** into the container is cleaner and faster than a Windows-host bind mount, and **Azure Blob + Event Grid (Mode C)** removes folder-mounting entirely — so the Azure flexibility path is the *more* container-robust deployment, not a compromise.

#### 3.2.3 Cross-platform support (Windows / macOS / Linux) — dev on macOS

Must run on **macOS** (test environment) as well as Windows/Linux. No architectural change — the portability falls out of decisions already made:

- **.NET 8 is natively cross-platform** (macOS arm64/x64, Windows, Linux) from one codebase; no OS-specific APIs in the design.
- **Poll-based discovery** (§3.2.1) behaves identically on APFS/macOS — no reliance on `FileSystemWatcher`/`inotify`/FSEvents.
- **Binary framing** — the pump reads raw bytes via `PipeReader`, never OS text mode, so no macOS/Windows newline translation can shift the fixed 1200-byte offsets. The record terminator (LF vs CRLF) is a property of the **source file**, made explicit in the layout, not inferred from the host.
- **Docker Desktop on macOS** runs Linux containers in a VM; host-folder bind mounts go through **VirtioFS/gRPC-FUSE** — same cross-OS caveat as Windows (slower I/O, unreliable `inotify`), already absorbed by poll + metadata-guard. For fast local testing the worker also runs **natively (`dotnet run`, no container)** against a local folder.

**Portability guardrails (enforced):** config-driven paths (never `C:\…`), `Path`-based composition, **ordinal** comparison for the record-type discriminator (macOS/Windows case-insensitive, Linux case-sensitive), binary reads only.

---

## 4. Soft-coded mapping (layout YAML)

The mapper is generic; all record structure is data, loaded and validated at startup, **fail-closed**.

```yaml
# layout.yaml  (mounted, not baked into the image)
encoding: ascii                    # single-byte; offsets are byte offsets
recordLength: 1200
terminator: 1                      # bytes per record terminator (1=LF, 2=CRLF, 0=none) — framing lives with the layout
discriminator: { start: 1, length: 4 }
recordTypes:
  per:
    match: "TRAN"
    fields:
      - { name: W136-ECT-PTG-RCD-IDN-CDE, start: 1,   length: 4 }
      - { name: W136-ECT-PTG-EXT-ACT-NBR, start: 72,  length: 34, encrypt: true, required: true }
      - { name: W136-ECT-PTG-PAN-TXT,     start: 323, length: 28, encrypt: true }
      # ... every byte of the record is tiled; padding is just a named field, sliced like any other
  fhr: { match: "HEAD", fields: [ ... ] }
  ftr: { match: "TRAI", fields: [ ... ] }
```

**Startup validation (fail-closed, no partial run):**
- fields tile each record type with no gap or overlap, summing to `recordLength`;
- each record type has a unique discriminator match;
- at least one record type is defined.

**No field types.** Every field is carried as its **raw text** — the pump does not interpret values. A field declares only `name`, `start` (1-based), `length`, and two optional, data-driven flags: `encrypt` (encrypt the value before publish) and `required` (reject the record if the value is blank). Data types, scale, sign, and date/time formats are a downstream concern and are deliberately not modelled here — that is what keeps *swap the YAML = new format, zero code* true.

---

## 5. Message contract

**The contract is the *envelope*, not the field schema.** Because fields are soft-coded (§4), the record payload is an **open, self-identifying name→value map** — there are **no G266-typed properties** compiled anywhere. The stable, typed parts (envelope, open field bag, encrypted-value shape, reject reasons) live in `*.Messaging.Contracts`; the *which-fields* knowledge stays in the versioned layout YAML, referenced by `layoutVersion`. This is exactly what lets one contract serve G266 V4.8, V4.11, and future delimited files with **zero recompile**.

- **One message = one batch of N records** (locked). N configured; payload bounded (§12/Q3).
- Envelope (generic, format-agnostic):

```jsonc
{
  "messageId":     "<FileId>-<batchSeq>",
  "correlationId": "<RunId>",
  "fileId":        "<sha256-of-file>",
  "fileName":      "g266tpeo_T000001_20221107",
  "profile":       "g266",
  "layoutVersion": "4.8",                       // consumer resolves field types/classification from this
  "batchSeq":      1234,
  "firstRecordSeq":123401, "lastRecordSeq":123500, "count":100,
  "records": [
    {
      "recordSeq": 123401, "byteOffset": 148081200, "recordType": "TRAN",
      "fields": {                               // OPEN map — names come from the layout, not from code
        "amount":   221.73,                     // clear, typed by the referenced layout
        "postDate": "2022-11-07",
        "pan":      { "enc": "AES-256-GCM", "keyId":"…", "keyVersion":"…", "nonce":"…", "ct":"…", "tag":"…" }
      }                                         // encrypted field = self-identifying ciphertext envelope
    }
  ]
}
```

**What is typed & compiled in `Contracts`:** `IngestBatchMessage` (envelope), `IngestRecord`, `FieldValue` (= clear value **or** `EncryptedValue`), `RejectMessage` + `RejectReason` (format-agnostic — describes *validation failures* generically). **What is not:** any G266 field name/offset — that is data.

**Schema-referenced, not self-describing per field** (DRY + smaller at GB scale): the message carries `profile`+`layoutVersion`; the consumer resolves types/classification from the **same versioned layout** (a shared, published schema artifact) rather than repeating metadata on every record. Encrypted fields remain self-identifying (their ciphertext envelope carries `alg/keyId`), so clear-vs-encrypted needs no external lookup. Two independent version axes: the **Contracts package** version (envelope shape) and **`layoutVersion`** (field schema).

- RabbitMQ message properties carry `MessageId`, `CorrelationId`, and W3C `traceparent`/`tracestate` (headers) for downstream trace continuity.
- **Wire = batch; state = per-record.** The `records[]` array is the seam the state machine uses to reconstruct per-record lifecycle.

---

## 6. Publish + delivery guarantee

- **Transport = MassTransit** (mandated), behind the narrow **owned port** `IMessagePublisher.PublishBatchAsync(batch, ct)`. MassTransit is the **pluggable adapter**; the profile's `destination.transport` selects **RabbitMQ** or **Azure Service Bus** (MultiBus for mixed brokers in one process). The orchestrator depends only on the port — MassTransit types never leak into the pipeline (keeps the hot path testable).
- **Durable by default; infra owns topology** (Q4) — MassTransit configured **not** to auto-provision; it publishes/sends to entities infrastructure already created.
- **ACK/NACK, fail-closed:** each batch send awaits broker acknowledgement (RabbitMQ publisher-confirms / ASB send-receipt, mapped to the port's *confirmed-or-throw* contract).
  - ack → continue;
  - **nack / return / timeout → abort the run, non-zero exit, nothing marked done** (fail-closed, publish/system class only — distinct from per-record `rejected`, Q5).
- **Bounded in-flight** — MassTransit send concurrency/pipeline is capped so unconfirmed sends cannot grow with file size (preserves the O(1) memory invariant, §3.1).
- **At-least-once** → downstream dedupes on `FileId+recordSeq` (Q7). Resume-from-watermark (Q8) re-sends only the in-flight window; dedupe absorbs it.
- **Trade-off on record (Q4/Q11):** MassTransit's per-message envelope overhead is accepted in exchange for uniform ACK/NACK, pluggable brokers, and a shared producer/consumer model; **batch-per-message** amortizes it. v9 commercial-licensing noted for procurement.

---

## 6.1 Resilience — retries, circuit breakers, failure handling

Queues **will** fail (broker restart, network blip, connection drop, ASB throttling). Resilience sits **underneath** fail-closed: transient faults are absorbed by retry + circuit-breaker; only **exhausted or terminal** faults escalate to a fail-closed abort. Retry is safe because delivery is at-least-once and downstream dedupes (Q7).

**Failure taxonomy (explicit classification — no blanket catch):**

| Class | Examples | Policy |
|---|---|---|
| **Transient (retryable)** | connection drop, timeout, broker unavailable, ASB `ServerBusy`/429 | **Retry** (exp backoff + jitter, bounded); **circuit-breaker** on sustained failure |
| **Terminal (non-retryable)** | auth failure, entity-not-found, serialization error | **Fail-closed** immediately — abort run, non-zero exit, watermark persisted |
| **Retries exhausted** | transient that won't clear within the budget | escalate to **fail-closed** |
| **Per-record data (Q5)** | bad amount/date | **not a failure** — quarantine to reject sink, continue |
| **Cancellation** | shutdown / SIGTERM | cooperative stop → deterministic cleanup (below) |

**Mechanisms — configured on MassTransit's pipeline (not hand-rolled):**
- **Retry** — `UseMessageRetry` with exponential backoff + jitter, bounded attempts; per-transport tuned via config.
- **Circuit breaker** — `UseCircuitBreaker` trips on sustained failure ratio → stops hammering a down broker (open → half-open probe → closed), preventing CPU/log-spam storms during an outage.
- **Rate limiting** — optional `UseRateLimit` for ASB throttling ceilings.
- **Connection recovery** — MassTransit auto-reconnects; the reader **pauses on backpressure** (bounded channel) while the circuit is open, so no data is lost or force-read.
- Consumer-side (stage 2) adds **redelivery + dead-letter** natively — out of scope here, noted for symmetry.

**Layering (the important invariant):**
`transient fault → retry(backoff) → still failing → circuit-breaker(open) → retries exhausted → fail-closed abort → watermark flushed → restart resumes from watermark`. Resilience delays escalation; it never silently swallows a terminal fault, and it never drops a message.

**Deterministic cleanup — `try / catch / finally` owned by the orchestrator:**
Every exit path (success, terminal fault, retries-exhausted, cancellation, unexpected exception) runs a `finally` that **guarantees**: flush the lineage/metric pipelines, **persist the final watermark**, dispose the MassTransit bus/connections, **release the claimed file** (leave it in `processing/` for resume, or move to `failed/`), and set the process exit code. No leaked connections, no half-open file handles, no half-written watermark. This is lifecycle correctness, pinned by tests.

**Observability-backend failure (flagged — §12/Q13):** the telemetry exporter has its own retry/backoff; but the "full per-record lineage, never drop" guarantee means a **hard** backend outage blocks ingestion (fail-closed on observability). Whether to keep blocking or spill lineage to a **local durable buffer** during a backend outage is an open sub-decision.

**Resilience tests (chaos / partial-failure):** broker dropped mid-run → retry+CB → recovery → run completes; broker down past budget → fail-closed abort + watermark written + resume verified; `SIGTERM` mid-batch → `finally` cleanup verified (watermark flushed, connections disposed, file left resumable); backend outage → block vs spill behaviour pinned.

---

## 6.2 Rejection queue & reject diagnostics

Per-record data failures (Q5) are quarantined to a **dedicated durable reject queue** — not dropped, not a local file — so every formatting failure is **actionable, queryable, and dashboard-able**. The run continues.

**What counts as a reject** (distinct from the fail-closed classes): a record the pump cannot map — **wrong record length**, **unknown record type** (the discriminator matches no layout entry), or a field the layout marks **`required`** is blank. The pump does **not** validate values, so there is no "non-numeric"/"bad date" reject. *(Startup/layout errors are fail-closed; publish/system errors are fail-closed §6.1. Only per-record structural/required errors are rejects.)*

**Reject message contract (must support both diagnosis and replay):**
- **Identity:** `RunId, fileId, fileName, profile, layoutVersion, recordSeq, byteOffset, recordType`, `correlationId`/`traceparent`.
- **Raw record bytes** — the original line, for inspection / repair / replay.
- **`reasons[]`** — structured `{ field, offset, length, rule, expected, actual, code }`; **all** field failures for the record are collected, not just the first (better diagnostics, better dashboards).

**Delivery guarantee:** rejects publish through the **same MassTransit transport with the same confirmed / fail-closed guarantee** — a reject that cannot be delivered is a system failure (§6.1 retry/CB → escalate), **never** a silent loss. Losing a reject = losing the record.

**Destination:** a durable reject queue/topic (infra-owned, Q4), configured **per profile** (Q5 reject-sink config), via the same transport abstraction.

**Metrics for the dashboard (OTel `Meter` → Prometheus/Datadog, §8):**
- `records_rejected_total{profile, fileId, recordType, reason_code, field, layoutVersion}`
- reject **rate/ratio** per file and per profile, rejects-per-file, **top failing fields / rules / codes**.
- These are exactly the signals a dashboard needs to show *which files, fields, and rules fail most*. **This component emits the signals; the dashboard itself (Grafana/Datadog) is ops/downstream — out of this build's scope.**

**Reject queue vs lineage (both fire, distinct purposes):** `lineage(rejected)` is the observability **trace event**; the reject-queue message is the **actionable artifact** for repair/replay/reconciliation. Same discipline as lineage-vs-state-machine — two sinks, two jobs.

**Replay** is enabled (raw record + identity travel on the message) but **not built here** — repair/replay is a stage-2/ops flow.

---

## 7. HEAD / TRAI handling

Per instruction, header/trailer are **deferred** for now. Recorded as an explicit decision, not an omission: the trailer carries a control total that a settlement pipeline would normally reconcile fail-closed against the body count. Policy is **open** (§12/Q6).

---

## 8. Observability, tracing, lineage

**Instrumentation:** OpenTelemetry .NET (`ActivitySource` / `Meter` / `ILogger`) — **vendor-neutral, multi-backend** (Q9). **Logs** → structured JSON to **stdout**, scraped by the **Datadog** log pipeline. **Metrics + traces** → OTel, exported via OTLP and/or a **Prometheus** scrape endpoint, so Datadog, Prometheus, and others consume the same signals. No backend is a code dependency; endpoints/sampling/`DD_ENV`/`SERVICE`/`VERSION` are soft-coded.

**Identity backbone (explicit, threaded — not ambient):**

| ID | Scope | Derivation |
|---|---|---|
| `RunId` | one job execution | generated per run (ULID) |
| `FileId` | source file | **SHA-256 of content**, streamed during the single read pass |
| `RecordId` | one line | `(FileId, recordSeq, byteOffset)`, recordSeq 1-based |
| `MessageId` | one batch | deterministic `(FileId, batchSeq)` |
| `CorrelationId` | the trace | = `RunId`, propagated to logs, spans, message headers |

**Trace shape:** spans at **run** and **batch** granularity only (per-record spans would explode volume/cost at GB scale — deliberate, not a silent cap). `traceparent` injected into message headers → distributed trace continues into downstream consumers.

**Lineage (locked: full per-record lifecycle, always):**
- states: `consumed` → `accepted` | `rejected` → `batched` → `published` → `confirmed` | `failed`;
- every record emits a structured event at every transition, stamped with the identity backbone + `trace_id`/`span_id`;
- **this is telemetry, not the system of record** — the downstream state machine is the authoritative business state; the lineage stream is the forensic trace of how a record moved.

**Lineage pipeline (consequence of "full, always" at GB scale):**
- lineage events go to a **dedicated async bounded channel** → batched OTLP exporter; the parse/publish hot path never blocks on Datadog I/O;
- **overflow policy = block (backpressure), never drop** — a dropped event is a silent hole in the guarantee; therefore **Datadog intake throughput gates end-to-end throughput**, and the Agent must be sized for peak records/sec.

**Metrics (OTel `Meter`; Prometheus/Datadog-scraped):**
- **Throughput/volume:** `records_consumed/accepted/rejected`, `batches_published/confirmed`, `bytes_read`, `records_per_sec`.
- **Reliability (§6.1):** `publish_ack/nack`, `publish_retries`, `circuit_breaker_state` (closed/open/half-open), `publish_failures_terminal`, `run_aborts`.
- **Latency:** publish/confirm-latency histogram, end-to-end record latency histogram.
- **Saturation/backpressure:** ingest & lineage channel depth, **in-flight/unconfirmed gauge** (the bounded window `W`), lineage-export lag.
- **Resume:** `checkpoint_watermark_offset`, `checkpoint_lag`.
- **Plus MassTransit's own metrics** (send/consume counts, fault counts) exposed through the same OTel pipeline.
- Every counter is dimensioned by `profile`, `fileId`, `recordType` where meaningful — so metrics answer "was it processed" per profile/file without reading logs.

---

## 8.1 Health checks

May or may not run in K8s → expose a **universal HTTP health surface** that any consumer (K8s probes, Docker `HEALTHCHECK`, manual) can hit; the app is **not** K8s-coupled. This is the **same lightweight HTTP listener** that serves the Prometheus `/metrics` scrape (§8) — one surface, not a new dependency per concern. Built on `Microsoft.Extensions.Diagnostics.HealthChecks`, checks **tagged** `live`/`ready`/`startup` and filtered per endpoint.

| Endpoint | Question | Checks |
|---|---|---|
| `/health/startup` | Has init finished? | layout/profiles loaded & **validated**, MassTransit bus connected — so liveness doesn't fire during slow start |
| `/health/live` | Is the process deadlocked? | **heartbeat watchdog** only (below) |
| `/health/ready` | Can it take/continue work? | broker reachable (**MassTransit's built-in bus health check**), source path/mount accessible, checkpoint store reachable |

**The liveness trap (explicit):** liveness is a **heartbeat watchdog**, not a progress or idle check. The orchestrator advances a heartbeat each pump cycle; `/health/live` is unhealthy **only** if the heartbeat is stale beyond a threshold (true hang/deadlock). It must **tolerate**:
- a single file taking **hours** (legitimately busy ≠ dead), and
- a **broker outage / open circuit** (correctly waiting ≠ dead).

**Mapping to resilience (§6.1):** circuit-open → **liveness HEALTHY, readiness DEGRADED**. Killing the pod during a broker outage would churn the watermark and lose in-progress work — explicitly avoided. Only a stale heartbeat (real deadlock) fails liveness and invites a restart, which then **resumes from the watermark** (Q8).

**Mode fit:** Mode B worker (Deployment) uses startup + liveness + readiness. Mode A/C per-file **Job** uses **liveness for hang-detection + the process exit code**; readiness/startup are N/A (a Job pod is not behind a Service).

**Cost:** probes are frequent → checks are **cheap/cached** (dependency status is read from the resilience layer's last-known state, not by hitting the broker on every probe). Probe paths, port, and heartbeat-staleness threshold are **soft-coded**.

---

## 8.2 Security — encryption in transit and at rest (bank-grade, non-negotiable)

Banking context: **all data — in transit and at rest — is encrypted with strong, reversible (two-way) encryption.** No plaintext of card data anywhere: not on the wire, disk, queue, or buffer. **Fail-closed: the system never falls back to plaintext.**

**In transit — TLS 1.2+ (prefer 1.3) on every connection; app aborts if it cannot negotiate:**
- Source read — SMB3-encrypted share / HTTPS (Azure Files/Blob).
- App ↔ broker — **AMQPS** (RabbitMQ) / TLS (ASB), mutual-TLS where supported.
- App ↔ Key Vault/KMS — TLS. · App ↔ telemetry (OTLP/scrape) — TLS. · Checkpoint store (if remote) — TLS.

**At rest — whole payload, transparent, infra-provisioned:**
- Source files + `processing`/`processed`/`failed` working copies → **encrypted volumes/storage** (BitLocker/LUKS, Azure Storage encryption).
- Broker/queue persistence → broker at-rest encryption (ASB default/CMK; RabbitMQ encrypted disk).
- Checkpoint/watermark store → encrypted at rest.
- **No plaintext spill to disk** — buffers stay in memory; if the lineage spill (Q13) is ever enabled, it writes to encrypted storage only.

**Application-layer field encryption — defense in depth (protects PAN even from an authorized queue reader):**
- Sensitive fields (PAN/token) encrypted with **AES-256-GCM** (authenticated) *inside* the payload; diagnostic fields stay clear so dashboards/metrics work (§6.2).
- **Envelope encryption** — a fresh **data key (DEK) per batch** wrapped by a **KEK in HSM-backed Key Vault/KMS**; reversible by authorized consumers (encrypt-on-send / decrypt-on-consume via MassTransit `UseEncryption`).
- **DEK transport = Option A (decided):** the DEK is **referenced by `keyId`/`keyVersion`** in the ciphertext and resolved centrally via `IKeyProvider` (the wrapped DEK lives in Key Vault/secret store). **Key material never travels on the message bus**, and `Contracts` needs no change (`EncryptedValue` already carries `keyId`/`keyVersion`).

**Algorithm & crypto-agility (pluggable — the bank-grade requirement):**
- **Chosen algorithm: AES-256-GCM — single, committed choice** (secure-at-field-level **and** fast). FIPS 197 / NIST SP 800-38D **AEAD** (confidentiality + integrity in one pass), HSM/Key Vault-native, AES-NI accelerated (multi-GB/s per core). **AAD** binds each ciphertext to `fileId + recordSeq + field` (anti-replay). GCM-SIV / post-quantum are **future plugin slots only**, not competing choices.
- **Field-level *and* fast** — the per-batch **DEK is unwrapped from Key Vault once per batch, never per field**; all per-field encryption then runs **locally in-process** (nanoseconds/field), so field-level protection carries **no per-field network cost**. This is the mechanism that makes field-level viable at tens-of-millions-of-records scale.
- **Nonce discipline** — per-batch DEK + unique 96-bit nonce bounds encryptions per key, eliminating GCM's nonce-reuse risk. Misuse-resistant drop-in if ever needed: **AES-256-GCM-SIV** (RFC 8452). *(ChaCha20-Poly1305 excluded — not FIPS-approved.)*
- **Plugin seam** — `ICryptoProvider` port in `*.Security.DataProtection`; `AesGcmCryptoProvider` ships now, GCM-SIV / post-quantum are additional plugins in the same slot. Algorithm selected by config (data-protection policy), **never hardcoded**.
- **Self-describing ciphertext** — every value carries `{ alg, keyId, keyVersion, nonce, tag }`, so **algorithm/key rotation is non-breaking**: a consumer reads the tag and selects the right plugin/key to decrypt old and new data side by side. This is what makes crypto-agility real, not aspirational.
- **Never home-grown** — each plugin is a thin adapter over `System.Security.Cryptography.AesGcm` / Key Vault crypto, not a custom cipher.

**Key management & crypto discipline:**
- Keys in **HSM-backed Key Vault/KMS** (FIPS 140-2/3), with a **rotation** policy; **no key material in code, config, or images** (§1.1).
- **Least privilege / separation of duties** — this producer holds *encrypt* rights; consumers decrypt.
- **Never roll our own crypto** — platform primitives only (`System.Security.Cryptography.AesGcm`, Key Vault crypto); FIPS mode where mandated.
- **Secrets** (connection strings, key IDs, credentials) come from Key Vault/secret store at runtime — never appsettings or images.

**Fail-closed (critical):** TLS negotiation failure, unavailable key, or a missing at-rest guarantee ⇒ **abort; never transmit or persist plaintext.** Encryption failures are terminal (§6.1), never retried into a plaintext path.

**Ownership boundary (explicit):** **App** enforces TLS on its connections + app-layer field encryption + Key Vault usage + no plaintext logging/spill. **Infra** provisions encrypted volumes/storage + broker at-rest encryption (Q4). Both required; the app fails closed if its side is unsatisfiable.

**Tests:** TLS-required (plaintext connection refused); encrypt→decrypt **round-trip** (both ways); key-unavailable → fail-closed with **no plaintext emitted**; no-sensitive-data-in-logs assertion.

## 8.3 Field protection (soft-coded — the layout's `encrypt` flag)

**Sensitivity is data, not code — and it lives in the layout.** Each field carries an optional `encrypt: true`; the host derives the field-protection policy from those flags at startup. One soft-coded file — the layout — describes *both* how to slice and what to protect, so a PAN or account-number field is classified right next to its position.

```yaml
# in the layout — classification travels with the field
- { name: W136-ECT-PTG-PAN-TXT,     start: 323, length: 28, encrypt: true }
- { name: W136-ECT-PTG-EXT-ACT-NBR, start: 72,  length: 34, encrypt: true, required: true }
```

**Single-sourced enforcement** — one `IFieldProtector`, driven by the layout-derived policy, is the only thing that encrypts, so protection can't be applied in one place and forgotten in another:
- `encrypt: true` → AES-256-GCM field-level (§8.2); the encrypted value is **self-describing on the wire** (an envelope object carrying `keyId`/`keyVersion`/`algorithm`), so a consumer knows which fields are encrypted and how to decrypt — no out-of-band schema;
- flagged fields are also **redacted from logs/lineage** (never emitted in clear);
- the **reject path** encrypts the whole raw record (`ProtectRaw`) — a record that failed to map has no field structure to classify, so it is protected unconditionally.

**Classified by construction, decoupled by design:** every layout field is Encrypt or Clear (flag present or absent), so the fail-closed policy lookup never faults on a field the layout defines. Protection is keyed by field name; if a name recurs across record types with **conflicting** classifications (encrypt vs clear), policy construction **fails closed** at startup rather than silently collapsing to one — a name resolves to a single classification or the host does not start. The crypto and the layout stay independent — bridged only at the composition root (`LayoutProtectionPolicy`). *(Masking — e.g. PAN→first6/last4 for diagnostics — is not layout-driven today; it would be a further per-field flag if needed.)*

---

## 9. Configuration (zero hardcoding)

`appsettings.json` + environment overrides + `IOptions<T>`. Nothing hardcoded. Keys (indicative):

| Group | Keys |
|---|---|
| Profiles | `profiles.yaml` — per-profile match rules (folder + glob, Q10), source, format, layout ref, destination, batch |
| Layout | versioned `layout.yaml` path(s) per profile (§12/Q1), `encoding`, record terminator |
| Source | drop path/mount, completion-signal mode (Q2b), poll interval, `processing/`/`processed/`/`failed/` dirs |
| Transport (MassTransit) | `destination.transport` (rabbitmq \| azure-service-bus), connection, target queue/topic, `BatchSize` (N) + per-transport byte cap (Q3), in-flight bound `W` (§3.1) |
| Resilience | retry attempts + backoff/jitter, circuit-breaker thresholds, rate limit (§6.1) |
| Reject | reject-sink destination (Q5) |
| Checkpoint | watermark store location + flush interval (Q8) |
| Channel | bounded capacity — ingest `C1`, lineage `C2` (§3.1) |
| Observability | OTLP endpoint, Prometheus scrape, `DD_ENV/SERVICE/VERSION`, sampling; lineage overflow policy (Q13) |
| Health | probe port + paths, heartbeat-staleness threshold (§8.1) |

---

## 10. Component decomposition (SRP — one reason to change each)

| Unit | Responsibility | Seam / pattern |
|---|---|---|
| `Profile` + `IProfileResolver` | `filePath → Profile` via ordered config rules | Config-driven Router |
| `IFileSource` (`FolderFileSource`; later `AzureBlobFileSource`) | yield a claimed, complete, readable stream + `complete`/`fail` lifecycle | Adapter |
| `StreamReaderCore` | `PipeReader` over the source stream; streaming SHA-256; byte-offset/seq assignment | generic |
| `Layout` | schema model + YAML loader + startup validator (per profile) | — |
| `IRecordParser` (`FixedLengthRecordParser`; later `DelimitedRecordParser`) | frame + slice each field to its **raw** value per layout; reject only structurally (wrong length / unknown type) or on a blank `required` field | Strategy |
| `RejectSink` | build the reject message (raw record + all-field `reasons[]` + identity), publish to the durable reject queue with confirmed/fail-closed delivery, emit reject metrics; run continues (§6.2) | — |
| `MessageContract` | batch envelope DTO | — |
| `IMessagePublisher` (`MassTransitPublisher` → RabbitMQ · ASB) | batched confirmed publish (ACK/NACK) to the profile's destination | Strategy / Adapter (MassTransit) |
| `ICheckpointStore` | persist/restore the last-confirmed watermark (byte offset + recordSeq); resume on restart (Q8) | — |
| `LineageEmitter` | per-record lifecycle events (backend-agnostic, OTel) | cross-cutting |
| `MetricsRecorder` | OTel `Meter` counters/histograms; scraped by Prometheus / Datadog (Q9) | cross-cutting |
| `HealthHttpHost` | lightweight HTTP listener serving `/health/{live,ready,startup}` **and** `/metrics` (§8.1) | — |
| `IngestionHeartbeat` | liveness watchdog — advanced each pump cycle; stale ⇒ deadlock (§8.1) | — |
| `IngestOrchestrator` (BackgroundService) | compose resolved strategies; wire reader → channel → publishers; watermark; heartbeat; cancellation; exit code | Pipes-and-Filters host |
| Keyed factories (DI) | resolve `IFileSource`/`IRecordParser`/`IMessagePublisher` for a profile | Keyed Factory |

---

## 11. Test strategy (deterministic, pins partial-failure)

**Gate: ≥ 90% line coverage, build-enforced** (coverlet threshold → build fails under 90%). Coverage is a floor; tests assert **behaviour**, not line execution. Tooling: **xUnit + FluentAssertions + NSubstitute**, coverlet, **Stryker.NET** (mutation) to keep assertions honest. Time is injected (`TimeProvider`) — no wall-clock flakiness.

**Per-component unit tests (the seams make each isolatable):**

| Component | What is proven |
|---|---|
| `IRecordParser` (fixed-length) | golden record → exact **raw** fields (spaces preserved); wrong length, unknown discriminator, and blank `required` field each → reject |
| `LayoutProtectionPolicy` | `encrypt: true` → Encrypt + redact; unflagged → Clear; every field classified |
| `Layout` validator | gap / overlap / under- or over-coverage → **startup fail**; duplicate discriminator → **startup fail** |
| `IProfileResolver` | folder + glob ordered rules; first-match; no-match handling (Q10) |
| Batching + window `W` | correct batch envelope, `first/lastRecordSeq`, bounded in-flight |
| `IMessagePublisher` (MassTransit) | ack → continue; **nack/exhausted → abort, non-zero exit, nothing marked done** |
| Resilience (§6.1) | transient→retry(backoff, injected clock)→CB open/half/closed; terminal→fail-closed; classification correctness |
| `RejectSink` (§6.2) | reject message contract, all-field `reasons[]`, confirmed delivery, reject metrics; run continues |
| `ICheckpointStore` (§8) | watermark persist/restore; resume skips confirmed; in-flight re-publish + dedupe |
| Lineage | every record emits every lifecycle event; overflow → **block, never drop** |
| Backpressure | bounded ingest + lineage channels block producers under load |
| Health (§8.1) | heartbeat-staleness → liveness fail; circuit-open → live HEALTHY / ready DEGRADED |
| Cleanup | `finally` flushes telemetry, persists watermark, disposes bus, releases file, sets exit code |

**Resilience/chaos (partial-failure, pinned):** broker drop mid-run → recover; broker down past budget → abort + watermark + resume verified; `SIGTERM` → clean `finally`; backend outage → block vs spill (Q13).

**Honest exclusions (the small remainder outside the 90% of *logic*):** `Program`/composition-root wiring, the MassTransit transport binding, and the HTTP host are covered by **integration** tests, not unit coverage; generated code is excluded. No business/parsing/resilience/reject logic is exempt.

Built against a **synthetic fixture layout** until the real G266 tables (V4.8/V4.11) land; the real tables change only `layout.yaml`, no code — so the test suite stands unchanged.

---

## 12. Open questions (must be resolved; fail closed until then)

| # | Question | Impact | Default if unanswered |
|---|---|---|---|
| **Q1** | **PARTIAL — V4.8 received; V4.11 still pending.** V4.8 layout transcribed to [`docs/layouts/g266-v4.8.yaml`](layouts/g266-v4.8.yaml) from the ACI mapping workbook (sheet `G266_4.8.2`). Record types are **FHR / PER / AER / FTR** (each 1200 bytes); the 4-char discriminator at start 1 maps to the type — **confirmed: HEAD→FHR, TRAN→PER, AUTH→AER, TRAI→FTR**. Positional integrity verified (all 144 fields chain; each record sums to 1200) **and validated against the real sample — 20,298 records slice cleanly**. **Field data types/scale/sign are no longer this component's concern** — the pump ships raw bytes, so there is nothing to type here. **Still open:** (a) V4.11 layout, (b) active-version selection (config vs detected). | Positions/lengths authoritative. No money math here → **no silent-scale risk**; value interpretation is downstream. | V4.8 layout in use; the raw-slicer design retired the "provisional types" risk; V4.11 to follow. |
| ~~Q2~~ | **RESOLVED** — Trigger model = **Mode B folder-watch worker** over a configured path (Windows/macOS local + Azure Files SMB); Azure Blob = future `IFileSource` (Mode C). See §3.2.1–3.2.3. | — | — |
| ~~Q2b~~ | **RESOLVED** — **Signal-driven completion is first-class**: prefer producer **atomic-rename** / **`.done` sentinel**; **stable-size + lock-probe is fallback only**. Every discovery/claim/complete/fail transition is **logged** (feeds lineage/Datadog). Signals + logs both required. | — | — |
| ~~Q3~~ | **RESOLVED (approach)** — batch size `N` + payload-byte cap are **per-profile config**, resolved per transport, with the ASB 256 KB ceiling enforced. Concrete default values are finalized when the ingestion component is built (they don't affect the contract or shared libraries). | — | — |
| ~~Q4~~ | **RESOLVED** — **Infra owns topology**: durable queues/exchanges/topics are provisioned by infrastructure, not the app. MassTransit is configured **not** to auto-provision (publish/send to existing entities). Durable by default. App **expects ACK/NACK** back. | — | — |
| ~~Q5~~ | **RESOLVED** — **Flag & quarantine the bad record, do NOT stop the run.** Invalid record → reject sink + `lineage(rejected)` + reject metric; processing continues. Fail-closed applies to **publish/system** failures only, **not** per-record data errors. | — | — |
| **Q6** | **HEAD/TRAI policy — trailer format now known.** The FTR carries `W136-ECT-TLR-PTG-NBR` = **count of PER (TRAN) records** and `W136-ECT-TLR-ATH-NBR` = **count of AER (AUTH) records**, ending with `W136-ECT-TLR-LST-CDE` = `LAST`. Control-total reconciliation is therefore **definable**: at end-of-file assert `TLR-PTG-NBR == observed PER count` and `TLR-ATH-NBR == observed AER count`. **Open:** whether to enforce it fail-closed (recommended for settlement) and whether to publish/drop HEAD & TRAI. | Data-integrity control; format understood, enforcement is a policy call. | Format captured in the layout; enable fail-closed reconciliation per policy when the ingestion trailer stage is built. |
| ~~Q7~~ | **RESOLVED** — dedupe key = **`FileId + recordSeq`** per record inside the batch (message-level `MessageId = FileId+batchSeq`). Downstream dedupes on these. | — | — |
| ~~Q8~~ | **RESOLVED** — **Watermark required.** Persist last-confirmed byte offset (+ recordSeq) durably; on restart, resume from the watermark. Records in the in-flight window between watermark and crash are re-published → downstream dedupe (Q7) absorbs duplicates. New component: `ICheckpointStore` (§10). **Storage: file-on-disk now** (`FileCheckpointStore`, atomic, config-driven durable directory). **BACKLOG — shared store (Redis, or Blob/DB) behind the same `ICheckpointStore` seam**, so a *brand-new* instance (not just the same pod/volume) can pick up a crashed job. A local mounted volume only recovers same-volume restarts; cross-instance recovery needs shared/central storage. | — | — |
| ~~Q9~~ | **RESOLVED** — **OpenTelemetry, multi-backend.** Structured logs to **stdout → Datadog scrapes** them; metrics/traces via **OTel**, which also enables **Prometheus** and others. Vendor-neutral, no backend lock-in. Backend endpoints soft-coded. | — | — |
| ~~Q10~~ | **RESOLVED** — resolver matches on **both folder location and filename glob**, via **ordered soft-coded rules** in `profiles.yaml`. Natural split is folder-per-type; rules can override ("bend"). | — | — |
| ~~Q11~~ | **RESOLVED** — transports are **flexible & pluggable** (RabbitMQ, Azure Service Bus, …) via **MassTransit**; the profile's `destination.transport` selects one. `IMessagePublisher` (confirmed-or-throw) is the port; MassTransit is the pluggable adapter. | — | — |
| **Q12** | **OPEN — Delimited format schema** (first real delimited file) — delimiter, quote, escape, header-row, field-by-index vs by-name. Seam **must** carry fixed-length **and** delimited from day one (both mandatory); only the delimited *schema detail* is deferred. | Needed to build `DelimitedRecordParser`; same Strategy seam, zero rework. | Deferred until a concrete delimited file/spec exists; fixed-length built now. |
| ~~Q13~~ | **RESOLVED** — on a hard telemetry-backend outage, **block** (preserves the "never drop lineage" guarantee). A local durable spill can be added later if availability ever needs to win; not built now. | — | — |
| **Q14** | **Sensitive-data handling — approach chosen, specifics to confirm.** Data must be **reversible (encrypt *and* decrypt)** ⇒ **hashing/SHA ruled out (one-way); binary-encoding ruled out (not encryption)**. Algorithm **chosen: AES-256-GCM** (AEAD, FIPS-approved, HSM-native), **envelope-wrapped (per-batch DEK / HSM KEK)**, **field-level** (sensitive fields only; diagnostics clear), behind a **pluggable `ICryptoProvider`** with **self-describing ciphertext** (`alg/keyId/keyVersion/nonce/tag`) for non-breaking rotation — GCM-SIV / post-quantum are drop-in plugins (§8.2). Keys in **Key Vault Managed HSM**, wired via **MassTransit `UseEncryption`**. Plus TLS in transit, at-rest (infra), **data minimization** (mask PAN first6/last4). **Open to confirm with security:** exact sensitive-field set, retention, key custody/rotation cadence. | PCI: card data on a queue/in logs is a controlled asset; reject queue must stay diagnosable without exposing PAN. | As stated: reversible field-level AES-GCM via KMS, minimize + mask, both ways. |

---

## 13. Shared libraries & dependencies

Cross-cutting concerns are extracted into **focused, single-responsibility shared libraries** — justified because a multi-component system is real (this ingestor, sibling file-ingestors, stage-2 consumers, the state machine) and they must share **contracts, crypto, and observability** exactly. **Explicitly rejected: a `Common`/`Shared`/`Utils` grab-bag** — low cohesion, a change-magnet, a cycle risk (violates §1.1 cohesion/coupling). Each library has **one** reason to change; dependency direction is one-way (libraries are mechanism and never depend on the app; no cycles).

### Internal shared libraries (`*` = solution root namespace, fixed once at setup)
| Library | Responsibility | Shared because |
|---|---|---|
| `*.Messaging.Contracts` | **generic** envelope + open name→value field bag + `EncryptedValue` + reject DTOs — **no G266-typed fields** (those stay in the versioned layout YAML, §5) | producer & all consumers bind the **same** format-agnostic contract; new file types add YAML, not code |
| `*.Security.DataProtection` | `IFieldProtector`, AES-256-GCM envelope crypto, classification-policy loader, Key Vault/KMS client | producer encrypts / consumers decrypt — **must** share the exact scheme (§8.2/§8.3) |
| `*.Observability` | OTel wiring (traces/metrics/logs), identity/correlation propagation, structured-log conventions | every service emits consistent, correlated telemetry |
| `*.Messaging.MassTransit` | MassTransit conventions — publisher, resilience (retry/CB/rate-limit), serialization bridge, bus registration | uniform transport behaviour across producers/consumers |
| `*.FileIngestion.Core` *(candidate)* | the generic fixed-length/delimited engine + layout/profile model | **only** promoted to a shared library if sibling **deployables** appear; today it is this component's core — not a premature library |

**Final count: 4 shared libraries** (Contracts, DataProtection, Observability, MassTransit). A `*.Configuration` library was **considered and rejected** — options binding + Key-Vault-secret access is a thin composition-root concern loaded per component from Helm values → env/appsettings → a Key Vault config provider, not shared behaviour. Making it a library risked exactly the `Common`/utils grab-bag §13 forbids. Each library already binds its own options directly from `IConfiguration`.

Dependency direction: `Contracts` ← `DataProtection` / `Observability`\* / `MassTransit` ← app (\*Observability is independent of Contracts — see §1.2). No cycles (§1.1-D).

**Enforced by the build, not by review (DRY/SOLID as gates):**
- **Architecture tests** (NetArchTest / ArchUnitNET) fail the build on: a dependency-direction violation (any library referencing the app), a **cycle** between libraries, or a forbidden cross-reference. SOLID-D and acyclicity become CI gates.
- **One-sentence-purpose rule per library** — if a library's responsibility can't be stated without "and", it is split. A package trending toward catch-all (unrelated types, disjoint dependents) is a defect caught before merge; the `Common`/`Utils`/`Shared` names are banned outright.
- **Narrow public surface** — each library exposes only its contract (interface segregation at the package grain); internals stay internal.
- **DRY at the right grain** — a shared library exists only where shape **and** reason-to-change coincide across ≥2 real consumers (which the producer/consumer split gives us) — never on "might reuse".

### Third-party packages (by concern — not reinvented)
- **Logging/telemetry:** `Microsoft.Extensions.Logging`, OpenTelemetry SDK (+ OTLP & Prometheus exporters); structured JSON → stdout (built-in JSON console formatter or Serilog) for Datadog scrape.
- **Encryption/secrets:** `System.Security.Cryptography` (AES-GCM, **platform crypto — no home-grown crypto**), `Azure.Security.KeyVault.Keys`/`.Secrets` + `Azure.Identity` (Managed Identity).
- **Messaging:** `MassTransit`, `MassTransit.RabbitMQ`, `MassTransit.Azure.ServiceBus.Core`.
- **Config:** `Microsoft.Extensions.Configuration`/`Options`, Key Vault config provider.
- **Parsing/pipeline:** `YamlDotNet`; `System.IO.Pipelines`, `System.Threading.Channels` (built-in).
- **Health:** `Microsoft.Extensions.Diagnostics.HealthChecks` (+ MassTransit health).
- **Resilience:** MassTransit built-in (Polly-based); `Microsoft.Extensions.Resilience` if needed outside the bus.
- **Time:** `TimeProvider` (built-in; injected for deterministic tests).
- **Testing:** xUnit, FluentAssertions, NSubstitute, coverlet, Stryker.NET.

---

## 14. Build status (POC)

Stage-1 implemented, `main` green: **512 tests**, per-assembly line-coverage gates met (≥90% floor;
`Common.FileIngestion` ~98%, `Ingestion.Worker` ~95%, shared libs 99–100%), SonarQube gate clean
(0 new issues). Scope is **single-profile G266** — one layout loaded from config; the profile router /
keyed factories / delimited parser of §1.2 remain design-level (build scope, §1.2), not yet built.

**Delivered:** generic layout model + loader (record length, **terminator**, encoding, discriminator,
and per-field `encrypt`/`required` — all data); raw-slicer fixed-length parser (emits raw text, no value
interpretation); streaming reader (O(1) memory, SHA-256 FileId via the single-sourced `FileContentHash`);
**one layout loaded from a configured path** (not a multi-profile router); positional watermark checkpoint
(keyed by stable source key) + file store; batcher; field-protection stage (`LayoutProtectionPolicy` →
`RecordProtector`, fail-closed on a name classified two ways); reject sink (raw record encrypted);
ingestion metrics + per-record lineage, with the **correlation scope opened at the worker** so spans/logs
carry run/correlation ids; worker-host health (heartbeat/liveness/readiness on a single-sourced tag
vocabulary); the end-to-end `FileIngestionPipeline` (hash → resume → parse → protect → batch → confirmed
publish → watermark, fail-closed); folder file source (claim/complete/fail + orphan recovery; a skipped
claim is logged, never silently swallowed); and the `Ingestion.Worker` host — composition root with
fail-closed config binding, a narrow `IIngestFileDispatcher` seam over **MassTransit.Mediator**, a
`TimeProvider`-driven poll loop, and a Dockerfile.

**Decisions:** worker host for the POC (Azure Functions deferred — see [[host-and-mediator-decision]]);
mediator = MassTransit.Mediator (OSS) at the host boundary so the library stays transport-agnostic;
publisher port `IMessagePublisher` relocated to Contracts (DIP); the worker depends on a narrow
`IIngestFileDispatcher` (ISP) rather than the full mediator surface. Message transport currently declares
**RabbitMQ only** — the Azure Service Bus enum member was removed until it is actually wired, so `Validate()`
never certifies a transport the composition root would reject (ASB remains a config-selectable case behind
the MassTransit seam when built, §1.2/§6).

**Review remediation (hardening pass):** a full DRY/SOLID/no-hardcoding review found and fixed 13 proven
defects, each as a one-concern slice with happy+unhappy tests and a clean Sonar gate before commit —
notably: terminator moved into the layout (framing fully layout-driven); numeric/enum config **fails
closed** (`RequiredConfig`, not silent `GetValue` defaults); `ClearFieldValue` validates its wire type at
construction; the decrypt paths zero recovered cleartext (symmetric with encrypt); the resume-integrity
hash is single-sourced; the correlation scope is actually opened; messaging options are immutable
(`init`-only); the worker distinguishes genuine shutdown from a non-shutdown cancellation.

**Backlog (not blocking the POC):**
- **Multi-profile router + keyed factories** (§1.2) — single-profile today; add when a second profile is real.
- Azure Functions host (isolated worker, Blob/Event Grid trigger) — deferred by decision.
- Real G266 **data-protection policy** for the full field set (security-owned) — driven by the layout's
  `encrypt` flags today; classification is fail-closed on conflicts.
- ~~**Reject-payload encryption**~~ — **DONE.** The reject raw record is encrypted before the reject
  queue via `IPayloadProtector` (unconditional AEAD, AAD-bound); never carried in clear.
- **Shared checkpoint store** (Redis/Blob/DB) for cross-instance resume (§12/Q8) — file store today.
- **EBCDIC / code-page** encodings: register `CodePagesEncodingProvider` when a layout needs a
  non–built-in single-byte encoding.
- Byte-cap uses a content-length proxy (documented in `Batcher`); tighten if a transport limit is hit.
- **Azure Service Bus transport** — re-add the enum member with its wiring when the case is real (§6).
- **V4.11 layout** (external dependency) — V4.8 done.
