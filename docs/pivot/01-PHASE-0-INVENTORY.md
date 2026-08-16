# Phase 0 — Inventory: tách "app" ra khỏi "khung"

**Mục tiêu:** phân loại 100% file trong repo vào đúng 1 trong 3 nhóm, ghi thành
`docs/BOILERPLATE.md`. Tài liệu đó là input bắt buộc cho mọi phase sau.

**Thời lượng:** ~1 tuần. **Không viết code mới trong phase này** (trừ script kiểm kê).

## 1. Ba nhóm phân loại

### Nhóm A — CORE (khung, giữ nguyên trong template)

Định nghĩa: file mà **mọi** project sinh ra đều cần, bất kể domain.

Dự kiến thuộc nhóm này:

```
server/cmd/server/                    composition root (sẽ có marker cho generator)
server/internal/app/                  kernel: Module contract, registry, mux
server/internal/platform/             config, logging, SQL Server, Mongo, event bus
server/tools/archlint/                gate 1
client/  (App shell, DI, mediator, navigation frame, theming infra)
client/  Kakehashi.ArchitectureTests  gate 2
proto/   buf.yaml, buf.gen.yaml       gate 3 config
.github/workflows/                    CI
docker-compose.yml, .editorconfig, .gitattributes, .gitignore
CONTRIBUTING.md (sườn), LICENSE
```

### Nhóm B — EXAMPLE (mẫu, đánh dấu để gỡ được)

Định nghĩa: code minh họa cách dùng khung. Phải gỡ được **sạch** bằng cách
xóa các file/thư mục được đánh dấu + gỡ các dòng wiring có marker.

Dự kiến:

```
server/internal/modules/notes/        module mẫu server (giữ)
client/ ...Notes...                   module mẫu client (giữ)
proto/kakehashi/notes/v1/             contract mẫu (giữ)
server/internal/modules/activity/     → QUYẾT ĐỊNH: gỡ hẳn hoặc gộp làm example thứ 2
client/ ...Activity/Feed...           → cùng quyết định với trên
RBAC (docs/RBAC.md + code liên quan)  → tách: cơ chế authz thuộc CORE,
                                        policy/role cụ thể thuộc EXAMPLE
DB-driven navigation                  → tách: NavigationService thuộc CORE,
                                        Descriptor/Reconcile + bảng nav thuộc EXAMPLE
                                        (hoặc feature flag — xem mục 4)
docs/ACTIVITY.md, docs/NAVIGATION.md  theo số phận code tương ứng
```

**Khuyến nghị:** template v1 chỉ ship **một** example module là `notes` —
nó đủ demo cả 4 layer server + rpc + proto + client 3-layer + mediator + một page CRUD.
Activity/RBAC/DB-nav chuyển sang một branch `showcase` hoặc repo `kakehashi-examples`
để không mất công sức đã bỏ ra.

### Nhóm C — IDENTITY (danh tính app, biến thành placeholder)

Định nghĩa: mọi thứ mang tên/màu/hình của "Kakehashi" như một sản phẩm.

```
Chuỗi "Kakehashi", "kakehashi"        → __APP_NAME__ / __APP_NAME_LOWER__
Namespace C# Kakehashi.*              → __ROOT_NAMESPACE__.*
Go module github.com/SekiroKenjii/... → __GO_MODULE__
proto package kakehashi.*             → __PROTO_PACKAGE__.*
docs/brand/ (banner, mark, palette)   → gỡ khỏi template; accent vermilion
                                        trở thành giá trị mặc định của __ACCENT__
Splash/sign-in artwork                → thay bằng placeholder trung tính
Tên "架け橋" trong README             → chỉ giữ ở README của repo template,
                                        không xuất hiện trong project sinh ra
```

## 2. Phương pháp kiểm kê

1. **Quét tự động trước, duyệt tay sau.** Viết script (`tools/inventory/`) grep các
   pattern: `Kakehashi`, `kakehashi`, `SekiroKenjii`, `架け橋`, mã màu vermilion,
   đường dẫn `notes|activity`. Xuất CSV: `path, match, line, suggested_group`.
2. Duyệt tay từng file chưa match — mọi file phải có nhóm, kể cả asset và config.
3. Với file "lai" (vừa core vừa example — điển hình: composition root, DI registration,
   nav registry): **không tách file**, mà đánh dấu vùng bằng marker comment
   (chuẩn hóa marker ngay từ phase này vì Phase 3 generator sẽ dùng lại):

```go
// kakehashi:module-imports:begin
notesapi "…/internal/modules/notes/api"
// kakehashi:module-imports:end

// kakehashi:module-registrations:begin
app.Register(notes.New(deps))
// kakehashi:module-registrations:end
```

```csharp
// kakehashi:module-registrations:begin
services.AddNotesModule();
// kakehashi:module-registrations:end
```

4. Xác minh: sau khi phân loại, thử **xóa toàn bộ nhóm B** trên một branch nháp +
   gỡ nội dung giữa các marker → build phải xanh, 3 gate phải chạy (bare skeleton).
   Nếu không xanh → phân loại sai, sửa lại bản đồ.

## 3. Định dạng deliverable `docs/BOILERPLATE.md`

```markdown
# Boilerplate Map

## Legend
- CORE / EXAMPLE / IDENTITY
- (M) = file lai, có marker vùng

## Map
| Path | Group | Notes |
|---|---|---|
| server/internal/app/ | CORE | |
| server/cmd/server/main.go | CORE (M) | markers: module-imports, module-registrations |
| server/internal/modules/notes/ | EXAMPLE | removable unit "notes" |
| proto/kakehashi/notes/v1/ | EXAMPLE | removable unit "notes" |
| docs/brand/ | IDENTITY | drop from template |
| ... | ... | ... |

## Removable units
- notes: [danh sách đầy đủ path + marker cần gỡ]
```

Khái niệm **removable unit** quan trọng: một example module = một unit có danh sách
path + marker khép kín. `kakehashi new --bare` (Phase 2) đơn giản là áp dụng
danh sách gỡ của unit `notes`.

## 4. Các quyết định phải chốt trong phase này

| # | Câu hỏi | Phương án khuyến nghị |
|---|---|---|
| D1 | Activity + RBAC UI + DB-nav đi đâu? | Branch `showcase` / repo `kakehashi-examples`; template v1 chỉ giữ `notes` |
| D2 | OIDC self-host là CORE hay optional? | CORE — là điểm khác biệt của stack; nhưng seed user/role mẫu là EXAMPLE |
| D3 | DB-driven navigation? | Static nav là CORE default; Descriptor/Reconcile giữ ở showcase, cân nhắc thành flag `--nav db` ở v2 |
| D4 | CLI sống trong monorepo hay repo riêng? | `tools/cli/` trong monorepo (chia sẻ CI, versioning tách bằng tag prefix — xem 06) |
| D5 | Example thứ hai minh họa event bus + Mongo? | v1: không. Notes có thể thêm 1 event nhỏ để chạm event bus là đủ |

Ghi các quyết định vào `docs/adr/` (mỗi quyết định một ADR ngắn).

## 5. Acceptance criteria Phase 0

- [x] `docs/BOILERPLATE.md` phủ 100% file trong repo (script kiểm chứng: mọi path
      trong git ls-files đều có dòng tương ứng hoặc thuộc thư mục đã phân loại).
      `cd tools/inventory && go run . -coverage` — xanh, và fail cả hai chiều.
- [x] Mọi file lai đã có marker chuẩn `kakehashi:<section>:begin/end`.
      Sáu file, năm section — bảng trong `docs/BOILERPLATE.md`.
- [~] Branch nháp "bare" build xanh + 3 gate chạy được.
      Gate 1 (`archlint`) và gate 3 (`buf lint`) xanh, cùng build/vet/test/gofmt.
      Gate 2 (`Kakehashi.ArchitectureTests`) **chưa chạy** — cần Windows + .NET SDK.
- [x] 5 quyết định D1–D5 có ADR: `docs/adr/0016`–`0020`. D3 ghi rõ là **lệch**
      khuyến nghị, kèm lý do (import graph).
- [x] Danh sách removable unit `notes` đầy đủ và đã kiểm chứng bằng branch nháp.
      Phần dư sau khi gỡ (không làm hỏng gate nào) liệt kê ở mục Residue.
