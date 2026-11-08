# Tài liệu dự án P2P ET4230

Điểm vào chung cho tài liệu Đề 14 bằng C# của Linh, Minh, Cường, Quân, Tú. Đọc theo việc đang làm; trạng thái chương trình nằm ở [README dự án](../README.md) và [bộ nhớ hiện tại](../.github/agent/CURRENT_STATE.md).

## Cần tìm gì

| Việc cần làm | Tài liệu chính |
|---|---|
| Hiểu đề tài, yêu cầu, phạm vi và đầu ra | [Tổng quan](Tong-quan.md) |
| Xem mốc triển khai, phân công và task | [Kế hoạch](Ke-hoach.md) |
| Học từ nền mạng/C#, ví dụ và bài tập | [Mục lục học liệu](hoc-tap/README.md), [nền C#](hoc-tap/CSharp-nen-tang.md) |
| Chọn kiến thức riêng cho phần được giao | [Lộ trình theo chuyên môn](hoc-tap/Lo-trinh-theo-chuyen-mon.md); Linh tự chọn người và bài đọc từng tuần |
| Tra slide môn học và các chỗ cần đọc kỹ | [Bản đồ slide](hoc-tap/Kien-thuc-tu-slide.md), [ghi chú chuẩn hóa](hoc-tap/Ghi-chu-chuan-hoa-slide.md) |
| Thiết kế module, state và ownership | [Kiến trúc](thiet-ke/Kien-truc-du-an.md) |
| Viết codec hoặc ghép các peer | [Giao thức v1](thiet-ke/Giao-thuc-v1.md) |
| Tích hợp source VstHelper | [Audit và kế hoạch dùng lại](thiet-ke/Tai-su-dung-VstHelper.md) |
| Cài công cụ, build và chạy | [Môi trường](trien-khai/Thiet-lap-moi-truong.md) |
| Xem những gì đã kiểm tra thực tế | [Kiểm tra bộ khởi tạo](trien-khai/Kiem-tra-bo-khoi-tao.md) |
| Viết test hoặc đo hiệu năng | [Ma trận kiểm thử](kiem-chung/Ke-hoach-kiem-thu.md), [thiết kế thực nghiệm](kiem-chung/Thiet-ke-thuc-nghiem.md) |
| Chuẩn bị bản nộp và bảo vệ | [Báo cáo và demo](Bao-cao-va-demo.md) |
| Truy nguồn slide, RFC, source và lựa chọn đề | [Tài liệu tham khảo](tham-khao/README.md) |

## Cấu trúc

```text
docs/
├── README.md                  # Tìm tài liệu theo việc cần làm
├── Tong-quan.md               # Yêu cầu và phạm vi hiện hành
├── Ke-hoach.md                # Một nơi cho phân công và backlog
├── Bao-cao-va-demo.md         # Sản phẩm cuối kỳ và checklist nộp
├── hoc-tap/                   # Lộ trình, 12 bài học, bản đồ slide
├── thiet-ke/                  # Kiến trúc, giao thức, VstHelper
├── trien-khai/                # Môi trường và kết quả kiểm tra nền
├── kiem-chung/                # Kiểm thử và thực nghiệm
└── tham-khao/                 # Nguồn, hash và lý do chọn đề tài
```

Mỗi nội dung có một nơi chính: yêu cầu ở Tổng quan, nhiệm vụ ở Kế hoạch, hợp đồng kỹ thuật ở Thiết kế. Bài học giải thích và dẫn tới hợp đồng; chúng không định nghĩa một phiên bản giao thức riêng. Hướng dẫn Git nằm trong [CONTRIBUTING](../CONTRIBUTING.md), dữ liệu thực nghiệm nằm ở `results/`, bộ nhớ assistant nằm ở `.github/agent/`.

## Bắt đầu

Mới mở dự án: Tổng quan → Môi trường → Bài 00–01. Khi viết code: đọc hợp đồng liên quan → chọn task trong Kế hoạch → làm bài/thực hành cần thiết → chạy kiểm tra tương ứng. Khi có kết quả mới, cập nhật tài liệu sở hữu nội dung đó; chỉ thêm file khi có nội dung riêng cần tra cứu.
