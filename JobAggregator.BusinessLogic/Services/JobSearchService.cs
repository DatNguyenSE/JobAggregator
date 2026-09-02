using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon.SQS;
using Amazon.SQS.Model;
using JobAggregator.BusinessLogic.DTOs;
using JobAggregator.BusinessLogic.Mappings;
using JobAggregator.BusinessLogic.Services.Interfaces;
using JobAggregator.DataAccess.Entities;
using JobAggregator.DataAccess.Repositories;

namespace JobAggregator.BusinessLogic.Services
{
    public class JobSearchService : IJobSearchService
    {
        private readonly IJobRepository _jobRepository;
        private readonly IAmazonSQS _sqsClient;
        private readonly string _scrapingQueueUrl;

        public JobSearchService(IJobRepository jobRepository, IAmazonSQS sqsClient)
        {
            _jobRepository = jobRepository;
            _sqsClient = sqsClient;
            
            // Lấy URL của SQS từ biến môi trường (Đã cấu hình trong template.yaml)
            _scrapingQueueUrl = Environment.GetEnvironmentVariable("SCRAPING_QUEUE_URL") ?? "";
        }

        public async Task<(bool IsFound, IEnumerable<JobPostDto> Jobs)> SearchJobsAsync(SearchCriteriaDto criteria)
        {
            var keywordEntity = await _jobRepository.GetSearchKeywordAsync(criteria.Keyword, criteria.Location, criteria.JobType);

            // Kiểm tra DB xem có tồn tại Job nào gắn với các tiêu chí này chưa
            if (keywordEntity != null && keywordEntity.TotalJobsFound > 0)
            {
                // KỊCH BẢN 1 (ZERO LATENCY): CÓ SẴN DỮ LIỆU
                var jobs = await _jobRepository.GetJobsByCriteriaAsync(criteria.Keyword, criteria.Location, criteria.JobType);
                var jobDtos = jobs.Select(j => j.ToDto()).ToList();

                // Bắn message ẩn cào ngầm để cập nhật dữ liệu mới (Incremental Scraping)
                await TriggerScrapingAsync(criteria, keywordEntity.LastScrapedAt);

                return (true, jobDtos);
            }
            else
            {
                // KỊCH BẢN 2: CHƯA CÓ DỮ LIỆU TRONG DB
                if (keywordEntity == null)
                {
                    keywordEntity = new SearchKeyword
                    {
                        Id = Guid.NewGuid(),
                        Keyword = criteria.Keyword,
                        Location = criteria.Location ?? string.Empty,
                        JobType = criteria.JobType ?? string.Empty,
                        LastScrapedAt = DateTime.MinValue,
                        TotalJobsFound = 0
                    };
                    await _jobRepository.AddSearchKeywordAsync(keywordEntity);
                    await _jobRepository.SaveChangesAsync();
                }

                // Gửi thông điệp kích hoạt hệ thống cào dữ liệu toàn bộ từ đầu
                await TriggerScrapingAsync(criteria, DateTime.MinValue);

                return (false, Enumerable.Empty<JobPostDto>());
            }
        }

        private async Task TriggerScrapingAsync(SearchCriteriaDto criteria, DateTime since)
        {
            if (string.IsNullOrEmpty(_scrapingQueueUrl)) return;

            var payload = new
            {
                Criteria = criteria,
                Since = since
            };

            var message = new SendMessageRequest
            {
                QueueUrl = _scrapingQueueUrl,
                MessageBody = JsonSerializer.Serialize(payload)
            };

            await _sqsClient.SendMessageAsync(message);
        }
    }
}
