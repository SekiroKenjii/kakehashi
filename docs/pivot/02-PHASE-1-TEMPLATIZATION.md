# Phase 1 — Template hóa

**Mục tiêu:** repo `kakehashi` trở thành template có placeholder; một script rename
thô chứng minh được: *rename toàn repo → build xanh → 3 gate xanh*. CI bảo vệ
điều đó mãi mãi bằng smoke job.

**Đầu vào:** `docs/BOILERPLATE.md` (Phase 0). **Thời lượng:** 1–2 tuần.

## 1. Quy ước placeholder

| Placeholder | Ý nghĩa | Ví dụ giá trị | Ràng buộc |
|---|---|---|---|
| `__APP_NAME__` | Tên PascalCase | `OrderDesk` | `^[A-Z][A-Za-z0-9]{1,39}$` |
| `__APP_NAME_LOWER__` | lowercase | `orderdesk` | dẫn xuất từ trên |
| `__APP_TITLE__` | Tên hiển thị (window title, MSIX) | `Order Desk` | tự do, mặc định = APP_NAME |
| `__ROOT_NAMESPACE__` | Namespace gốc C# | `OrderDesk` | mặc định = APP_NAME |
| `__GO_MODULE__` | Go module path | `github.com/me/orderdesk` | regex module path hợp lệ |
| `__PROTO_PACKAGE__` | Gốc proto package | `orderdesk` | `^[a-z][a-z0-9_]*$` |
| `__ACCENT__` | Màu accent hex | `#E34234` | mặc định vermilion |
| `__AUTHOR__` | Tên tác giả (LICENSE, csproj) | `Thuong Nguyen` | tự do |

Nguyên tắc:

- Dạng `__X__` được chọn vì hợp lệ trong **mọi** loại file của repo (C#, Go, XAML,
  proto, YAML, JSON, MD, ps1) — không đụng syntax nào.
- Thay thế **thuần văn bản** (literal), không dùng template engine có logic
  (không if/loop trong file nguồn) → repo template vẫn gần-như-build-được và dễ đọc.
  Logic điều kiện (bare mode, auth mode) xử lý bằng **danh sách xóa file + marker**,
  không bằng cú pháp template trong file.
- Placeholder xuất hiện cả trong **nội dung file** lẫn **tên file/thư mục**
  (`Kakehashi.App.csproj` → `__APP_NAME__.App.csproj`).

## 2. Bản đồ thay thế theo công nghệ

### 2.1 C# / WinUI (phần khó nhất)

| Vị trí | Việc cần làm |
|---|---|
| Tên thư mục `client/src/**/Kakehashi.*` | rename dir |
| `*.csproj` | rename file; nội dung: `RootNamespace`, `AssemblyName`, `PackageId` → placeholder |
| `Kakehashi.slnx` | rename file + mọi project path bên trong |
| `namespace Kakehashi...` mọi file .cs | thay chuỗi |
| XAML: `x:Class="Kakehashi..."`, `xmlns:local="using:Kakehashi..."` | thay chuỗi — **phải khớp 1:1 với namespace .cs**, lệch là lỗi XamlCompiler khó đọc |
| `App.xaml` / theming resource keys | chỉ đổi nếu key chứa tên app; khuyến nghị đổi key sang tên trung tính ngay trong phase này để giảm diện tích thay thế |
| `Package.appxmanifest` | `Identity Name`, `DisplayName`, `PublisherDisplayName`, logo assets → placeholder / asset trung tính |
| `appsettings.json` | `Auth:Authority`, tên app trong config |
| ArchitectureTests | các assertion theo namespace `Kakehashi.*` → dùng hằng số namespace gốc đọc từ một nơi duy nhất (ví dụ `TestConstants.RootNamespace`), file đó chứa placeholder |
| launchSettings, .vscode tasks | đường dẫn csproj |

**Bẫy đã biết:**
- XamlCompiler cache: sau rename phải xóa `obj/` trước khi build (script rename làm luôn).
- `.slnx`/`.csproj` phân biệt hoa thường trong path trên CI Linux (nếu có bước nào chạy Linux).
- Resource `.resw`/`Assets` có thể chứa tên app trong metadata.

### 2.2 Go server

| Vị trí | Việc cần làm |
|---|---|
| `go.mod` | `module __GO_MODULE__` |
| Mọi import nội bộ | `github.com/SekiroKenjii/kakehashi/...` → `__GO_MODULE__/...` |
| `archlint` config/rules | nếu rule chứa module path hoặc prefix `kakehashi` → placeholder; khuyến nghị refactor archlint đọc module path từ `go.mod` để **không cần** placeholder ở đây |
| Binary name trong Makefile/compose/docs | `kakehashi` → `__APP_NAME_LOWER__` |
| OIDC issuer/client-id mặc định | placeholder hoặc giá trị dẫn xuất từ APP_NAME_LOWER |

### 2.3 Proto & buf

| Vị trí | Việc cần làm |
|---|---|
| Thư mục `proto/kakehashi/` | → `proto/__PROTO_PACKAGE__/` |
| `package kakehashi.notes.v1;` | → `package __PROTO_PACKAGE__.notes.v1;` |
| `option go_package` | → `__GO_MODULE__/internal/gen/...` (khớp buf.gen.yaml) |
| `option csharp_namespace` | → `__ROOT_NAMESPACE__.Contracts...` |
| `buf.yaml` module name, breaking config | placeholder |
| `buf.gen.yaml` output paths / managed mode overrides | placeholder |

**Lưu ý breaking check:** `buf breaking --against '.git#branch=main'` trong project
sinh ra sẽ so với chính main của project đó — đúng ý muốn. Trong repo template,
CI smoke job phải chạy breaking check **sau rename** so với commit đầu của bản
đã rename (hoặc bỏ qua breaking ở smoke job, chỉ chạy `buf lint` + `buf generate`).

### 2.4 Khác

- `docker-compose.yml`: tên service/container/network/volume.
- `.github/workflows/`: tên workflow giữ trung tính; đường dẫn csproj/slnx dùng placeholder.
- `README.md`: viết **hai bản** — `README.md` của repo template (nói về boilerplate)
  và `templates/README.scaffold.md` (thành README của project sinh ra, chứa placeholder).
- `LICENSE`: năm + `__AUTHOR__`.
- `CLAUDE.md` / `.claude/`: cập nhật cho project sinh ra (mô tả kiến trúc, lệnh gate) —
  đây là điểm cộng lớn: project sinh ra "AI-agent-ready" sẵn.

## 3. Rename script (bằng chứng khả thi)

Vị trí: `tools/rename/rename.ps1` (chính, vì client cần Windows) + `rename.sh`
(server-only path cho CI Linux).

Spec:

```
./tools/rename/rename.ps1 `
  -AppName OrderDesk `
  -GoModule github.com/me/orderdesk `
  [-AppTitle "Order Desk"] [-ProtoPackage orderdesk] [-Accent "#E34234"] [-Author "Me"]
```

Thuật toán:

1. Validate tham số theo regex ở mục 1; suy ra giá trị dẫn xuất.
2. Đổi **nội dung**: quét mọi file text (theo `.gitattributes`), thay tất cả placeholder.
3. Đổi **tên file/dir**: duyệt sâu-nhất-trước (deepest-first) để rename dir không phá path con.
4. Dọn: xóa `**/obj`, `**/bin`, cache buf.
5. Tự kiểm: grep lại toàn repo — còn bất kỳ `__[A-Z_]+__` hoặc `Kakehashi|SekiroKenjii`
   nào → exit 1 kèm danh sách.
6. In các bước tiếp theo (docker compose up, dotnet run…).

Script này chính là "engine" mà CLI Phase 2 sẽ gọi lại dưới dạng thư viện Go —
viết logic sao cho port sang Go dễ (tránh tính năng PowerShell đặc thù).

## 4. CI smoke job (quan trọng nhất phase này)

Job mới `scaffold-smoke` trong workflow, chạy mỗi push + PR:

```yaml
jobs:
  scaffold-smoke-server:        # runner: ubuntu-latest
    steps:
      - checkout
      - run rename.sh -AppName SmokeApp -GoModule example.com/smokeapp
      - buf lint && buf generate
      - cd server && go build ./... && go vet ./...
      - go run ./tools/archlint          # gate 1
      - go test ./...
  scaffold-smoke-client:        # runner: windows-latest
    steps:
      - checkout
      - run rename.ps1 -AppName SmokeApp -GoModule example.com/smokeapp
      - dotnet build client/SmokeApp.slnx -p:Platform=x64
      - dotnet test  (ArchitectureTests) # gate 2
```

Nguyên tắc: **template không bao giờ được merge nếu bản rename không build.**
Đây là hợp đồng sống của cả dự án boilerplate.

## 5. Thứ tự thực hiện

1. Refactor giảm diện tích thay thế trước (archlint đọc go.mod; ArchitectureTests
   dùng `TestConstants.RootNamespace`; resource key trung tính).
2. Đưa placeholder vào theo bản đồ mục 2, đi từng khối: proto → server → client → misc.
   Mỗi khối một PR, PR nào cũng phải giữ smoke job xanh (bootstrap: viết smoke job
   trước với rename script rỗng, siết dần).
3. Viết rename.ps1/.sh song song với việc đưa placeholder.
4. Hai bản README + scaffold README.
5. Gắn nhãn removable unit `notes` bằng manifest máy-đọc-được:
   `templates/units/notes.json` (danh sách path + marker) — Phase 2/3 dùng file này.

## 6. Acceptance criteria Phase 1

- [x] Không còn identity hardcode ngoài placeholder (grep gate trong CI).
      Self-check của `rename.sh` chạy trong `scaffold-smoke`, exit non-zero nếu còn sót.
      Hai ngoại lệ duy nhất, cố ý: marker `kakehashi:<section>:begin` và `.kakehashi.json`
      — đó là namespace của generator, không phải của app.
- [~] `rename.ps1` trên máy Windows sạch: rename → build client + server → 3 gate xanh.
      `rename.sh` đã chạy thật: rename → buf lint + buf generate (diff sạch) + go
      build/vet/test + archlint (61 packages) + comment checks — **tất cả xanh**.
      `rename.ps1` **chưa chạy** (không có Windows/.NET ở đây); client được kiểm bằng
      structural check (17 x:Class, 59 project reference, 34 project — nhất quán).
- [x] CI có `scaffold-smoke-server` (Linux) và `scaffold-smoke-client` (Windows), chạy mỗi push.
      Thêm `scaffold-smoke-bare` (Linux) cho bare mode. File `.github/workflows/scaffold-smoke.yml`
      bị chính rename xóa, nên project sinh ra chỉ còn `ci.yml`.
- [x] `templates/units/notes.json` tồn tại; xóa theo nó + rename → vẫn build xanh.
      Đã chạy thật: gỡ unit (9 path, 6 file unwire) → rename → buf lint + generate + go
      build/vet/test + archlint (53 packages) xanh; client structure 27 project nhất quán.
- [x] README template / README scaffold tách đôi.
      `README.md` nói về boilerplate; `templates/README.scaffold.md` thành README của project.

### Điều phát sinh, không có trong spec

1. **`buf lint` không thể chạy trên template.** `PACKAGE_LOWER_SNAKE_CASE` từ chối mọi tên
   package có hai dấu gạch dưới, nên không có cách viết `__X__` nào qua được. Đã kiểm 5 biến
   thể. Giải: `ci.yml` bỏ qua bước này khi thư mục placeholder còn tồn tại, và
   `scaffold-smoke` lint cây đã rename. Gate 3 vẫn chạy mỗi push.
2. **Go bỏ qua thư mục bắt đầu bằng `_` khi mở rộng `./...`**, nên trong template archlint
   thấy 49 package thay vì 61. 12 package thiếu đều là generated; rule 6 vẫn chặn chúng ở
   phía import. Sau rename: đủ 61.
3. **Rename không thể chỉ thay chữ trong generated code.** protoc suy ra tên symbol Go và các
   option namespace từ proto package, và gộp dấu gạch dưới trên đường đi: bản thay chữ ra
   `file__smokeapp_…`, bản generate ra `file_smokeapp_…`. `rename` giờ chạy `buf generate`.
4. **Thêm 2 placeholder ngoài bảng §1**: `__APP_NAME_UPPER__` (tiền tố biến môi trường) và
   `__YEAR__` (LICENSE).
5. **`option csharp_namespace` giờ được khai báo** thay vì để protoc suy ra — nếu để suy ra,
   namespace C# sẽ bám theo proto package placeholder và alias bên client không gọi tên được.
