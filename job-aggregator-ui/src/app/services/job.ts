import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, timeout } from 'rxjs';

export interface JobPost {
  id: string;
  title: string;
  salaryInfo?: string;
  requirements?: string;
  location?: string;
  employerName?: string;
  contactPhone?: string;
  locations?: Array<{ province?: string, district?: string, exactAddress?: string }>;
  workingHours?: string;
  workingDays?: string;
  holidayBonuses?: string;
  otherBenefits?: string;
  sourceUrl: string;
  platform: string;
  externalId: string;
  postedDate: string;
  
  jobInfo?: string;
  gender?: string;
  vacancies?: number;
  workingFormat?: string;
  ageRequirement?: string;
  experience?: string;
}

export interface SearchCriteriaDto {
  Keyword: string;
  Location: string;
  JobType: string;
  Sources: string[];
  MaxJobs: number;
  Province?: string;
  District?: string;
}

export interface JobSearchResponse {
  requestId: string | null;
  jobs: JobPost[];
  requestedCount: number;
  missingCount: number;
  status: 'pending' | 'complete' | 'failed';
  isScraping: boolean;
  message?: string;
}

@Injectable({
  providedIn: 'root'
})
export class JobService {
  // Backend C# mặc định được cố định ở cổng 5233 (theo file launchSettings.json)
  private apiUrl = 'https://ly17ckpa2h.execute-api.ap-southeast-1.amazonaws.com/Prod/api/jobs'; 

  constructor(private http: HttpClient) { }

  searchJobs(criteria: SearchCriteriaDto): Observable<JobSearchResponse> {
    // POST chạy DB-first: response có thể đủ ngay hoặc gồm job hiện có + requestId đang cào bù.
    return this.http.post<JobSearchResponse>(`${this.apiUrl}/search`, criteria).pipe(timeout(35000));
  }

  getSearchStatus(requestId: string): Observable<JobSearchResponse> {
    // GET chỉ đọc trạng thái/kết quả đã lưu, không khởi động thêm một lần cào.
    return this.http.get<JobSearchResponse>(`${this.apiUrl}/search/${encodeURIComponent(requestId)}`).pipe(timeout(15000));
  }
}
