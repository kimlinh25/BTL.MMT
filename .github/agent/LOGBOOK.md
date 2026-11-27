# Nhật ký dự án

## 27/11/2026 — Hoàn thiện code P2P và runner

Theo yêu cầu Linh, Codex thêm đường P2P và runner vào worktree tách riêng, giữ lệnh W03. Dùng một yêu cầu BLOCK đang chờ mỗi lượt tải, giới hạn phiên upload, lấy snapshot Verified nhất quán, chỉ quảng bá sau khi có piece đã xác minh và chờ phiên dừng trước khi đóng storage. Windows/.NET 10.0.301/Python 3.13.13: `dotnet build ET4230.P2P.sln -c Release`, xUnit 92/92, `tools/test_w03_cli.ps1`, `python tools/run_experiments.py --prove-sharing` và `--smoke`. Proof B cấp dữ liệu khi đang tải, B và C nhận đúng hash cho tệp 2.097.155 byte. Smoke 3/4 peer, 3 kịch bản × 2 mode, 26 hoàn tất và 4 rời theo lịch. Checker xác minh 21 tệp Vst snapshot. Công sức hỗ trợ được ghi là Codex, chưa xác nhận đóng góp cá nhân hoặc nộp bài. Không stage, commit hoặc push.

## L001 Ngày 23/10/2026 Khởi tạo theo slide và mô hình Hệ điều hành

Nguồn trực tiếp: Linh yêu cầu nghiên cứu thư mục Slide, tạo dự án A–Z theo cách tổ chức môn Hệ điều hành, nhóm 5 người Linh/Minh/Cường/Quân/Tú. Linh xác nhận dùng C# được phép. Các nhận định trước về C++ hoặc phải xác nhận lại C# đã được cập nhật.

Đã lập chỉ mục 14 PDF/697 trang bằng pypdf + pdftotext, giữ SHA-256 và trang PDF 1-based; đọc cấu trúc học phần và đào sâu phần Intro/IP/TCP/flow/socket. Đã render/xem 6 trang sơ đồ/công thức; ghi chú window/RTT, slow start, EWMA và đóng TCP có đối chiếu RFC.

Đã tham khảo cấu trúc docs/lessons, hợp đồng module, GitHub Desktop, evidence và memory Light ở repo Hệ điều hành. Không sao chép implementation xv6, danh tính nhóm hoặc deadline/quy định báo cáo sang ET4230.

Đã tạo solution .NET 10 (Core, Cli bootstrap, VstReference), snapshot 21 source/project files giữ bytes và provenance. Build đầu thành công trên SDK 10.0.301; 10 CS8500 được giữ và audit, chưa thử allocator runtime. Chưa có chức năng P2P/test mạng/benchmark. Phân công là đề xuất, chưa phải đóng góp hoặc nhận task của sinh viên.

Đã soạn 12 bài học, thiết kế wire protocol v1, invariant/ownership, backlog, test matrix, 3 scenario/2 tham số, khung report/demo và các công cụ kiểm tra project.

## L002 Ngày 23/10/2026 Kiểm tra bootstrap

Release build exit 0, 10 warning duy nhất CS8500 từ snapshot; CLI help/version exit 0, command chưa hỗ trợ exit 2. Evidence được ghi vào results/setup/ và docs/Kiem-tra-bo-khoi-tao.md. Audit đầu có 59 Markdown/130 link local, JSON/config/vector, 21 snapshot hash và 14 PDF hash đúng; audit cuối được chạy sau các cập nhật tài liệu. Đây không là test codec/mạng hoặc benchmark.

Đã khởi tạo Git local main, chưa stage/commit/push hoặc tạo remote. Đã chuẩn bị workflow bootstrap Windows, chưa chạy GitHub CI. Bước tiếp theo là nhóm tự mở/build/học Lesson 00–01, review contract và nhận task; không ghi ai đã đọc hoặc nhận việc.

## L003 Ngày 23/10/2026 Tổ chức docs theo nhu cầu P2P

Linh phản hồi thư mục onboarding theo người không cần thiết và yêu cầu nghiên cứu, làm lại docs hợp lý, không sao chép mọi thứ từ dự án Hệ điều hành. Đã đổi quy tắc làm việc và bộ nhớ theo điều chỉnh này; vẫn giữ cách giải thích cơ chế, ownership và review khi hướng dẫn học.

Đã rà 42 file, gộp còn 30: thêm mục lục theo việc cần làm; gom vào học tập, thiết kế, triển khai, kiểm chứng và tham khảo. Lộ trình gộp với mục lục 12 bài học; phân công/quy trình gộp vào kế hoạch/backlog; dàn ý báo cáo và kịch bản demo gộp thành một tài liệu; hai bản khảo sát ban đầu được rút gọn vào cơ sở chọn đề và thiết kế hiện hành. Đã bỏ 6 file onboarding và mẫu tiến độ chưa có dữ liệu thành viên. Hướng dẫn Git gộp về CONTRIBUTING. Các hợp đồng giao thức, kiểm thử, thực nghiệm và nội dung bài học kỹ thuật được giữ.

Trước khi chuyển, đã lưu bản sao và hash ở `tmp/docs-reorganization/20261023T121530Z`; MIGRATION.json ghi đường dẫn cũ/mới. Cập nhật liên kết của docs, README phụ thuộc, AGENTS và bộ nhớ; cập nhật checker, nơi ghi chỉ mục slide và dòng hướng dẫn help. Tài liệu/log lịch sử giữ dấu vết lần chạy cũ.

Kiểm tra `tools/check_project.py --verify-sources --report results/setup/docs-reorganization.json` đạt: link local, JSON/config/vector, 21 snapshot hash và 14 PDF hash đúng. Build riêng CLI Debug và chạy help với `--no-build` đều exit 0; build có 0 warning/error. Log ở `results/setup/docs-reorganization-build.log` và `docs-reorganization-help.log`. Không chạy wrapper Vst hoặc thử chức năng mạng trong đợt này. Không stage/commit/push.

## L004 Ngày 23/10/2026 Rà cấu trúc dự án

Linh hỏi cấu trúc đã hợp lý chưa. Đã đọc project references, solution, cây source, kiến trúc, config, tests, workflow và ignore. Khung 3 project giữ hướng Cli → Core và VstReference độc lập, phù hợp giai đoạn hiện tại. Core chưa có implementation; test project cần đi cùng codec đầu tiên theo N03. Adapter Vst là phần sau, chưa được tích hợp hoặc kiểm chứng runtime. Không tạo thêm project/class rỗng trong đợt review.

Phát hiện bằng `git check-ignore -v` rằng dòng `VstHelper/` trong `.gitignore` cũng bỏ qua `references/VstHelper/`. Đã giới hạn thành `/VstHelper/`. Kiểm tra lại bằng Git với danh sách đường dẫn phân tách NUL: 21 file source/project cùng README và SNAPSHOT.json không bị ignore; 5 đường dẫn mẫu cho root checkout, bin/obj/tmp/raw vẫn bị ignore. Evidence ở `results/setup/git-ignore-verification.json`. Không sửa snapshot, stage, commit hoặc push.

## L005 Ngày 23/10/2026 Xuất bản GitHub theo yêu cầu Linh

Linh giao tạo/đưa dự án lên `https://github.com/kimsakota/BTL.MMT.git` và cung cấp các lệnh commit/push. Khi kiểm tra, origin đã đúng URL, remote tồn tại nhưng chưa có ref, local main chưa có commit. Không khởi tạo lại repo, không nối thêm dòng echo vào README; cập nhật tên BTL.MMT và hướng dẫn clone, giữ nội dung dự án.

Đã kiểm tra local: checker có đối chiếu nguồn đạt, build Release toàn solution exit 0 (incremental, 0 warning/error ở lần này; 10 CS8500 ở build sạch trước vẫn thuộc audit Vst). Kiểm tra tag của ba action trong workflow có tồn tại. Loại dòng trống thừa cuối 52 file văn bản của dự án, giữ snapshot nguyên byte; kiểm tra staged blobs của đủ 21 file theo SHA-256 và không có bin/obj/tmp. Source/tài liệu/cấu hình/evidence được stage theo yêu cầu, không chứa repository VstHelper gốc của môn khác.

Đã tạo root commit `4c072d6b557e5e6844a7bdd0be86b8c80ae328d7`, message `first commit`, 108 file; push main thành công và đặt upstream origin/main. SHA local/remote trùng. GitHub API xác nhận repository public, default branch main. Không force push, không mời thành viên hoặc tạo board.

Workflow `Project bootstrap validation`, run `39954201976`, completed/success: checker/config/vector/snapshot hash, build, help và version trên Windows đều đạt. Evidence ở `results/setup/github-first-ci.json`. Đã cập nhật bộ nhớ và hướng dẫn kiểm tra để phản ánh việc publish; kết quả này không chứng minh application P2P đã được viết hoặc benchmark đã chạy.

## L006 Ngày 23/10/2026 Lập lịch 8 tuần và việc tuần 1

Linh xác nhận BTL chỉ có 8 tuần làm việc, tuần hiện tại là tuần 1, và hỏi tuần này làm gì. Đã cập nhật trong Kế hoạch hiện có, không tạo thư mục/hồ sơ theo tuần hoặc theo người. Mốc đề xuất: tuần 1 TCP/codec/test, tuần 2 truyền tệp một nguồn, tuần 3 discovery/hai máy, tuần 4 P2P nhiều nguồn, tuần 5 lỗi/tài nguyên/resume theo phạm vi, tuần 6 scheduler/Vst nhỏ/calibration, tuần 7 thực nghiệm, tuần 8 hoàn thiện/bảo vệ. Báo cáo viết dần, không dồn toàn bộ vào tuần 8.

Tuần 1 có việc chung clone/build, nhận việc và review hợp đồng; Linh làm N01/N02, Tú N03/test/CI, Minh piece/block arithmetic, Cường registry/TTL với đồng hồ giả, Quân sequential/bitmap và state độc lập. Đây là kế hoạch được đề xuất, không xác nhận đóng góp hoặc hoàn thành. Các task D01/S01 đầy đủ vẫn cần transport/storage/discovery ở các tuần sau. Đề xuất không xếp V02 pool lớn vào 8 tuần; V01 cần giới hạn API/size đúng audit, phần đã đăng ký bắt buộc không tự cắt. Không suy ra ngày nộp cụ thể từ số tuần; chưa biết quỹ giờ từng người. Chỉ sửa tài liệu/bộ nhớ, không viết implementation hoặc push.

## L007 Ngày 23/10/2026 Viết lại học liệu và trả quyền phân công tuần cho Linh

Linh phản hồi học liệu hời hợt, người học chưa có nền, cần cả kiến thức riêng; Linh muốn tự giao hàng tuần ai làm gì và đọc gì. Đã thay cách tiếp cận checklist bằng 12 bài giảng giải thích cơ chế, ví dụ tính/trace, thực hành tăng mức và gợi ý tự đối chiếu. Thêm nền C# về input/output, kiểu/ref/alias, collection, Memory, Dispose, Task/cancel; thêm 5 đường chuyên môn A–E với prerequisite, mục đọc, câu hỏi, bài nhỏ, phần sâu và giao diện cần hiểu chéo. Không tạo hồ sơ theo tên hoặc theo tuần.

Đã đối chiếu bản trích slide với RFC TCP, BEP và tài liệu Microsoft/Wireshark chính thức; trích slide dùng tên file/trang PDF. Ví dụ bao gồm header/request byte, UTF-8 length, offset/tệp cuối lẻ, TTL clock, rarity/credit, ownership qua queue/I/O, late response, resume và thống kê/denominator. Các số học tập được gắn nhãn, không tạo benchmark giả. Ghi rõ generation local không tự giải quyết ID reuse trên wire.

Đã sửa Kế hoạch: bỏ gán tên vào phần việc tuần 1 và owner mặc định backlog; giữ khung 8 tuần như tham khảo để Linh chọn. AGENTS, README, CONTRIBUTING và memory hiện hành ghi rõ Linh tự phân công và chọn bài đọc/mức thực hành. Phân công trong L006 là đề xuất cũ đã được điều chỉnh, không dùng làm công việc đã nhận.

Trước sửa đã sao lưu 22 file trong `tmp/learning-rewrite/20261023T125344Z`, có hash. QA riêng học liệu kiểm 17 Markdown, 28 citation page ranges và ví dụ số; 4 block C# trích nguyên từ bài được compile/run ở tmp với output đúng. 72 kiểm tra đạt, evidence `results/setup/learning-examples-validation.json`; đây không phải test ứng dụng P2P. Checker toàn repo với `--verify-sources` đạt, evidence `results/setup/learning-docs-validation.json`, 21 snapshot file và 14 PDF đúng hash. Không sửa snapshot hoặc implementation, không stage/commit/push, không xác nhận ai đã học xong.

## L008 Ngày 23/10/2026 Thu hẹp việc tuần 1 cho hai ngày cuối tuần

Linh nói chỉ còn T7/CN, sinh viên còn môn khác, và yêu cầu nghiên cứu đề xuất việc cho 5 người. Đã thay mục tuần 1 trong Kế hoạch hiện có bằng khối lượng150 phút/người cả cuối tuần, bao gồm setup15, nền chung45, bài riêng45, nhóm30 và sửa15; bản rút gọn90 phút. Số giờ là đề xuất, chưa được từng thành viên xác nhận.

Theo yêu cầu lần này đã nêu tên để Linh chọn giao: Linh trace partial receive16 byte, Minh piece/block của tệp21, Cường heartbeat/TTL, Quân sequential/rarest từ bảng5 piece, Tú expected/actual ba CLI command hiện có. Bài đọc được giới hạn tới mục; output bảng/sơ đồ/ít dòng, không tạo báo cáo/hồ sơ theo người. Có key để tự đối chiếu sau khi làm, không gán kết quả bài tập thành hoàn thành của sinh viên. Không yêu cầu code codec, tracker hay Vst ở tuần 1; quỹ giờ môn khác được giữ bằng timebox. Phân công chỉ là đề xuất cuối tuần này; Linh vẫn quyết định nhận/giao việc các tuần sau. Chỉ cập nhật tài liệu/bộ nhớ, không commit/push hoặc gửi tin cho thành viên.

Đã kiểm lại tổng150/90 phút và đáp án partial receive, F21/P8/B4, TTL, rarity bằng tính toán độc lập. Checker repo đạt với251 link local và21 snapshot hash; diff whitespace đạt, không có thay đổi src/tests/configs/references. Đây là QA kế hoạch/bài tập, không phải kết quả sinh viên đã học hoặc chạy ứng dụng.

## L009 Ngày 23/10/2026 Kích hoạt lại repo trong Visual Studio

Linh gửi ảnh Git Changes báo chưa dùng Git và hỏi vì sao chưa kết nối GitHub. Đối chiếu local/remote: origin là `https://github.com/kimsakota/BTL.MMT.git`, main theo dõi origin/main; `git ls-remote` và HEAD cùng SHA `a570e602e04564a06f55be13ae0851220236f6e0`. Repo local và kết nối GitHub hoạt động; chưa xác định lý do Visual Studio không tự kích hoạt repo khi mở folder.

Dùng computer-use chọn đúng cửa sổ BTL Mạng máy tính, Git > Local Repositories > BTL (đường dẫn Mạng máy tính). Sau thao tác, Git Changes - BTL hiển thị main và 40 thay đổi local; accessibility xác nhận đúng root, 0 outgoing / 0 incoming. Không tạo/clone lại repo, không sửa cấu hình Git, không stage/commit/push. Số 0/0 chỉ nói các commit đã đồng bộ, không có nghĩa học liệu chưa commit đã lên GitHub.

Khi bắt đầu đợt này, `agent/` đã bị di chuyển bên ngoài sang `.github/agent/` trong working tree. Đọc/cập nhật memory ở vị trí thực tế, không khôi phục hay sửa hàng loạt tham chiếu cũ vì đây không phải nguyên nhân Git Changes chưa chọn repo. Chưa xác nhận người dùng muốn chuẩn hóa đường dẫn mới.
