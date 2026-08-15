# Phase 3 — Generators: `add module`, `add page`, `remove module`

**Mục tiêu:** điểm khác biệt của Kakehashi so với template thường —
một lệnh sinh ra module hai đầu đã nối dây, contract đã generate,
**cả 3 gate xanh ngay lập tức, không sửa tay**.

**Thời lượng:** 2–3 tuần. Phụ thuộc: markers (Phase 0), units (Phase 1), CLI (Phase 2).

## 1. `kakehashi add module <id>`

```
kakehashi add module orders [flags]
  --entity string       tên entity chính PascalCase (default: singular của id)
  --crud                sinh sẵn CRUD end-to-end (default: true)
  --store sql|mongo     (default: sql)
  --no-client           chỉ server + proto
  --no-page             có client module nhưng không sinh page
  --dry-run
```

Ràng buộc `<id>`: `^[a-z][a-z0-9]{1,29}$`, không trùng module hiện có,
không thuộc reserved list (`app`, `platform`, `auth`, `gen`, …).

Chạy **trong** thư mục project (phát hiện qua `.kakehashi.json`; version template
trong manifest phải ≥ version tối thiểu mà CLI hỗ trợ generator).

### 1.1 File sinh ra — phía proto

```
proto/<pkg>/orders/v1/orders.proto
  package <pkg>.orders.v1
  service OrdersService { Create/Get/List/Update/Delete }   (nếu --crud)
  message Order { id, name, created_at, updated_at }        (tối thiểu, dev sửa sau)
  option go_package / csharp_namespace theo convention của project
```

Sau khi ghi file: chạy `buf lint` + `buf generate`. Generated code rơi đúng chỗ
hai đầu theo `buf.gen.yaml` hiện có.

### 1.2 File sinh ra — phía server

```
server/internal/modules/orders/
  api/api.go           interface OrdersAPI + DTO + event types
  domain/order.go      entity + invariants (validate name…)
  store/store.go       interface Store
  store/sql.go         impl SQL Server, schema `orders`, bảng `orders.Order`
  store/migrations/0001_init.sql
  service/service.go   use cases, publish event qua platform event bus
  rpc/handler.go       connect handler, map api <-> pb (duy nhất được import gen code)
  module.go            wiring: New(deps) → app.Module
```

Sửa file có marker:

```
server/cmd/server/main.go
  kakehashi:module-imports        += orders
  kakehashi:module-registrations  += app.Register(orders.New(deps))
```

### 1.3 File sinh ra — phía client

```
client/src/Modules/<App>.Modules.Orders/
  <App>.Modules.Orders.csproj                (+ add vào .slnx)
  Contracts/  (notifications/requests cho mediator)
  Services/OrdersService.cs                  (gọi connect client từ gen code)
  ViewModels/OrdersPageViewModel.cs
  Views/OrdersPage.xaml(.cs)                 (list + form CRUD tối thiểu, theo theme)
  ModuleRegistration.cs                      (DI + nav entry)
```

Sửa file có marker: composition root client (DI `AddOrdersModule()`),
nav registry (menu item "Orders", icon mặc định), slnx.

**Ràng buộc kiến trúc phải tự thỏa:** client module chỉ nói chuyện với module khác
qua mediator; page/VM tuân 3-layer → template code sinh ra phải được viết sao cho
ArchitectureTests pass mà không cần ngoại lệ.

### 1.4 Pipeline & verify

1. Parse manifest, validate id, dry-run plan.
2. Ghi proto → `buf lint` → `buf generate`.
3. Ghi server files → chèn marker → `go build ./...` → `go run ./tools/archlint`.
4. Ghi client files → chèn marker + slnx → (nếu đang trên Windows) `dotnet build` +
   ArchitectureTests filter; nếu không phải Windows: in cảnh báo "client chưa được
   verify trên máy này".
5. Fail bất kỳ bước nào → **rollback toàn bộ** (thao tác trên staging copy của các
   file bị sửa; file mới thì xóa). In lỗi gốc + hướng dẫn.
6. Thành công → in summary + gợi ý: sửa message trong proto trước, chạy lại
   `buf generate`, rồi đổ logic vào domain/service.

Marker engine: chèn theo thứ tự alphabet trong vùng, idempotent
(chạy lại với id đã tồn tại → lỗi rõ ràng, không chèn đôi).

## 2. `kakehashi add page <module> <PageName>`

Sinh `Views/<PageName>Page.xaml(.cs)` + `ViewModels/<PageName>PageViewModel.cs`
+ nav entry (flag `--no-nav` để bỏ), + đăng ký DI. Chỉ đụng phía client.
Verify: build + ArchitectureTests (Windows).

## 3. `kakehashi remove module <id>`

- Nguồn sự thật để gỡ: **ghi lại lúc sinh**. `add module` ghi
  `.kakehashi/units/<id>.json` (đúng format unit file của template) gồm danh sách
  file đã tạo + marker đã chèn. `remove` áp dụng ngược lại.
- Với module `notes` từ template: dùng `templates/units/notes.json` đã có.
- An toàn: yêu cầu working tree sạch (git status), hoặc `--force`.
- Cảnh báo phần không tự gỡ: bảng SQL đã migrate trong DB thật (in câu lệnh DROP SCHEMA
  gợi ý, không tự chạy), reference tay mà dev đã thêm (nếu build fail sau khi gỡ,
  in danh sách site lỗi).

## 4. Chiến lược template nội bộ cho generator

- Template file dùng `text/template` của Go, embed vào CLI binary
  (`tools/cli/internal/gen/templates/…`). Khác Phase 1: đây là template *nhỏ, thuần code mới*,
  không phải cả repo — nên dùng template engine ở đây là hợp lý (cần biến hoá tên,
  singular/plural, PascalCase).
- **Nguồn sự thật của mẫu module = module `notes` trong repo template.** Quy trình dev:
  sửa notes → chạy tool `tools/cli/gen-sync` để derive lại generator templates từ notes
  (thay tên bằng biến). Có test so sánh: generator sinh module `notes2` phải tương đương
  cấu trúc với `notes`. Điều này chống drift giữa example và generator.
- Version khớp: generator templates gắn với template version (đọc từ manifest);
  CLI từ chối generate nếu template project quá cũ/mới so với dải hỗ trợ.

## 5. Testing plan

| Tầng | Nội dung |
|---|---|
| Unit | marker engine (chèn/gỡ/idempotent/alphabet); naming utils (plural, case); unit-record ghi/đọc |
| Golden | `add module orders` trên project fixture → so với golden tree |
| Integration CI | pipeline: `new smoke --bare` → `add module orders` → build + 3 gate (Linux server-side; Windows full) → `remove module orders` → build lại xanh → so working tree với trạng thái trước khi add (phải sạch) |
| Drift test | generator ⇄ notes equivalence test (mục 4) |

## 6. Acceptance criteria Phase 3

- [~] `add module orders` trên project mới: 3 gate xanh không sửa tay, app chạy có
      trang Orders CRUD hoạt động end-to-end vào SQL Server.
      Verified on a `--bare` project: 46 files, 9 wiring sites, then `buf lint`, `buf generate`
      (diff clean), `go build`, `go vet`, `gofmt` and archlint — 61 packages, no violations — in
      about seven seconds, nothing edited by hand. The module's own domain and service tests pass.
      The running app and the round trip into SQL Server are unverified here for the same reason
      as Phases 1 and 2: no Windows, no .NET SDK, no SQL Server. `scaffold-smoke-client` builds the
      client half; nothing in CI runs the app against a database.
- [x] `remove module orders` trả working tree về sạch.
      `git diff` against the commit before the add is empty — including after `add page`, because
      the record's paths are the module's directories rather than the files one run wrote.
- [~] `add page` sinh page pass ArchitectureTests.
      Generated and registered in both marker sections; the gate itself runs on Windows only.
- [x] Mọi lệnh có `--dry-run` in kế hoạch chính xác.
      `add module`, `add page` and `remove module` each print what they would write or take back,
      and write nothing.
- [x] Fail giữa chừng rollback sạch.
      The transaction is unit-tested across create, edit and delete, including that a second edit
      of one file still rolls back to what it held before the first. It was also exercised for
      real twice during development: two defects failed the pipeline mid-way, and both times the
      project came back exactly as it was.
- [x] Drift test generator ⇄ notes chạy trong CI.
      Rendering the derived templates with the example's own names reproduces the example module
      byte for byte, all 46 files; a second test renders another name and refuses any trace of the
      example left in a path, a body or a wiring line. The `generate` job re-runs the derivation
      and fails on a diff.

### Điều phát sinh, không có trong spec

1. **The generated module is the example module, so it has the example's shape.** §1.1 sketches a
   minimal `{id, name, created_at, updated_at}` message, and §4 makes the example the source of
   truth. They disagree, and §4 wins: a generated `Order` carries Title and Body, and the first of
   the next steps the command prints is to edit the proto — which is what §1.4 step 6 asks for
   anyway. Deriving would otherwise have to stop at the file list and hand-write the contents,
   which is the drift the phase exists to prevent.
2. **The client half is three projects and four test projects, not the one of §1.3.** The template
   splits Domain, Application and UI, and the generator follows the template. That is why a module
   is 46 files: it includes its own layering test, which is what makes gate 2 pass for it without
   an exception.
3. **`--crud=false`, `--store mongo` and `--no-page` are refused, with the reason.** Each needs a
   second example to derive from — the Mongo one already exists in `activity`, and a second
   derivation is Phase 4's to take if it wants them. Accepting a flag and half-honouring it is
   worse than saying it is not built.
4. **`add page` is the one hand-written template set.** The example has no second page to derive
   from: its page *is* the module's page, tied to its gateway and its commands. Two marker sections
   were added to the example module's own composition entry point so a page can register itself
   there — `module-page-services` and `module-page-navigation` — which is what keeps `add page`
   client-only as §2 asks, since the client declares its own navigation items beside the server's
   destinations.
5. **A generated module gets a neutral icon, and the two icon vocabularies are still two.** The
   server declares a semantic name and the client is handed a glyph directly; a module derived from
   the example would otherwise ask for the example's icon in both. Both now default to the document
   icon. `--icon` sets the server's name; the client's glyph is a constant the developer edits.
6. **A marker is a line that *is* a marker.** The composition roots explain their own markers in
   prose, and matching one anywhere in a line reads that sentence as a second opening of the
   section — which is how the first run of `add module` failed. The idempotency check is per
   section rather than per file for the same reason: one composition root legitimately holds one
   module in two sections.
7. **Removal is atomic, so a project that no longer builds without the module is put back.** §3 asks
   for the failing sites to be printed; they are, and then the removal is rolled back rather than
   left half-done. Delete the references first, then remove.
8. **`.kakehashi/units/<id>.json` is written for generated modules only.** A module the template
   ships is removed through the unit file the template ships, and a removal does not delete that:
   it is not the removal's to delete.
