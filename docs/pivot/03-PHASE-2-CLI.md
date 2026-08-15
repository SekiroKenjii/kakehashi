# Phase 2 — CLI `kakehashi` (MVP)

**Mục tiêu:** binary Go duy nhất với `kakehashi new` và `kakehashi doctor`,
đóng gói lại logic rename của Phase 1, thêm fetch template theo version và manifest.

**Thời lượng:** 2–3 tuần. **Vị trí code:** `tools/cli/` trong monorepo (quyết định D4).

## 1. Kiến trúc CLI

```
tools/cli/
  cmd/kakehashi/main.go
  internal/
    cli/            cobra commands (new, doctor, version; add/remove ở Phase 3)
    tui/            wizard (Phase 4) — tách package ngay từ đầu
    template/       fetch, verify, extract, cache
    scaffold/       apply placeholders, remove units, git init
    manifest/       đọc/ghi .kakehashi.json
    checks/         doctor checks
    unitfile/       parser templates/units/*.json
```

- **Stack:** Go (trùng server → 1 toolchain), `spf13/cobra`, không CGO,
  build 1 binary cho windows/amd64 + arm64 (linux/darwin cho server-only workflow là bonus).
- **Không phụ thuộc git ngoài:** dùng archive tải qua HTTPS thay vì `git clone`
  (máy người dùng có thể chưa có git); `git init` cuối cùng chỉ chạy nếu git có mặt,
  không có thì cảnh báo và bỏ qua.

## 2. Lệnh `kakehashi new`

```
kakehashi new <app-name> [flags]

Flags:
  --module string        Go module path (bắt buộc nếu không chạy wizard)
  --title string         Display name (default: app-name)
  --proto-package string (default: lower(app-name))
  --accent string        (default: "#E34234")
  --author string        (default: git config user.name nếu có)
  --with-example         kèm module notes (default: true)
  --bare                 = --with-example=false
  --auth string          inapp|browser|none (default: inapp)
  --template-version     tag template, vd v0.3.0 (default: mới nhất tương thích)
  --dir string           thư mục đích (default: ./<app-name-lower>)
  --offline              chỉ dùng cache
  --dry-run              in kế hoạch, không ghi file
```

Chạy `kakehashi new` **không tham số** → mở TUI wizard (Phase 4). Có `--no-input`
để CI dùng, fail nếu thiếu tham số bắt buộc.

### Pipeline thực thi

1. **Validate** inputs (regex như Phase 1 §1); kiểm tra thư mục đích rỗng.
2. **Resolve version**: đọc kênh release (GitHub Releases của repo template),
   chọn tag `template/vX.Y.Z` mới nhất có `cliCompat` khớp (xem 06-RELEASE).
3. **Fetch**: tải tarball release asset (không phải auto-generated source archive —
   dùng asset build sẵn `template-vX.Y.Z.tar.gz` để có checksum ổn định),
   verify SHA-256 từ file `checksums.txt` của release, giải nén vào temp.
   Cache tại `%LOCALAPPDATA%/kakehashi/templates/<version>/`.
4. **Prune theo lựa chọn**:
   - `--bare` → áp dụng `templates/units/notes.json` (xóa path, gỡ vùng marker).
   - `--auth none` → áp dụng unit `auth-inapp`… (mỗi lựa chọn có unit file riêng;
     nếu Phase 1 chưa tách auth thành unit thì v1 chỉ hỗ trợ inapp/browser qua
     config `appsettings.json`, `none` để v1.1).
5. **Apply placeholders** (nội dung + tên file, deepest-first — port từ rename script).
6. **Self-check**: grep placeholder sót / identity sót → fail sạch (xóa thư mục đích).
7. **Ghi manifest** `.kakehashi.json`.
8. **git init + commit đầu** (nếu có git): message `chore: scaffold from kakehashi template vX.Y.Z`.
9. **In next steps** (đúng như README scaffold: compose up → curl healthz → dotnet run).

**Nguyên tắc atomic:** mọi thao tác làm trong temp dir, thành công mới move vào
thư mục đích. Fail giữa chừng không để lại rác.

### Manifest `.kakehashi.json`

```json
{
  "schemaVersion": 1,
  "template": { "source": "github.com/SekiroKenjii/kakehashi", "version": "0.3.0" },
  "cli": { "version": "0.2.1" },
  "createdAt": "2026-09-01T10:00:00Z",
  "inputs": {
    "appName": "OrderDesk",
    "goModule": "github.com/me/orderdesk",
    "protoPackage": "orderdesk",
    "accent": "#E34234",
    "auth": "inapp",
    "withExample": true
  },
  "units": { "removed": [], "applied": ["notes"] }
}
```

Đây là nền móng cho `kakehashi upgrade` (v2) và là nơi duy nhất được phép
nhắc đến "kakehashi" trong project sinh ra.

## 3. Lệnh `kakehashi doctor`

Kiểm tra môi trường, in bảng ✅/⚠️/❌ + hướng khắc phục (winget/link):

| Check | Cách kiểm | Mức |
|---|---|---|
| Go ≥ 1.26 | `go version` | ❌ nếu thiếu |
| .NET SDK 10 | `dotnet --list-sdks` | ❌ |
| buf | `buf --version` | ❌ |
| Docker + daemon chạy | `docker info` | ⚠️ (chỉ cần khi chạy compose) |
| Windows App SDK / Runtime | registry / `winget list` | ⚠️ (chỉ client) |
| git | `git --version` | ⚠️ |
| Windows Developer Mode | registry | ⚠️ (chỉ khi deploy MSIX) |
| Network tới GitHub | HEAD release URL | ⚠️ (offline vẫn dùng cache) |

`kakehashi doctor --json` cho CI. `new` tự chạy subset check ❌ trước khi scaffold.

## 4. Lệnh phụ

- `kakehashi version` — version CLI + template mới nhất trong cache + mới nhất remote.
- `kakehashi cache clean|list`.

## 5. Testing plan

| Tầng | Nội dung |
|---|---|
| Unit | validate inputs; placeholder apply trên fixture nhỏ; unitfile parser; manifest round-trip |
| Golden | scaffold fixture-template mini → so cây thư mục + nội dung với golden dir |
| Integration (CI) | `kakehashi new smoke --module example.com/smoke --no-input` với template thật → build server (Linux job) + build client & gates (Windows job). **Job này thay thế/gộp với scaffold-smoke của Phase 1** — từ đây CLI là đường scaffold chính thức, rename script chỉ còn là fallback cho người dùng GitHub template button |
| Ngược | `--bare`, `--dry-run`, thư mục đích không rỗng, mất mạng (dùng cache), checksum sai |

## 6. Phân phối MVP

- `go install github.com/SekiroKenjii/kakehashi/tools/cli/cmd/kakehashi@latest`
- GitHub Release: binary windows-amd64/arm64 (+ linux/darwin), kèm checksums.
- winget/scoop để Phase 5.

## 7. Acceptance criteria Phase 2

- [~] `kakehashi new demo --module example.com/demo` trên máy sạch có prerequisites
      → project build xanh hai đầu, 3 gate xanh, < 10 phút.
      Server half verified for real: scaffold in ~1s, then `buf lint`, `buf generate` (diff clean),
      `go build`, `go vet`, `go test`, archlint (61 packages) and `gofmt` — all green. The client
      half is unverified here for the same reason as Phase 1 (no Windows, no .NET SDK);
      `scaffold-client` is what proves it.
- [x] `--bare` cho skeleton xanh không Notes.
      Scaffolded and run: `buf lint`, `go build`, `go test`, archlint (53 packages) green, no
      `Notes` module on either half.
- [x] `.kakehashi.json` đúng schema; không còn chuỗi identity nào khác trong project.
      The manifest round-trips against the documented shape byte for byte, and a test walks the
      scaffolded tree asserting the manifest is the only file that names the generator. Stronger:
      the CLI's output tree and `rename.sh`'s differ by exactly one file — the manifest.
- [~] `doctor` phát hiện đúng khi gỡ từng prerequisite.
      Verified for Go, .NET, buf, the protoc plugins, docker and git. The two Windows probes
      (App Runtime, Developer Mode) have never run on Windows.
- [x] Fail atomic: giả lập lỗi giữa chừng → không để lại thư mục rác.
      Three failure points are tested — a unit file that disagrees with the tree, a surviving
      placeholder, a destination that is not empty — and each leaves neither the destination nor a
      working directory behind. Everything happens in a temp dir beside the destination, which is
      also why the final move is a rename and not a copy.
- [~] CI integration job (Linux + Windows) xanh, chạy mỗi push.
      `scaffold-smoke.yml` now builds and tests the CLI, scaffolds with it on both operating
      systems, and keeps a `rename-fallback` job. Written and locally rehearsed command by command;
      the run itself is what the first push proves.

### Điều phát sinh, không có trong spec

1. **`templates/template.json` exists now, not in Phase 5.** The CLI needs to know which paths
   belong to the template repository rather than to a project, and a list compiled into the binary
   would leak a new template-only directory into every project until the CLI shipped again. The
   descriptor holds that list, the `requiresCli` range 06-PHASE-5 §1.2 already specifies, and the
   name of the setting `--auth` writes to. The rename scripts keep their own copy of the drop list;
   that duplication ends when they do.
2. **`new` regenerates the contract, and cannot be talked out of it.** A `.pb.go` embeds the file
   descriptor as a string with byte-length prefixes, so substituting a shorter package name into it
   leaves lengths that no longer match and the server fails to parse its own descriptor at startup.
   protoc also derives Go symbol names and the per-language namespace options from the proto
   package. `rename.sh` warns here; the CLI fails, and the failure is atomic.
3. **The `new` preflight is a subset of the ❌ checks, not all of them.** Scaffolding needs buf and
   the two protoc plugins. Building the result also needs Go and the .NET SDK — but a server-only
   workflow on Linux creates projects perfectly well without .NET, and refusing there would be a
   check that is wrong more often than it is right. `doctor` still reports the whole table.
4. **`--auth none` is refused rather than ignored**, as §2 step 4 anticipated: Phase 1 did not
   split auth into a unit. `inapp` and `browser` write `Auth:Mode` through the descriptor. The
   rewrite goes through a JSON decode, so the settings file comes back with its keys in
   alphabetical order — only when the mode actually changes.
5. **One package beside the seven in §1:** `internal/semver`, because doctor's minimum versions and
   the template's compatibility range are the same comparison written twice otherwise. `internal/tui`
   is one of the seven, and holds the wizard's refusal — `new` with no arguments calls it and turns
   that refusal into exit code 2.
6. **`--root-namespace` and `--year` are not flags.** §2's table has neither and both derive
   (from the app name and from the clock). The manifest records them anyway: an upgrade that cannot
   reproduce the inputs cannot diff anything.
7. **`version` does not reach the network.** §4 wants the newest remote version in it. That, and
   `cache list|clean`, wait for the release channel to exist — until a `template/vX.Y.Z` tag is
   published there is nothing to ask about. It prints the CLI version and what is cached.
8. **Go ≥ 1.26 is a warning, not a failure, when the toolchain can download itself.** `go version`
   reports what is on PATH, and since 1.21 an older Go fetches the toolchain a module asks for
   unless `GOTOOLCHAIN=local` forbids it. Failing on the banner alone would refuse machines that
   build the project fine.
9. **`--title` and `--author` are free text minus five characters.** 02-PHASE-1 §1 calls both free,
   and they are, except that the same value is substituted literally into a Go string literal, an
   XML attribute and a JSON string — which escape `"`, `&`, `<`, `>` and `\` three different ways.
   A title of `Ben & Jerry` produced a project whose client half was not well-formed XML, so those
   five are refused with a message that says why. `--author` defaults to `git config user.name`,
   which is not a value the caller necessarily typed.
10. **`--template-dir` is a flag §2 does not list**, and it is the only one that works before the
    first `template/vX.Y.Z` tag exists: it scaffolds from a checkout, which is what CI uses and
    what the README's first command shows. A project scaffolded that way records the directory it
    was read from as `template.source`, rather than the release channel it never touched — true,
    and a manifest an upgrade cannot fetch from until the project is re-scaffolded from a release.
11. **`--dry-run` does the work and then throws it away.** §2 asks it to print the plan and write
    nothing. It prints the plan, and then also stages, prunes, substitutes, renames and regenerates
    in the temporary directory before deleting it — a plan that has been carried out once is the
    only kind that cannot be wrong. The cost is that it needs buf and the protoc plugins, like a
    real run.
12. **Left alone, mentioned here:** `server/cmd/server/main.go` and the client's `ModuleCatalog.cs`
   both point a reader at `docs/BOILERPLATE.md`, which no scaffolded project has. Pre-existing from
   Phase 1, and fixing it is a comment change in two files nobody asked for.
