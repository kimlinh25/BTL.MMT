# Lõi P2P

Core triển khai giao thức v1, TCP frame, phiên seed, manifest và lưu block, tracker/discovery và receiver tuần tự có thể đổi nguồn. CLI đọc tham số rồi gọi Core. Xem [giao thức](../../docs/thiet-ke/Giao-thuc-v1.md) và [kiến trúc](../../docs/thiet-ke/Kien-truc-du-an.md).

Tracker trả địa chỉ peer theo fileId. Receiver tải trực tiếp bằng TCP. `FileSeedSource` dùng `FileStorage` để phục vụ block từ piece đã xác minh. Handshake lấy snapshot Verified dưới khóa trạng thái ngắn. Đọc block kiểm tra lại Verified dưới cùng semaphore dùng để điều phối truy cập tệp. Caller giữ storage sống đến khi tác vụ kết thúc. Kiểm thử và cách chạy nằm ở [README dự án](../../README.md#chạy-p2p-nhiều-nguồn-và-thu-số-liệu).

Core cơ sở dùng BCL .NET, không phụ thuộc snapshot VstHelper. Adapter Vst sẽ được viết sau khi framing và ownership đã có kiểm thử.
