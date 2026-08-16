# Phase 4 — UI khởi đầu: TUI wizard + trải nghiệm app sinh ra

**Mục tiêu:** "vừa có CLI và UI để bắt đầu phát triển được ngay" — hiểu theo 2 tầng:
(1) UI của **quá trình scaffold** = TUI wizard; (2) UI **đầu tiên dev nhìn thấy** =
app sinh ra tự nó là điểm khởi đầu có hướng dẫn.

**Không làm** GUI installer WinUI riêng ở v1 (non-goal — chi phí cao, TUI đủ; xem lại khi có traction).

## 1. TUI Wizard (`kakehashi new` không tham số)

Stack: `charmbracelet/huh` (form) + `lipgloss` (style) — nhẹ hơn full bubbletea app,
đủ cho wizard tuyến tính. Fallback: terminal không hỗ trợ TTY (CI, pipe) → in lỗi
yêu cầu flags + `--no-input`.

### Flow

```
Bước 1  App name          [text]  validate PascalCase, gợi ý từ tên thư mục hiện tại
Bước 2  Display title     [text]  default: App name tách từ (OrderDesk → "Order Desk")
Bước 3  Go module path    [text]  default: github.com/<git user>/<lower(app)>
Bước 4  Ví dụ Notes?      [yes/no] default yes — kèm 1 dòng giải thích gỡ được bằng
                                   `kakehashi remove module notes`
Bước 5  Auth mode         [select] In-app (default) / System browser — 1 dòng giải thích
Bước 6  Accent color      [select] Vermilion (default) / Custom hex
Bước 7  Confirm           [summary] bảng tổng hợp + đường dẫn đích → Enter để chạy
```

Trong lúc chạy: progress từng bước pipeline (fetch → verify → apply → check → git),
mỗi bước ✓ khi xong. Kết thúc: block "Next steps" copy-paste được.

Nguyên tắc thiết kế TUI:

- Mọi câu hỏi có default hợp lý → Enter-Enter-Enter phải ra được project dùng được.
- Không hỏi những gì suy ra được (proto package, lower name, author từ git config).
- Màu TUI: dùng accent vermilion mặc định của brand template; **không** gradient/tô vẽ.

## 2. Trải nghiệm app sinh ra (first-run UX)

Đây là phần "UI để bắt đầu phát triển được ngay" quan trọng hơn wizard.

### 2.1 Home page (chế độ with-example)

- **Backend card**: trạng thái Connected/Disconnected (đã có), thêm địa chỉ endpoint
  + nút "Retry". Khi Disconnected: hiển thị đúng lệnh `docker compose up -d` để chạy.
- **Getting started card** (mới): checklist tương tác đọc trạng thái thật:
  - [ ] Backend connected (tự tick khi healthz OK)
  - [ ] Mở module Notes, tạo một note (tick khi count > 0)
  - [ ] Đọc `docs/ARCHITECTURE.md`
  - [ ] Thêm module đầu tiên: `kakehashi add module <id>` (copy button)
  - [ ] Gỡ example: `kakehashi remove module notes`
- **Gates card**: liệt kê 3 gate + lệnh chạy từng cái (copy button). Dev biết ngay
  "luật chơi" của codebase.

### 2.2 Home page (chế độ --bare)

Không Notes → Getting started card trở thành trung tâm: backend status +
lệnh `add module` + link docs. Trang trống nhưng **có chủ đích**, không phải trống trơn.

### 2.3 Ràng buộc visual

- Layout/styling Fluent chuẩn WinUI, theme resource của template.
- Accent = `__ACCENT__`; mọi thành phần Getting started phải theo token,
  không hardcode màu.
- Tuyệt đối tránh visual "AI-alike": không purple/indigo gradient, không glassmorphism,
  không emoji rải trong UI. Giữ chất Fluent + vermilion trung tính hiện có.

### 2.4 Nội dung đi kèm trong project sinh ra

- `README.md` (scaffold): "5 phút đầu tiên" → run → "Thêm module đầu tiên" →
  "Ba gate là gì" → "Gỡ example".
- `CLAUDE.md` (scaffold): mô tả kiến trúc + lệnh gate + convention marker —
  project sinh ra sẵn sàng cho AI agent làm việc đúng luật.
- `docs/` giữ ARCHITECTURE/CONTRACTS (đã placeholder hóa), bỏ các docs thuộc showcase.

## 3. Phối hợp với Phase 3

Getting started card gọi được trạng thái module (Notes tồn tại hay không) →
đọc từ `.kakehashi/units/` hoặc DI (module registration tự khai báo). Chọn cách
**DI-based** (mỗi module đăng ký metadata vào registry) để card không phụ thuộc
file hệ thống lúc runtime.

## 4. Acceptance criteria Phase 4

- [ ] `kakehashi new` không tham số → wizard hoàn chỉnh; Enter với default ra project
      build xanh.
- [ ] Terminal không TTY → thông báo hướng dẫn flags, exit code ≠ 0.
- [ ] App with-example: checklist tự tick 2 mục đầu theo trạng thái thật.
- [ ] App --bare: home page có hướng dẫn add module, không màn hình trống vô nghĩa.
- [ ] Không vi phạm ràng buộc visual (review checklist riêng).
- [ ] README + CLAUDE.md scaffold đầy đủ, một dev lạ làm theo được không cần hỏi.
