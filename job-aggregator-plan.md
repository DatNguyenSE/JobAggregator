# Kế Hoạch Triển Khai Kiến Trúc: Serverless Job Aggregator MVP

## 1. Tổng Quan Dự Án (Project Overview)
- **Mô tả:** Hệ thống tổng hợp tin tuyển dụng (Job Aggregator) lấy dữ liệu từ các mạng xã hội (Facebook, LinkedIn).
- **Mục tiêu hạ tầng:** Xây dựng kiến trúc Serverless Event-driven trên AWS để xử lý bất đồng bộ, tối ưu hóa độ trễ (latency) cho người dùng thông qua cơ chế Cào bù (Incremental Scraping).
- **Ngôn ngữ & Framework:** 
  - Frontend: Angular.
  - Backend: C# .NET 10.
  - Database: PostgreSQL.
  - Infrastructure as Code (IaC): AWS SAM (Serverless Application Model).

## 2. Các Nguyên Tắc Lập Trình Bắt Buộc (Strict Coding Guidelines for AI Agent)
Khi tạo source code cho dự án này, AI Agent **BẮT BUỘC** tuân thủ các quy tắc cốt lõi sau:
1. **Kiến trúc N-Tier (3 Lớp):** Tổ chức dự án theo mô hình N-Tier tinh gọn cho Serverless:
   - *Presentation/Entry Layer:* Các hàm AWS Lambda tiếp nhận HTTP Request (từ API Gateway) hoặc Message (từ SQS).
   - *Business Logic Layer (BLL):* Tầng Service xử lý nghiệp vụ, thuật toán Smart Router, giao tiếp với bên thứ 3.
   - *Data Access Layer (DAL):* Tầng chứa Entity, DbContext và các class tương tác DB.
2. **Mapping Dữ liệu Thủ công (Manual Mapping):** TUYỆT ĐỐI KHÔNG sử dụng các thư viện ánh xạ tự động (như AutoMapper hay Mapster). Phải tạo các class `ExtensionMethods` để tự viết logic mapping thủ công giữa Entity và DTO.
3. **Decoupling (Tách biệt Service và DB):** Tầng Business Logic (Service Layer) tuyệt đối KHÔNG được inject hoặc gọi trực tiếp `DbContext`. Mọi giao tiếp với cơ sở dữ liệu phải được thực hiện thông qua các Interface (Repository Pattern) được triển khai ở tầng DAL.

## 3. Danh Sách AWS Services & Vai Trò (AWS Cloud Map)
- **AWS Amplify Hosting:** Host và quản lý CI/CD cho Frontend Angular.
- **Amazon API Gateway:** Cổng tiếp nhận HTTP Request từ ứng dụng.
- **AWS Lambda:** Thực thi logic tính toán, chia làm 3 nhóm hàm chính: User API, Data Fetcher, và AI Processor.
- **Amazon SQS (Simple Queue Service):** Trạm trung chuyển thông điệp bất đồng bộ (giúp Throttling, kiểm soát tốc độ gọi API).
- **Amazon S3:** Kho lưu trữ dữ liệu thô (HTML text đã dọn dẹp, Hình ảnh nén) trước khi đưa cho AI (Claim-Check pattern).
- **Amazon Bedrock (Claude 3 Haiku):** Xử lý AI Đa phương thức (Multimodal). Đọc hiểu văn bản hoặc Hình ảnh banner để bóc tách thông tin tuyển dụng ra định dạng JSON.
- **Amazon RDS (PostgreSQL) + RDS Proxy:** Lưu trữ dữ liệu cấu trúc. Dùng RDS Proxy để quản lý Connection Pooling từ các hàm Lambda.

## 4. Thiết Kế Cơ Sở Dữ Liệu (Database Entities)
Các Entity bắt buộc phải có để phục vụ luồng Cào bù và lưu trữ chi tiết công việc:

```csharp
public class SearchKeyword
{
    public Guid Id { get; set; }
    public string Keyword { get; set; } = null!;
    public DateTime LastScrapedAt { get; set; } // Phục vụ luồng Incremental Scraping
    public int TotalJobsFound { get; set; } 
    public ICollection<JobPost> JobPosts { get; set; } = new List<JobPost>();
}

public class JobPost
{
    public Guid Id { get; set; }
    public Guid SearchKeywordId { get; set; }
    public SearchKeyword SearchKeyword { get; set; } = null!;

    // Thông tin cốt lõi
    public string Title { get; set; } = null!;
    public string? SalaryInfo { get; set; } 
    public string? Requirements { get; set; }
    public string? Location { get; set; }

    // Thông tin thời gian làm việc
    public string? WorkingHours { get; set; } 
    public string? WorkingDays { get; set; }  

    // Đãi ngộ & Phúc lợi
    public string? HolidayBonuses { get; set; } 
    public string? OtherBenefits { get; set; }  

    // Meta data (Chống trùng lặp)
    public string SourceUrl { get; set; } = null!; 
    public string Platform { get; set; } = null!; 
    public string ExternalId { get; set; } = null!; 
    
    public DateTime PostedDate { get; set; } 
    public DateTime CreatedAt { get; set; } 
}
## 5. Chi Tiết Các Luồng Hoạt Động (System Workflows)

### Luồng 1: Giao Tiếp Người Dùng (User-Driven Search & Incremental Trigger)
**Mục tiêu:** Trả về kết quả ngay lập tức (Zero Latency) và làm mới dữ liệu thông minh.
1. Khách hàng tìm từ khóa (VD: "Angular") trên UI. API Gateway kích hoạt **Lambda (User API)**.
2. Code C# kiểm tra trong bảng `KeywordCache` hoặc `Jobs` của PostgreSQL qua Repository. 
3. **Trường hợp có sẵn dữ liệu:** 
   - Kiểm tra cột `LastScrapedAt`. Code C# map thủ công Entity sang DTO và trả về danh sách job ngay lập tức (HTTP 200 OK).
   - Bắn 1 message vào **SQS (Scraping Queue)** mang tham số: `{ "Keyword": "Angular", "Since": "LastScrapedAt" }` để cập nhật ngầm dữ liệu bị thiếu.
4. **Trường hợp chưa có dữ liệu:** Trả về HTTP 202 Accepted, bắn từ khóa vào SQS. Frontend Angular thực hiện Polling đợi dữ liệu mới.

### Luồng 2: Thu Thập Dữ Liệu Ngầm (Incremental Scraping Flow)
**Mục tiêu:** Vượt tường lửa bằng Scraping Fish, làm sạch dữ liệu.
1. **SQS (Scraping Queue)** kích hoạt **Lambda (Data Fetcher)**.
2. Lambda gọi API của **Scraping Fish** truyền vào "Keyword" và "Since" để lấy về HTML thô của các bài tuyển dụng mới nhất.
3. Logic xử lý C# (`HtmlAgilityPack`):
   - Loại bỏ các thẻ `<script>`, `<style>`, header, footer... chỉ giữ lại văn bản trơn (Text).
   - Lấy Text đã lọc và URL của thẻ `<img>` (nếu có).
4. Lưu gói dữ liệu (Text + Image URL) lên **Amazon S3**.
5. Bắn thông điệp chứa S3 URI vào **SQS (AI Processing Queue)**.

### Luồng 3: Bóc Tách Bằng Trí Tuệ Nhân Tạo (AI Multimodal Processing Flow)
**Mục tiêu:** Bóc tách dữ liệu chuẩn xác, sử dụng thuật toán **Smart Router** ở tầng BLL để phân loại bài đăng nhằm tối ưu chi phí Token.
1. **SQS (AI Processing Queue)** kích hoạt **Lambda (AI Processor)** (Thiết lập Reserved Concurrency để tránh lỗi Rate Limit TPM).
2. Tải payload từ S3. Code C# tại tầng Service thực hiện chấm điểm Text (độ dài, từ khóa như "lương", "yêu cầu") và rẽ nhánh xử lý (Smart Router):
   - *Kịch bản 1 (Đầy đủ Text):* Nếu Text > 150 ký tự và chứa từ khóa trọng tâm -> Bỏ qua ảnh. Chỉ gửi chuỗi Text cho Bedrock. (Tối ưu token).
   - *Kịch bản 2 & 3 (Chỉ có Ảnh hoặc Thiếu Text):* Nếu Text quá ngắn (< 50 ký tự) hoặc thiếu thông tin + Có Image URL -> Dùng thư viện `ImageSharp` tải và nén ảnh (max 1024x1024px). Convert ảnh sang Base64 và gửi CẢ Ảnh + Text cho Bedrock (Sử dụng tính năng Vision Multimodal).
   - *Kịch bản 4 (Dữ liệu rác):* Text rỗng + Không có ảnh -> Bỏ qua.
3. Amazon Bedrock phân tích và trả về cấu trúc JSON đồng nhất (Title, Salary, Requirements, Location).
4. Code C# Deserialize JSON. Dùng Extension Methods map thủ công JSON object sang Entity.
5. Tầng Service gọi Interface Repository để Insert/Update vào PostgreSQL và cập nhật `LastScrapedAt`.