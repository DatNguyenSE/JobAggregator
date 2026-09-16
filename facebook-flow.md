# Facebook Groups qua Apify

Flow triển khai theo 4 dạng bài trong `job-aggregator-plan.md`.

URL mặc định: https://www.facebook.com/groups/sinhvientimvieclamthem/
Người dùng đổi nhóm qua ô URL trên UI; FacebookGroupUrl đi theo request tới scraper. URL được chuẩn hóa bỏ query và fragment. Trình duyệt nhớ 10 URL gần đây. Đã kiểm tra qua Apify ngày 12/09/2026: lấy được 3 bài, 1 bài có ảnh. Khả năng truy cập nhóm có thể thay đổi theo Facebook.

Actor: `apify/facebook-groups-scraper`. Input dùng `startUrls`, `searchGroupKeyword` (từ khóa user nhập), `resultsLimit`, `viewOption: CHRONOLOGICAL`. Token đọc từ `APIFY_API_KEY`, gửi bằng Authorization Bearer. Không cần cookie Facebook.

## Chạy từ ứng dụng

Chọn **Facebook Groups** ở bộ lọc nguồn, hoặc POST `/api/jobs/search`:

```json
{
  "keyword": "nhân viên",
  "location": "",
  "jobType": "",
  "sources": ["facebook"],
  "maxJobs": 5
}
```

`DataFetcherService` chọn FacebookGroupScraper → Apify chạy và trả dataset → mapping thủ công ID/link/ngày/text/ảnh → lưu JSON vào S3 → SQS chỉ mang key → AIProcessor đọc S3 → FacebookJobExtractor → repository lưu JobPost.

- Text trên 150 ký tự và có từ khóa thông tin tuyển dụng: chỉ gửi text.
- Text ngắn hoặc thiếu thông tin, có ảnh: tải các ảnh bài đăng, nén JPEG tối đa 1024×1024, gửi text + ảnh cho Bedrock.
- Chỉ ảnh: gửi ảnh cho Bedrock.
- Rỗng và không ảnh: bỏ qua.
- Bài không tuyển dụng/không phù hợp tiêu chí: model trả `[]`.
- Platform, ExternalId, SourceUrl, PostedDate luôn lấy từ bài gốc, không lấy từ model. Loại ID trùng và ExcludedExternalIds trước khi gọi AI.
- Một bài được lưu thành tối đa một JobPost. Nếu AI trả nhiều vị trí, code gộp tiêu đề và giữ thông tin theo từng vị trí (lương, yêu cầu, liên hệ, giờ làm...), gộp địa chỉ không trùng; không báo lỗi chỉ vì có nhiều vị trí.
- Lỗi actor, dataset, tải ảnh, S3 hoặc JSON từ AI được báo thành lỗi request, không biến thành thành công rỗng.

Mỗi lần đọc tối đa `min(maxJobs, 20)` bài từ kết quả tìm kiếm trong nhóm để giới hạn chi phí. Đây là số bài khảo sát, không đảm bảo đủ số job phù hợp; chưa tự phân trang ngược toàn bộ lịch sử hoặc triển khai watermark Since. Keyword được trim và gửi nguyên cụm từ vào searchGroupKeyword để actor tìm trong nhóm trước khi cào; AI kiểm tra lại mức phù hợp trên text và ảnh. Từ khóa rỗng bị chặn trước khi gọi Apify. Không tự rút từ khóa xuống một vài ký tự, đổi năm hoặc cào feed khi kết quả rỗng. Theo tài liệu actor, tìm nguyên từ khi không đăng nhập có thể trả rất ít hoặc không có kết quả; không đảm bảo có đủ số job yêu cầu.

## Cấu hình và kiểm tra

Cần `APIFY_API_KEY`, `RAW_DATA_BUCKET`, `AI_PROCESSING_QUEUE_URL` và IAM cho S3/SQS/Bedrock như template. Template đã tăng timeout AI lên 900 giây và visibility queue tương ứng; cần deploy template + Lambda để dùng flow mới trên AWS. Vieclam24h không cần Apify key.

```powershell
dotnet build JobAggregator.slnx
dotnet run --project tests/FacebookFlow.Checks
# Chỉ khi muốn gọi dịch vụ thật; sử dụng APIFY_API_KEY đã đặt trong môi trường:
dotnet run --project tests/FacebookFlow.Checks -- --live
```

Offline checks dùng HTTP/Bedrock giả lập, có kiểm tra xử lý ảnh thật và payload Vision. `--live` chạy Apify 3 bài, chỉ in số lượng, không ghi DB. Chưa kiểm tra end-to-end S3 → SQS → Bedrock → PostgreSQL trên môi trường triển khai.

Tài liệu tham chiếu:
- https://apify.com/apify/facebook-groups-scraper/input-schema
- https://apify.com/apify/facebook-groups-scraper
- https://docs.apify.com/api/v2/actor-run-get
- https://docs.aws.amazon.com/bedrock/latest/userguide/model-parameters-anthropic-claude-messages-request-response.html


## Lỗi sau deploy ngày 15/09/2026

Request 29529c30-5864-456f-b6d6-9f1d92dd5b90 đã nhận đúng URL
vieclamvinhomesgrandpark và cào 5 bài vào S3. AIProcessor (bản deploy 06:50:58 UTC)
dừng tại ParseListings với lỗi "Expected at most one non-null listing per Facebook post."
Bản sửa local gộp nhiều vị trí về một bài và có test hồi quy; chưa xác nhận lại
Bedrock/DB thực tế sau bản sửa này. Lỗi Facebook xảy ra trước bước xử lý nguồn
Vieclam24h nên lượt tìm nhiều nguồn cũng bị báo thất bại.
## Kiểm chứng lượt game ngày 16/09/2026
Run DbFbg5bETC2Cf2RHg: input đúng URL nhóm, searchGroupKeyword=game,
resultsLimit=10, viewOption=CHRONOLOGICAL.
Log actor ghi BLOCKED trong handleGroupSearchByKeyword tới maximum retries.
Dataset chỉ có error=no_items, errorDescription="Empty or private data for provided input".
SUCCEEDED ở mức run không chứng minh đã lấy được bài; "2 requests" không phải 2 bài.
Bản sửa nối FacebookViewOption, FacebookSearchYear, FacebookOnlyPostsNewerThan từ
UI qua DTO tới actor, sửa MARKETPLACE thành CHRONOLOGICAL_LISTINGS, và giữ lỗi
no_items thành lỗi nguồn rõ ràng kèm run ID trong log. Không chuyển sang cào feed.
Các sửa này không khắc phục được việc Facebook chặn actor. Chưa có xác nhận
scraper lấy được kết quả tìm kiếm thật trong phiên đăng nhập như ảnh người dùng.
