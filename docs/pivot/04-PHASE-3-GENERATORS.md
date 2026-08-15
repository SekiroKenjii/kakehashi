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

- [ ] `add module orders` trên project mới: 3 gate xanh không sửa tay, app chạy có
      trang Orders CRUD hoạt động end-to-end vào SQL Server.
- [ ] `remove module orders` trả working tree về sạch (git diff rỗng so với trước add,
      ngoại trừ `.kakehashi/units/` đã xóa record).
- [ ] `add page` sinh page pass ArchitectureTests.
- [ ] Mọi lệnh có `--dry-run` in kế hoạch chính xác.
- [ ] Fail giữa chừng rollback sạch (test giả lập lỗi ở từng bước pipeline).
- [ ] Drift test generator ⇄ notes chạy trong CI.
