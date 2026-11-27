# Thiết kế thực nghiệm tái lập

[Cấu hình ma trận](../../configs/experiments.json) ghi 3 kịch bản và giá trị khảo sát. [Runner CLI](../../tools/run_experiments.py) tạo các tiến trình .NET thật trên TCP loopback và lưu CSV/config/log. Dùng bước chạy nhanh trong [README](../../README.md#chạy-p2p-nhiều-nguồn-và-thu-số-liệu) để kiểm cấu hình, sau đó hiệu chỉnh kích thước tệp, timeout và mức tải trước khi đo chính thức. Khi dùng runner, thời gian mỗi receiver lấy từ lúc vào CLI đến lúc công bố tệp, còn thời gian chung và lịch rời lấy từ đồng hồ monotonic của runner, tính từ lúc bắt đầu khởi chạy receiver đầu tiên.

## Câu hỏi và biến

Q1: Với cùng nguồn ban đầu và tập receiver, P2P phân phối tệp khác client–server thế nào? Q2: Sequential và rarest-first thay đổi kết quả khi peer upload khác nhau hoặc rời? Q3: Buffer strategy ảnh hưởng CPU/RAM/goodput thế nào khi giữ phần mạng giống nhau?

| Kịch bản | Thay đổi có chủ đích | Giữ cố định trong từng cặp so sánh |
|---|---|---|
| S1 ổn định | Tổng peer 3/5/8, gồm 1 seed | Tệp, giới hạn upload từng vai trò, block/piece, queue/inflight, máy |
| S2 upload không đều | Mẫu giới hạn payload upload của receiver | N và policy của cặp, source, các tham số còn lại |
| S3 peer rời | Một receiver được chỉ định rời ở mốc 10 s, seed giữ online | Cùng lịch rời trong cặp; dữ liệu còn nguồn |

Giới hạn upload do ứng dụng áp dụng khác băng thông link. Config gọi rõ `applicationUploadLimitMbps`; chỉ gọi là đường truyền bị giới hạn khi thực sự có bộ shaping link được ghi lại. Wi-Fi có chia sẻ airtime, không suy ra mỗi peer có một link độc lập từ số peer.

Chạy nhóm so sánh mode CS/P2P trước, scheduler sau, buffer sau cùng; không làm factorial khổng lồ cả ba khi chưa ổn định. N trong config gồm seed, nên số receiver=N−1. P2P bổ sung upload từ receiver, phải công bố tài nguyên đó. Có thể thêm cặp cùng tổng upload budget để tách hiệu quả phân phối khỏi việc thêm tài nguyên.

## Quy trình một lượt đo

1. Chốt revision/config/input SHA, kiểm tra clock, CPU/RAM/đĩa, NIC/link, SDK và các socket options. Đo đường tham chiếu iperf3 riêng, không đồng thời với workload.
2. Chuẩn bị tệp đủ dài, đề xuất ban đầu 256 MiB; chạy calibration để tránh kết luận từ tệp hoàn tất quá nhanh. Chọn policy cache/flush/hash giống nhau.
3. Chuẩn bị tracker và peer, chờ READY; bộ điều phối phát GO và ghi monotonic start. Deadline chung và lịch rời tính từ GO.
4. Thu JSONL từng process, byte counters, hash, exit code và pcap đại diện. Dừng có kiểm soát khi hết deadline, đánh dấu timeout/failed thay vì xóa run.
5. Lưu metadata/run ID và kết quả nguyên bản trước khi tổng hợp. Lặp tối thiểu 5 lần cho mỗi cấu hình được chọn, dùng seed khác nhau nếu có random tie-break. Đảo thứ tự cặp A/B để giảm ảnh hưởng thời gian/cache.

## Chỉ tiêu và denominator

- Goodput mỗi receiver: 8×fileSizeBytes/timeSeconds/1e6, chỉ khi tệp hoàn tất đã xác minh. Thời gian tính từ GO đến completion event theo quy ước đo, bao gồm ghi/hash; báo riêng thời gian truyền nếu phân tách được.
- Goodput phân phối: tổng byte tệp hữu ích hoàn tất chia makespan chung. Bộ điều phối dùng một đồng hồ; không trừ timestamp wall-clock giữa hai máy chưa đồng bộ. Thời gian nhận completion event tại điều phối gồm độ trễ thông báo và phải ghi giới hạn này.
- Thời gian hoàn tất từng receiver và cả lượt; số hoàn tất/thất bại/timeout/receiver rời theo lịch phải riêng. Với S3 báo cả tập receiver khởi đầu và tập còn được yêu cầu hoàn tất; không tự loại các lần fail khỏi denominator.
- Tổng payload upload nguồn ban đầu và receiver; byte control/header; block gửi lặp ở ứng dụng; không đồng nhất với TCP retransmission. Ghi điểm capture và tránh cộng đôi cùng packet nhìn ở hai NIC.
- CPU time, peak working set, managed allocations/GC và live/high-water unmanaged per process. Pool giữ RAM dự trữ có thể làm working set tăng dù giảm allocation; diễn giải theo retention policy.
- Thời gian phát hiện ngắt và thời gian phân công lại, hash pass, retry count, queue depth/inflight theo thời gian.

Mbit/s là 10^6 bit/s; KiB/MiB là 2^10/2^20 byte. Mỗi metric có đơn vị và định nghĩa trong schema kết quả.

## Phân tích và biểu đồ

Giữ mọi run; báo n, trung bình, độ lệch chuẩn và min–max. Mẫu nhỏ không chứng minh chênh lệch nhỏ có ý nghĩa; không chọn một run đẹp nhất. Đồ thị: completion time theo N với mode CS/P2P; source upload theo N; scheduler dưới S2/S3; goodput/RAM theo buffer strategy. Dùng cùng trục và ghi error bar/config.

Nếu P2P chậm hơn, kiểm tra uplink chia sẻ, hash/đĩa, quá nhiều kết nối, stop-and-wait ở ứng dụng hoặc các piece cuối hiếm. Nếu Vst không cải thiện tốc độ, có thể mạng đã là nút thắt; không sửa dữ liệu để khớp giả thuyết.

## Capture và giới hạn

Chọn NIC/loopback đúng, capture filter theo port tracker/peer, gắn connection ID vào log. Wireshark dùng TCP stream/throughput/RTT để đối chiếu; cwnd không phải trường hiện sẵn trong mọi pcap. Offload có thể tạo checksum cảnh báo hoặc segment lớn ở capture host; không gọi đó là lỗi ứng dụng khi chưa kiểm chứng. [Wireshark TCP graphs](https://www.wireshark.org/docs/wsug_html_chunked/ChStatTCPStreamGraphs.html).

Impairment delay/loss ở Linux router/VM là mở rộng; ghi vị trí, hai chiều, seed và RTT/loss thực tế. Không đổi thiết lập mạng host chính tự động trong bộ khởi tạo. Chưa có script netem hay luật firewall được áp dụng.
