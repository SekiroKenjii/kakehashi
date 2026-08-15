# 07 — Prompt khởi đầu cho AI Agent

Prompt viết bằng tiếng Anh (agent làm việc ổn định nhất với tiếng Anh; codebase và
commit message cũng tiếng Anh). Có 2 phần:

- **Phần A** — System/Project prompt: dán vào `CLAUDE.md` (hoặc system prompt của
  agent) ở repo `kakehashi`, sống lâu dài xuyên suốt dự án pivot.
- **Phần B** — Kickoff prompt: message đầu tiên giao việc cho từng phase.

Trước khi dùng: commit cả bộ tài liệu này vào repo tại `docs/pivot/` để agent
đọc trực tiếp bằng đường dẫn.

---

## Phần A — Project prompt (dán vào CLAUDE.md / system prompt)

```markdown
# Kakehashi — Boilerplate Pivot

## Mission
Kakehashi (this repo) is pivoting from a standalone WinUI 3 + Go application into a
true boilerplate: a template repository plus a Go CLI (`kakehashi`) that scaffolds
new projects (`kakehashi new`) and generates wired-up modules (`kakehashi add module`).
The full plan lives in `docs/pivot/00-OVERVIEW.md` through `06-PHASE-5-RELEASE.md`.
Read the overview and the document for the current phase before doing anything.

## Repository facts
- Monorepo: `client/` (WinUI 3, .NET 10, CommunityToolkit.Mvvm, modular monolith,
  mediator between modules), `server/` (Go 1.26, connect-go, modular monolith,
  one static binary), `proto/` (the single contract, managed by buf).
- The CLI will live in `tools/cli/` (Go, cobra). Scaffolding engine proof lives in
  `tools/rename/`.
- Server module layout: `api/ domain/ store/ service/ rpc/ module.go`. Only `api/`
  is importable by other modules; only `rpc/` may import generated protobuf code.
- Client modules communicate only via mediator notifications; three layers enforced
  by architecture tests.

## The three gates (INVARIANT — never weaken, never skip, never add exceptions)
1. `cd server && go run ./tools/archlint` — server module boundaries.
2. `cd client && dotnet test` (Kakehashi.ArchitectureTests) — client layers.
3. `buf lint` and `buf breaking --against '.git#branch=main'` — the contract.

Every change you make must keep all three green. If a task seems to require
weakening a gate or adding an exemption, STOP and ask instead of doing it.
Generated code (from `kakehashi add module`) must pass all gates with zero
manual edits — that is the product's core promise.

## Working rules
- Small, reviewable changes. One logical unit per commit. Conventional commits with
  scope: `template:` for template-side changes, `cli:` for CLI changes
  (e.g. `feat(cli): add doctor command`).
- Before claiming done, actually run the relevant builds/tests/gates and paste the
  results. "It should work" is not done.
- Placeholders use the `__NAME__` convention (see `docs/pivot/02-…` §1). Never
  introduce a templating engine syntax into template source files; conditional
  content is handled via unit files + markers, not template logic.
- Markers use the exact format `// kakehashi:<section>:begin` / `:end`
  (`//` or the file's native comment token). Marker and unit-file schemas are
  versioned; never change their format silently.
- Windows-only steps (client build, WinUI runtime) may be unavailable in your
  environment. In that case: still write the code, run every check you CAN run
  (Go build, archlint, buf, dotnet build if SDK present), and explicitly list the
  checks that still need a Windows machine. Never claim client verification you
  did not perform.
- Do not rename, reformat, or "clean up" files outside the task's scope — diff
  noise breaks the template's rename/upgrade story.
- Design/visual work must avoid AI-cliché styling: no purple/indigo gradients,
  no glassmorphism. Follow the existing Fluent + vermilion token system.
- Docs are deliverables, not afterthoughts: if a task changes behavior, update the
  matching doc in the same PR.

## Definition of done (applies to every task)
1. All three gates green (or explicitly listed as "needs Windows verification").
2. CI smoke jobs (scaffold → rename → build) green once they exist (Phase 1+).
3. No stray `Kakehashi` / `SekiroKenjii` identity strings outside allowed locations
   (template README, manifest, LICENSE header of the template repo itself).
4. Acceptance criteria checkboxes of the current phase document updated honestly.
```

---

## Phần B — Kickoff prompts theo phase

### B0 — Phase 0 (Inventory)

```markdown
Read docs/pivot/00-OVERVIEW.md and docs/pivot/01-PHASE-0-INVENTORY.md fully.

Task: execute Phase 0.
1. Build the inventory script under tools/inventory/ that scans the repo for
   identity strings (Kakehashi, kakehashi, SekiroKenjii, 架け橋, the vermilion hex,
   notes/activity paths) and emits CSV: path, match, line, suggested_group.
2. Produce docs/BOILERPLATE.md classifying 100% of tracked files into
   CORE / EXAMPLE / IDENTITY, using the format in the phase doc §3. Mark hybrid
   files with (M) and add the standard markers
   (kakehashi:module-imports / kakehashi:module-registrations / etc.) to them.
3. Define the removable unit "notes" as templates/units/notes.json (paths + markers).
4. On a scratch branch, prove the bare skeleton: delete everything in the notes unit,
   strip marker regions, build server + run archlint + buf lint. Report results.
5. Write short ADRs for decisions D1–D5 in docs/adr/ using the recommendations in
   the phase doc as defaults; flag any you disagree with instead of silently changing.

Deliverables: the CSV script, docs/BOILERPLATE.md, templates/units/notes.json,
the ADRs, and a report of the bare-branch verification. Do NOT start Phase 1 work.
Ask before touching anything classified as ambiguous.
```

### B1 — Phase 1 (Templatization)

```markdown
Prerequisite: Phase 0 merged (docs/BOILERPLATE.md, notes unit, markers, ADRs).
Read docs/pivot/02-PHASE-1-TEMPLATIZATION.md fully.

Task: execute Phase 1 as a sequence of small PR-sized commits, in this order:
1. Shrink the replacement surface: make archlint derive the module path from go.mod;
   make ArchitectureTests read the root namespace from a single TestConstants;
   neutralize resource keys that embed the app name.
2. Introduce placeholders block by block (proto → server → client → misc) per the
   maps in §2. After each block, run everything runnable and report.
3. Implement tools/rename/rename.ps1 and rename.sh per §3, including the
   self-check step (grep for leftover placeholders/identity → non-zero exit).
4. Add CI jobs scaffold-smoke-server (ubuntu) and scaffold-smoke-client (windows)
   per §4.
5. Split the README into the template README and templates/README.scaffold.md.

Constraint reminder: literal placeholder substitution only; no template-engine
syntax in source files. Verify the notes unit still applies cleanly after renames.
Finish by updating the Phase 1 acceptance checklist with honest statuses.
```

### B2 — Phase 2 (CLI)

```markdown
Prerequisite: Phase 1 merged and smoke jobs green.
Read docs/pivot/03-PHASE-2-CLI.md fully.

Task: build the CLI MVP in tools/cli/ with the package layout from §1.
Implement in this order, each step compiling and unit-tested before the next:
1. manifest + unitfile packages (schema from §2), with round-trip tests.
2. scaffold engine (placeholder apply + unit pruning + atomic temp-dir workflow),
   ported from the rename script; golden tests on a mini fixture template.
3. template fetch/verify/cache (§2 step 3). For local dev, support
   --template-dir to scaffold from a local checkout without a release.
4. `kakehashi new` command wiring the pipeline (§2), including --bare, --dry-run,
   --no-input, self-check, git init, manifest write.
5. `kakehashi doctor` per §3, with --json.
6. CI integration job replacing/absorbing the Phase 1 smoke jobs: scaffold via the
   CLI, then build + gates on both OSes.

The wizard (no-args TUI) is Phase 4 — for now, no-args prints usage and exits 2.
Finish by updating the Phase 2 acceptance checklist.
```

### B3 — Phase 3 (Generators)

```markdown
Prerequisite: Phase 2 merged; CLI can scaffold from a local template dir.
Read docs/pivot/04-PHASE-3-GENERATORS.md fully.

Task: implement `kakehashi add module`, `add page`, `remove module` per the spec.
Order: marker engine (+unit-record writing) → proto generation (+buf integration)
→ server generation (+archlint verify) → client generation (+build/arch-test verify
where possible) → remove/rollback → add page.

Hard requirements:
- Generated code passes all three gates with zero manual edits.
- Every pipeline is atomic: any failure rolls back completely.
- Set up the gen-sync mechanism and the notes-equivalence drift test from §4.
- CI pipeline: new --bare → add module orders → gates → remove → tree clean.

Finish by updating the Phase 3 acceptance checklist.
```

### B4 / B5 — Phase 4 & 5

```markdown
Read docs/pivot/05-PHASE-4-UI.md (or 06-PHASE-5-RELEASE.md) fully, then implement
it as specified. For Phase 4, respect the visual constraints in §2.3 strictly.
For Phase 5, do not publish any tag or release without my explicit confirmation —
prepare everything up to the dry-run and stop for review.
```

---

## Gợi ý vận hành với agent

- **Mỗi phase một phiên/branch riêng**, kickoff prompt tương ứng; đừng dồn nhiều
  phase vào một phiên dài — context loãng, chất lượng giảm.
- Sau mỗi bước lớn, yêu cầu agent **dán output thật** của lệnh gate — đây là chốt
  chống "báo cáo xanh ảo".
- Khi agent đề xuất lệch spec: yêu cầu ghi thành ADR ngắn thay vì tự sửa spec —
  giữ bộ docs này là nguồn sự thật.
- Task client-side nên chạy agent trên máy Windows (hoặc chấp nhận vòng
  verify tay); server/proto/CLI chạy tốt trên Linux.
