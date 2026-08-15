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

- [ ] `kakehashi new demo --module example.com/demo` trên máy sạch có prerequisites
      → project build xanh hai đầu, 3 gate xanh, < 10 phút.
- [ ] `--bare` cho skeleton xanh không Notes.
- [ ] `.kakehashi.json` đúng schema; không còn chuỗi identity nào khác trong project.
- [ ] `doctor` phát hiện đúng khi gỡ từng prerequisite.
- [ ] Fail atomic: giả lập lỗi giữa chừng → không để lại thư mục rác.
- [ ] CI integration job (Linux + Windows) xanh, chạy mỗi push.
