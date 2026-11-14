# Điểm tiếp tục

## Sửa đường dẫn kiểm tra bootstrap — 08/11/2026

Linh yêu cầu khắc phục workflow thất bại sau merge PR#15. Log run42453787595 trên main0563fba cho thấy checker dừng với39lỗi vì memory đã chuyển sang `.github/agent/` nhưng required paths và relative links còn vị trí cũ. Đã cập nhật checker, README/mục lục, AGENTS và các link đi từ memory ra root theo vị trí thực.

Kiểm tra local trên Windows/.NET SDK10.0.301/Python3.13.13: `python tools/check_project.py` đạt0lỗi, kiểm đúng21file snapshot và2vector. `dotnet build ET4230.P2P.sln --configuration Release --nologo` đạt0error,10warningCS8500 thuộc VstReference. CLI Release `--help` và `--version` đều exit0. Phạm vi là cấu trúc/config/hash và bootstrap, chưa phải test P2P hoặc nghiệm thu test project chung của#13. Bản sửa do assistant thực hiện theo yêu cầu hiện tại của Linh; kết quả CI trên GitHub cần đọc tại PR/run tương ứng.


Dự án Đề 14 C# cho Linh, Minh, Cường, Quân, Tú. Bộ khởi tạo và tài liệu đã chuẩn bị; chưa viết chức năng mạng. Đọc [CURRENT_STATE](CURRENT_STATE.md) để phân biệt evidence với thiết kế.

Linh đã yêu cầu sắp xếp lại docs cho nhu cầu P2P và bỏ việc rập khuôn môn Hệ điều hành. Điểm vào là [docs/README](../../docs/README.md); kế hoạch/phân công dùng chung [Ke-hoach](../../docs/Ke-hoach.md). Không tạo lại hồ sơ onboarding theo tên hoặc mẫu báo cáo tiến độ trùng task.

Repo đã xuất bản tại [kimsakota/BTL.MMT](https://github.com/kimsakota/BTL.MMT), main theo dõi origin/main. Commit đầu `4c072d6` có GitHub CI bootstrap thành công. Thành viên clone bằng GitHub Desktop theo CONTRIBUTING; chưa xác nhận ai đã được cấp quyền. Lệnh Git đã được Linh giao cho đợt xuất bản đầu, không tự suy ra quyền push cho công việc tương lai.

Bước tiếp theo theo điều chỉnh mới nhất: BTL có 8 tuần, đang tuần 1; **Linh tự giao ai học/làm gì và đọc gì mỗi tuần**. Mở [học liệu](../../docs/hoc-tap/README.md), [nền C#](../../docs/hoc-tap/CSharp-nen-tang.md) và [đường chuyên môn](../../docs/hoc-tap/Lo-trinh-theo-chuyen-mon.md). Khi Linh chọn một mục, dạy cơ chế bằng ví dụ, hỏi/kiểm tra cách hiểu rồi tăng mức bài tập; không nhảy thẳng từ tên task sang code. Đã bỏ bảng gán sẵn người/tuần trong kế hoạch. Chưa xác nhận trình độ, nhận việc hoặc hoàn thành của ai.

Thông tin mới: tuần 1 chỉ còn T7–CN, phải chừa thời gian môn khác. Linh đã nhờ đề xuất việc cho cả 5; bảng cuối tuần trong Kế hoạch là gợi ý một lần để Linh chốt, không tự giao cố định tương lai. Khối lượng đề xuất 150 phút/người tính cả học chung/bài nhỏ/review; có bản 90 phút. Chưa có cam kết số giờ thật hoặc evidence hoàn thành. Ưu tiên dạy/giải ví dụ, chưa ép xong codec/tracker/P2P.

Linh đã xác nhận C# được phép; không hỏi lại chọn ngôn ngữ. VstReference build có CS8500, adapter chưa tích hợp. Snapshot được giữ nguyên; học/audit API trước khi chạy unmanaged. Deadline/mã nhóm/họ tên/MSSV/username chưa có; không cản trở học và thiết kế đang được giao.
