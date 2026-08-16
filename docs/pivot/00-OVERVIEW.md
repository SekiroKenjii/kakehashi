# Kakehashi Boilerplate Pivot — Tổng quan

> Bộ tài liệu này định nghĩa kế hoạch chuyển Kakehashi từ một dự án "app độc lập"
> thành một **boilerplate thực thụ**: có CLI để scaffold, có generator để thêm module,
> và app sinh ra chạy được ngay lập tức.

## 1. Bối cảnh & vấn đề

Kakehashi hiện tại (`github.com/SekiroKenjii/kakehashi`) là một monorepo:

- WinUI 3 client (.NET 10, CommunityToolkit.Mvvm) — modular monolith 3 layer.
- Go 1.26 server — modular monolith, connect-go, một static binary.
- `proto/` — contract duy nhất giữa hai đầu, buf lint + breaking check.
- **Ba gate**: `archlint` (server), `Kakehashi.ArchitectureTests` (client),
  `buf breaking` (contract). Chạy trên mọi push.

Vấn đề: trong quá trình phát triển, repo đã tích lũy nhiều thứ thuộc về
*một sản phẩm cụ thể* chứ không phải một *khung khởi đầu*:

- Module Notes, Activity feed, RBAC, DB-driven navigation.
- OIDC provider tự host, splash/sign-in styling riêng.
- Brand identity (vermilion, banner, tên "Kakehashi" hardcode khắp nơi).

Người muốn dùng Kakehashi làm điểm khởi đầu hiện phải **tự gỡ** những thứ này ra —
đó là dấu hiệu của một app mẫu, không phải boilerplate.

## 2. Tầm nhìn (Vision)

> **Một lệnh, một app hai đầu đã nối dây, build xanh, chạy được ngay.**

```
kakehashi new myapp --module github.com/me/myapp
cd myapp
docker compose up -d
dotnet run --project client/src/App/MyApp.App -p:Platform=x64
# → Home page hiện "Backend: Connected"
```

Và sau đó:

```
kakehashi add module orders
# → server module + client module + proto + wiring, cả 3 gate vẫn xanh
```

## 3. Giá trị cốt lõi cần bảo toàn

Thứ khiến Kakehashi khác mọi template khác **không phải** là stack, mà là:

1. **Ba gate kiến trúc** — modular monolith chỉ modular khi có thứ kiểm tra.
2. **Contract-first hai đầu** — proto là nguồn sự thật duy nhất giữa client/server.
3. **Cấu trúc module chuẩn hóa** — `api/domain/store/service/rpc` (server),
   3-layer + mediator (client).

Mọi quyết định trong plan này phải trả lời được câu hỏi:
*"Điều này có làm suy yếu ba gate không?"* Nếu có → không làm.

## 4. Người dùng mục tiêu

| Persona | Nhu cầu | Tính năng phục vụ |
|---|---|---|
| Dev Windows muốn app desktop có backend | Khởi đầu nhanh, không phải thiết kế kiến trúc | `kakehashi new` + example module |
| Team đã có product, muốn kỷ luật kiến trúc | Gate + cấu trúc, không cần example | `kakehashi new --bare` |
| Dev đang dùng Kakehashi, cần thêm tính năng | Thêm module không phải copy-paste tay | `kakehashi add module` |
| Người không thích CLI | Bắt đầu từ GitHub | Template repo button + rename script |

## 5. Kết quả sau cùng (End state)

- **Repo `kakehashi`**: template thuần, mọi identity là placeholder,
  example module đánh dấu rõ, CI có job scaffold-smoke-test.
- **Repo/thư mục `kakehashi-cli`** (hoặc `tools/cli` trong monorepo):
  binary Go duy nhất, các lệnh `new / add / remove / doctor / upgrade(v2)`.
- **TUI wizard** khi chạy `kakehashi new` không tham số.
- **App sinh ra** có trang Getting Started in-app, backend card Connected,
  một module mẫu end-to-end (tùy chọn).

## 6. Non-goals (phiên bản này KHÔNG làm)

- GUI installer/wizard riêng bằng WinUI — chi phí cao, TUI đủ dùng. Xem lại khi có traction.
- Multi-platform client (GTK/macOS) — giữ đúng scope Windows + Linux server.
- Hỗ trợ DB khác ngoài SQL Server + MongoDB — kiến trúc cho phép, nhưng không ship trong v1.
- `kakehashi upgrade` hoàn chỉnh — chỉ đặt nền móng (manifest file), làm thật ở v2.
- Plugin system cho CLI.

## 7. Lộ trình 6 phase

| Phase | Tên | Thời lượng ước tính | Output chính |
|---|---|---|---|
| 0 | Inventory & tách app khỏi khung | 1 tuần | `docs/BOILERPLATE.md` — bản đồ phân loại file |
| 1 | Template hóa | 1–2 tuần | Placeholder + rename script + CI smoke job |
| 2 | CLI MVP | 2–3 tuần | `kakehashi new`, `kakehashi doctor` |
| 3 | Generators | 2–3 tuần | `kakehashi add module / add page / remove module` |
| 4 | UI khởi đầu | 1–2 tuần | TUI wizard + Getting Started page in-app |
| 5 | Versioning & phát hành | 1 tuần | Tagging, docs mới, kênh phân phối |

Phụ thuộc: 0 → 1 → 2 → 3; Phase 4 và 5 có thể chạy song song sau Phase 2.

**Đường tắt nếu thiếu thời gian:** Phase 1 + CI smoke job là xương sống bắt buộc.
Phase 3 là differentiator. TUI wizard làm cuối.

## 8. Tiêu chí thành công (Definition of Done toàn dự án)

1. `kakehashi new demo` trên máy sạch (chỉ có prerequisites) → build xanh cả hai đầu
   trong < 10 phút.
2. `kakehashi add module foo` → cả 3 gate xanh **ngay lập tức**, không sửa tay.
3. Không còn chuỗi `Kakehashi` / `SekiroKenjii` nào trong project sinh ra
   (trừ file manifest `.kakehashi.json` ghi nguồn template).
4. CI của repo template có job scaffold + rename + build chạy mỗi push.
5. README mới có mục "5 phút đầu tiên" mà một dev lạ làm theo được không cần hỏi.

## 9. Danh mục tài liệu

| File | Nội dung |
|---|---|
| `00-OVERVIEW.md` | Tài liệu này |
| `01-PHASE-0-INVENTORY.md` | Phân loại core / example / identity |
| `02-PHASE-1-TEMPLATIZATION.md` | Placeholder, rename, CI smoke test |
| `03-PHASE-2-CLI.md` | Spec CLI `kakehashi new` / `doctor` |
| `04-PHASE-3-GENERATORS.md` | Spec `add module` / `add page` / `remove module` |
| `05-PHASE-4-UI.md` | TUI wizard + trải nghiệm app khởi đầu |
| `06-PHASE-5-RELEASE.md` | Versioning, upgrade foundation, phân phối |
| `07-AGENT-PROMPT.md` | Prompt khởi đầu để giao việc cho AI agent |

## 10. Thuật ngữ

- **Template repo**: repo `kakehashi` sau khi template hóa — chứa placeholder.
- **Scaffolded project**: project mà `kakehashi new` sinh ra.
- **Gate**: một trong ba kiểm tra kiến trúc bắt buộc.
- **Placeholder**: chuỗi dạng `__NAME__` được thay thế lúc scaffold.
- **Manifest**: file `.kakehashi.json` trong project sinh ra, ghi template version
  và các lựa chọn lúc scaffold.
- **Bare mode**: scaffold không kèm example module.
