# Lõi P2P

Core triển khai giao thức v1, TCP frame, phiên seed, manifest và lưu block, tracker/discovery và receiver tuần tự một nguồn. CLI đọc tham số rồi gọi Core. Xem [giao thức](../../docs/thiet-ke/Giao-thuc-v1.md) và [kiến trúc](../../docs/thiet-ke/Kien-truc-du-an.md).

Tracker trả địa chỉ seed theo fileId. Receiver tải trực tiếp từ seed. `FileSeedSource` dùng `FileStorage` để phục vụ block từ piece đã xác minh. Caller giữ storage sống đến khi tác vụ kết thúc. Kiểm thử và cách chạy nằm ở [README dự án](../../README.md#chạy-lại-w03-trên-một-máy).

Core cơ sở dùng BCL .NET, không phụ thuộc snapshot VstHelper. Adapter Vst sẽ được viết sau khi framing và ownership đã có kiểm thử.
