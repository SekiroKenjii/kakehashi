# Phase 5 — Versioning, nền móng upgrade, docs & phân phối

**Mục tiêu:** hai dòng version độc lập (CLI / template) với ma trận tương thích rõ ràng,
nền móng cho `upgrade`, bộ docs viết cho *người dùng boilerplate*, và các kênh phân phối.

## 1. Versioning

### 1.1 Hai dòng tag trong một monorepo

| Dòng | Tag | Semver nghĩa là gì |
|---|---|---|
| Template | `template/vX.Y.Z` | MAJOR: đổi cấu trúc/marker/unit format (generator cũ không hiểu); MINOR: thêm khả năng (unit mới, marker mới); PATCH: sửa lỗi trong template |
| CLI | `cli/vX.Y.Z` | Semver chuẩn cho tool |

### 1.2 Ma trận tương thích

- File `templates/template.json` trong repo template:

```json
{ "templateVersion": "0.3.0", "requiresCli": ">=0.2 <0.4", "markersSchema": 1, "unitsSchema": 1 }
```

- CLI khai dải template hỗ trợ; `new`/`add` kiểm tra chéo hai chiều, lỗi thì in
  version cần nâng cấp bên nào.
- Release template = GitHub Release trên tag `template/*`, asset:
  `template-vX.Y.Z.tar.gz` + `checksums.txt` (CI build, đã strip `.github`
  của repo template và các file chỉ-thuộc-repo như README boilerplate, tools/cli).

### 1.3 Quy trình release

- CI job `release-template`: chạy khi push tag `template/*` — chạy full smoke
  (new + add + remove trên cả 2 OS) rồi mới đóng gói asset.
- CI job `release-cli`: goreleaser (hoặc script) build đa nền tảng + checksums
  + `go install` compatible.
- CHANGELOG tách hai file: `CHANGELOG.template.md`, `CHANGELOG.cli.md`
  (conventional commits với scope `template:` / `cli:` để tự sinh).

## 2. Nền móng `kakehashi upgrade` (chưa ship lệnh, chỉ chuẩn bị)

Những gì v1 phải làm để v2 khả thi:

1. `.kakehashi.json` ghi đủ: template version + inputs + units (đã có từ Phase 2).
2. `add module` ghi unit record (đã có từ Phase 3).
3. **Không bao giờ** đổi format marker/unit mà không tăng `markersSchema`/`unitsSchema`.
4. Thiết kế dự kiến v2 (ghi thành ADR, không implement): 3-way merge kiểu
   `nx migrate`/`cruft` — CLI scaffold lại project ảo ở version cũ + version mới
   với cùng inputs, diff hai bản, apply patch lên project thật, xung đột thì để
   conflict marker cho dev xử.

## 3. Bộ docs mới (viết cho người *dùng*, không chỉ người *đọc kiến trúc*)

Cấu trúc đề xuất cho repo template:

```
README.md               boilerplate pitch: 1 GIF/screenshot, 3 lệnh, link docs
docs/
  getting-started.md    5 phút đầu tiên (new → run → thấy Connected)
  first-module.md       add module orders, sửa proto, đổ logic — hướng dẫn từng bước
  remove-example.md     gỡ notes
  cli.md                reference đầy đủ các lệnh + flags
  architecture.md       (giữ, đã có) — vì sao shape như vậy
  contracts.md          (giữ) — luật thay đổi proto
  gates.md              (mới, gộp giải thích 3 gate + cách đọc lỗi từng gate)
  faq.md                Windows-only? đổi DB? deploy đâu? …
  adr/                  các quyết định D1–D5 + upgrade design
```

Chuẩn chất lượng: mỗi trang hướng-dẫn phải được một người chưa từng đụng repo
làm theo thành công (test bằng chính đồng nghiệp hoặc AI agent trên máy sạch).

## 4. Kênh phân phối

| Kênh | Việc cần làm | Ưu tiên |
|---|---|---|
| `go install …/tools/cli/cmd/kakehashi@latest` | có sẵn từ Phase 2 | P0 |
| GitHub Releases (binaries + checksums) | goreleaser | P0 |
| GitHub "Use this template" button | bật setting; README chỉ rõ: sau khi dùng button, chạy `tools/rename/rename.ps1` | P0 |
| winget | manifest `SekiroKenjii.Kakehashi` | P1 |
| scoop | bucket riêng hoặc PR extras | P2 |

## 5. Launch checklist

- [ ] Full pipeline test trên máy Windows sạch (VM mới): doctor → new → run →
      add module → gates → remove.
- [ ] Screenshot/GIF cho README (app Home + terminal wizard).
- [ ] Topics GitHub cập nhật: thêm `scaffolding`, `code-generator`, `cli`.
- [ ] Bản mô tả repo mới: nhấn "boilerplate + CLI", không phải "an app and its server".
- [ ] Bài giới thiệu (tùy chọn): dev.to / reddit r/csharp + r/golang — nhấn vào
      ba gate và `add module` end-to-end như điểm khác biệt.
- [ ] Issue templates: bug (CLI) / bug (template) / feature — buộc ghi version cả hai dòng.

## 6. Acceptance criteria Phase 5

- [ ] Tag `template/v0.x` và `cli/v0.x` đầu tiên phát hành, asset + checksums đầy đủ.
- [ ] CLI từ chối đúng khi lệch ma trận tương thích (test 2 chiều).
- [ ] Bộ docs mục 3 hoàn chỉnh; getting-started được người thứ hai verify.
- [ ] `go install` + release binary + template button đều hoạt động.
- [ ] ADR thiết kế upgrade v2 được viết và review.
