# Kế hoạch cập nhật dữ liệu tuyển dụng và luồng cào

Trạng thái: lập trước khi sửa code. Chưa áp dụng migration hoặc deploy.

## Mục tiêu
- Facebook hiển thị các nhóm đã lưu; chọn nhóm chỉ đọc DB.
- Vieclam24h giữ form tìm kiếm; tìm kiếm chỉ đọc DB.
- Chỉ cào khi người dùng bấm thao tác cào riêng; tiếp tục dùng Apify cho Facebook, không dùng phiên đăng nhập Facebook cá nhân.
- Một bài Facebook là một tin tuyển dụng, có thể chứa nhiều vị trí.

## Database
- [ ] Thêm FacebookGroups: khóa nội bộ, ID Facebook nếu biết, tên, URL chuẩn hóa, thời điểm tạo và lần cào thành công.
- [ ] Thêm khóa nhóm nullable vào JobPosts; giữ Platform phân biệt nguồn.
- [ ] Thêm ContentHash và danh sách vị trí; tận dụng LastScrapedAt làm thời điểm nhìn thấy gần nhất.
- [ ] Lịch sử ScrapeRuns chung: nguồn, nhóm, tiêu chí, trạng thái, thời gian, số bài lấy/mới/cập nhật/bỏ qua/lỗi, tham chiếu dữ liệu gốc và run Apify.
- [ ] Index nhóm/ngày đăng/ID; bảo toàn unique Platform + ExternalId.
- [ ] Migration tăng thêm, không xóa dữ liệu cũ; chỉ gắn nhóm cho bài có bằng chứng từ URL. Bài không xác định giữ null.
- [ ] Không suy ra nhóm đã được cào đầy đủ từ ngày chạy hoặc số bài giới hạn.

## API và logic
- [ ] Tách truy vấn dữ liệu khỏi yêu cầu cào; đọc DB không tạo SQS.
- [ ] Truy vấn nhóm và tin theo nhóm, phân trang phía server; bỏ cửa sổ 24 giờ khi xem dữ liệu lưu.
- [ ] API chủ động cào trả ID để theo dõi, dùng hàng đợi hiện có.
- [ ] Tra ID + hash theo batch bằng index; không tải toàn bộ JobPosts.
- [ ] Bài không đổi bỏ qua AI; bài mới/thay đổi mới xử lý; không xóa tin khi nguồn không trả lại.
- [ ] Giữ nội dung gốc trong S3; phân biệt bài không tuyển dụng và bài lỗi.
- [ ] Có trạng thái processing/partial/failed/completed; chỉ cập nhật mốc thành công khi xử lý hoàn tất.
- [ ] Chống bấm lặp và lượt cào đồng thời cùng nhóm bằng cơ chế phía backend/database.
- [ ] Hạn mức đề xuất: user thường 20 bài/lượt, 3 lượt/ngày, 1 lượt đang chạy; cooldown nhóm 30 phút; admin có trần cấu hình.
- [ ] Kiểm tra cơ chế xác thực sẵn có trước khi gắn hạn mức. Không tin userId/role tự gửi từ trình duyệt; nếu chưa có xác thực phải ghi rõ và khóa cào public theo cấu hình, không giả vờ đã có quota theo tài khoản.
- [ ] Không tự động cào bù khi kết quả thiếu; không hứa lần sau là 20 bài tiếp theo.

## Giao diện
- [ ] Facebook: danh sách nhóm, số tin, lần cập nhật; bấm nhóm đọc DB; tìm/lọc trên DB.
- [ ] Facebook: thao tác Cào thêm và Cào nhóm mới riêng; URL, sắp xếp, ngày, số lượng; không gửi keyword/năm sang Apify.
- [ ] Vieclam24h: form tìm DB trước; nút Cào thêm theo tiêu chí này riêng.
- [ ] Phân trang; hiển thị tin tuyển dụng thay vì số vị trí; hiển thị nhiều vị trí trong một tin.
- [ ] Hiển thị tiến độ, kết quả một phần, lỗi rõ ràng; không báo đã lấy toàn bộ nhóm.

## Kiểm chứng và bàn giao
- [ ] Build backend/frontend; cập nhật test hành vi đọc không cào, nhóm không lẫn nhau, batch chống trùng, phân quyền/hạn mức, trạng thái và phân trang.
- [ ] Kiểm tra migration và dữ liệu cũ; không chạy migration trên DB production trong lúc phát triển.
- [ ] Ghi rõ phần đã chạy thực tế và phần chỉ test giả lập; không phát sinh lượt Apify trả phí để thử UI.
- [ ] Hướng dẫn thứ tự migration, deploy backend, deploy frontend; cập nhật tài liệu này theo kết quả thực tế.

## Điều chỉnh theo yêu cầu 17/09/2026
- Bản test chức năng không cần đăng nhập/mã admin; Cognito triển khai sau.
- Bỏ quota tài khoản/ngày và cooldown; giữ giới hạn số bài mỗi lượt, chống gửi lặp và ngăn chạy đồng thời để tránh cào trùng.
- Đã viết migration và UI, build qua; chưa áp dụng migration/deploy. Kiểm thử tích hợp và kiểm tra dữ liệu cũ còn đang làm.

## Kết quả triển khai bản test
- [x] Tạo FacebookGroups, khóa ngoại nhóm, ContentHash, Roles và index trong JobPosts.
- [x] Mở rộng JobSearchRequests thành lịch sử cào chung (giữ tên bảng để không mất lịch sử).
- [x] Migration tăng thêm; gắn bài cũ từ URL nhóm/bài có định dạng hợp lệ. Không đoán nhóm của bài thiếu bằng chứng.
- [x] API xem nhóm/tìm kiếm chỉ đọc DB, phân trang 20 tin; không giới hạn độ mới 24 giờ.
- [x] API cào riêng, chống gửi lặp bằng Idempotency-Key, lưu raw cho cả hai nguồn vào S3.
- [x] Bài Facebook cùng ID/hash bỏ qua AI; khác nội dung xử lý lại; kết quả từng bài và trạng thái partial.
- [x] API xử lý lại lấy raw đã lưu và chỉ gửi AI queue. Khóa PostgreSQL chống hai worker xử lý cùng lượt.
- [x] Facebook hiện nhóm trước, tìm trong nhóm trên DB; Vieclam24h nhập tiêu chí rồi tìm DB; nút cào riêng.
- [x] Bản test không yêu cầu đăng nhập. Quota tài khoản/Cognito chưa triển khai theo yêu cầu mới.
- [x] 10 test UI đạt; 45 kiểm tra Facebook giả lập đạt; 10 kiểm tra catalog trên PostgreSQL test riêng đạt.
- [ ] Chưa chạy end-to-end trên AWS/Apify/Bedrock thật; chưa deploy hoặc migrate DB đang dùng.

### Giới hạn cần biết
- Tên nhóm mới tạm dùng slug URL; chưa tự lấy tên hiển thị thật từ Facebook.
- URL slug và URL ID số chưa được tự hợp nhất nếu không có bằng chứng ánh xạ; nhập lại đúng URL hoặc chọn nhóm đã có.
- Bài không tuyển dụng được ghi kết quả trong lượt cào để retry không gọi lại; cache loại bài này giữa các lượt độc lập chưa có.
- Hash ảnh dựa trên đường dẫn ảnh, bỏ query CDN; chưa phát hiện trường hợp nội dung ảnh đổi nhưng đường dẫn giữ nguyên.
- Phân trang dùng offset có giới hạn phía server; khi dữ liệu rất lớn sẽ chuyển sang cursor.
- Retry xử lý có thể phát sinh phí AI; không tự cào lại. Nếu worker chết trước khi lưu raw, cần kiểm tra ApifyRunId trước khi cào mới.
- Không cam kết nguồn đã trả hết bài. Chạm giới hạn số bài sẽ có thông báo.

## Triển khai sau khi test
1. Sao lưu DB và kiểm tra script `.artifacts/catalog-migration.sql` trên bản sao dữ liệu.
2. Áp dụng migration AddFacebookCatalogAndScrapeRuns bằng quy trình migration hiện có.
3. Deploy API, DataFetcher và AIProcessor cùng phiên bản. API cần quyền gửi cả ScrapingQueue và AIProcessingQueue cho thao tác retry.
4. Giữ cấu hình DB_CONNECTION_STRING, SCRAPING_QUEUE_URL, AI_PROCESSING_QUEUE_URL, S3 và Bedrock hiện có. SCRAPE_MAX_POSTS mặc định 20.
5. Deploy frontend. Frontend hiện vẫn trỏ API Gateway cũ; sửa backend local không thay đổi Lambda đang chạy.
6. Kiểm chứng lượt cào thật và retry trước khi coi luồng AWS hoàn tất. Cấu hình Cognito/quyền truy cập thực hiện ở giai đoạn sau.
