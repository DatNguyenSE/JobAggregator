# Ý tưởng: Vector Search, Hybrid Search và RAG cho JobAggregatorMVP

Ngày ghi nhận: 11/09/2026.

**Trạng thái: Ghi nhận để nghiên cứu và triển khai sau. Chưa triển khai các ý tưởng vector/RAG này.**

## 1. Mục tiêu và ba ý tưởng ban đầu

1. Khi scraper trả về dữ liệu, xem xét tạo vector cho từng job để phục vụ tìm kiếm theo ngữ nghĩa.
2. Khi user tìm việc, tìm trong DB bằng vector kết hợp các điều kiện hiện tại để tìm được những job gần nghĩa, không chỉ trùng từ khóa.
3. Sau này tích hợp trợ lý AI/agent sử dụng dữ liệu được truy xuất để trả lời, so sánh và tư vấn việc làm theo hướng RAG.

Định hướng thảo luận: bổ sung khả năng tìm kiếm và truy xuất vào kiến trúc hiện có, chưa cần thay toàn bộ .NET, SQS, Lambda hoặc PostgreSQL/Supabase.

## 2. Những nguyên tắc cần giữ

- Vector là biểu diễn bổ sung của nội dung, không thay thế dữ liệu job gốc.
- Vẫn tách dữ liệu cào thành từng job trước khi lưu. Không gộp toàn bộ một đợt cào nhiều job thành một vector duy nhất.
- Bảng công việc hiện tại là `JobPosts`; địa điểm làm việc nằm ở `JobLocations`. Tên `JobLists` trong ý tưởng ban đầu được hiểu là bảng lưu từng job.
- `Platform + ExternalId` tiếp tục là căn cứ chống trùng chính. Hai job gần vector chưa chắc là cùng một tin tuyển dụng.
- Tỉnh/quận và các điều kiện bắt buộc phải được kiểm tra chính xác. Vector gần nhau không được dùng để bỏ qua điều kiện địa phương.
- Độ mới của lần cào và ngày đăng tuyển là hai thông tin riêng: xét hạn 24 giờ bằng thời điểm cào, sắp xếp mới nhất bằng ngày đăng.
- Các đề xuất dưới đây là hướng nghiên cứu, chưa phải quyết định chốt model, schema hay thư viện.

## 3. Ý tưởng 1: Lưu thêm embedding cho từng job

### Phương án đề xuất

Giữ nguyên:

- `JobPosts`: tiêu đề, công ty, mô tả, lương, URL nguồn, ngày đăng, thời điểm cào và các thông tin khác.
- `JobLocations`: từng nơi làm việc của job.

Cân nhắc thêm bảng `JobEmbeddings` liên kết về `JobPosts` bằng `JobPostId`. Những trường cần xem xét:

- `JobPostId`: job gốc.
- `Embedding`: vector do embedding model tạo ra.
- `EmbeddingModel`: model/phiên bản dùng để tạo vector.
- `ContentHash`: nhận biết nội dung đầu vào đã thay đổi hay chưa.
- `EmbeddedAt`: thời điểm tạo embedding.
- Trạng thái xử lý embedding nếu cần chạy nền, retry và theo dõi lỗi.

Bắt đầu với một vector cho mỗi job. Nội dung để tạo vector có thể gồm tiêu đề, mô tả công việc, kỹ năng và yêu cầu kinh nghiệm. Cần thử nghiệm cách ghép nội dung trước khi chốt.

Nếu mô tả quá dài hoặc cần truy xuất từng đoạn cho RAG, có thể chuyển sang nhiều đoạn nội dung (chunks), mỗi đoạn vẫn liên kết về cùng `JobPostId`.

### Luồng xử lý dự kiến

Scraper trả dữ liệu → tách/chuẩn hóa từng job → lưu hoặc cập nhật DB → tạo embedding ở nền → lưu vector liên kết với job.

Job vẫn có thể được tìm bằng từ khóa khi embedding chưa sẵn sàng. Lỗi tạo embedding không nên làm mất khả năng lưu và hiển thị job.

Chỉ tạo lại embedding khi nội dung liên quan thay đổi. Nếu cào lại cùng nội dung, cập nhật thời điểm cào mà không nhất thiết gọi embedding model lại. Khi đổi model, cần có kế hoạch tạo lại vector và tránh so sánh vector thuộc các không gian embedding không tương thích.

## 4. Ý tưởng 2: Tìm kiếm kết hợp từ khóa và vector

### Vì sao có ích

Tìm kiếm ngữ nghĩa có thể tìm được những cách diễn đạt khác nhau như “nhân viên bán hàng”, “sales associate” hoặc “tư vấn bán hàng”. Đây là khả năng cần đánh giá bằng dữ liệu thực tế; điểm tương đồng không phải xác suất job phù hợp.

### Hybrid search đề xuất

Kết hợp ba phần:

1. Lọc có cấu trúc: tỉnh, quận/huyện, độ mới trong 24 giờ, nguồn và các điều kiện bắt buộc khác.
2. Tìm từ khóa: giữ khả năng tìm chính xác chức danh, công nghệ hoặc tên công ty.
3. Tìm vector: bổ sung kết quả gần nghĩa dù khác cách viết.

Ví dụ user tìm “việc tư vấn khách hàng, chưa có kinh nghiệm, tại Quận 1”. Vector hỗ trợ tìm các mô tả gần nghĩa; điều kiện Quận 1 vẫn phải được thỏa mãn bằng dữ liệu địa phương.

Không lấy đủ số lượng bằng mọi giá: kết quả gần nhất trong vector vẫn có thể không liên quan. Cần đánh giá cách kết hợp điểm và tiêu chuẩn đủ liên quan trên tập câu hỏi thực tế, không chốt một ngưỡng tùy ý.

### Giữ nguyên flow DB trước, cào bù sau

1. Nhận từ khóa, tỉnh, thành phố/quận/huyện và số lượng yêu cầu.
2. Tìm các job hợp lệ trong DB bằng hybrid search, có áp dụng điều kiện địa phương và độ mới.
3. Trả ngay các job đã hợp lệ lên màn hình, tối đa số lượng yêu cầu.
4. Nếu đủ số lượng: không cào lại.
5. Nếu thiếu: giữ nguyên các job đã hiện, cào bù ở nền, chống trùng, lưu/cập nhật DB rồi bổ sung kết quả.
6. Nếu DB không có job hợp lệ: cào ngay và hiển thị trạng thái đang tìm.
7. Nếu nguồn không đủ: trả số thực tế tìm được và báo rõ trạng thái hoàn tất.

Ví dụ: cần 10 job nhưng DB chỉ có 6 job còn mới và phù hợp → hiện ngay 6 job, cào bù 4 job còn thiếu.

Độ liên quan và độ mới cần được phân biệt. Đề xuất ban đầu là chọn job đủ liên quan rồi xếp ngày đăng mới nhất theo yêu cầu hiện tại. Sau này có thể thêm lựa chọn “Phù hợp nhất”.

## 5. Ý tưởng 3: Trợ lý AI/agent theo hướng RAG

### Cơ chế dự kiến

Câu hỏi user → xác định nhu cầu và bộ lọc → tìm job liên quan → lấy nội dung gốc từ DB → đưa nội dung cùng câu hỏi vào prompt → LLM trả lời kèm nguồn.

Vector dùng để truy xuất. LLM thông thường nhận nội dung job đã được tìm ra, không nhận trực tiếp dãy số vector để đọc và trả lời.

Ví dụ câu hỏi:

> Trong các việc ở Quận 1, việc nào phù hợp với người mới và có lương cứng?

Hệ thống tìm job đáp ứng điều kiện, sau đó AI so sánh thông tin được ghi trong các job. Nếu nguồn không nói rõ lương cứng, cần trả lời chưa có thông tin, không tự suy diễn thành dữ kiện.

### Phân biệt RAG và agent

- Có thể bắt đầu bằng RAG với một flow truy xuất rồi trả lời cố định, chưa cần agent.
- Agent hữu ích khi cần tự chọn công cụ như `SearchJobs`, `GetJobDetails`, `CompareJobs` hoặc đề nghị cào thêm.
- Những yêu cầu đếm/thống kê chính xác phải dùng truy vấn DB phù hợp. Danh sách top-k từ vector không đại diện cho toàn bộ dữ liệu.
- Nội dung được cào là dữ liệu tham khảo, không phải chỉ dẫn mà agent được tự động làm theo.

## 6. Công nghệ nên nghiên cứu trước

### PostgreSQL/Supabase và pgvector

Ưu tiên thử `pgvector` ngay trên PostgreSQL/Supabase hiện có, thay vì đưa thêm hệ quản trị vector riêng từ đầu. `pgvector-dotnet` hỗ trợ tích hợp .NET qua Npgsql và Entity Framework Core.

### Embedding model

Không dùng dịch vụ cào ngoài cho ViecLam24h không đồng nghĩa với việc không cần model tạo vector. Có hai hướng cần cân nhắc:

- Gọi embedding model qua dịch vụ, ví dụ Amazon Bedrock.
- Chạy model tự host; cần đánh giá tài nguyên, vận hành và độ trễ trong kiến trúc serverless.

Chưa chọn model cụ thể. Cần kiểm thử tiếng Việt, tên chức danh, từ viết tắt và các câu hỏi pha tiếng Anh. Theo tài liệu AWS được tham khảo trong buổi thảo luận, Titan Text Embeddings V2 có hỗ trợ tiếng Việt nhưng được tối ưu cho tiếng Anh; hỗ trợ ngôn ngữ không có nghĩa chất lượng đã phù hợp với project.

## 7. Lộ trình đề xuất cho lần triển khai sau

1. Chọn một tập job nhỏ và bộ câu hỏi tìm việc tiếng Việt để đánh giá.
2. Thử tạo embedding, lưu liên kết với job gốc.
3. So sánh tìm từ khóa, tìm vector và hybrid search: kết quả đúng nhu cầu, lọc đúng địa phương, độ trễ và chi phí.
4. Tích hợp phương án phù hợp vào flow trả DB trước, cào bù sau; giữ đường tìm từ khóa khi embedding chưa có hoặc dịch vụ lỗi.
5. Khi chất lượng truy xuất đủ tốt, thêm RAG để giải thích và so sánh job kèm URL nguồn.
6. Sau đó mới mở rộng thành agent có khả năng chọn công cụ.

Các điểm cần quyết định khi bắt đầu: model/kích thước vector, nội dung embedding, schema và index, chiến lược xếp hạng, tiêu chuẩn liên quan, cập nhật vector, xử lý job hết hạn và chi phí vận hành.

## 8. Tài liệu tham khảo

- [Supabase — Hybrid search](https://supabase.com/docs/guides/ai/hybrid-search)
- [pgvector-dotnet — Tích hợp .NET, Npgsql và EF Core](https://github.com/pgvector/pgvector-dotnet)
- [AWS — Amazon Titan Text Embeddings](https://docs.aws.amazon.com/bedrock/latest/userguide/titan-embedding-models.html)
- [Microsoft — Tổng quan Retrieval-Augmented Generation](https://learn.microsoft.com/en-us/azure/storage/files/artificial-intelligence/retrieval-augmented-generation/overview)

Tài liệu chỉ lưu ý tưởng và định hướng đã thảo luận. Cần kiểm tra lại tài liệu, khả năng cung cấp dịch vụ và kết quả thử nghiệm trước khi triển khai.
