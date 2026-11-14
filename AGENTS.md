# Quy tắc làm việc cho ET4230 Đề 14

- Dự án C# chia sẻ tệp P2P qua TCP; trao đổi và tài liệu học bằng tiếng Việt.
- Nhóm được Linh xác nhận gồm Linh, Minh, Cường, Quân, Tú. Không suy ra MSSV, tài khoản, đóng góp hay việc đã nhận từ phân công đề xuất.
- Linh xác nhận dùng C# được phép ngày 23/10/2026; không hỏi lại việc chọn ngôn ngữ vì danh sách cũ chỉ nêu Python/C++/Java.
- Đọc `.github/agent/RECOVERY_NOTES.md` và `.github/agent/CURRENT_STATE.md` khi bắt đầu hoặc tiếp tục sau gián đoạn; dùng `.github/agent/CONTEXT_MANIFEST.md` để tìm tài liệu đúng việc. Không nạp toàn bộ lịch sử mặc định.
- Khi hướng dẫn học, giải thích input/output, trách nhiệm, reference, ownership và lỗi; cho thành viên tự thiết kế/viết implementation theo bước nhỏ, rồi review. Khi người dùng yêu cầu rõ viết code hoặc làm trọn tính năng, thực hiện theo yêu cầu hiện tại. Bộ khởi tạo và công cụ dự án có thể được dựng trong phạm vi đã giao.
- Linh tự phân công hàng tuần ai làm gì và cần đọc gì. Assistant xây học liệu có giải thích, ví dụ, bài tập/gợi ý và prerequisite theo chuyên môn; không tự gán cố định tên người với mảng hoặc deadline viết code khi nền chưa có. Học từ kiến thức và ví dụ trước rồi nâng mức thực hành theo Linh giao.
- Tổ chức tài liệu theo nhu cầu P2P, tra qua `docs/README.md`. Không sao chép mọi quy trình của môn Hệ điều hành; không tạo hồ sơ onboarding theo người hoặc mẫu tiến độ trùng với kế hoạch chỉ vì dự án kia có.
- Nguồn yêu cầu môn học nằm trong `docs/Tong-quan.md`; thiết kế v1 trong `docs/thiet-ke/Giao-thuc-v1.md` là đề xuất kỹ thuật nội bộ, không phải chuẩn BitTorrent hay yêu cầu của giảng viên.
- Phân biệt học thuyết, thiết kế, mã đã có, build, test và benchmark. Không tạo kết quả giả, không ghi thành viên đã học xong hoặc nhận task khi chưa xác nhận.
- Slide tham chiếu bằng tên file và số trang PDF 1-based; không nhầm số footer với trang PDF. Các chỗ giản lược cần đọc cùng `docs/hoc-tap/Ghi-chu-chuan-hoa-slide.md`.
- VstHelper ở `references/VstHelper/` là snapshot giữ nguyên byte, có hash và provenance. Không sửa snapshot khi sửa ứng dụng; adapter/biến thể mới phải nằm trong source riêng và được mô tả. Không stage repository VstHelper gốc ở môn khác.
- Không trả buffer còn đang được socket/I/O dùng; không coi hủy token là thao tác đã kết thúc; không giữ custom lock qua await hoặc I/O chặn. C# có GC không tự quản lý vùng nhớ unmanaged.
- Core không phụ thuộc implementation native Windows. Tích hợp Vst qua hợp đồng có ownership; benchmark managed và Vst trên cùng môi trường Windows và cùng chính sách I/O.
- Giữ cấu trúc ít project/module theo nhu cầu thật. Không thêm framework web, ORM, DI tổng quát hoặc DHT chỉ để làm kiến trúc lớn hơn.
- Thao tác Git của thành viên ưu tiên GitHub Desktop; assistant không stage/commit/push hay tạo repo GitHub khi chưa được giao rõ. Remote hiện hành là `https://github.com/kimsakota/BTL.MMT.git`, nhánh chính `main`. Linh đã giao commit/push bộ khởi tạo ngày 23/10/2026; yêu cầu này không mặc nhiên cho phép mọi lần push về sau.
- Sau mốc có ý nghĩa cập nhật bộ nhớ đúng bằng chứng theo `agent/DOCS_GUIDE.md`; mọi thành viên phải có đầu ra kỹ thuật và minh chứng riêng.
