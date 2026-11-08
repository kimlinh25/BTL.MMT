# Trạng thái hiện tại ET4230

## Sửa đường dẫn kiểm tra bootstrap — 08/11/2026

Linh yêu cầu khắc phục workflow thất bại sau merge PR#15. Log run42453787595 trên main0563fba cho thấy checker dừng với39lỗi vì memory đã chuyển sang `.github/agent/` nhưng required paths và relative links còn vị trí cũ. Đã cập nhật checker, README/mục lục, AGENTS và các link đi từ memory ra root theo vị trí thực.

Kiểm tra local trên Windows/.NET SDK10.0.301/Python3.13.13: `python tools/check_project.py` đạt0lỗi, kiểm đúng21file snapshot và2vector. `dotnet build ET4230.P2P.sln --configuration Release --nologo` đạt0error,10warningCS8500 thuộc VstReference. CLI Release `--help` và `--version` đều exit0. Phạm vi là cấu trúc/config/hash và bootstrap, chưa phải test P2P hoặc nghiệm thu test project chung của#13. Bản sửa do assistant thực hiện theo yêu cầu hiện tại của Linh; kết quả CI trên GitHub cần đọc tại PR/run tương ứng.


Cập nhật 23/10/2026. Đây là bộ khởi tạo dự án Đề 14; chưa có application P2P.

Kiểm tra kết nối Git ngày 23/10/2026: origin vẫn đúng, `git ls-remote origin refs/heads/main` trả về cùng SHA với HEAD `a570e602e04564a06f55be13ae0851220236f6e0`. Visual Studio đang mở đúng thư mục nhưng chưa kích hoạt repository; đã chọn BTL (Mạng máy tính) qua Git > Local Repositories. UI xác nhận đúng đường dẫn, nhánh main, 0 outgoing / 0 incoming và 40 thay đổi local tại thời điểm kiểm tra. Không stage/commit/push; học liệu mới vẫn chưa được xuất bản. Khi tiếp tục, kiểm tra vị trí memory thực tế: các file hiện nằm trong `.github/agent/`, chưa sửa lại các tham chiếu cũ trong đợt chẩn đoán Git này.

## Đã xác nhận

- Linh yêu cầu đọc slide và dựng dự án cặn kẽ A–Z, tổ chức riêng cho P2P. Điều chỉnh mới nhất: học liệu phải dạy từ nền với giải thích/ví dụ/bài tập; Linh tự phân công hàng tuần ai làm gì và đọc gì. Không tự gán tên vào mảng hoặc ép code khi nền chưa đủ.
- Nhóm gồm Linh, Minh, Cường, Quân, Tú. Chưa có đầy đủ họ tên/MSSV/username hoặc việc nhận nhiệm vụ.
- Linh xác nhận C# được phép dùng. Chọn C#/.NET 10; không hỏi lại do danh sách ngôn ngữ ban đầu của DOCX.
- Linh xác nhận ngày 23/10/2026: tổng thời gian BTL là 8 tuần làm việc, tuần hiện tại là tuần 1. [Kế hoạch](../../docs/Ke-hoach.md) giữ khung mốc tham khảo; Linh tự điều chỉnh lịch học/việc theo nền và quỹ giờ. Đã bỏ bảng gán sẵn người/tuần và owner mặc định trong backlog. Chưa có ngày theo lịch lớp hoặc quỹ giờ từng người; không tự suy ra ngày nộp.
- Đã lập chỉ mục 14 PDF, 697 trang; có hash, bản đồ kiến thức và ghi chú chuẩn hóa; đã mở 6 trang render trọng yếu.
- Đã soạn yêu cầu, kiến trúc/giao thức v1, audit Vst, kế hoạch test/thí nghiệm, hướng dẫn bàn giao, workspace và bộ nhớ. Đã viết lại 12 bài giảng có giải thích, ví dụ, bài tập tăng mức/gợi ý; thêm [nền C#](../../docs/hoc-tap/CSharp-nen-tang.md) và [lộ trình chuyên môn](../../docs/hoc-tap/Lo-trinh-theo-chuyen-mon.md). Chưa có xác nhận ai đã học xong.
- Docs từng gộp từ 42 xuống 30 file; hiện có 32 sau khi thêm 2 học liệu có chức năng riêng. Tra qua [mục lục](../../docs/README.md). Học liệu có nền chung và đường đọc A–E không gán tên; không có hồ sơ onboarding hoặc mẫu tiến độ theo người.
- Snapshot gồm 20 file C# + csproj gốc, HEAD 7236e72..., working tree dirty; giữ byte có SHA-256. Chưa sửa source gốc.
- Máy Linh có SDK 10.0.301, runtime 10.0.9 Windows x64. Build wrapper đầu thành công, 10 cảnh báo CS8500; không phải kiểm chứng runtime Vst.

## Giới hạn hiện hành

Core chưa có implementation; CLI chỉ help/version/command lỗi. Chưa có tracker, peer, file transfer, application unit tests, test hai máy, resume, adapter Vst hoặc benchmark. Phân công/backlog là đề xuất, chưa gán đóng góp.

Tuần 1 chuyển sang học nền chung và phần riêng Linh chọn: giải thích cơ chế, tự tính/vẽ input nhỏ, rồi nâng lên hàm/lab khi đủ nền. Echo/codec/test là đầu việc có thể chọn, không còn mặc định bắt cả nhóm xong trong tuần 1. Các proposedArea ở team.json là gợi ý cũ, không phải phân công. V02 pool lớn chưa xếp lịch; V01 cần API/size hợp lệ theo audit.

Điều chỉnh tiếp theo: Linh cho biết tuần 1 chỉ còn T7–CN, sinh viên còn học môn khác, và yêu cầu đề xuất việc cụ thể cho cả 5. Đã bổ sung đề xuất trong Kế hoạch: khoảng 150 phút/người cả cuối tuần (60 T7 +90 CN, đã tính 30 phút nhóm), có phương án 90 phút. Nền chung chỉ Bài 00 mục 1–3/Bài 01 mục 1–4; bài riêng lần này đề xuất Linh trace TCP, Minh chia tệp21 byte, Cường TTL, Quân rarity, Tú expected/actual CLI. Đây là đề xuất theo yêu cầu hiện tại, chưa ai xác nhận nhận/làm xong và không là phân công cố định về sau. Không ép code module cuối tuần này. Học liệu mới vẫn local; cần chia sẻ đúng bản cho nhóm.

QA học liệu: 17 Markdown trong hoc-tap có code fence/details/table nhất quán; 28 trích dẫn trang rõ tên file nằm trong số trang chỉ mục; các phép tính/ví dụ kiểm tra đạt. Đã trích 4 block C# từ học liệu, compile/run trong tmp và xác nhận output; tổng 72 kiểm tra của helper học liệu đạt. [Evidence ví dụ](../../results/setup/learning-examples-validation.json). [Checker toàn repo có đối chiếu nguồn](../../results/setup/learning-docs-validation.json) cũng đạt; 21 snapshot file và 14 PDF đúng hash. Đây không phải unit/integration test P2P hoặc benchmark. Đợt này chỉ sửa học liệu/hướng dẫn/bộ nhớ, không viết implementation vào src và không commit/push.

Kiểm tra bootstrap trước đợt sắp xếp docs: Release build thành công với 10 cảnh báo CS8500; help/version exit 0, command không hỗ trợ exit 2. Báo cáo ở `results/setup/project-validation.json`; evidence xem [Kiểm tra bộ khởi tạo](../../docs/trien-khai/Kiem-tra-bo-khoi-tao.md).

Sau sắp xếp docs ngày 23/10/2026: checker với `--verify-sources` đạt, không có link hỏng; 21 snapshot file và 14 PDF đúng hash. Báo cáo [docs-reorganization.json](../../results/setup/docs-reorganization.json). Đã cập nhật đường dẫn trong checker, công cụ trích slide và dòng hướng dẫn CLI. Build riêng CLI Debug exit 0, không warning/error; help exit 0 và trỏ về README. Log [build](../../results/setup/docs-reorganization-build.log), [help](../../results/setup/docs-reorganization-help.log). Lần build này không bao gồm wrapper Vst; không phải test P2P hoặc benchmark.

Linh đã yêu cầu commit/push bộ khởi tạo ngày 23/10/2026. Đã xuất bản 108 file bằng commit đầu `4c072d6` (`first commit`) lên [kimsakota/BTL.MMT](https://github.com/kimsakota/BTL.MMT), repository public, default branch main. Origin là `https://github.com/kimsakota/BTL.MMT.git`, local main theo dõi origin/main; đã đối chiếu SHA local và remote sau push.

[GitHub Actions lần đầu](https://github.com/kimsakota/BTL.MMT/actions/runs/39954201976) đã completed/success trên `4c072d6`: checker, build Release toàn solution, CLI help/version đều đạt. [Evidence CI](../../results/setup/github-first-ci.json); kiểm tra local trước xuất bản ở `results/setup/github-publication-validation.json` và `github-publication-build.log`. Đây vẫn là kiểm tra bootstrap, chưa phải test mạng/P2P. Chưa có xác nhận invitation/board hoặc bài nộp. Không nhập kết quả xv6, thông tin nhóm hoặc quy tắc báo cáo tuần của môn khác.

Rà cấu trúc ngày 23/10/2026: khung Core/Cli/VstReference phù hợp để bắt đầu; Core vẫn chưa có module implementation, tests chưa có test project. Đã sửa `.gitignore` từ `VstHelper/` thành `/VstHelper/` vì quy tắc cũ vô tình bỏ qua cả snapshot tham chiếu. Đã kiểm tra 23 file snapshot/metadata hiện được Git nhìn thấy và 5 đường dẫn build/cache/repo gốc giả định vẫn bị ignore; [evidence](../../results/setup/git-ignore-verification.json). Source snapshot không đổi.
